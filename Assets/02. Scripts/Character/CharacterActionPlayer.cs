using PatternSpace;
using UnityEngine;

/// <summary>
/// 캐릭터 액션을 <b>패턴 입력 도중</b> 재생한다.
/// - 성공(베기): 판정 대상이 시작되면, 마지막 노드 판정 시각에 끝나도록 `시작 = max(마지막노드 − 재생시간, 첫노드)` 지점에 예약해 조기 재생한다.
///   재생시간은 트림(AnimationStartOffset/Duration)과 패턴 배속(AnimationSpeed, 배속 하한)을 반영하고, 입력 구간이 짧으면 자동으로 더 배속한다(상한 maxAttackSpeed).
/// - 미스(첫 미스 1회): 그 순간 힛(Hit) 클립을 재생하고 예약/진행 중이던 성공 애니를 취소한다 — 재생 중이던 베기는 힛 크로스페이드로 즉시 끊긴다.
///
/// <b>트림 끝(actionEndTime)은 '재생이 끝나는 시각'이 아니다.</b> 클립은 스테이트에 통째로 물려 있어 그 뒤로도 마무리 동작(follow-through)이 계속 재생된다.
/// actionEndTime은 '임팩트 정렬이 끝나 복귀를 시작해도 되는 시각'일 뿐이다. 이 시점에 recoveryHoldDuration 동안 마무리 동작을 웨이트 1로 노출한 뒤(세 경로 공통), 아래 세 경로로 갈린다:
/// - <b>연계 O · 간격 부족</b> — 다음 공격이 comboLinkWindow 안이면서 minRunExposure보다 촘촘히 붙는다. 웨이트를 <b>1로 유지</b>한 채 다음 클립으로 크로스페이드한다(Run·Release 모두 생략). 짧은 연계의 웨이트 깜빡임을 막는다.
/// - <b>연계 O · 간격 여유</b> — comboLinkWindow 안이되 다음 공격까지 minRunExposure 이상 남는다. blendOutDuration 동안 웨이트를 0으로 내려 <b>그 사이 base Sprint(Sprint_HS)를 노출</b>하고, 다음 공격에서 다시 올린다(Release 생략). 실측상 이쪽이 주 경로다(채보 연결의 약 95%).
/// - <b>연계 X</b> — comboLinkWindow 밖(곡 공백). <b>트림 끝에서 곧바로 Release 스테이트로 CrossFade</b>하고(애니메이터에는 Attack→Release 전이가 없다 — 진입은 코드가 유일하게 통제한다),
///   releaseDuration에 맞춰 압축해 <b>끝까지 재생한 뒤</b> blendOutDuration 동안 base Run으로 페이드한다.
///
/// <b>base 로코모션은 경로에 따라 클립이 갈린다.</b> base Running Layer는 평소 Run이 기본이고, 연계 O·간격 여유(콤보 사이)를 탈 때 Sprint로,
/// 연계 X(곡 공백 복귀)를 탈 때 다시 Run으로 CrossFade한다(SwitchBaseState). 전환은 Attack 웨이트에 가려진 동안 일어나 눈에 띄지 않는다.
/// 콤보 gap 대부분은 시작 순간 Release가 트리거되므로(hasPending이 뒤늦게 섬), "Release를 냈는가"가 아니라 <b>"Release가 releaseEndTime까지 완주했는가"(releaseCompleted)</b>로
/// 진짜 곡 공백과 콤보 gap을 가른다. 완주했으면 Run, 인터럽트됐으면 Sprint. releaseCompleted는 다음 PlaySlot에서 리셋된다.
/// 세 경로 모두 actionEndTime에 AttackSpeed를 1로 되돌려 마무리 동작이 배속으로 지나가지 않게 한다.
///
/// AnimatorOverrideController로 듀얼 슬롯(Attack_A, Attack_B)을 교대로 교체하며 재생해 모션 끊김(Popping)을 방지한다. 재생할 클립이 없으면 무연출로 넘어간다.
/// </summary>
public class CharacterActionPlayer : MonoBehaviour
{
    [SerializeField] private PatternHandler handler;
    [SerializeField] private Animator animator;

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
    [Tooltip("연계 X(공백/Release 복귀) 시 드러낼 base 스테이트 이름.")]
    [SerializeField] private string runStateName = "Run";
    [Tooltip("연계 O·간격 여유(콤보 사이 노출) 시 드러낼 base 스테이트 이름.")]
    [SerializeField] private string sprintStateName = "Sprint";
    [Tooltip("base 레이어 Run↔Sprint 포즈 블렌딩 시간(초). base가 Attack 웨이트에 가려진 동안 진행된다.")]
    [SerializeField] private float baseCrossFadeDuration = 0.2f;

    [Header("Clips")]
    [Tooltip("패턴 실패 시 재생할 피격 리액션 클립들. 번갈아 재생된다.")]
    [SerializeField] private AnimationClip[] hitClips; // Hit1, Hit2

    [Header("Tuning")]
    [Tooltip("공격 간 크로스페이드 시간(초). 연계(콤보)가 주 경로이므로 이 값이 체감 품질을 좌우한다.")]
    [SerializeField] private float attackCrossFadeDuration = 0.15f;
    [Tooltip("이 시간(초) 안에 다음 공격이 시작되면 연계로 보고 Release(마무리)를 생략한다.")]
    [SerializeField] private float comboLinkWindow = 1.0f;
    [Tooltip("연계 중, 다음 공격까지 이 시간(초) 이상 남아 있으면 웨이트를 내려 그 사이 Run을 노출한다. 미만이면 웨이트 1을 유지해 바로 다음 공격으로 잇는다.")]
    [SerializeField] private float minRunExposure = 0.35f;
    [Tooltip("트림 끝 이후 마무리 동작을 웨이트 1로 노출하는 시간(초). 세 경로 공통.")]
    [SerializeField] private float recoveryHoldDuration = 0.25f;
    [Tooltip("Run으로 녹아드는 시간(초). 레이어 웨이트를 0으로 내리는 구간.")]
    [SerializeField] private float blendOutDuration = 0.15f;
    [Tooltip("Release(마무리) 스테이트를 이 시간(초) 안에 완주시킨다. 배속은 클립 길이에서 역산된다(길이 2.33초 / 0.9초 ≈ 2.6배).")]
    [SerializeField] private float releaseDuration = 0.9f;
    [Tooltip("공격 → Release 크로스페이드 시간(초).")]
    [SerializeField] private float releaseCrossFadeDuration = 0.10f;
    [Tooltip("액션 시작 시 레이어 웨이트를 현재값에서 1까지 올리는 시간(초). 복귀 도중 인터럽트될 때의 스냅을 막는다.")]
    [SerializeField] private float layerBlendInDuration = 0.12f;
    [Tooltip("자동 배속(입력 구간이 짧을 때)의 상한.")]
    [SerializeField] private float maxAttackSpeed = 2.5f;

    private AnimatorOverrideController overrideController;
    private int attackLayerIndex = -1;
    private int attackStateAHash;
    private int attackStateBHash;
    private int attackSpeedHash;
    private int releaseStateHash;
    private int releaseSpeedHash;
    private int runningLayerIndex = -1;
    private int runStateHash;
    private int sprintStateHash;
    private int currentBaseStateHash; // 현재 base 레이어가 향하는 스테이트(중복 CrossFade 방지)
    private bool releaseTriggered; // 이번 액션에서 Release로 넘어갔는지
    private bool releaseCompleted; // 이번 사이클에 Release가 releaseEndTime까지 완주했는지(= 진짜 곡 공백). base=Run 유지 판별용.
    private float releaseEndTime;  // Release 재생이 끝나는 시각(= 트리거 시각 + releaseDuration)
    private int hitIndex;         // Hit 클립 번갈아 재생용 커서
    private bool useSlotA = true; // 듀얼 슬롯 전환 플래그

    // 현재 액션의 복귀 스케줄.
    private float actionEndTime;   // 트림 구간이 끝나는 시각(= 복귀 판단 시작점)
    private float recoveryEndTime; // 연계가 없을 때 마무리 동작 노출이 끝나는 시각
    private bool speedRestored;    // actionEndTime에서 AttackSpeed를 1로 되돌렸는지

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
    private float pendingDur;
    private float pendingBaseSpeed;
    private float pendingScheduleStart;
    private float pendingLastNodeTime;
    private bool missedThisTarget; // 이번 판정 대상에서 이미 첫 미스 처리를 했는지

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
            Debug.LogError($"[CharacterActionPlayer] 레이어 '{runningLayerName}'를 찾을 수 없습니다 — Sprint/Run 전환이 동작하지 않습니다.", this);
        runStateHash = Animator.StringToHash(runStateName);
        sprintStateHash = Animator.StringToHash(sprintStateName);
        currentBaseStateHash = runStateHash; // base 레이어의 default 스테이트는 Run이다.

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
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnJudgeTargetBegan -= HandleJudgeTargetBegan;
            handler.OnJudgeTargetFirstMiss -= HandleJudgeTargetFirstMiss;
        }
    }

    void Update()
    {
        if (attackLayerIndex < 0) return;

        TryStartPendingSuccess();

        // 트림 구간이 끝나면 배속을 해제해 마무리 동작이 정상 속도로 재생되게 한다.
        if (!speedRestored && Time.time >= actionEndTime)
        {
            animator.SetFloat(attackSpeedHash, 1f);
            speedRestored = true;
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

        // 연계 O — Release는 생략한다. 다만 다음 공격까지의 간격이 충분할 때만 웨이트를 내려 Run을 노출하고,
        // 간격이 짧으면(minRunExposure 미만) 웨이트 1을 유지해 곧바로 다음 공격으로 잇는다(깜빡임 방지).
        if (IsLinkedToNextAction())
        {
            if (HasRoomForRunExposure())
            {
                // 콤보 사이 잠깐 달리는 구간 — Sprint를 드러낸다.
                // 단, 이번 사이클에 Release가 '완주'했다면(=진짜 곡 공백을 거쳤다면) Run을 유지한다.
                // gap 대부분은 시작 순간 Release가 트리거되지만(hasPending이 뒤늦게 섬), 다음 연계에
                // 인터럽트되면 Release는 완주하지 못한다 → 그 경우는 콤보 gap이므로 Sprint.
                // Release가 releaseEndTime까지 완주(RELEASE-RUN 도달)한 경우만 진짜 공백 → Run. 스펙: Release 출력 시 Run.
                SwitchBaseState(releaseCompleted ? runStateHash : sprintStateHash);
                ApplyBlendOut();
            }
            else ApplyBlendIn();
            return;
        }

        // 연계 X — Release를 완주시킨 뒤 Run으로 내린다.
        if (!releaseTriggered) TriggerRelease();

        if (Time.time < releaseEndTime)
        {
            ApplyBlendIn();
            return;
        }

        // 곡 공백 복귀 — 평상시 Run으로 되돌린다.
        releaseCompleted = true; // Release가 완주했다 → 이후 뒤늦게 hasPending이 서도 Run 유지(다음 PlaySlot까지).
        SwitchBaseState(runStateHash);
        ApplyBlendOut();
    }

    /// <summary>
    /// base 로코모션 레이어를 지정 스테이트로 CrossFade한다(경로 ②=Sprint / 경로 ③=Run).
    /// 목표가 현재와 같으면 즉시 반환해 매 프레임 재진입을 막는다. 전환은 Attack 웨이트에 가려진 동안 일어나므로 눈에 띄지 않는다.
    /// </summary>
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

    /// <summary>연계 중, Run 노출 창(recovery 끝 ~ 다음 공격 시작)이 minRunExposure 이상인가.
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

    /// <summary>레이어 웨이트를 0으로 내려 base Running Layer(Run)를 드러낸다. 시작 웨이트는 '내리기로 결정한 시점'의 값으로 래치한다.</summary>
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

    /// <summary>판정 대상이 선두가 되면 성공 애니 시작을 예약한다(마지막 노드에 끝을 맞추는 조기 시작).</summary>
    private void HandleJudgeTargetBegan(JudgeTargetInfo info)
    {
        missedThisTarget = false;
        hasPending = false;

        AnimationClip clip = info.Template != null ? info.Template.SuccessAnimationClip : null;
        if (clip == null) return; // 미지정 클립 — 무연출

        float startOffset = info.Template.AnimationStartOffset;
        float dur = info.Template.AnimationDuration > 0f ? info.Template.AnimationDuration : clip.length - startOffset;
        if (dur <= 0f) return;

        float baseSpeed = info.Template.AnimationSpeed;        // 배속 하한
        float playTime = dur / baseSpeed;                     // 지정 배속으로 재생 시 소요 시간
        float scheduleStart = Mathf.Max(info.LastNodeTime - playTime, info.FirstNodeTime);

        pendingClip = clip;
        pendingStartOffset = startOffset;
        pendingDur = dur;
        pendingBaseSpeed = baseSpeed;
        pendingScheduleStart = scheduleStart;
        pendingLastNodeTime = info.LastNodeTime;
        hasPending = true;
    }

    /// <summary>예약된 성공 애니의 시작 시각에 도달하면 재생한다. 남은 시간 기준으로 배속을 재계산해 마지막 노드에 정렬한다.</summary>
    private void TryStartPendingSuccess()
    {
        if (!hasPending || missedThisTarget || Time.time < pendingScheduleStart) return;

        float remaining = Mathf.Max(pendingLastNodeTime - Time.time, 0.0001f);
        float needed = pendingDur / remaining;
        float cap = Mathf.Max(maxAttackSpeed, pendingBaseSpeed);
        float speed = Mathf.Clamp(needed, pendingBaseSpeed, cap);

        PlaySlot(pendingClip, pendingStartOffset, pendingDur, speed);
        hasPending = false;
    }

    // ─────────────────────────── 첫 미스 → 힛 ───────────────────────────

    /// <summary>판정 대상의 첫 미스 순간: 예약/진행 중이던 성공 애니를 취소하고 힛을 재생한다(재생 중이던 베기는 힛 크로스페이드로 끊긴다).</summary>
    private void HandleJudgeTargetFirstMiss()
    {
        if (missedThisTarget) return;
        missedThisTarget = true;
        hasPending = false; // 예약 취소 → 원래 나올 베기 안 나옴

        AnimationClip hit = NextHitClip();
        if (hit != null)
            PlaySlot(hit, 0f, hit.length, 1f);
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

    /// <summary>주어진 클립의 [startOffset, startOffset+dur] 구간을 speed 배속으로 재생한다(듀얼 슬롯 교대 + 크로스페이드).</summary>
    private void PlaySlot(AnimationClip clip, float startOffset, float dur, float speed)
    {
        if (overrideController == null || placeholderA == null || placeholderB == null || attackLayerIndex < 0) return;

        int targetStateHash = useSlotA ? attackStateAHash : attackStateBHash;
        AnimationClip targetPlaceholder = useSlotA ? placeholderA : placeholderB;

        overrideController[targetPlaceholder] = clip;

        animator.SetFloat(attackSpeedHash, speed);
        speedRestored = false;

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
