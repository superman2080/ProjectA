using System;
using PatternSpace;
using UnityEngine;

/// <summary>
/// 캐릭터 액션을 <b>패턴 입력 도중</b> 재생한다.
/// - 성공(베기): 판정 대상이 시작되면, <b>클립의 임팩트 프레임(칼날이 표적을 지나가는 프레임)이 표적 절단 시각에 오도록</b>
///   `시작 = max(임팩트정렬시각 − 임팩트까지의 재생시간, 첫노드)` 지점에 예약해 조기 재생한다.
///   임팩트 프레임 이후의 잔여 구간은 <b>같은 배속으로 그대로 이어 재생</b>되어 마무리 동작이 뒤에 남는다.
///   재생시간은 슬롯의 트림(<c>ClipAlignment</c>의 StartOffset/Duration/ImpactTime)과 배속(Speed, 배속 하한)을 반영하고, 입력 구간이 짧으면 자동으로 더 배속한다(상한 maxAttackSpeed).
/// - 미스(첫 미스 1회): 예약/진행 중이던 성공 애니를 취소한다. 힛(Hit) 클립은 <b>적이 공격자(<c>Attacker.Enemy</c>)일 때만</b>,
///   그것도 첫 미스 순간이 아니라 <b><c>impactTime</c>에 예약해서</b> 재생한다(그때 적 칼이 닿으므로 — 첫 미스 순간엔 아직 오는 중이다).
///   플레이어가 공격자면 적은 애초에 휘두르지 않았으므로 <b>피격 자체가 없고 헛스윙으로 끝난다</b>(적은 제자리에서 패링한다).
///   재생 중이던 베기는 힛 크로스페이드로 즉시 끊긴다.
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
/// <b>base 로코모션의 소유자는 둘이고 시간으로 갈린다.</b> 도착 전(<c>convergeUntil</c>까지)은 수렴이 주인이고
/// <see cref="HandleDuelScheduled"/>가 이동 속도에 맞춰 Sprint/Quickshift를 건다. 도착 뒤는 복귀가 주인이고 언제나 <b>Idle</b>이다 —
/// 움직이지 않는데 달리는 클립을 걸면 제자리에서 달린다(<c>SwitchBaseStateUnlessConverging</c>이 수렴 중에는 물러나므로,
/// 복귀 경로가 실제로 실행되는 때는 이미 도착해 서 있는 순간뿐이다). 전환은 Attack 웨이트에 가려진 동안 일어나 눈에 띄지 않는다.
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

    [Tooltip("한 번 멈추는 시간을 물어볼 대상(다중 히트스톱의 정지 예산). 비우면 예산이 0 —\n" +
             "추가 스톱은 여전히 걸리지만 마지막 베기 정렬은 따라잡기가 맡는다.")]
    [SerializeField] private HitStopDirector hitStopDirector;

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
    [Tooltip("Quickshift 스테이트에 물려 있는 클립. 길이를 읽어 배속을 역산하고, 백스텝 교체의 키로도 쓴다.\n" +
             "⚠ 애니메이터의 Quickshift 스테이트에 실제로 물린 그 클립이어야 한다(키가 안 맞으면 교체가 조용히 무시된다).")]
    [SerializeField] private AnimationClip quickshiftClip;

    [Tooltip("뒤로 빠질 때 쓸 대시 클립(Quickshift_B). 비우면 전진 클립 그대로 — 예전 동작.\n" +
             "새 스테이트를 만들지 않고 AnimatorOverrideController로 quickshiftClip 자리를 갈아 끼운다.")]
    [SerializeField] private AnimationClip quickshiftBackClip;
    [Tooltip("Sprint 스테이트의 Speed Multiplier 파라미터 이름.")]
    [SerializeField] private string sprintSpeedParam = "SprintSpeed";
    [Tooltip("Sprint 애니메이션이 1배속으로 보일 이동 속도(m/s). 배속 = 실제 이동속도 ÷ 이 값.\n" +
             "다리 회전이 실제 이동보다 빠르면(발이 미끄러지면) 이 값을 올리고, 느리면 내린다.\n" +
             "상한이 아니다 — 이 값을 넘는 속도로 이동하면 배속도 1을 넘는다.\n" +
             "예: 이동 4.5m/s일 때 이 값이 4.5면 1배속, 2면 2.25배속(다리가 두 배 넘게 빨라짐).")]
    [SerializeField] private float sprintReferenceSpeed = 4.5f;
    [Tooltip("Sprint 배속의 하한/상한(x = 배속). 계산된 배속을 이 범위로 자른다.\n" +
             "⚠ 잘리면 다리 회전과 실제 이동이 어긋난다(발 미끄러짐) — 그림이 무너지는 극단값만 막는 안전장치다.\n" +
             "끄려면 (0.01, 99) 같은 넓은 범위를 넣는다.")]
    [SerializeField] private Vector2 sprintSpeedRange = new Vector2(0.6f, 2f);
    [Tooltip("Sprint를 쓰려면 실제 이동 속도가 이 값(m/s) 이상이어야 한다. 미만이면 Quickshift로 간다 — " +
             "느린 Sprint는 배속이 그만큼 떨어져 제자리에서 다리만 젓는 그림이 된다(실패 후 재접근이 0.9m/s).")]
    [SerializeField] private float minSprintTravelSpeed = 2f;
    [Tooltip("Quickshift 배속의 하한. 창이 클립보다 길어 늘여 쓸 때 무한정 느려지지 않게 막는다. " +
             "1로 두면 늘이지 않는다(클립이 먼저 끝나고 남은 구간은 미끄러진다).")]
    [SerializeField] private float minQuickshiftSpeed = 0.6f;
    [Tooltip("이 거리(m) 미만이면 로코모션을 켜지 않는다 — 제자리에서 발을 구르지 않게.")]
    [SerializeField] private float convergeMinDistance = 0.15f;
    [Tooltip("수렴 로코모션의 판단 근거를 콘솔에 찍는다(에디터 전용). 모션이 안 나올 때 원인을 가른다.")]
    [SerializeField] private bool logConvergeDecision = true;

    [Header("Clips")]
    [Tooltip("적이 공격자인 패턴을 실패했을 때 재생할 피격 리액션 클립들. 번갈아 재생된다.\n" +
             "플레이어가 공격자인 패턴의 실패는 헛스윙이라 이 클립이 쓰이지 않는다.")]
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

    [Header("Mash (연타)")]
    [Tooltip("연타 타격의 크로스페이드(초). ⚠ 여기가 '역동적인가 흐느적거리는가'를 가르는 값이다.\n" +
             "일반 공격값(0.15초)을 그대로 쓰면 연타 간격(0.16초 수준)과 거의 같아 포즈가 도착하기 전에\n" +
             "다음 타격이 들어온다 — 캐릭터가 영원히 '전환 중'이라 임팩트 포즈가 한 번도 안 보인다.\n" +
             "0에 가까울수록 스냅(2~3프레임에 포즈가 꽂힌다).")]
    [SerializeField] private float mashCrossFadeDuration = 0.04f;

    [Tooltip("연타 타격을 임팩트 프레임보다 이만큼(초) 앞에서 시작한다.\n" +
             "0이면 임팩트 포즈로 바로 스냅한다(가장 날카롭다).\n" +
             "0.05~0.10이면 칼이 임팩트를 '지나가는' 짧은 궤적이 보인다 — 스윙으로 읽힌다.\n" +
             "⚠ 키울수록 임팩트가 입력보다 늦게 도착해 반응이 무뎌진다.")]
    [SerializeField] private float mashPreRoll = 0.05f;

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
    /// 확장 포인트: <b>마지막 베기 이전의 칼질이 도달하는 절대 시각</b>. 다중 히트스톱이 이것만 구독한다.
    ///
    /// <para><b>이 시각을 아는 것은 여기뿐이다</b> — 클립 초로 저작된 마크를 월드 시각으로 바꾸려면
    /// 그때의 실제 배속과 재생 시작 시각이 필요하고, 그 둘은 재생하는 배우만 든다(§6).
    /// 반면 "얼마나 멈출까 · 누구를 멈출까"는 여전히 <c>HitStopDirector</c> 하나가 정한다.</para>
    ///
    /// <para>⚠ <b>한 번에 하나씩</b> 발행한다. 정지가 끼면 이후 칼질이 그만큼 밀리므로,
    /// 미리 다 발행하면 두 번째부터 이르게 터진다.</para>
    /// </summary>
    public event Action<float> OnExtraImpact;

    /// <summary>
    /// 확장 포인트: <b>적 칼이 실제로 플레이어에게 닿는 순간</b>(= <c>impactTime</c>). 첫 미스 순간이 아니다.
    /// 체력 감소·카메라 피격 큐가 이 이벤트만 구독한다.
    /// </summary>
    public event Action OnPlayerHit;

    /// <summary>
    /// 확장 포인트: <b>플레이어 애니메이션이 비어 있는 구간</b>(start, end). 도착도 끝나고 마무리 노출도 끝났는데
    /// 다음 액션 클립은 아직 시작 전인, <b>가만히 서 있기만 하는 시간</b>이다.
    ///
    /// <para><b>이 구간을 아는 클래스는 여기뿐이다</b> — 네 값(<see cref="recoveryEndTime"/>·<see cref="convergeUntil"/>·
    /// <see cref="pendingScheduleStart"/>·<c>FirstNodeTime</c>)을 동시에 드는 곳이 여기밖에 없기 때문.
    /// 그래서 이 구간에 무언가를 끼워 넣는 연출(기습 회피 등)은 폴링하지 않고 이 이벤트만 구독한다.</para>
    ///
    /// <para>§11-6 무리 배치가 이 구간을 키웠다 — 무리 안에서는 이동이 0에 가까워
    /// 예전에 대시가 채우던 시간이 통째로 "서 있는 시간"이 됐다.</para>
    /// </summary>
    public event Action<float, float> OnIdleWindow;

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
    private float releaseEndTime;  // Release 재생이 끝나는 시각(= 트리거 시각 + releaseDuration)
    private int hitIndex;         // Hit 클립 번갈아 재생용 커서
    private AnimationClip appliedQuickshiftClip; // Quickshift 자리에 지금 물려 있는 클립(전진/백스텝)
    private bool useSlotA = true; // 듀얼 슬롯 전환 플래그

    // 현재 액션의 복귀 스케줄.
    private float actionEndTime;   // 트림 구간이 끝나는 시각(= 복귀 판단 시작점)
    private float recoveryEndTime; // 연계가 없을 때 마무리 동작 노출이 끝나는 시각
    private bool speedRestored;    // actionEndTime에서 AttackSpeed를 1로 되돌렸는지

    // 히트스톱이 얼렸다 되돌릴 배속.
    private float playSpeed = 1f;

    // 재생 진행 스냅샷. 임팩트 '이전'에 멈추는 다중 히트스톱이 이 둘을 요구한다 —
    // 얼마나 남았는지 알아야 해제 시 배속을 다시 계산하고(따라잡기), 다음 칼질 시각도 구한다.
    // ⚠ 이력: 2026-08-10 리팩토링이 소비자가 없다고 판단해 비슷한 필드(playStartTime/playDur)를 지웠다.
    // 이 기능이 그 소비자다(docs/MultiHitStop). 지우기 전에 여기 주석부터 확인할 것.
    private float segmentStartTime;  // 지금 배속 구간이 시작된 실시간
    private float clipConsumed;      // 이번 '원소'에서 소비한 클립 초(정지 구간 제외). 추가 히트스톱 마크의 단위다.
    private float playingBaseSpeed = 1f; // 재생 중인 원소의 저작 배속. 클립 초 → 저작 초 환산 단위.
    private float playingImpactAlignTime = float.NaN; // 재생 중인 정렬 대상의 임팩트 절대시각(= 그 클립이 속한 패턴의 신분증)

    /// <summary>
    /// 시퀀스 시작부터 지금까지 흐른 <b>저작 초</b>. 클립 초(<see cref="clipConsumed"/>)와 달리
    /// <b>원소를 가로질러 누적된다</b> — 여러 클립이 이어져도 헤드는 하나여야 하기 때문이다.
    ///
    /// <para>단위가 둘인 것은 소비자가 둘이기 때문이다: 추가 히트스톱 마크는 <b>그 원소의 클립 초</b>로 저작되고,
    /// 거리 커브(§11-9)와 임팩트 판정은 <b>시퀀스 전체의 저작 초</b>를 본다. 합칠 수 없어서 나눈 것이지
    /// 시계를 늘린 것이 아니다.</para>
    /// </summary>
    private float headAuthored;

    /// <summary>
    /// 시퀀스 시작부터 <b>마지막 임팩트</b>까지의 저작 초. 0이면 정렬 대상이 아니다(피격·일회성).
    /// 리드인이 없으면 <c>ResolvedImpactSpan / Speed</c>와 같아 <b>예전 단일 클립 경로와 대수적으로 동일</b>하다.
    /// </summary>
    private float impactAuthored;

    // ─── 클립 시퀀스(리드인) ───
    // 마지막(정렬 대상) 클립 앞에 순서대로 재생되는 원소들. 리스트가 비면 아래 셋은 전부 휴지 상태이고
    // 재생 경로가 예전과 완전히 같다.
    private ClipAlignment[] leadInQueue = System.Array.Empty<ClipAlignment>();
    private int leadInCursor;
    private bool inLeadIn;              // 아직 마지막 클립에 도달하지 않았다 — 복귀 로직을 통째로 막는 게이트
    private float elementEndTime;       // 지금 원소의 트림이 끝나는 실시간
    private float playingElementDuration; // 지금 원소의 트림 길이(클립 초). elementEndTime 재계산의 기준
    private float sequenceRatio = 1f;   // 시퀀스 전체에 걸리는 균일 배속 비율. 원소 i의 애니메이터 배속 = speed_i × 이 값

    /// <summary>
    /// 지금 재생 중인 <b>정렬 대상 액션이 어느 패턴의 것인지</b>를 말하는 값 — 그 패턴의 임팩트 절대시각이다.
    /// 재생 중이 아니면 <c>NaN</c>.
    ///
    /// <para><b>왜 필요한가</b>: <see cref="DuelCurveTime"/>은 "지금 헤드가 어디냐"만 말할 뿐
    /// <b>누구의 헤드인지</b>는 말하지 않는다. 다음 패턴의 결투 계획이 도착하면 그 패턴의 거리 커브가 걸리는데
    /// 클립은 아직 시작 전이라, 소비자가 그대로 쓰면 <b>직전 클립의(이미 마지막 키를 지난) 시각</b>으로
    /// 새 커브를 읽어 끝값(예: −5m)으로 튄다 — 화면에서는 <b>적을 관통해 뒤로 순간이동</b>한다.
    /// 계획의 임팩트 시각과 이 값을 대조하면 그 구간이 사라진다.</para>
    /// </summary>
    public float PlayingImpactAlignTime => playingImpactAlignTime;

    /// <summary>
    /// 지금 재생 중인 액션의 <b>임팩트 기준 시각</b>(초). 저작 배속 단위라 <c>Animation Clip Trimmer</c>의
    /// <c>t</c>와 같은 값이며, 결투 거리 커브가 이것을 시간 원점으로 쓴다.
    ///
    /// <para><b>월드 시각(<c>Time.time − impactTime</c>)을 쓰면 안 되는 이유가 셋이다</b> —
    /// ① 히트스톱은 <c>AttackSpeed = 0</c>으로만 걸리고 <c>Time.time</c>은 계속 흐른다(§7-3):
    /// 월드 시각으로 굴리면 캐릭터는 얼었는데 몸만 미끄러진다. ② <c>AttackSpeed</c> 압축(최대
    /// <see cref="maxAttackSpeed"/>)이 걸리면 칼 리치와 간격의 대응이 깨진다. ③ 다중 히트스톱 예산은
    /// 재생을 F만큼 일찍 시작한다(§7-3-1). <b>재생 헤드에서 파생하면 셋 다 공짜로 성립한다.</b></para>
    ///
    /// <para>재생 중인 정렬 대상 액션이 없으면 <c>NaN</c>이다(피격·일회성 클립 포함) —
    /// 소비자는 그 구간에 아무것도 하지 않아 마지막 값이 그대로 유지된다.</para>
    ///
    /// <para>⚠ <c>Animator</c> 스테이트 시간을 읽지 않는다 — 크로스페이드 중에 값이 튄다.</para>
    /// </summary>
    public float DuelCurveTime
    {
        get
        {
            if (impactAuthored <= 0f) return float.NaN;

            // 정지 중에는 헤드가 얼어 있다 — 흐른 실시간을 저작 초로 환산하면 안 된다.
            return HeadAuthoredNow() - impactAuthored;
        }
    }

    /// <summary>
    /// 지금 이 순간의 헤드(저작 초). 확정분(<see cref="headAuthored"/>) + 지금 구간의 미확정분이다.
    ///
    /// <para>환산율이 <c>playSpeed / playingBaseSpeed</c>인 것이 전부다 — 시퀀스 재생 중에는 이 값이
    /// <see cref="sequenceRatio"/>와 같고, 트림 끝에서 배속이 1로 복원된 뒤에는 <c>1 / 저작배속</c>이 된다.
    /// <b>비율을 직접 곱하지 않는 이유가 그 두 번째 구간이다</b>(임팩트 이후 거리 커브가 여기서 갈린다).</para>
    /// </summary>
    private float HeadAuthoredNow()
    {
        if (hitStopped) return headAuthored;

        return headAuthored + (Time.time - segmentStartTime) * playSpeed / Mathf.Max(playingBaseSpeed, 0.01f);
    }

    /// <summary>
    /// 지금 구간의 진행분을 확정하고 새 구간을 연다. <b>클립 초와 저작 초를 반드시 함께</b> 밀어야
    /// 두 시계가 갈라지지 않는다 — 배속이 바뀌는 모든 지점(트림 끝 · 정지 · 원소 전환)이 여기를 부른다.
    /// </summary>
    private void CommitHeadProgress()
    {
        float elapsed = Time.time - segmentStartTime;

        clipConsumed += elapsed * playSpeed;
        headAuthored += elapsed * playSpeed / Mathf.Max(playingBaseSpeed, 0.01f);
        segmentStartTime = Time.time;
    }

    // 마지막 베기 이전의 칼질들(트림 시작 기준 상대 초, 오름차순)과 커서.
    // ⚠ 한 번에 하나씩만 발행한다 — 전부 미리 발행하면 첫 정지 시간만큼 나머지가 이르게 터진다.
    private float[] playingExtraSpans;
    private int extraCursor;

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
    private float pendingImpactSpan; // 트림 시작 → 임팩트 프레임까지의 길이(클립 초).
    private float pendingBaseSpeed;
    private ClipAlignment[] pendingLeadIn = System.Array.Empty<ClipAlignment>(); // 마지막 클립 앞에 붙는 원소들(쓸 수 있는 것만)
    private float pendingAuthored;   // 시퀀스 시작 → 마지막 임팩트까지의 저작 초. 시작 시각·배속 역산의 기준.
    private float pendingScheduleStart;
    private float pendingImpactAlignTime; // 임팩트 프레임이 도달해야 할 절대시각(= 표적 절단 시각).
    private float[] pendingExtraSpans;    // 마지막 베기 이전의 칼질들(트림 시작 기준 상대 초).
    private float pendingFreeze;          // 재생 중 소비될 총 정지 시간(예산). 시작 시각·배속이 이만큼을 미리 뺀다.
    private bool missedThisTarget; // 이번 판정 대상에서 이미 첫 미스 처리를 했는지

    // 이번 판정 대상에서 누가 휘두르는가. 클립 선택과 "맞는지 여부"를 동시에 가른다.
    private EnemySpace.Attacker currentAttacker = EnemySpace.Attacker.Player;

    // 이번 판정 대상이 상호 공격인가(Attacker.Player인데 적도 같이 휘두른다 — Pattern.CountersOnFail).
    // 역할과 수명이 같아야 어긋나지 않으므로 같은 자리에서 캐시한다.
    private bool currentCountersOnFail;

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
            handler.OnMashHit += HandleMashHit;
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
            handler.OnMashHit -= HandleMashHit;
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
            ReleaseHitStop();
        }

        TickLeadIn();
        TryStartPendingSuccess();
        TryStartPendingHit();

        // ⚠ 리드인 원소를 재생 중이면 아래를 하나도 통과시키지 않는다. 중간 원소의 트림 끝에서
        // 배속 복원·스윙 종료·복귀가 열리면 <b>시퀀스가 1타 만에 끝난다</b>.
        // 마지막 클립이 시작되는 순간 이 게이트가 내려가고 예전 경로가 그대로 주인이 된다.
        if (inLeadIn)
        {
            ApplyBlendIn();
            return;
        }

        // 트림 구간이 끝나면 배속을 해제해 마무리 동작이 정상 속도로 재생되게 한다.
        // 이 래치는 트림 끝을 정확히 1회만 통과하므로 스윙 종료 발행 지점으로 그대로 재사용한다.
        if (!speedRestored && Time.time >= actionEndTime)
        {
            // ⚠ 재생 진행 스냅샷도 여기서 끊어 준다. 배속이 1로 바뀌는데 playSpeed가 옛 값으로 남으면
            // 그 뒤 DuelCurveTime이 실제보다 빠르게 흐른다(거리 커브의 임팩트 이후 구간이 어긋난다).
            CommitHeadProgress();
            playSpeed = 1f;

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
                // ⚠ 여기서 Sprint를 걸면 안 된다. SwitchBaseStateUnlessConverging은 수렴 중에는 물러나므로
                // 이 호출이 실제로 실행되는 때는 <b>이미 도착해 서 있는 순간</b>뿐이다 — 그때 Sprint를 걸면
                // 제자리에서 달린다. 콤보 사이의 달리기는 HandleDuelScheduled가 이동 속도에 맞춰 이미 걸어 두었고
                // (Sprint/Quickshift), 그 소유권은 convergeUntil까지다. 도착 뒤 남는 구간은 서 있는 구간이다.
                SwitchBaseStateUnlessConverging(idleStateHash);
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
        SwitchBaseStateUnlessConverging(idleStateHash);
        ApplyBlendOut();
    }

    /// <summary>
    /// 결투 수렴 구간의 로코모션. <b>창으로 갈린다</b> — 거리가 아니다.
    ///
    /// <para>기준은 <b>둘</b>이다 — "클립 하나가 창을 채우는가"와 <b>"그 속도가 달리기로 읽히는가"</b>.
    /// Quickshift(대시)는 <b>루프가 아니라 1초짜리 단발</b>이라 창이 그보다 길면 클립이 먼저 끝나고
    /// 남은 시간은 그냥 미끄러진다 — 예전엔 거리로 갈라서 평균 창(1.48초) 대부분이 이 구멍에 빠졌다.
    /// Sprint는 루프라 길이에 상관없이 채운다.</para>
    ///
    /// <list type="bullet">
    /// <item>창 &gt; 클립 길이 <b>그리고</b> 이동 속도 ≥ <see cref="minSprintTravelSpeed"/> → <b>Sprint</b>. 이동 속도에 맞춰 배속(다리와 몸이 따로 놀지 않게)</item>
    /// <item>그 외 → <b>Quickshift</b>. 창을 정확히 채우도록 배속을 역산</item>
    /// </list>
    ///
    /// <para><b>⚠ 속도 조건이 없으면 느린 이동이 통째로 Sprint로 빠진다.</b> Sprint 배속은 이동 속도에 비례하므로
    /// 느릴수록 클립도 같이 느려진다 — 실패 후 재접근(1.3m를 창 1.4초에)이 <b>0.9 m/s = 0.2배속</b>이라
    /// 제자리에서 다리만 젓는 그림이 됐다. 그 구간은 <b>거리가 창에 맞춰지지 않는 유일한 경로</b>라
    /// (재접근은 <c>TakeTargetForWindow</c>를 안 거친다) 구조적으로 느리고, 그래서 조건이 필요하다.
    /// 같은 속도라도 <b>단발 대시는 성립한다</b> — 대시는 "짧게 확 붙는 동작"이라 늘여도 대시로 읽힌다.</para>
    ///
    /// <para><b>배속에 상한을 두지 않는다.</b> 자르면 클립이 창 안에 완주하지 못해 몸은 도착했는데
    /// 다리는 대시 도중에 끊긴다. 상한이 필요 없는 이유는 <b>배속이 올라가는 만큼 화면에 남는 시간도 같이 줄기</b>
    /// 때문이다: 8배속 Quickshift는 0.12초짜리라 "튀는 클립"이 아니라 순식간에 붙는 그림으로 읽힌다.
    /// 반대쪽 하한만 <see cref="minQuickshiftSpeed"/>로 막는다(무한정 슬로모션이 되지 않게).</para>
    ///
    /// <para>거의 안 움직이는 경우(<see cref="convergeMinDistance"/> 미만)는 아무것도 하지 않는다 —
    /// 제자리에서 발을 구르면 더 부자연스럽다.</para>
    /// </summary>
    private void HandleDuelScheduled(EnemySpace.EnemyDirector.DuelPlan plan)
    {
        Vector3 move = Vector3.ProjectOnPlane(plan.PlayerPosition - transform.position, Vector3.up);
        float distance = move.magnitude;

        // 뒤로 빠지는가. 도착 뒤 바라볼 방향(적 쪽)과 이동 방향이 반대면 뒷걸음질이다 —
        // 회전은 PlayerCombatMover가 이미 적 쪽으로 걸어 두므로 이 식이 화면의 사실과 같다.
        Vector3 facing = Vector3.ProjectOnPlane(plan.EnemyPosition - plan.PlayerPosition, Vector3.up);
        bool backward = Vector3.Dot(move, facing) < 0f;

        // ⚠ ArriveTime이 아니라 PlayerArriveTime이다 — 재접근에서는 플레이어가 그보다 일찍 도착한다.
        // 실제 이동이 끝나는 시각으로 봐야 배속이 맞는다(창을 길게 잡으면 다 온 뒤에도 다리가 돈다).
        float window = plan.PlayerArriveTime - Time.time;

        if (runningLayerIndex < 0) { LogConverge("레이어 없음", distance, window); return; }
        if (distance < convergeMinDistance) { LogConverge("거리 부족", distance, window); return; }
        if (window <= 0f) { LogConverge("시간 없음", distance, window); return; }

        // 도착할 때까지 base의 주인은 수렴이다. 복귀 로직이 매 프레임 되찾아가지 못하게 막는다.
        // 도착 뒤 남는 시간은 복귀 로직에 돌려준다(서 있는 구간이 로코모션에 묶이지 않게).
        convergeUntil = plan.PlayerArriveTime;

        // 뒤로 갈 때는 백스텝 클립의 길이로 배속을 역산한다(길이가 다르면 창을 못 채운다).
        AnimationClip dashClip = backward && quickshiftBackClip != null ? quickshiftBackClip : quickshiftClip;
        float dashLength = dashClip != null ? dashClip.length : 1f;
        float travelSpeed = distance / window;

        // ⚠ 뒤로 갈 때 Sprint 분기를 타면 전진 달리기 루프로 뒤로 미끄러진다. 백스텝은 Quickshift 경로로만.
        if (window > dashLength && travelSpeed >= minSprintTravelSpeed && !backward)
        {
            // 실제 이동 속도에 배속을 맞춘다 — 안 맞추면 발이 지면을 긁는다.
            float runSpeed = Mathf.Max(0.1f, travelSpeed) / Mathf.Max(sprintReferenceSpeed, 0.01f);
            runSpeed = Mathf.Clamp(runSpeed, sprintSpeedRange.x, sprintSpeedRange.y);
            animator.SetFloat(sprintSpeedHash, runSpeed);

            // 배속이 바뀌었으므로 같은 스테이트라도 처음부터 다시 건다(SwitchBaseState는 같으면 조기 반환).
            animator.CrossFadeInFixedTime(sprintStateHash, baseCrossFadeDuration, runningLayerIndex, 0f);
            currentBaseStateHash = sprintStateHash;
            LogConverge($"Sprint x{runSpeed:0.00}", distance, window);
            return;
        }

        // 클립 길이 / 남은 시간 = 창을 정확히 채우는 배속. 상한은 없고(문서 참조), 하한만 둔다.
        // 창이 클립보다 길어도(느린 이동 경로) 늘여서 채운다 — 0.7배 대시는 여전히 대시로 읽히지만
        // 0.2배 Sprint는 기어가는 그림이 된다. minQuickshiftSpeed 아래로는 늘이지 않는다.
        float speed = Mathf.Max(minQuickshiftSpeed, dashLength / window);

        // 새 스테이트를 만들지 않는다 — 같은 Quickshift 스테이트의 클립만 갈아 끼운다.
        // ⚠ 양쪽 분기에서 매번 대입한다. 한쪽만 쓰면 직전 방향의 클립이 그대로 남는다.
        ApplyQuickshiftClip(dashClip);

        animator.SetFloat(quickshiftSpeedHash, speed);
        animator.CrossFadeInFixedTime(quickshiftStateHash, baseCrossFadeDuration, runningLayerIndex, 0f);
        currentBaseStateHash = quickshiftStateHash;
        LogConverge($"{(backward ? "Quickshift(뒤)" : "Quickshift")} x{speed:0.00}", distance, window);
    }

    /// <summary>
    /// Quickshift 스테이트가 재생할 클립을 갈아 끼운다(전진 ↔ 백스텝).
    /// <b>키는 언제나 원본 <see cref="quickshiftClip"/></b>이다 — 오버라이드는 원본 클립을 키로 잡으므로
    /// 이전에 무엇으로 바꿔 놨든 이 키로 되돌릴 수 있다.
    /// </summary>
    private void ApplyQuickshiftClip(AnimationClip clip)
    {
        if (overrideController == null || quickshiftClip == null || clip == null) return;
        if (appliedQuickshiftClip == clip) return; // 같은 클립을 다시 대입하면 재생이 리셋된다

        overrideController[quickshiftClip] = clip;
        appliedQuickshiftClip = clip;
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
        SchedulePendingSuccess(info);

        // ⚠ 예약이 끝난 <b>뒤</b>에 낸다 — 공백의 끝이 pendingScheduleStart이므로 그 값이 확정돼야 한다.
        RaiseIdleWindow(info);
    }

    /// <summary>
    /// 다음 액션 클립이 시작되기 전까지의 <b>빈 구간</b>을 알린다.
    ///
    /// <para><b>⚠ <c>OnDuelScheduled</c>보다 뒤라는 순서에 의존한다</b> — <see cref="convergeUntil"/>이 그 핸들러에서
    /// 갱신되기 때문이다. 이미 보장돼 있다: <c>OnPatternComplete</c>(디렉터가 <c>BindNextReservation</c> →
    /// <c>OnDuelScheduled</c>) → <c>RaiseJudgeTargetBegan</c>.</para>
    ///
    /// <para>슬롯이 비어 무연출인 패턴에서는 <see cref="pendingScheduleStart"/>가 <b>낡은 값</b>이라 쓰면 안 된다.
    /// 그때는 이 패턴이 끝나는 시각(<c>Deadline</c>)까지가 통째로 빈 구간이다.</para>
    ///
    /// <para><b>⚠ 첫 노드로 자르지 않는다.</b> 초안은 <c>min(pendingScheduleStart, FirstNodeTime)</c>이었는데,
    /// 실측(<c>Dreamer_Lv10</c> 89개 연결)에서 <b>83개가 "앞 패턴 마지막 노드 → 다음 첫 노드 = 정확히 0.40초"</b>였다.
    /// 거기서 <see cref="recoveryHoldDuration"/>을 빼면 남는 창이 0.15초로 <b>상수처럼 굳어</b>
    /// 이 구간을 쓰는 연출이 원리적으로 성립할 수 없었다.
    ///
    /// <para>시간의 공급처는 전부 <b>노드를 입력하는 구간 안</b>에 있다 — 공격 클립은 첫 노드가 아니라
    /// <c>임팩트 − 와인드업</c>(템플릿 실측 p50 0.26초)에 시작하므로, 그 사이 캐릭터는 서 있기만 한다.
    /// 그래서 창의 끝은 <b>클립 시작</b> 하나로 잡는다(p50 0.54초, ≥0.8초가 45%).
    /// 이 구간에 붙는 연출은 <b>패턴 입력과 동시에 일어난다</b>는 뜻이고, 그건 의도된 요구다.</para>
    /// </summary>
    private void RaiseIdleWindow(JudgeTargetInfo info)
    {
        if (OnIdleWindow == null) return;

        float start = Mathf.Max(Mathf.Max(recoveryEndTime, convergeUntil), Time.time);
        float end = hasPending ? pendingScheduleStart : info.Deadline;

        if (end - start <= 0f) return;

        OnIdleWindow.Invoke(start, end);
    }

    private void SchedulePendingSuccess(JudgeTargetInfo info)
    {
        missedThisTarget = false;
        hasPending = false;
        hasPendingHit = false;
        currentCountersOnFail = false;

        if (info.Template == null) return;

        currentAttacker = enemyDirector != null ? enemyDirector.CurrentAttacker : EnemySpace.Attacker.Player;
        currentCountersOnFail = info.Template.CountersOnFail;

        // 칼이 닿는 시각 — 적 공격·표적 절단과 반드시 같은 식이어야 한다.
        float impactAlignTime = info.ImpactTime();
        pendingImpactAlignTime = impactAlignTime;

        var alignment = currentAttacker == EnemySpace.Attacker.Enemy
            ? info.Template.PlayerParry
            : info.Template.PlayerAttack;

        // 슬롯이 비어 있으면 무연출이다. 예전에는 구 SuccessAnimationClip + 트림 4필드로 떨어지는 폴백이 있었지만,
        // 템플릿이 전부 ClipAlignment로 이관돼 발동할 수 없는 분기가 됐다(같은 클립이 두 군데 적혀 갈라지기만 했다).
        if (alignment == null || !alignment.IsUsable) return;

        pendingClip = alignment.Clip;
        pendingStartOffset = alignment.StartOffset;
        pendingDur = alignment.ResolvedDuration;
        pendingImpactSpan = alignment.ResolvedImpactSpan;
        pendingBaseSpeed = alignment.Speed;

        // 마지막 클립 '앞에' 붙는 원소들. 비면 아래 계산이 전부 예전 단일 클립 경로와 같은 값이 된다.
        pendingLeadIn = ClipSequence.Usable(info.Template.PlayerLeadInClips);

        // 마지막 베기 이전의 칼질마다 한 번씩 멈춘다 → 그 총 정지 시간(F)만큼 재생이 안 흐른다.
        // ⚠ F를 미리 빼서 '일찍 시작'하는 것이 주 경로다. 배속으로 때우면 모션이 뭉개진다.
        // 정지 길이는 HitStopDirector에서 당겨 온다 — 두 곳에 적으면 언젠가 하나만 고쳐진다.
        // ⚠ 리드인 원소의 칼질도 같은 예산에 들어간다 — 정지는 시퀀스 어디서 나든 재생을 그만큼 멈춘다.
        pendingExtraSpans = alignment.ResolvedExtraImpactSpans;
        pendingFreeze = ResolveFreezeBudget(pendingExtraSpans.Length);

        // 시퀀스 시작 → 마지막 임팩트까지의 저작 초. 리드인이 비면 impactSpan / speed와 같다.
        pendingAuthored = ClipSequence.AuthoredSpan(pendingLeadIn, alignment);

        pendingScheduleStart = Mathf.Max(impactAlignTime - pendingFreeze - pendingAuthored, info.FirstNodeTime);
        hasPending = true;
    }

    /// <summary>
    /// 이 시퀀스가 재생 중 소비할 총 정지 시간. <b>리드인 원소의 칼질까지 합산한다</b> —
    /// 정지가 시퀀스 어디서 나든 그만큼 재생이 안 흐르므로, 예산이 모자라면 마지막 임팩트가 그만큼 늦는다.
    /// </summary>
    private float ResolveFreezeBudget(int finalExtraCount)
    {
        if (hitStopDirector == null) return 0f;

        int stops = finalExtraCount;
        for (int i = 0; i < pendingLeadIn.Length; i++)
            stops += pendingLeadIn[i].ResolvedExtraImpactSpans.Length;

        return stops > 0 ? stops * hitStopDirector.HitStopDuration : 0f;
    }

    /// <summary>
    /// 예약된 성공 애니의 시작 시각에 도달하면 재생한다. 남은 시간 기준으로 배속을 재계산해 <b>임팩트 프레임</b>을 정렬한다.
    /// 배속은 트림 전체에 걸리므로 임팩트 이후 잔여 구간도 같은 배속으로 이어 재생된다.
    /// </summary>
    private void TryStartPendingSuccess()
    {
        if (!hasPending || missedThisTarget || Time.time < pendingScheduleStart) return;

        // 정지로 소비될 F를 먼저 뺀다 — 그 시간 동안 클립은 안 흐르기 때문이다.
        // ⚠ 창이 예산을 못 감당하면(남은 시간 ≤ 0) 예산을 포기하고 F = 0으로 재계산한다.
        // 정렬은 그 뒤 따라잡기(ApplyHitStop 해제 경로)가 끌어온다.
        float freeze = pendingFreeze;
        if (pendingImpactAlignTime - freeze - Time.time <= 0f)
        {
            if (freeze > 0f)
                Debug.LogWarning(
                    $"[CharacterActionPlayer] 정지 예산({freeze:0.00}s)이 남은 창보다 커서 포기합니다 — " +
                    "추가 히트스톱 수를 줄이거나 마크를 앞으로 당기세요.", this);
            freeze = 0f;
        }

        // ⚠ 배속은 원소마다 따로 정하지 않는다. 시퀀스 전체에 걸리는 비율 하나를 구해
        // 원소 i의 애니메이터 배속을 speed_i × ratio로 만든다 — 그래야 창이 모자라도
        // 원소가 잘리지 않고 '전부 재생되되 같은 비율로 빨라진다'.
        float available = pendingImpactAlignTime - freeze - Time.time;
        float ratio = ClipSequence.ResolveRatio(pendingAuthored, available);

        // 원소가 하나뿐이면 상한을 유지한다 — 기존 템플릿의 실패 양상을 바꾸지 않기 위해서다.
        // 원소가 둘 이상이면 상한을 걸지 않는다: "전부 재생된다"가 이 기능의 요구라 상한과 양립하지 않는다.
        float cap = Mathf.Max(maxAttackSpeed, pendingBaseSpeed);
        if (pendingLeadIn.Length == 0) ratio = Mathf.Min(ratio, cap / pendingBaseSpeed);

        WarnIfOverCompressed(ratio, cap);

        sequenceRatio = ratio;
        leadInQueue = pendingLeadIn;
        leadInCursor = 0;
        headAuthored = 0f;

        if (leadInQueue.Length > 0)
        {
            // ⚠ 게이트는 재생 <b>뒤</b>에 올린다 — PlaySlot(continuesSequence:false)이 진행 중이던 시퀀스를
            // 끊으려고 inLeadIn을 내리므로, 먼저 올리면 그 리셋에 그대로 덮인다.
            PlayLeadInElement(0, continuesSequence: false);
            inLeadIn = true;

            // 리드인 동안의 복귀 판단은 inLeadIn 게이트가 막지만, 값 자체도 시퀀스 끝을 가리키게 둔다.
            // 마지막 원소가 시작될 때 실제 배속으로 정확히 다시 세워진다.
            float tailAuthored = Mathf.Max(pendingDur - pendingImpactSpan, 0f) / pendingBaseSpeed;
            actionEndTime = Time.time + (pendingAuthored + tailAuthored) / Mathf.Max(ratio, 0.01f);
            recoveryEndTime = actionEndTime + recoveryHoldDuration;
        }
        else
        {
            inLeadIn = false;
            PlayAlignedFinal(pendingBaseSpeed * ratio, continuesSequence: false);
        }

        // ⚠ PlaySlot이 0으로 리셋한 뒤라 여기서 세워야 한다(피격·일회성은 0으로 남아 정렬 대상이 아니게 된다).
        impactAuthored = pendingAuthored;

        hasPending = false;
    }

    /// <summary>
    /// 압축이 눈에 보일 만큼 심하면 알린다. <b>조용히 어긋나는 게 최악</b>이라는 기존 규율 그대로다 —
    /// 패링은 칼끼리 만나는 거라 즉시 보이고, 시퀀스는 모션이 통째로 뭉개져 보인다.
    /// </summary>
    private void WarnIfOverCompressed(float ratio, float cap)
    {
        float finalSpeed = pendingBaseSpeed * ratio;
        if (finalSpeed <= cap) return;

        if (pendingLeadIn.Length > 0)
        {
            Debug.LogWarning(
                $"[CharacterActionPlayer] 클립 시퀀스({pendingLeadIn.Length + 1}개, 저작 {pendingAuthored:0.00}s)가 " +
                $"창에 안 들어가 {ratio:0.00}배로 압축됩니다(상한 {cap / pendingBaseSpeed:0.00}배 초과). " +
                "원소를 줄이거나 채보의 노드 간격을 늘리세요.", this);
            return;
        }

        if (currentAttacker == EnemySpace.Attacker.Enemy)
        {
            Debug.LogWarning(
                $"[CharacterActionPlayer] 패링 클립이 배속 상한({cap:0.00})에 걸려 임팩트 정렬이 어긋납니다 " +
                $"(필요 {finalSpeed:0.00}배). 클립의 ImpactTime을 앞으로 당기세요.", this);
        }
    }

    /// <summary>
    /// 리드인 원소 하나를 재생한다. <b>정렬 대상이 아니다</b> — 트림 전체가 재생 길이이고
    /// <c>ImpactTime</c>은 보지 않는다(정렬 앵커는 마지막 클립 하나뿐, §6).
    /// </summary>
    private void PlayLeadInElement(int index, bool continuesSequence)
    {
        var element = leadInQueue[index];
        float duration = element.ResolvedDuration;

        PlaySlot(element.Clip, element.StartOffset, duration, element.Speed * sequenceRatio,
                 isSwing: true, continuesSequence);

        playingBaseSpeed = element.Speed;                 // 헤드 환산 단위(= 이 원소의 저작 배속)
        playingImpactAlignTime = pendingImpactAlignTime;  // 헤드는 여전히 '이 패턴의 것'이다(§11-9)
        playingElementDuration = duration;
        playingExtraSpans = element.ResolvedExtraImpactSpans;
        extraCursor = 0;
        EmitNextExtraImpact();
        RefreshElementEnd();
    }

    /// <summary>
    /// 정렬 대상(마지막) 클립을 재생한다. 여기서부터는 <b>예전 단일 클립 경로와 완전히 같은 상태</b>가 되어
    /// 트림 끝 래치·복귀 세 경로·히트스톱 밀기가 그대로 돈다.
    /// </summary>
    private void PlayAlignedFinal(float speed, bool continuesSequence)
    {
        PlaySlot(pendingClip, pendingStartOffset, pendingDur, speed, isSwing: true, continuesSequence);

        playingBaseSpeed = pendingBaseSpeed;             // 거리 커브의 시간 단위 = 저작 배속(툴의 t와 같다)
        playingImpactAlignTime = pendingImpactAlignTime; // 이 헤드가 '어느 패턴의 것'인지
        playingElementDuration = pendingDur;
        playingExtraSpans = pendingExtraSpans;
        extraCursor = 0;
        EmitNextExtraImpact();
        RefreshElementEnd();

        // 리드인을 거쳐 왔다면 복귀 스케줄이 아직 '추정값'이다. 실제 배속을 아는 지금 정확히 세운다.
        if (continuesSequence)
        {
            actionEndTime = Time.time + pendingDur / Mathf.Max(speed, 0.01f);
            recoveryEndTime = actionEndTime + recoveryHoldDuration;
        }
    }

    /// <summary>
    /// 지금 원소가 끝나는 시각을 <b>남은 클립 내용에서</b> 다시 구한다. 정지·따라잡기로 배속이 바뀐 뒤에도
    /// 자기 수정되는 것이 이 계산의 존재 이유다(시작 시각에서 한 번 재면 그 뒤 배속 변화를 못 따라간다).
    /// </summary>
    private void RefreshElementEnd()
    {
        float remaining = Mathf.Max(playingElementDuration - clipConsumed, 0f);
        elementEndTime = Time.time + remaining / Mathf.Max(playSpeed, 0.01f);
    }

    /// <summary>
    /// 리드인 원소가 끝나면 다음 원소로 넘긴다. 마지막 리드인이 끝나면 정렬 대상 클립으로 넘어가며
    /// 그 순간 <see cref="inLeadIn"/>이 내려가 예전 경로가 다시 주인이 된다.
    /// </summary>
    private void TickLeadIn()
    {
        if (!inLeadIn || Time.time < elementEndTime) return;

        CommitHeadProgress();
        leadInCursor++;

        if (leadInCursor < leadInQueue.Length)
        {
            PlayLeadInElement(leadInCursor, continuesSequence: true);
            return;
        }

        inLeadIn = false;
        PlayAlignedFinal(pendingBaseSpeed * sequenceRatio, continuesSequence: true);
    }

    // ─────────────────────────── 히트스톱 ───────────────────────────

    /// <summary>
    /// 임팩트 프레임에서 <b>공격 클립만</b> 멈춘다. 판정·오디오·이동은 전혀 건드리지 않는다 —
    /// 멈추는 것은 Animator의 Speed Multiplier(<c>AttackSpeed</c>) 하나뿐이다.
    /// (<c>Time.timeScale</c>은 쓸 수 없다. 채보는 <c>audioSource.time</c>으로 도는데 판정은 <c>Time.time</c>이라
    /// 시계를 내리면 둘이 영구히 갈라진다 — 히트스톱 한 번이 <c>perfectWindow</c>(0.05초)를 넘는다.)
    ///
    /// <para><b>⚠ '밀기'다. 캐치업이 아니다</b> — 적(<c>EnemyView.ApplyHitStop</c>)과 같은 모델이다.
    /// 공격 클립은 임팩트를 <b>트림 끝 근처</b>에 찍으므로 임팩트 시점에 남은 트림 내용이
    /// 실측 0.036~0.109초뿐이다(전 패턴 10/10). 정지 0.08초가 그보다 길어서
    /// <b>재개하는 순간 이미 원래 종료 시각이 지나 있다</b> — 압축할 시간이 음수라 캐치업이 원리적으로 불가능하다.</para>
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

        // 멈추기 전에 지금까지의 진행분을 확정한다 — 해제 시 남은 스팬을 알아야 한다.
        CommitHeadProgress();

        hitStopReleaseTime = Time.time + duration;
        hitStopped = true;

        // ⚠ 임팩트를 '지난 뒤'에만 복귀 스케줄을 민다. 아직 전이라면 미는 순간
        // 마지막 베기가 정지 시간만큼 늦어져 표적 절단과 어긋난다 — 그건 해제 시 배속으로 벌충한다.
        if (!IsBeforeImpact())
        {
            actionEndTime += duration;
            recoveryEndTime += duration;
        }

        animator.SetFloat(attackSpeedHash, 0f);
        return true;
    }

    /// <summary>
    /// 정렬 대상 재생이면서 아직 마지막 베기(임팩트 프레임)에 도달하지 않았는가.
    /// <b>시퀀스 전체의 저작 초로 본다</b> — 리드인 원소를 재생 중이면 언제나 임팩트 이전이다.
    /// </summary>
    private bool IsBeforeImpact() => impactAuthored > 0f && headAuthored < impactAuthored;

    /// <summary>
    /// 정지를 푼다. <b>임팩트 전이면 배속을 다시 계산한다</b>(따라잡기) —
    /// 남은 임팩트 스팬을 남은 실시간에 정확히 채워 마지막 베기가 제자리에 도착하게 한다.
    ///
    /// <para>예산(<c>pendingFreeze</c>)이 정확하면 이 값은 원래 배속과 거의 같다.
    /// 따라잡기는 <b>주 수단이 아니라 오차 보정</b>이다 — 스톱이 하나 버려지거나 창이 모자랐을 때 자기 수정된다.</para>
    /// </summary>
    private void ReleaseHitStop()
    {
        segmentStartTime = Time.time;

        if (IsBeforeImpact())
        {
            float remainingAuthored = impactAuthored - headAuthored;
            float remainingTime = pendingImpactAlignTime - Time.time;

            if (remainingTime > 0.0001f)
            {
                // ⚠ 따라잡기는 지금 원소의 배속이 아니라 <b>시퀀스 비율</b>을 다시 잡는다.
                // 남은 스팬이 원소 경계를 넘을 수 있으므로, 비율을 고쳐야 뒤따르는 원소까지 함께 벌충된다.
                float needed = remainingAuthored / remainingTime;
                float cap = Mathf.Max(maxAttackSpeed, playSpeed);

                if (needed * playingBaseSpeed > cap)
                    Debug.LogWarning(
                        $"[CharacterActionPlayer] 히트스톱 이후 따라잡기가 배속 상한({cap:0.00})에 걸렸습니다 " +
                        $"(필요 {needed * playingBaseSpeed:0.00}배) — 마지막 베기가 절단보다 늦습니다. 추가 스톱 수를 줄이세요.", this);

                sequenceRatio = Mathf.Max(needed, 0.01f);
                playSpeed = Mathf.Clamp(sequenceRatio * playingBaseSpeed, 0.01f, cap);
            }
        }

        animator.SetFloat(attackSpeedHash, playSpeed);

        // 배속이 바뀌었으니 이 원소가 끝나는 시각도 다시 잡는다(정지 시간만큼 밀린 것도 여기서 흡수된다).
        RefreshElementEnd();

        // 다음 칼질은 이 시점에야 정확히 계산된다(정지로 밀렸으므로).
        EmitNextExtraImpact();
    }

    /// <summary>
    /// 아직 안 온 칼질 중 <b>하나만</b> 알린다. 남은 클립 스팬을 지금 배속으로 나눠 절대 시각을 만든다.
    /// 이미 지나간 마크는 건너뛴다(정지가 길어 통째로 삼켜진 경우).
    /// </summary>
    private void EmitNextExtraImpact()
    {
        if (OnExtraImpact == null || playingExtraSpans == null) return;

        while (extraCursor < playingExtraSpans.Length)
        {
            float remainingSpan = playingExtraSpans[extraCursor] - clipConsumed;
            extraCursor++;

            if (remainingSpan <= 0f) continue; // 이미 지나갔다

            OnExtraImpact.Invoke(Time.time + remainingSpan / Mathf.Max(playSpeed, 0.01f));
            return;
        }
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
    ///
    /// <para><b>예외는 상호 공격뿐이다</b>(<see cref="PatternSpace.Pattern.CountersOnFail"/>) — 그 패턴에서는
    /// 적이 견제 대신 진짜 공격을 같이 휘두르고 있으므로, 실패하면 그 칼이 <c>impactTime</c>에 닿는다.
    /// 예약 경로는 <c>Attacker.Enemy</c>와 완전히 같다.</para>
    /// </summary>
    private void HandleJudgeTargetFirstMiss()
    {
        if (missedThisTarget) return;
        missedThisTarget = true;
        hasPending = false; // 예약 취소 → 원래 나올 베기 안 나옴

        // 리드인 재생 중이었다면 다음 원소로 넘기지 않는다 — 지금 원소는 끝까지 재생되고 거기서 복귀한다
        // (단일 클립에서 진행 중이던 베기가 취소되지 않는 것과 같은 결). 복귀 스케줄은 아직 '추정값'이라
        // 지금 원소의 끝으로 정확히 다시 잡아 준다.
        if (inLeadIn)
        {
            inLeadIn = false;
            actionEndTime = elementEndTime;
            recoveryEndTime = actionEndTime + recoveryHoldDuration;
        }

        // 맞는 경우가 둘이다 — 적이 공격자였거나(패링 실패), 상호 공격이었거나(적도 같이 휘둘렀다).
        // 어느 쪽이든 적의 칼이 이미 오고 있으므로 도착 시각(impactTime)에 피격을 예약한다.
        if (currentAttacker != EnemySpace.Attacker.Enemy && !currentCountersOnFail)
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

        PlayHitReaction();
    }

    /// <summary>
    /// 피격 리액션을 <b>지금</b> 재생한다. 패턴 실패(<see cref="TryStartPendingHit"/>)와
    /// 패턴 밖의 피격(기습 회피 실패)이 같은 경로를 쓴다 — 맞는 건 맞는 거라 가를 이유가 없다.
    ///
    /// <para><see cref="OnPlayerHit"/>가 여기서 나므로 <b>카메라 피격 큐·체력 감소가 그대로 따라온다</b>
    /// (구독자 쪽에 새 배선이 필요 없다).</para>
    /// </summary>
    public void PlayHitReaction()
    {
        AnimationClip hit = NextHitClip();
        if (hit != null)
            PlaySlot(hit, 0f, hit.length, 1f, isSwing: false);
        else
            RaiseSwingEnded(); // 힛 클립이 없어도 진행 중이던 베기는 취소됐다.

        OnPlayerHit?.Invoke();
    }

    /// <summary>
    /// 클립 하나를 <b>일회성</b>으로 재생한다(회피 구르기 등). 휘두르는 동작이 아니므로 트레일은 켜지지 않는다.
    ///
    /// <para><b>예약(<c>hasPending</c>)은 건드리지 않는다</b> — 다음 공격은 자기 <c>Deadline</c>에서 독립 예약이라
    /// 제시각에 시작하고, 겹치면 크로스페이드가 이 클립을 끊을 뿐 임팩트 정렬은 안 깨진다.</para>
    /// </summary>
    /// <param name="speed">배속. 창이 빠듯할 때 구르기를 압축하는 노브다.</param>
    public void PlayOneShot(AnimationClip clip, float speed = 1f)
    {
        if (clip == null) return;

        PlaySlot(clip, 0f, clip.length, Mathf.Max(speed, 0.01f), isSwing: false);
    }

    /// <summary>
    /// 연타 타격 하나. <b>정렬 대상이 아니다</b> — 타격 시각은 플레이어가 정하므로 역산할 시각이 없다(§6의 전제가 없다).
    /// 그래서 <c>ImpactTime</c>을 정렬 앵커가 아니라 <b>재생 시작점</b>으로 쓴다: 와인드업을 건너뛰고
    /// <b>써는 구간만</b> 나온다.
    ///
    /// <para>클립은 리스트를 <b>번갈아</b> 돈다(<c>hitClips</c>와 같은 관용구) — 하나만 반복하면
    /// 연타가 한 동작의 되감기로 보인다. <b>목표 타수를 넘겨도 계속 돈다</b>: 그것이
    /// "그 이상은 애니메이션만 변경된다"의 구현이다.</para>
    ///
    /// <para>⚠ <c>isSwing: true</c>여야 한다 — <see cref="ApplyHitStop"/>의 가드가 <c>swingActive</c>를 본다.</para>
    /// </summary>
    private void HandleMashHit(MashHitInfo info)
    {
        // ⚠ 커서를 들지 않는다. 누적 타수에서 유도하므로 적 리액션·이펙트가 '같은 답'을 볼 수밖에 없다
        //    (각자 커서를 돌리면 언젠가 다른 모션에 다른 리액션이 붙는다).
        var strike = info.Template.MashStrikeFor(info.Hits);
        var alignment = strike != null ? strike.PlayerClip : null;
        if (alignment == null || !alignment.IsUsable) return;

        float trimStart = alignment.StartOffset;
        float end = trimStart + alignment.ResolvedDuration;
        float impact = alignment.ImpactTime > 0f ? alignment.ImpactTime : trimStart;

        // 임팩트 '부터'가 아니라 임팩트 '직전'부터 시작한다 — 그 짧은 구간이 칼이 지나가는 순간이다.
        // ⚠ 임팩트에서 정확히 시작하면 보이는 것이 마무리 동작(follow-through)뿐이라
        //   매 타격이 '느리게 가라앉는 모션'이 되고, 그게 연타 전체를 흐느적거리게 만든다.
        float from = Mathf.Max(impact - mashPreRoll, trimStart);
        if (end - from <= 0f) return;

        // ⚠ 크로스페이드가 짧아야 임팩트 포즈가 실제로 '도착'한다. 일반 공격값(0.15초)은
        //   연타 간격과 같은 자릿수라 포즈가 도착하기 전에 다음 타격이 들어오고,
        //   캐릭터가 영원히 두 포즈 사이에서 섞인 채로 남는다.
        PlaySlot(alignment.Clip, from, end - from, alignment.Speed, isSwing: true,
                 continuesSequence: false, crossFadeOverride: mashCrossFadeDuration);
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
    ///
    /// <para><paramref name="continuesSequence"/>는 이 클립이 <b>같은 시퀀스의 다음 원소</b>인지다. true면
    /// 아래 여섯을 건너뛴다 — 하나만 빠져도 증상이 다 다르다:
    /// 복귀 스케줄(1타 만에 종료) · 레이어 웨이트 블렌드(매 타 깜빡임) · Release 래치(중간에 Release 누출) ·
    /// 히트스톱 플래그(전환이 정지를 삼킴) · 스윙 재발행(<c>swingActive</c>가 잠깐 false → 그 순간 히트스톱이 무시됨) ·
    /// 헤드 리셋(거리 커브의 원점이 원소마다 다시 잡힘).</para>
    /// </summary>
    private void PlaySlot(AnimationClip clip, float startOffset, float dur, float speed, bool isSwing,
                          bool continuesSequence = false, float crossFadeOverride = -1f)
    {
        if (overrideController == null || placeholderA == null || placeholderB == null || attackLayerIndex < 0) return;

        if (!continuesSequence)
        {
            if (isSwing) RaiseSwingBegan();
            else RaiseSwingEnded();
        }

        int targetStateHash = useSlotA ? attackStateAHash : attackStateBHash;
        AnimationClip targetPlaceholder = useSlotA ? placeholderA : placeholderB;

        overrideController[targetPlaceholder] = clip;

        animator.SetFloat(attackSpeedHash, speed);
        speedRestored = false;

        playSpeed = Mathf.Max(speed, 0.01f);
        segmentStartTime = Time.time;
        clipConsumed = 0f;           // ⚠ 원소마다 리셋된다 — 추가 히트스톱 마크가 그 원소의 클립 초라서다.
        playingElementDuration = dur;
        playingImpactAlignTime = float.NaN;
        playingExtraSpans = null;
        extraCursor = 0;

        if (!continuesSequence)
        {
            // 새 클립이 들어오면 진행 중이던 히트스톱은 의미를 잃는다(정지시킬 대상 자체가 바뀌었다).
            hitStopped = false;

            // 진행 중이던 시퀀스는 여기서 끊긴다 — 다음 패턴 · 피격 · 일회성이 전부 이 경로다.
            inLeadIn = false;

            // 헤드는 시퀀스 단위다. 정렬 대상이면 호출부가 곧바로 impactAuthored를 채운다.
            headAuthored = 0f;
            impactAuthored = 0f;

            actionEndTime = Time.time + dur / Mathf.Max(speed, 0.01f);
            recoveryEndTime = actionEndTime + recoveryHoldDuration;

            // 웨이트는 즉시 1로 점프시키지 않는다. 복귀(blend-out) 도중 인터럽트되면 현재 웨이트에서 이어 올린다.
            blendInStartTime = Time.time;
            blendInFromWeight = animator.GetLayerWeight(attackLayerIndex);
            blendOutLatched = false;
            releaseTriggered = false;
            releaseEndTime = 0f;
        }

        // CrossFadeInFixedTime의 fixedTimeOffset은 '클립 초'가 아니라 스테이트 speed가 곱해지는 '스테이트 재생 초'로 해석된다.
        // AttackSpeed를 먼저 걸어둔 상태이므로 startOffset(클립 초)을 speed로 나눠 넘겨야 실제 클립상 startOffset 지점에서 시작한다.
        // (보정하지 않으면 startOffset*speed 지점에서 시작해 클립 끝에 조기 도달 → Exit Time 전이로 애니가 중간에 끊긴다.)
        float crossFade = crossFadeOverride >= 0f ? crossFadeOverride : attackCrossFadeDuration;
        animator.CrossFadeInFixedTime(targetStateHash, crossFade, attackLayerIndex, startOffset / Mathf.Max(speed, 0.01f));

        useSlotA = !useSlotA;
    }
}
