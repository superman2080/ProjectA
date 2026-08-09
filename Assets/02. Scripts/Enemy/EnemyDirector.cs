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

        [Tooltip("원형 무대의 중심. ⚠ 플레이어가 아니라 '무대' 오브젝트를 넣을 것 — 월드에 고정돼 있어야 한다. 비우면 이 오브젝트의 위치.")]
        [SerializeField] private Transform arenaCenter;

        [Tooltip("칼이 만나는 지점. 플레이어의 자식이어야 한다 — 이게 곧 결투 지점이다.")]
        [SerializeField] private Transform duelAnchor;

        [Tooltip("시야 기준 카메라. 스폰을 화면 밖에 배치하는 데 쓴다(절두체 판정). 비우면 Camera.main.")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("투사체 연출. 비우면 원거리 오브젝트 없음.")]
        [SerializeField] private SliceTargetDirector projectileDirector;

        [Header("Stage")]
        [Tooltip("원형 무대의 반경(m). 적은 이 원 '안'에 흩어져 서고 플레이어가 그 사이를 오간다.")]
        [SerializeField] private float stageRadius = 8f;
        [Tooltip("무대에 유지할 적 수.")]
        [SerializeField] private int ringCount = 6;
        [Tooltip("적끼리 유지할 최소 거리(m).")]
        [SerializeField] private float minSpacing = 2.5f;
        [Tooltip("플레이어 코앞에 튀어나오지 않게 하는 최소 거리(m).")]
        [SerializeField] private float minPlayerDistance = 4f;
        [Tooltip("무대 배치 후보 수. 늘리면 간격이 고르지만 스폰 비용이 는다.")]
        [SerializeField] private int spawnCandidateCount = 24;

        [Header("Cluster")]
        [Tooltip("적을 무리 지어 배치한다. 끄면 예전처럼 무대 전체에 흩어진다(회귀 없음).")]
        [SerializeField] private bool clusterEnabled = true;

        [Tooltip("한 무리의 적 수. 무대에 유지할 총원(ringCount)은 이 값 × 2로 파생된다 —\n" +
                 "지금 싸우는 무리(active)와 다음 무리(staged) 둘만 살아 있다.\n" +
                 "⚠ 이 값이 곧 '제자리 난타가 몇 패턴 이어지는가'다. 무리 안에서는 이동이 0에 가까우므로\n" +
                 "4면 3패턴 연속 정지 뒤 한 번 크게 대시한다(docs/EnemyCluster).\n" +
                 "⚠ 하한 3 — 2 이하면 '무리'로 안 읽히고 매 처치마다 집결지가 바뀌어 어지럽다.")]
        [Min(3)]
        [SerializeField] private int clusterSize = 4;

        [Tooltip("무리의 반경(m). 키우면 무리 안에서도 거리 선택지가 생겨 정지 구간이 줄지만,\n" +
                 "'무리'라기보다 '느슨한 그룹'으로 읽힌다.")]
        [SerializeField] private float clusterRadius = 2f;

        [Tooltip("무리 이동 속도(m/s). 결투 접근(cruiseSpeed)과 다른 값이어야 한다 — 이건 합류이지 돌진이 아니다.")]
        [SerializeField] private float clusterMoveSpeed = 3f;

        [Tooltip("무리 스폰 시 한 명씩 나오는 간격(초). 전원이 동시에 나타나면 팝인이 티 난다.")]
        [SerializeField] private float spawnStagger = 0.15f;

        [Tooltip("스폰이 화면에 걸릴 때 자기 자리에서 밀어낼 수 있는 최대 거리(m).\n" +
                 "자리가 이미 화면 밖이면 0 — 그 자리에 그대로 선다.")]
        [SerializeField] private float spawnMaxOffset = 6f;

        [Header("Wander")]
        [Tooltip("교전 중이 아닌 적이 플레이어 주위를 배회한다. 끄면 자기 자리에 정지(예전 동작).\n" +
                 "⚠ active 무리 전용이다 — staged가 배회하면 집결 자체가 무너진다.")]
        [SerializeField] private bool wanderEnabled = true;

        [Tooltip("배회하며 플레이어와 유지할 거리(m). ⚠ duelDistance보다 커야 한다 — 교전 시 좁혀 들어갈 여지.")]
        [SerializeField] private float standoffDistance = 3.5f;

        [Tooltip("배회 이동 속도(m/s). ⚠ 걷기 클립의 실제 루트 이동 속도와 같아야 발이 안 미끄러진다.\n" +
                 "Walk 4종 실측값이 정확히 1.297 m/s다.")]
        [SerializeField] private float wanderSpeed = 1.3f;

        [Tooltip("궤도가 도는 속도(도/초).")]
        [SerializeField] private float orbitSpeed = 15f;

        [Tooltip("이 간격(초, 랜덤 범위)마다 궤도 방향을 뒤집는다. 한 방향으로만 돌면 회전목마로 보인다.")]
        [SerializeField] private Vector2 orbitReverseInterval = new Vector2(3f, 7f);

        [Tooltip("궤도 각도의 개체별 랜덤 오프셋(도). 0이면 넷이 정확한 원 위에 서서 인공적이다.")]
        [SerializeField] private float orbitJitter = 12f;

        [Header("Roster")]
        [Tooltip("등장시킬 적 종류. 여러 종을 넣으면 같은 모델 반복이 티 나지 않는다.")]
        [SerializeField] private EnemyDefinition[] rosterPool;

        [Header("Duel")]
        [Tooltip("적 공격 클립의 자동 배속 상한. 넘으면 정렬이 깨지므로 경고가 뜬다.")]
        [SerializeField] private float maxAttackSpeed = 2.5f;

        [Tooltip("만나는 지점의 플레이어 몫. 1이면 적은 제자리에 서고 플레이어가 전부 좁힌다.\n" +
                 "적이 정지 표적으로 보이지 않게 하는 것은 이제 '마중 한 걸음'이 아니라 견제 클립(Pattern.EnemyFeint)이다 —\n" +
                 "1보다 낮추면 적 이동이 창에 비례해 커져 그 견제 구간이 0.735배로 잘린다.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float playerShare = 1f;

        [Tooltip("견제 클립을 걸 최소 창(초). 표적이 되는 순간부터 임팩트까지 이보다 짧으면 재생하지 않고 Idle로 둔다.\n" +
                 "너무 짧은 재생은 동작이 아니라 깜빡임으로 보인다.")]
        [SerializeField] private float minFeintWindow = 0.35f;

        [Tooltip("플레이어의 목표 이동 속도(m/s). 다음 표적까지의 거리를 이 속도로 역산해 고른다.\n" +
                 "창은 음악이 정해 못 바꾸므로 이 값이 곧 체감 속도다.")]
        [SerializeField] private float cruiseSpeed = 4.5f;
        [Tooltip("표적 선택의 최소 거리(m). 너무 가까운 적만 고르면 제자리 난타가 된다.")]
        [SerializeField] private float minTargetDistance = 2f;

        [Tooltip("플레이어가 cruiseSpeed로 갈 수 있는 시간에 더해 주는 여유(초).\n" +
                 "0이면 딱 cruiseSpeed로 달려 '천천히 다가가는 압박감'이 사라진다.\n" +
                 "⚠ 상수여야 한다 — 창의 비율로 만들면 창이 길수록 느려지는 문제가 되돌아온다.")]
        [SerializeField] private float playerArriveSlack = 0.15f;

        [Tooltip("실패해서 회피할 때 뒤로 물러나는 거리(m). 물러난 그 자리에 선다 — 플레이어가 다시 찾아간다.\n" +
                 "⚠ 다음 패턴의 창이 감당할 때만 물러난다. 못 감당하면 제자리 패링이 된다.")]
        [SerializeField] private float failRetreatDistance = 1.5f;
        [Tooltip("후퇴에 걸리는 시간(초). 이 시간이 지나야 다시 접근을 시작한다.")]
        [SerializeField] private float retreatDuration = 0.25f;

        [Tooltip("회피/패링을 가르는 여유(초). 후퇴 + 되돌아오기에 이만큼 더 남아야 물러난다.\n" +
                 "키우면 패링이 잦아지고, 0에 가까우면 회피가 잦아지는 대신 재접근이 빠듯해진다.")]
        [SerializeField] private float retreatWindowMargin = 0.35f;

        [Header("Chain")]
        [Range(0f, 1f)]
        [Tooltip("사슬(한 적에게 이어지는 패턴들)을 처치로 인정할 성공 타수의 비율.\n" +
                 "1이면 전부 성공해야 하고, 0에 가까우면 마지막 타만 맞으면 된다.\n" +
                 "비사슬 엔트리(길이 1)는 어느 값이든 1타 필요 — 기존 채보 동작과 같다.")]
        [SerializeField] private float chainKillRatio = 0.6f;

        [Tooltip("회피한 적에게 다시 다가갈 때, 남은 빈 시간(=1) 중 이동에 쓸 비율.\n" +
                 "0.5면 절반 만에 도착하고 나머지 절반은 서서 기다린다 — 같은 거리를 절반 시간에 가므로 속도는 2배다.\n" +
                 "1이면 예전과 같다(창 전체를 이동에 쓴다). 재접근에만 걸리고 새 표적으로 가는 주 경로는 건드리지 않는다.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float playerApproachShare = 1f;

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

        /// <summary>교전 상대가 바뀌는 순간. 카메라 재프레이밍·UI·SFX가 본체 수정 없이 붙는다.</summary>
        public event Action<EnemyView, EnemyView> OnOpponentChanged;

        /// <summary>
        /// 적의 <b>죽음이 확정된 순간</b>(패턴 완료). 승격·링 보충과 같은 시점이라 이 셋은 갈라지면 안 된다.
        /// <b>화면에서는 아직 아무 일도 안 일어났다</b> — 갈라지는 순간은 <see cref="OnEnemyBurst"/>다.
        /// </summary>
        public event Action<EnemyView> OnEnemyKilled;

        /// <summary>
        /// 적이 <b>실제로 갈라지는 순간</b>(사망 클립의 트림 끝). 화면에서 사건이 일어나는 시각이라
        /// 카메라 쉐이크처럼 '보이는 것에 붙는' 연출은 이쪽을 구독한다.
        /// </summary>
        public event Action<EnemyView> OnEnemyBurst;

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

            /// <summary>
            /// 플레이어가 자기 자리에 도착할 시각. 보통 <see cref="ArriveTime"/>과 같지만,
            /// 같은 상대와 이어 싸우는 재접근(회피 뒤)에서는 <c>playerApproachShare</c>만큼 <b>앞당겨진다</b> —
            /// 그 구간은 거리가 창에 맞춰지지 않아 그냥 두면 늘어져 기어간다.
            ///
            /// <para><b>앞당기기만 한다.</b> 늦추면 대시가 플레이어 자기 스윙 한복판으로 들어간다
            /// (스윙 시작 시각은 이 시점에 알 수 없다 — <c>docs/FailConverge</c>의 폐기된 DepartTime 안).
            /// 일찍 도착해 서 있는 것은 언제나 안전하다(적의 <c>EarliestArrival</c>과 같은 규율).</para>
            /// </summary>
            public readonly float PlayerArriveTime;

            public DuelPlan(Vector3 player, Vector3 enemy, float arriveTime, float playerArriveTime)
            {
                PlayerPosition = player;
                EnemyPosition = enemy;
                ArriveTime = arriveTime;
                PlayerArriveTime = playerArriveTime;
            }
        }

        /// <summary>결투 배치가 정해진 순간. 플레이어 이동이 이것만 구독한다.</summary>
        public event Action<DuelPlan> OnDuelScheduled;

        /// <summary>
        /// 실패한 적이 재생하는 반응 클립. <b>같은 실패라도 화면에 남는 사실이 다르다</b> —
        /// 막으면 칼이 맞부딪히고, 물러나면 칼이 아예 닿지 않는다.
        /// </summary>
        public enum EnemyReaction
        {
            /// <summary>제자리에서 막아 세웠다(<c>parryStateName</c>). 칼이 만나는 유일한 실패다.</summary>
            Parry,

            /// <summary>물러났다(<c>evadeStateName</c>). <c>Attacker.Enemy</c>에서는 플레이어 피격의 여파다.</summary>
            Evade
        }

        /// <summary>
        /// 실패로 적이 반응 클립을 재생하는 순간(<b>확정</b>). 어떤 반응인지와 <b>칼이 만나는 시각</b>을 함께 준다.
        ///
        /// <para><b>왜 <c>OnPatternComplete</c>로는 안 되나</b>: 그 페이로드에는 막았는지 굴렀는지가 없다.
        /// 그건 다음 패턴의 창을 보고 여기서 정하며(<see cref="ResolveRetreatDistance"/>),
        /// 실패를 한 덩어리로 보면 <b>적이 뒤로 구르는 동안 허공에서 스파크가 튄다</b>.</para>
        ///
        /// <para><b>왜 임팩트 시각을 같이 주나</b>: 확정은 마지막 노드를 입력한 순간이고 칼이 만나는 것은
        /// 그보다 <c>goodWindow + ImpactOffset</c> 뒤다(<see cref="OnEnemyKilled"/>와 <see cref="OnEnemyBurst"/>가
        /// 갈려 있는 것과 같은 이유). 시각을 실어 보내면 구독자는 예약만 하면 된다.</para>
        /// </summary>
        public event Action<Pattern, EnemyReaction, float> OnEnemyReacted;

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

            /// <summary>실제로 갈라지는 시각 = <b>임팩트 프레임</b>. 히트스톱이 걸리면 정지 시간만큼 뒤로 밀린다.</summary>
            public float burstTime;
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
        private readonly List<Vector3> positionScratch = new List<Vector3>();
        private int nextToken;

        // BakeMesh 대상 메쉬 재사용 큐. 교체마다 new Mesh()를 만들면 GC 압박이 된다.
        private readonly Queue<Mesh> frozenMeshPool = new Queue<Mesh>();

        private EnemyView currentOpponent;

        // 사슬 누적. 수명은 currentOpponent와 같다 — 상대가 바뀌는 곳이 곧 리셋 지점이다.
        // 임계 미달로 살아남은 적은 리셋하지 않는다: 계속 맞아 온 적이라 다음 사슬에서 성공이 누적돼 결국 죽는다.
        private int chainHits;
        private int chainSuccesses;

        /// <summary>
        /// 지금 교전 중인 상대. 없으면 null.
        ///
        /// <para><see cref="OnOpponentChanged"/>만으로는 <b>구독 시점의 상태를 알 수 없다</b> —
        /// 구독보다 먼저 승격이 일어났으면 그 이벤트는 이미 지나갔다. 구독 직후 한 번 읽어 동기화하라고 연다.</para>
        /// </summary>
        public EnemyView CurrentOpponent => currentOpponent;

        /// <summary>적 클립의 자동 배속 상한. 기습도 같은 상한을 써야 정렬 규율이 한 벌로 남는다.</summary>
        public float MaxAttackSpeed => maxAttackSpeed;

        /// <summary>지금 무대에 살아 있는 적들(읽기 전용). 구르기 방향처럼 "적이 없는 쪽"을 재는 연출이 쓴다.</summary>
        public IReadOnlyList<EnemyView> Ring => ring;

        /// <summary>무대 반경. 구르기가 무대 밖으로 나가지 않게 하는 데 쓴다.</summary>
        public float StageRadius => stageRadius;

        /// <summary>무대 중심(월드 고정).</summary>
        public Vector3 ArenaCenter => Center;

        // ── 무리 상태 ────────────────────────────────────────────────────────
        // ring(전체 목록)은 그대로 두고 소속만 따로 든다. 무리를 통째로 옮기려면
        // 누가 그 무리인지 확정적으로 알아야 한다 — 위치로 추정하면 후퇴·결투로 흔들린다.
        private readonly List<EnemyView> activeCluster = new List<EnemyView>();
        private readonly List<EnemyView> stagedCluster = new List<EnemyView>();

        /// <summary>표적 후보 버퍼. positionScratch와 인덱스가 1:1이어야 한다.</summary>
        private readonly List<EnemyView> candidateScratch = new List<EnemyView>();

        /// <summary>다음 무리가 놓인 방향. 집결지를 새로 지정할 때만 다시 뽑는다.</summary>
        private Vector3 stagedDirection;

        /// <summary>
        /// <b>집결지</b> — 다음 무리가 모이는 고정된 한 점.
        ///
        /// <para><b>한 번 정하면 무리가 찰 때까지 안 움직인다.</b> 예전에는 창이 바뀔 때마다 무리를 통째로
        /// 옮겼는데(재배치), 플레이 결과 <b>적들이 우르르 몰려다니는 그림</b>이 되어 폐기했다.
        /// 지금은 점이 고정이고 <b>적이 하나씩 태어나 거기로 걸어와 합류</b>한다.</para>
        /// </summary>
        private Vector3 stagedCenter;

        /// <summary>
        /// 마지막으로 계산된 목표 거리. 집결지를 지정할 때 쓴다 —
        /// 사망 시점에는 창을 알 수 없으므로 <see cref="BindReservation"/>이 지나가며 남겨 둔다.
        /// </summary>
        private float lastDesiredDistance;

        /// <summary>무대에 유지할 총원. 무리 모드에서는 <b>사망 1 : 스폰 1</b>이라 언제나 clusterSize다.</summary>
        private int RingCapacity => clusterEnabled ? Mathf.Max(clusterSize, 1) : ringCount;

        // 프리팹별 자체 풀(EffectManager·SliceTargetDirector 선례). Pool(PoolKey 단일 매핑)은 프리팹 수 증가에 맞지 않는다.
        private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
        private readonly Dictionary<GameObject, int> maxSizes = new Dictionary<GameObject, int>();
        private Transform poolRoot;

        /// <summary>
        /// 무대의 중심. <b>월드에 고정된 상수다</b> — <c>arenaCenter</c>는 무대 오브젝트를 가리켜야 하며
        /// 플레이어를 가리키면 안 된다.
        ///
        /// <para>예전에는 여기가 플레이어였고, 그래서 배치·스폰·계획이 전부 플레이어를 따라다녔다.
        /// 그 위에 월드 고정점(리시) 개념을 하나만 덧댄 탓에 <b>모델이 둘</b>이었고, 표류 버그가 그 증상이었다.
        /// 무대를 고정하면 이 값이 상수가 되어 그 부류가 원천적으로 사라진다.</para>
        /// </summary>
        private Vector3 Center => arenaCenter != null ? arenaCenter.position : transform.position;

        /// <summary>플레이어 위치. 배치·표적 선택이 "플레이어에게서 얼마나 먼가"를 재는 데 쓴다.</summary>
        private Vector3 PlayerPosition => duelAnchor != null ? duelAnchor.position : Center;

        // 절두체 평면 6장. 스폰마다 배열을 새로 만들지 않도록 재사용한다.
        private readonly Plane[] frustumScratch = new Plane[6];

        /// <summary>
        /// "이 위치가 화면 안인가"를 판정하는 함수를 만든다. 적의 몸을 대략적인 구로 보고
        /// <b>발밑 한 점이 아니라 부피</b>로 판정한다 — 한 점만 보면 머리가 화면에 걸린 채로 통과한다.
        /// </summary>
        private System.Func<Vector3, bool> BuildVisibilityTest(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, frustumScratch);
            var size = new Vector3(1.2f, 2f, 1.2f);

            return position => GeometryUtility.TestPlanesAABB(
                frustumScratch, new Bounds(position + Vector3.up, size));
        }

        /// <summary>
        /// 카메라 폴백까지 포함한 시야 판정 함수. 카메라가 없으면 null(= 판정 없음)을 돌려준다.
        /// 기습 후보를 고르는 쪽(<c>DodgeDirector</c>)이 같은 도구를 쓰게 열어 둔다 — 판정을 두 벌로 만들지 않는다.
        /// </summary>
        public System.Func<Vector3, bool> BuildVisibilityTest()
        {
            var cam = viewCamera != null ? viewCamera : Camera.main;
            return cam != null ? BuildVisibilityTest(cam) : null;
        }

        /// <summary>
        /// 패턴 밖 공백에 기습할 <b>노는 적</b> 하나를 고른다. 없으면 null.
        ///
        /// <para>후보는 <b><c>activeCluster</c>로 묶는다</b>(§11-6, <see cref="TakeTargetForWindow"/>와 같은 근거) —
        /// <c>staged</c>를 집으면 집결 중인 적이 이탈해 무리 모델이 깨진다.</para>
        ///
        /// <para><b>기습 클립이 없는 적은 여기서 걸러낸다.</b> 무연출 기습은 "회피할 대상이 없는 회피 프롬프트"라
        /// 존재할 수 없다 — 다른 무연출 폴백들과 성질이 다르다.</para>
        ///
        /// <para><b>이동 시간은 보지 않는다.</b> 기습자는 사전 접근으로 이미 붙어 있고, 못 따라온 경우는
        /// 발동 시점의 <see cref="EnemyView.IsIdle"/>가 거른다.</para>
        /// </summary>
        /// <param name="hasAmbushClip">이 적이 쓸 기습 클립이 있는가. 폴백 판단은 호출자가 안다.</param>
        public EnemyView PickIdleAmbusher(System.Func<Vector3, bool> isVisible, System.Func<EnemyView, bool> hasAmbushClip)
        {
            candidateScratch.Clear();

            foreach (var view in activeCluster)
            {
                if (view == null || view == currentOpponent) continue;
                if (!view.IsIdle) continue;
                if (isVisible != null && !isVisible(view.transform.position)) continue;
                if (hasAmbushClip != null && !hasAmbushClip(view)) continue;

                candidateScratch.Add(view);
            }

            if (candidateScratch.Count == 0) return null;

            return candidateScratch[UnityEngine.Random.Range(0, candidateScratch.Count)];
        }

        /// <summary>
        /// 기습이 끝난 적을 놓아준다 — <b>제자리에 남아 배회로 돌아간다</b>. 성패로 가르지 않는다.
        ///
        /// <para><b>⚠ 물러나지 않는다</b>(정정 8). 후퇴는 *"베이려다 피했다"*의 후속 동작이라 기습에는 붙지 않는다 —
        /// 무리에서 하나가 튀어나와 찌르고, 그 자리에서 다시 무리로 섞이는 것이 이 사건의 전부다.
        /// 그래서 무대 계산(<c>PickRetreatTarget</c>)도 이 경로에는 없다.</para>
        /// </summary>
        public void ReleaseAmbusher(EnemyView view) => view?.ReleaseAction();

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

            if (clusterEnabled)
            {
                // 첫 무리만 세운다. 다음 무리는 <b>사망마다 한 명씩</b> 집결지로 모여 스스로 채워진다 —
                // 미리 다 세우면 곡 시작부터 두 덩어리가 보여 원래 문제(정신없음)로 되돌아간다.
                activeCluster.Clear();
                stagedCluster.Clear();

                Vector3 activeCenter = EnemyRing.PickClusterCenter(
                    PlayerPosition, Vector3.forward, minTargetDistance * 2f, Center, stageRadius, minPlayerDistance);
                SpawnCluster(activeCluster, activeCenter);

                DesignateStagedCenter();
            }
            else
            {
                for (int i = ring.Count; i < RingCapacity; i++)
                    SpawnIntoStage();
            }

            ReleaseCurrentOpponent();
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

        /// <summary>
        /// 무대에 적 하나를 세운다. 위치는 <b>플레이어 시야 밖</b>에서 고른다.
        ///
        /// <para><b>걸어 들어오지 않는다.</b> 어차피 화면 밖에서 나타나므로 등장 이동은 아무도 못 본다 —
        /// 그 구간을 없애 팝인 연출과 그 비용을 같이 지웠다.</para>
        ///
        /// <para>시야 판정은 각도 근사가 아니라 <b>실제 절두체</b>다. 무대가 월드에 고정되면
        /// 적도 플레이어도 원 안 어디에나 있어서 "카메라 yaw와의 각차"로는 화면 안인지 알 수 없다.</para>
        /// </summary>
        private EnemyView SpawnIntoStage()
        {
            var definition = PickDefinition();
            if (definition == null) return null;

            positionScratch.Clear();
            foreach (var e in ring) positionScratch.Add(e.transform.position);
            if (currentOpponent != null) positionScratch.Add(currentOpponent.transform.position);

            var cam = viewCamera != null ? viewCamera : Camera.main;
            System.Func<Vector3, bool> isVisible = cam != null ? BuildVisibilityTest(cam) : null;

            Vector3 stagePos = EnemyRing.PickStagePosition(
                positionScratch, Center, stageRadius,
                PlayerPosition,
                cam != null ? cam.transform.position : Center,
                ViewForward,
                minSpacing, minPlayerDistance,
                isVisible, () => UnityEngine.Random.value, spawnCandidateCount);

            var go = Rent(definition.Prefab, definition.MaxPoolSize);
            if (go == null) return null;

            var view = go.GetComponent<EnemyView>();
            if (view == null) view = go.AddComponent<EnemyView>();

            float angle = EnemyRing.DirectionToAngle(stagePos - Center);
            view.Setup(definition, angle, stagePos, stagePos, 0.01f, PlayerPosition);

            // 무대에 선 적 전원이 플레이어를 노려본다. 대상은 트랜스폼이라 플레이어가 움직여도 따라간다.
            view.SetGazeTarget(duelAnchor != null ? duelAnchor : transform);
            view.ApplyBackgroundBudget(true);
            ring.Add(view);
            return view;
        }

        // ── 무리 (docs/EnemyCluster) ──────────────────────────────────────────

        /// <summary>
        /// 무리 하나를 통째로 세운다. <b>각자 자기 자리 근처에 나타나 짧게 걸어 들어온다</b> —
        /// 무대 가장자리에서 걸어오게 하면 첫 이동만 무대 횡단(최대 16m)이 되어 재배치 예산으로 감당할 수 없다.
        ///
        /// <para><paramref name="stagger"/>로 한 명씩 시차를 둔다. 넷이 동시에 나타나면 팝인이 티 난다.</para>
        /// </summary>
        private void SpawnCluster(List<EnemyView> cluster, Vector3 center)
        {
            var cam = viewCamera != null ? viewCamera : Camera.main;
            System.Func<Vector3, bool> isVisible = cam != null ? BuildVisibilityTest(cam) : null;
            Vector3 viewPosition = cam != null ? cam.transform.position : Center;

            int count = Mathf.Max(clusterSize, 1);
            for (int i = cluster.Count; i < count; i++)
            {
                Vector3 slot = EnemyRing.PlaceInCluster(center, i, count, clusterRadius, minSpacing);
                var view = SpawnAt(slot, isVisible, viewPosition, i * Mathf.Max(spawnStagger, 0f));
                if (view == null) return; // 풀이 말랐다 — 다음 기회에 다시 시도한다

                cluster.Add(view);
            }
        }

        /// <summary>
        /// 적 하나를 <paramref name="slot"/>에 세운다. 화면에 걸리면 가장 가까운 화면 밖 지점에서 걸어 들어온다.
        ///
        /// <para><b>등장 연출은 여기 하나로 격리돼 있다.</b> 후속 파티클 등장은 이 메서드만 갈아끼우면 되고,
        /// 그때는 오프셋도 절두체 판정도 통째로 필요 없어진다(자리에 바로 나타나면 되므로).</para>
        /// </summary>
        private EnemyView SpawnAt(Vector3 slot, System.Func<Vector3, bool> isVisible, Vector3 viewPosition, float delay)
        {
            var definition = PickDefinition();
            if (definition == null) return null;

            Vector3 from = EnemyRing.PickSpawnNearCluster(
                slot, Center, stageRadius, PlayerPosition, minPlayerDistance,
                viewPosition, ViewForward, isVisible, spawnMaxOffset);

            var go = Rent(definition.Prefab, definition.MaxPoolSize);
            if (go == null) return null;

            var view = go.GetComponent<EnemyView>();
            if (view == null) view = go.AddComponent<EnemyView>();

            // 오프셋이 0이면(자리가 이미 화면 밖) 이동 없이 그 자리에 선다 — 정상 경로다.
            float travel = Vector3.Distance(from, slot);
            float duration = travel <= 0.01f ? 0.01f : travel / Mathf.Max(clusterMoveSpeed, 0.1f);

            float angle = EnemyRing.DirectionToAngle(slot - Center);
            view.Setup(definition, angle, slot, from, duration + Mathf.Max(delay, 0f), PlayerPosition);

            view.SetGazeTarget(duelAnchor != null ? duelAnchor : transform);
            view.ApplyBackgroundBudget(true);
            ring.Add(view);
            return view;
        }

        /// <summary>
        /// 다음 무리가 모일 <b>집결지를 새로 지정한다</b>. 무리가 비었을 때만 부른다 —
        /// 한 번 정하면 그 무리가 다 찰 때까지 고정이다.
        ///
        /// <para><b>무리를 통째로 옮기지 않는다.</b> 창이 바뀔 때마다 재배치하던 예전 방식은
        /// 플레이 결과 <b>적들이 우르르 몰려다니는 그림</b>이 되어 폐기했다. 거리를 음악에 맞추는 일은
        /// 이 한 번의 지정이 하고, 그 뒤로는 <b>적이 하나씩 걸어와 합류</b>할 뿐이다.</para>
        /// </summary>
        private void DesignateStagedCenter()
        {
            // 사망 시점에는 창을 알 수 없다. BindReservation이 지나가며 남긴 마지막 목표 거리를 쓴다.
            float desired = lastDesiredDistance > 0f ? lastDesiredDistance : minTargetDistance * 2f;
            desired = Mathf.Clamp(desired, minTargetDistance, stageRadius * 2f);

            Vector3 avoidCenter = ClusterCenterOf(activeCluster, PlayerPosition);
            float avoidRadius = activeCluster.Count > 0 ? clusterRadius * 2f : 0f;

            // 방향은 매번 새로 뽑는다 — 무리가 통째로 안 움직이므로 요동이 생길 여지가 없다.
            stagedDirection = EnemyRing.PickClusterDirection(
                PlayerPosition, Vector3.zero, Center, stageRadius, desired,
                avoidCenter, avoidRadius, () => UnityEngine.Random.value);

            stagedCenter = EnemyRing.PickClusterCenter(
                PlayerPosition, stagedDirection, desired, Center, stageRadius, minPlayerDistance);
        }

        /// <summary>
        /// 집결지에 적 <b>하나</b>를 태워 보낸다. 사망 하나당 정확히 한 번 불린다.
        ///
        /// <para>자리는 <b>이미 모인 인원 수</b>가 정한다 — 무리가 채워지는 순서대로 대형이 완성된다.
        /// 무리가 이미 찼으면 아무것도 하지 않는다(승격이 비워 줄 때까지 기다린다).</para>
        /// </summary>
        private void SpawnOneIntoStaged()
        {
            int count = Mathf.Max(clusterSize, 1);
            if (stagedCluster.Count >= count) return;

            var cam = viewCamera != null ? viewCamera : Camera.main;
            System.Func<Vector3, bool> isVisible = cam != null ? BuildVisibilityTest(cam) : null;
            Vector3 viewPosition = cam != null ? cam.transform.position : Center;

            Vector3 slot = EnemyRing.PlaceInCluster(stagedCenter, stagedCluster.Count, count, clusterRadius, minSpacing);
            var view = SpawnAt(slot, isVisible, viewPosition, 0f);
            if (view != null) stagedCluster.Add(view);
        }

        // ── 배회 (docs/EnemyIdleWander) ───────────────────────────────────────

        private float orbitPhase;
        private float orbitDirection = 1f;
        private float orbitReverseAt;

        // 개체별 각도 지터. 인스턴스ID로 뽑아 대여 동안 고정된다 — 매 프레임 랜덤이면 떨린다.
        private static float JitterOf(EnemyView view, float amount) =>
            amount <= 0f ? 0f : (Mathf.Abs(view.GetInstanceID() * 0.6180339887f % 1f) - 0.5f) * 2f * amount;

        /// <summary>
        /// 교전 중이 아닌 <b>active 무리</b>의 적들을 플레이어 주위 궤도에 세운다.
        ///
        /// <para><b>staged는 대상이 아니다.</b> 그쪽이 배회하면 집결지에 모이지 못해 무리 자체가 안 만들어진다.</para>
        ///
        /// <para><b>슬롯 인덱스는 리스트 순서로 고정한다.</b> 매 프레임 가까운 자리로 재배정하면
        /// 적끼리 자리를 바꾸며 서로를 가로지른다.</para>
        /// </summary>
        private void TickWander()
        {
            if (!clusterEnabled || !wanderEnabled) return;

            // 한 방향으로만 돌면 회전목마다. 주기적으로 뒤집는다.
            if (Time.time >= orbitReverseAt)
            {
                orbitDirection = -orbitDirection;
                orbitReverseAt = Time.time + UnityEngine.Random.Range(
                    Mathf.Min(orbitReverseInterval.x, orbitReverseInterval.y),
                    Mathf.Max(orbitReverseInterval.x, orbitReverseInterval.y));
            }

            orbitPhase += orbitSpeed * orbitDirection * Time.deltaTime;

            Vector3 player = PlayerPosition;
            int count = Mathf.Max(activeCluster.Count, 1);

            for (int i = 0; i < activeCluster.Count; i++)
            {
                var view = activeCluster[i];
                if (view == null) continue;

                // 지금 싸우는 상대는 배회하지 않는다. 나머지 금지 조건은 EnemyView가 스스로 거른다.
                if (view == currentOpponent) { view.StopWander(); continue; }

                Vector3 slot = EnemyRing.PickOrbitSlot(
                    player, i, count, standoffDistance, orbitPhase,
                    Center, stageRadius, JitterOf(view, orbitJitter));

                view.SetWander(slot, wanderSpeed);
            }

            // staged는 집결지에 서 있어야 한다 — 혹시 켜져 있으면 끈다.
            foreach (var view in stagedCluster)
                if (view != null) view.StopWander();
        }

        /// <summary>무리의 무게중심. 비면 <paramref name="fallback"/>.</summary>
        private static Vector3 ClusterCenterOf(List<EnemyView> cluster, Vector3 fallback)
        {
            if (cluster == null || cluster.Count == 0) return fallback;

            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var e in cluster)
            {
                if (e == null) continue;
                sum += e.transform.position;
                n++;
            }

            return n == 0 ? fallback : sum / n;
        }

        /// <summary>
        /// 죽거나 사라진 적을 무리 목록에서 걷어낸다. <c>ring</c>에서 빠지는 곳마다 같이 불러야
        /// 무리가 유령을 들고 있지 않는다.
        /// </summary>
        private void ForgetFromClusters(EnemyView view)
        {
            activeCluster.Remove(view);
            stagedCluster.Remove(view);
        }

        /// <summary>
        /// 지금 무리가 비었으면 다음 무리를 승격시키고 새 다음 무리를 채운다.
        ///
        /// <para>승격과 동시에 <see cref="stagedDirection"/>을 비운다 — 새 무리는 방향을 처음부터 고른다.
        /// 이월하면 방금 승격된 무리와 같은 방향에 겹쳐 놓인다.</para>
        /// </summary>
        private void PromoteClusterIfEmpty()
        {
            if (!clusterEnabled || activeCluster.Count > 0) return;

            activeCluster.AddRange(stagedCluster);
            stagedCluster.Clear();

            // 무리가 비었으니 다음 집결지를 새로 지정한다. 이후 사망마다 한 명씩 여기로 걸어온다.
            DesignateStagedCenter();
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
        /// 죽은 상대를 놓아준다. <b>다음 상대를 고르지 않는다</b> — 고르는 일은 <see cref="BindReservation"/>이 한다.
        ///
        /// <para>표적을 창(음악이 정한다)으로 고르는데, 창은 배정 시점에서야 알 수 있기 때문이다.
        /// 둘은 <see cref="ResolveReservation"/> 한 호출 안이라 <b>같은 프레임</b>이고, 그래서 옮겨도
        /// <see cref="OnOpponentChanged"/> 발행 타이밍이 실질적으로 바뀌지 않는다.</para>
        /// </summary>
        private void ReleaseCurrentOpponent()
        {
            currentOpponent = null;
        }

        /// <summary>
        /// <b>다음 표적을 창이 고른다</b> — 여기가 속도감의 심장이다.
        ///
        /// <para>창은 음악이 정해 0.5~2.1초로 4배 흔들린다. 거리를 고정하면 <b>속도가 그만큼 흔들린다.</b>
        /// 거꾸로 <c>cruiseSpeed × 창</c>을 목표 거리로 삼으면 <b>체감 속도가 일정</b>해지고,
        /// 짧은 구간은 근거리 난타 / 긴 구간은 무대 횡단 대시로 자연히 갈린다.</para>
        ///
        /// <para><b><c>playerShare</c>로 나누는 것이 핵심이다.</b> 플레이어는 간격의 일부만 가므로
        /// 그 몫이 <c>cruiseSpeed × 창</c>이 되려면 간격은 그보다 커야 한다.
        /// 안 나누면 비율을 올릴수록 오히려 느려진다.</para>
        /// </summary>
        private EnemyView TakeTargetForWindow(float window, float duelDistance)
        {
            if (ring.Count == 0) return null;

            Vector3 from = PlayerPosition;

            float desired = cruiseSpeed * Mathf.Max(window, 0f) / Mathf.Max(playerShare, 0.01f) + duelDistance;
            desired = Mathf.Clamp(desired, minTargetDistance, stageRadius * 2f);

            // 집결지 지정은 사망 시점에 일어나 창을 볼 수 없다. 여기서 마지막 값을 남겨 둔다.
            lastDesiredDistance = desired;

            // ⚠ 무리 모드에서는 후보를 '지금 싸우는 무리'로 묶는다. 링 전체에서 거리로 고르면
            // desired가 클 때 다음 무리(staged)를 집어 무리 모델이 통째로 깨진다 —
            // 플레이어가 두 무리 사이를 왔다 갔다 하게 된다.
            candidateScratch.Clear();
            positionScratch.Clear();

            bool restrict = clusterEnabled && activeCluster.Count > 0;
            foreach (var e in ring)
            {
                if (restrict && !activeCluster.Contains(e)) continue;
                candidateScratch.Add(e);
                positionScratch.Add(e.transform.position);
            }

            // 무리가 비었는데 링에는 남아 있다(승격 직전 등) — 그때만 링 전체로 폴백한다.
            if (candidateScratch.Count == 0)
            {
                foreach (var e in ring)
                {
                    candidateScratch.Add(e);
                    positionScratch.Add(e.transform.position);
                }
            }

            int index = EnemyRing.PickTargetByDistance(positionScratch, from, desired);
            if (index < 0) return null;

            var picked = candidateScratch[index];
            ring.Remove(picked);
            picked.ApplyBackgroundBudget(false);
            return picked;
        }

        /// <summary>
        /// 이번 패턴에서 둘이 유지할 간격(m). <b>앵커가 기준 거리의 단일 출처다</b> —
        /// 앵커는 플레이어 자식이므로 플레이어까지의 평면 거리가 곧 기본 간격이고, 패턴이 리치만큼 보정한다.
        /// 툴(<c>짝 에디터</c>·<c>슬라이서</c>)의 `기준 결투 거리`에 같은 값을 넣어야 그림이 일치한다.
        /// </summary>
        private float DuelDistanceOf(Pattern template)
        {
            // 앵커가 없으면 툴 기본값과 같은 1m로 본다 — 두 곳이 다르면 프리뷰와 게임이 어긋난다.
            float baseDistance = duelAnchor != null && duelAnchor.parent != null
                ? Vector3.ProjectOnPlane(duelAnchor.position - duelAnchor.parent.position, Vector3.up).magnitude
                : 1f;

            if (baseDistance < 0.1f) baseDistance = 1f;

            return Mathf.Max(baseDistance + (template != null ? template.DuelDistanceOffset : 0f), 0.1f);
        }

        /// <summary>
        /// 둘이 만나는 배치를 만든다. <b>비율은 <see cref="playerShare"/>가 정한다</b>(0.85 = 8:2 남짓).
        ///
        /// <para>예전에는 0.5 고정이라 둘이 똑같이 절반씩 왔고, 거기에 리시까지 걸려 플레이어 몫이
        /// 1.5m로 잘렸다 — <b>초속 1m, 걷는 것보다 느렸다.</b> 무대가 월드에 고정된 지금은
        /// 리시가 필요 없다(무대 자체가 경계이고 표적이 무대 안이므로 플레이어도 무대 안에 남는다).</para>
        ///
        /// <para><b>적을 완전히 세우지는 않는다.</b> 1로 두면 정지 표적으로 읽힌다 —
        /// 한 걸음이라도 마중 나와야 교전으로 보인다. 그 몫이 창 전체로 늘어져 기어가는 문제는
        /// <c>EnemyView.EarliestArrival</c>이 막는다(빨리 가서 서고 플레이어를 바라본다).</para>
        /// </summary>
        private DuelPlan BuildDuelPlan(EnemyView opponent, Pattern template, float arriveTime, float playerArriveTime)
        {
            Vector3 player = PlayerPosition;
            float distance = DuelDistanceOf(template);

            // 상대가 없으면(디버그 경로) 플레이어는 제자리, 적 자리만 앞에 잡아 준다.
            if (opponent == null)
                return new DuelPlan(player, player + Vector3.forward * distance, arriveTime,
                                    ResolvePlayerArrival(playerArriveTime, 0f));

            // '지금 위치'가 아니라 '갈 곳'으로 잡는다 — 배정 순간 적이 이동 중이면(후퇴 등)
            // transform.position은 곧 떠날 위치다. 후퇴에서는 "둘 다 제자리"로 계산되어 플레이어가 안 붙는다.
            Vector3 toEnemy = Vector3.ProjectOnPlane(opponent.Destination - player, Vector3.up);
            Vector3 dir = toEnemy.sqrMagnitude < 1e-6f ? Vector3.forward : toEnemy.normalized;

            Vector3 meet = player + toEnemy * playerShare;
            Vector3 playerTarget = meet - dir * (distance * playerShare);
            playerTarget.y = player.y;

            // 도착 마감을 실제 거리로 조인다 — 여기서 한 번 조이면 PlayerCombatMover(이동 배속)와
            // CharacterActionPlayer(convergeUntil)가 같은 값을 읽으므로 둘이 어긋날 수가 없다.
            float travel = Vector3.ProjectOnPlane(playerTarget - player, Vector3.up).magnitude;

            return new DuelPlan(playerTarget, playerTarget + dir * distance, arriveTime,
                                ResolvePlayerArrival(playerArriveTime, travel));
        }

        /// <summary>
        /// 플레이어가 <b>실제로 도착할 수 있는 가장 이른 시각</b>. <c>EnemyView.EarliestArrival</c>의 플레이어 판이다.
        ///
        /// <para><b>왜 필요한가</b>: <c>PlayerCombatMover</c>는 이동을 마감까지 늘려 쓴다 — 거리가 0.3m든 8m든
        /// 도착은 언제나 마감이다. 그러면 <c>convergeUntil</c>이 그 늦은 시각이 되어
        /// <b>서 있는 구간(= 애니메이션 공백)이 통째로 사라진다</b>(docs/EnemyAmbushDodge 정정 6 A).</para>
        ///
        /// <para><b>예전에는 필요 없었다</b> — <see cref="TakeTargetForWindow"/>가 <c>cruiseSpeed × 창</c>으로
        /// 거리를 잡아 속도가 이미 일정했기 때문이다. <b>§11-6 무리 배치가 그 전제를 깼다</b>:
        /// 후보가 무리(반경 2m) 안으로 묶여 거리가 창에 비례하지 않으므로, 창이 길수록 오히려 느리게 걷는다.</para>
        ///
        /// <para><b>⚠ <see cref="playerArriveSlack"/>은 상수여야 한다.</b> 창의 비율로 만들면
        /// "창에 반비례하는 속도"가 그대로 되돌아온다.</para>
        /// </summary>
        private float ResolvePlayerArrival(float latest, float distance)
        {
            if (cruiseSpeed <= 0f) return latest;

            return Mathf.Min(latest, Time.time + distance / cruiseSpeed + Mathf.Max(playerArriveSlack, 0f));
        }

        /// <summary>
        /// 플레이어가 도착할 시각. <b>재접근(같은 상대와 이어 싸움)에서만 앞당긴다.</b>
        ///
        /// <para>주 경로(새 표적)는 <see cref="TakeTargetForWindow"/>가 <c>cruiseSpeed × 창</c>으로 거리를 잡아
        /// 이미 체감 속도가 일정하다 — 거기서 창을 더 줄이면 <c>cruiseSpeed</c>를 넘어 달린다.
        /// 반면 재접근은 그 규율을 안 거쳐 거리를 <c>failRetreatDistance</c>가 통째로 정하고,
        /// 창이 길면 그 거리가 창 전체로 늘어져 <b>0.9 m/s로 기어간다</b>. 이 경로에만 비율을 건다.</para>
        /// </summary>
        private float ResolvePlayerArriveTime(float arriveTime, bool reapproach)
        {
            if (!reapproach || playerApproachShare >= 1f) return arriveTime;

            float window = arriveTime - Time.time;
            if (window <= 0f) return arriveTime;

            return Time.time + window * playerApproachShare;
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
        /// <para>여기가 <c>currentOpponent</c>를 <b>정하는</b> 유일한 지점이다. 호출 시점이 곧
        /// "이전 패턴의 성패가 반영된 뒤"이고, 동시에 <b>창을 알 수 있는 유일한 시점</b>이라
        /// 표적 선택(<see cref="TakeTargetForWindow"/>)도 여기서 한다.</para>
        /// </summary>
        private void BindReservation(Reservation r)
        {
            if (r.bound) return;
            r.bound = true;

            // 도착 시각 = 클립 시작 시각. 클립이 없으면 임팩트까지가 여유다.
            // AssignAttack이 내부에서 같은 식을 쓰지만(순수 함수) 계획을 먼저 세워야 해서 여기서 한 번 더 부른다.
            float arriveTime = r.attack != null && r.attack.IsUsable
                ? r.attack.ResolveScheduleStart(r.impactTime, Time.time)
                : r.impactTime;

            float duelDistance = DuelDistanceOf(r.template);

            // 상대가 남아 있다 = 직전 교전이 처치로 끝나지 않았다 = 같은 적에게 다시 다가간다.
            // 이 경로만 거리가 창에 안 맞춰지므로 여기서만 이동 시간을 줄인다.
            bool reapproach = currentOpponent != null;

            // 상대가 비어 있으면(직전 교전이 처치로 끝났다) 창에 맞는 거리의 적을 새로 고른다.
            // 살아 있으면 그대로 이어 싸운다 — "실패하면 같은 상대와 계속"이 여기서 지켜진다.
            if (currentOpponent == null)
            {
                var previous = currentOpponent;
                currentOpponent = TakeTargetForWindow(arriveTime - Time.time, duelDistance);
                if (currentOpponent != previous) OnOpponentChanged?.Invoke(previous, currentOpponent);

                // 새 상대 = 새 사슬. 누적은 상대와 수명을 같이한다.
                chainHits = 0;
                chainSuccesses = 0;
            }

            var opponent = currentOpponent;
            r.opponent = opponent;

            var plan = BuildDuelPlan(opponent, r.template, arriveTime, ResolvePlayerArriveTime(arriveTime, reapproach));
            lastPlan = plan;
            OnDuelScheduled?.Invoke(plan);

            if (opponent != null)
                AssignEngageClip(opponent, r, plan);

            if (r.cue.projectile != null && projectileDirector != null)
            {
                Vector3 origin = opponent != null ? opponent.RingPosition : Center;
                projectileDirector.Reserve(r.token, r.cue.projectile, r.startTime, r.impactTime, Vector2.zero, origin);
            }
        }

        /// <summary>
        /// 이 교전에서 적이 재생할 클립을 고른다. <b>두 경로는 배타적이다</b> —
        /// <c>Reservation.attack</c>은 <c>Attacker.Enemy</c>일 때만 채워지고, 견제는 <c>Attacker.Player</c> 전용이다.
        ///
        /// <para><b>견제는 이 구간이 원래 비어 있었기 때문에 존재한다.</b> 플레이어가 공격자인 패턴에서 적은
        /// 휘두르지 않으므로 클립이 없었고, 그래서 표적이 된 순간부터 베이는 순간까지 <b>가만히 서 있었다</b>.</para>
        ///
        /// <para>클립이 없거나 창이 <see cref="minFeintWindow"/>보다 짧으면 <see cref="EnemyView.AssignFeint"/>가
        /// 자리만 잡고 물러나 <b>기본 Idle</b>이 유지된다 — 예전 동작 그대로다.</para>
        /// </summary>
        private void AssignEngageClip(EnemyView opponent, Reservation r, DuelPlan plan)
        {
            var feint = AttackerOf(r.template) == Attacker.Player ? r.template?.EnemyFeint : null;

            // 창이 너무 짧으면 아예 걸지 않는다 — 깜빡임으로만 보이는 재생은 없느니만 못하다.
            bool roomy = (r.impactTime - Time.time) >= minFeintWindow;

            if (feint != null && feint.IsUsable && roomy)
            {
                opponent.AssignFeint(feint, r.impactTime, plan.EnemyPosition, plan.PlayerPosition, maxAttackSpeed);
                return;
            }

            opponent.AssignAttack(r.attack, r.impactTime, plan.EnemyPosition, plan.PlayerPosition, maxAttackSpeed);
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
                // 사슬 누적은 처치 판정보다 먼저 올린다 — 마지막 타 자신이 카운트에 들어가야 한다.
                chainHits++;
                if (playerSucceeded) chainSuccesses++;

                // killOnSuccess를 성공하고 사슬 임계를 채웠으면 처치, 아니면 죽지 않고 교전이 이어진다.
                // 이 분기가 "실패하면 같은 상대와 계속"의 유일한 소유자다 — 아래 배정은 그 결과를 읽기만 한다.
                if (r.cue.killOnSuccess && playerSucceeded && chainSuccesses >= RequiredHits(chainHits))
                    KillOpponent(opponent, ResolveDeathSet(r), r.impactTime, r.template?.EnemyDeath); // 안에서 상대 해제
                else
                {
                    // 물러날 자리는 무대를 아는 쪽이 정한다 — 뷰는 무대 중심도 반경도 모른다.
                    float retreat = ResolveRetreatDistance(r, playerSucceeded);
                    Vector3 retreatTarget = EnemyRing.PickRetreatTarget(
                        opponent.transform.position, -opponent.transform.forward, Center, stageRadius, retreat);

                    // 리액션 클립은 패턴이 소유한다. 회피(물러남)만 뷰의 고정 스테이트로 남는다 —
                    // 클립·후퇴 이동·무대 경계 클램프가 한 덩어리라 ClipAlignment 하나로 안 끝난다.
                    ClipAlignment reactionClip = playerSucceeded
                        ? r.template?.EnemyHit
                        : (retreat <= 0f ? r.template?.EnemyParry : null);

                    opponent.Resolve(playerSucceeded, AttackerOf(r.template), retreat, retreatDuration, retreatTarget,
                                     reactionClip, r.impactTime);

                    // 반응 통지 — 판정은 EnemyView.Resolve의 parried 식과 '같은 retreat 값'을 본다.
                    // 다른 값으로 다시 계산하면 애니메이션과 이펙트가 언젠가 어긋난다.
                    if (!playerSucceeded)
                    {
                        bool parried = AttackerOf(r.template) != Attacker.Enemy && retreat <= 0f;
                        OnEnemyReacted?.Invoke(r.template, parried ? EnemyReaction.Parry : EnemyReaction.Evade, r.impactTime);
                    }
                }
            }

            // 이 확정이 곧 '다음 패턴이 판정 대상이 되는 순간'이다(CLAUDE.md §3 — 같은 프레임에 즉시 승계).
            // 승격 여부가 위에서 이미 반영됐으므로 currentOpponent가 언제나 정답이다.
            BindNextReservation();
        }

        /// <summary>
        /// 실패한 적이 물러날 거리. <b>다음 패턴의 창이 감당하면 회피, 아니면 0(= 제자리 패링)</b>이다.
        ///
        /// <para><b>왜 조건부인가</b>: 물러나면 플레이어가 다시 다가가야 하는데, 그 재접근은
        /// <see cref="TakeTargetForWindow"/>를 안 거친다(같은 적이 유지되므로). 즉 거리를 창에 맞추는 규율이
        /// 빠져 있어 <b>창이 짧으면 그대로 늘어져 기어간다</b> — 실측 0.9 m/s, <see cref="cruiseSpeed"/>의 1/5.
        /// 창이 넉넉할 때만 물러나면 그 구간은 <c>cruiseSpeed</c>로 달려와 회피가 회피로 보이고,
        /// 짧은 구간은 제자리 공방이 된다. <b>음악이 정한 창이 곧 연출을 고른다.</b></para>
        ///
        /// <para><b>적이 공격자인 실패는 언제나 물러난다</b> — 그건 회피가 아니라 벤 뒤의 여파이고,
        /// 플레이어는 맞아서 어차피 피격 클립에 묶인다.</para>
        ///
        /// <para>다음 예약이 없으면(곡 공백) <b>물러나지 않는다.</b> 돌아올 사람이 없어 물러난 채로 남는다.</para>
        /// </summary>
        /// <summary>
        /// 이 길이의 사슬을 처치로 인정할 <b>최소 성공 타수</b>. 최소 1 — 마지막 타는 언제나 성공해야 한다
        /// (빗나간 임팩트에 절단을 붙이면 그 순간 적은 패링·회피 모션 중이라 처치 연출이 걸릴 자리가 없다).
        ///
        /// <para>비사슬 엔트리는 길이가 1이라 어느 비율에서도 1을 돌려준다 — <b>기존 채보에 회귀가 없다.</b></para>
        /// </summary>
        private int RequiredHits(int chainLength) =>
            Mathf.Max(1, Mathf.CeilToInt(chainLength * chainKillRatio));

        private float ResolveRetreatDistance(Reservation r, bool playerSucceeded)
        {
            // 적이 공격자인 패턴의 결말은 어느 쪽이든 물러난다 — 성공은 패링당해 밀려나는 것이고,
            // 실패는 벤 뒤의 여파다. 둘 다 회피가 아니므로 창을 보지 않는다.
            if (AttackerOf(r.template) == Attacker.Enemy) return failRetreatDistance;

            // 사슬 중간 타격에는 넉백이 없다. 물러나면 플레이어가 매 타격마다 다시 붙어야 하는데,
            // 그 재접근은 TakeTargetForWindow를 안 거쳐 거리가 창에 안 맞춰진다(docs/FailConverge/) —
            // 사슬은 그 구간을 매 타격 반복하게 된다. 제자리에 세우면 그 문제 자체가 없다.
            if (playerSucceeded) return 0f;

            // r은 위에서 이미 제거됐으므로 선두가 곧 다음 패턴이다(FIFO).
            if (reservations.Count == 0) return 0f;

            var next = reservations[0];
            float arriveTime = next.attack != null && next.attack.IsUsable
                ? next.attack.ResolveScheduleStart(next.impactTime, Time.time)
                : next.impactTime;

            // 플레이어가 되돌아와야 하는 거리는 후퇴 거리의 playerShare 몫이다(BuildDuelPlan과 같은 비율).
            //
            // ponytail: arriveTime은 낙관적인 데드라인이다. 진짜 마감은 '플레이어 자기 스윙 시작'이라
            // 그보다 이르다(Attacker.Player 패턴은 적 클립이 없어 arriveTime이 임팩트 그 자체가 된다).
            // 스윙 시작은 CharacterActionPlayer만 알고 OnJudgeTargetBegan은 이 시점 뒤에 오므로 여기서는 못 본다.
            // 그 간격을 retreatWindowMargin이 흡수한다 — 회피가 빠듯해 보이면 그 값을 키운다.
            float travel = failRetreatDistance * playerShare / Mathf.Max(cruiseSpeed, 0.01f);
            float needed = retreatDuration + travel + retreatWindowMargin;

            return (arriveTime - Time.time) >= needed ? failRetreatDistance : 0f;
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
        /// <para>다만 <b>연출은 기다린다</b>(<see cref="PendingKill"/>). 상대 전환·링 보충은 여기서 즉시 하고,
        /// 사망 클립을 임팩트에 정렬해 재생한 뒤 <b>임팩트 프레임</b>에 갈라짐을 예약한다 —
        /// 칼이 지나가는 그 순간 갈라진다. 클립 유무와 무관하게 같은 시각이다.</para>
        /// </summary>
        private void KillOpponent(EnemyView opponent, SliceSet set, float impactTime, ClipAlignment death)
        {
            opponent.MarkDying();

            // 절단 시각은 배속을 아는 쪽(뷰)이 계산한다 — 여기서 따로 추정하면 두 값이 갈라진다.
            float burstTime = opponent.AssignDeath(death, impactTime, maxAttackSpeed);

            pendingKills.Add(new PendingKill { opponent = opponent, set = set, burstTime = burstTime });

            OnEnemyKilled?.Invoke(opponent);

            if (currentOpponent == opponent) currentOpponent = null;

            // 사슬이 끝났다. 다음 상대는 새 카운터로 시작한다.
            chainHits = 0;
            chainSuccesses = 0;

            if (clusterEnabled)
            {
                // 하나 죽었으니 하나 태운다 — 사망 1 : 스폰 1. 새 적은 집결지로 걸어가 합류한다.
                // 순서가 중요하다: 걷어내기 → 보충 → 승격. 승격을 먼저 하면 방금 태운 적이 승격에 휩쓸린다.
                ForgetFromClusters(opponent);
                SpawnOneIntoStaged();
                PromoteClusterIfEmpty();
            }
            else if (ring.Count < RingCapacity)
            {
                SpawnIntoStage();
            }

            ReleaseCurrentOpponent();
        }

        /// <summary>
        /// 히트스톱을 <b>아직 안 터진 죽는 적 전부</b>에 전달한다. 보통 한 명이지만, 연속 처치 구간에서
        /// 앞선 적이 아직 쓰러지는 중일 수 있다 — 화면에서 같이 멈춰야 이음매가 안 생긴다.
        ///
        /// <para><b>반환된 절단 시각을 반드시 되받아 쓴다.</b> 절단은 임팩트 그 순간이라 정지 시간만큼
        /// 뒤로 밀리는데, 갱신을 빠뜨리면 <b>멈춘 프레임에 그대로 갈라져 정지가 안 보인다.</b></para>
        /// </summary>
        public void ApplyHitStop(float duration)
        {
            for (int i = 0; i < pendingKills.Count; i++)
            {
                var pending = pendingKills[i];
                if (pending.opponent == null) continue;

                pending.burstTime = pending.opponent.ApplyHitStop(duration, pending.burstTime);
            }

            // 살아남은 상대(사슬 중간 타격)도 같이 언다. 안 그러면 플레이어만 멈추고 적 리액션만 흐른다.
            // 죽는 중이면 위 순회가 이미 얼렸고 뷰의 hitStopped 가드가 두 번째 호출을 막는다.
            currentOpponent?.ApplyHitStop(duration, 0f);
        }

        /// <summary>사망 클립이 끝난 예약을 실행한다. 여기서야 적이 실제로 갈라진다.</summary>
        private void TickPendingKills()
        {
            float now = Time.time;

            for (int i = pendingKills.Count - 1; i >= 0; i--)
            {
                var pending = pendingKills[i];
                if (now < pending.burstTime) continue;

                pendingKills.RemoveAt(i);
                ExecuteKill(pending);
            }
        }

        private void ExecuteKill(PendingKill pending)
        {
            var opponent = pending.opponent;
            if (opponent == null) return;

            OnEnemyBurst?.Invoke(opponent);

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
            RecycleDissolved();
            RecycleDebris();
            RecycleCorpses();
            EnforcePieceBudget();
            TickWander();
        }

        /// <summary>
        /// 절단은 <b>LateUpdate</b>에서 판정한다. 절단 시각과 히트스톱 발사 시각이 <b>같은 임팩트 프레임</b>이라
        /// 둘 다 <c>Update</c>에 있으면 스크립트 실행 순서에 따라 <b>정지가 걸리기 전에 몸이 갈라진다</b>.
        /// <c>HitStopDirector.Fire</c>가 <c>Update</c>에서 <c>burstTime</c>을 밀고 난 뒤 여기서 보게 만든다.
        /// </summary>
        void LateUpdate()
        {
            TickPendingKills();
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
                ForgetFromClusters(view);
                ReleaseEnemy(view);
            }

            if (currentOpponent != null && currentOpponent.DissolveFinished)
            {
                ForgetFromClusters(currentOpponent);
                ReleaseEnemy(currentOpponent);
                currentOpponent = null;
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

    /// <summary>풀 반납 시 어느 프리팹에서 나왔는지 되짚기 위한 표식.</summary>
    public class EnemyPooledInstance : MonoBehaviour
    {
        public GameObject SourcePrefab;
    }
}
