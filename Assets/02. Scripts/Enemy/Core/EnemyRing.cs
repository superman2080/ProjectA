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

        // ── 무리 배치 (유동) ──────────────────────────────────────────────────

        /// <summary>
        /// 다음 무리가 놓일 <b>방향</b>. <paramref name="previousDirection"/>이 아직 쓸 만하면 <b>그대로 돌려준다</b>.
        ///
        /// <para><b>방향을 고정하는 것이 요동 대책의 핵심이다.</b> 목표 거리는 창(음악이 정한다)에서 나와
        /// 패턴마다 0.5~2.1초로 4배 흔들린다. 거리만 흔들리면 무리가 "다가왔다 물러났다" 하는 것으로 읽히지만,
        /// 방향까지 매번 다시 뽑으면 무리가 무대를 가로질러 왔다 갔다 해서 <b>목적 없이 서성이는 그림</b>이 된다.
        /// 순간이동이면 중간 결과가 안 보여 상관없지만, 걸어서 옮기면 <b>갱신 한 번이 곧 보이는 동작 하나</b>다.</para>
        ///
        /// <para>방향을 바꾸는 경우는 둘뿐이다 — 그 방향으로 <paramref name="desiredDistance"/>를 갔을 때
        /// 무대를 벗어나거나, 지금 교전 중인 무리(<paramref name="avoidCenter"/>)와 겹칠 때.</para>
        /// </summary>
        public static Vector3 PickClusterDirection(
            Vector3 playerPosition, Vector3 previousDirection, Vector3 stageCenter, float stageRadius,
            float desiredDistance, Vector3 avoidCenter, float avoidRadius,
            System.Func<float> random, int candidateCount = 16)
        {
            if (IsDirectionUsable(previousDirection, playerPosition, stageCenter, stageRadius, desiredDistance, avoidCenter, avoidRadius))
                return Flat(previousDirection).normalized;

            candidateCount = Mathf.Max(candidateCount, 4);

            // 같은 규율 — "될 때까지 재시도"가 아니라 "가장 나은 후보". 전부 부적합해도 실패하지 않는다.
            Vector3 best = Vector3.forward;
            float bestScore = float.MinValue;
            float offset = random != null ? random() * 360f : 0f;

            for (int i = 0; i < candidateCount; i++)
            {
                float angle = offset + 360f * i / candidateCount;
                Vector3 dir = AngleToPosition(Vector3.zero, angle, 1f);

                if (IsDirectionUsable(dir, playerPosition, stageCenter, stageRadius, desiredDistance, avoidCenter, avoidRadius))
                    return dir;

                // 폴백 점수: 교전 중인 무리에서 먼 쪽이 낫다.
                float score = Flat(playerPosition + dir * desiredDistance - avoidCenter).magnitude;
                if (score <= bestScore) continue;

                bestScore = score;
                best = dir;
            }

            return best;
        }

        private static bool IsDirectionUsable(
            Vector3 direction, Vector3 playerPosition, Vector3 stageCenter, float stageRadius,
            float desiredDistance, Vector3 avoidCenter, float avoidRadius)
        {
            Vector3 flat = Flat(direction);
            if (flat.sqrMagnitude < 1e-6f) return false;

            Vector3 point = playerPosition + flat.normalized * desiredDistance;
            if (Flat(point - stageCenter).magnitude > stageRadius) return false;
            if (avoidRadius > 0f && Flat(point - avoidCenter).magnitude < avoidRadius) return false;

            return true;
        }

        /// <summary>
        /// 무리 중심. 방향은 <see cref="PickClusterDirection"/>이 정했으므로 여기서는 <b>거리만 클램프</b>한다.
        ///
        /// <para><b>무대를 벗어나면 거리를 줄이는 쪽으로 자른다.</b> 늘리면 플레이어가 창 안에 도달하지 못해
        /// 칼이 닿는 순간에 아직 달리는 중이 된다 — 짧게 자르면 일찍 도착해 서 있을 뿐이라 훨씬 낫다.</para>
        /// </summary>
        public static Vector3 PickClusterCenter(
            Vector3 playerPosition, Vector3 direction, float desiredDistance,
            Vector3 stageCenter, float stageRadius, float minPlayerDistance)
        {
            Vector3 flat = Flat(direction);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            flat.Normalize();

            float maxInside = TravelInsideCircle(playerPosition, flat, stageCenter, stageRadius, desiredDistance);
            float distance = Mathf.Min(desiredDistance, maxInside);
            distance = Mathf.Max(distance, minPlayerDistance);

            Vector3 center = playerPosition + flat * distance;
            center.y = playerPosition.y;
            return center;
        }

        /// <summary>
        /// 무리 안 <paramref name="index"/>번째 자리. <b>결정적이어야 한다</b> —
        /// 재배치마다 대형이 바뀌면 같은 무리로 안 읽히고, 무리가 통째로 옮겨간 게 아니라 흩어졌다 모인 것처럼 보인다.
        ///
        /// <para>황금각 나선이라 인원수가 바뀌어도 고르게 퍼지고, 중심 한 명 + 바깥 원이라는 뭉친 대형이 나온다.
        /// <paramref name="minSpacing"/>은 반경의 하한으로만 쓴다 — 서로 밀어내면 무리가 흐트러진다(그게 원래 문제였다).</para>
        /// </summary>
        public static Vector3 PlaceInCluster(Vector3 center, int index, int count, float clusterRadius, float minSpacing)
        {
            if (count <= 1) return center;

            index = Mathf.Clamp(index, 0, count - 1);

            const float GoldenAngle = 137.507764f;
            float t = count <= 1 ? 0f : (float)index / (count - 1);
            float radius = Mathf.Sqrt(t) * clusterRadius;

            // 이웃이 minSpacing보다 가까워지지 않도록 반경의 하한을 준다(0번은 중심에 남긴다).
            if (index > 0) radius = Mathf.Max(radius, minSpacing * 0.5f);

            return AngleToPosition(center, GoldenAngle * index, radius);
        }

        /// <summary>
        /// 스폰 지점. <b>자기 자리에서 가장 가까운 화면 밖 지점</b>을 고른다.
        ///
        /// <para><b>왜 무대 가장자리가 아닌가</b>: 가장자리에서 걸어오게 하면 첫 이동만 무대 횡단(최대 16m)이 되어
        /// 재배치 예산으로는 감당할 수 없다. 자리 근처에 내면 초기 이동이 0에 가까워지고, 실제 이동은 재배치만 남는다.</para>
        ///
        /// <para><b>여기가 이 시스템에서 카메라를 보는 유일한 곳이다.</b> 재배치(연속 이동)는 보여도 무해하지만
        /// 스폰은 팝인이라 즉시 티가 난다. 실패 양상이 달라서 판정을 여기만 남겼다.</para>
        ///
        /// <para>반경을 0부터 키워 가며 훑고 <b>처음 찾은 화면 밖 후보를 즉시 반환</b>한다 —
        /// 가장 가까운 것이 곧 최선이라 더 볼 이유가 없다. 자리 자체가 화면 밖이면 오프셋 0이다.</para>
        /// </summary>
        public static Vector3 PickSpawnNearCluster(
            Vector3 slot, Vector3 stageCenter, float stageRadius,
            Vector3 playerPosition, float minPlayerDistance,
            Vector3 viewPosition, Vector3 viewForward,
            System.Func<Vector3, bool> isVisible, float maxOffset, int ringCount = 8, int radialSteps = 4)
        {
            if (isVisible == null || !isVisible(slot)) return slot;

            ringCount = Mathf.Max(ringCount, 3);
            radialSteps = Mathf.Max(radialSteps, 1);

            Vector3 bestFallback = slot;
            float bestFacing = float.MaxValue;

            for (int step = 1; step <= radialSteps; step++)
            {
                float radius = maxOffset * step / radialSteps;

                for (int i = 0; i < ringCount; i++)
                {
                    Vector3 candidate = AngleToPosition(slot, 360f * i / ringCount, radius);
                    candidate.y = slot.y;

                    if (Flat(candidate - stageCenter).magnitude > stageRadius) continue;
                    if (Flat(candidate - playerPosition).magnitude < minPlayerDistance) continue;

                    if (!isVisible(candidate)) return candidate; // 가까운 것부터 훑으므로 첫 발견이 최선

                    float facing = Vector3.Dot(Flat(candidate - viewPosition).normalized, Flat(viewForward).normalized);
                    if (facing >= bestFacing) continue;

                    bestFacing = facing;
                    bestFallback = candidate;
                }
            }

            return bestFallback;
        }

        // ── 배회 궤도 (docs/EnemyIdleWander) ──────────────────────────────────

        /// <summary>
        /// 플레이어 주위 배회 궤도의 <paramref name="index"/>번째 자리.
        ///
        /// <para><b>각도를 균등 분할하는 것이 곧 겹침 방지다.</b> 서로 밀어내는 반발 계산을 두지 않는다 —
        /// 그게 원래 "적이 흩어져서 정신없다"의 원인이었다(<see cref="PickStagePosition"/>의 clearance 최대화).
        /// 균등 분할은 결정적이고, 인덱스가 고정이면 적끼리 자리를 바꾸며 서로를 가로지르는 일도 없다.</para>
        ///
        /// <para><paramref name="phase"/>가 시간에 따라 도는 값이라 궤도 전체가 천천히 회전한다.
        /// <paramref name="jitter"/>는 개체별 고정 오프셋 — 없으면 넷이 정확한 원 위에 서서 인공적이다.</para>
        ///
        /// <para>결과는 무대 안으로 클램프된다. 플레이어가 가장자리에 붙어 있으면 궤도의 바깥쪽 절반이
        /// 무대 밖이므로, 그 자리는 <b>거리를 줄여</b> 안으로 당긴다 — 각도를 바꾸면 자리가 뒤섞인다.</para>
        /// </summary>
        public static Vector3 PickOrbitSlot(
            Vector3 playerPosition, int index, int count, float standoffDistance, float phase,
            Vector3 stageCenter, float stageRadius, float jitter = 0f)
        {
            count = Mathf.Max(count, 1);
            index = Mathf.Clamp(index, 0, count - 1);

            float angle = phase + 360f * index / count + jitter;
            Vector3 direction = AngleToPosition(Vector3.zero, angle, 1f);

            float distance = Mathf.Max(standoffDistance, 0.1f);
            distance = Mathf.Min(distance, TravelInsideCircle(playerPosition, direction, stageCenter, stageRadius, distance));

            Vector3 slot = playerPosition + direction * distance;
            slot.y = playerPosition.y;
            return slot;
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

        // ── 불가침 캡슐 (docs/ActorSeparation) ───────────────────────────────

        /// <summary>
        /// <paramref name="position"/>이 선분 <paramref name="a"/>–<paramref name="b"/>의 <b>캡슐</b>
        /// (두 끝 원 + 잇는 통로) 안에 들어와 있으면 밖으로 밀어낼 <b>평면 변위</b>를 돌려준다.
        /// 안 겹치면 <see cref="Vector3.zero"/>.
        ///
        /// <para><b>원은 특수해다</b> — <c>a == b</c>면 그대로 점 기준 원이 된다. 상대가 없는 구간을 위해
        /// 분기를 따로 두지 않는다.</para>
        ///
        /// <para><b>⚠ 밀어내는 방향은 선분에 수직이다.</b> 통로 한가운데 선 적을 축 방향으로 밀면
        /// 통로를 <b>따라</b> 미끄러질 뿐 밖으로 안 나간다. 수직이라야 옆으로 비켜선다 —
        /// 화면에서 "군중이 갈라진다"가 되는 것은 이쪽뿐이다.</para>
        ///
        /// <para><b>⚠ <c>DuelGap.ResolveAxis</c>를 쓰지 않는다</b> — 그 함수는 부호가 뒤집히면 직전 축을 유지한다
        /// (커브 관통 전용 규칙). 이격은 언제나 <b>지금 방향</b>으로 밀어야 하므로 그 규칙이 해롭다.</para>
        /// </summary>
        public static Vector3 SeparationPush(Vector3 position, Vector3 a, Vector3 b, float radius)
        {
            // 전부 평면에서 푼다 — 높이는 이격의 관심사가 아니다.
            position = Flat(position);
            a = Flat(a);
            b = Flat(b);

            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;

            // a == b면 t가 0이 되어 점 기준 원으로 자연히 떨어진다.
            float t = lenSq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / lenSq) : 0f;
            Vector3 closest = a + ab * t;

            Vector3 flat = position - closest;
            float d = flat.magnitude;
            if (d >= radius) return Vector3.zero;

            // 선분 위에 정확히 올라선 경우 밀 방향이 없다 — 선분에 수직인 쪽으로 뺀다.
            // ⚠ 플레이어 forward를 쓰면 안 된다: 그건 선분 방향과 거의 같아 통로를 따라 미끄러진다.
            Vector3 axis = d > 1e-4f
                ? flat / d
                : (lenSq > 1e-6f ? Vector3.Cross(ab.normalized, Vector3.up) : Vector3.right);

            return axis * (radius - d);
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
