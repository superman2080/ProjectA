using UnityEngine;

namespace EnemySpace
{
    /// <summary>
    /// 씬 뷰 기즈모(에디터 전용). 무대 반경·무리 집결지·결투 거리는 숫자만 봐선 못 정해 그림으로 확인한다.
    ///
    /// <para><b>클래스를 쪼갠 것이 아니라 파일만 쪼갰다</b>(<c>partial</c>) — 무리·예약·처치가 서로의 상태를
    /// 직접 읽으므로 진짜 분리는 성립하지 않는다. 얻는 것은 "한 파일을 스크롤하지 않는다" 하나다.</para>
    /// </summary>
    public partial class EnemyDirector
    {
        // ── 기즈모 (에디터 전용) ─────────────────────────────────────────────────

#if UNITY_EDITOR
        /// <summary>
        /// 반경은 카메라 화각·격자 크기와 같이 봐야 정해진다. 숫자만 봐선 못 정하므로 씬 뷰에 그린다.
        /// <b>편집 중에도 그린다</b> — 플레이 없이 반경을 잡아야 하기 때문.
        /// </summary>
        /// <summary>
        /// 무리 상태. <b>집결지가 고정인지, 몇 명이 모였는지, 누가 아직 걸어오는 중인지</b>를 그린다 —
        /// 집결지가 프레임마다 움직이면 그게 곧 예전의 우르르 이동 버그다.
        /// </summary>
        private void DrawClusterGizmos()
        {
            Vector3 activeCenter = ClusterCenterOf(activeCluster, PlayerPosition);

            Gizmos.color = Color.red;
            DrawCircle(activeCenter, clusterRadius);

            // 집결지 — 무리가 찰 때까지 여기 고정이다.
            Gizmos.color = Color.cyan;
            DrawCircle(stagedCenter, clusterRadius);
            Gizmos.DrawLine(PlayerPosition, stagedCenter);

            // 아직 걸어오는 중인 적: 현재 위치 → 자기 자리.
            Gizmos.color = Color.green;
            foreach (var e in stagedCluster)
            {
                if (e == null) continue;
                Gizmos.DrawLine(e.transform.position, e.Destination);
            }

            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.Label(stagedCenter + Vector3.up * 1.2f,
                $"집결 {stagedCluster.Count}/{clusterSize}   dist={Vector3.Distance(PlayerPosition, stagedCenter):0.0}m\n" +
                $"active {activeCluster.Count}   desired={lastDesiredDistance:0.0}m");

            if (!wanderEnabled) return;

            // 배회 궤도 — 플레이어 주위 standoff 원과, 각 적이 자기 슬롯으로 가는 선.
            Gizmos.color = Color.yellow;
            DrawCircle(PlayerPosition, standoffDistance);

            int count = Mathf.Max(activeCluster.Count, 1);
            for (int i = 0; i < activeCluster.Count; i++)
            {
                var view = activeCluster[i];
                if (view == null) continue;

                bool engaged = view == currentOpponent;
                Gizmos.color = engaged ? Color.red : (view.Wandering ? Color.yellow : Color.grey);

                if (engaged) continue;

                Vector3 slot = EnemyRing.PickOrbitSlot(
                    PlayerPosition, i, count, standoffDistance, orbitPhase,
                    Center, stageRadius, JitterOf(view, orbitJitter));

                Gizmos.DrawLine(view.transform.position, slot);
                Gizmos.DrawWireSphere(slot, 0.2f);

                // 배회를 안 하고 있으면 이유가 뷰 안에 있다 — 라벨로 끌어낸다.
                if (!view.Wandering)
                    UnityEditor.Handles.Label(view.transform.position + Vector3.up * 1.6f, $"정지({view.Current})");
            }
        }

        /// <summary>
        /// 불가침 캡슐(docs/ActorSeparation) — 플레이어 원 · 현재 상대 원 · 둘을 잇는 통로.
        /// <b>통로 이격은 눈으로 봐야 튜닝된다</b>: 반경이 <c>standoffDistance</c>에 가까워지면
        /// 배회 슬롯과 상시 싸우는데, 그건 숫자로는 안 보이고 캡슐이 배회 원을 삼키는 그림으로 보인다.
        /// </summary>
        private void DrawSeparationGizmo()
        {
            if (separationRadius <= 0f) return;

            Vector3 a = PlayerPosition;
            Vector3 b = currentOpponent != null ? currentOpponent.transform.position : a;

            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            DrawCircle(a, separationRadius);

            if (Vector3.ProjectOnPlane(b - a, Vector3.up).sqrMagnitude < 1e-4f) return;

            DrawCircle(b, separationRadius);

            // 통로의 양쪽 벽. 캡슐이 원 둘이 아니라 '이어진 하나'임을 그림으로 못박는다.
            Vector3 side = Vector3.Cross(Vector3.ProjectOnPlane(b - a, Vector3.up).normalized, Vector3.up) * separationRadius;
            Gizmos.DrawLine(a + side, b + side);
            Gizmos.DrawLine(a - side, b - side);
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            Vector3 center = Center;

            // 무대 경계. 적은 이 원 '안'에 흩어진다.
            Gizmos.color = gizmoRingColor;
            DrawCircle(center, stageRadius);

            var faded = gizmoRingColor;
            faded.a *= 0.35f;
            Gizmos.color = faded;
            DrawCircle(center, stageRadius * 0.5f);

            // 플레이어 주변 스폰 금지 반경 — 코앞에 튀어나오는지 눈으로 본다.
            Gizmos.color = gizmoDuelColor;
            DrawCircle(PlayerPosition, minPlayerDistance);

            UnityEditor.Handles.color = gizmoRingColor;
            UnityEditor.Handles.Label(center + Vector3.up * 0.5f,
                $"stage r={stageRadius:0.0}  count={RingCapacity}  spacing={minSpacing:0.0}m\n" +
                $"share={playerShare:0.00}  cruise={cruiseSpeed:0.0}m/s" +
                (clusterEnabled ? $"\ncluster {clusterSize}  r={clusterRadius:0.0}  move={clusterMoveSpeed:0.0}m/s" : ""));

            if (!Application.isPlaying) return;

            DrawSeparationGizmo();

            if (clusterEnabled) DrawClusterGizmos();

            foreach (var e in ring)
            {
                if (e == null) continue;
                Gizmos.color = gizmoRingColor;
                Gizmos.DrawWireCube(e.transform.position, Vector3.one * 0.4f);
            }

            if (currentOpponent != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(currentOpponent.transform.position, Vector3.one * 0.8f);
            }

            // 수렴 계획 — 둘이 어디서 만나기로 했는지. 숫자로는 안 보이는 문제라 그린다.
            if (lastPlan.HasValue)
            {
                var plan = lastPlan.Value;
                Vector3 meet = (plan.PlayerPosition + plan.EnemyPosition) * 0.5f;

                Gizmos.color = new Color(0.4f, 1f, 0.6f);
                Gizmos.DrawWireSphere(plan.PlayerPosition, 0.25f);
                Gizmos.DrawWireSphere(plan.EnemyPosition, 0.25f);
                Gizmos.DrawLine(plan.PlayerPosition, plan.EnemyPosition);

                UnityEditor.Handles.color = new Color(0.4f, 1f, 0.6f);
                UnityEditor.Handles.Label(meet + Vector3.up * 0.6f,
                    $"결투 {Vector3.Distance(plan.PlayerPosition, plan.EnemyPosition):0.00}m  " +
                    $"(도착까지 {plan.ArriveTime - Time.time:+0.00;-0.00;0.00}s)");
            }
        }

        private static void DrawCircle(Vector3 center, float radius, int segments = 48)
        {
            if (radius <= 0f) return;

            Vector3 prev = EnemyRing.AngleToPosition(center, 0f, radius);
            for (int i = 1; i <= segments; i++)
            {
                Vector3 next = EnemyRing.AngleToPosition(center, 360f * i / segments, radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
