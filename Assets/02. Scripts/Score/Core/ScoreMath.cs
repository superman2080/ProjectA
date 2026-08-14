using System.Collections.Generic;
using UnityEngine;

namespace ScoreSpace
{
    /// <summary>
    /// 등급. <b>⚠ 정수를 명시한다</b> — 직렬화·저장 기록의 키가 되므로 순서가 바뀌면 과거 기록이 밀린다
    /// (<c>SfxTrigger</c>·<c>EffectTrigger</c>와 같은 규율).
    /// </summary>
    public enum ScoreGrade
    {
        D = 0,
        C = 1,
        B = 2,
        A = 3,
        S = 4,
        SS = 5,
        SSS = 6
    }

    /// <summary>
    /// 채점의 <b>순수 계산</b>. MonoBehaviour·씬에 의존하지 않아 그대로 유닛테스트할 수 있다
    /// (<c>EnemyRing</c>·<c>DuelGap</c>과 같은 규율).
    ///
    /// <para><b>⚠ <c>JudgementResult</c>를 모른다.</b> 그 타입은 Assembly-CSharp에 있어 asmdef가 참조할 수 없고,
    /// 참조할 이유도 없다 — 여기 들어오는 것은 이미 <b>세어진 개수</b>뿐이다. 판정 → 개수 변환은
    /// <c>ScoreDirector</c>가 한다.</para>
    /// </summary>
    public static class ScoreMath
    {
        /// <summary>
        /// 풀콤보일 때의 콤보 누적합 = <c>N(N+1)/2</c>. <b>콤보 풀의 분모</b>다.
        ///
        /// <para><b>⚠ <c>long</c>이다.</b> 노트 3000개면 450만이라 곱셈 전에 <c>int</c>로 잡으면 넘칠 여지가 생긴다.</para>
        /// </summary>
        public static long FullComboSum(int noteCount)
        {
            if (noteCount <= 0) return 0L;
            return (long)noteCount * (noteCount + 1) / 2;
        }

        /// <summary>
        /// 노트 풀의 달성도(0~1). <paramref name="missCount"/>는 받지 않는다 —
        /// Miss는 가중치가 0이라 <b>총 노트 수에만 기여</b>하고, 그 총 수는 곡이 이미 알고 있다.
        ///
        /// <para><b>⚠ 전부 Perfect면 정확히 <c>1f</c></b>여야 한다(같은 값끼리의 나눗셈이라 오차가 없다) —
        /// 그래야 만점이 딱 떨어진다.</para>
        /// </summary>
        public static float NoteAchievement(int perfectCount, int goodCount, int totalNotes, float goodWeight)
        {
            if (totalNotes <= 0) return 0f;   // 빈 곡·중단은 예외가 아니라 0점이다

            float earned = perfectCount + goodCount * Mathf.Clamp01(goodWeight);
            return Mathf.Clamp01(earned / totalNotes);
        }

        /// <summary>패턴 풀의 달성도(0~1).</summary>
        public static float PatternAchievement(int successCount, int totalPatterns)
        {
            if (totalPatterns <= 0) return 0f;
            return Mathf.Clamp01((float)successCount / totalPatterns);
        }

        /// <summary>
        /// 콤보 풀의 달성도(0~1). <paramref name="comboSum"/>은 <b>각 노트 직후의 콤보 수를 전부 더한 값</b>이며,
        /// 분모가 <see cref="FullComboSum"/>이라 <b>풀콤보에서 정확히 1</b>이 된다.
        ///
        /// <para>그래서 튜닝 테이블도 구간 배율도 필요 없다 — 만점 조건이 식 안에 이미 들어 있다.</para>
        /// </summary>
        public static float ComboAchievement(long comboSum, int totalNotes)
        {
            long full = FullComboSum(totalNotes);
            if (full <= 0L) return 0f;

            return Mathf.Clamp01((float)((double)comboSum / full));
        }

        /// <summary>
        /// 세 풀을 비중으로 합친 최종 달성도(0~1).
        ///
        /// <para><b>⚠ 비중 합으로 나눈다.</b> <c>0.7f + 0.2f + 0.1f</c>는 부동소수점에서 정확히 1이 아니라
        /// <c>0.99999994f</c>다 — 그대로 쓰면 <b>퍼펙트인데 만점이 1점 모자란다</b>. 분자와 분모가
        /// 같은 식이라 셋이 전부 1일 때 결과가 <b>정확히 1f</b>가 된다. 덤으로 비중을 상대값으로 써도 된다.</para>
        /// </summary>
        public static float Achievement(
            float noteAchievement, float patternAchievement, float comboAchievement,
            float noteShare, float patternShare, float comboShare)
        {
            float total = noteShare + patternShare + comboShare;
            if (total <= 0f) return 0f;

            float weighted = noteAchievement * noteShare
                           + patternAchievement * patternShare
                           + comboAchievement * comboShare;

            return Mathf.Clamp01(weighted / total);
        }

        /// <summary>달성도를 점수로. 달성도가 1이면 정확히 <paramref name="maxScore"/>다.</summary>
        public static long Total(float achievement, long maxScore)
        {
            if (maxScore <= 0L) return 0L;
            return (long)System.Math.Round(Mathf.Clamp01(achievement) * (double)maxScore);
        }

        /// <summary>
        /// 등급. <b>절대 점수가 아니라 달성 비율</b>로 가른다 — 만점이 곡마다 다르므로(<c>SongChart.maxScore</c>)
        /// 점수로 가르면 쉬운 곡의 만점과 어려운 곡의 절반이 같은 등급을 받는다.
        ///
        /// <para><b>⚠ <paramref name="isPerfect"/>가 비율보다 먼저다.</b> 최고 등급을 <c>ratio >= 1</c>로 판정하면
        /// 반올림 오차 하나로 <b>영영 안 나올 수 있다</b>. 퍼펙트는 정수 비교 셋의 논리곱이라
        /// 오차가 낄 자리가 없고, 그래서 <b>최고 등급의 유일성이 구조적으로 보장</b>된다.</para>
        /// </summary>
        /// <param name="thresholds">
        /// 최고 등급을 뺀 <b>내림차순</b> 비율 문턱(SS / S / A / B / C의 하한). 경계는 이상(&gt;=)이다.
        /// 비면 언제나 <see cref="ScoreGrade.D"/>(퍼펙트 제외).
        /// </param>
        public static ScoreGrade GradeOf(float ratio, bool isPerfect, IReadOnlyList<float> thresholds)
        {
            if (isPerfect) return ScoreGrade.SSS;
            if (thresholds == null || thresholds.Count == 0) return ScoreGrade.D;

            // 문턱 i를 넘으면 (최고 등급 - 1 - i)등급. 배열이 짧아도 아래쪽 등급으로 자연히 접힌다.
            for (int i = 0; i < thresholds.Count; i++)
            {
                if (ratio < thresholds[i]) continue;

                int grade = (int)ScoreGrade.SSS - 1 - i;
                return (ScoreGrade)Mathf.Max(grade, (int)ScoreGrade.D);
            }

            return ScoreGrade.D;
        }

        /// <summary>
        /// 콤보가 속한 단계(0 = 평소). <b>문턱 배열의 길이가 곧 단계 수</b>다 — 코드는 3을 모른다.
        /// 경계는 이상(&gt;=)이며, 배열이 비면 언제나 0단계다(연출이 조용히 꺼진 상태).
        /// </summary>
        public static int ComboTierOf(int combo, IReadOnlyList<int> thresholds)
        {
            if (thresholds == null) return 0;

            int tier = 0;
            for (int i = 0; i < thresholds.Count; i++)
                if (combo >= thresholds[i]) tier = i + 1;

            return tier;
        }

        /// <summary>
        /// 이 패턴에서 <b>놓친 노트 수</b>. 놓친 노트는 이벤트로 오지 않으므로
        /// (<c>OnJudged</c>는 안 나고 <c>OnFocusRingMissedArrival</c>은 늦은 Good에도 난다)
        /// <b>뺄셈이 유일한 셈법</b>이다.
        ///
        /// <para>음수를 돌려주지 않는다 — 통과 노드 자동 인식 등으로 판정 수가 노드 수를 넘는 경우가
        /// 생기더라도 점수가 거꾸로 오르면 안 된다.</para>
        /// </summary>
        public static int MissedNotes(int nodeCount, int judgedCount) => Mathf.Max(nodeCount - judgedCount, 0);
    }
}
