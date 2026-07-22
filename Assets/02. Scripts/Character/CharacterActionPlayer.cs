using PatternSpace;
using UnityEngine;

/// <summary>
/// 캐릭터 액션을 <b>패턴 입력 도중</b> 재생한다.
/// - 성공(베기): 판정 대상이 시작되면, 마지막 노드 판정 시각에 끝나도록 `시작 = max(마지막노드 − 재생시간, 첫노드)` 지점에 예약해 조기 재생한다.
///   재생시간은 트림(AnimationStartOffset/Duration)과 패턴 배속(AnimationSpeed, 배속 하한)을 반영하고, 입력 구간이 짧으면 자동으로 더 배속한다(상한 maxAttackSpeed).
/// - 미스(첫 미스 1회): 그 순간 힛(Hit) 클립을 재생하고 예약/진행 중이던 성공 애니를 취소한다 — 재생 중이던 베기는 힛 크로스페이드로 즉시 끊긴다.
///
/// <b>트림 끝(actionEndTime)은 '재생이 끝나는 시각'이 아니다.</b> 클립은 스테이트에 통째로 물려 있어 그 뒤로도 마무리 동작(follow-through)이 계속 재생된다.
/// actionEndTime은 '임팩트 정렬이 끝나 복귀를 시작해도 되는 시각'일 뿐이며, 이 시점부터 다음 두 경로로 갈린다:
/// - <b>연계 있음(콤보)</b> — 다음 공격이 comboLinkWindow 안에 시작되면 Attack Layer 웨이트를 1로 <b>유지</b>한 채 다음 클립으로 크로스페이드한다.
///   레이어를 내렸다 올리지 않으므로 사이에 Run이 비집고 들어오지 않는다. 실측상 이쪽이 주 경로다(채보 연결의 약 95%).
/// - <b>연계 없음</b> — recoveryHoldDuration 동안 마무리 동작을 노출한 뒤, blendOutDuration 동안 웨이트를 0으로 내려 base Running Layer(Run)와 크로스블렌드한다.
///   공격 클립에 남은 마무리 구간이 없어 Release 스테이트로 흘러간 경우에는, Release를 releaseDuration에 맞춰 압축해 <b>끝까지 재생한 뒤</b> Run으로 페이드한다(재생 도중 잘리지 않게).
/// 두 경로 모두 actionEndTime에 AttackSpeed를 1로 되돌려 마무리 동작이 배속으로 지나가지 않게 한다.
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

    [Header("Clips")]
    [Tooltip("패턴 실패 시 재생할 피격 리액션 클립들. 번갈아 재생된다.")]
    [SerializeField] private AnimationClip[] hitClips; // Hit1, Hit2

    [Header("Tuning")]
    [Tooltip("공격 간 크로스페이드 시간(초). 연계(콤보)가 주 경로이므로 이 값이 체감 품질을 좌우한다.")]
    [SerializeField] private float attackCrossFadeDuration = 0.15f;
    [Tooltip("이 시간(초) 안에 다음 공격이 시작되면 연계로 보고 레이어 웨이트를 내리지 않는다.")]
    [SerializeField] private float comboLinkWindow = 1.0f;
    [Tooltip("연계가 없을 때, 트림 끝 이후 마무리 동작을 웨이트 1로 노출하는 시간(초).")]
    [SerializeField] private float recoveryHoldDuration = 0.25f;
    [Tooltip("Run으로 녹아드는 시간(초). 레이어 웨이트를 0으로 내리는 구간.")]
    [SerializeField] private float blendOutDuration = 0.30f;
    [Tooltip("Release(마무리) 스테이트를 이 시간(초) 안에 완주시킨다. 배속은 클립 길이에서 역산된다(길이 2.33초 / 0.9초 ≈ 2.6배).")]
    [SerializeField] private float releaseDuration = 0.9f;
    [Tooltip("액션 시작 시 레이어 웨이트를 현재값에서 1까지 올리는 시간(초). 복귀 도중 인터럽트될 때의 스냅을 막는다.")]
    [SerializeField] private float layerBlendInDuration = 0.08f;
    [Tooltip("자동 배속(입력 구간이 짧을 때)의 상한.")]
    [SerializeField] private float maxAttackSpeed = 2.5f;

    private AnimatorOverrideController overrideController;
    private int attackLayerIndex = -1;
    private int attackStateAHash;
    private int attackStateBHash;
    private int attackSpeedHash;
    private int releaseStateHash;
    private int releaseSpeedHash;
    private bool releaseSpeedApplied; // 이번 액션의 Release 진입 시 배속을 걸었는지
    private bool hasPlayedAction;     // 액션을 한 번이라도 재생했는지(시작 시 기본 스테이트가 Release라 필요)
    private bool attackStateObserved; // 이번 액션의 공격 스테이트에 실제로 진입한 것을 확인했는지
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

        // Release 진입을 매 프레임 확인한다(아래 조건의 단락 평가에 가리지 않도록 먼저 호출).
        bool releaseHolding = UpdateReleaseState();

        // 웨이트 1을 유지(또는 blend-in)하는 경우: 재생 중 / 연계 있음(콤보) / Release 재생 중 / 마무리 동작 노출 중.
        if (Time.time < actionEndTime || IsLinkedToNextAction() || releaseHolding || Time.time < recoveryEndTime)
        {
            ApplyBlendIn();
            return;
        }

        ApplyBlendOut();
    }

    /// <summary>
    /// 공격 클립이 끝나 Release(마무리) 스테이트로 흘러갔는지 확인하고, 그렇다면 배속을 걸어 <b>끝까지 재생되게</b> 웨이트를 유지시킨다.
    /// Release는 2.3초짜리 논루프 클립이라 정상 속도로는 복귀 창 안에 담기지 않아 재생 도중 웨이트가 0으로 잘렸다.
    /// releaseDuration(목표 재생 시간)에서 배속을 역산해 압축 재생하고, 완주시킨 뒤 Run으로 페이드한다.
    /// </summary>
    /// <returns>Release 재생이 끝나지 않아 웨이트를 유지해야 하면 true.</returns>
    private bool UpdateReleaseState()
    {
        if (!hasPlayedAction) return false; // 게임 시작 직후의 기본 스테이트(Release)를 액션으로 오인하지 않는다.

        AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
        bool curIsAttack = cur.shortNameHash == attackStateAHash || cur.shortNameHash == attackStateBHash;

        // 이번 액션의 공격 스테이트에 아직 진입하지 않았다면 Release는 '직전 액션의 잔상'이다.
        // CrossFade를 건 다음 프레임에도 IsInTransition이 아직 false일 수 있어, 전이 방향만으로는 걸러지지 않는다.
        // 공격 스테이트를 실제로 관측한 뒤부터만 Release를 마무리로 인정한다.
        if (!attackStateObserved)
        {
            if (curIsAttack) attackStateObserved = true;
            return false;
        }

        bool entering = animator.IsInTransition(attackLayerIndex)
            && animator.GetNextAnimatorStateInfo(attackLayerIndex).shortNameHash == releaseStateHash;
        bool inRelease = !animator.IsInTransition(attackLayerIndex) && cur.shortNameHash == releaseStateHash;

        if (!entering && !inRelease) return false;

        if (!releaseSpeedApplied)
        {
            // 목표 재생 시간에 맞춰 배속을 역산한다. 길이는 스테이트에서 읽으므로 클립을 따로 배선할 필요가 없다.
            // PlaySlot에서 ReleaseSpeed를 1로 되돌려 두므로 이 시점의 length는 배속이 섞이지 않은 클립 길이다.
            float length = entering
                ? animator.GetNextAnimatorStateInfo(attackLayerIndex).length
                : cur.length;

            float speed = (releaseDuration > 0.01f && length > 0f)
                ? length / releaseDuration
                : 1f;

            animator.SetFloat(releaseSpeedHash, Mathf.Max(speed, 0.01f));
            releaseSpeedApplied = true;
        }

        // 전이 중(아직 Release 시작 전)이거나, 재생이 끝나지 않았으면 유지한다.
        return entering || cur.normalizedTime < 1f;
    }

    /// <summary>다음 공격이 comboLinkWindow 안에 시작되는가. 예약 정보를 그대로 쓰므로 추가 이벤트 구독이 필요 없다.</summary>
    private bool IsLinkedToNextAction()
    {
        return hasPending && (pendingScheduleStart - actionEndTime) <= comboLinkWindow;
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
        releaseSpeedApplied = false;
        attackStateObserved = false;
        hasPlayedAction = true;
        // 다음 Release 진입 때 배속이 섞이지 않은 클립 길이를 읽을 수 있도록 되돌려 둔다.
        animator.SetFloat(releaseSpeedHash, 1f);

        // CrossFadeInFixedTime의 fixedTimeOffset은 '클립 초'가 아니라 스테이트 speed가 곱해지는 '스테이트 재생 초'로 해석된다.
        // AttackSpeed를 먼저 걸어둔 상태이므로 startOffset(클립 초)을 speed로 나눠 넘겨야 실제 클립상 startOffset 지점에서 시작한다.
        // (보정하지 않으면 startOffset*speed 지점에서 시작해 클립 끝에 조기 도달 → Exit Time 전이로 애니가 중간에 끊긴다.)
        animator.CrossFadeInFixedTime(targetStateHash, attackCrossFadeDuration, attackLayerIndex, startOffset / Mathf.Max(speed, 0.01f));

        useSlotA = !useSlotA;
    }
}
