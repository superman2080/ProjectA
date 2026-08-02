using PatternSpace;
using UnityEngine;

/// <summary>
/// 히트스톱의 <b>유일한 관리 지점</b>. <see cref="PatternHandler"/>의 기존 확장 이벤트만 구독하는
/// 순수 연출이라 판정 파이프라인에는 개입하지 않는다 — <c>EffectManager</c>·<c>CameraDirector</c>와 같은 자리다.
///
/// <para><b>⚠ <c>Time.timeScale</c>은 이 게임에서 쓸 수 없다.</b> 판정·클립 정렬·표적 운동이 전부
/// <c>Time.time</c>인 반면 채보는 <c>audioSource.time</c>으로 돈다 — <b>오디오는 timeScale의 지배를 받지 않는다.</b>
/// 시계를 내리면 게임 시계만 느려져 그 차이가 되돌릴 수 없이 누적되고, <c>perfectWindow</c>가 0.05초인데
/// 통상 히트스톱이 0.05~0.10초라 <b>단 한 번으로 판정이 무너진다</b>.
/// 그래서 여기서 멈추는 것은 <b>Animator의 Speed Multiplier</b>(<c>AttackSpeed</c>/<c>DeathSpeed</c>)뿐이다.</para>
///
/// <para><b>공백은 캐치업 배속이 흡수한다.</b> 정지한 만큼 남은 클립을 빨리 돌려 각 배우가
/// <b>원래 예정된 절대 시각</b>(<c>actionEndTime</c>·<c>burstTime</c>)을 지킨다. 그래서 연계 판정·복귀 스케줄·
/// <c>OnEnemyBurst</c>·카메라 쉐이크 예약이 하나도 밀리지 않는다.</para>
///
/// <para><b>시각과 시간의 진실의 원천은 여기 하나다.</b> 임팩트 시각을 아는 곳은 넷이지만
/// (<c>CharacterActionPlayer</c>·<c>EnemyView</c>·<c>CameraDirector</c>·<c>SliceTargetDirector</c>)
/// "얼마나 멈출지"를 각자 들면 인스펙터에 같은 값이 여러 번 적히고 언젠가 하나만 고쳐진다.</para>
///
/// <para><b>카메라를 직접 만지지는 않는다.</b> 정지 창을 <c>CameraDirector.HoldForHitStop</c>에 <b>알려 줄</b> 뿐이고,
/// 실제로 무엇을 어떻게 얼릴지는 그쪽이 정한다 — Cinemachine 호출은 전부 <c>CameraDirector</c> 안에 남는다.
/// 정지 시간이 <b>한 값</b>이어야 배우와 카메라가 같은 창에서 멈추므로 시각·시간의 원천은 여기 하나로 유지된다.</para>
///
/// <para><b>연출 순서</b>: 임팩트 → (쉐이크 끄고 카메라 잠금 + 배우 정지) → 해제 → <b>그 다음에</b> 카메라 큐.
/// 멈추는 순간 화면이 흔들리면 "멈췄다"가 아니라 "끊겼다"로 읽힌다.</para>
///
/// <para><b>배우 참조가 비면 그 배우만 조용히 빠진다.</b> 기존 씬을 깨지 않게 하는 규율 그대로다.</para>
/// </summary>
public class HitStopDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("플레이어 공격 클립을 멈출 대상. 비우면 플레이어만 안 멈춘다.")]
    [SerializeField] private CharacterActionPlayer actionPlayer;

    [Tooltip("죽는 적의 사망 클립을 멈출 대상. 비우면 적만 안 멈춘다.")]
    [SerializeField] private EnemySpace.EnemyDirector enemyDirector;

    [Tooltip("정지 동안 카메라를 잠글 대상. 비우면 카메라는 안 멈추고 큐도 안 밀린다(나머지는 그대로).")]
    [SerializeField] private CameraDirector cameraDirector;

    [Header("Tuning")]
    [Tooltip("전체 On/Off. 끄면 예약도 잡지 않는다.")]
    [SerializeField] private bool hitStopEnabled = true;

    [Tooltip("멈추는 시간(초). 배우·카메라가 공유하는 하나의 값이다.\n" +
             "이 값이 클수록 적의 캐치업 배속이 올라가고, 여유가 없는 적은 아예 건너뛴다.")]
    [SerializeField] private float hitStopDuration = 0.1f;

    [Tooltip("[적 전용] 캐치업 배속의 상한. 걸리면 그 적의 절단 시각이 부족분만큼 뒤로 밀린다.\n" +
             "플레이어는 캐치업이 아니라 밀기라 이 값을 쓰지 않는다.")]
    [SerializeField] private float maxCatchupSpeed = 3f;

    [Tooltip("[적 전용] 임팩트 이후 잔여가 '정지시간 × 이 배수'보다 짧으면 그 적은 멈추지 않는다.\n" +
             "멈췄다가 폭발적인 배속으로 튀는 것보다 안 멈추는 게 낫다.")]
    [SerializeField] private float minCatchupHeadroom = 3f;

    // 대기 중인 예약(최대 하나). 근거는 CameraDirector와 같다 —
    // 패턴 완료는 순차적이고 A의 임팩트보다 B의 완료가 최소 0.4초 뒤라 동시에 둘이 뜨지 않는다.
    private bool hasPending;
    private float pendingFireTime;

    void OnEnable()
    {
        if (handler == null)
        {
            Debug.LogError("[HitStopDirector] handler가 배선되지 않았습니다 — 히트스톱이 동작하지 않습니다.", this);
            return;
        }

        handler.OnPatternComplete += HandlePatternComplete;
        handler.OnAllPatternsCleared += HandleAllCleared;
    }

    void OnDisable()
    {
        if (handler == null) return;

        handler.OnPatternComplete -= HandlePatternComplete;
        handler.OnAllPatternsCleared -= HandleAllCleared;
        hasPending = false;
    }

    /// <summary>
    /// <b>성공에서만</b> 예약한다. 실패는 표적이 부딪혀 소멸하는 것이라 멈출 임팩트 프레임이 없고,
    /// 피격은 플레이어가 맞는 순간이라 멈추면 타격감이 아니라 렉으로 읽힌다.
    ///
    /// <para>시각은 <c>Deadline + Pattern.ImpactOffset</c> — 칼날 임팩트 프레임·표적 절단·카메라 큐와
    /// <b>같은 식</b>이다(§6·§11). 성공은 이 시각보다 먼저 완료 이벤트가 오므로 예약이 정상 경로다.</para>
    /// </summary>
    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        if (!hitStopEnabled || !info.AllCorrect) return;

        float offset = info.Pattern != null ? info.Pattern.ImpactOffset : 0f;
        float fireTime = info.LastNodeTime + handler.GoodWindow + offset;

        if (Time.time >= fireTime)
        {
            Fire();
            return;
        }

        hasPending = true;
        pendingFireTime = fireTime;
    }

    private void HandleAllCleared() => hasPending = false;

    void Update()
    {
        if (!hasPending || Time.time < pendingFireTime) return;

        hasPending = false;
        Fire();
    }

    /// <summary>
    /// 두 배우에게 각자 멈추라고 지시한다. <b>⚠ 공백을 처리하는 모델이 서로 다르다</b> —
    /// 배우마다 임팩트 프레임이 클립 어디에 찍히느냐가 정반대이기 때문이다.
    ///
    /// <list type="bullet">
    /// <item><b>플레이어 = 밀기.</b> 공격 클립은 임팩트가 트림 <i>끝</i> 근처라 잔여가 실측 0.036~0.109초뿐(10/10 패턴).
    /// 정지 0.08초가 그보다 길어 캐치업이 원리적으로 불가능하다 → 복귀 스케줄을 통째로 민다.</item>
    /// <item><b>적 = 캐치업.</b> 사망 클립은 <c>ImpactTime</c>이 트림 <i>시작</i> 근처라(§11-3) 잔여가
    /// 0.245~0.953초로 넉넉하다 → 원래 <c>burstTime</c>을 그대로 지킨다.</item>
    /// </list>
    ///
    /// <para><b>한쪽만 멈춰도 타격감은 성립한다.</b> 사망 클립이 없는 패턴은 적이 빠지고 플레이어만 멈춘다.</para>
    /// </summary>
    private void Fire()
    {
        if (actionPlayer != null)
            actionPlayer.ApplyHitStop(hitStopDuration);

        if (enemyDirector != null)
            enemyDirector.ApplyHitStop(hitStopDuration, maxCatchupSpeed, minCatchupHeadroom);

        // 카메라도 같은 창만큼 얼린다 — 쉐이크를 끄고, 잠그고, 해제 뒤에 큐를 낸다.
        // 여기서 카메라를 직접 만지지는 않는다(Cinemachine 호출은 전부 CameraDirector 안에 남는다).
        if (cameraDirector != null)
            cameraDirector.HoldForHitStop(hitStopDuration);
    }
}
