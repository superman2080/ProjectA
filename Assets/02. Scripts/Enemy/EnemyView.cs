using System.Collections.Generic;
using PatternSpace;
using SliceSpace;
using UnityEngine;

namespace EnemySpace
{
    /// <summary>
    /// 적 하나의 뷰. 상태 전이는 <b>전부 시각 기반</b>(디렉터가 준 절대시각)이고 물리를 쓰지 않는다 —
    /// <see cref="SliceTargetView"/>와 같은 규율이다. 임팩트 시각에 <b>정확히</b> 닿아야 하기 때문.
    ///
    /// <para><b>공격 클립은 패턴이 준다</b>(<see cref="Pattern.EnemyAttack"/>). 임팩트 프레임이 <c>impactTime</c>에
    /// 오도록 시작 시각·배속을 역산하며, 이 계산은 <see cref="ClipAlignment"/>에 있어 플레이어와 공유된다 —
    /// 그래서 <b>적 칼이 지나가는 순간 = 플레이어 칼이 지나가는 순간</b>이 구조적으로 성립한다.</para>
    /// </summary>
    public class EnemyView : MonoBehaviour
    {
        public enum Phase
        {
            Enter,      // 화면 밖 → 링
            RingIdle,   // 링에서 대기
            Engage,     // 링 → 결투 앵커
            Windup,     // 공격 모션 재생 중
            Recover,    // 리액션(KnockBack/Evade) 후 링 복귀
            Dying
        }

        [Header("Animator")]
        [SerializeField] private Animator animator;
        [Tooltip("공격 슬롯 스테이트 이름. 클립은 런타임에 주입된다.")]
        [SerializeField] private string attackStateName = "Attack";
        [Tooltip("공격 슬롯에 물려 있는 placeholder 클립.")]
        [SerializeField] private AnimationClip attackPlaceholder;
        [SerializeField] private string attackSpeedParam = "AttackSpeed";
        [Tooltip("사망 슬롯 스테이트 이름. 패턴이 준 사망 클립이 런타임에 주입된다.")]
        [SerializeField] private string deathStateName = "Death";
        [Tooltip("사망 슬롯에 물려 있는 placeholder 클립. 패턴에 사망 클립이 없으면 이게 그대로 재생된다(폴백).")]
        [SerializeField] private AnimationClip deathPlaceholder;
        [SerializeField] private string deathSpeedParam = "DeathSpeed";
        [Tooltip("죽으면서 결투 위치를 비켜 주는 거리(m). 다음 상대가 같은 자리로 들어오기 때문.\n" +
                 "루트 모션이 있는 사망 클립이면 0으로 둬서 끈다.")]
        [SerializeField] private float deathClearOffset = 0.6f;
        [SerializeField] private string idleStateName = "Idle";
        [Tooltip("이동 구간에만 드러낼 로코모션 스테이트. 기본 자세는 Idle이고 움직일 때만 여기로 바뀐다.")]
        [SerializeField] private string moveStateName = "Run";
        [Tooltip("이 거리(m) 이하로 움직이면 로코모션을 켜지 않는다 — 제자리에서 뛰는 것처럼 보이지 않게.")]
        [SerializeField] private float minMoveDistance = 0.3f;
        [Tooltip("결투 위치로 접근할 때의 이동 속도(m/s). 도착 시각까지 시간이 남으면 이 속도로 먼저 가서 선다.")]
        [SerializeField] private float moveSpeed = 3f;

        /// <summary>이 적이 접근에 쓰는 속도(m/s). 디렉터가 "창 안에 닿을 수 있는가"를 판단할 때 같은 값을 봐야 한다.</summary>
        public float MoveSpeed => moveSpeed;
        [Tooltip("리액션(피격·회피)이 로코모션에 덮이지 않게 지키는 시간(초). 리액션 클립 길이에 맞춘다.")]
        [SerializeField] private float reactionHoldDuration = 0.6f;

        [Tooltip("플레이어를 계속 바라보는 회전 속도(도/초). 0이면 시선 고정을 끈다.")]
        [SerializeField] private float gazeTurnSpeed = 360f;
        [Tooltip("패링당해 뒤로 밀려나는 리액션.")]
        [SerializeField] private string knockBackStateName = "KnockBack";
        [Tooltip("플레이어 헛스윙을 피해 물러나는 리액션.")]
        [SerializeField] private string evadeStateName = "Evade";
        // Death 스테이트는 두지 않는다 — 처치 순간 렌더러를 끄거나 시체 프리팹으로 통째로 교체하므로
        // 클립이 재생될 프레임이 존재하지 않는다(예전엔 CrossFade 직후 렌더러를 꺼서 죽은 배선이었다).
        [SerializeField] private float crossFadeDuration = 0.12f;

        [Header("Dissolve")]
        [Tooltip("소멸 셰이더의 노출 프로퍼티 이름. Assets/Shaders/Dissolve/Dissolve.shadergraph 기준.")]
        [SerializeField] private string dissolveProperty = "_Dissolve";

        /// <summary>현재 단계. 디렉터가 배정 가능 여부를 판단하는 데 쓴다.</summary>
        public Phase Current { get; private set; } = Phase.RingIdle;

        /// <summary>링 위 각도(도). 배치·다음 상대 선택의 기준값.</summary>
        public float RingAngle { get; set; }

        /// <summary>링 위 정위치(월드). 교전이 끝나면 여기로 돌아온다.</summary>
        public Vector3 RingPosition { get; set; }

        public EnemyDefinition Definition { get; private set; }

        // 이동 스케줄 — 시작/도착 시각과 양 끝점만 들고 매 프레임 보간한다(적분 누적 없음).
        private Vector3 moveFrom;
        private Vector3 moveTo;
        private float moveStart;
        private float moveEnd;
        private bool moving;
        private bool wantsLocomotion;  // 이번 이동이 걸어갈 만한 거리인가
        private float reactionUntil;   // 이 시각까지는 리액션이 애니메이터를 점유한다

        // 현재 구간 뒤에 이어 붙는 구간(하나). 후퇴가 끝난 뒤 접근을 잇는 데 쓴다.
        private bool hasQueuedMove;
        private Vector3 queuedTo;
        private Vector3 queuedFace;
        private float queuedEnd;

        private float retreatUntil;    // 실패 후퇴가 진행 중인 시각까지
        private bool retreating => Time.time < retreatUntil;

        /// <summary>
        /// 이 적이 <b>결국 서 있게 될 곳</b>. 이동 중이면 최종 목적지, 아니면 현재 위치.
        ///
        /// <para>결투 계획은 이 값으로 세워야 한다 — 배정 순간 적이 이동 중이면(후퇴·대기석 진입·링 등장)
        /// <c>transform.position</c>은 <b>곧 떠날 위치</b>라, 그걸로 중점을 잡으면 플레이어가 엉뚱한 데로 간다.
        /// 특히 후퇴에서는 "둘 다 제자리"로 계산되어 플레이어가 아예 안 붙는다.</para>
        /// </summary>
        public Vector3 Destination
        {
            get
            {
                if (hasQueuedMove) return queuedTo;
                return moving ? moveTo : transform.position;
            }
        }
        private Transform gazeTarget;  // 링에서 대기하는 동안 계속 바라볼 대상(플레이어)

        // 회전 스케줄. 이동과 같은 규율.
        private Quaternion faceFrom;
        private Quaternion faceTo;

        // 공격 예약.
        private ClipAlignment pendingAttack;
        private float pendingImpactTime;
        private float pendingScheduleStart;
        private float pendingMaxSpeed = 2.5f;
        private bool hasPendingAttack;
        private bool attackStarted;

        // 소멸.
        private float dissolveStart;
        private float dissolveDuration;
        private bool dissolving;

        private AnimatorOverrideController overrideController;
        private int attackStateHash;
        private int attackSpeedHash;
        private int deathStateHash;
        private int deathSpeedHash;
        private MaterialPropertyBlock propertyBlock;
        private Renderer[] renderers;
        private int dissolveId;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();

            attackStateHash = Animator.StringToHash(attackStateName);
            attackSpeedHash = Animator.StringToHash(attackSpeedParam);
            deathStateHash = Animator.StringToHash(deathStateName);
            deathSpeedHash = Animator.StringToHash(deathSpeedParam);
            dissolveId = Shader.PropertyToID(dissolveProperty);
            renderers = GetComponentsInChildren<Renderer>(true);
            propertyBlock = new MaterialPropertyBlock();

            // 인스턴스마다 따로 만든다 — 공유하면 동시에 공격하는 두 적이 서로의 클립을 덮어쓴다.
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
                animator.runtimeAnimatorController = overrideController;
            }
        }

        /// <summary>풀에서 꺼내 쓸 때의 초기화. 링 위치·각도는 디렉터가 정한다.</summary>
        public void Setup(EnemyDefinition definition, float ringAngle, Vector3 ringPosition, Vector3 enterPosition, float enterDuration, Vector3 faceTarget)
        {
            Definition = definition;
            RingAngle = ringAngle;
            RingPosition = ringPosition;

            hasPendingAttack = false;
            attackStarted = false;
            dissolving = false;
            SetDissolveAmount(0f);

            transform.position = enterPosition;
            LookAtInstant(faceTarget);

            float jitter = definition != null ? definition.ScaleJitter : 0f;
            transform.localScale = Vector3.one * (1f + Random.Range(-jitter, jitter));

            Current = Phase.Enter;
            reactionUntil = 0f; // 풀에서 갓 꺼낸 상태 — 이전 대여의 리액션 잠금이 남으면 안 된다.

            // ScheduleMove가 거리를 보고 Idle/Run을 정한다. 여기서 Idle을 덮으면 걸어 들어오는 동안 제자리 포즈가 된다.
            ScheduleMove(enterPosition, ringPosition, Time.time, Time.time + Mathf.Max(enterDuration, 0.01f));
        }

        // ── 이동 / 회전 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 구간 이동을 예약한다. <b>도착 시각을 맞추는 게 목적</b>이라 속도가 아니라 시각으로 준다.
        ///
        /// <para>기본 자세는 Idle이고 <b>이동 중에만</b> 로코모션으로 바꾼다 — 안 그러면 제자리 포즈로 미끄러진다.
        /// 거리가 0에 가까우면(대기석 승격 등) 굳이 뛰지 않는다.</para>
        /// </summary>
        public void ScheduleMove(Vector3 from, Vector3 to, float startTime, float endTime)
        {
            moveFrom = from;
            moveTo = to;
            moveStart = startTime;
            moveEnd = Mathf.Max(endTime, startTime + 0.01f);
            moving = true;
            hasQueuedMove = false; // 새 구간은 이어 붙은 구간까지 통째로 대체한다
            wantsLocomotion = (to - from).sqrMagnitude > minMoveDistance * minMoveDistance;

            ApplyLocomotion();
        }

        /// <summary>
        /// 지금 구간이 <b>끝난 뒤</b> 이어질 구간을 예약한다. 진행 중인 구간을 덮지 않는다.
        ///
        /// <para>후퇴가 이것 때문에 존재한다 — 실패 확정과 다음 패턴의 <see cref="AssignAttack"/>이
        /// <b>같은 프레임</b>에 오므로, 덮어쓰기였다면 후퇴가 한 프레임도 렌더되지 않는다.
        /// 구간을 하나만 더 들면 충분하다: 접근 도중에 또 다른 이동이 끼어들 일이 없다
        /// (다음 배정은 이 패턴이 확정된 뒤에야 온다).</para>
        /// </summary>
        public void ScheduleMoveAfter(Vector3 to, Vector3 faceTarget, float endTime)
        {
            if (!moving) { ScheduleFace(faceTarget); ScheduleMove(transform.position, to, Time.time, endTime); return; }

            hasQueuedMove = true;
            queuedTo = to;
            queuedFace = faceTarget;
            queuedEnd = endTime;
        }

        /// <summary>
        /// 이동 상태를 애니메이션에 반영한다. <b>리액션 구간에는 건드리지 않는다.</b>
        ///
        /// <para><see cref="Resolve"/>가 리액션을 CrossFade한 <b>직후</b> 링 복귀 이동을 예약하므로,
        /// 아무 조건 없이 로코모션으로 바꾸면 <b>피격·회피 모션이 한 프레임 만에 씹힌다.</b>
        /// 리액션이 끝나면 <see cref="TickMove"/>가 다시 불러 이어받는다.</para>
        /// </summary>
        private void ApplyLocomotion()
        {
            // 공격/사망 중에는 그 클립이 자리를 잡고 있다.
            if (Current == Phase.Windup || Current == Phase.Dying) return;
            if (Time.time < reactionUntil) return;

            CrossFade(moving && wantsLocomotion ? moveStateName : idleStateName);
        }

        /// <summary>
        /// 리액션(피격·회피·무방비)을 재생하고 <see cref="reactionHoldDuration"/> 동안 로코모션 전환을 막는다.
        /// 이 구간이 없으면 곧바로 이어지는 복귀 이동이 리액션을 덮어쓴다.
        /// </summary>
        private void CrossFadeReaction(string stateName)
        {
            CrossFade(stateName);
            reactionUntil = Time.time + Mathf.Max(reactionHoldDuration, 0f);
        }

        /// <summary>이동과 같은 구간 동안 <paramref name="target"/>을 바라보도록 회전을 건다.</summary>
        public void ScheduleFace(Vector3 target)
        {
            faceFrom = transform.rotation;
            faceTo = FlatLookRotation(target - transform.position);
        }

        /// <summary>
        /// 링에서 대기하는 동안 계속 바라볼 대상. 적 전원이 플레이어를 노려보는 그림을 만든다.
        /// 디렉터가 스폰 시 한 번 꽂는다.
        /// </summary>
        public void SetGazeTarget(Transform target) => gazeTarget = target;

        /// <summary>
        /// 플레이어 쪽으로 서서히 돌아본다.
        ///
        /// <para><b>이동 중과 공격/사망 중에는 건드리지 않는다.</b> 이동은 예약 회전(<see cref="ScheduleFace"/>)이
        /// 소유하고, 스윙 중에는 <b>기하가 얼어 있어야</b> 칼이 겨냥한 지점이 늦게 바뀌지 않는다.</para>
        /// </summary>
        private void TickGaze()
        {
            if (gazeTarget == null || gazeTurnSpeed <= 0f) return;
            if (moving) return;
            if (Current == Phase.Windup || Current == Phase.Dying) return;

            Vector3 direction = gazeTarget.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(direction), gazeTurnSpeed * Time.deltaTime);
        }

        public void LookAtInstant(Vector3 target)
        {
            faceFrom = faceTo = FlatLookRotation(target - transform.position);
            transform.rotation = faceTo;
        }

        private static Quaternion FlatLookRotation(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(direction);
        }

        // ── 공격 배정 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 결투 앵커로 들어가 <paramref name="impactTime"/>에 칼이 닿도록 공격을 예약한다.
        /// <b>이동은 클립 시작 전에 끝난다</b> — 스윙 도중 기하가 바뀌면 칼이 어긋난다.
        /// </summary>
        public void AssignAttack(ClipAlignment attack, float impactTime, Vector3 duelPosition, Vector3 faceTarget, float maxSpeed)
        {
            Current = Phase.Engage;

            if (attack == null || !attack.IsUsable)
            {
                // 무연출 — 자리만 잡는다.
                hasPendingAttack = false;
                ApproachDuel(duelPosition, faceTarget, impactTime);
                return;
            }

            pendingAttack = attack;
            pendingImpactTime = impactTime;
            pendingScheduleStart = attack.ResolveScheduleStart(impactTime, Time.time);
            pendingMaxSpeed = maxSpeed; // 실제 배속은 시작 시점에 남은 시간으로 재계산한다(TryStartAttack)
            hasPendingAttack = true;
            attackStarted = false;

            // 클립이 시작되기 전에 결투 위치에 도착해 있어야 한다.
            ApproachDuel(duelPosition, faceTarget, pendingScheduleStart);
        }

        /// <summary>
        /// 결투 위치로 접근한다. <b>후퇴 중이면 덮지 않고 그 뒤에 잇는다</b> —
        /// 실패 확정과 다음 패턴 배정이 같은 프레임에 오기 때문(<see cref="ScheduleMoveAfter"/>).
        /// </summary>
        private void ApproachDuel(Vector3 duelPosition, Vector3 faceTarget, float arriveTime)
        {
            if (retreating)
            {
                ScheduleMoveAfter(duelPosition, faceTarget, arriveTime);
                return;
            }

            ScheduleFace(faceTarget);
            ScheduleMove(transform.position, duelPosition, Time.time, EarliestArrival(transform.position, duelPosition, arriveTime));
        }

        /// <summary>
        /// <b>도착 시각을 앞당긴다</b> — <paramref name="latest"/>까지 끌지 않고 <see cref="moveSpeed"/>로 갈 수 있는 만큼 빨리 간다.
        ///
        /// <para>이게 없으면 <b>적이 도착하는 순간이 곧 베이는 순간</b>이라 서 있는 구간이 아예 없다.
        /// 거리는 1m 남짓인데 창은 1초가 넘으므로 <c>Lerp</c>가 초속 0.7m로 기어가고,
        /// 화면에는 <b>제자리에 선 것처럼 보이는데 Run이 계속 도는</b> 그림이 된다
        /// (<c>moving</c>이 true인 동안 <see cref="ApplyLocomotion"/>이 로코모션을 유지하므로).</para>
        ///
        /// <para>앞당기는 건 <b>언제나 안전하다</b> — "클립 시작 전에 도착해 있어야 한다"는 제약의 방향과 같다.
        /// 남는 시간에는 서서 <see cref="TickGaze"/>가 상대를 바라본다.</para>
        /// </summary>
        private float EarliestArrival(Vector3 from, Vector3 to, float latest)
        {
            if (moveSpeed <= 0f) return latest;

            float distance = Vector3.ProjectOnPlane(to - from, Vector3.up).magnitude;
            return Mathf.Min(latest, Time.time + distance / moveSpeed);
        }

        /// <summary>
        /// 성패 확정 — 리액션을 재생하고 <b>짧게 물러난다</b>.
        ///
        /// <para><b>제자리로 돌아가지 않는다.</b> 무대에 자기 자리가 따로 있는 게 아니라
        /// <b>물러난 그 자리가 새 자리</b>다 — 플레이어가 다시 찾아온다.</para>
        ///
        /// <para>이 후퇴는 <b>덮이지 않는다</b> — 곧바로 이어지는 <see cref="AssignAttack"/>이
        /// <see cref="ScheduleMoveAfter"/>로 뒤에 붙기 때문. 그래서 "물러났다가 다시 붙는" 그림이 실제로 렌더된다.</para>
        /// </summary>
        public void Resolve(bool playerSucceeded, Attacker attacker, float retreatDistance, float retreatDuration)
        {
            hasPendingAttack = false;

            if (Current == Phase.Dying) return;

            // 실패는 공격자와 무관하게 회피로 통일한다 — 미스가 마지막 노드든 그 전이든
            // 화면에 남는 사실은 "베지 못했다" 하나뿐이라 반응을 나눌 이유가 없다.
            if (!playerSucceeded) CrossFadeReaction(evadeStateName);
            else if (attacker == Attacker.Enemy) CrossFadeReaction(knockBackStateName); // 적 공격을 막아냈다 → 밀려난다
            else CrossFadeReaction(idleStateName);

            Current = Phase.Recover;

            // 바라보던 방향의 반대쪽으로 물러난다. 계속 상대를 보고 있어야 하므로 회전은 그대로 둔다.
            Vector3 back = -transform.forward;
            Vector3 target = transform.position + Vector3.ProjectOnPlane(back, Vector3.up).normalized * Mathf.Max(retreatDistance, 0f);

            float duration = Mathf.Max(retreatDuration, 0.01f);
            retreatUntil = Time.time + duration;
            ScheduleMove(transform.position, target, Time.time, retreatUntil);
        }

        /// <summary>
        /// 포즈 전사의 원본이 되는 스키닝 렌더러. 시체가 이 본 배열을 순서 그대로 물려받는다.
        ///
        /// <para><b>본이 가장 많은 렌더러를 고른다.</b> 캐릭터는 몸통 말고도 칼·검집처럼
        /// 본 한두 개짜리 렌더러를 달고 있고, 계층 순서상 그쪽이 먼저 잡히는 경우가 있다
        /// (예: <c>T-Pose.FBX</c>는 첫 렌더러가 본 1개짜리 <c>BladeL</c>이다).
        /// 그걸 집으면 시체에 본이 1개만 전사되어 <b>T-포즈로 튄다</b>.</para>
        /// </summary>
        public SkinnedMeshRenderer SourceSkinned
        {
            get
            {
                if (sourceSkinned != null) return sourceSkinned;

                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.bones == null) continue;
                    if (sourceSkinned == null || r.bones.Length > sourceSkinned.bones.Length) sourceSkinned = r;
                }

                return sourceSkinned;
            }
        }

        private SkinnedMeshRenderer sourceSkinned;

        /// <summary>처치 확정 표시. 실제 연출(시체 교체)은 디렉터가 임팩트 시각에 실행한다.</summary>
        public void MarkDying()
        {
            Current = Phase.Dying;
            hasPendingAttack = false;
            moving = false;
        }

        /// <summary>
        /// 처치 — <b>정적 프록시 경로 전용</b>이다. 스키닝 본체를 감추고 미리 구운 조각으로 교체해 갈라뜨린다.
        ///
        /// <para>휴머노이드(스킨드) 세트는 이 경로를 타지 않는다 — 시체 프리팹으로 통째로 교체되며
        /// 그 처리는 <see cref="EnemyDirector"/>가 한다(산 적은 풀로 반납되므로 뷰가 자기 소멸을 주관할 수 없다).</para>
        /// </summary>
        /// <param name="set">
        /// <b>조각을 빌려 온 바로 그 세트여야 한다.</b> 여기서 <c>Definition.DeathSliceSet</c>을 다시 읽으면
        /// 디렉터가 패턴 세트에서 조각을 빌렸을 때 <b>조각과 배치 오프셋이 서로 다른 세트에서 나온다</b> —
        /// 조각 수가 다르면 남는 조각이 피벗에 뭉치고, 세트가 null이면 적이 사라지지도 않는다.
        /// </param>
        public void Kill(SliceSet set, IReadOnlyList<SlicePiece> pieces, float scatterSpeed, float scatterSpin, int pieceLayer)
        {
            MarkDying();

            if (set == null || pieces == null || pieces.Count == 0) return;

            SetRenderersEnabled(false);

            var offsets = set.PieceLocalOffsets;
            var dirs = set.PieceScatterDirs;
            int n = pieces.Count;

            for (int i = 0; i < n; i++)
            {
                if (pieces[i] == null) continue;

                Vector3 local = i < offsets.Length ? offsets[i] : Vector3.zero;
                Vector3 dir = i < dirs.Length ? dirs[i] : Vector3.zero;

                if (dir.sqrMagnitude < 1e-6f)
                {
                    float angle = 360f * i / Mathf.Max(n, 1) * Mathf.Deg2Rad;
                    dir = new Vector3(Mathf.Cos(angle), 0.3f, Mathf.Sin(angle));
                }

                pieces[i].Place(transform, local);

                // 서 있는 적이므로 승계할 이동 속도가 없다 — 그 자리에서 무너진다.
                Vector3 worldDir = transform.TransformDirection(dir);
                Vector3 axis = Random.onUnitSphere;
                pieces[i].Launch(Vector3.zero, worldDir, scatterSpeed, axis, Random.Range(-scatterSpin, scatterSpin), pieceLayer);
            }
        }

        /// <summary>곡이 끝날 때 남은 적을 없애는 연출. <b>절단이 아니다</b> — 베지 않았으니 갈라지면 안 된다.</summary>
        public void Dissolve(float duration)
        {
            if (dissolving) return;

            dissolving = true;
            dissolveStart = Time.time;
            dissolveDuration = Mathf.Max(duration, 0.01f);
            Current = Phase.Dying;
            hasPendingAttack = false;
            moving = false;
        }

        /// <summary>소멸이 끝났는지. 디렉터가 회수 시점을 잡는 데 쓴다.</summary>
        public bool DissolveFinished => dissolving && Time.time >= dissolveStart + dissolveDuration;

        // ── 루프 ────────────────────────────────────────────────────────────────

        void Update()
        {
            if (dissolving)
            {
                float p = Mathf.Clamp01((Time.time - dissolveStart) / dissolveDuration);
                SetDissolveAmount(p);
                return;
            }

            TickMove();
            TickGaze();      // 이동이 없을 때만 실제로 돈다(TickMove가 회전을 소유한다)
            TryStartAttack();
        }

        private void TickMove()
        {
            // 리액션이 끝나는 순간 로코모션을 이어받는다(리액션 중에는 ApplyLocomotion이 스스로 물러난다).
            if (reactionUntil > 0f && Time.time >= reactionUntil)
            {
                reactionUntil = 0f;
                ApplyLocomotion();
            }

            if (!moving) return;

            float t = Mathf.Clamp01((Time.time - moveStart) / (moveEnd - moveStart));
            transform.position = Vector3.Lerp(moveFrom, moveTo, t);
            transform.rotation = Quaternion.Slerp(faceFrom, faceTo, t);

            if (t < 1f) return;

            // 이어 붙은 구간이 있으면 여기서 인수인계한다(후퇴 → 접근).
            if (hasQueuedMove)
            {
                hasQueuedMove = false;
                // 이어받는 구간도 끝까지 끌지 않는다 — 후퇴 뒤 접근이 기어가면 같은 증상이 난다.
                ScheduleMove(moveTo, queuedTo, Time.time, EarliestArrival(moveTo, queuedTo, queuedEnd));
                ScheduleFace(queuedFace);
                return;
            }

            moving = false;
            wantsLocomotion = false;
            if (Current == Phase.Enter || Current == Phase.Recover) Current = Phase.RingIdle;

            ApplyLocomotion(); // 도착했으니 다시 선다
        }

        /// <summary>예약된 공격의 시작 시각에 도달하면 재생한다. 남은 시간 기준으로 배속을 재계산해 임팩트를 정렬한다.</summary>
        private void TryStartAttack()
        {
            if (!hasPendingAttack || attackStarted || Time.time < pendingScheduleStart) return;

            float speed = pendingAttack.ResolvePlaySpeed(Time.time, pendingImpactTime, pendingMaxSpeed, out bool clamped);
            if (clamped)
            {
                Debug.LogWarning(
                    $"[EnemyView] '{name}' 공격 클립이 배속 상한({pendingMaxSpeed:0.0})에 걸려 임팩트 정렬이 어긋납니다. " +
                    $"클립의 ImpactTime을 앞으로 당기세요.", this);
            }

            PlayAttack(pendingAttack, speed);
            attackStarted = true;
            Current = Phase.Windup;
        }

        /// <summary>
        /// 사망 클립을 재생하고 <b>절단이 일어날 시각</b>(트림 끝)을 돌려준다.
        ///
        /// <para><b>공유하는 것은 임팩트 순간 하나뿐이다.</b> 플레이어 공격과 이 클립은 길이도 저작 배속도
        /// 압축을 유발하는 제약도 달라 배속이 같아질 이유가 없다 — 시작이나 끝을 맞추는 정렬은 성립하지 않는다.
        /// 각자 <paramref name="impactTime"/>이라는 같은 절대 시각에 <b>자기 임팩트 프레임</b>이 오도록
        /// 자기 시작 시점과 자기 배속을 역산한다.</para>
        ///
        /// <para><b>지금 즉시 시작한다.</b> 처치 확정보다 이른 시각은 알 수가 없기 때문 —
        /// 성패가 마지막 노드 입력에서야 정해진다. 그래서 임팩트까지 남은 실시간은
        /// <c>goodWindow</c>(0.1초) + <c>impactOffset</c>뿐이고, 정렬은 배속이 담당한다.</para>
        ///
        /// <para>⚠ <b>배속은 클립 전체에 걸린다.</b> <c>ResolvePlaySpeed</c>는 임팩트 <i>이전</i> 구간을
        /// 남은 시간에 맞추려 배속을 올리는데, 그 값이 Speed Multiplier라 <b>쓰러지는 구간까지 같이 빨라지고
        /// 반환하는 절단 시각도 그만큼 당겨진다.</b> 즉 사망 클립의 ImpactTime 위치가 쓰러지는 속도를 정한다 —
        /// 트림 시작 근처에 찍어야 한다(죽는 모션은 원래 '맞는 순간'이 시작점이라 자연스럽게 맞는다).</para>
        /// </summary>
        public float AssignDeath(ClipAlignment death, float impactTime, float maxSpeed)
        {
            hasPendingAttack = false;
            Current = Phase.Dying;
            reactionUntil = 0f;
            retreatUntil = 0f;

            // 클립이 없으면 예전 그대로 임팩트에 터진다. placeholder가 물려 있으면 그게 재생된다.
            if (death == null || !death.IsUsable)
            {
                ClearDuelSpot(impactTime);
                return impactTime;
            }

            float speed = death.ResolvePlaySpeed(Time.time, impactTime, maxSpeed, out bool clamped);
            if (clamped)
            {
                Debug.LogWarning(
                    $"[EnemyView] '{name}' 사망 클립이 배속 상한({maxSpeed:0.0})에 걸려 임팩트 정렬이 어긋납니다. " +
                    $"클립의 ImpactTime을 트림 시작 쪽으로 당기세요.", this);
            }

            PlayDeath(death, speed);

            float burstTime = Time.time + death.ResolvedDuration / Mathf.Max(speed, 0.01f);
            ClearDuelSpot(burstTime);
            return burstTime;
        }

        /// <summary>
        /// 죽으면서 결투 위치를 비켜 준다. 승격은 처치 확정 즉시 일어나므로
        /// <b>다음 상대가 시체가 선 자리로 걸어 들어온다</b> — 예전에는 임팩트에 사라져 문제가 없었다.
        /// </summary>
        private void ClearDuelSpot(float until)
        {
            if (deathClearOffset <= 0f) return;

            Vector3 back = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
            if (back.sqrMagnitude < 1e-6f) return;

            // ApplyLocomotion은 Phase.Dying에서 스스로 물러나므로 사망 클립을 덮지 않는다.
            ScheduleMove(transform.position, transform.position + back.normalized * deathClearOffset, Time.time, until);
        }

        private void PlayDeath(ClipAlignment alignment, float speed)
        {
            if (animator == null) return;

            // 슬롯을 Attack과 나눈다 — 적이 공격 도중에 죽으면 같은 슬롯을 덮어 진행 중인 클립이 튄다.
            if (overrideController != null && deathPlaceholder != null)
                overrideController[deathPlaceholder] = alignment.Clip;

            animator.SetFloat(deathSpeedHash, speed);

            // fixedTimeOffset은 '스테이트 재생 초'라 speed가 곱해진다 — 클립 초를 speed로 나눠 넘겨야 맞다.
            animator.CrossFadeInFixedTime(deathStateHash, crossFadeDuration, 0, alignment.StartOffset / Mathf.Max(speed, 0.01f));
        }

        private void PlayAttack(ClipAlignment alignment, float speed)
        {
            if (animator == null || overrideController == null || attackPlaceholder == null) return;

            overrideController[attackPlaceholder] = alignment.Clip;
            animator.SetFloat(attackSpeedHash, speed);

            // fixedTimeOffset은 '스테이트 재생 초'라 speed가 곱해진다 — 클립 초를 speed로 나눠 넘겨야 맞다.
            animator.CrossFadeInFixedTime(attackStateHash, crossFadeDuration, 0, alignment.StartOffset / Mathf.Max(speed, 0.01f));
        }

        private void CrossFade(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;

            int hash = Animator.StringToHash(stateName);
            if (!animator.HasState(0, hash)) return; // 미배선 스테이트는 조용히 건너뛴다

            animator.CrossFadeInFixedTime(hash, crossFadeDuration, 0);
        }

        // ── 렌더링 ──────────────────────────────────────────────────────────────

        /// <summary><b>MaterialPropertyBlock으로 인스턴스별 진행.</b> 머티리얼을 공유하면 적 전원이 같이 사라진다.</summary>
        private void SetDissolveAmount(float amount)
        {
            if (renderers == null) return;

            foreach (var r in renderers)
            {
                if (r == null) continue;

                r.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(dissolveId, amount);
                r.SetPropertyBlock(propertyBlock);
            }
        }

        private void SetRenderersEnabled(bool value)
        {
            if (renderers == null) return;

            foreach (var r in renderers)
            {
                if (r != null) r.enabled = value;
            }
        }

        /// <summary>풀 반납 직전 복구. 다음 대여에서 이전 상태가 새지 않도록.</summary>
        public void ResetState()
        {
            moving = false;
            wantsLocomotion = false;
            reactionUntil = 0f;   // 안 지우면 다음 대여에서 로코모션이 잠긴 채로 나온다
            dissolving = false;
            hasPendingAttack = false;
            attackStarted = false;
            Current = Phase.RingIdle;
            transform.localScale = Vector3.one;
            SetDissolveAmount(0f);
            SetRenderersEnabled(true);
        }

        /// <summary>링 대기 중인 적은 애니메이션 계산과 그림자를 끈다 — 무대장치라 풀스펙이 필요 없다.</summary>
        public void ApplyBackgroundBudget(bool isBackground)
        {
            if (animator != null)
                animator.cullingMode = isBackground ? AnimatorCullingMode.CullCompletely : AnimatorCullingMode.AlwaysAnimate;

            if (renderers == null) return;

            var mode = isBackground
                ? UnityEngine.Rendering.ShadowCastingMode.Off
                : UnityEngine.Rendering.ShadowCastingMode.On;

            foreach (var r in renderers)
            {
                if (r != null) r.shadowCastingMode = mode;
            }
        }
    }
}
