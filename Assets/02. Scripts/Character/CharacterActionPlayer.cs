using System;
using PatternSpace;
using UnityEngine;

/// <summary>
/// 캐릭터 액션을 <b>패턴 입력 도중</b> 재생한다.
/// - 성공(베기): 판정 대상이 시작되면, <b>클립의 임팩트 프레임(칼날이 표적을 지나가는 프레임)이 표적 절단 시각에 오도록</b>
///   `시작 = max(임팩트정렬시각 − 임팩트까지의 재생시간, 첫노드)` 지점에 예약해 조기 재생한다.
///   임팩트 프레임 이후의 잔여 구간은 <b>같은 배속으로 그대로 이어 재생</b>되어 마무리 동작이 뒤에 남는다.
///   재생시간은 트림(AnimationStartOffset/Duration/ImpactTime)과 패턴 배속(AnimationSpeed, 배속 하한)을 반영하고, 입력 구간이 짧으면 자동으로 더 배속한다(상한 maxAttackSpeed).
/// - 미스(첫 미스 1회): 그 순간 힛(Hit) 클립을 재생하고 예약/진행 중이던 성공 애니를 취소한다 — 재생 중이던 베기는 힛 크로스페이드로 즉시 끊긴다.
///
/// <b>정렬 앵커는 표적 절단 시각이다.</b> `임팩트정렬시각 = Deadline + Pattern.ImpactOffset`으로,
/// <see cref="SliceSpace.SliceTargetDirector"/>가 표적 도착에 쓰는 식과 <b>동일하다</b>. 그래서 칼날이 지나가는 순간과
/// 표적이 갈라지는 순간이 구조적으로 일치한다. LastNodeTime이 아니라 Deadline인 이유는 표적 쪽과 같다 —
/// 마지막 노드를 goodWindow 안에 늦게 눌러도 Good 성공이므로, 성패는 Deadline에서야 확정된다.
/// 임팩트 프레임이 오서링되지 않은(0 이하이거나 트림 범위 밖) 패턴은 <b>트림 끝</b>을 임팩트로 간주한다.
///
/// <b>트림 끝(actionEndTime)은 '재생이 끝나는 시각'이 아니다.</b> 클립은 스테이트에 통째로 물려 있어 그 뒤로도 마무리 동작(follow-through)이 계속 재생된다.
/// actionEndTime은 '임팩트 정렬이 끝나 복귀를 시작해도 되는 시각'일 뿐이다. 이 시점에 recoveryHoldDuration 동안 마무리 동작을 웨이트 1로 노출한 뒤(세 경로 공통), 아래 세 경로로 갈린다:
/// - <b>연계 O · 간격 부족</b> — 다음 공격이 comboLinkWindow 안이면서 minRunExposure보다 촘촘히 붙는다. 웨이트를 <b>1로 유지</b>한 채 다음 클립으로 크로스페이드한다(Sprint·Release 모두 생략). 짧은 연계의 웨이트 깜빡임을 막는다.
/// - <b>연계 O · 간격 여유</b> — comboLinkWindow 안이되 다음 공격까지 minRunExposure 이상 남는다. blendOutDuration 동안 웨이트를 0으로 내려 <b>그 사이 base Sprint(Sprint_HS)를 노출</b>하고, 다음 공격에서 다시 올린다(Release 생략). 실측상 이쪽이 주 경로다(채보 연결의 약 95%).
/// - <b>연계 X</b> — comboLinkWindow 밖(곡 공백). <b>트림 끝에서 곧바로 Release 스테이트로 CrossFade</b>하고(애니메이터에는 Attack→Release 전이가 없다 — 진입은 코드가 유일하게 통제한다),
///   releaseDuration에 맞춰 압축해 <b>끝까지 재생한 뒤</b> blendOutDuration 동안 base Idle로 페이드한다.
///
/// <b>base 로코모션은 경로에 따라 클립이 갈린다.</b> base Running Layer는 평소 Idle이 기본이고, 연계 O·간격 여유(콤보 사이)를 탈 때 Sprint로,
/// 연계 X(곡 공백 복귀)를 탈 때 다시 Idle로 CrossFade한다(SwitchBaseState). 전환은 Attack 웨이트에 가려진 동안 일어나 눈에 띄지 않는다.
/// 콤보 gap 대부분은 시작 순간 Release가 트리거되므로(hasPending이 뒤늦게 섬), "Release를 냈는가"가 아니라 <b>"Release가 releaseEndTime까지 완주했는가"(releaseCompleted)</b>로
/// 진짜 곡 공백과 콤보 gap을 가른다. 완주했으면 Idle, 인터럽트됐으면 Sprint. releaseCompleted는 다음 PlaySlot에서 리셋된다.
/// 세 경로 모두 actionEndTime에 AttackSpeed를 1로 되돌려 마무리 동작이 배속으로 지나가지 않게 한다.
///
/// AnimatorOverrideController로 듀얼 슬롯(Attack_A, Attack_B)을 교대로 교체하며 재생해 모션 끊김(Popping)을 방지한다. 재생할 클립이 없으면 무연출로 넘어간다.
///
/// <b>확장 포인트</b>: <see cref="OnSwingBegan"/> / <see cref="OnSwingEnded"/>가 스윙(베기) 트림 구간의 시작·끝을 알린다.
/// '칼을 휘두르는 동안'에만 붙는 연출(무기 트레일 등)은 이 이벤트만 구독해 붙인다 — 본체를 고치지 않는다.
/// </summary>
public class CharacterActionPlayer : MonoBehaviour
{
    [SerializeField] private PatternHandler handler;
    [SerializeField] private Animator animator;

    [Tooltip("누가 휘두르는지(Attacker)를 물어볼 대상. 비우면 언제나 Attacker.Player로 본다(디버그 경로).")]
    [SerializeField] private EnemySpace.EnemyDirector enemyDirector;

    [Header("Animator Slot")]
    [Tooltip("첫 번째 슬롯 스테이트 이름.")]
    [SerializeField] private string attackStateAName = "Attack_A";
    [Tooltip("두 번째 슬롯 스테이트 이름.")]
    [SerializeField] private string attackStateBName = "Attack_B";
    [Tooltip("액션 모션이 재생되는 레이어 이름.")]
    [SerializeField] private string attackLayerName = "Attack Layer";
    [Tooltip("Attack_A 스테이트에 물려 있는 첫 번째 더미 placeholder 클립.")]
    [SerializeField] private AnimationClip placeholderA;
    [Tooltip("Attack_B 스테이트에 물려 있는 두 번째 더미 placeholder 클립.")]
    [SerializeField] private AnimationClip placeholderB;
    [Tooltip("Attack 스테이트의 Speed Multiplier 파라미터 이름.")]
    [SerializeField] private string attackSpeedParam = "AttackSpeed";
    [Tooltip("공격 클립이 끝나면 흘러가는 마무리 스테이트 이름.")]
    [SerializeField] private string releaseStateName = "Release";
    [Tooltip("Release 스테이트의 Speed Multiplier 파라미터 이름.")]
    [SerializeField] private string releaseSpeedParam = "ReleaseSpeed";
    [Tooltip("Release 스테이트에 물려 있는 클립. 길이를 읽어 배속을 역산하는 데만 쓴다.")]
    [SerializeField] private AnimationClip releaseClip;

    [Header("Base Locomotion")]
    [Tooltip("base 로코모션 레이어 이름. Attack Layer 웨이트가 0일 때 이 레이어가 드러난다.")]
    [SerializeField] private string runningLayerName = "Running Layer";
    // FormerlySerializedAs를 쓰지 않는다 — 이 필드는 값이 아니라 '의미'가 바뀌었다(달리는 스테이트 → 서는 스테이트).
    // 옛 값("Run")을 끌고 오면 삭제된 스테이트를 가리켜 base 전환이 조용히 실패한다.
    [Tooltip("연계 X(공백/Release 복귀) 시 드러낼 base 스테이트 이름. 곡이 비었으면 달릴 이유가 없으므로 Idle이 기본이다.")]
    [SerializeField] private string idleStateName = "Katana_Idle";
    [Tooltip("연계 O·간격 여유(콤보 사이 노출) 시 드러낼 base 스테이트 이름. 다음 적으로 이동하는 구간이다.")]
    [SerializeField] private string sprintStateName = "Sprint";
    [Tooltip("base 레이어 Idle↔Sprint 포즈 블렌딩 시간(초). base가 Attack 웨이트에 가려진 동안 진행된다.")]
    [SerializeField] private float baseCrossFadeDuration = 0.2f;

    [Header("Converge Locomotion")]
    [Tooltip("결투 수렴 구간에서 짧은 이동에 쓸 스테이트 이름.")]
    [SerializeField] private string quickshiftStateName = "Quickshift";
    [Tooltip("Quickshift 스테이트의 Speed Multiplier 파라미터 이름.")]
    [SerializeField] private string quickshiftSpeedParam = "QuickshiftSpeed";
    [Tooltip("Quickshift 스테이트에 물려 있는 클립. 길이를 읽어 배속을 역산하는 데만 쓴다.")]
    [SerializeField] private AnimationClip quickshiftClip;
    [Tooltip("Sprint 스테이트의 Speed Multiplier 파라미터 이름.")]
    [SerializeField] private string sprintSpeedParam = "SprintSpeed";
    [Tooltip("Sprint 스테이트에 물려 있는 클립. 길이를 읽어 배속을 역산하는 데만 쓴다.")]
    [SerializeField] private AnimationClip sprintClip;
    [Tooltip("Sprint 배속의 기준 이동 속도(m/s). 이 속도로 갈 때 클립이 1배속이 된다.")]
    [SerializeField] private float sprintReferenceSpeed = 4.5f;
    [Tooltip("이 거리(m) 미만이면 로코모션을 켜지 않는다 — 제자리에서 발을 구르지 않게.")]
    [SerializeField] private float convergeMinDistance = 0.15f;
    [Tooltip("수렴 로코모션의 판단 근거를 콘솔에 찍는다(에디터 전용). 모션이 안 나올 때 원인을 가른다.")]
    [SerializeField] private bool logConvergeDecision = true;

    [Header("Clips")]
    [Tooltip("패턴 실패 시 재생할 피격 리액션 클립들. 번갈아 재생된다.")]
    [SerializeField] private AnimationClip[] hitClips; // Hit1, Hit2

    [Header("Tuning")]
    [Tooltip("공격 간 크로스페이드 시간(초). 연계(콤보)가 주 경로이므로 이 값이 체감 품질을 좌우한다.")]
    [SerializeField] private float attackCrossFadeDuration = 0.15f;
    [Tooltip("이 시간(초) 안에 다음 공격이 시작되면 연계로 보고 Release(마무리)를 생략한다.")]
    [SerializeField] private float comboLinkWindow = 1.0f;
    [Tooltip("연계 중, 다음 공격까지 이 시간(초) 이상 남아 있으면 웨이트를 내려 그 사이 Sprint를 노출한다. 미만이면 웨이트 1을 유지해 바로 다음 공격으로 잇는다.")]
    [SerializeField] private float minRunExposure = 0.35f;
    [Tooltip("트림 끝 이후 마무리 동작을 웨이트 1로 노출하는 시간(초). 세 경로 공통.")]
    [SerializeField] private float recoveryHoldDuration = 0.25f;
    [Tooltip("base 로코모션으로 녹아드는 시간(초). 레이어 웨이트를 0으로 내리는 구간.")]
    [SerializeField] private float blendOutDuration = 0.15f;
    [Tooltip("Release(마무리) 스테이트를 이 시간(초) 안에 완주시킨다. 배속은 클립 길이에서 역산된다(길이 2.33초 / 0.9초 ≈ 2.6배).")]
    [SerializeField] private float releaseDuration = 0.9f;
    [Tooltip("공격 → Release 크로스페이드 시간(초).")]
    [SerializeField] private float releaseCrossFadeDuration = 0.10f;
    [Tooltip("액션 시작 시 레이어 웨이트를 현재값에서 1까지 올리는 시간(초). 복귀 도중 인터럽트될 때의 스냅을 막는다.")]
    [SerializeField] private float layerBlendInDuration = 0.12f;
    [Tooltip("자동 배속(입력 구간이 짧을 때)의 상한.")]
    [SerializeField] private float maxAttackSpeed = 2.5f;

    /// <summary>
    /// 확장 포인트: <b>스윙(베기) 트림 구간의 시작</b>. 무기 트레일처럼 '칼을 휘두르는 동안'에만 붙는 연출이 구독한다.
    /// 피격(Hit) 클립에서는 발행되지 않는다 — 휘두르는 동작이 아니기 때문이다.
    /// </summary>
    public event Action OnSwingBegan;

    /// <summary>
    /// 확장 포인트: <b>스윙 트림 구간의 끝</b>. 트림 끝에 정상 종료될 때뿐 아니라
    /// 다음 액션/피격에 인터럽트될 때도 발행되므로, 구독자는 켜진 채 남지 않는다.
    /// </summary>
    public event Action OnSwingEnded;

    /// <summary>
    /// 확장 포인트: <b>적 칼이 실제로 플레이어에게 닿는 순간</b>(= <c>impactTime</c>). 첫 미스 순간이 아니다.
    /// 체력 감소·카메라 피격 큐가 이 이벤트만 구독한다.
    /// </summary>
    public event Action OnPlayerHit;

    /// <summary>스윙 구간 안인지. 종료 신호가 두 경로(트림 끝 / 인터럽트)로 들어와 중복 발행되지 않게 한다.</summary>
    private bool swingActive;

    private AnimatorOverrideController overrideController;
    private int attackLayerIndex = -1;
    private int attackStateAHash;
    private int attackStateBHash;
    private int attackSpeedHash;
    private int releaseStateHash;
    private int releaseSpeedHash;
    private int runningLayerIndex = -1;
    private int idleStateHash;
    private int sprintStateHash;
    private int quickshiftStateHash;
    private int quickshiftSpeedHash;
    private int sprintSpeedHash;

    /// <summary>
    /// 이 시각까지는 <b>수렴 로코모션이 base 레이어의 주인</b>이다.
    /// <see cref="Update"/>의 복귀 로직은 "액션이 없으면 base는 내 것"이라고 가정하고 매 프레임 되돌리므로,
    /// 이 창이 없으면 수렴 모션이 다음 프레임에 지워진다.
    /// </summary>
    private float convergeUntil;
    private int currentBaseStateHash; // 현재 base 레이어가 향하는 스테이트(중복 CrossFade 방지)
    private bool releaseTriggered; // 이번 액션에서 Release로 넘어갔는지
    private bool releaseCompleted; // 이번 사이클에 Release가 releaseEndTime까지 완주했는지(= 진짜 곡 공백). base=Idle 유지 판별용.
    private float releaseEndTime;  // Release 재생이 끝나는 시각(= 트리거 시각 + releaseDuration)
    private int hitIndex;         // Hit 클립 번갈아 재생용 커서
    private bool useSlotA = true; // 듀얼 슬롯 전환 플래그

    // 현재 액션의 복귀 스케줄.
    private float actionEndTime;   // 트림 구간이 끝나는 시각(= 복귀 판단 시작점)
    private float recoveryEndTime; // 연계가 없을 때 마무리 동작 노출이 끝나는 시각
    private bool speedRestored;    // actionEndTime에서 AttackSpeed를 1로 되돌렸는지

    // 현재 재생의 스냅샷. actionEndTime만으로는 "지금까지 소비한 클립 초"를 역산할 수 없어
    // 히트스톱 캐치업(ApplyHitStop)이 남은 클립 길이를 구하지 못한다.
    private float playStartTime;
    private float playSpeed = 1f;
    private float playDur;

    // 히트스톱. 정지 중에는 Update의 복귀 로직을 통째로 막고, 해제 시각에 원래 배속으로 이어 붙인다.
    private bool hitStopped;
    private float hitStopReleaseTime;

    // 레이어 웨이트 블렌드 상태. blend-out은 '결정 시점'에 래치한다(절대 시각 계산은 취소 케이스에서 웨이트가 튄다).
    private float blendInStartTime;
    private float blendInFromWeight;
    private bool blendOutLatched;
    private float blendOutStartTime;
    private float blendOutFromWeight;

    // 성공 애니 예약(판정 대상별). scheduleStart에 도달하면 재생한다.
    private bool hasPending;
    private AnimationClip pendingClip;
    private float pendingStartOffset;
    private float pendingDur;        // 트림 전체 길이(클립 초) — 임팩트 이후 잔여 구간까지 포함한다.
    private float pendingImpactSpan; // 트림 시작 → 임팩트 프레임까지의 길이(클립 초). 배속 역산의 기준.
    private float pendingBaseSpeed;
    private float pendingScheduleStart;
    private float pendingImpactAlignTime; // 임팩트 프레임이 도달해야 할 절대시각(= 표적 절단 시각).
    private bool missedThisTarget; // 이번 판정 대상에서 이미 첫 미스 처리를 했는지

    // 이번 판정 대상에서 누가 휘두르는가. 클립 선택과 "맞는지 여부"를 동시에 가른다.
    private EnemySpace.Attacker currentAttacker = EnemySpace.Attacker.Player;

    // 피격 예약 — 적 칼이 도착하는 시각(impactTime)에 재생한다.
    private bool hasPendingHit;
    private float pendingHitTime;

    void Awake()
    {
        if (animator == null)
        {
            Debug.LogError("[CharacterActionPlayer] Animator가 배선되지 않았습니다.", this);
            return;
        }

        attackLayerIndex = animator.GetLayerIndex(attackLayerName);
        if (attackLayerIndex < 0)
            Debug.LogError($"[CharacterActionPlayer] 레이어 '{attackLayerName}'를 찾을 수 없습니다.", this);

        attackStateAHash = Animator.StringToHash(attackStateAName);
        attackStateBHash = Animator.StringToHash(attackStateBName);
        attackSpeedHash = Animator.StringToHash(attackSpeedParam);
        releaseStateHash = Animator.StringToHash(releaseStateName);
        releaseSpeedHash = Animator.StringToHash(releaseSpeedParam);

        runningLayerIndex = animator.GetLayerIndex(runningLayerName);
        if (runningLayerIndex < 0)
            Debug.LogError($"[CharacterActionPlayer] 레이어 '{runningLayerName}'를 찾을 수 없습니다 — Sprint/Idle 전환이 동작하지 않습니다.", this);
        idleStateHash = Animator.StringToHash(idleStateName);
        sprintStateHash = Animator.StringToHash(sprintStateName);
        quickshiftStateHash = Animator.StringToHash(quickshiftStateName);
        quickshiftSpeedHash = Animator.StringToHash(quickshiftSpeedParam);
        sprintSpeedHash = Animator.StringToHash(sprintSpeedParam);
        currentBaseStateHash = idleStateHash; // base 레이어의 default 스테이트는 Idle이다.

        // 없는 스테이트로 CrossFade하면 Unity가 조용히 무시한다 — base가 이전 포즈에 굳어 버린다.
        // 스테이트 이름을 고치거나 애니메이터에서 지웠을 때 즉시 알아채야 한다.
        WarnIfMissingBaseState(idleStateHash, idleStateName);
        WarnIfMissingBaseState(sprintStateHash, sprintStateName);
        WarnIfMissingBaseState(quickshiftStateHash, quickshiftStateName);

        // 원본 컨트롤러를 감싼 오버라이드 인스턴스를 씌운다. 이후 이 인스턴스의 클립만 런타임에 교체한다.
        overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
        animator.runtimeAnimatorController = overrideController;

        if (placeholderA == null || placeholderB == null)
            Debug.LogError("[CharacterActionPlayer] placeholderA 또는 placeholderB가 배선되지 않았습니다 — 오버라이드 키가 없어 클립 주입이 동작하지 않습니다.", this);

        // 휴지 상태 웨이트 0 보장.
        if (attackLayerIndex >= 0)
            animator.SetLayerWeight(attackLayerIndex, 0f);

        speedRestored = true; // 재생 전에는 되돌릴 배속이 없다.
    }

    void OnEnable()
    {
        if (handler != null)
        {
            handler.OnJudgeTargetBegan += HandleJudgeTargetBegan;
            handler.OnJudgeTargetFirstMiss += HandleJudgeTargetFirstMiss;
        }

        // base 레이어는 이 클래스가 유일하게 소유한다 — 수렴 로코모션도 여기서 정한다.
        // PlayerCombatMover가 직접 애니메이터를 건드리면 두 주인이 매 프레임 싸운다.
        if (enemyDirector != null) enemyDirector.OnDuelScheduled += HandleDuelScheduled;
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnJudgeTargetBegan -= HandleJudgeTargetBegan;
            handler.OnJudgeTargetFirstMiss -= HandleJudgeTargetFirstMiss;
        }

        if (enemyDirector != null) enemyDirector.OnDuelScheduled -= HandleDuelScheduled;

        RaiseSwingEnded(); // 재생기가 꺼지는데 트레일만 켜진 채 남지 않도록.
    }

    /// <summary>스윙 시작을 1회만 발행한다. 이미 진행 중이면 먼저 끝낸 뒤 새로 시작한다.</summary>
    private void RaiseSwingBegan()
    {
        RaiseSwingEnded();
        swingActive = true;
        OnSwingBegan?.Invoke();
    }

    /// <summary>스윙 종료를 1회만 발행한다. 트림 끝과 인터럽트 양쪽에서 호출되므로 멱등이어야 한다.</summary>
    private void RaiseSwingEnded()
    {
        if (!swingActive) return;
        swingActive = false;
        OnSwingEnded?.Invoke();
    }

    void Update()
    {
        if (attackLayerIndex < 0) return;

        // ⚠ 정지 중에는 아래를 하나도 통과시키지 않는다. actionEndTime 분기가 열리면
        // AttackSpeed가 1로 복원되어 캐치업이 통째로 무산된다.
        if (hitStopped)
        {
            if (Time.time < hitStopReleaseTime) return;

            hitStopped = false;
            animator.SetFloat(attackSpeedHash, playSpeed); // 멈춘 자리에서 원래 속도로 이어진다.
        }

        TryStartPendingSuccess();
        TryStartPendingHit();

        // 트림 구간이 끝나면 배속을 해제해 마무리 동작이 정상 속도로 재생되게 한다.
        // 이 래치는 트림 끝을 정확히 1회만 통과하므로 스윙 종료 발행 지점으로 그대로 재사용한다.
        if (!speedRestored && Time.time >= actionEndTime)
        {
            animator.SetFloat(attackSpeedHash, 1f);
            speedRestored = true;
            RaiseSwingEnded();
        }

        // 공격(트림) 구간 재생 중.
        if (Time.time < actionEndTime)
        {
            ApplyBlendIn();
            return;
        }

        // 클립 자체의 마무리 동작을 recoveryHoldDuration만큼 노출한다(세 경로 공통, 0이면 즉시).
        if (Time.time < recoveryEndTime)
        {
            ApplyBlendIn();
            return;
        }

        // 연계 O — Release는 생략한다. 다만 다음 공격까지의 간격이 충분할 때만 웨이트를 내려 Sprint를 노출하고,
        // 간격이 짧으면(minRunExposure 미만) 웨이트 1을 유지해 곧바로 다음 공격으로 잇는다(깜빡임 방지).
        if (IsLinkedToNextAction())
        {
            if (HasRoomForRunExposure())
            {
                // 콤보 사이 잠깐 달리는 구간 — Sprint를 드러낸다.
                // 단, 이번 사이클에 Release가 '완주'했다면(=진짜 곡 공백을 거쳤다면) Idle을 유지한다.
                // gap 대부분은 시작 순간 Release가 트리거되지만(hasPending이 뒤늦게 섬), 다음 연계에
                // 인터럽트되면 Release는 완주하지 못한다 → 그 경우는 콤보 gap이므로 Sprint.
                // Release가 releaseEndTime까지 완주한 경우만 진짜 공백 → Idle.
                SwitchBaseStateUnlessConverging(releaseCompleted ? idleStateHash : sprintStateHash);
                ApplyBlendOut();
            }
            // 수렴 중에는 웨이트를 올리지 않는다 — 올리면 Attack 레이어가 base를 덮어
            // 애써 건 수렴 모션이 화면에서 사라진다(아직 공격 클립은 시작 전이다).
            else if (Time.time < convergeUntil) ApplyBlendOut();
            else ApplyBlendIn();
            return;
        }

        // 연계 X — Release를 완주시킨 뒤 Idle로 내린다.
        if (!releaseTriggered) TriggerRelease();

        if (Time.time < releaseEndTime)
        {
            ApplyBlendIn();
            return;
        }

        // 곡 공백 복귀 — 평상시 Idle로 되돌린다.
        releaseCompleted = true; // Release가 완주했다 → 이후 뒤늦게 hasPending이 서도 Idle 유지(다음 PlaySlot까지).
        SwitchBaseStateUnlessConverging(idleStateHash);
        ApplyBlendOut();
    }

    /// <summary>
    /// base 로코모션 레이어를 지정 스테이트로 CrossFade한다(경로 ②=Sprint / 경로 ③=Idle).
    /// 목표가 현재와 같으면 즉시 반환해 매 프레임 재진입을 막는다. 전환은 Attack 웨이트에 가려진 동안 일어나므로 눈에 띄지 않는다.
    /// </summary>
    /// <summary>
    /// 결투 수렴 구간의 로코모션. <b>창으로 갈린다</b> — 거리가 아니다.
    ///
    /// <para>기준은 <b>"클립 하나가 창을 채우는가"</b>다. Quickshift(대시)는 <b>루프가 아니라 1초짜리 단발</b>이라
    /// 창이 그보다 길면 클립이 먼저 끝나고 남은 시간은 그냥 미끄러진다 — 예전엔 거리로 갈라서
    /// 평균 창(1.48초) 대부분이 이 구멍에 빠졌다. Sprint는 루프라 길이에 상관없이 채운다.</para>
    ///
    /// <list type="bullet">
    /// <item>창 &gt; 클립 길이 → <b>Sprint</b>. 이동 속도에 맞춰 배속(다리와 몸이 따로 놀지 않게)</item>
    /// <item>창 ≤ 클립 길이 → <b>Quickshift</b>. 창 안에 완주하도록 배속을 역산</item>
    /// </list>
    ///
    /// <para><b>배속에 상한을 두지 않는다.</b> 자르면 클립이 창 안에 완주하지 못해 몸은 도착했는데
    /// 다리는 대시 도중에 끊긴다. 상한이 필요 없는 이유는 <b>배속이 올라가는 만큼 화면에 남는 시간도 같이 줄기</b>
    /// 때문이다: 8배속 Quickshift는 0.12초짜리라 "튀는 클립"이 아니라 순식간에 붙는 그림으로 읽힌다.</para>
    ///
    /// <para>거의 안 움직이는 경우(<see cref="convergeMinDistance"/> 미만)는 아무것도 하지 않는다 —
    /// 제자리에서 발을 구르면 더 부자연스럽다.</para>
    /// </summary>
    private void HandleDuelScheduled(EnemySpace.EnemyDirector.DuelPlan plan)
    {
        float distance = Vector3.ProjectOnPlane(plan.PlayerPosition - transform.position, Vector3.up).magnitude;
        float window = plan.ArriveTime - Time.time;

        if (runningLayerIndex < 0) { LogConverge("레이어 없음", distance, window); return; }
        if (distance < convergeMinDistance) { LogConverge("거리 부족", distance, window); return; }
        if (window <= 0f) { LogConverge("시간 없음", distance, window); return; }

        // 도착할 때까지 base의 주인은 수렴이다. 복귀 로직이 매 프레임 되찾아가지 못하게 막는다.
        convergeUntil = plan.ArriveTime;

        float dashLength = quickshiftClip != null ? quickshiftClip.length : 1f;

        if (window > dashLength)
        {
            // 실제 이동 속도에 배속을 맞춘다 — 안 맞추면 발이 지면을 긁는다.
            float runSpeed = Mathf.Max(0.1f, distance / window) / Mathf.Max(sprintReferenceSpeed, 0.01f);
            animator.SetFloat(sprintSpeedHash, runSpeed);

            // 배속이 바뀌었으므로 같은 스테이트라도 처음부터 다시 건다(SwitchBaseState는 같으면 조기 반환).
            animator.CrossFadeInFixedTime(sprintStateHash, baseCrossFadeDuration, runningLayerIndex, 0f);
            currentBaseStateHash = sprintStateHash;
            LogConverge($"Sprint x{runSpeed:0.00}", distance, window);
            return;
        }

        // 클립 길이 / 남은 시간 = 완주에 필요한 배속. 하한 1(느리게 늘이지 않는다), 상한은 없다.
        float speed = Mathf.Max(1f, dashLength / window);
        animator.SetFloat(quickshiftSpeedHash, speed);
        animator.CrossFadeInFixedTime(quickshiftStateHash, baseCrossFadeDuration, runningLayerIndex, 0f);
        currentBaseStateHash = quickshiftStateHash;
        LogConverge($"Quickshift x{speed:0.00}", distance, window);
    }

    /// <summary>
    /// 수렴 로코모션의 <b>판단 근거</b>를 남긴다. 모션이 안 나올 때 원인이 거리인지 시간인지 배선인지
    /// 화면만 봐서는 구분할 수 없어서, 결정마다 한 줄씩 찍는다. 에디터 전용이라 빌드에는 없다.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogConverge(string decision, float distance, float window)
    {
        if (!logConvergeDecision) return;

        Debug.Log($"[CharacterActionPlayer] 수렴 로코모션 → {decision}  " +
                  $"(거리 {distance:0.00}m / 창 {window:0.00}s / " +
                  $"대시클립 {(quickshiftClip != null ? quickshiftClip.length : 1f):0.00}s · 최소거리 {convergeMinDistance:0.00})", this);
    }

    private void WarnIfMissingBaseState(int stateHash, string stateName)
    {
        if (runningLayerIndex < 0 || animator.HasState(runningLayerIndex, stateHash)) return;

        Debug.LogError(
            $"[CharacterActionPlayer] base 레이어 '{runningLayerName}'에 스테이트 '{stateName}'가 없습니다 — " +
            "전환이 조용히 무시되어 포즈가 굳습니다.", this);
    }

    /// <summary>
    /// 복귀 경로에서 쓰는 base 전환. <b>수렴 구간에는 물러난다.</b>
    ///
    /// <para><see cref="Update"/>의 복귀 로직은 "액션이 없으면 base는 내 것"이라고 가정하고
    /// 매 프레임 Idle/Sprint로 되돌린다. 그대로 두면 <see cref="HandleDuelScheduled"/>가 건 수렴 모션이
    /// <b>다음 프레임에 즉시 덮인다</b> — 로그에는 Quickshift가 찍히는데 화면에는 안 나오는 이유다.</para>
    /// </summary>
    private void SwitchBaseStateUnlessConverging(int stateHash)
    {
        if (Time.time < convergeUntil) return;
        SwitchBaseState(stateHash);
    }

    private void SwitchBaseState(int stateHash)
    {
        if (runningLayerIndex < 0 || stateHash == currentBaseStateHash) return;
        animator.CrossFadeInFixedTime(stateHash, baseCrossFadeDuration, runningLayerIndex, 0f);
        currentBaseStateHash = stateHash;
    }

    /// <summary>
    /// 트림 끝에서 Release(마무리) 스테이트로 직접 넘어간다. <b>Release 진입 경로는 이 호출 하나뿐이다.</b>
    ///
    /// 애니메이터의 Attack→Release 전이(과거 `ExitTime = 1.0`)는 제거했다. 그 전이는 클립 전체가 끝나야 발동해,
    /// 트림 끝과 클립 끝이 다른 클립(대부분)에서 Release가 1초 이상 늦게 튀어나왔고, 연계 경로에서도 코드와 무관하게 Release가 새어 나왔다.
    /// 진입을 코드로 일원화해 모든 클립이 트림 끝 기준으로 동일하게 동작하고, 연계 시에는 아예 호출되지 않는다.
    ///
    /// 재생 시간은 releaseDuration으로 고정하고 배속을 역산하므로, 종료 시각을 시간 계산만으로 알 수 있다
    /// (스테이트 폴링은 전이 중 현재/다음 스테이트가 뒤바뀌어 오탐이 잦아 쓰지 않는다).
    /// </summary>
    private void TriggerRelease()
    {
        releaseTriggered = true;

        float length = releaseClip != null ? releaseClip.length : 0f;
        if (length <= 0f)
        {
            // 클립 미배선 — 압축할 대상이 없으므로 곧바로 blend-out으로 넘긴다.
            releaseEndTime = Time.time;
            return;
        }

        float duration = Mathf.Max(releaseDuration, 0.01f);
        animator.SetFloat(releaseSpeedHash, Mathf.Max(length / duration, 0.01f));
        releaseEndTime = Time.time + duration;
        animator.CrossFadeInFixedTime(releaseStateHash, releaseCrossFadeDuration, attackLayerIndex, 0f);
    }

    /// <summary>다음 공격이 comboLinkWindow 안에 시작되는가(= Release를 생략할 연계인가). 예약 정보를 그대로 쓰므로 추가 이벤트 구독이 필요 없다.</summary>
    private bool IsLinkedToNextAction()
    {
        return hasPending && (pendingScheduleStart - actionEndTime) <= comboLinkWindow;
    }

    /// <summary>연계 중, Sprint 노출 창(recovery 끝 ~ 다음 공격 시작)이 minRunExposure 이상인가.
    /// <b>고정 간격으로 판정한다</b>(Time.time이 아니라 recoveryEndTime 기준). 매 프레임 줄어드는 잔여시간으로 재면
    /// 창 도중 문턱을 밑돌아 blend-out↔blend-in이 뒤집히고, 그 순간 stale한 blendInStartTime 탓에 웨이트가 1로 튄다.</summary>
    private bool HasRoomForRunExposure()
    {
        return (pendingScheduleStart - recoveryEndTime) >= minRunExposure;
    }

    /// <summary>레이어 웨이트를 액션 시작 시점의 값에서 1까지 올린다. 이미 1이었으면 사실상 즉시 완료된다.</summary>
    private void ApplyBlendIn()
    {
        if (blendInFromWeight >= 1f)
        {
            if (animator.GetLayerWeight(attackLayerIndex) < 1f)
                animator.SetLayerWeight(attackLayerIndex, 1f);
            return;
        }

        float t = Mathf.Clamp01((Time.time - blendInStartTime) / Mathf.Max(layerBlendInDuration, 0.0001f));
        animator.SetLayerWeight(attackLayerIndex, Mathf.Lerp(blendInFromWeight, 1f, Mathf.SmoothStep(0f, 1f, t)));
    }

    /// <summary>레이어 웨이트를 0으로 내려 base Running Layer(Idle/Sprint)를 드러낸다. 시작 웨이트는 '내리기로 결정한 시점'의 값으로 래치한다.</summary>
    private void ApplyBlendOut()
    {
        if (!blendOutLatched)
        {
            blendOutLatched = true;
            blendOutStartTime = Time.time;
            blendOutFromWeight = animator.GetLayerWeight(attackLayerIndex);
        }

        if (blendOutFromWeight <= 0f) return;

        float t = Mathf.Clamp01((Time.time - blendOutStartTime) / Mathf.Max(blendOutDuration, 0.0001f));
        float w = blendOutFromWeight * (1f - Mathf.SmoothStep(0f, 1f, t));

        if (w <= 0f)
        {
            blendOutFromWeight = 0f; // 이후 프레임에서 재설정하지 않는다.
            animator.SetLayerWeight(attackLayerIndex, 0f);
            return;
        }

        animator.SetLayerWeight(attackLayerIndex, w);
    }

    // ─────────────────────────── 성공 애니 예약/시작 ───────────────────────────

    /// <summary>
    /// 판정 대상이 선두가 되면 대응 애니 시작을 예약한다(임팩트 프레임을 칼이 닿는 시각에 맞추는 조기 시작).
    ///
    /// <b>클립은 누가 휘두르는가로 갈린다</b> — 적이 공격자면 패링, 플레이어가 공격자면 공격.
    /// 값은 <see cref="EnemySpace.EnemyDirector.CurrentAttacker"/>에서 <b>당겨 온다</b>(pull).
    /// 밀어 넣으면 FIFO 큐가 둘이 되고 둘이 어긋나는 순간을 디버깅하게 된다.
    ///
    /// <para>순서는 보장된다: <c>SetPattern</c> 안에서 <c>OnPatternQueued</c>가 <c>OnJudgeTargetBegan</c>보다 먼저 발행되고,
    /// 승계 경로에서도 <c>OnPatternComplete</c>(디렉터가 dequeue) → <c>RaiseJudgeTargetBegan</c> 순이다.</para>
    /// </summary>
    private void HandleJudgeTargetBegan(JudgeTargetInfo info)
    {
        missedThisTarget = false;
        hasPending = false;
        hasPendingHit = false;

        if (info.Template == null) return;

        currentAttacker = enemyDirector != null ? enemyDirector.CurrentAttacker : EnemySpace.Attacker.Player;

        // 칼이 닿는 시각 — 적 공격·표적 절단과 반드시 같은 식이어야 한다.
        float impactAlignTime = info.Deadline + info.Template.ImpactOffset;
        pendingImpactAlignTime = impactAlignTime;

        var alignment = currentAttacker == EnemySpace.Attacker.Enemy
            ? info.Template.PlayerParry
            : info.Template.PlayerAttack;

        if (alignment != null && alignment.IsUsable)
        {
            pendingClip = alignment.Clip;
            pendingStartOffset = alignment.StartOffset;
            pendingDur = alignment.ResolvedDuration;
            pendingImpactSpan = alignment.ResolvedImpactSpan;
            pendingBaseSpeed = alignment.Speed;
        }
        else
        {
            // 폴백: 아직 ClipAlignment로 이관되지 않은 기존 패턴 에셋(구 SuccessAnimationClip + 트림 4필드).
            AnimationClip clip = info.Template.SuccessAnimationClip;
            if (clip == null) return; // 미지정 — 무연출

            float startOffset = info.Template.AnimationStartOffset;
            float dur = info.Template.AnimationDuration > 0f ? info.Template.AnimationDuration : clip.length - startOffset;
            if (dur <= 0f) return;

            pendingClip = clip;
            pendingStartOffset = startOffset;
            pendingDur = dur;
            pendingImpactSpan = ResolveImpactSpan(info.Template, startOffset, dur);
            pendingBaseSpeed = info.Template.AnimationSpeed;
        }

        float playTime = pendingImpactSpan / pendingBaseSpeed; // 지정 배속으로 임팩트까지 가는 데 걸리는 시간
        pendingScheduleStart = Mathf.Max(impactAlignTime - playTime, info.FirstNodeTime);
        hasPending = true;
    }

    /// <summary>
    /// 트림 시작부터 임팩트 프레임까지의 길이(클립 초). 임팩트가 오서링되지 않았거나 트림 범위 밖이면
    /// <b>트림 끝</b>으로 폴백한다 — 그래야 값이 없는 기존 패턴도 그대로 동작한다.
    /// </summary>
    private static float ResolveImpactSpan(Pattern template, float startOffset, float dur)
    {
        float impactTime = template.AnimationImpactTime;
        if (impactTime <= 0f) return dur;

        float span = impactTime - startOffset;
        if (span <= 0f || span > dur) return dur;

        return span;
    }

    /// <summary>
    /// 예약된 성공 애니의 시작 시각에 도달하면 재생한다. 남은 시간 기준으로 배속을 재계산해 <b>임팩트 프레임</b>을 정렬한다.
    /// 배속은 트림 전체에 걸리므로 임팩트 이후 잔여 구간도 같은 배속으로 이어 재생된다.
    /// </summary>
    private void TryStartPendingSuccess()
    {
        if (!hasPending || missedThisTarget || Time.time < pendingScheduleStart) return;

        float remaining = Mathf.Max(pendingImpactAlignTime - Time.time, 0.0001f);
        float needed = pendingImpactSpan / remaining;
        float cap = Mathf.Max(maxAttackSpeed, pendingBaseSpeed);
        float speed = Mathf.Clamp(needed, pendingBaseSpeed, cap);

        // 클램프가 걸리면 임팩트 프레임이 제시각에 못 온다. 표적 절단은 티가 안 나지만
        // 패링은 칼끼리 만나는 거라 즉시 보인다 — 조용히 어긋나는 게 최악이라 알린다.
        if (needed > cap && currentAttacker == EnemySpace.Attacker.Enemy)
        {
            Debug.LogWarning(
                $"[CharacterActionPlayer] 패링 클립이 배속 상한({cap:0.00})에 걸려 임팩트 정렬이 어긋납니다 " +
                $"(필요 {needed:0.00}배). 클립의 ImpactTime을 앞으로 당기세요.", this);
        }

        PlaySlot(pendingClip, pendingStartOffset, pendingDur, speed, isSwing: true);
        hasPending = false;
    }

    // ─────────────────────────── 히트스톱 ───────────────────────────

    /// <summary>
    /// 임팩트 프레임에서 <b>공격 클립만</b> 멈춘다. 판정·오디오·이동은 전혀 건드리지 않는다 —
    /// 멈추는 것은 Animator의 Speed Multiplier(<c>AttackSpeed</c>) 하나뿐이다.
    /// (<c>Time.timeScale</c>은 쓸 수 없다. 채보는 <c>audioSource.time</c>으로 도는데 판정은 <c>Time.time</c>이라
    /// 시계를 내리면 둘이 영구히 갈라진다 — 히트스톱 한 번이 <c>perfectWindow</c>(0.05초)를 넘는다.)
    ///
    /// <para><b>⚠ 플레이어는 '밀기'다. 캐치업이 아니다</b> — 적(<c>EnemyView.ApplyHitStop</c>)과 모델이 다르다.
    /// 공격 클립은 임팩트를 <b>트림 끝 근처</b>에 찍으므로 임팩트 시점에 남은 트림 내용이
    /// 실측 0.036~0.109초뿐이다(전 패턴 10/10). 정지 0.08초가 그보다 길어서
    /// <b>재개하는 순간 이미 원래 종료 시각이 지나 있다</b> — 압축할 시간이 음수라 캐치업이 원리적으로 불가능하다.
    /// 반대로 적 사망 클립은 <c>ImpactTime</c>이 트림 <i>시작</i> 근처라(§11-3) 잔여가 클립 대부분이고,
    /// 그래서 그쪽만 원래 <c>burstTime</c>을 지킬 수 있다.</para>
    ///
    /// <para>그래서 여기서는 <b>복귀 스케줄을 정지 시간만큼 통째로 뒤로 민다</b>
    /// (<see cref="actionEndTime"/>·<see cref="recoveryEndTime"/>). 클립은 멈춘 자리에서 원래 배속으로 이어진다.
    /// <b>임팩트는 이미 지나간 뒤라 §6의 정렬은 깨지지 않는다</b> — 미는 것은 마무리 동작과 복귀뿐이다.</para>
    ///
    /// <para>비용은 하나다: 임팩트 이후가 전부 <paramref name="duration"/>만큼 늦는다.
    /// <b>다음 공격은 자기 Deadline에서 독립적으로 예약되므로 제시각에 그대로 시작한다</b> —
    /// 실제로 줄어드는 것은 그 사이 Sprint가 보이는 시간뿐이고, <see cref="minRunExposure"/>(0.35초) 대비 여유가 있다.</para>
    /// </summary>
    /// <returns>실제로 멈췄으면 true.</returns>
    public bool ApplyHitStop(float duration)
    {
        if (attackLayerIndex < 0 || duration <= 0f) return false;
        if (hitStopped) return false;

        // 트림 구간(스윙)을 재생 중일 때만 의미가 있다. speedRestored가 서 있으면 이미 마무리 동작이다.
        if (speedRestored || !swingActive) return false;

        hitStopReleaseTime = Time.time + duration;
        hitStopped = true;

        actionEndTime += duration;
        recoveryEndTime += duration;

        animator.SetFloat(attackSpeedHash, 0f);
        return true;
    }

    // ─────────────────────────── 첫 미스 → 힛 ───────────────────────────

    /// <summary>
    /// 판정 대상의 첫 미스 순간: 예약/진행 중이던 대응 애니를 <b>즉시</b> 취소한다.
    ///
    /// <para><b>피격은 즉시가 아니라 <c>impactTime</c>에 예약한다.</b> 첫 미스 순간엔 적 칼이 아직 도착 전이라
    /// 그때 맞으면 칼보다 먼저 맞는 그림이 된다.</para>
    ///
    /// <para>플레이어가 공격자였다면(<c>Attacker.Player</c>) <b>피격 자체가 없다</b> — 적은 애초에 휘두르지 않았고
    /// 뒤로 물러나 회피할 뿐이다. 헛스윙으로 끝난다.</para>
    /// </summary>
    private void HandleJudgeTargetFirstMiss()
    {
        if (missedThisTarget) return;
        missedThisTarget = true;
        hasPending = false; // 예약 취소 → 원래 나올 베기 안 나옴

        if (currentAttacker != EnemySpace.Attacker.Enemy)
        {
            RaiseSwingEnded(); // 적 무방비 — 맞지 않는다. 진행 중이던 스윙만 끊는다.
            return;
        }

        hasPendingHit = true;
        pendingHitTime = pendingImpactAlignTime;
    }

    /// <summary>적 칼이 도착하는 시각에 피격을 재생한다. 예약 메커니즘은 성공 애니와 동일하다.</summary>
    private void TryStartPendingHit()
    {
        if (!hasPendingHit || Time.time < pendingHitTime) return;
        hasPendingHit = false;

        AnimationClip hit = NextHitClip();
        if (hit != null)
            PlaySlot(hit, 0f, hit.length, 1f, isSwing: false);
        else
            RaiseSwingEnded(); // 힛 클립이 없어도 진행 중이던 베기는 취소됐다.

        OnPlayerHit?.Invoke();
    }

    /// <summary>피격 리액션 클립을 번갈아 반환한다. 배선이 없으면 null. (랜덤을 원하면 이 인덱스 선택만 교체.)</summary>
    private AnimationClip NextHitClip()
    {
        if (hitClips == null || hitClips.Length == 0) return null;

        AnimationClip clip = hitClips[hitIndex];
        hitIndex = (hitIndex + 1) % hitClips.Length;
        return clip;
    }

    // ─────────────────────────── 재생 프리미티브 ───────────────────────────

    /// <summary>
    /// 주어진 클립의 [startOffset, startOffset+dur] 구간을 speed 배속으로 재생한다(듀얼 슬롯 교대 + 크로스페이드).
    ///
    /// <paramref name="isSwing"/>은 이 클립이 <b>칼을 휘두르는 동작인지</b>다(성공 베기 = true, 피격 = false).
    /// 진입 시 <b>무조건</b> 이전 스윙을 끝내는데, 이 클립이 이전 액션을 트림 끝 전에 인터럽트했을 수 있기 때문이다
    /// (베기 도중 미스 → 피격으로 끊김). 그러지 않으면 트레일이 켜진 채 남는다.
    /// </summary>
    private void PlaySlot(AnimationClip clip, float startOffset, float dur, float speed, bool isSwing)
    {
        if (overrideController == null || placeholderA == null || placeholderB == null || attackLayerIndex < 0) return;

        if (isSwing) RaiseSwingBegan();
        else RaiseSwingEnded();

        int targetStateHash = useSlotA ? attackStateAHash : attackStateBHash;
        AnimationClip targetPlaceholder = useSlotA ? placeholderA : placeholderB;

        overrideController[targetPlaceholder] = clip;

        animator.SetFloat(attackSpeedHash, speed);
        speedRestored = false;

        // 새 클립이 들어오면 진행 중이던 히트스톱은 의미를 잃는다(정지시킬 대상 자체가 바뀌었다).
        hitStopped = false;

        playStartTime = Time.time;
        playSpeed = Mathf.Max(speed, 0.01f);
        playDur = dur;

        actionEndTime = Time.time + dur / Mathf.Max(speed, 0.01f);
        recoveryEndTime = actionEndTime + recoveryHoldDuration;

        // 웨이트는 즉시 1로 점프시키지 않는다. 복귀(blend-out) 도중 인터럽트되면 현재 웨이트에서 이어 올린다.
        blendInStartTime = Time.time;
        blendInFromWeight = animator.GetLayerWeight(attackLayerIndex);
        blendOutLatched = false;
        releaseTriggered = false;
        releaseCompleted = false; // 새 공격이 시작됐다 → 다음 gap은 다시 Sprint 후보.
        releaseEndTime = 0f;

        // CrossFadeInFixedTime의 fixedTimeOffset은 '클립 초'가 아니라 스테이트 speed가 곱해지는 '스테이트 재생 초'로 해석된다.
        // AttackSpeed를 먼저 걸어둔 상태이므로 startOffset(클립 초)을 speed로 나눠 넘겨야 실제 클립상 startOffset 지점에서 시작한다.
        // (보정하지 않으면 startOffset*speed 지점에서 시작해 클립 끝에 조기 도달 → Exit Time 전이로 애니가 중간에 끊긴다.)
        animator.CrossFadeInFixedTime(targetStateHash, attackCrossFadeDuration, attackLayerIndex, startOffset / Mathf.Max(speed, 0.01f));

        useSlotA = !useSlotA;
    }
}
