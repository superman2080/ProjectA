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

        // ── 무대 배치 (월드 고정 원형) ────────────────────────────────────────

        /// <summary>
        /// 무대 원 <b>안</b>의 스폰 위치를 고른다. 각도 1차원이 아니라 2차원이다 —
        /// 무대가 월드에 고정되면 적도 플레이어도 원 안 어디에나 있어서 각도만으로는 자리를 정할 수 없다.
        ///
        /// <para><b>시야 판정을 주입받는다</b>(<paramref name="isVisible"/>). 절두체는 카메라의 것이고
        /// 이 클래스는 순수 계산으로 남아야 테스트할 수 있다.</para>
        ///
        /// <para><see cref="PickSpawnAngle"/>과 같은 규율 — <b>"조건을 만족할 때까지 재시도"가 아니라
        /// "가장 나은 후보를 고른다"</b>. 전부 화면 안이어도 실패하지 않고 카메라 정면에서 가장 먼 곳을 준다.</para>
        /// </summary>
        /// <param name="occupied">이미 서 있는 적들의 위치.</param>
        /// <param name="stageCenter">무대 중심(월드).</param>
        /// <param name="stageRadius">무대 반경.</param>
        /// <param name="viewPosition">카메라 위치. 폴백 점수의 기준.</param>
        /// <param name="viewForward">카메라 전방(정규화 가정). 폴백 점수의 기준.</param>
        /// <param name="minSpacing">기존 적과 유지할 최소 거리.</param>
        /// <param name="minPlayerDistance">플레이어 코앞에 튀어나오지 않게 하는 최소 거리.</param>
        /// <param name="isVisible">이 위치가 화면 안인가. null이면 전부 보이지 않는 것으로 본다.</param>
        /// <param name="random">0~1 난수원. 테스트에서 결정적으로 만들 수 있게 주입한다.</param>
        public static Vector3 PickStagePosition(
            IReadOnlyList<Vector3> occupied, Vector3 stageCenter, float stageRadius,
            Vector3 playerPosition, Vector3 viewPosition, Vector3 viewForward,
            float minSpacing, float minPlayerDistance,
            System.Func<Vector3, bool> isVisible, System.Func<float> random, int candidateCount = 24)
        {
            candidateCount = Mathf.Max(candidateCount, 4);

            Vector3 bestHidden = stageCenter;
            float bestHiddenScore = float.MinValue;   // 클수록 좋다(기존 적과의 최소거리)
            bool foundHidden = false;

            Vector3 bestFallback = stageCenter;
            float bestFallbackScore = float.MaxValue; // 작을수록 좋다(카메라 전방과의 내적)

            for (int i = 0; i < candidateCount; i++)
            {
                // 원 안 균등 분포 — 반경에 sqrt를 씌우지 않으면 중심에 몰린다.
                float angle = random() * 360f;
                float radius = Mathf.Sqrt(Mathf.Clamp01(random())) * stageRadius;
                Vector3 candidate = AngleToPosition(stageCenter, angle, radius);

                if (Flat(candidate - playerPosition).magnitude < minPlayerDistance) continue;

                float clearance = MinDistance(occupied, candidate);
                if (clearance < minSpacing) continue;

                if (isVisible != null && isVisible(candidate))
                {
                    // 화면 안 — 폴백 후보로만 담아 둔다. 카메라 전방과 덜 겹치는 쪽이 낫다.
                    float facing = Vector3.Dot(Flat(candidate - viewPosition).normalized, Flat(viewForward).normalized);
                    if (facing < bestFallbackScore) { bestFallbackScore = facing; bestFallback = candidate; }
                    continue;
                }

                if (clearance <= bestHiddenScore) continue;

                bestHiddenScore = clearance;
                bestHidden = candidate;
                foundHidden = true;
            }

            return foundHidden ? bestHidden : bestFallback;
        }

        /// <summary>
        /// <paramref name="desiredDistance"/>에 <b>가장 가까운 거리</b>의 후보 인덱스. 후보가 없으면 -1.
        ///
        /// <para>가장 가까운 적도, 각도 순서도 아니다 — <b>목표 거리에 가장 근접한</b> 적이다.
        /// 목표 거리는 창(음악이 정한다)에서 나오므로, 이 함수가 <b>이동 속도를 일정하게 유지</b>하는 장치다.
        /// 짧은 구간은 근거리 난타로, 긴 구간은 무대 횡단 대시로 자연히 갈린다.</para>
        /// </summary>
        public static int PickTargetByDistance(IReadOnlyList<Vector3> candidates, Vector3 from, float desiredDistance)
        {
            if (candidates == null || candidates.Count == 0) return -1;

            int best = -1;
            float bestError = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                float error = Mathf.Abs(Flat(candidates[i] - from).magnitude - desiredDistance);
                if (error >= bestError) continue;

                bestError = error;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// 회피할 자리. <b>등 뒤 반원 안에서 무대를 벗어나지 않는 방향</b>을 고른다.
        ///
        /// <para><b>정직하게 뒤로 가는 것이 1순위다.</b> 후보를 0°, ±step, ±2step… 순으로(= 등 뒤에서 벌어지는 순서로)
        /// 훑으며 <b>온전한 거리를 갈 수 있는 첫 방향</b>을 그대로 쓴다. 그래서 무대 한복판에서는 언제나 정확히
        /// 뒤로 물러나고, 가장자리에 몰렸을 때만 옆으로 비껴 빠진다.</para>
        ///
        /// <para><see cref="PickSpawnAngle"/>·<see cref="PickStagePosition"/>과 같은 규율 —
        /// <b>"될 때까지 재시도"가 아니라 "가장 나은 후보를 고른다"</b>. 어느 방향으로도 온전히 못 가면
        /// 가장 멀리 갈 수 있는 방향으로 <b>거리를 잘라</b> 돌려준다. 실패하지 않으므로 호출자에 예외 경로가 없다.</para>
        ///
        /// <para><paramref name="maxSpreadDegrees"/>가 90도면 후보는 <b>등 뒤 반원</b>이다 —
        /// 그보다 벌리면 옆이나 앞으로 나가 회피로 안 읽힌다.</para>
        /// </summary>
        /// <param name="position">지금 서 있는 자리.</param>
        /// <param name="back">물러나고 싶은 방향(정규화 전이어도 된다. 보통 -forward).</param>
        public static Vector3 PickRetreatTarget(
            Vector3 position, Vector3 back, Vector3 stageCenter, float stageRadius, float distance,
            float maxSpreadDegrees = 90f, int candidateCount = 7)
        {
            if (distance <= 0f) return position;

            Vector3 baseDir = Flat(back);
            if (baseDir.sqrMagnitude < 1e-6f) return position;
            baseDir.Normalize();

            candidateCount = Mathf.Max(candidateCount, 1);
            float step = maxSpreadDegrees / candidateCount;

            Vector3 bestDir = baseDir;
            float bestTravel = -1f;

            // 0, +step, -step, +2step, -2step … — 등 뒤에서 벌어지는 순서.
            for (int i = 0; i <= candidateCount; i++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    Vector3 dir = Quaternion.Euler(0f, sign * step * i, 0f) * baseDir;
                    float travel = TravelInsideCircle(position, dir, stageCenter, stageRadius, distance);

                    // 온전히 갈 수 있으면 더 벌릴 이유가 없다 — 가장 뒤쪽 후보가 이긴다.
                    if (travel >= distance) return position + dir * distance;

                    if (travel > bestTravel) { bestTravel = travel; bestDir = dir; }

                    if (i == 0) break; // 0도는 부호가 없다
                }
            }

            return position + bestDir * Mathf.Max(bestTravel, 0f);
        }

        /// <summary>
        /// <paramref name="from"/>에서 <paramref name="dir"/>로 갈 때 원 안에 머무를 수 있는 거리
        /// (<paramref name="maxDistance"/>로 클램프). 원 밖으로 나가는 지점까지의 거리를 직선-원 교차로 구한다.
        ///
        /// <para>ponytail: 이미 원 <b>밖</b>에 서 있는데 안쪽으로 향하면 '반대편으로 나가는 지점'을 돌려준다
        /// (관통 거리). 스폰이 언제나 원 안이라 실제로는 안 나오는 상태이고, 필요해지면 근접 교차점을 빼면 된다.</para>
        /// </summary>
        private static float TravelInsideCircle(Vector3 from, Vector3 dir, Vector3 center, float radius, float maxDistance)
        {
            Vector3 f = Flat(from - center);
            float b = Vector3.Dot(f, Flat(dir));
            float disc = b * b - (f.sqrMagnitude - radius * radius);
            if (disc < 0f) return 0f; // 원과 아예 만나지 않는다(밖에서 비껴가는 방향)

            return Mathf.Clamp(-b + Mathf.Sqrt(disc), 0f, maxDistance);
        }

        private static float MinDistance(IReadOnlyList<Vector3> points, Vector3 to)
        {
            if (points == null || points.Count == 0) return float.MaxValue;

            float min = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
                min = Mathf.Min(min, Flat(points[i] - to).magnitude);

            return min;
        }

        /// <summary>배치 판단은 전부 평면 문제다 — 높이 차가 거리에 섞이면 안 된다.</summary>
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

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
