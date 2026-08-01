using NUnit.Framework;
using PatternSpace;
using UnityEngine;

/// <summary>
/// 임팩트 정렬 계산 검증. <b>플레이어와 적이 이 계산을 공유하기 때문에</b>,
/// 여기가 맞으면 "적 칼이 지나가는 순간 = 플레이어 칼이 지나가는 순간"이 구조적으로 성립한다.
/// </summary>
public class ClipAlignmentTests
{
    private static ClipAlignment Make(float startOffset, float duration, float impactTime, float speed)
    {
        var alignment = new ClipAlignment();
        alignment.EditorAssign(null, startOffset, duration, impactTime, speed);
        return alignment;
    }

    // ── 임팩트 구간 폴백 ────────────────────────────────────────────────────

    [Test]
    public void 임팩트_미지정이면_트림_끝으로_폴백한다()
    {
        var alignment = Make(startOffset: 0.5f, duration: 2f, impactTime: 0f, speed: 1f);

        // 오서링되지 않은 기존 데이터도 그대로 동작해야 한다.
        Assert.AreEqual(2f, alignment.ResolvedImpactSpan, 1e-4f);
    }

    [Test]
    public void 임팩트가_트림_범위_밖이면_트림_끝으로_폴백한다()
    {
        var tooLate = Make(startOffset: 0.5f, duration: 2f, impactTime: 5f, speed: 1f);
        var tooEarly = Make(startOffset: 0.5f, duration: 2f, impactTime: 0.2f, speed: 1f);

        Assert.AreEqual(2f, tooLate.ResolvedImpactSpan, 1e-4f);
        Assert.AreEqual(2f, tooEarly.ResolvedImpactSpan, 1e-4f);
    }

    [Test]
    public void 임팩트_구간은_트림_시작부터_잰다()
    {
        var alignment = Make(startOffset: 0.5f, duration: 2f, impactTime: 1.5f, speed: 1f);

        Assert.AreEqual(1f, alignment.ResolvedImpactSpan, 1e-4f); // 1.5 − 0.5
    }

    // ── 시작 시각 역산 ──────────────────────────────────────────────────────

    [Test]
    public void 시작시각은_임팩트에서_역산된다()
    {
        var alignment = Make(startOffset: 0f, duration: 2f, impactTime: 1f, speed: 1f);

        // 임팩트까지 1초 걸리니 10초에 닿으려면 9초에 시작해야 한다.
        Assert.AreEqual(9f, alignment.ResolveScheduleStart(impactAlignTime: 10f, earliest: 0f), 1e-4f);
    }

    [Test]
    public void 시작시각은_earliest보다_이를_수_없다()
    {
        var alignment = Make(startOffset: 0f, duration: 2f, impactTime: 1f, speed: 1f);

        // 첫 노드보다 먼저 휘두를 수는 없다.
        Assert.AreEqual(9.5f, alignment.ResolveScheduleStart(impactAlignTime: 10f, earliest: 9.5f), 1e-4f);
    }

    [Test]
    public void 배속이_높으면_더_늦게_시작한다()
    {
        var alignment = Make(startOffset: 0f, duration: 2f, impactTime: 1f, speed: 2f);

        Assert.AreEqual(9.5f, alignment.ResolveScheduleStart(impactAlignTime: 10f, earliest: 0f), 1e-4f);
    }

    // ── 배속 클램프 ─────────────────────────────────────────────────────────

    [Test]
    public void 여유가_충분하면_클램프되지_않는다()
    {
        var alignment = Make(startOffset: 0f, duration: 2f, impactTime: 1f, speed: 1f);

        float speed = alignment.ResolvePlaySpeed(now: 0f, impactAlignTime: 1f, maxSpeed: 2.5f, out bool clamped);

        Assert.IsFalse(clamped);
        Assert.AreEqual(1f, speed, 1e-4f);
    }

    [Test]
    public void 시간이_모자라면_클램프되고_알린다()
    {
        var alignment = Make(startOffset: 0f, duration: 2f, impactTime: 1f, speed: 1f);

        // 1초짜리 임팩트 구간을 0.1초 만에 — 10배가 필요하지만 상한은 2.5다.
        float speed = alignment.ResolvePlaySpeed(now: 0f, impactAlignTime: 0.1f, maxSpeed: 2.5f, out bool clamped);

        // 클램프 = 임팩트가 제시각에 못 온다는 뜻. 조용히 어긋나면 안 되므로 반드시 true여야 한다.
        Assert.IsTrue(clamped);
        Assert.AreEqual(2.5f, speed, 1e-4f);
    }

    [Test]
    public void 배속은_지정_하한_밑으로_내려가지_않는다()
    {
        var alignment = Make(startOffset: 0f, duration: 2f, impactTime: 1f, speed: 1.5f);

        // 여유가 넘쳐 0.5배면 되는 상황이어도 하한을 지킨다.
        float speed = alignment.ResolvePlaySpeed(now: 0f, impactAlignTime: 2f, maxSpeed: 2.5f, out bool clamped);

        Assert.IsFalse(clamped);
        Assert.AreEqual(1.5f, speed, 1e-4f);
    }

    [Test]
    public void 상한이_하한보다_낮아도_하한을_지킨다()
    {
        var alignment = Make(startOffset: 0f, duration: 2f, impactTime: 1f, speed: 3f);

        float speed = alignment.ResolvePlaySpeed(now: 0f, impactAlignTime: 1f, maxSpeed: 2.5f, out _);

        Assert.AreEqual(3f, speed, 1e-4f);
    }

    // ── 사용 가능 여부 ──────────────────────────────────────────────────────

    [Test]
    public void 클립이_없으면_사용_불가다()
    {
        Assert.IsFalse(Make(0f, 2f, 1f, 1f).IsUsable); // EditorAssign이 clip을 null로 넣었다
    }
}
