using NUnit.Framework;
using ScoreSpace;

/// <summary>
/// 채점의 <b>순수 로직</b> 검증. 여기서 깨지면 점수가 조용히 틀리는데,
/// 화면에는 숫자가 멀쩡히 나오므로 플레이로는 못 잡는다 — 그게 이 테스트의 존재 이유다.
/// </summary>
public class ScoreMathTests
{
    private static readonly float[] Grades = { 0.95f, 0.90f, 0.85f, 0.77f, 0.60f };
    private static readonly int[] Tiers = { 10, 30, 60 };

    private const float NoteShare = 0.7f, PatternShare = 0.2f, ComboShare = 0.1f;

    /// <summary>완벽한 플레이의 달성도. 세 풀이 전부 1이어야 한다.</summary>
    private static float PerfectAchievement(int notes, int patterns) => ScoreMath.Achievement(
        ScoreMath.NoteAchievement(notes, 0, notes, 0.6f),
        ScoreMath.PatternAchievement(patterns, patterns),
        ScoreMath.ComboAchievement(ScoreMath.FullComboSum(notes), notes),
        NoteShare, PatternShare, ComboShare);

    // ── 만점이 딱 떨어지는가 ────────────────────────────────────────────────

    [TestCase(1_000_000L)]
    [TestCase(500_000L)]
    [TestCase(2_000_000L)]
    [TestCase(777_777L)]
    public void PerfectPlayScoresExactlyMaxScore(long maxScore)
    {
        // ⚠ 0.7f + 0.2f + 0.1f는 부동소수점에서 0.99999994f다.
        // Achievement가 비중 합으로 나누지 않으면 여기서 1점이 새고, SSS가 영영 안 나온다.
        float achievement = PerfectAchievement(notes: 120, patterns: 30);

        Assert.AreEqual(1f, achievement, 1e-6f, "퍼펙트인데 달성도가 1이 아니다");
        Assert.AreEqual(maxScore, ScoreMath.Total(achievement, maxScore));
    }

    [Test]
    public void AllMissScoresZero()
    {
        float achievement = ScoreMath.Achievement(
            ScoreMath.NoteAchievement(0, 0, 100, 0.6f),
            ScoreMath.PatternAchievement(0, 25),
            ScoreMath.ComboAchievement(0L, 100),
            NoteShare, PatternShare, ComboShare);

        Assert.AreEqual(0L, ScoreMath.Total(achievement, 1_000_000L));
    }

    [Test]
    public void EmptyChartCollapsesToZeroWithoutDividingByZero()
    {
        Assert.AreEqual(0f, ScoreMath.NoteAchievement(0, 0, 0, 0.6f));
        Assert.AreEqual(0f, ScoreMath.PatternAchievement(0, 0));
        Assert.AreEqual(0f, ScoreMath.ComboAchievement(0L, 0));
        Assert.AreEqual(0L, ScoreMath.FullComboSum(0));
        Assert.AreEqual(0L, ScoreMath.Total(1f, 0L));
    }

    // ── 각 풀 ───────────────────────────────────────────────────────────────

    [Test]
    public void FullComboSumIsTriangular()
    {
        Assert.AreEqual(6L, ScoreMath.FullComboSum(3));
        Assert.AreEqual(1L, ScoreMath.FullComboSum(1));
        Assert.AreEqual(0L, ScoreMath.FullComboSum(-5));
    }

    [Test]
    public void AllGoodGivesExactlyGoodWeight()
    {
        Assert.AreEqual(0.6f, ScoreMath.NoteAchievement(0, 50, 50, 0.6f), 1e-6f);
    }

    [Test]
    public void MissedNotesOnlyCountsTheGap()
    {
        Assert.AreEqual(3, ScoreMath.MissedNotes(nodeCount: 5, judgedCount: 2));
        Assert.AreEqual(0, ScoreMath.MissedNotes(nodeCount: 5, judgedCount: 5));

        // 판정 수가 노드 수를 넘어도 점수가 거꾸로 오르면 안 된다.
        Assert.AreEqual(0, ScoreMath.MissedNotes(nodeCount: 5, judgedCount: 7));
    }

    // ── 등급 ────────────────────────────────────────────────────────────────

    [TestCase(1.00f, ExpectedResult = ScoreGrade.SS)]   // 퍼펙트가 아니면 만점이어도 SS다
    [TestCase(0.95f, ExpectedResult = ScoreGrade.SS)]
    [TestCase(0.90f, ExpectedResult = ScoreGrade.S)]
    [TestCase(0.8999f, ExpectedResult = ScoreGrade.A)]
    [TestCase(0.85f, ExpectedResult = ScoreGrade.A)]
    [TestCase(0.77f, ExpectedResult = ScoreGrade.B)]
    [TestCase(0.60f, ExpectedResult = ScoreGrade.C)]
    [TestCase(0.5999f, ExpectedResult = ScoreGrade.D)]
    [TestCase(0f, ExpectedResult = ScoreGrade.D)]
    public ScoreGrade GradeBoundariesAreInclusive(float ratio) =>
        ScoreMath.GradeOf(ratio, isPerfect: false, Grades);

    [Test]
    public void TopGradeRequiresPerfectNotRatio()
    {
        // 비율이 만점이어도 퍼펙트가 아니면 SSS가 아니다 — 최고 등급의 유일성.
        Assert.AreEqual(ScoreGrade.SS, ScoreMath.GradeOf(1f, isPerfect: false, Grades));

        // 반대로 반올림으로 비율이 밀려도 퍼펙트면 강등되지 않는다.
        Assert.AreEqual(ScoreGrade.SSS, ScoreMath.GradeOf(0.9999f, isPerfect: true, Grades));
        Assert.AreEqual(ScoreGrade.SSS, ScoreMath.GradeOf(0f, isPerfect: true, Grades));
    }

    [Test]
    public void GradeIsIndependentOfMaxScore()
    {
        // 같은 플레이(달성도 0.87)를 만점만 바꿔 채점해도 등급이 같아야 한다 —
        // 절대 점수로 갈랐다면 여기서 깨진다.
        const float ratio = 0.87f;

        Assert.AreEqual(ScoreGrade.A, ScoreMath.GradeOf(ratio, false, Grades));
        Assert.AreEqual(870_000L, ScoreMath.Total(ratio, 1_000_000L));
        Assert.AreEqual(435_000L, ScoreMath.Total(ratio, 500_000L));
        Assert.AreEqual(1_740_000L, ScoreMath.Total(ratio, 2_000_000L));
    }

    [Test]
    public void EmptyGradeThresholdsFallToLowestButKeepPerfect()
    {
        Assert.AreEqual(ScoreGrade.D, ScoreMath.GradeOf(1f, false, new float[0]));
        Assert.AreEqual(ScoreGrade.SSS, ScoreMath.GradeOf(1f, true, new float[0]));
        Assert.AreEqual(ScoreGrade.D, ScoreMath.GradeOf(1f, false, null));
    }

    // ── 콤보 단계 ───────────────────────────────────────────────────────────

    [TestCase(0, ExpectedResult = 0)]
    [TestCase(9, ExpectedResult = 0)]
    [TestCase(10, ExpectedResult = 1)]   // 경계는 이상(>=)
    [TestCase(29, ExpectedResult = 1)]
    [TestCase(30, ExpectedResult = 2)]
    [TestCase(60, ExpectedResult = 3)]
    [TestCase(9999, ExpectedResult = 3)]
    public int ComboTierStepsAtThresholds(int combo) => ScoreMath.ComboTierOf(combo, Tiers);

    [Test]
    public void EmptyComboThresholdsMeanNoTier()
    {
        // 배열이 비면 연출이 조용히 꺼진 상태다 — 예외가 아니다.
        Assert.AreEqual(0, ScoreMath.ComboTierOf(500, new int[0]));
        Assert.AreEqual(0, ScoreMath.ComboTierOf(500, null));
    }

    [Test]
    public void ComboPoolPunishesBreaks()
    {
        // 같은 노트 수에서 한 번 끊긴 쪽이 반드시 낮아야 한다(끊김이 점수에 안 보이면 콤보 풀이 무의미하다).
        const int notes = 100;

        float full = ScoreMath.ComboAchievement(ScoreMath.FullComboSum(notes), notes);
        // 50에서 끊긴 경우: (1..50) + (1..50)
        long broken = ScoreMath.FullComboSum(50) * 2;

        Assert.AreEqual(1f, full, 1e-6f);
        Assert.Less(ScoreMath.ComboAchievement(broken, notes), full);
    }
}
