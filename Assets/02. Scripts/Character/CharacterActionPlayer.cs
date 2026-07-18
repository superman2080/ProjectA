using PatternSpace;
using UnityEngine;

/// <summary>
/// 패턴 완료 시 캐릭터 액션을 재생한다. 완주 성공(AllCorrect)이면 패턴별 베기 클립을, 실패면 공용 피격(Hit) 클립을 재생한다.
/// AnimatorOverrideController로 단일 슬롯(Attack) 스테이트의 클립을 그때그때 덮어쓴 뒤 그 스테이트를 재생한다.
/// 재생할 클립이 없으면 무연출로 넘어간다 — 연출 부재가 게임 흐름을 막지 않는다.
///
/// Attack Layer는 휴지 상태 웨이트 0, 액션 재생 중에만 1, 종료 후 다시 0으로 페이드한다.
/// 액션은 항상 1배속으로 처음부터 재생한다(배속 처리 없음). 다음 패턴이 바로 이어지면 그 액션의 CrossFade가 현재 액션을 자연스럽게 끊는다(단일 스테이트라 겹치지 않음).
/// </summary>
public class CharacterActionPlayer : MonoBehaviour
{
    [SerializeField] private PatternHandler handler;
    [SerializeField] private Animator animator;

    [Header("Animator Slot")]
    [Tooltip("클립을 주입받는 슬롯 스테이트 이름(베기·피격 공용).")]
    [SerializeField] private string attackStateName = "Attack";
    [Tooltip("액션 모션이 재생되는 레이어 이름.")]
    [SerializeField] private string attackLayerName = "Attack Layer";
    [Tooltip("Attack 스테이트에 author 타임에 물려 있는 더미 placeholder 클립(오버라이드 키). AttackSlot_Placeholder.anim.")]
    [SerializeField] private AnimationClip placeholderClip;
    [Tooltip("Attack 스테이트의 Speed Multiplier 파라미터 이름.")]
    [SerializeField] private string attackSpeedParam = "AttackSpeed";

    [Header("Clips")]
    [Tooltip("패턴 실패 시 재생할 피격 리액션 클립들. 번갈아 재생된다.")]
    [SerializeField] private AnimationClip[] hitClips; // Hit1, Hit2

    [Header("Tuning")]
    [SerializeField] private float crossFadeDuration = 0.05f;
    [Tooltip("겹침 방지 속도 스케일의 상한.")]
    [SerializeField] private float maxAttackSpeed = 2.5f;
    [Tooltip("액션 종료 후 Attack Layer 웨이트를 0으로 내릴 때의 페이드 시간(초).")]
    [SerializeField] private float layerFadeOutDuration = 0.08f;

    private AnimatorOverrideController overrideController;
    private int attackLayerIndex = -1;
    private int attackStateHash;
    private int attackSpeedHash;
    private int hitIndex;        // Hit 클립 번갈아 재생용 커서
    private float actionEndTime; // 현재 액션의 재생 종료 예정 시각(이 시각 이후 레이어 웨이트를 0으로 페이드)

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

        attackStateHash = Animator.StringToHash(attackStateName);
        attackSpeedHash = Animator.StringToHash(attackSpeedParam);

        // 원본 컨트롤러를 감싼 오버라이드 인스턴스를 씌운다. 이후 이 인스턴스의 클립만 런타임에 교체한다.
        overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
        animator.runtimeAnimatorController = overrideController;

        if (placeholderClip == null)
            Debug.LogError("[CharacterActionPlayer] placeholderClip이 배선되지 않았습니다 — 오버라이드 키가 없어 클립 주입이 동작하지 않습니다.", this);

        // 휴지 상태 웨이트 0 보장.
        if (attackLayerIndex >= 0)
            animator.SetLayerWeight(attackLayerIndex, 0f);
    }

    void OnEnable()
    {
        if (handler != null)
            handler.OnPatternComplete += HandlePatternComplete;
    }

    void OnDisable()
    {
        if (handler != null)
            handler.OnPatternComplete -= HandlePatternComplete;
    }

    void Update()
    {
        if (attackLayerIndex < 0) return;

        // 배속 해제: Attack에서 '다른 스테이트(=복귀 Sprint_Forward)'로 빠져나가는 전이 중이면 AttackSpeed를 1로 되돌린다.
        // 이렇게 하면 배속된 공격에 이어지는 복귀 구간(전이 블렌드로 섞여 나오는 Attack 꼬리 + Sprint_Forward)이 정상 속도로 나온다.
        // 연속 공격(Attack→Attack 재진입)은 next가 Attack이므로 제외 → 다음 공격의 배속을 망치지 않는다.
        if (animator.IsInTransition(attackLayerIndex))
        {
            int curHash = animator.GetCurrentAnimatorStateInfo(attackLayerIndex).shortNameHash;
            int nextHash = animator.GetNextAnimatorStateInfo(attackLayerIndex).shortNameHash;
            if (curHash == attackStateHash && nextHash != attackStateHash && animator.GetFloat(attackSpeedHash) != 1f)
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

    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        AnimationClip clip = info.AllCorrect
            ? (info.Pattern != null ? info.Pattern.SuccessAnimationClip : null)
            : NextHitClip(); // 실패면 Hit 클립 번갈아

        if (clip == null) return; // 미지정 클립 — 무연출(경고 없이)

        PlayActionClip(clip, info.NextLastNodeTime);
    }

    /// <summary>피격 리액션 클립을 번갈아 반환한다. 배선이 없으면 null. (랜덤을 원하면 이 인덱스 선택만 교체.)</summary>
    private AnimationClip NextHitClip()
    {
        if (hitClips == null || hitClips.Length == 0) return null;

        AnimationClip clip = hitClips[hitIndex];
        hitIndex = (hitIndex + 1) % hitClips.Length;
        return clip;
    }

    /// <summary>
    /// 베기·피격 공통 재생. 슬롯 클립을 덮어쓰고 처음부터 재생하되, 다음 패턴 애니메이션 시작 시각까지의 창보다 길면
    /// 잘리지 않게 그 창에 맞춰 배속한다(창이 넉넉하면 1배속). 배속은 Attack 스테이트의 Speed Multiplier(AttackSpeed)로만 적용한다.
    /// </summary>
    private void PlayActionClip(AnimationClip clip, float nextLastNodeTime)
    {
        if (overrideController == null || placeholderClip == null || attackLayerIndex < 0) return;

        overrideController[placeholderClip] = clip;

        float speed = 1f;
        if (nextLastNodeTime >= 0f)
        {
            float budget = nextLastNodeTime - Time.time;
            if (budget > 0f && clip.length > budget)
                speed = Mathf.Min(clip.length / budget, maxAttackSpeed);
        }
        animator.SetFloat(attackSpeedHash, speed);

        animator.SetLayerWeight(attackLayerIndex, 1f);
        actionEndTime = Time.time + clip.length / speed;

        animator.CrossFadeInFixedTime(attackStateHash, crossFadeDuration, attackLayerIndex, 0f);
    }
}
