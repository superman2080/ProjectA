namespace ScoreSpace
{
    /// <summary>
    /// 정수를 <b>한자 수사</b>로 적는다(<c>137</c> → <c>百三十七</c>).
    /// <c>ScoreMath</c>와 같은 규율의 <b>순수 함수</b> — 엔진·씬·판정을 모르고 들어오는 것은 <c>int</c> 하나뿐이다.
    ///
    /// <para>규칙 셋이고, <b>마지막 둘이 서로 방향이 반대다</b>:</para>
    /// <list type="number">
    /// <item><b>0인 자리는 통째로 건너뛴다</b> — <c>250</c> → <c>二百五十</c>, <c>105</c> → <c>百五</c>.</item>
    /// <item><b>⚠ <c>十</c>·<c>百</c>·<c>千</c> 앞의 <c>一</c>은 생략한다</b> — <c>10</c> → <c>十</c>(<c>一十</c>이 아니다).</item>
    /// <item><b>⚠ 그런데 <c>万</c>·<c>億</c> 앞의 <c>一</c>은 생략하지 않는다</b> — <c>10000</c> → <c>一万</c>(<c>万</c>이 아니다).
    /// 2번과 반대라 한 함수 안에서 섞이기 쉬운 지점이고, <c>KanjiNumeralTests</c>가 지키는 곳이다.</item>
    /// </list>
    ///
    /// <para><b>⚠ 틀린 한자는 화면에 멀쩡히 그려진다</b> — 플레이로는 못 잡으므로 검증은 테스트가 전부다
    /// (<c>ScoreMathTests</c>의 존재 이유와 같다).</para>
    /// </summary>
    public static class KanjiNumeral
    {
        private const string Digits = "〇一二三四五六七八九";

        /// <summary>네 자리 묶음의 표시. 인덱스가 곧 10^(4i)이라 배열 길이가 표현 상한을 정한다(億 = int 전 범위).</summary>
        private static readonly string[] GroupMarks = { "", "万", "億" };

        private static readonly int[] Places = { 1000, 100, 10 };
        private static readonly string[] PlaceMarks = { "千", "百", "十" };

        /// <summary>
        /// <paramref name="value"/>를 한자 수사로 적는다. 0 이하는 <c>〇</c>.
        ///
        /// <para>콤보는 음수가 될 수 없지만 <c>int</c>를 받는 함수라 정의해 둔다.
        /// 0은 화면에 안 뜨지만(<c>ScoreHudView</c>가 감춘다) 그것은 호출자의 사정이지 이 함수의 값이 아니다.</para>
        /// </summary>
        public static string Of(int value)
        {
            if (value <= 0) return "〇";

            string result = "";
            for (int g = 0; value > 0 && g < GroupMarks.Length; g++)
            {
                int group = value % 10000;
                value /= 10000;

                // ⚠ 묶음이 통째로 0이면 그 묶음 표시도 안 붙인다(100000000 → 一億, 一億〇万이 아니다).
                if (group != 0) result = GroupOf(group) + GroupMarks[g] + result;
            }

            return result;
        }

        /// <summary>1~9999 한 묶음. 자리표 앞의 <c>一</c>만 생략한다(규칙 2) — 묶음 표시는 호출자가 붙인다(규칙 3).</summary>
        private static string GroupOf(int n)
        {
            string s = "";

            for (int i = 0; i < Places.Length; i++)
            {
                int d = n / Places[i] % 10;
                if (d == 0) continue;

                s += (d == 1 ? "" : Digits[d].ToString()) + PlaceMarks[i];
            }

            int ones = n % 10;
            if (ones != 0) s += Digits[ones];

            return s;
        }
    }
}
