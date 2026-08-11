using System.Collections.Generic;
using PatternSpace;
using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 베이는 표적의 <b>유일한 관리 지점</b>. <see cref="PatternHandler"/>의 이벤트만 구독해
    /// 표적을 예약·스폰·확정·회수한다. 판정 파이프라인에는 일절 개입하지 않는다(연출 전용).
    ///
    /// <para><b>임팩트 시각은 Deadline이다.</b> 마지막 노드를 goodWindow 안에 늦게 눌러도 Good 성공이므로,
    /// LastNodeTime에 도착시키면 정상적인 늦은 입력이 실패로 연출된다. 도착을 Deadline에 맞추면
    /// 표적이 다가오는 내내가 판정 구간이 되고, 닿는 순간 성패는 이미 확정되어 있다.</para>
    ///
    /// <para><b>접근시간은 클램프된다.</b> 표적은 패턴이 큐에 들어와야 존재를 알 수 있어 첫 노드보다 먼저 나타날 수 없다.
    /// 따라서 상한은 StartTime~impactTime 구간이며, 그보다 긴 approachDuration은 이 구간으로 잘린다.</para>
    ///
    /// <para><b>등장 위치를 authoring하고 속도는 파생시킨다.</b> 도착 시각이 Deadline으로 고정이므로
    /// '거리 = 속도 × 시간'에서 둘 중 하나만 정할 수 있다. 여기서는 <see cref="spawnAnchor"/>로 거리를 잡아
    /// 표적이 언제나 같은 자리에서 등장하게 하고(화면 구도 일정), 속도는 거리÷접근시간으로 따라오게 둔다.
    /// 그래서 <b>패턴이 짧을수록 표적이 빨리 날아온다</b> — 의도된 결과다.</para>
    /// </summary>
    public class SliceTargetDirector : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("칼이 지나가는 월드 지점. 표적은 여기로 다가와 갈라진다.")]
        [SerializeField] private Transform impactAnchor;

        [Tooltip("표적이 등장하는 월드 지점. 비우면 임팩트 지점에서 +Z로 fallbackSpawnDistance만큼 떨어진 곳을 쓴다.")]
        [SerializeField] private Transform spawnAnchor;

        [Header("Approach")]
        [Tooltip("표적이 판정 지점까지 오는 데 쓰는 희망 시간(초). 패턴이 짧으면 그 구간 전체로 클램프된다.")]
        [SerializeField] private float approachDuration = 1.5f;

        [Tooltip("spawnAnchor가 비었을 때만 쓰는 기본 등장 거리(+Z 방향, 월드 단위).")]
        [SerializeField] private float fallbackSpawnDistance = 18f;

        [Header("Scatter")]
        [SerializeField] private float scatterSpeed = 3f;
        [SerializeField] private float scatterJitter = 0.8f;
        [Tooltip("조각 회전 속도의 범위(도/초).")]
        [SerializeField] private float scatterSpin = 180f;

        [Tooltip("조각에 적용할 레이어. 조각끼리의 충돌은 자동으로 꺼진다(바닥하고만 부딪힌다).")]
        [SerializeField] private int pieceLayer;

        [Header("Cleanup")]
        [Tooltip("절단/소멸 후 조각을 회수하기까지의 시간(초).")]
        [SerializeField] private float debrisLifetime = 2f;

        [Tooltip("동시 활성 조각 상한. 넘으면 가장 오래된 표적부터 회수한다.")]
        [SerializeField] private int maxActivePieces = 64;

        [Header("Pooling")]
        [Tooltip("씬 시작 시 미리 채워 둘 세트. 첫 히치를 줄이는 최적화일 뿐이라 비워도 동작한다.")]
        [SerializeField] private SliceSet[] prewarmSets;

        [Header("Effects (optional)")]
        [Tooltip("절단 성공 시 임팩트 지점에 재생할 이펙트. 비우면 무연출.")]
        [SerializeField] private GameObject sliceEffectPrefab;

        [Tooltip("실패(충돌) 시 재생할 이펙트. 비우면 무연출.")]
        [SerializeField] private GameObject crushEffectPrefab;

        [Header("Gizmos")]
        [Tooltip("씬 뷰에 스폰 지점·임팩트 지점·접근 경로를 그린다. 에디터 전용이라 빌드에는 영향이 없다.")]
        [SerializeField] private bool drawGizmos = true;

        [SerializeField] private Color gizmoSpawnColor = new Color(0.3f, 0.8f, 1f);
        [SerializeField] private Color gizmoImpactColor = new Color(1f, 0.35f, 0.2f);
        [SerializeField] private float gizmoRadius = 0.25f;

        /// <summary>성패 확정 상태. Deadline 도착 시점엔 Pending이 아니어야 정상이다.</summary>
        private enum Outcome { Pending, Success, Failure }

        private sealed class Reservation
        {
            public int token;          // 패턴 인스턴스 단위 토큰(같은 템플릿이 겹쳐도 구분된다)
            public SliceSet set;
            public Vector3 spawnPos;
            public Vector3 impactPos;
            public float spawnTime;
            public float impactTime;
            public Outcome outcome;
            public SliceTargetView view;   // 스폰 전에는 null
        }

        private readonly List<Reservation> reservations = new List<Reservation>();
        private readonly List<Reservation> active = new List<Reservation>();

        // 프리팹별 인스턴스 풀. Pool(PoolKey 단일 매핑)은 표적 프리팹 수 증가에 맞지 않는다.
        private PrefabPool pool;

        void Awake()
        {
            var rootGo = new GameObject("[SliceTargetPool]");
            rootGo.transform.SetParent(transform, false);
            rootGo.SetActive(false);
            pool = new PrefabPool(rootGo.transform);
        }

        void Start()
        {
            Prewarm();
        }

        // ── 공개 API (EnemyDirector가 유일한 호출자) ────────────────────────────
        //
        // 이 클래스는 더 이상 PatternHandler 이벤트를 직접 구독하지 않는다.
        // 예약 소스를 한 곳(EnemyDirector)으로 모아야 토큰 발급·성패 확정이 한 줄로 흐른다 —
        // 두 디렉터가 같은 이벤트를 각자 구독하면 구독 순서에 따라 결과가 달라진다.

        /// <summary>
        /// 투사체 하나를 예약한다. 도착 시각은 <b>판정이 끝나는 순간</b>이어야 닿을 때 성패가 이미 확정되어 있다.
        /// 접근시간은 패턴이 살아 있는 구간으로 클램프된다 — 투사체는 첫 노드보다 먼저 나타날 수 없다.
        /// </summary>
        /// <param name="token">패턴 인스턴스 토큰. <see cref="Resolve"/>가 같은 값으로 성패를 확정한다.</param>
        /// <param name="spawnOverride">발사 지점. 지정하면 <c>spawnAnchor</c> 대신 이 위치에서 날아온다(쏘는 적의 링 위치).</param>
        public void Reserve(int token, SliceSet set, float startTime, float impactTime, Vector2 offset, Vector3? spawnOverride = null)
        {
            if (set == null) return; // 무연출

            // 휴머노이드(스킨드) 세트는 시체 프리팹이 산출물이라 조각 배열이 비어 있다.
            // IsUsable은 그쪽 기준으로 true를 돌려주므로 여기서 따로 거른다 —
            // 안 거르면 원본만 날아와 조각 없이 사라지는, 원인 찾기 어려운 무연출이 된다.
            if (set.Skinned)
            {
                Debug.LogWarning(
                    $"[SliceTargetDirector] '{set.name}'은 휴머노이드(시체) 세트라 투사체로 쓸 수 없습니다. " +
                    "채보의 '원거리 오브젝트'에는 일반 메쉬 모드로 구운 세트를 지정하세요.", set);
                return;
            }

            if (!set.IsUsable)
            {
                Debug.LogWarning($"[SliceTargetDirector] '{set.name}'이 사용 가능한 상태가 아닙니다(원본/조각 배열 확인). 건너뜁니다.", set);
                return;
            }

            float approach = Mathf.Min(approachDuration, impactTime - startTime);
            approach = Mathf.Max(approach, 0.01f);

            // 배치 오프셋은 스폰·임팩트 양쪽에 똑같이 실린다 — 경로가 기울지 않고 나란히 평행이동하도록.
            Vector3 anchor = impactAnchor != null ? impactAnchor.position : transform.position;
            var offset3 = new Vector3(offset.x, offset.y, 0f);

            reservations.Add(new Reservation
            {
                token = token,
                set = set,
                spawnPos = (spawnOverride ?? SpawnBase(anchor)) + offset3,
                impactPos = anchor + offset3,
                spawnTime = impactTime - approach,
                impactTime = impactTime,
                outcome = Outcome.Pending
            });
        }

        /// <summary>예약/활성 투사체의 성패를 확정한다. 이미 확정된 것은 건드리지 않는다.</summary>
        public void Resolve(int token, bool success)
        {
            SetOutcome(token, success ? Outcome.Success : Outcome.Failure);
        }

        /// <summary>곡 중단 등으로 전부 정리한다.</summary>
        public void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--) Recycle(active[i]);
            active.Clear();
            reservations.Clear();
        }

        /// <summary>표적이 등장하는 기준 지점. 앵커가 없으면 임팩트 지점에서 +Z로 물러난 곳을 쓴다.</summary>
        private Vector3 SpawnBase(Vector3 anchor)
        {
            return spawnAnchor != null
                ? spawnAnchor.position
                : anchor + Vector3.forward * fallbackSpawnDistance;
        }

        private void SetOutcome(int token, Outcome outcome)
        {
            foreach (var r in reservations)
                if (r.token == token && r.outcome == Outcome.Pending) r.outcome = outcome;

            foreach (var r in active)
                if (r.token == token && r.outcome == Outcome.Pending) r.outcome = outcome;
        }

        // ── 루프 ────────────────────────────────────────────────────────────────

        void Update()
        {
            float now = Time.time;

            // 1) 예약 스폰
            for (int i = reservations.Count - 1; i >= 0; i--)
            {
                var r = reservations[i];
                if (now < r.spawnTime) continue;

                reservations.RemoveAt(i);
                Spawn(r);
                active.Add(r);
            }

            // 2) 임팩트 처리
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var r = active[i];
                if (r.view == null || r.view.Resolved) continue;
                if (now < r.impactTime) continue;

                // Deadline 프레임에 Director가 PatternHandler보다 먼저 돌면 확정이 아직 없을 수 있다.
                // 실패로 넘기지 말고 도착 상태로 한 프레임 기다린다(이미 도착해 있어 눈에 띄지 않는다).
                if (r.outcome == Outcome.Pending) continue;

                if (r.outcome == Outcome.Success) SliceTarget(r);
                else CrushTarget(r);
            }

            // 3) 수명 만료 회수
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var r = active[i];
                if (r.view == null) { active.RemoveAt(i); continue; }
                // 수명이 다했거나, 조각이 전부 잠들었으면(바닥에 눕고 멈춤) 이르게 회수한다.
                if (!r.view.Resolved) continue;
                if (r.view.TimeSinceResolved < debrisLifetime && !r.view.AllPiecesSettled) continue;

                Recycle(r);
                active.RemoveAt(i);
            }

            EnforcePieceBudget();
        }

        /// <summary>동시 활성 조각이 상한을 넘으면 가장 오래된 것부터 회수한다.</summary>
        private void EnforcePieceBudget()
        {
            int total = 0;
            foreach (var r in active)
                if (r.view != null && r.view.Resolved) total += r.view.Pieces.Count;

            if (total <= maxActivePieces) return;

            for (int i = 0; i < active.Count && total > maxActivePieces; i++)
            {
                var r = active[i];
                if (r.view == null || !r.view.Resolved) continue;

                total -= r.view.Pieces.Count;
                Recycle(r);
                active.RemoveAt(i);
                i--;
            }
        }

        // ── 스폰 / 확정 / 회수 ───────────────────────────────────────────────────

        private void Spawn(Reservation r)
        {
            var viewGo = new GameObject($"SliceTarget_{r.set.name}");
            viewGo.transform.SetParent(transform, false);

            var view = viewGo.AddComponent<SliceTargetView>();
            var original = pool.Rent(r.set.OriginalPrefab, r.set.MaxPoolSize);

            view.Setup(r.set, original, r.spawnPos, r.impactPos, r.spawnTime, r.impactTime);
            r.view = view;
        }

        private void SliceTarget(Reservation r)
        {
            var set = r.set;
            var spawned = new List<SlicePiece>(set.PieceCount);

            for (int i = 0; i < set.PieceCount; i++)
            {
                var go = pool.Rent(set.PiecePrefabs[i], set.MaxPoolSize);
                if (go == null) continue;

                var piece = go.GetComponent<SlicePiece>();
                if (piece == null) piece = go.AddComponent<SlicePiece>();
                spawned.Add(piece);
            }

            r.view.Slice(spawned, scatterSpeed, scatterJitter, scatterSpin, r.token, pieceLayer);
            PlayEffect(sliceEffectPrefab, r.impactPos);
        }

        private void CrushTarget(Reservation r)
        {
            r.view.Crush();
            PlayEffect(crushEffectPrefab, r.impactPos);
        }

        private void PlayEffect(GameObject prefab, Vector3 position)
        {
            if (prefab == null) return; // 비우면 무연출
            Instantiate(prefab, position, Quaternion.identity);
        }

        private void Recycle(Reservation r)
        {
            if (r.view == null) return;

            foreach (var piece in r.view.Pieces)
            {
                if (piece == null) continue;
                piece.ResetState();
                pool.Release(piece.gameObject);
            }

            var original = r.view.DetachOriginal();
            if (original != null) pool.Release(original);

            Destroy(r.view.gameObject);
            r.view = null;
        }

        // ── 프리웜 ──────────────────────────────────────────────────────────────

        private void Prewarm()
        {
            if (prewarmSets == null) return;

            foreach (var set in prewarmSets)
            {
                if (set == null || !set.IsUsable) continue;

                pool.Prewarm(set.OriginalPrefab, set.InitialPoolSize, set.MaxPoolSize);
                foreach (var prefab in set.PiecePrefabs)
                    pool.Prewarm(prefab, set.InitialPoolSize, set.MaxPoolSize);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 편집 중에는 <b>기준 경로</b>(스폰 지점 → 임팩트 지점)를 그린다.
        /// 실제 표적은 패턴의 sliceTargetOffset/sliceTargetImpactOffset만큼 이 선에서 나란히 어긋나고,
        /// 접근시간이 패턴 길이에 따라 클램프되면 <b>속도만</b> 빨라진다(경로 자체는 이 선 그대로다).
        /// </summary>
        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            Vector3 anchor = impactAnchor != null ? impactAnchor.position : transform.position;
            Vector3 spawn = SpawnBase(anchor);
            float distance = Vector3.Distance(spawn, anchor);

            DrawApproach(spawn, anchor);

            UnityEditor.Handles.color = gizmoImpactColor;
            UnityEditor.Handles.Label(anchor + Vector3.up * (gizmoRadius * 1.5f),
                $"impact ({anchor.x:0.00}, {anchor.y:0.00}, {anchor.z:0.00})");
            UnityEditor.Handles.color = gizmoSpawnColor;
            UnityEditor.Handles.Label(spawn + Vector3.up * (gizmoRadius * 1.5f),
                $"spawn  {distance:0.0}u  →  {distance / Mathf.Max(approachDuration, 0.01f):0.0}u/s @ {approachDuration:0.00}s"
                + (spawnAnchor == null ? "  (fallback)" : ""));

            if (!Application.isPlaying) return;

            // 런타임 — 스폰을 기다리는 예약과 살아 있는 표적의 '실제' 경로.
            foreach (var r in reservations) DrawApproach(r.spawnPos, r.impactPos);

            foreach (var r in active)
            {
                if (r.view == null) continue;

                DrawApproach(r.spawnPos, r.impactPos);

                // 표적의 현재 위치와 성패 확정 상태(노랑=미확정 / 초록=성공 / 빨강=실패).
                Gizmos.color = OutcomeColor(r.outcome);
                Gizmos.DrawWireCube(r.view.transform.position, Vector3.one * (gizmoRadius * 2f));
            }
        }

        private void DrawApproach(Vector3 spawn, Vector3 impact)
        {
            Gizmos.color = gizmoSpawnColor;
            Gizmos.DrawWireSphere(spawn, gizmoRadius);
            Gizmos.DrawLine(spawn, impact);

            Gizmos.color = gizmoImpactColor;
            Gizmos.DrawWireSphere(impact, gizmoRadius);

            // 임팩트 지점의 XY 십자 — 칼 궤적의 높이·좌우를 맞추는 기준선.
            float arm = gizmoRadius * 3f;
            Gizmos.DrawLine(impact + Vector3.left * arm, impact + Vector3.right * arm);
            Gizmos.DrawLine(impact + Vector3.down * arm, impact + Vector3.up * arm);
        }

        private Color OutcomeColor(Outcome outcome)
        {
            switch (outcome)
            {
                case Outcome.Success: return Color.green;
                case Outcome.Failure: return Color.red;
                default: return Color.yellow;
            }
        }
#endif
    }

}
