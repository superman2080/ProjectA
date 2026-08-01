using System.Collections.Generic;
using UnityEngine;

namespace EnemySpace
{
    /// <summary>
    /// 링 배치·상대 선택의 <b>순수 계산</b>. MonoBehaviour·씬에 의존하지 않아 그대로 유닛테스트할 수 있다
    /// (<c>EnemyDirector</c>는 이 결과를 받아 오브젝트를 움직이기만 한다).
    /// </summary>
    public static class EnemyRing
    {
        /// <summary>
        /// 기존 각도들과 <paramref name="minAngleGap"/> 이상 떨어지면서, <b>시야 방향과의 내적이 가장 낮은</b>
        /// (= 가장 확실하게 등 뒤인) 각도를 고른다.
        ///
        /// <para><b>"뒤가 될 때까지 재시도"가 아니라 "가장 뒤인 곳을 고른다"</b>는 점이 핵심이다.
        /// 빈 각도가 앞쪽밖에 없어도 실패하지 않고 최선을 반환하므로 무한 재시도·스폰 실패가 원천적으로 없다.</para>
        /// </summary>
        /// <param name="occupiedAngles">이미 점유된 각도(도).</param>
        /// <param name="viewYaw">시야 방향의 yaw(도). 카메라 forward 기준.</param>
        /// <param name="minAngleGap">기존 적과 유지할 최소 각 간격(도).</param>
        /// <param name="candidateCount">원주를 몇 등분해 후보로 삼을지.</param>
        public static float PickSpawnAngle(
            IReadOnlyList<float> occupiedAngles, float viewYaw, float minAngleGap, int candidateCount = 36)
        {
            candidateCount = Mathf.Max(candidateCount, 4);

            float bestAngle = viewYaw + 180f;
            float bestScore = float.MaxValue;
            bool foundSpaced = false;

            for (int i = 0; i < candidateCount; i++)
            {
                float angle = 360f * i / candidateCount;

                // 시야와의 각차가 클수록(=등 뒤일수록) 내적이 낮다. cos(각차)가 곧 내적이다.
                float score = Mathf.Cos((angle - viewYaw) * Mathf.Deg2Rad);
                bool spaced = HasClearance(occupiedAngles, angle, minAngleGap);

                // 간격을 지키는 후보가 하나라도 있으면 그쪽만 경쟁시킨다.
                if (foundSpaced && !spaced) continue;

                if (spaced && !foundSpaced)
                {
                    foundSpaced = true;
                    bestAngle = angle;
                    bestScore = score;
                    continue;
                }

                if (score >= bestScore) continue;

                bestAngle = angle;
                bestScore = score;
            }

            return Normalize(bestAngle);
        }

        /// <summary>후보 각도가 기존 각도들과 <paramref name="minAngleGap"/> 이상 떨어져 있는지.</summary>
        public static bool HasClearance(IReadOnlyList<float> occupiedAngles, float angle, float minAngleGap)
        {
            if (occupiedAngles == null) return true;

            for (int i = 0; i < occupiedAngles.Count; i++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(occupiedAngles[i], angle)) < minAngleGap) return false;
            }

            return true;
        }

        /// <summary>
        /// 다음 교전 상대의 인덱스. <b>링 각도 순</b>으로 고른다 — 배치는 랜덤이되 선택은 순서라야
        /// 플레이어가 아레나를 한 바퀴 훑는 그림이 나온다(완전 랜덤은 좌우로 홱홱 꺾여 어지럽다).
        /// 후보가 없으면 -1.
        /// </summary>
        public static int PickNextOpponent(IReadOnlyList<float> candidateAngles, float currentAngle)
        {
            if (candidateAngles == null || candidateAngles.Count == 0) return -1;

            int best = -1;
            float bestDelta = float.MaxValue;

            for (int i = 0; i < candidateAngles.Count; i++)
            {
                // 한 방향(반시계)으로만 진행한다. 0도는 자기 자신이므로 360으로 밀어 마지막 순번이 되게 한다.
                float delta = Mathf.Repeat(candidateAngles[i] - currentAngle, 360f);
                if (delta <= Mathf.Epsilon) delta = 360f;

                if (delta >= bestDelta) continue;

                bestDelta = delta;
                best = i;
            }

            return best;
        }

        /// <summary>각도(도)를 링 위 월드 좌표로. 0도 = +Z, 반시계.</summary>
        public static Vector3 AngleToPosition(Vector3 center, float angleDegrees, float radius)
        {
            float rad = angleDegrees * Mathf.Deg2Rad;
            return center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius;
        }

        /// <summary>월드 방향의 yaw(도). <see cref="AngleToPosition"/>과 같은 기준(0도 = +Z).</summary>
        public static float DirectionToAngle(Vector3 direction)
        {
            return Normalize(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
        }

        public static float Normalize(float angleDegrees) => Mathf.Repeat(angleDegrees, 360f);
    }
}
