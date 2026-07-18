using PatternSpace;
using UnityEngine;

/// <summary>
/// 캐릭터 액션을 <b>패턴 입력 도중</b> 재생한다.
/// - 성공(베기): 판정 대상이 시작되면, 마지막 노드 판정 시각에 끝나도록 `시작 = max(마지막노드 − 재생시간, 첫노드)` 지점에 예약해 조기 재생한다.
///   재생시간은 트림(AnimationStartOffset/Duration)과 패턴 배속(AnimationSpeed, 배속 하한)을 반영하고, 입력 구간이 짧으면 자동으로 더 배속한다(상한 maxAttackSpeed).
/// - 미스(첫 미스 1회): 그 순간 힛(Hit) 클립을 재생하고 예약/진행 중이던 성공 애니를 취소한다 — 재생 중이던 베기는 힛 크로스페이드로 즉시 끊긴다.
///
/// AnimatorOverrideController로 듀얼 슬롯(Attack_A, Attack_B)을 교대로 교체하며 재생해 모션 끊김(Popping)을 방지한다.
/// Attack Layer는 휴지 웨이트 0, 재생 중 1, 종료 후 0으로 페이드한다. 재생할 클립이 없으면 무연출로 넘어간다.
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

    [Header("Clips")]
    [Tooltip("패턴 실패 시 재생할 피격 리액션 클립들. 번갈아 재생된다.")]
    [SerializeField] private AnimationClip[] hitClips; // Hit1, Hit2

    [Header("Tuning")]
    [SerializeField] private float crossFadeDuration = 0.05f;
    [Tooltip("자동 배속(입력 구간이 짧을 때)의 상한.")]
    [SerializeField] private float maxAttackSpeed = 2.5f;
    [Tooltip("액션 종료 후 Attack Layer 웨이트를 0으로 내릴 때의 페이드 시간(초).")]
    [SerializeField] private float layerFadeOutDuration = 0.08f;

    private AnimatorOverrideController overrideController;
    private int attackLayerIndex = -1;
    private int attackStateAHash;
    private int attackStateBHash;
    private int attackSpeedHash;
    private int hitIndex;        // Hit 클립 번갈아 재생용 커서
    private float actionEndTime; // 현재 액션의 재생 종료 예정 시각(이 시각 이후 레이어 웨이트를 0으로 페이드)
    private bool useSlotA = true; // 듀얼 슬롯 전환 플래그

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

        // 원본 컨트롤러를 감싼 오버라이드 인스턴스를 씌운다. 이후 이 인스턴스의 클립만 런타임에 교체한다.
        overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
        animator.runtimeAnimatorController = overrideController;

        if (placeholderA == null || placeholderB == null)
            Debug.LogError("[CharacterActionPlayer] placeholderA 또는 placeholderB가 배선되지 않았습니다 — 오버라이드 키가 없어 클립 주입이 동작하지 않습니다.", this);

        // 휴지 상태 웨이트 0 보장.
        if (attackLayerIndex >= 0)
            animator.SetLayerWeight(attackLayerIndex, 0f);
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

        // 배속 해제: Attack_A/B에서 '다른 스테이트(=복귀 Sprint_Forward)'로 빠져나가는 전이 중이면 AttackSpeed를 1로 되돌린다.
        // 배속된 공격에 이어지는 복귀 구간이 정상 속도로 나오게 한다. 연속 공격(Attack→Attack)은 next가 Attack이므로 제외.
        if (animator.IsInTransition(attackLayerIndex))
        {
            int curHash = animator.GetCurrentAnimatorStateInfo(attackLayerIndex).shortNameHash;
            int nextHash = animator.GetNextAnimatorStateInfo(attackLayerIndex).shortNameHash;
            bool isCurrentAttack = (curHash == attackStateAHash || curHash == attackStateBHash);
            bool isNextAttack = (nextHash == attackStateAHash || nextHash == attackStateBHash);
            if (isCurrentAttack && !isNextAttack && animator.GetFloat(attackSpeedHash) != 1f)
                animator.SetFloat(attackSpeedHash, 1f);
        }

        // 재생 중(Time.time < actionEndTime)에는 웨이트 1을 유지한다. 종료 후에만 레이어를 0으로 페이드아웃.
        if (Time.time < actionEndTime) return;

        float w = animator.GetLayerWeight(attackLayerIndex);
        if (w > 0f)
        {
            w = Mathf.MoveTowards(w, 0f, Time.deltaTime / Mathf.Max(layerFadeOutDuration, 0.0001f));
            animator.SetLayerWeight(attackLayerIndex, w);
        }
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
        animator.SetLayerWeight(attackLayerIndex, 1f);
        actionEndTime = Time.time + dur / Mathf.Max(speed, 0.01f);

        animator.CrossFadeInFixedTime(targetStateHash, crossFadeDuration, attackLayerIndex, startOffset);

        useSlotA = !useSlotA;
    }
}
