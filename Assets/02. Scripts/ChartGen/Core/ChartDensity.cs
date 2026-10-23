using System;

namespace ChartGen
{
    /// <summary>
    /// 레벨 → 목표 노트 밀도(초당 노트 수, NPS) → 목표 노트 수. Unity API 비의존.
    ///
    /// <para>레벨이 노트 수를 <b>직접</b> 정한다(docs/ChartDensity). 예전에는 레벨이 격자 해상도만 정해서
    /// 노트 수가 10/11·20/21 경계에서 계단식으로 뛰었다.</para>
    /// </summary>
    public static class ChartDensity
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 30;

        /// <summary>레벨에 대한 선형 보간. <paramref name="minNps"/>는 Lv1, <paramref name="maxNps"/>는 Lv30의 밀도다.</summary>
        public static float TargetNps(int level, float minNps, float maxNps)
        {
            float t = (float)(level - MinLevel) / (MaxLevel - MinLevel);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return minNps + (maxNps - minNps) * t;
        }

        /// <summary>
        /// 목표 노트 수 = <c>round(nps × (last − first))</c>. 분모는 곡 전체가 아니라 <b>첫~마지막 온셋 구간</b>이다 —
        /// 인트로·아웃트로 무음이 밀도를 깎지 않게 한다.
        /// </summary>
        public static int TargetCount(float nps, float firstOnsetTime, float lastOnsetTime)
        {
            float span = lastOnsetTime - firstOnsetTime;
            if (span <= 0f || nps <= 0f) return 0;
            return (int)Math.Round(nps * span, MidpointRounding.AwayFromZero);
        }
    }
}
