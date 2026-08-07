using System.Collections;
using System.Collections.Generic;
using PatternSpace;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 연출의 <b>유일한 관리 지점</b>. <see cref="PatternHandler"/>의 기존 확장 이벤트만 구독해
/// 카탈로그에서 큐를 골라 재생한다. 판정 파이프라인에는 개입하지 않는다(순수 연출) —
/// <c>EffectManager</c>·<c>SliceTargetDirector</c>와 같은 위치다.
///
/// <para><b>큐는 셋이고, 각각 화면에서 실제로 사건이 일어나는 순간에 맞춘다.</b>
/// 성공/실패(표적 파괴)는 <b>Deadline</b> — 칼날 임팩트 프레임·표적 절단과 같은 식이다.
/// 반면 피격은 <see cref="CharacterActionPlayer.OnPlayerHit"/> — <b>적 칼이 실제로 닿는 순간</b>이다
/// (첫 미스 순간이 아니다. 그때는 적 칼이 아직 오는 중이라 화면에는 아무 일도 안 일어났다).
/// 실패는 사건이 둘이라 큐도 둘이다.</para>
///
/// <para>⚠ <b>플레이어가 공격자인 패턴(<c>Attacker.Player</c>)은 실패해도 피격 큐가 없다</b> —
/// 적이 애초에 휘두르지 않았고 <c>OnPlayerHit</c>도 발행되지 않는다. 그 실패에는 <c>PatternFailure</c> 하나만 난다.</para>
///
/// <para><b>예약은 하나면 충분하다.</b> 패턴 완료는 순차적이고, A의 Deadline은 A 마지막 노드 +goodWindow(0.1초)인데
/// B의 완료는 A보다 최소 0.4초 뒤다 → 동시에 대기 중인 예약은 최대 하나다. 리스트를 두지 않는다.</para>
///
/// <para><b>겹침은 합성되지 않는다.</b> Perlin은 채널이 하나뿐이다. 그리고 마지막 노드에서 미스가 나면
/// <see cref="CameraTrigger.PatternMiss"/>와 <see cref="CameraTrigger.PatternFailure"/>가 0.1초 간격으로 확실히 붙는다.
/// 그래서 새 쉐이크는 타이머를 재시작하되 <b>진폭은 큰 쪽을 취한다</b> — 단순 덮어쓰기면 강한 쉐이크 도중
/// 약한 쉐이크가 들어와 세기가 뚝 떨어진다.</para>
///
/// <para><b>휴지값은 0이 아니다.</b> 씬의 Perlin에 상시 흔들림 값이 들어 있을 수 있어
/// <see cref="Awake"/>에서 현재 값을 캐시해 그리로 복귀한다.</para>
///
/// <para>─────────────────────────────────────────────────────────</para>
///
/// <para>이 클래스가 하는 일은 <b>셋</b>이고, 서로 다른 층에 산다 —
/// <b>쉐이크</b>(노이즈 채널), <b>프레이밍</b>(무엇을 + 어느 방향에서 담을지), <b>인트로</b>(어느 vcam을 쓸지).
/// 채널이 겹치지 않아 동시에 돌아도 간섭하지 않는다.</para>
///
/// <para><b>프레이밍은 두 값을 낸다</b> — 상대의 <b>가중치</b>(거리의 함수)와 카메라 궤도의 <b>방향</b>(플레이어 yaw).
/// 방향은 그룹 오브젝트의 회전으로 나가며, 그래서 카메라가 언제나 플레이어 등 뒤에 선다(<see cref="ApplyFraming"/>).</para>
///
/// <para><b>Cinemachine 타입은 네 이음매에만 등장한다</b> — <see cref="ApplyShake"/>,
/// <see cref="ApplyLens"/>, <see cref="ApplyFraming"/>, <see cref="IntroRoutine"/>.
/// 위층(트리거·카탈로그·타이밍·거리 계산)은 Cinemachine을 모르므로, 나중에 쉐이크를 Impulse로 갈아끼우거나
/// 프레이밍 방식을 바꿔도 그대로 남는다.</para>
///
/// <para><b>FOV를 미는 소비자는 둘이다</b> — 임팩트 순간의 <b>펀치</b>(짧은 감쇠)와 마지막 노트를 향해 조였다 펴는
/// <b>줌</b>(<see cref="ScheduleZoom"/>). 둘은 임팩트에 동시에 살아 있으므로 각자 오프셋만 갱신하고
/// <see cref="ApplyLens"/>가 합산해 한 번에 쓴다. 렌즈에 직접 쓰면 나중에 쓴 쪽이 앞의 것을 지운다.</para>
///
/// <para><b>쉐이크와 펀치는 채널이 다르다</b> — 쉐이크는 Perlin 노이즈, 펀치는 렌즈 FOV다.
/// 임팩트 순간 둘이 같이 나가도 간섭하지 않는다. <b>애니메이터를 멈추는 히트스톱은 여기 없다</b>
/// (<c>HitStopDirector</c>가 자기 층에서 한다) — 이 클래스는 카메라만 만진다.</para>
///
/// <para><b>세 기능은 각자 독립적으로 꺼진다.</b> 배선이 빠진 기능만 조용히 비활성되고 나머지는 동작한다 —
/// 새 기능이 기존 씬을 깨지 않게 하는 규율이다.</para>
/// </summary>
public class CameraDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("피격 큐의 시각원. 칼이 실제로 닿는 순간을 알려준다(첫 미스 순간이 아니다).")]
    [SerializeField] private CharacterActionPlayer actionPlayer;

    [Tooltip("적 처치 큐. 비우면 그 트리거만 무연출.")]
    [SerializeField] private EnemySpace.EnemyDirector enemyDirector;

    [Tooltip("쉐이크·펀치의 폴백 대상. 앵글 교체(CameraAngleSwitcher)로 여러 vcam을 쓸 때는 " +
             "live vcam을 조회해 그쪽에 걸고, 조회가 실패할 때만 이 값을 쓴다.")]
    [SerializeField] private CinemachineCamera gameplayCamera;

    [Header("Toggles")]
    [Tooltip("화면 흔들림. 끄면 Perlin은 씬의 휴지값 그대로 남는다.")]
    [SerializeField] private bool shakeEnabled = true;

    [Tooltip("임팩트 순간의 FOV 펀치. 끄면 렌즈는 씬 값 그대로 남는다.")]
    [SerializeField] private bool punchEnabled = true;

    [Tooltip("교전 상대를 함께 담고 카메라를 플레이어 등 뒤에 두는 프레이밍. 끄면 그룹을 건드리지 않는다.")]
    [SerializeField] private bool framingEnabledOption = true;

    [Tooltip("곡 시작 전 스플라인 인트로.")]
    [SerializeField] private bool introEnabled = true;

    [Header("Angle Switch")]
    [Tooltip("앵글 vcam 랜덤 교체. 0번이 곡 시작 카메라이며, 2대 미만이면 조용히 비활성된다.")]
    [SerializeField] private CameraAngleSwitcher angleSwitcher = new CameraAngleSwitcher();

    [Header("Cue Catalog")]
    [Tooltip("트리거별 쉐이크 설정. 연출 추가 = 여기에 한 줄.")]
    [SerializeField] private List<CameraCueEntry> catalog = new List<CameraCueEntry>();

    [Tooltip("모든 큐가 공유하는 흔들림 주파수. 0.2초 남짓 쉐이크에서는 큐별로 나눌 만한 차이가 나지 않는다.")]
    [SerializeField] private float shakeFrequency = 1.6f;

    [Header("Impact Zoom")]
    [Tooltip("마지막 노트 임팩트 줌. 끄면 FOV는 펀치만 만진다.")]
    [SerializeField] private bool zoomEnabled = true;

    [Tooltip("줌인 최대 화각 변화(도). 음수 = 화각이 좁아진다(조여든다). " +
             "임팩트에 이 값에서 0으로 되돌아오는 것이 줌아웃이다.")]
    [SerializeField] private float zoomFovDelta = -8f;

    [Tooltip("임팩트 기준 몇 초 전부터 조이기 시작할지. " +
             "패턴 간격이 촘촘하면(최소 0.4초) 앞 패턴의 줌아웃에 밀려 램프가 압축된다.")]
    [SerializeField] private float zoomLeadTime = 0.3f;

    [Tooltip("임팩트에서 원래 화각으로 펴지는 시간(초). 히트스톱이 걸리면 정지 창만큼 뒤로 밀린다.")]
    [SerializeField] private float zoomReleaseDuration = 0.12f;

    [Tooltip("이 개수 이상의 노드를 가진 패턴에서만 줌이 걸린다. " +
             "노트가 적으면 조일 구간 자체가 없어 연출이 성립하지 않는다.")]
    [SerializeField] private int minNodeCount = 3;

    [Tooltip("줌인 구간의 시간 배분. 줌아웃은 선형이다(임팩트에서 펴지는 맛이 곡선보다 낫다).")]
    [SerializeField]
    private AnimationCurve zoomEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Framing")]
    [Tooltip("게임플레이 vcam이 추적하는 타겟 그룹. 비우면 프레이밍 기능만 꺼진다.")]
    [SerializeField] private CinemachineTargetGroup targetGroup;

    [Tooltip("이 거리 이하면 적을 완전히 담는다(가중치 1).")]
    [SerializeField] private float fullFrameDistance = 3f;

    [Tooltip("이 거리 이상이면 적을 담지 않는다(가중치 0) — 화면에는 플레이어만 남는다.")]
    [SerializeField] private float dropoffDistance = 6f;

    [Tooltip("가중치가 목표를 따라가는 시간상수(초). 거리 계산이 튀어도 구도는 부드럽게 따라온다.")]
    [SerializeField] private float weightDamping = 0.35f;

    [Tooltip("카메라 궤도가 플레이어 방향을 따라가는 시간상수(초). " +
             "PlayerCombatMover.turnDuration(0.15초)보다 충분히 길어야 한다 — " +
             "같으면 상대 교체 때 화면이 0.15초에 반 바퀴 돈다. 0 이하면 즉시 스냅.")]
    [SerializeField] private float cameraTurnDamping = 0.45f;

    [Header("Intro")]
    [Tooltip("카운트다운 시각원. 비우면 인트로 기능만 꺼진다.")]
    [SerializeField] private ChartGen.ChartPlayer chartPlayer;

    [Tooltip("인트로용 vcam. 스플라인 위를 달리며 플레이어를 본다.")]
    [SerializeField] private CinemachineCamera introCamera;

    [Tooltip("인트로 vcam의 스플라인 주행 컴포넌트. PositionUnits는 Normalized여야 한다.")]
    [SerializeField] private CinemachineSplineDolly introDolly;

    [Tooltip("Main Camera의 Brain. 마무리 블렌드 시간의 진실의 원천이다.")]
    [SerializeField] private CinemachineBrain brain;

    [Tooltip("인트로 동안 vcam에 줄 우선순위. 게임플레이 vcam보다 높아야 한다.")]
    [SerializeField] private int introPriority = 20;

    [Tooltip("스플라인 진행도의 시간 배분. 경로 '모양'은 씬의 스플라인이, '속도'는 이 곡선이 정한다.")]
    [SerializeField]
    private AnimationCurve introEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private readonly Dictionary<CameraTrigger, CameraCueEntry> catalogByTrigger =
        new Dictionary<CameraTrigger, CameraCueEntry>();

    /// <summary>
    /// vcam 하나의 휴지값(씬에 적힌 값). <b>vcam마다 다를 수 있으므로 대상별로 캐시한다</b> —
    /// 하나만 캐시해 두면 다른 앵글의 상시 흔들림·화각이 첫 큐에서 통째로 덮인다.
    /// </summary>
    private struct CameraIdle
    {
        public CinemachineBasicMultiChannelPerlin perlin;
        public float amplitude;
        public float frequency;
        public float fov;
    }

    private readonly Dictionary<CinemachineCamera, CameraIdle> idleByCamera =
        new Dictionary<CinemachineCamera, CameraIdle>();

    // 지금 쉐이크·펀치가 걸려 있는 vcam. live가 바뀌면 이전 것을 반드시 휴지값으로 되돌린다.
    private CinemachineCamera effectCamera;

    // 진행 중인 쉐이크.
    private float shakeAmplitude;
    private float shakeStartTime;
    private float shakeDuration;

    // 진행 중인 FOV 펀치.
    private float punchDelta;
    private float punchStartTime;
    private float punchDuration;

    // ⚠ FOV 오프셋 소비자가 둘이다(펀치·줌). 각자 자기 오프셋만 갱신하고
    // ApplyLens가 합산해 한 번에 쓴다 — 각자 렌즈에 직접 쓰면 서로를 지운다.
    private float punchOffset;
    private float zoomOffset;

    // 진행 중인 줌. hasZoom이 false면 zoomOffset은 0이다.
    private bool hasZoom;
    private float zoomRampStart;
    private float zoomImpactTime;

    // 대기 중인 줌(최대 하나). 스케줄이 이전 임팩트보다 먼저 오므로 덮어쓸 수 없다 — 아래 ScheduleZoom 참조.
    private bool hasPendingZoom;
    private float pendingZoomImpact;

    // 히트스톱 락. 이 시각까지 카메라는 완전히 얼어 있고, 큐는 해제 뒤로 미뤄진다.
    private float holdUntil;
    private bool hasDeferredCue;
    private CameraTrigger deferredCue;
    private bool brainDisabledByHold;

    private bool IsHolding => Time.time < holdUntil;

    // 대기 중인 예약(최대 하나). duration이 0 이하면 예약 없음.
    private bool hasPending;
    private float pendingFireTime;
    private CameraTrigger pendingTrigger;

    // 마지막으로 실제 재생한 큐. 잠금이 같은 프레임에 들어와도 그 큐를 버리지 않고 옮기기 위해 기억한다.
    private CameraTrigger lastCueTrigger;

    // 프레이밍 상태. framingEnabled가 false면 targetGroup을 건드리지 않는다.
    private bool framingEnabled;
    private Transform opponentTransform;
    private float opponentWeight;

    // 카메라 궤도의 방향. 플레이어 yaw를 목표로 뒤따르며, SmoothDampAngle이 속도를 들고 있어
    // 목표가 감쇠 도중에 또 바뀌어도(상대 연속 교체) 이어진다.
    private float cameraYaw;
    private float cameraYawVelocity;

    // 인트로 상태.
    private Coroutine introRoutine;
    private int introRestingPriority;

    void Awake()
    {
        if (handler == null)
            Debug.LogError("[CameraDirector] handler가 배선되지 않았습니다 — 카메라 연출이 동작하지 않습니다.", this);

        foreach (var entry in catalog)
            catalogByTrigger[entry.trigger] = entry;

        SetupFraming();
        SetupIntro();

        // 시작 구도를 확정한다. 씬에 저장된 우선순위가 무엇이든 0번이 이긴다.
        angleSwitcher.Setup();
    }

    /// <summary>
    /// 타겟 그룹을 <b>고정 2칸</b>으로 세운다 — 0번 플레이어(가중치 1), 1번 교전 상대(가중치 0에서 시작).
    ///
    /// <para>멤버를 넣었다 뺐다 하지 않는 이유: 바운드가 계단식으로 튀어 구도가 팝한다.
    /// 가중치만 움직이면 바운드가 연속적으로 변해 카메라가 부드럽게 물러나고 붙는다.
    /// 가중치 0인 멤버는 바운드 계산에서 제외되므로 "적 없음"이 정확히 표현된다.</para>
    /// </summary>
    private void SetupFraming()
    {
        if (!framingEnabledOption) return;
        if (targetGroup == null) return; // 프레이밍만 비활성 — 쉐이크·인트로는 그대로 동작한다.

        if (actionPlayer == null)
        {
            Debug.LogError("[CameraDirector] actionPlayer가 없어 프레이밍을 비활성화합니다 — 그룹의 기준이 되는 플레이어를 알 수 없습니다.", this);
            return;
        }

        if (dropoffDistance <= fullFrameDistance)
        {
            Debug.LogError($"[CameraDirector] dropoffDistance({dropoffDistance})가 fullFrameDistance({fullFrameDistance})보다 커야 합니다. " +
                           "프레이밍을 비활성화합니다.", this);
            return;
        }

        targetGroup.Targets.Clear();
        targetGroup.AddMember(actionPlayer.transform, 1f, 1f);
        targetGroup.AddMember(null, 0f, 1f); // 1번 칸은 자리만 잡아 둔다. 대상은 승격될 때 채운다.

        opponentWeight = 0f;

        // 첫 프레임에 0°에서 감쇠가 시작되면 카메라가 한 바퀴 돌며 들어온다. 지금 방향에서 출발시킨다.
        cameraYaw = actionPlayer.transform.eulerAngles.y;
        cameraYawVelocity = 0f;

        framingEnabled = true;
    }

    private void SetupIntro()
    {
        if (!introEnabled) return;
        if (chartPlayer == null || introCamera == null || introDolly == null || brain == null) return;

        // 씬에 적힌 값을 그대로 휴지값으로 삼는다. 게임플레이 vcam(우선순위 0)보다 낮게 두어야
        // 인트로가 끝난 뒤 동점으로 남지 않는다 — 동점이면 어느 쪽이 이길지 활성화 순서에 달린다.
        introRestingPriority = introCamera.Priority.Value;
        if (introRestingPriority >= introPriority)
            Debug.LogWarning($"[CameraDirector] introCamera의 휴지 우선순위({introRestingPriority})가 " +
                             $"인트로 우선순위({introPriority}) 이상입니다 — 인트로가 끝나도 내려오지 않습니다.", this);

        // Normalized가 아니면 0→1이 '전체 경로'를 뜻하지 않는다 — 경로가 조용히 일부만 재생된다.
        if (introDolly.PositionUnits != UnityEngine.Splines.PathIndexUnit.Normalized)
            Debug.LogWarning("[CameraDirector] introDolly.PositionUnits가 Normalized가 아닙니다 — " +
                             "인트로 경로가 일부만 재생됩니다.", this);
    }

    void OnEnable()
    {
        if (actionPlayer != null) actionPlayer.OnPlayerHit += HandlePlayerHit;

        if (enemyDirector != null)
        {
            enemyDirector.OnEnemyBurst += HandleEnemyKilled;
            enemyDirector.OnOpponentChanged += HandleOpponentChanged;

            // 구독보다 먼저 승격이 끝났을 수 있다 — 이벤트만으로는 지금 상태를 알 수 없어 한 번 읽는다.
            SetOpponent(enemyDirector.CurrentOpponent);
        }

        if (chartPlayer != null) chartPlayer.OnCountdownStarted += HandleCountdownStarted;

        if (handler == null) return;
        handler.OnPatternComplete += HandlePatternComplete;
        handler.OnAllPatternsCleared += HandleAllCleared;
        handler.OnJudgeTargetBegan += HandleJudgeTargetBegan;
    }

    void OnDisable()
    {
        if (actionPlayer != null) actionPlayer.OnPlayerHit -= HandlePlayerHit;

        if (enemyDirector != null)
        {
            enemyDirector.OnEnemyBurst -= HandleEnemyKilled;
            enemyDirector.OnOpponentChanged -= HandleOpponentChanged;
        }

        if (chartPlayer != null) chartPlayer.OnCountdownStarted -= HandleCountdownStarted;

        if (handler != null)
        {
            handler.OnPatternComplete -= HandlePatternComplete;
            handler.OnAllPatternsCleared -= HandleAllCleared;
            handler.OnJudgeTargetBegan -= HandleJudgeTargetBegan;
        }

        StopShake(); // 꺼진 채 흔들림이 남지 않도록.
        StopZoom();  // 꺼진 채 좁혀진 화각이 씬에 남지 않도록.
        StopIntro(); // 인트로 도중 꺼져도 vcam이 높은 우선순위로 남지 않도록.

        // ⚠ 잠금 도중 꺼지면 Brain이 꺼진 채 남아 카메라가 영구히 굳는다.
        holdUntil = 0f;
        hasDeferredCue = false;
        if (brainDisabledByHold)
        {
            brainDisabledByHold = false;
            if (brain != null) brain.enabled = true;
        }
    }

    // ── 이벤트 처리 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 성공/실패 큐를 <b>Deadline에 맞춰 예약</b>한다. 표적이 갈라지거나 부딪히는 바로 그 순간이다.
    /// 완주 성공은 Deadline 이전에, 만료 실패는 Deadline 직후에 이 이벤트가 오므로
    /// "시각이 이미 지났으면 즉시 발사"는 예외가 아니라 실패 경로의 정상 동작이다.
    /// </summary>
    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        var trigger = info.AllCorrect ? CameraTrigger.PatternSuccess : CameraTrigger.PatternFailure;

        float offset = info.Pattern != null ? info.Pattern.ImpactOffset : 0f;
        float fireTime = info.LastNodeTime + handler.GoodWindow + offset;

        if (Time.time >= fireTime)
        {
            PlayCue(trigger);
            return;
        }

        hasPending = true;
        pendingFireTime = fireTime;
        pendingTrigger = trigger;
    }

    /// <summary>
    /// 피격 큐는 <b>칼이 실제로 닿는 순간</b>에 터진다. 예전에는 첫 미스 순간에 즉시 냈는데,
    /// 그건 적 칼이 도착하기 전이라 화면 흔들림이 피격보다 먼저 왔다.
    /// <see cref="CharacterActionPlayer.OnPlayerHit"/>이 그 시각에 정확히 발행되므로 예약이 필요 없다.
    /// </summary>
    private void HandlePlayerHit() => PlayCue(CameraTrigger.PatternMiss);

    /// <summary>
    /// 적이 갈라지는 순간 — 히트스톱을 못 쓰는 대신 타격감을 내는 주 큐다.
    ///
    /// <para><b>처치 확정(<c>OnEnemyKilled</c>)이 아니라 절단(<c>OnEnemyBurst</c>)을 듣는다.</b>
    /// 확정은 마지막 노드 입력 순간이고 절단은 사망 클립이 끝난 뒤라, 확정에 걸면
    /// 적이 쓰러지기도 전에 화면이 흔들린다. 큐 시각은 언제나 <b>화면에서 사건이 일어나는 순간</b>이다.</para>
    /// </summary>
    private void HandleEnemyKilled(EnemySpace.EnemyView view) => PlayCue(CameraTrigger.EnemyKilled);

    /// <summary>
    /// 패턴이 넘어가는 순간 — 앵글 교체를 <b>예약</b>한다(발사는 카메라 큐가 끝난 뒤, <see cref="Update"/>에서).
    /// 이 이벤트는 임팩트보다 먼저 오므로 여기서 바로 교체하면 블렌드가 임팩트·쉐이크를 덮는다.
    /// </summary>
    private void HandleJudgeTargetBegan(JudgeTargetInfo info)
    {
        angleSwitcher.OnPatternBoundary();

        if (!zoomEnabled) return;

        // 노트가 적은 패턴은 조일 구간 자체가 없다. 진행 중인 줌은 자기 시각대로 마저 끝난다.
        if (info.Template == null || info.Template.AllData.Count < minNodeCount) return;

        // 임팩트 앵커는 §7-3·§11과 같은 식이다 — 칼날 임팩트 프레임·표적 절단·히트스톱과 한 시각.
        ScheduleZoom(info.Deadline + info.Template.ImpactOffset);
    }

    private void HandleAllCleared()
    {
        hasPending = false;
        StopShake();
        StopZoom();
        angleSwitcher.Reset();
    }

    private void HandleOpponentChanged(EnemySpace.EnemyView previous, EnemySpace.EnemyView current) => SetOpponent(current);

    /// <summary>
    /// 슬롯 1의 대상을 갈아끼운다. <b>가중치를 같은 순간에 0으로 떨어뜨린다.</b>
    ///
    /// <para>가중치는 연속적으로 움직이지만 <b>대상 위치는 순간이동한다</b> — 처치 순간 상대가
    /// 결투 위치(≈1m)의 적에서 링(≈6m)의 다음 적으로 바뀐다. 가중치를 이월하면
    /// 그 0.35초 동안 그룹이 <b>6m 밖 한 점을 무겁게 껴안아</b> 바운드가 부풀고, 카메라가 바깥으로 튄다.</para>
    ///
    /// <para>0에서 다시 시작하면 새 상대는 <see cref="dropoffDistance"/> 안으로 들어오는 만큼만
    /// 화면에 자리를 얻는다. 대상이 튄 그 프레임에는 가중치가 0이라 <b>바운드 계산에서 아예 빠진다</b> —
    /// 이게 D-1이 "멤버를 넣었다 뺐다 하지 않는다"로 노렸던 연속성이고, 대상 교체에도 같은 규칙을 적용하는 것이다.</para>
    ///
    /// <para>죽은 적이 풀로 반납되는 것(<c>SwapToCorpse</c>)은 임팩트 시각이라 <b>이보다 늦다</b>.
    /// 그때는 이미 대상이 다음 적을 가리키고 있어 문제가 되지 않는다.</para>
    /// </summary>
    private void SetOpponent(EnemySpace.EnemyView view)
    {
        var next = view != null ? view.transform : null;
        if (next == opponentTransform) return;

        opponentTransform = next;
        opponentWeight = 0f;
    }

    private void HandleCountdownStarted(float duration)
    {
        if (!introEnabled) return;
        if (chartPlayer == null || introCamera == null || introDolly == null || brain == null) return;

        StopIntro();
        introRoutine = StartCoroutine(IntroRoutine(duration));
    }

    // ── 루프 ────────────────────────────────────────────────────────────────

    void Update()
    {
        // 히트스톱 동안 카메라는 <b>완전히 언다</b> — 쉐이크·펀치·프레이밍이 전부 서고,
        // 예약된 큐도 여기서 통과하지 못해 자연히 해제 뒤로 밀린다(= "멈춘 다음에 연출").
        if (holdUntil > 0f)
        {
            if (IsHolding) return;
            ReleaseHold();
        }

        if (hasPending && Time.time >= pendingFireTime)
        {
            hasPending = false;
            PlayCue(pendingTrigger);
        }

        UpdateShake();
        UpdatePunch();
        UpdateZoom();
        UpdateFraming();

        // 예약된 앵글 교체는 큐가 전부 끝난 뒤에야 발사된다 — 블렌드가 임팩트·쉐이크를 덮지 않게.
        angleSwitcher.Tick(IsCuePlaying);
    }

    // ── 히트스톱 락 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 히트스톱 동안 카메라를 <b>얼린다</b>. <c>HitStopDirector</c>가 임팩트 순간에 부르는 <b>유일한 진입점</b>이다.
    ///
    /// <para><b>순서가 요구 그 자체다</b> — ① 진행 중인 쉐이크를 즉시 끄고 ② 카메라를 잠가 못 움직이게 한 뒤
    /// ③ 해제된 <b>다음에</b> 큐가 나간다. 임팩트에 쉐이크가 겹치면 "멈췄다"가 아니라 "끊겼다"로 읽힌다.</para>
    ///
    /// <para><b>잠금은 <c>CinemachineBrain</c>을 끄는 것이다.</b> 그래야 카메라 Transform이 마지막 값에 그대로 굳는다 —
    /// 프레이밍 갱신만 멈추면 감쇠(<c>PositionDamping</c> 1.0)가 남은 오차를 계속 따라가 여전히 흐른다.
    /// Brain이 배선돼 있지 않으면 프레이밍·쉐이크 정지만으로 <b>부분 잠금</b>이 되고 나머지는 그대로 동작한다.</para>
    ///
    /// <para><b>큐를 버리지 않고 미룬다.</b> 같은 프레임에 이 클래스의 <see cref="Update"/>가 먼저 돌아
    /// 큐가 이미 시작됐을 수 있어서(실행 순서는 보장되지 않는다), 진행 중인 쉐이크가 있으면 그 트리거를
    /// 지연 큐로 옮긴다 — 그래서 <b>어느 순서로 돌든 결과가 같다.</b></para>
    /// </summary>
    public void HoldForHitStop(float duration)
    {
        if (duration <= 0f) return;

        // 이 프레임에 이미 시작된 큐가 있으면 버리지 말고 해제 뒤로 옮긴다.
        if (shakeDuration > 0f || punchDuration > 0f)
        {
            hasDeferredCue = true;
            deferredCue = lastCueTrigger;
        }

        StopShake();

        // ⚠ 펀치만 지운다. 줌은 살려 둔다 — 히트스톱은 임팩트 순간이라 매 성공마다 걸리고,
        // 여기서 같이 지우면 조여 있던 화각이 정지 진입 프레임에 통째로 풀린다.
        punchDuration = 0f;
        punchDelta = 0f;
        punchOffset = 0f;
        ApplyLens();

        holdUntil = Time.time + duration;

        // 줌아웃도 <b>밀기</b>다 — 플레이어 actionEndTime·적 burstTime과 같은 규칙(§7-3의 "두 배우 모두 밀기").
        // 정지 창 동안 Brain이 꺼져 있어 렌즈에 쓴 값이 화면에 도달하지도 않으므로, 그냥 두면 해제 프레임에
        // 램프가 창 길이만큼 건너뛰어 화각이 한 프레임에 튄다(0.1초 정지 / 0.12초 줌아웃 = 83%).
        // Max인 이유: 이 클래스와 HitStopDirector의 Update 실행 순서는 보장되지 않는다 —
        // 어느 쪽이 먼저 돌든 줌아웃은 정확히 잠금 해제 순간부터 시작한다.
        if (hasZoom) zoomImpactTime = Mathf.Max(zoomImpactTime, holdUntil);

        if (brain != null && brain.enabled)
        {
            brain.enabled = false;
            brainDisabledByHold = true;
        }
    }

    private void ReleaseHold()
    {
        holdUntil = 0f;

        if (brainDisabledByHold)
        {
            brainDisabledByHold = false;
            if (brain != null) brain.enabled = true;
        }

        if (!hasDeferredCue) return;

        hasDeferredCue = false;
        PlayCue(deferredCue);
    }

    /// <summary>
    /// FOV 펀치. 쉐이크와 <b>같은 감쇠 공식</b>을 쓰지만 <b>채널이 달라</b>(노이즈 vs 렌즈) 동시에 돌아도 간섭하지 않는다.
    ///
    /// <para>⚠ <b>돌리(<c>FollowOffset</c>)가 아니라 FOV에 건다.</b> <c>CinemachineGroupFraming</c>이 매 프레임
    /// 돌리를 계산하므로 거기에 펀치를 걸면 둘이 싸운다. <c>SizeAdjustment = DollyOnly</c>의 "Zoom 금지"는
    /// <b>상시 프레이밍</b>에 대한 경고지, 0.1초짜리 전환에 대한 것이 아니다 — 그 왜곡 자체가 타격감의 재료다.</para>
    /// </summary>
    private void UpdatePunch()
    {
        if (punchDuration <= 0f) return;

        float t = (Time.time - punchStartTime) / punchDuration;
        if (t >= 1f)
        {
            punchDuration = 0f;
            punchDelta = 0f;
            punchOffset = 0f;
            ApplyLens();
            return;
        }

        punchOffset = punchDelta * Decay(t);
        ApplyLens();
    }

    // ── 임팩트 줌 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 마지막 노트 임팩트를 향해 화각을 조였다가 임팩트에서 펴는 램프를 예약한다.
    /// 시각원은 <see cref="HandleJudgeTargetBegan"/> — 임팩트 시각과 노드 수를 <b>미리</b> 알 수 있는 유일한 이벤트다
    /// (<c>OnPatternComplete</c>는 마지막 노트 뒤라 조일 구간이 이미 지나갔다).
    ///
    /// <para>⚠ <b>진행 중인 줌을 덮어쓰면 안 된다.</b> 이 스케줄은 이전 패턴이 <b>완료되는 순간</b>(마지막 노드 입력)에
    /// 오는데, 그 패턴의 임팩트는 거기서 <c>goodWindow</c>(0.1초)만큼 뒤다 → <b>새 스케줄이 이전 임팩트보다 먼저 도착한다.</b>
    /// 덮어쓰면 최대로 조여 있던 화각이 그 프레임에 풀려, 줌아웃이 임팩트가 아니라 마지막 노트 입력에 즉발로 일어난다.
    /// 그래서 대기 슬롯에 넣고 현재 줌이 끝나는 프레임에 승계한다. 슬롯이 하나면 되는 근거는 큐 예약과 같다(패턴 완료가 순차적).</para>
    /// </summary>
    private void ScheduleZoom(float impactTime)
    {
        if (hasZoom)
        {
            hasPendingZoom = true;
            pendingZoomImpact = impactTime;
            return;
        }

        BeginZoom(impactTime);
    }

    /// <summary>
    /// 램프 구간을 확정한다. 늦게 승계되면 <b>램프를 그 자리에서 시작해 압축한다</b> —
    /// 건너뛰면 화각이 한 프레임에 튀므로, 짧은 램프가 언제나 낫다.
    /// 임팩트마저 지났으면 그 패턴은 줌을 버린다(펼 시간밖에 안 남았다).
    /// </summary>
    private void BeginZoom(float impactTime)
    {
        if (Time.time >= impactTime) return;

        zoomImpactTime = impactTime;
        zoomRampStart = Mathf.Max(impactTime - zoomLeadTime, Time.time);
        hasZoom = true;
    }

    private void UpdateZoom()
    {
        if (!hasZoom)
        {
            PromotePendingZoom();
            return;
        }

        float t = Time.time;
        if (t < zoomRampStart) return; // 예약만 돼 있고 아직 조이기 전 — 화각은 휴지값 그대로다.

        if (t < zoomImpactTime)
        {
            float span = zoomImpactTime - zoomRampStart;
            float k = span > 0f ? (t - zoomRampStart) / span : 1f;
            zoomOffset = zoomFovDelta * zoomEase.Evaluate(k);
        }
        else if (zoomReleaseDuration > 0f && t < zoomImpactTime + zoomReleaseDuration)
        {
            zoomOffset = zoomFovDelta * (1f - (t - zoomImpactTime) / zoomReleaseDuration);
        }
        else
        {
            zoomOffset = 0f;
            hasZoom = false;
        }

        ApplyLens();

        if (!hasZoom) PromotePendingZoom();
    }

    private void PromotePendingZoom()
    {
        if (!hasPendingZoom) return;

        hasPendingZoom = false;
        BeginZoom(pendingZoomImpact);
    }

    private void StopZoom()
    {
        hasZoom = false;
        hasPendingZoom = false;
        zoomOffset = 0f;
        ApplyLens();
    }

    /// <summary>
    /// 줌이 <b>화면에 보이는 중</b>인가. 예약만 돼 있고 아직 조이기 전이면 false다 —
    /// <c>hasZoom</c>을 그대로 쓰면 예약 구간까지 잠겨 앵글 교체가 발사될 창이 사라진다
    /// (패턴 간격 0.4초 &lt; 리드타임 + 줌아웃).
    /// </summary>
    private bool IsZoomActive => hasZoom && Time.time >= zoomRampStart;

    // ── 프레이밍 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 교전 상대를 구도에 <b>얼마나</b> 담을지 매 프레임 정한다.
    ///
    /// <para><b>"적이 있다/없다"를 이진값으로 두지 않는다.</b> 처치 즉시 다음 상대가 승격되는데
    /// 그 적은 아직 링(6m) 위에 있어서, 이진값이면 카메라가 확 물러났다가 적이 달려오며 다시 붙는다.
    /// 거리의 함수로 두면 멀리서 오는 적이 화면에 서서히 자리를 만들고, 링에 흩어진 배경 적들은
    /// 애초에 구도에 개입하지 않는다. 요구("있으면 함께, 없으면 플레이어만")를 만족하면서
    /// 승격 구간 문제를 같은 식 하나로 흡수한다.</para>
    ///
    /// <para><b>⚠ 카메라 방향은 플레이어의 회전 속도를 복사하지 않는다.</b>
    /// <c>PlayerCombatMover.turnDuration</c>은 0.15초다 — 상대를 먼저 보고 달리기 위한 의도된 날카로움이라
    /// 캐릭터에는 옳지만, 카메라가 그 각속도를 그대로 따르면 상대가 무대 반대편으로 바뀔 때
    /// <b>화면 전체가 0.15초에 반 바퀴 돈다.</b> 그래서 <see cref="cameraTurnDamping"/>이라는
    /// 자기 시간상수로 뒤따르고, 그 지연 자체가 "플레이어가 먼저 돌고 카메라가 따라붙는" 연출이 된다.</para>
    ///
    /// <para><c>SmoothDampAngle</c>을 쓰는 이유는 둘이다 — 각도 랩어라운드(359°→1°)를 알아서 처리하고,
    /// 속도를 들고 있어 감쇠 도중 목표가 또 바뀌어도(연속 처치) 이어진다.</para>
    /// </summary>
    private void UpdateFraming()
    {
        if (!framingEnabled) return;

        float target = 0f;
        if (opponentTransform != null && opponentTransform.gameObject.activeInHierarchy)
        {
            // 높이는 무시한다 — 구도 판단은 평면 거리의 문제다.
            float distance = Vector3.ProjectOnPlane(
                opponentTransform.position - actionPlayer.transform.position, Vector3.up).magnitude;

            target = 1f - Mathf.Clamp01((distance - fullFrameDistance) / (dropoffDistance - fullFrameDistance));
        }

        opponentWeight = weightDamping > 0f
            ? Mathf.Lerp(opponentWeight, target, 1f - Mathf.Exp(-Time.deltaTime / weightDamping))
            : target;

        // yaw만 가져온다. 플레이어는 지금 평면 회전만 하지만, 훗날 피격 리액션 등으로 기울면
        // 회전을 통째로 복사한 궤도가 지면을 뚫거나 하늘로 솟는다.
        float targetYaw = actionPlayer.transform.eulerAngles.y;

        cameraYaw = cameraTurnDamping > 0f
            ? Mathf.SmoothDampAngle(cameraYaw, targetYaw, ref cameraYawVelocity, cameraTurnDamping)
            : targetYaw;

        ApplyFraming(opponentTransform, opponentWeight, cameraYaw);
    }

    /// <summary>
    /// 그룹을 실제로 갱신하는 <b>유일한 지점</b> — 멤버(무엇을 담을지)와 회전(어느 방향에서 담을지) 둘 다.
    /// <see cref="ApplyShake"/>와 같은 규율이라 프레이밍 방식을 바꿔도 위층(거리·각도 계산)은 그대로 남는다.
    ///
    /// <para><b>회전이 카메라를 플레이어 등 뒤로 돌리는 장치다.</b> 그룹은 <c>RotationMode = Manual</c>이라
    /// 그룹 회전 = 이 GameObject의 <c>transform.rotation</c>이고, vcam의 <c>BindingMode = LockToTarget</c>이라
    /// <c>FollowOffset</c>이 그 로컬 축으로 해석된다 → 여기를 돌리면 카메라 궤도가 통째로 따라 돈다.
    /// <c>GroupFraming</c>(바운드→돌리)과 <c>RotationComposer</c>(그룹을 바라보기)는 이 축과 무관해 그대로 산다.</para>
    ///
    /// <para>⚠ <b>그룹을 <c>GroupAverage</c>로 바꾸면 안 된다</b> — 회전이 멤버 배치에서 파생돼
    /// 구도가 적을 따라 돌고, 상대 가중치가 0인 구간에서는 정의되지 않아 튄다.</para>
    /// </summary>
    private void ApplyFraming(Transform opponent, float weight, float yaw)
    {
        if (targetGroup == null || targetGroup.Targets.Count < 2) return;

        var slot = targetGroup.Targets[1];
        slot.Object = opponent;
        slot.Weight = weight;

        targetGroup.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void UpdateShake()
    {
        if (shakeDuration <= 0f) return;

        float t = (Time.time - shakeStartTime) / shakeDuration;
        if (t >= 1f)
        {
            StopShake();
            return;
        }

        ApplyShake(shakeAmplitude * Decay(t), shakeFrequency * Decay(t));
    }

    /// <summary>1 → 0 감쇠. 제곱이라 끝에서 부드럽게 잦아든다.</summary>
    private static float Decay(float t)
    {
        float inv = 1f - t;
        return inv * inv;
    }

    // ── 큐 재생 ──────────────────────────────────────────────────────────────

    private void PlayCue(CameraTrigger trigger)
    {
        if (!catalogByTrigger.TryGetValue(trigger, out var cue)) return; // 카탈로그에 없으면 무연출

        // 잠금 중에는 내지 않고 미룬다 — 요구가 "멈춘 다음에 연출"이다.
        if (IsHolding)
        {
            hasDeferredCue = true;
            deferredCue = trigger;
            return;
        }

        lastCueTrigger = trigger;
        PlayPunch(cue);

        if (!shakeEnabled) return;
        if (cue.shakeDuration <= 0f) return;

        // 겹침: 진행 중인 쉐이크의 남은 진폭과 비교해 큰 쪽을 취한다(세기 낙차 방지).
        float remaining = 0f;
        if (shakeDuration > 0f)
        {
            float t = Mathf.Clamp01((Time.time - shakeStartTime) / shakeDuration);
            remaining = shakeAmplitude * Decay(t);
        }

        shakeAmplitude = Mathf.Max(remaining, cue.shakeAmplitude);
        shakeDuration = cue.shakeDuration;
        shakeStartTime = Time.time;
    }

    /// <summary>
    /// 펀치를 시작한다. 겹침 규칙은 쉐이크와 같다 — 타이머는 재시작하되 <b>세기는 큰 쪽을 취한다</b>
    /// (덮어쓰면 강한 펀치 도중 약한 펀치가 들어와 낙차가 생긴다). 부호가 섞이면 절댓값으로 비교한다.
    /// </summary>
    private void PlayPunch(CameraCueEntry cue)
    {
        if (!punchEnabled) return;
        if (cue.punchDuration <= 0f || cue.punchFovDelta == 0f) return;

        float remaining = 0f;
        if (punchDuration > 0f)
        {
            float t = Mathf.Clamp01((Time.time - punchStartTime) / punchDuration);
            remaining = punchDelta * Decay(t);
        }

        punchDelta = Mathf.Abs(remaining) > Mathf.Abs(cue.punchFovDelta) ? remaining : cue.punchFovDelta;
        punchDuration = cue.punchDuration;
        punchStartTime = Time.time;
    }

    private void StopShake()
    {
        shakeDuration = 0f;
        shakeAmplitude = 0f;
        ApplyShake(0f, 0f);
    }

    /// <summary>
    /// 렌즈를 실제로 만지는 <b>유일한 지점</b>. <see cref="ApplyShake"/>·<see cref="ApplyFraming"/>·
    /// <see cref="IntroRoutine"/>과 함께 Cinemachine 타입이 등장하는 네 곳 중 하나다.
    ///
    /// <para><b>오프셋을 합산해서 쓴다.</b> FOV를 미는 소비자가 둘(펀치·줌)이라, 각자 렌즈에 직접 쓰면
    /// 나중에 쓴 쪽이 앞의 것을 지운다 — 둘은 임팩트 순간에 동시에 살아 있다.</para>
    /// </summary>
    private void ApplyLens()
    {
        var cam = ResolveEffectCamera();
        if (cam == null) return;

        var idle = GetIdle(cam);
        var lens = cam.Lens;
        lens.FieldOfView = idle.fov + punchOffset + zoomOffset;
        cam.Lens = lens;
    }

    // ── 연출 대상 vcam 조회 ──────────────────────────────────────────────────

    /// <summary>
    /// 쉐이크·펀치를 걸 <b>지금 화면에 나오는 vcam</b>을 찾는다.
    ///
    /// <para>앵글 교체(<c>CameraAngleSwitcher</c>)를 쓰면 live vcam이 곡 도중 바뀐다. 예전처럼 특정 vcam을
    /// 하드와이어로 잡고 있으면 <b>다른 앵글이 올라온 순간 타격감 연출이 통째로 사라진다</b> —
    /// 화면에 안 나오는 카메라를 흔들고 있기 때문이다.</para>
    ///
    /// <para><b>대상이 바뀌면 이전 vcam을 반드시 휴지값으로 되돌린다.</b> 안 그러면 흔들리던 상태로 굳은 채
    /// 대기열에 남았다가 다음에 올라올 때 그 값으로 등장한다.</para>
    ///
    /// <para>Brain이 없거나 live를 못 찾으면 <see cref="gameplayCamera"/>로 폴백한다 — 앵글 교체를 안 쓰는
    /// 씬에서는 예전과 완전히 같은 동작이다.</para>
    /// </summary>
    private CinemachineCamera ResolveEffectCamera()
    {
        CinemachineCamera cam = null;

        if (brain != null)
            cam = brain.ActiveVirtualCamera as CinemachineCamera;

        if (cam == null) cam = gameplayCamera;

        if (cam != effectCamera)
        {
            RestoreIdle(effectCamera);
            effectCamera = cam;
        }

        return cam;
    }

    /// <summary>vcam의 휴지값을 처음 볼 때 캐시한다. 씬에 적힌 값이 진실의 원천이다(쉐이크·펀치 공통 규율).</summary>
    private CameraIdle GetIdle(CinemachineCamera cam)
    {
        if (idleByCamera.TryGetValue(cam, out var idle)) return idle;

        idle = new CameraIdle
        {
            perlin = cam.GetComponent<CinemachineBasicMultiChannelPerlin>(),
            fov = cam.Lens.FieldOfView,
        };

        if (idle.perlin != null)
        {
            idle.amplitude = idle.perlin.AmplitudeGain;
            idle.frequency = idle.perlin.FrequencyGain;
        }

        idleByCamera[cam] = idle;
        return idle;
    }

    /// <summary>연출이 걸려 있던 vcam을 씬 값으로 되돌린다. 대상 교체와 종료 양쪽에서 쓰인다.</summary>
    private void RestoreIdle(CinemachineCamera cam)
    {
        if (cam == null || !idleByCamera.TryGetValue(cam, out var idle)) return;

        if (idle.perlin != null)
        {
            idle.perlin.AmplitudeGain = idle.amplitude;
            idle.perlin.FrequencyGain = idle.frequency;
        }

        var lens = cam.Lens;
        lens.FieldOfView = idle.fov;
        cam.Lens = lens;
    }

    /// <summary>
    /// 진행 중인 카메라 큐가 있는가. 앵글 교체가 <b>쉐이크·펀치·줌이 끝난 뒤에</b> 발사되도록 판단 근거를 준다.
    /// 블렌드(0.4초)가 줌 램프와 겹치면 화면이 뭉개진다.
    /// </summary>
    public bool IsCuePlaying => shakeDuration > 0f || punchDuration > 0f || IsZoomActive;

    // ── 인트로 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 곡 시작 전 인트로. <b>두 도막</b>이다 — ① 씬의 스플라인을 달리고, ② Brain이 게임플레이 구도로 블렌드한다.
    ///
    /// <para><b>경로는 코드가 모른다.</b> 좌표·높이·곡률·시작 각도는 전부 씬의 <c>SplineContainer</c>가 갖고 있고,
    /// 여기서 미는 것은 진행도 0→1 하나뿐이다(<c>PositionUnits = Normalized</c> 전제).
    /// 그래서 경로를 다시 그려도 이 코드는 바뀌지 않는다. 코드가 주는 것은 <b>속도 배분</b>(<see cref="introEase"/>)뿐이다 —
    /// 모양과 시간 배분은 층이 다르다.</para>
    ///
    /// <para><b>스플라인 끝점을 게임플레이 구도에 정확히 맞출 의무가 없다.</b> 남은 차이는 ②의 블렌드가 흡수한다.</para>
    ///
    /// <para>블렌드 시간은 <see cref="CinemachineBrain.DefaultBlend"/>에서 <b>읽는다</b>. 인스펙터에 같은 값을 두 번 적으면
    /// 언젠가 한쪽만 고쳐져 "곡은 시작됐는데 카메라가 아직 움직이는" 상태가 된다. 진실의 원천은 Brain 하나다.</para>
    /// </summary>
    private IEnumerator IntroRoutine(float duration)
    {
        introCamera.Priority = new PrioritySettings { Enabled = true, Value = introPriority };
        introDolly.CameraPosition = 0f;

        // 곡 시작을 늦출 수는 없으므로, 블렌드가 창을 다 먹으면 인트로가 잘리는 쪽을 택한다.
        float travel = duration - brain.DefaultBlend.BlendTime;
        if (travel <= 0f)
        {
            Debug.LogWarning($"[CameraDirector] 블렌드 시간({brain.DefaultBlend.BlendTime}초)이 카운트다운({duration}초)을 " +
                             "다 써서 스플라인 주행을 생략합니다. Brain의 DefaultBlend를 줄이세요.", this);
        }
        else
        {
            for (float elapsed = 0f; elapsed < travel; elapsed += Time.deltaTime)
            {
                introDolly.CameraPosition = introEase.Evaluate(elapsed / travel);
                yield return null;
            }
        }

        introDolly.CameraPosition = 1f; // 마지막 프레임이 1에 못 미쳐도 끝점에서 출발하게 확정한다.
        introRoutine = null;
        RestoreIntroPriority();
    }

    private void StopIntro()
    {
        if (introRoutine != null)
        {
            StopCoroutine(introRoutine);
            introRoutine = null;
        }

        RestoreIntroPriority();
    }

    private void RestoreIntroPriority()
    {
        if (introCamera != null)
            introCamera.Priority = new PrioritySettings { Enabled = true, Value = introRestingPriority };
    }

    /// <summary>
    /// 쉐이크를 실제로 카메라에 적용하는 <b>유일한 지점</b>. <see cref="ApplyFraming"/>·<see cref="IntroRoutine"/>과 함께
    /// Cinemachine 타입이 등장하는 세 곳 중 하나라, 나중에 Impulse로 갈아끼울 때 위층(트리거·카탈로그·타이밍)은 그대로 둘 수 있다.
    /// </summary>
    private void ApplyShake(float amplitude, float frequency)
    {
        var cam = ResolveEffectCamera();
        if (cam == null) return;

        var idle = GetIdle(cam);
        if (idle.perlin == null) return; // 이 앵글엔 노이즈가 없다 — 쉐이크만 조용히 빠진다.

        idle.perlin.AmplitudeGain = idle.amplitude + amplitude;
        idle.perlin.FrequencyGain = idle.frequency + frequency;
    }
}
