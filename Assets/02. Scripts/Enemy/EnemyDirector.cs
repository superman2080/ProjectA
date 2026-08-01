using System;
using System.Collections.Generic;
using PatternSpace;
using SliceSpace;
using UnityEngine;

namespace EnemySpace
{
    /// <summary>
    /// 무쌍 전투의 <b>유일한 관리 지점</b>. <see cref="PatternHandler"/>의 이벤트만 구독해 적을 배치·배정·확정·회수한다.
    /// 판정 파이프라인에는 일절 개입하지 않는다(연출 전용).
    ///
    /// <para><b>임팩트 시각은 Deadline이다.</b> 마지막 노드를 goodWindow 안에 늦게 눌러도 성공이므로,
    /// LastNodeTime에 맞추면 정상적인 늦은 입력이 실패로 연출된다. 플레이어 칼·표적 절단과 같은 식을 쓴다.</para>
    ///
    /// <para><b>패턴 인스턴스 ↔ 적 매칭은 토큰 FIFO다.</b> 이벤트 페이로드에 인스턴스 ID가 없기 때문 —
    /// 판정 대상은 언제나 선두 하나이고 완료도 순서대로 일어나므로 안전하다. cue도 같은 규율로
    /// <c>ChartPlayer</c>가 <c>SetPattern</c> 직전에 밀어 넣는다.</para>
    /// </summary>
    public class EnemyDirector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PatternHandler handler;

        [Tooltip("아레나 중앙(플레이어 위치). 비우면 이 오브젝트의 위치.")]
        [SerializeField] private Transform arenaCenter;

        [Tooltip("칼이 만나는 지점. 플레이어의 자식이어야 한다 — 이게 곧 결투 지점이다.")]
        [SerializeField] private Transform duelAnchor;

        [Tooltip("시야 기준 카메라. 추가 스폰을 등 뒤에 배치하는 데 쓴다. 비우면 Camera.main.")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("투사체 연출. 비우면 원거리 오브젝트 없음.")]
        [SerializeField] private SliceTargetDirector projectileDirector;

        [Header("Ring")]
        [SerializeField] private float ringRadius = 6f;
        [Tooltip("링 반경의 ± 편차. 배치에 다양성을 준다.")]
        [SerializeField] private float radiusJitter = 1f;
        [Tooltip("적끼리 유지할 최소 각 간격(도).")]
        [SerializeField] private float minAngleGap = 35f;
        [Tooltip("링에 유지할 적 수. 기즈모를 보며 화각에 맞춰 잡는다.")]
        [SerializeField] private int ringCount = 6;
        [Tooltip("등장 시작 거리 배율. 링보다 바깥에서 걸어 들어온다(팝인 방지).")]
        [SerializeField] private float entryDistanceMul = 2.2f;
        [SerializeField] private float entryDuration = 2f;

        [Header("Roster")]
        [Tooltip("등장시킬 적 종류. 여러 종을 넣으면 같은 모델 반복이 티 나지 않는다.")]
        [SerializeField] private EnemyDefinition[] rosterPool;

        [Header("Duel")]
        [Tooltip("리액션 후 링으로 돌아가는 시간(초).")]
        [SerializeField] private float returnDuration = 0.5f;
        [Tooltip("적 공격 클립의 자동 배속 상한. 넘으면 정렬이 깨지므로 경고가 뜬다.")]
        [SerializeField] private float maxAttackSpeed = 2.5f;

        [Tooltip("클립 시작까지 이만큼의 여유가 없으면 수렴을 생략하고 적만 붙는다(플레이어는 제자리).\n" +
                 "순간이동처럼 미끄러지는 것보다 낫고, 어느 쪽이든 간격은 항상 지켜진다.")]
        [SerializeField] private float minConvergeTime = 0.25f;

        [Tooltip("적이 걸어서 좁힐 수 있는 속도(m/s). 승격 후 이 속도로 창 안에 못 닿을 거리면 대기석이 미리 들어온다.")]
        [SerializeField] private float approachSpeed = 3f;
        [Tooltip("대기석이 미리 들어와 서는 반경(m). 아레나 중심 기준이며, 자기 링 방향은 유지한 채 좁혀 든다.")]
        [SerializeField] private float stagingDistance = 2f;
        [Tooltip("상대 배정 후 임팩트까지 실제로 주어지는 시간(초). 대기석을 미리 들여보낼지 판단하는 기준이다.\n" +
                 "채보의 최소 엔트리 간격에 맞춰 보수적으로 잡는다 — 넉넉히 잡으면 적이 못 닿는다.")]
        [SerializeField] private float bindToImpactWindow = 0.45f;

        [Tooltip("실패해서 회피할 때 뒤로 물러나는 거리(m). 링까지 돌아가지 않는다 — 교전이 이어지기 때문.")]
        [SerializeField] private float failRetreatDistance = 1.5f;
        [Tooltip("후퇴에 걸리는 시간(초). 이 시간이 지나야 다시 접근을 시작한다.")]
        [SerializeField] private float retreatDuration = 0.25f;

        [Header("Death")]
        [SerializeField] private float scatterSpeed = 2.5f;
        [SerializeField] private float scatterSpin = 180f;
        [Tooltip("조각 레이어. 조각끼리의 충돌은 자동으로 꺼진다.")]
        [SerializeField] private int pieceLayer;
        [Tooltip("잔해를 회수하기까지의 시간(초).")]
        [SerializeField] private float debrisLifetime = 4f;
        [Tooltip("동시 활성 조각 상한(정적 프록시 경로). 넘으면 오래된 것부터 회수한다.")]
        [SerializeField] private int maxActivePieces = 96;

        [Tooltip("동시 활성 시체 상한. 래그돌 비용은 조각 수가 아니라 스켈레톤 수에 비례하므로 조각 상한과 따로 잡는다.")]
        [SerializeField] private int maxActiveCorpses = 6;

        [Tooltip("루트 조각을 스킨드로 남겨 래그돌에 넘긴다. 래그돌이 실제로 붙어 있을 때만 켤 것 —\n" +
                 "물리가 없으면 그 조각만 공중에 매달린 채 남는다(docs/EnemyRagdoll/ 후속 플랜).")]
        [SerializeField] private bool keepRootSkinnedForRagdoll;

        [Header("Dissolve")]
        [Tooltip("곡이 끝날 때 남은 적이 사라지는 시간(초).")]
        [SerializeField] private float dissolveDuration = 1.2f;

#if UNITY_EDITOR
        [Header("Debug (Editor Only)")]
        [Tooltip("채보 없이 패턴을 투입했을 때(PatternHandler의 Debug: Set Test Pattern) 쓸 전투 지시.\n" +
                 "정상 재생에서는 ChartPlayer가 항상 cue를 밀어 넣으므로 이 값은 쓰이지 않는다.\n" +
                 "killOnSuccess를 켜면 채보 없이도 처치·절단을 시험할 수 있다.")]
        [SerializeField] private EnemyCue debugCue = new EnemyCue();
#endif

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color gizmoRingColor = new Color(0.3f, 0.8f, 1f);
        [SerializeField] private Color gizmoDuelColor = new Color(1f, 0.35f, 0.2f);
        [Tooltip("플레이어가 중앙에서 벗어날 수 있는 최대 거리(리시). 수렴 계획을 여기로 클램프한다 — " +
                 "안 걸면 교전마다 중점으로 끌려가 링 쪽으로 표류한다.")]
        [SerializeField] private float maxOffset = 1.5f;

        /// <summary>교전 상대가 바뀌는 순간. 카메라 재프레이밍·UI·SFX가 본체 수정 없이 붙는다.</summary>
        public event Action<EnemyView, EnemyView> OnOpponentChanged;

        /// <summary>적이 처치된 순간. 이펙트/카메라 큐가 구독한다.</summary>
        public event Action<EnemyView> OnEnemyKilled;

        /// <summary>
        /// 이번 교전에서 <b>둘이 어디서 만날지</b>. 두 위치를 동시에 아는 곳이 여기뿐이라 계산도 여기서 한다.
        ///
        /// <para><c>PlayerCombatMover</c>가 구독해 자기 몫만 움직인다 —
        /// <b>디렉터는 플레이어를 모른다</b>(계층을 뒤집지 않는다).</para>
        /// </summary>
        public readonly struct DuelPlan
        {
            public readonly Vector3 PlayerPosition;
            public readonly Vector3 EnemyPosition;
            public readonly float ArriveTime;   // 둘 다 이 시각까지 도착해야 한다(= 클립 시작)

            public DuelPlan(Vector3 player, Vector3 enemy, float arriveTime)
            {
                PlayerPosition = player;
                EnemyPosition = enemy;
                ArriveTime = arriveTime;
            }
        }

        /// <summary>결투 배치가 정해진 순간. 플레이어 이동이 이것만 구독한다.</summary>
        public event Action<DuelPlan> OnDuelScheduled;

        /// <summary>기즈모용 — 지금 살아 있는 계획. 숫자로는 안 보이는 문제라 씬 뷰에 그린다.</summary>
        private DuelPlan? lastPlan;

        /// <summary>
        /// 지금 판정 대상 패턴에서 누가 휘두르는가. 예약이 없으면 Player(적 무방비)로 본다.
        /// <b>출처는 패턴이다</b> — 채보가 아니라(같은 패턴이 엔트리마다 다른 역할을 가질 수 없다).
        /// </summary>
        public Attacker CurrentAttacker
        {
            get
            {
                if (pendingTokens.Count == 0) return Attacker.Player;
                int token = pendingTokens.Peek();

                foreach (var r in reservations)
                {
                    if (r.token == token) return AttackerOf(r.template);
                }

                return Attacker.Player;
            }
        }

        /// <summary>패턴의 역할. 템플릿이 없는 디버그 경로는 Player(적 무방비)로 본다.</summary>
        private static Attacker AttackerOf(Pattern template) =>
            template != null ? template.Attacker : Attacker.Player;

        private sealed class Reservation
        {
            public int token;
            public EnemyCue cue;
            public Pattern template;   // 패턴 전용 SliceSet 조회용
            public EnemyView opponent;
            public float impactTime;
            public bool resolved;

            /// <summary>상대가 배정됐는가. <b>큐 투입 시점에는 아직 모른다</b> — 이전 패턴의 성패가 상대를 정하기 때문.</summary>
            public bool bound;

            // 배정 시점에 필요한 값들. 접수 때 챙겨 두고 배정 때 꺼내 쓴다.
            // arriveTime은 여기 두지 않는다 — ResolveScheduleStart가 '지금'에 의존하므로 배정 시점에 계산해야 한다.
            public ClipAlignment attack;
            public float startTime;
        }

        private sealed class Debris
        {
            public List<SlicePiece> pieces;
            public EnemyView view;
            public float time;
        }

        /// <summary>
        /// 처치 확정 후 <b>임팩트 시각을 기다리는</b> 교체 예약.
        ///
        /// <para>성패 확정(패턴 완료)은 마지막 노드를 <i>입력한 순간</i>에 일어나는데, 플레이어 칼은
        /// <c>Deadline(= LastNodeTime + goodWindow) + Pattern.ImpactOffset</c>에 지나가도록 정렬돼 있다.
        /// Perfect로 치면 그 둘이 goodWindow만큼 벌어지므로, 확정 즉시 갈라뜨리면
        /// <b>잘 칠수록 적이 칼보다 먼저 죽는다.</b> 그래서 확정과 연출을 분리한다
        /// (<see cref="SliceTargetDirector"/>가 투사체에 쓰는 것과 같은 규율).</para>
        /// </summary>
        private sealed class PendingKill
        {
            public EnemyView opponent;
            public SliceSet set;
            public float impactTime;
        }

        /// <summary>교체된 시체 하나. 수명은 조각이 잠들거나 <c>debrisLifetime</c>이 지나면 끝난다.</summary>
        private sealed class Corpse
        {
            public CorpseView view;
            public GameObject prefab;
            public float time;
        }

        private readonly List<EnemyView> ring = new List<EnemyView>();
        private readonly List<Reservation> reservations = new List<Reservation>();
        private readonly Queue<EnemyCue> pendingCues = new Queue<EnemyCue>();
        private readonly Queue<int> pendingTokens = new Queue<int>();
        private readonly List<Debris> debris = new List<Debris>();
        private readonly List<PendingKill> pendingKills = new List<PendingKill>();
        private readonly List<Corpse> corpses = new List<Corpse>();
        private readonly List<float> angleScratch = new List<float>();
        private int nextToken;

        // BakeMesh 대상 메쉬 재사용 큐. 교체마다 new Mesh()를 만들면 GC 압박이 된다.
        private readonly Queue<Mesh> frozenMeshPool = new Queue<Mesh>();

        private EnemyView currentOpponent;
        private EnemyView onDeck;

        /// <summary>
        /// 지금 교전 중인 상대. 없으면 null.
        ///
        /// <para><see cref="OnOpponentChanged"/>만으로는 <b>구독 시점의 상태를 알 수 없다</b> —
        /// 구독보다 먼저 승격이 일어났으면 그 이벤트는 이미 지나갔다. 구독 직후 한 번 읽어 동기화하라고 연다.</para>
        /// </summary>
        public EnemyView CurrentOpponent => currentOpponent;

        // 프리팹별 자체 풀(EffectManager·SliceTargetDirector 선례). Pool(PoolKey 단일 매핑)은 프리팹 수 증가에 맞지 않는다.
        private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
        private readonly Dictionary<GameObject, int> maxSizes = new Dictionary<GameObject, int>();
        private Transform poolRoot;

        private Vector3 Center => arenaCenter != null ? arenaCenter.position : transform.position;

        /// <summary>
        /// 리시(표류) 판정의 <b>고정 기준점</b>. <c>Center</c>는 보통 플레이어를 가리켜 함께 움직이므로
        /// 그걸로 이탈을 재면 "한 걸음의 길이"를 재는 꼴이 되어 표류를 전혀 못 막는다.
        /// 그래서 시작 시점의 위치를 한 번 잡아 두고 그것만 쓴다.
        /// </summary>
        private Vector3 ArenaOrigin => arenaOrigin;

        private Vector3 arenaOrigin;

        private Vector3 ViewForward
        {
            get
            {
                var cam = viewCamera != null ? viewCamera : Camera.main;
                return cam != null ? cam.transform.forward : Vector3.forward;
            }
        }

        void Awake()
        {
            var rootGo = new GameObject("[EnemyPool]");
            rootGo.transform.SetParent(transform, false);
            rootGo.SetActive(false);
            poolRoot = rootGo.transform;

            // 표류 기준점을 한 번만 잡는다 — Center는 플레이어를 따라 움직여 기준으로 쓸 수 없다.
            arenaOrigin = Center;
        }

        void OnEnable()
        {
            if (handler == null) return;
            handler.OnPatternQueued += HandlePatternQueued;
            handler.OnPatternComplete += HandlePatternComplete;
            handler.OnJudgeTargetFirstMiss += HandleFirstMiss;
            handler.OnAllPatternsCleared += HandleAllCleared;
        }

        void OnDisable()
        {
            if (handler == null) return;
            handler.OnPatternQueued -= HandlePatternQueued;
            handler.OnPatternComplete -= HandlePatternComplete;
            handler.OnJudgeTargetFirstMiss -= HandleFirstMiss;
            handler.OnAllPatternsCleared -= HandleAllCleared;
        }

        // ── 프리웜 / 로스터 ──────────────────────────────────────────────────────

        /// <summary>
        /// 곡 시작 전에 링을 채운다. <b>곡 도중에는 Instantiate가 한 번도 일어나지 않아야 한다</b> —
        /// 스키닝 메쉬 생성 한 프레임이 히치가 되고, 그게 곧 판정 손실이다.
        /// <c>ChartPlayer</c>가 카운트다운 구간에서 호출한다.
        /// </summary>
        public void PrepareStage()
        {
            Prewarm();

            for (int i = ring.Count; i < ringCount; i++)
                SpawnIntoRing(instant: true);

            PromoteOpponent();
        }

#if UNITY_EDITOR
        /// <summary>
        /// 채보 없이 적을 세운다. <b>디버그 투입 전에 한 번 눌러야 한다</b> —
        /// 정상 재생에서는 <c>ChartPlayer</c>가 카운트다운에서 <see cref="PrepareStage"/>를 부르지만,
        /// <c>Debug: Set Test Pattern</c> 경로에는 그게 없어 링이 빈 채로 패턴만 들어온다
        /// (그러면 <c>currentOpponent</c>가 null이라 적 연출이 통째로 무연출이 된다).
        /// </summary>
        [ContextMenu("Debug: Prepare Stage")]
        private void DebugPrepareStage()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[EnemyDirector] 플레이 중에만 무대를 세울 수 있습니다.", this);
                return;
            }

            PrepareStage();
            Debug.Log($"[EnemyDirector] 무대 준비 완료 — 링 {ring.Count}명, 상대 " +
                      $"{(currentOpponent != null ? currentOpponent.name : "(없음)")}.", this);
        }
#endif

        /// <summary>
        /// 로스터를 훑어 적 프리팹과 그 절단 세트를 미리 채운다.
        /// <b>세트는 적 종류가 소유하므로 로스터만 보면 전부 걸린다</b> — 채보를 훑을 필요가 없다.
        /// </summary>
        private void Prewarm()
        {
            if (rosterPool == null) return;

            var seen = new HashSet<SliceSet>();

            foreach (var definition in rosterPool)
            {
                if (definition == null || !definition.IsUsable) continue;

                maxSizes[definition.Prefab] = definition.MaxPoolSize;
                for (int i = 0; i < definition.InitialPoolSize; i++)
                    Release(CreateInstance(definition.Prefab));

                PrewarmSet(definition.DeathSliceSet, seen);
            }
        }

        /// <summary>세트 하나를 프리웜한다. 여러 패턴이 같은 세트를 공유하므로 중복 제거가 핵심이다.</summary>
        private void PrewarmSet(SliceSet set, HashSet<SliceSet> seen)
        {
            if (set == null || !set.IsUsable || !seen.Add(set)) return;

            // 스킨드 세트의 산출물은 시체 프리팹 하나다(조각 배열이 아니라).
            if (set.Skinned)
            {
                maxSizes[set.CorpsePrefab] = set.MaxPoolSize;
                for (int i = 0; i < set.InitialPoolSize; i++)
                    Release(CreateInstance(set.CorpsePrefab));
                return;
            }

            foreach (var prefab in set.PiecePrefabs)
            {
                if (prefab == null) continue;
                maxSizes[prefab] = set.MaxPoolSize;
                for (int i = 0; i < set.InitialPoolSize; i++)
                    Release(CreateInstance(prefab));
            }
        }

        /// <summary>링에 적 하나를 채운다. 등장 각도는 <b>시야 반대편</b>에서 고른다(§3-5).</summary>
        private EnemyView SpawnIntoRing(bool instant)
        {
            var definition = PickDefinition();
            if (definition == null) return null;

            angleScratch.Clear();
            foreach (var e in ring) angleScratch.Add(e.RingAngle);

            float viewYaw = EnemyRing.DirectionToAngle(ViewForward);
            float angle = EnemyRing.PickSpawnAngle(angleScratch, viewYaw, minAngleGap);

            float radius = ringRadius + UnityEngine.Random.Range(-radiusJitter, radiusJitter);
            Vector3 ringPos = EnemyRing.AngleToPosition(Center, angle, radius);
            Vector3 entryPos = instant ? ringPos : EnemyRing.AngleToPosition(Center, angle, radius * Mathf.Max(entryDistanceMul, 1f));

            var go = Rent(definition.Prefab, definition.MaxPoolSize);
            if (go == null) return null;

            var view = go.GetComponent<EnemyView>();
            if (view == null) view = go.AddComponent<EnemyView>();

            view.Setup(definition, angle, ringPos, entryPos, instant ? 0.01f : entryDuration, Center);

            // 링에 선 적 전원이 플레이어를 노려본다. 대상은 트랜스폼이라 플레이어가 움직여도 따라간다.
            view.SetGazeTarget(arenaCenter != null ? arenaCenter : transform);
            view.ApplyBackgroundBudget(true);
            ring.Add(view);
            return view;
        }

        private EnemyDefinition PickDefinition()
        {
            if (rosterPool == null || rosterPool.Length == 0) return null;

            // 사용 가능한 것 중에서만 고른다 — 미배선 슬롯이 섞여 있어도 무연출로 죽지 않는다.
            int start = UnityEngine.Random.Range(0, rosterPool.Length);
            for (int i = 0; i < rosterPool.Length; i++)
            {
                var candidate = rosterPool[(start + i) % rosterPool.Length];
                if (candidate != null && candidate.IsUsable) return candidate;
            }

            return null;
        }

        /// <summary>
        /// 대기석을 승격시키고 링에서 새 대기석을 뽑는다.
        /// <b>대기석은 자리를 옮기지 않는다</b> — 다음 상대를 미리 <i>뽑아만</i> 둔다.
        ///
        /// <para>예전에는 결투 앵커 옆으로 미리 걸어 나오게 했다. 적이 링(반경 6m)을 통째로 건너와야 해서
        /// 클립 시작 전에 못 닿는 일이 잦았기 때문이다. <b>수렴이 들어오면서 그 이유가 사라졌다</b> —
        /// 이제 플레이어가 절반을 마중 나가므로 링에서 바로 출발해도 시간이 맞는다.
        /// 미리 나와 있으면 오히려 링이 헐거워 보이고, 플레이어가 딴 데로 뛰면 혼자 서 있는 그림이 된다.</para>
        /// </summary>
        private void PromoteOpponent()
        {
            var previous = currentOpponent;

            currentOpponent = onDeck ?? TakeNextFromRing(previous);
            onDeck = TakeNextFromRing(currentOpponent);

            // 이전 상대가 살아 있는데 자리를 넘겼다면 링으로 돌려보낸다.
            // 실패 후퇴(짧게 물러남)와 달리 여기는 진짜 이탈이다 — 그래서 목적지도 다르다.
            if (previous != null && previous != currentOpponent && previous.Current != EnemyView.Phase.Dying)
            {
                previous.ReturnToRing(returnDuration);
                if (!ring.Contains(previous)) ring.Add(previous);
            }

            if (currentOpponent != previous)
                OnOpponentChanged?.Invoke(previous, currentOpponent);
        }

        /// <summary>링 각도 순으로 다음 적을 뽑는다. 배치는 랜덤이되 선택은 순서라야 아레나를 한 바퀴 훑는 그림이 된다.</summary>
        private EnemyView TakeNextFromRing(EnemyView reference)
        {
            if (ring.Count == 0) return null;

            angleScratch.Clear();
            foreach (var e in ring) angleScratch.Add(e.RingAngle);

            float from = reference != null ? reference.RingAngle : EnemyRing.DirectionToAngle(ViewForward);
            int index = EnemyRing.PickNextOpponent(angleScratch, from);
            if (index < 0) return null;

            var picked = ring[index];
            ring.RemoveAt(index);
            picked.ApplyBackgroundBudget(false);
            return picked;
        }

        private Vector3 DuelDirection()
        {
            if (duelAnchor == null) return Vector3.forward;

            Vector3 dir = duelAnchor.position - Center;
            dir.y = 0f;
            return dir.sqrMagnitude < 1e-6f ? Vector3.forward : dir.normalized;
        }

        /// <summary>
        /// 이번 패턴에서 둘이 유지할 간격(m). <b>앵커가 기준 거리의 단일 출처다</b> —
        /// 앵커는 플레이어 자식이므로 중앙까지의 평면 거리가 곧 기본 간격이고, 패턴이 리치만큼 보정한다.
        /// 툴(<c>짝 에디터</c>·<c>슬라이서</c>)의 `기준 결투 거리`에 같은 값을 넣어야 그림이 일치한다.
        /// </summary>
        private float DuelDistanceOf(Pattern template)
        {
            // 앵커가 없으면 툴 기본값과 같은 1m로 본다 — 두 곳이 다르면 프리뷰와 게임이 어긋난다.
            float baseDistance = duelAnchor != null
                ? Vector3.ProjectOnPlane(duelAnchor.position - Center, Vector3.up).magnitude
                : 1f;

            return Mathf.Max(baseDistance + (template != null ? template.DuelDistanceOffset : 0f), 0.1f);
        }

        /// <summary>
        /// 둘이 마주 달려 만나는 배치를 만든다.
        ///
        /// <para><b>중점 기준 대칭</b>이라 각자 이동 거리가 절반이 되고, 누가 얼마나 움직였는지가 공평하게 읽힌다.
        /// 다만 중점으로 그냥 가면 플레이어가 교전마다 링 쪽으로 끌려가므로(누적 표류)
        /// <see cref="maxOffset"/>으로 <b>클램프한 뒤 적 목표를 다시 계산</b>한다 —
        /// 순서를 뒤집으면 간격이 <see cref="DuelDistanceOf"/>와 어긋나 칼이 빗나간다.</para>
        ///
        /// <para>여유가 <see cref="minConvergeTime"/> 미만이면 <b>플레이어는 제자리</b>고 적만 붙는다.
        /// 순간이동처럼 미끄러지는 것보다 낫고, 어느 쪽이든 <b>간격은 항상 지켜진다</b>.</para>
        /// </summary>
        private DuelPlan BuildDuelPlan(EnemyView opponent, Pattern template, float arriveTime)
        {
            Vector3 center = Center;
            float distance = DuelDistanceOf(template);

            // 상대가 없으면(디버그 경로) 앵커를 그대로 쓴다.
            if (opponent == null)
                return new DuelPlan(center, center + DuelDirection() * distance, arriveTime);

            // '지금 위치'가 아니라 '갈 곳'으로 잡는다 — 배정 순간 적이 이동 중이면(후퇴·대기석 진입·링 등장)
            // transform.position은 곧 떠날 위치다. 특히 후퇴에서는 "둘 다 제자리"로 계산되어 플레이어가 안 붙는다.
            Vector3 enemyPos = opponent.Destination;
            Vector3 toEnemy = Vector3.ProjectOnPlane(enemyPos - center, Vector3.up);
            Vector3 dir = toEnemy.sqrMagnitude < 1e-6f ? DuelDirection() : toEnemy.normalized;

            bool canConverge = (arriveTime - Time.time) >= minConvergeTime;
            if (!canConverge)
                return new DuelPlan(center, center + dir * distance, arriveTime);

            Vector3 mid = center + toEnemy * 0.5f;
            Vector3 playerTarget = mid - dir * (distance * 0.5f);

            // 리시 — 아레나 원점에서 너무 멀어지면 클램프한다. 플레이어가 덜 가고 적이 그만큼 더 온다.
            //
            // 기준점은 반드시 '고정된 원점'이어야 한다. arenaCenter가 플레이어를 가리키면
            // Center가 플레이어와 함께 움직여, 이 식이 "중앙에서의 이탈"이 아니라 "한 걸음의 길이"를 재게 된다
            // → 매 교전 1.5m씩 링 쪽으로 걸어 나가도 아무도 막지 못한다.
            Vector3 offset = Vector3.ProjectOnPlane(playerTarget - ArenaOrigin, Vector3.up);
            if (offset.magnitude > maxOffset) playerTarget = ArenaOrigin + offset.normalized * maxOffset;

            playerTarget.y = center.y;
            return new DuelPlan(playerTarget, playerTarget + dir * distance, arriveTime);
        }

        // ── 이벤트 처리 ──────────────────────────────────────────────────────────

        /// <summary><c>ChartPlayer</c>가 <c>SetPattern</c> 직전에 호출한다. cue 큐와 토큰 큐가 같은 FIFO 규율로 흐른다.</summary>
        public void EnqueueCue(EnemyCue cue)
        {
            pendingCues.Enqueue(cue ?? new EnemyCue());
        }

        /// <summary>
        /// 채보 cue 없이 패턴이 들어왔을 때 쓸 값. 정상 재생에서는 <c>ChartPlayer</c>가 항상 cue를 밀어 넣으므로
        /// <b>이 경로는 디버그 투입(<c>Debug: Set Test Pattern</c>)에서만 탄다.</b>
        /// 에디터에서는 <see cref="debugCue"/>를 써서 채보 없이도 처치·투사체를 시험할 수 있게 한다.
        /// </summary>
        private EnemyCue FallbackCue()
        {
#if UNITY_EDITOR
            if (debugCue != null) return debugCue;
#endif
            return new EnemyCue();
        }

        /// <summary>
        /// <b>접수</b>만 한다 — 상대는 아직 정하지 않는다.
        ///
        /// <para><b>큐 투입 시점에는 상대를 알 수 없다.</b> 다음 패턴의 상대는 지금 패턴의 성패가 정하는데
        /// (성공하면 죽고 다음 적, 실패하면 같은 적이 이어진다), 그 답은 지금 패턴이 완료돼야 나온다.
        /// 겹침 리드타임(<c>exposureDuration</c> 0.5초) 때문에 다음 패턴은 그보다 <b>먼저</b> 큐에 오르므로,
        /// 여기서 <c>currentOpponent</c>를 잡으면 <b>한 명씩 밀린 상대</b>가 잡힌다
        /// (실측 채보에서 92%가 어긋났다 — 링에 서 있는 엉뚱한 적이 갈라지는 원인).</para>
        ///
        /// <para><b>반대로 접수는 미룰 수 없다.</b> 토큰·cue는 <c>ChartPlayer</c>가 밀어 넣는 FIFO와 짝이 맞아야 하고,
        /// 대기석 이동은 시간이 걸리는 일이라 일찍 시작해야 한다.</para>
        /// </summary>
        private void HandlePatternQueued(PatternQueuedInfo info)
        {
            int token = nextToken++;
            pendingTokens.Enqueue(token);

            // cue가 없으면(디버그 경로/기존 채보) 기본값 = Attacker.Player, 무연출 폴백.
            EnemyCue cue = pendingCues.Count > 0 ? pendingCues.Dequeue() : FallbackCue();

            // 임팩트 보정도 패턴이 정한다 — 플레이어 칼·카메라 큐와 같은 값을 읽어야 어긋날 수가 없다.
            float impactTime = info.Deadline + (info.Template != null ? info.Template.ImpactOffset : 0f);

            var reservation = new Reservation
            {
                token = token,
                cue = cue,
                template = info.Template,
                opponent = null,           // 배정은 BindReservation에서
                impactTime = impactTime,
                startTime = info.StartTime,

                // 적이 공격자일 때만 적 클립을 재생한다. 플레이어가 공격자면 적은 무방비로 서 있는다.
                attack = info.Template != null && info.Template.Attacker == Attacker.Enemy
                    ? info.Template.EnemyAttack
                    : null
            };
            reservations.Add(reservation);

            StageOnDeck(impactTime);

            // 선두 예약(앞에 미확정 예약이 없다)은 지금이 곧 판정 대상이 되는 순간이다 — 즉시 배정한다.
            // 첫 패턴과 곡 중간 공백 뒤가 이 경로를 탄다.
            if (IsHeadReservation(reservation)) BindReservation(reservation);
        }

        /// <summary>이 예약보다 앞서 대기 중인(미확정) 예약이 없는가. 없으면 지금 판정 대상이다.</summary>
        private bool IsHeadReservation(Reservation reservation)
        {
            foreach (var r in reservations)
            {
                if (r == reservation) continue;
                if (!r.resolved) return false;
            }

            return true;
        }

        /// <summary>
        /// <b>배정</b> — 이 패턴이 판정 대상이 되는 순간에 상대를 확정하고 결투를 건다.
        ///
        /// <para>여기가 <c>currentOpponent</c>를 읽는 <b>유일한 지점</b>이다. 호출 시점이 곧
        /// "이전 패턴의 성패가 반영된 뒤"라서, 승격했든 안 했든 언제나 정답이 들어 있다.</para>
        /// </summary>
        private void BindReservation(Reservation r)
        {
            if (r.bound) return;
            r.bound = true;

            var opponent = currentOpponent;
            r.opponent = opponent;

            // 도착 시각 = 클립 시작 시각. 클립이 없으면 임팩트까지가 여유다.
            // AssignAttack이 내부에서 같은 식을 쓰지만(순수 함수) 계획을 먼저 세워야 해서 여기서 한 번 더 부른다.
            float arriveTime = r.attack != null && r.attack.IsUsable
                ? r.attack.ResolveScheduleStart(r.impactTime, Time.time)
                : r.impactTime;

            var plan = BuildDuelPlan(opponent, r.template, arriveTime);
            lastPlan = plan;
            OnDuelScheduled?.Invoke(plan);

            if (opponent != null)
                opponent.AssignAttack(r.attack, r.impactTime, plan.EnemyPosition, plan.PlayerPosition, maxAttackSpeed);

            if (r.cue.projectile != null && projectileDirector != null)
            {
                Vector3 origin = opponent != null ? opponent.RingPosition : Center;
                projectileDirector.Reserve(r.token, r.cue.projectile, r.startTime, r.impactTime, Vector2.zero, origin);
            }
        }

        /// <summary>가장 오래된 미배정 예약 하나를 배정한다. 예약이 확정될 때마다 불린다.</summary>
        private void BindNextReservation()
        {
            foreach (var r in reservations)
            {
                if (r.resolved || r.bound) continue;
                BindReservation(r);
                return;
            }
        }

        /// <summary>
        /// 대기석을 <b>지금 패턴이 입력되는 동안</b> 결투 지점 쪽으로 미리 걸어 들여보낸다.
        ///
        /// <para>승격은 처치 순간에 일어나고, 다음 패턴의 창은 노드 개수가 정한다(대략 0.6~1.4초).
        /// 링 반경(6m)에서 출발하면 짧은 패턴에서는 그 시간에 도저히 못 닿아 미끄러지듯 끌려온다.
        /// <b>그래서 걸을 시간이 있을 때 미리 걷는다</b> — 지금 패턴의 입력 구간이 통째로 남는다.</para>
        ///
        /// <para><b>항상 들여보내지는 않는다.</b> 창 안에 <see cref="approachSpeed"/>로 닿을 거리면
        /// 링에 그대로 둔다 — 그래야 링이 헐거워 보이지 않고, 수렴이 원래 하려던 "서로 마주 달려가는" 그림이 산다.</para>
        ///
        /// <para><b>판단 창은 지금 패턴의 창이 아니라 고정값(<see cref="bindToImpactWindow"/>)이다.</b>
        /// 상대 배정이 판정 대상 승계 시점으로 내려간 뒤로, 승격된 적에게 실제로 주어지는 시간은
        /// "지금 패턴이 얼마나 긴가"와 무관하게 <b>배정~임팩트 구간</b>뿐이다. 지금 패턴의 창으로 재면
        /// 긴 패턴이 지나갈 때 "여유 있다"고 링에 두는데, 정작 배정 직후엔 0.4초밖에 없다.</para>
        /// </summary>
        private void StageOnDeck(float arriveTime)
        {
            if (onDeck == null) return;

            Vector3 center = Center;
            Vector3 radial = Vector3.ProjectOnPlane(onDeck.transform.position - center, Vector3.up);

            // 배정 후 주어질 시간 안에 뛰어올 수 있으면 손대지 않는다.
            if (radial.magnitude - stagingDistance <= approachSpeed * bindToImpactWindow) return;

            // 자기 링 방향을 유지한 채 좁혀 든다 — 링을 가로질러 순간이동하는 것처럼 보이지 않는다.
            Vector3 dir = radial.sqrMagnitude < 1e-6f ? -DuelDirection() : radial.normalized;
            Vector3 target = center + dir * stagingDistance;
            target.y = onDeck.transform.position.y;

            onDeck.ScheduleFace(center);
            onDeck.ScheduleMove(onDeck.transform.position, target, Time.time, arriveTime);
        }

        /// <summary>보정 없는 기준선. 기즈모가 쓴다 — 특정 패턴에 묶이지 않는다.</summary>
        private Vector3 DuelAnchorPosition() =>
            duelAnchor != null ? duelAnchor.position : Center + Vector3.forward * 2f;

        private void HandlePatternComplete(PatternCompletionInfo info)
        {
            if (pendingTokens.Count == 0) return;
            int token = pendingTokens.Dequeue();

            var reservation = Find(token);
            if (reservation == null) return;

            ResolveReservation(reservation, info.AllCorrect);
        }

        /// <summary>첫 미스 순간 — 아직 확정되지 않은 가장 오래된 토큰이 곧 현재 판정 대상이다.</summary>
        private void HandleFirstMiss()
        {
            if (pendingTokens.Count == 0) return;

            var reservation = Find(pendingTokens.Peek());
            if (reservation == null || reservation.resolved) return;

            // 투사체는 즉시 실패로 확정한다(도착 시점에 부딪히도록). 적 리액션은 완료 이벤트에서 처리한다.
            projectileDirector?.Resolve(reservation.token, false);
        }

        private void ResolveReservation(Reservation r, bool playerSucceeded)
        {
            if (r.resolved) return;
            r.resolved = true;

            projectileDirector?.Resolve(r.token, playerSucceeded);
            reservations.Remove(r);

            var opponent = r.opponent;
            if (opponent != null)
            {
                // killOnSuccess를 성공하면 처치, 실패하면 죽지 않고 교전이 이어진다.
                // 이 분기가 "실패하면 같은 상대와 계속"의 유일한 소유자다 — 아래 배정은 그 결과를 읽기만 한다.
                if (r.cue.killOnSuccess && playerSucceeded)
                    KillOpponent(opponent, ResolveDeathSet(r), r.impactTime); // 안에서 PromoteOpponent
                else
                    opponent.Resolve(playerSucceeded, AttackerOf(r.template), failRetreatDistance, retreatDuration);
            }

            // 이 확정이 곧 '다음 패턴이 판정 대상이 되는 순간'이다(CLAUDE.md §3 — 같은 프레임에 즉시 승계).
            // 승격 여부가 위에서 이미 반영됐으므로 currentOpponent가 언제나 정답이다.
            BindNextReservation();
        }

        /// <summary>
        /// 처치할 때 쓸 절단 산출물. <b>적 종류가 유일한 소유자다.</b>
        ///
        /// <para>시체 프리팹 안에는 그 적의 스켈레톤 사본이 들어 있어 세트는 원리적으로 적 모델을 넘나들 수 없다.
        /// 패턴에도 두면 "세트는 패턴이 고르고 죽는 적은 링에서 고른다"가 되어 모델이 어긋난다 —
        /// 리그가 같고 메쉬만 다르면 경고도 안 뜨고 엉뚱한 몸이 갈라진다.
        /// 프리팹을 소유한 쪽에 두어 그 상태를 <b>표현 불가능</b>하게 만든다.</para>
        ///
        /// <para><c>ponytail:</c> 절단 각도는 적 종류당 하나로 고정된다. 패턴별 각도가 필요해지면
        /// 정의에 세트 배열을 두고 패턴이 인덱스로 고르게 올린다 — 전부 같은 프리팹에서 구워지므로
        /// 모델 정합성은 그대로 유지된다.</para>
        /// </summary>
        private static SliceSet ResolveDeathSet(Reservation r) =>
            r.opponent != null && r.opponent.Definition != null ? r.opponent.Definition.DeathSliceSet : null;

        /// <summary>
        /// 처치. <b>죽는 연출을 기다리지 않는다</b> — 죽는 적은 그 자리에 버려두고 즉시 다음 상대로 넘어간다.
        /// 무쌍 감각의 핵심이 이거다(베고 뒤도 안 돌아본다).
        ///
        /// <para>다만 <b>연출만은 임팩트 시각까지 기다린다</b>(<see cref="PendingKill"/>).
        /// 상대 전환·링 보충은 여기서 즉시 하고, 갈라짐만 예약한다.</para>
        /// </summary>
        private void KillOpponent(EnemyView opponent, SliceSet set, float impactTime)
        {
            opponent.MarkDying();

            pendingKills.Add(new PendingKill { opponent = opponent, set = set, impactTime = impactTime });

            OnEnemyKilled?.Invoke(opponent);

            if (currentOpponent == opponent) currentOpponent = null;
            if (onDeck == opponent) onDeck = null;

            // 링을 다시 채우고(등 뒤에서 걸어 들어온다) 다음 상대로 전환한다.
            if (ring.Count < ringCount) SpawnIntoRing(instant: false);
            PromoteOpponent();
        }

        /// <summary>임팩트 시각에 도달한 예약을 실행한다. 여기서야 적이 실제로 갈라진다.</summary>
        private void TickPendingKills()
        {
            float now = Time.time;

            for (int i = pendingKills.Count - 1; i >= 0; i--)
            {
                var pending = pendingKills[i];
                if (now < pending.impactTime) continue;

                pendingKills.RemoveAt(i);
                ExecuteKill(pending);
            }
        }

        private void ExecuteKill(PendingKill pending)
        {
            var opponent = pending.opponent;
            if (opponent == null) return;

            var set = pending.set;

            // 스킨드 세트 — 시체 프리팹으로 통째로 교체하고 산 적은 풀로 반납한다.
            if (set != null && set.Skinned && set.IsUsable)
            {
                SwapToCorpse(opponent, set);
                return;
            }

            // 정적 프록시 — 기존 경로(조각 프리팹 N개를 대여해 뷰가 갈라뜨린다).
            var spawned = new List<SlicePiece>();
            if (set != null && set.IsUsable)
            {
                for (int i = 0; i < set.PieceCount; i++)
                {
                    var go = Rent(set.PiecePrefabs[i], set.MaxPoolSize);
                    if (go == null) continue;

                    var piece = go.GetComponent<SlicePiece>();
                    if (piece == null) piece = go.AddComponent<SlicePiece>();
                    spawned.Add(piece);
                }
            }

            // 조각을 빌려 온 세트를 그대로 넘긴다 — 뷰가 다시 조회하면 조각과 배치가 다른 세트에서 나온다.
            opponent.Kill(set, spawned, scatterSpeed, scatterSpin, pieceLayer);
            debris.Add(new Debris { pieces = spawned, view = opponent, time = Time.time });
        }

        /// <summary>
        /// 산 적 → 시체 교체. <b>산 적은 여기서 풀로 돌아간다</b> —
        /// 시체가 자기 스켈레톤을 갖기 때문에 원본 인스턴스를 붙잡아 둘 이유가 없다.
        /// </summary>
        private void SwapToCorpse(EnemyView opponent, SliceSet set)
        {
            var go = Rent(set.CorpsePrefab, set.MaxPoolSize);
            if (go == null)
            {
                ReleaseEnemy(opponent);
                return;
            }

            var corpse = go.GetComponent<CorpseView>();
            if (corpse == null)
            {
                Debug.LogWarning($"[EnemyDirector] '{set.name}'의 시체 프리팹에 CorpseView가 없습니다. 다시 구우세요.", set);
                Release(go);
                ReleaseEnemy(opponent);
                return;
            }

            var source = opponent.SourceSkinned;
            corpse.AdoptPose(opponent.transform, source != null ? source.bones : null);
            corpse.Burst(scatterSpeed, scatterSpin, pieceLayer, frozenMeshPool, keepRootSkinnedForRagdoll);

            corpses.Add(new Corpse { view = corpse, prefab = set.CorpsePrefab, time = Time.time });

            ReleaseEnemy(opponent);
        }

        private void HandleAllCleared()
        {
            pendingCues.Clear();
            pendingTokens.Clear();
            reservations.Clear();
            projectileDirector?.ClearAll();

            // 대기 중인 교체 예약도 버린다 — 안 그러면 곡이 끝난 뒤에 적이 갈라진다.
            foreach (var pending in pendingKills)
            {
                if (pending.opponent != null) ReleaseEnemy(pending.opponent);
            }
            pendingKills.Clear();

            DissolveAll();
        }

        /// <summary>곡이 끝났을 때 남은 적을 소멸시킨다. <b>절단이 아니다</b> — 베지 않았으니 갈라지면 안 된다.</summary>
        public void DissolveAll()
        {
            foreach (var e in ring) e?.Dissolve(dissolveDuration);
            currentOpponent?.Dissolve(dissolveDuration);
            onDeck?.Dissolve(dissolveDuration);
        }

        /// <summary>소멸 연출까지 전부 끝났는지. 스테이지 종료 신호를 낼 시점 판단에 쓴다.</summary>
        public bool StageCleanupFinished
        {
            get
            {
                foreach (var e in ring)
                {
                    if (e != null && !e.DissolveFinished) return false;
                }

                if (currentOpponent != null && !currentOpponent.DissolveFinished) return false;
                if (onDeck != null && !onDeck.DissolveFinished) return false;

                return true;
            }
        }

        private Reservation Find(int token)
        {
            foreach (var r in reservations)
            {
                if (r.token == token) return r;
            }

            return null;
        }

        // ── 루프 ────────────────────────────────────────────────────────────────

        void Update()
        {
            TickPendingKills();
            RecycleDissolved();
            RecycleDebris();
            RecycleCorpses();
            EnforcePieceBudget();
        }

        /// <summary>수명이 다했거나 조각이 전부 잠든 시체를 회수한다.</summary>
        private void RecycleCorpses()
        {
            for (int i = corpses.Count - 1; i >= 0; i--)
            {
                var corpse = corpses[i];
                if (corpse.view == null) { corpses.RemoveAt(i); continue; }

                bool expired = Time.time - corpse.time >= debrisLifetime;
                if (!expired && !corpse.view.AllPiecesSettled) continue;

                ReleaseCorpse(corpse);
                corpses.RemoveAt(i);
            }

            // 시체 수 상한 — 래그돌 비용은 조각 수가 아니라 시체(스켈레톤) 수에 비례한다.
            while (corpses.Count > maxActiveCorpses)
            {
                ReleaseCorpse(corpses[0]);
                corpses.RemoveAt(0);
            }
        }

        private void ReleaseCorpse(Corpse corpse)
        {
            if (corpse.view == null) return;

            corpse.view.ResetState(frozenMeshPool);
            Release(corpse.view.gameObject);
        }

        private void RecycleDissolved()
        {
            for (int i = ring.Count - 1; i >= 0; i--)
            {
                var view = ring[i];
                if (view == null) { ring.RemoveAt(i); continue; }
                if (!view.DissolveFinished) continue;

                ring.RemoveAt(i);
                ReleaseEnemy(view);
            }

            if (currentOpponent != null && currentOpponent.DissolveFinished)
            {
                ReleaseEnemy(currentOpponent);
                currentOpponent = null;
            }

            if (onDeck != null && onDeck.DissolveFinished)
            {
                ReleaseEnemy(onDeck);
                onDeck = null;
            }
        }

        private void RecycleDebris()
        {
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i];
                bool expired = Time.time - d.time >= debrisLifetime;
                if (!expired && !AllSettled(d)) continue;

                RecycleDebrisEntry(d);
                debris.RemoveAt(i);
            }
        }

        private static bool AllSettled(Debris d)
        {
            if (d.pieces == null || d.pieces.Count == 0) return true;

            foreach (var p in d.pieces)
            {
                if (p != null && !p.IsSettled) return false;
            }

            return true;
        }

        private void RecycleDebrisEntry(Debris d)
        {
            if (d.pieces != null)
            {
                foreach (var p in d.pieces)
                {
                    if (p == null) continue;
                    p.ResetState();
                    Release(p.gameObject);
                }
            }

            if (d.view != null) ReleaseEnemy(d.view);
        }

        private void EnforcePieceBudget()
        {
            int total = 0;
            foreach (var d in debris) total += d.pieces != null ? d.pieces.Count : 0;

            for (int i = 0; i < debris.Count && total > maxActivePieces; i++)
            {
                var d = debris[i];
                total -= d.pieces != null ? d.pieces.Count : 0;
                RecycleDebrisEntry(d);
                debris.RemoveAt(i);
                i--;
            }
        }

        private void ReleaseEnemy(EnemyView view)
        {
            view.ResetState();
            Release(view.gameObject);
        }

        // ── 프리팹별 풀 ──────────────────────────────────────────────────────────

        private GameObject CreateInstance(GameObject prefab)
        {
            if (prefab == null) return null;

            var go = Instantiate(prefab, poolRoot);
            go.name = prefab.name;

            var link = go.GetComponent<EnemyPooledInstance>();
            if (link == null) link = go.AddComponent<EnemyPooledInstance>();
            link.SourcePrefab = prefab;
            return go;
        }

        private GameObject Rent(GameObject prefab, int maxSize)
        {
            if (prefab == null) return null;

            maxSizes[prefab] = maxSize;

            if (pools.TryGetValue(prefab, out var queue) && queue.Count > 0)
            {
                var pooled = queue.Dequeue();
                pooled.transform.SetParent(null, false);
                pooled.SetActive(true);
                return pooled;
            }

            var created = CreateInstance(prefab);
            if (created != null)
            {
                created.transform.SetParent(null, false);
                created.SetActive(true);
            }

            return created;
        }

        private void Release(GameObject instance)
        {
            if (instance == null) return;

            var link = instance.GetComponent<EnemyPooledInstance>();
            if (link == null || link.SourcePrefab == null)
            {
                Destroy(instance);
                return;
            }

            var prefab = link.SourcePrefab;
            if (!pools.TryGetValue(prefab, out var queue))
                pools[prefab] = queue = new Queue<GameObject>();

            int cap = maxSizes.TryGetValue(prefab, out int m) ? m : int.MaxValue;
            if (queue.Count >= cap)
            {
                Destroy(instance);
                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(poolRoot, false);
            queue.Enqueue(instance);
        }

        // ── 기즈모 (에디터 전용) ─────────────────────────────────────────────────

#if UNITY_EDITOR
        /// <summary>
        /// 반경은 카메라 화각·격자 크기와 같이 봐야 정해진다. 숫자만 봐선 못 정하므로 씬 뷰에 그린다.
        /// <b>편집 중에도 그린다</b> — 플레이 없이 반경을 잡아야 하기 때문.
        /// </summary>
        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            Vector3 center = Center;

            Gizmos.color = gizmoRingColor;
            DrawCircle(center, ringRadius);

            // 배치 가능 범위가 띠로 보이도록 안팎 경계를 같이 그린다.
            var faded = gizmoRingColor;
            faded.a *= 0.4f;
            Gizmos.color = faded;
            DrawCircle(center, ringRadius - radiusJitter);
            DrawCircle(center, ringRadius + radiusJitter);
            DrawCircle(center, ringRadius * Mathf.Max(entryDistanceMul, 1f));

            // 최소 각 간격을 방사선으로 — 몇 명이 들어가는지 눈으로 센다.
            Gizmos.color = faded;
            for (float a = 0f; a < 360f; a += Mathf.Max(minAngleGap, 1f))
                Gizmos.DrawLine(center, EnemyRing.AngleToPosition(center, a, ringRadius));

            // 보정 없는 기준선만 그린다 — 패턴별 보정까지 그리면 무엇을 보고 있는지 모호해진다.
            Gizmos.color = gizmoDuelColor;
            Vector3 duel = DuelAnchorPosition();
            Gizmos.DrawWireSphere(duel, 0.3f);
            Gizmos.DrawLine(center, duel);
            UnityEditor.Handles.color = gizmoDuelColor;
            UnityEditor.Handles.Label(duel + Vector3.up * 0.4f,
                $"결투 기준선 {Vector3.Distance(center, duel):0.00}m (패턴별 ± 보정은 별도)");
            DrawCircle(center, maxOffset);

            UnityEditor.Handles.color = gizmoRingColor;
            UnityEditor.Handles.Label(center + Vector3.up * 0.5f,
                $"ring r={ringRadius:0.0}±{radiusJitter:0.0}  count={ringCount}  gap={minAngleGap:0}°  (최대 {Mathf.FloorToInt(360f / Mathf.Max(minAngleGap, 1f))}명)");

            if (!Application.isPlaying) return;

            foreach (var e in ring)
            {
                if (e == null) continue;
                Gizmos.color = gizmoRingColor;
                Gizmos.DrawWireCube(e.transform.position, Vector3.one * 0.4f);
            }

            if (onDeck != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(onDeck.transform.position, Vector3.one * 0.6f);
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

    /// <summary>풀 반납 시 어느 프리팹에서 나왔는지 되짚기 위한 표식.</summary>
    public class EnemyPooledInstance : MonoBehaviour
    {
        public GameObject SourcePrefab;
    }
}
