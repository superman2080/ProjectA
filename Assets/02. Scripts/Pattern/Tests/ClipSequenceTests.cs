using NUnit.Framework;
using PatternSpace;
using UnityEngine;

/// <summary>
/// <see cref="ClipSequence"/> 검증.
///
/// <para>지키는 것은 둘이다 — ① <b>리드인이 비면 예전 단일 클립 경로와 대수적으로 같다</b>(이 기능의 회귀 0 보장 전부가
/// 여기 걸려 있다) ② <b>창이 모자라도 원소가 잘리지 않는다</b>: 비율 하나로 전체가 압축되고 그 합이 정확히 창에 들어간다.</para>
/// </summary>
public class ClipSequenceTests
{
    private const float Tolerance = 1e-4f;

    private static ClipAlignment Make(float startOffset, float duration, float impactTime, float speed)
    {
        var alignment = new ClipAlignment();
        alignment.EditorAssign(new AnimationClip(), startOffset, duration, impactTime, speed);
        return alignment;
    }

    /// <summary>배선이 없는 원소. 리스트에 섞여 있는 것이 정상 상태라 계산에서 빠져야 한다.</summary>
    private static ClipAlignment Empty()
    {
        var alignment = new ClipAlignment();
        alignment.EditorAssign(null, 0f, 1f, 0f, 1f);
        return alignment;
    }

    /// <summary>
    /// 리드인이 비면 <c>ResolvedImpactSpan / Speed</c>와 같다 — 즉 기존 예약 식
    /// (<c>impactAlign − freeze − impactSpan/speed</c>)이 한 글자도 안 바뀐 것과 같다.
    /// </summary>
    [Test]
    public void AuthoredSpanWithoutLeadInMatchesSingleClipPath()
    {
        // 트림 [0, 1.0], 임팩트 0.6 → 임팩트 스팬 0.6, 저작 배속 1.5 → 0.4초
        var final = Make(0f, 1.0f, 0.6f, 1.5f);

        Assert.AreEqual(0.4f, ClipSequence.AuthoredSpan(null, final), Tolerance);
        Assert.AreEqual(0.4f, ClipSequence.AuthoredSpan(new ClipAlignment[0], final), Tolerance);
        Assert.AreEqual(ClipSequence.AuthoredImpactSpan(final), ClipSequence.AuthoredSpan(null, final), Tolerance);
    }

    /// <summary>리드인은 <b>트림 전체</b>가 재생 길이다 — ImpactTime을 보지 않는다(정렬 앵커는 마지막 하나).</summary>
    [Test]
    public void LeadInUsesFullTrimNotImpactSpan()
    {
        // 임팩트가 0.2에 찍혀 있어도 길이는 트림 전체 0.8 / 배속 2 = 0.4초여야 한다.
        var element = Make(0f, 0.8f, 0.2f, 2f);

        Assert.AreEqual(0.4f, ClipSequence.AuthoredDuration(element), Tolerance);
    }

    /// <summary>창이 충분하면 저작 배속 그대로다(느리게 늘이지 않는다).</summary>
    [Test]
    public void ResolveRatioIsOneWhenWindowFits()
    {
        Assert.AreEqual(1f, ClipSequence.ResolveRatio(0.4f, 1.0f), Tolerance);
        Assert.AreEqual(1f, ClipSequence.ResolveRatio(0.4f, 0.4f), Tolerance);
        Assert.AreEqual(1f, ClipSequence.ResolveRatio(0f, 0.01f), Tolerance); // 재생할 것이 없으면 1
    }

    /// <summary>창이 절반이면 정확히 2배. 원소마다 따로 정하지 않고 <b>하나의 비율</b>이 나온다.</summary>
    [Test]
    public void ResolveRatioCompressesToFitWindow()
    {
        Assert.AreEqual(2f, ClipSequence.ResolveRatio(1.0f, 0.5f), Tolerance);
        Assert.AreEqual(4f, ClipSequence.ResolveRatio(1.2f, 0.3f), Tolerance);
    }

    /// <summary>
    /// <b>이 기능의 핵심 불변식</b>: 압축이 걸려도 원소가 하나도 잘리지 않고, 전부 재생한 실시간의 합이
    /// 정확히 창과 같다(= 마지막 임팩트가 제시각에 도착한다).
    /// </summary>
    [Test]
    public void EveryElementPlaysAndTotalFitsExactly()
    {
        var leadIn = new[]
        {
            Make(0f, 0.6f, 0f, 1f),   // 저작 0.60
            Make(0f, 0.8f, 0f, 2f)    // 저작 0.40
        };
        var final = Make(0f, 1.0f, 0.5f, 1f); // 임팩트까지 저작 0.50

        float authored = ClipSequence.AuthoredSpan(leadIn, final);
        Assert.AreEqual(1.5f, authored, Tolerance);

        const float available = 0.75f; // 저작의 절반 → 비율 2
        float ratio = ClipSequence.ResolveRatio(authored, available);
        Assert.AreEqual(2f, ratio, Tolerance);

        float total = 0f;
        foreach (var element in leadIn)
        {
            float real = ClipSequence.RealDuration(element, ratio);
            Assert.Greater(real, 0f, "원소가 0초로 잘리면 안 된다.");
            total += real;
        }
        total += ClipSequence.AuthoredImpactSpan(final) / ratio;

        Assert.AreEqual(available, total, Tolerance);
    }

    /// <summary>배선이 없는 원소는 길이에도, 추림에도 들어가지 않는다.</summary>
    [Test]
    public void UnusableElementsAreExcluded()
    {
        var leadIn = new[] { Make(0f, 0.6f, 0f, 1f), Empty(), null };
        var final = Make(0f, 1.0f, 0.5f, 1f);

        Assert.AreEqual(1.1f, ClipSequence.AuthoredSpan(leadIn, final), Tolerance);
        Assert.AreEqual(0f, ClipSequence.AuthoredDuration(Empty()), Tolerance);
        Assert.AreEqual(1, ClipSequence.Usable(leadIn).Length);
        Assert.AreEqual(0, ClipSequence.Usable(null).Length);
    }

    /// <summary>
    /// 리드인 원소의 트림 끝은 <b>다음 원소가 시작하는 지점</b>이다 — 마지막 원소의 끝이
    /// 곧 마지막 클립의 시작(= 임팩트에서 임팩트 스팬만큼 앞)이어야 저작 툴 타임라인이 런타임과 일치한다.
    /// </summary>
    [Test]
    public void AuthoredEndOffsetChainsBackwardsFromImpact()
    {
        var leadIn = new[]
        {
            Make(0f, 0.6f, 0f, 1f),   // 저작 0.60
            Make(0f, 0.8f, 0f, 2f)    // 저작 0.40
        };
        var final = Make(0f, 1.0f, 0.5f, 1f); // 임팩트까지 저작 0.50

        // 마지막 리드인의 끝 = 마지막 클립의 시작 = −0.50
        Assert.AreEqual(-0.5f, ClipSequence.AuthoredEndOffset(leadIn, 1, final), Tolerance);

        // 그 앞 원소의 끝 = −0.50 − 0.40 = −0.90 (= 시퀀스 시작 −1.50에서 0.60만큼 뒤)
        Assert.AreEqual(-0.9f, ClipSequence.AuthoredEndOffset(leadIn, 0, final), Tolerance);
    }
}
