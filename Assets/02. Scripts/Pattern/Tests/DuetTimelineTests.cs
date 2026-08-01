using NUnit.Framework;
using PatternSpace;

/// <summary>
/// <see cref="DuetTimeline"/> 검증.
///
/// <para>합주 프리뷰는 <b>화면(임팩트 기준 상대시간)과 에셋(클립 절대시간)</b> 두 단위를 오간다.
/// 이 변환이 틀리면 저장값이 통째로 어긋나는데 <b>화면에는 안 나타난다</b> — 그래서 여기서 못박는다.</para>
/// </summary>
public class DuetTimelineTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    [TestCase(0.5f, 1f, 0f)]
    [TestCase(0.5f, 1f, 0.25f)]
    [TestCase(0.5f, 1f, -0.25f)]
    [TestCase(1.2f, 2.5f, 0.4f)]
    [TestCase(1.2f, 0.5f, -0.8f)]
    [TestCase(0f, 1.7f, 0.33f)]
    public void RoundTrip_IsIdentity(float impact, float speed, float t)
    {
        float clipTime = DuetTimeline.ClipTimeOf(impact, speed, t);
        float back = DuetTimeline.RelativeOf(impact, speed, clipTime);

        Assert.AreEqual(t, back, Tolerance, "뷰 → 클립 → 뷰는 항등이어야 한다.");
    }

    [Test]
    [TestCase(0.5f, 1f)]
    [TestCase(0.5f, 2.5f)]
    [TestCase(1.2f, 0.3f)]
    public void AtZero_IsImpactRegardlessOfSpeed(float impact, float speed)
    {
        // 이 프리뷰가 성립하는 근거 자체 — t=0에서 배속이 식에서 소거된다.
        Assert.AreEqual(impact, DuetTimeline.ClipTimeOf(impact, speed, 0f), Tolerance);
    }

    [Test]
    public void DoubleSpeed_AdvancesClipTwiceAsFar()
    {
        float slow = DuetTimeline.ClipTimeOf(1f, 1f, 0.2f) - 1f;
        float fast = DuetTimeline.ClipTimeOf(1f, 2f, 0.2f) - 1f;

        Assert.AreEqual(slow * 2f, fast, Tolerance);
    }

    [Test]
    public void NonPositiveSpeed_DoesNotFreezeOrInvertTime()
    {
        // 0이면 시간이 멈추고 음수면 뒤집힌다. 하한으로 막혀 있어야 한다.
        Assert.Greater(DuetTimeline.ClipTimeOf(0f, 0f, 1f), 0f);
        Assert.Greater(DuetTimeline.ClipTimeOf(0f, -5f, 1f), 0f);
    }

    [Test]
    public void LeadAndTail_AreRelativeSeconds()
    {
        // 클립 초 0.4 앞 / 0.6 뒤, 배속 2 → 상대시간은 절반이다.
        Assert.AreEqual(0.2f, DuetTimeline.LeadOf(0.6f, 1.0f, 2f), Tolerance);
        Assert.AreEqual(0.3f, DuetTimeline.TailOf(1.0f, 1.6f, 2f), Tolerance);
    }

    [Test]
    public void LeadAndTail_ClampAtZeroWhenInverted()
    {
        Assert.AreEqual(0f, DuetTimeline.LeadOf(1.5f, 1.0f, 1f), Tolerance, "임팩트가 시작보다 앞이면 리드는 0이다.");
        Assert.AreEqual(0f, DuetTimeline.TailOf(1.0f, 0.5f, 1f), Tolerance, "임팩트가 끝보다 뒤면 테일은 0이다.");
    }

    [Test]
    public void Range_IsUnionOfBothActors()
    {
        // A: 리드 0.4 / 테일 0.2   B: 리드 0.1 / 테일 0.9
        var (min, max) = DuetTimeline.Range(
            0.0f, 0.4f, 0.6f, 1f,
            0.9f, 1.0f, 1.9f, 1f);

        Assert.AreEqual(-0.4f, min, Tolerance, "더 긴 리드가 범위를 정한다.");
        Assert.AreEqual(0.9f, max, Tolerance, "더 긴 테일이 범위를 정한다.");
    }

    [Test]
    public void Range_WorksWithOneActorEmpty()
    {
        // 한쪽 배우가 비면(전부 0) 나머지 하나로 성립해야 한다 — 슬롯이 비어도 저작은 계속된다.
        var (min, max) = DuetTimeline.Range(
            0.0f, 0.4f, 0.6f, 1f,
            0f, 0f, 0f, 1f);

        Assert.AreEqual(-0.4f, min, Tolerance);
        Assert.AreEqual(0.2f, max, Tolerance);
    }

    [Test]
    public void Range_BothEmpty_IsZero()
    {
        var (min, max) = DuetTimeline.Range(0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);

        Assert.AreEqual(0f, min, Tolerance);
        Assert.AreEqual(0f, max, Tolerance);
    }

    [Test]
    public void ClampToTrim_ReturnsBoundary()
    {
        Assert.AreEqual(0.2f, DuetTimeline.ClampToTrim(0.1f, 0.2f, 0.8f), Tolerance);
        Assert.AreEqual(0.8f, DuetTimeline.ClampToTrim(1.5f, 0.2f, 0.8f), Tolerance);
        Assert.AreEqual(0.5f, DuetTimeline.ClampToTrim(0.5f, 0.2f, 0.8f), Tolerance);
    }

    [Test]
    public void ClampToTrim_DegenerateRange_ReturnsStart()
    {
        // 트림이 뒤집혔거나 길이 0 — 늘려 보여주는 것보다 시작에 세워 두는 편이 거짓이 적다.
        Assert.AreEqual(0.4f, DuetTimeline.ClampToTrim(9f, 0.4f, 0.4f), Tolerance);
        Assert.AreEqual(0.4f, DuetTimeline.ClampToTrim(9f, 0.4f, 0.1f), Tolerance);
    }
}
