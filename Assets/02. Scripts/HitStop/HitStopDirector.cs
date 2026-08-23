using System.Collections.Generic;
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
/// <para><b>공백은 밀어서 처리한다.</b> 두 배우 모두 임팩트 시점에 흡수할 잔여가 없어(플레이어는 임팩트가
/// 트림 끝 근처, 적은 절단이 임팩트 바로 그 순간) 캐치업이 성립하지 않는다 — 임팩트 <i>이후</i>의 일정
/// (<c>actionEndTime</c>·<c>recoveryEndTime</c>·<c>burstTime</c>)을 정지 시간만큼 통째로 민다.
/// <b>임팩트 자체는 이미 지나간 뒤라 §6 정렬은 안 깨진다.</b></para>
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

    [Tooltip("정지 동안 파티클을 얼릴 대상. 비우면 이펙트만 계속 흐른다(나머지는 그대로).")]
    [SerializeField] private PatternEffectDirector patternEffectDirector;

    [Header("Tuning")]
    [Tooltip("전체 On/Off. 끄면 예약도 잡지 않는다.")]
    [SerializeField] private bool hitStopEnabled = true;

    private bool suppressNextMain;

    [Tooltip("멈추는 시간(초). 배우·카메라가 공유하는 하나의 값이다.\n" +
             "적의 절단(시체 교체·폭발)도 이만큼 뒤로 밀린다.")]
    [SerializeField] private float hitStopDuration = 0.1f;

    [Tooltip("직전 정지로부터 이 시간(초) 안에 오는 추가 예약은 버린다. 0이면 hitStopDuration을 쓴다.\n" +
             "⚠ 창을 '연장'하지 않는 이유 — 연장하면 연타가 여러 번 끊기는 느낌이 아니라\n" +
             "한 번 길게 멈춘 것이 되어 다중 히트스톱의 목적과 정반대가 된다.")]
    [SerializeField] private float minHitStopGap = 0f;

    [Tooltip("연타 타격 하나가 멈추는 시간(초). 0이면 연타에 정지를 안 건다.\n" +
             "⚠ 위 hitStopDuration(0.1초)을 그대로 쓰면 안 된다 — 연타 간격이 0.16초 수준이라\n" +
             "시간의 60% 이상을 얼어 있게 되고, 타격감이 아니라 '멈춘 캐릭터'가 된다.\n" +
             "실제 타격 간격의 30%를 넘지 않도록 런타임이 한 번 더 잘라 준다.")]
    [Min(0f)]
    [SerializeField] private float mashHitStopDuration = 0.03f;

    /// <summary>
    /// 연타 정지가 <b>실제 타격 간격에서 차지해도 되는 최대 비율</b>. 인스펙터에 안 여는 이유는
    /// 튜닝값이 아니라 규칙이기 때문이다 — "간격의 일부만 멈춘다"가 깨지는 순간 연타는 정지 그림이 된다.
    ///
    /// <para>플레이어가 얼마나 빨리 두들길지는 알 수 없으므로 <see cref="mashHitStopDuration"/>만으로는
    /// 부족하다. 빠르게 칠수록 정지가 저절로 짧아져야 비율이 유지된다.</para>
    /// </summary>
    private const float MashFreezeMaxRatio = 0.3f;

    /// <summary>직전 연타 타격 시각. 위 비율을 계산할 실제 간격을 여기서 얻는다.</summary>
    private float lastMashHitTime = float.NegativeInfinity;

    /// <summary>
    /// 예약 큐. <b>예전에는 최대 하나였다</b> — 패턴 완료가 순차적이고 A의 임팩트보다 B의 완료가
    /// 최소 0.4초 뒤라는 근거였다. 그 근거는 <b>지금도 참이지만 전제가 바뀌었다</b>:
    /// 한 패턴이 여러 번 베면 스톱도 여러 번 난다(마지막 베기 이전의 칼질마다 하나씩).
    ///
    /// <para>각 항목은 "그 시각에 절단을 밀어야 하는가"를 함께 든다 — <b>마지막 베기만 true</b>다.</para>
    /// </summary>
    private readonly List<Reservation> pending = new List<Reservation>();

    private struct Reservation
    {
        public float fireTime;
        public bool isMainImpact;   // 절단을 밀고 적까지 얼릴지
    }

    /// <summary>직전에 실제로 발사한 시각. 너무 촘촘한 예약을 버리는 데 쓴다.</summary>
    private float lastFireTime = float.NegativeInfinity;

    /// <summary>
    /// 한 번 멈추는 시간(초). <b>플레이어가 정지 예산을 잡는 데 이 값을 당겨 간다</b> —
    /// 인스펙터에 두 번 적으면 언젠가 하나만 고쳐지기 때문이다(§6의 <c>CurrentAttacker</c>를 pull로 당기는 것과 같은 근거).
    ///
    /// <para><b>꺼져 있으면 0을 돌려준다.</b> 그래야 "예산만큼 일찍 시작했는데 실제로는 안 멈추는" 어긋남이
    /// 원천적으로 안 생긴다.</para>
    /// </summary>
    public float HitStopDuration => hitStopEnabled ? hitStopDuration : 0f;

    void OnEnable()
    {
        if (handler == null)
        {
            Debug.LogError("[HitStopDirector] handler가 배선되지 않았습니다 — 히트스톱이 동작하지 않습니다.", this);
            return;
        }

        handler.OnPatternComplete += HandlePatternComplete;
        handler.OnAllPatternsCleared += HandleAllCleared;
        handler.OnMashHit += HandleMashHit;

        // 마지막 베기 '이전'의 칼질은 판정에서 파생되지 않는다 — 그 시각을 아는 것은
        // 자기 배속과 시작 시각을 든 배우뿐이다(§6). 여기서는 받아서 예약만 한다.
        if (actionPlayer != null) actionPlayer.OnExtraImpact += HandleExtraImpact;
    }

    void OnDisable()
    {
        if (handler == null) return;

        handler.OnPatternComplete -= HandlePatternComplete;
        handler.OnAllPatternsCleared -= HandleAllCleared;
        handler.OnMashHit -= HandleMashHit;
        if (actionPlayer != null) actionPlayer.OnExtraImpact -= HandleExtraImpact;
        pending.Clear();
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

        if (suppressNextMain)
        {
            suppressNextMain = false;
            return;
        }

        Schedule(info.ImpactTime(), isMainImpact: true);
    }

    /// <summary>
    /// 다음 <b>마지막 베기</b> 정지 하나를 건너뛴다. <see cref="FinaleSilhouetteDirector"/>가 부른다.
    ///
    /// <para><b>왜 필요한가:</b> 마무리 연출은 <c>Time.timeScale</c>을 0.1로 내리는데
    /// 해제 시각이 <c>Time.time + hitStopDuration</c>이고 <b><c>Time.time</c>은 스케일된 시계</b>다 —
    /// 0.1초 정지가 <b>실시간 1초</b>가 되어 노출 창 전체를 먹는다. 그러면 화면이 얼어붙어
    /// <b>슬로우모션이 아니라 정지 컷</b>이 된다. 슬로우가 타격감 강조를 대신하므로 여기서는 물러난다.</para>
    ///
    /// <para>⚠ 추가 스톱(<c>OnExtraImpact</c>)은 막지 않는다 — 그것들은 마지막 베기 <i>이전</i>이라
    /// 슬로우가 시작되기 전에 이미 끝나 있다.</para>
    /// </summary>
    public void SuppressNextMainImpact() => suppressNextMain = true;

    /// <summary>
    /// 마지막 베기 <b>이전</b>의 칼질. 순수 타격감이므로 <b>절단을 밀지 않고 적도 얼리지 않는다</b>(§Step 5).
    /// </summary>
    private void HandleExtraImpact(float fireTime) => Schedule(fireTime, isMainImpact: false);

    /// <summary>
    /// 연타 타격 하나를 <b>즉시</b> 멈춘다(맞는 순간이 곧 지금이라 예약할 것이 없다).
    ///
    /// <para><b>⚠ <see cref="Fire"/>와 모양이 다르다 — 배우만 멈추고 카메라·파티클은 안 건드린다.</b>
    /// 그쪽은 임팩트 한 번을 위한 것이라 넷을 다 얼려도 되지만, 연타는 초당 6타 이상이다.
    /// <c>HoldForHitStop</c>은 <c>CinemachineBrain</c>을 통째로 껐다 켜므로 초당 6번 토글하면
    /// 타격감이 아니라 <b>카메라 판정</b>이 되고, 파티클도 튀는 도중 반복해서 얼면 어색하다.
    /// <b>멈춰야 하는 것은 보고 있는 대상, 즉 배우뿐이다.</b></para>
    ///
    /// <para><b>⚠ 길이를 실제 간격에서 다시 자른다.</b> 인스펙터 값만 믿으면 플레이어가 빨리 칠수록
    /// 정지가 시간을 잡아먹는다 — 간격 0.16초에 0.10초를 멈추면 60% 이상 얼어 있게 되고,
    /// 그게 이 기능을 한 번 걷어냈던 이유다(<see cref="MashFreezeMaxRatio"/>).</para>
    ///
    /// <para><b>⚠ 적도 함께 멈춘다.</b> §7-3-1은 추가 스톱에서 적을 얼리지 않는데, 그 근거는
    /// 적 클립이 <c>impactAlignTime</c>에 정렬돼 있어 함께 얼리면 정렬만 밀린다는 것이었다.
    /// <b>연타의 적 리액션은 정렬을 들고 있지 않다</b>(맞는 순간에 즉시 재생될 뿐이라 밀릴 것이 없다) —
    /// 그래서 여기서는 얼려도 안전하고, 젖혀지는 순간이 같이 멈춰야 타격감이 산다.</para>
    /// </summary>
    private void HandleMashHit(MashHitInfo info)
    {
        if (!hitStopEnabled || mashHitStopDuration <= 0f) return;

        float gap = Time.time - lastMashHitTime;
        lastMashHitTime = Time.time;

        // 첫 타는 비교할 간격이 없다 — 저작값을 그대로 쓴다.
        float duration = float.IsInfinity(gap)
            ? mashHitStopDuration
            : Mathf.Min(mashHitStopDuration, gap * MashFreezeMaxRatio);

        if (duration <= 0.001f) return;   // 너무 촘촘하면 아예 안 멈춘다(연장하지 않는다)

        if (actionPlayer != null) actionPlayer.ApplyHitStop(duration);
        if (enemyDirector != null) enemyDirector.ApplyHitStop(duration, pushBurst: false);
    }

    private void Schedule(float fireTime, bool isMainImpact)
    {
        if (!hitStopEnabled) return;

        if (Time.time >= fireTime)
        {
            Fire(isMainImpact);
            return;
        }

        pending.Add(new Reservation { fireTime = fireTime, isMainImpact = isMainImpact });
    }

    private void HandleAllCleared()
    {
        pending.Clear();
        suppressNextMain = false;   // 억제는 '다음 하나'뿐이다 — 곡이 끊기면 소진되지 않은 채 남는다
        lastMashHitTime = float.NegativeInfinity;   // 다음 연타의 첫 타가 저작값을 온전히 쓰도록
    }

    void Update()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (Time.time < pending[i].fireTime) continue;

            bool main = pending[i].isMainImpact;
            pending.RemoveAt(i);
            Fire(main);
        }
    }

    /// <summary>
    /// 두 배우에게 각자 멈추라고 지시한다. <b>둘 다 '밀기' 모델이다</b> — 정지 창 안에 흡수할 잔여가
    /// 양쪽 다 없기 때문이다(플레이어는 임팩트가 트림 <i>끝</i> 근처라 잔여 0.036~0.109초,
    /// 적은 절단이 임팩트 <i>바로 그 순간</i>이라 잔여 0). 플레이어는 복귀 스케줄을,
    /// 적은 절단 시각(<c>burstTime</c>)을 정지 시간만큼 뒤로 민다.
    ///
    /// <para><b>한쪽만 멈춰도 타격감은 성립한다.</b> 사망 클립이 없는 패턴은 적이 빠지고 플레이어만 멈춘다.</para>
    /// </summary>
    private void Fire(bool isMainImpact)
    {
        // 너무 촘촘한 연타는 버린다(연장하지 않는다 — minHitStopGap 툴팁 참조).
        // ⚠ 마지막 베기는 버리지 않는다. 그것만이 절단·적 정지를 책임진다.
        float gap = minHitStopGap > 0f ? minHitStopGap : hitStopDuration;
        if (!isMainImpact && Time.time - lastFireTime < gap) return;

        lastFireTime = Time.time;

        if (actionPlayer != null)
            actionPlayer.ApplyHitStop(hitStopDuration);

        // ⚠ 추가 스톱은 적을 얼리지 않는다. 적의 공격·사망 클립도 같은 impactAlignTime에 정렬돼 있는데
        // (§6·§11-3) 적에게는 예산도 따라잡기도 없다 — 함께 얼리면 적 쪽 정렬만 정지 시간만큼 밀린다.
        // 마지막 베기에서는 이미 임팩트가 지나간 뒤라 예전처럼 전원 정지해도 안전하다.
        if (isMainImpact && enemyDirector != null)
            enemyDirector.ApplyHitStop(hitStopDuration, pushBurst: true);

        // 카메라도 같은 창만큼 얼린다 — 쉐이크를 끄고, 잠그고, 해제 뒤에 큐를 낸다.
        // 여기서 카메라를 직접 만지지는 않는다(Cinemachine 호출은 전부 CameraDirector 안에 남는다).
        if (cameraDirector != null)
            cameraDirector.HoldForHitStop(hitStopDuration);

        // 이펙트도 같은 창만큼. 캐릭터가 멈췄는데 스파크만 흐르면 "멈췄다"가 아니라
        // "캐릭터만 렉 걸렸다"로 읽힌다. 여기서도 파티클을 직접 만지지 않는다(호출은 이펙트 층에 남는다).
        if (patternEffectDirector != null)
            patternEffectDirector.ApplyHitStop(hitStopDuration);
    }
}
