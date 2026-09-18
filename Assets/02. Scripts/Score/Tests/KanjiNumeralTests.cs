using NUnit.Framework;
using ScoreSpace;

/// <summary>
/// 한자 수사 표기 검증. <b>틀린 한자는 화면에 멀쩡히 그려지므로 플레이로는 못 잡는다</b> —
/// 그게 이 테스트의 존재 이유다(<see cref="ScoreMathTests"/>와 같은 결).
/// </summary>
public class KanjiNumeralTests
{
    // ── 경계 ───────────────────────────────────────────────────────────────

    /// <summary>0 이하는 〇. 콤보는 음수가 될 수 없지만 int를 받는 함수라 정의돼 있어야 한다.</summary>
    [TestCase(0)]
    [TestCase(-5)]
    [TestCase(int.MinValue)]
    public void NonPositive_IsZeroGlyph(int value)
    {
        Assert.AreEqual("〇", KanjiNumeral.Of(value));
    }

    // ── 규칙 1: 0인 자리는 건너뛴다 ─────────────────────────────────────────

    [TestCase(105, "百五")]
    [TestCase(250, "二百五十")]
    [TestCase(2004, "二千四")]
    public void ZeroPlace_IsSkipped(int value, string expected)
    {
        Assert.AreEqual(expected, KanjiNumeral.Of(value));
    }

    // ── 규칙 2: 十·百·千 앞의 一은 생략한다 ─────────────────────────────────

    [TestCase(10, "十")]
    [TestCase(100, "百")]
    [TestCase(1000, "千")]
    [TestCase(1111, "千百十一")]
    public void LeadingOne_BeforePlaceMark_IsOmitted(int value, string expected)
    {
        Assert.AreEqual(expected, KanjiNumeral.Of(value));
    }

    // ── 규칙 3: 万·億 앞의 一은 생략하지 <b>않는다</b> (규칙 2와 반대 방향) ──

    [TestCase(10000, "一万")]
    [TestCase(100000000, "一億")]
    public void LeadingOne_BeforeGroupMark_IsKept(int value, string expected)
    {
        Assert.AreEqual(expected, KanjiNumeral.Of(value));
    }

    // ── 일반 ───────────────────────────────────────────────────────────────

    [TestCase(1, "一")]
    [TestCase(7, "七")]
    [TestCase(9, "九")]
    [TestCase(12, "十二")]
    [TestCase(47, "四十七")]
    [TestCase(99, "九十九")]
    [TestCase(137, "百三十七")]
    [TestCase(274, "二百七十四")]   // 현재 채보(Dreamer_Lv10)의 풀콤보 실측값
    [TestCase(999, "九百九十九")]
    [TestCase(12345, "一万二千三百四十五")]
    [TestCase(int.MaxValue, "二十一億四千七百四十八万三千六百四十七")]
    public void Numerals(int value, string expected)
    {
        Assert.AreEqual(expected, KanjiNumeral.Of(value));
    }

    // ── 실제 쓰임: 콤보는 1부터 1씩 오른다 ──────────────────────────────────

    /// <summary>
    /// 1~1000이 전부 비어 있지 않고 서로 다르다. 자리표 생략 규칙이 어느 구간에서
    /// 값을 통째로 삼키거나 두 수를 같은 글자로 만들면 여기서 걸린다.
    /// </summary>
    [Test]
    public void EveryComboValue_IsDistinctAndNonEmpty()
    {
        var seen = new System.Collections.Generic.HashSet<string>();

        for (int combo = 1; combo <= 1000; combo++)
        {
            string s = KanjiNumeral.Of(combo);
            Assert.IsNotEmpty(s, "combo " + combo);
            Assert.IsTrue(seen.Add(s), "중복 표기: combo " + combo + " → " + s);
        }
    }
}
