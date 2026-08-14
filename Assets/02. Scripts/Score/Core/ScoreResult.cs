namespace ScoreSpace
{
    /// <summary>
    /// 곡 하나의 채점 결과. <b>씬을 넘어가는 값</b>이라(<c>GameSession.LastResult</c>) 불변 struct다.
    ///
    /// <para><b>왜 등급을 같이 담나</b>: 등급은 점수의 함수(<see cref="ScoreMath.GradeOf"/>)라 다시 계산할 수도
    /// 있지만, 그러려면 소비자가 <b>문턱 배열까지</b> 알아야 한다. 결과를 읽는 쪽(결과 화면)이 채점 설정을
    /// 다시 들여다보게 만들면 둘이 어긋나는 순간이 조용히 생긴다.</para>
    /// </summary>
    public readonly struct ScoreResult
    {
        public readonly long Score;
        public readonly long MaxScore;

        /// <summary>달성 비율(0~1). 만점이 곡마다 다르므로 <b>곡 간 비교가 가능한 유일한 값</b>이다.</summary>
        public readonly float Ratio;

        public readonly ScoreGrade Grade;

        public readonly int PerfectCount;
        public readonly int GoodCount;
        public readonly int MissCount;
        public readonly int TotalNotes;

        public readonly int MaxCombo;
        public readonly int SuccessPatterns;
        public readonly int TotalPatterns;

        /// <summary>
        /// 전 노트 Perfect · 전 패턴 성공 · 풀콤보. <b>정수 비교 셋의 논리곱</b>이라 오차가 낄 자리가 없고,
        /// 이 값 하나가 <see cref="ScoreGrade.SSS"/>의 유일한 조건이다.
        /// </summary>
        public readonly bool IsPerfect;

        public ScoreResult(
            long score, long maxScore, float ratio, ScoreGrade grade,
            int perfectCount, int goodCount, int missCount, int totalNotes,
            int maxCombo, int successPatterns, int totalPatterns, bool isPerfect)
        {
            Score = score;
            MaxScore = maxScore;
            Ratio = ratio;
            Grade = grade;
            PerfectCount = perfectCount;
            GoodCount = goodCount;
            MissCount = missCount;
            TotalNotes = totalNotes;
            MaxCombo = maxCombo;
            SuccessPatterns = successPatterns;
            TotalPatterns = totalPatterns;
            IsPerfect = isPerfect;
        }
    }
}
