using EnemySpace;
using UnityEngine;

/// <summary>
/// 튜토리얼 씬 전용 글루 셋 — <b>무대 준비 · 제자리 달리기 · 첫 노드 슬로우</b>.
/// 시퀀스가 <c>TutorialCueStep</c>으로 켜고, 이 클래스는 그 상태를 매 프레임 유지한다.
///
/// <para><b>셋을 한 클래스에 두는 이유</b>: 전부 <b>이 씬에서만 참인 반칙</b>이다.
/// <c>Time.timeScale</c>은 CLAUDE.md §7-3이 곡이 도는 씬에서 금지하고, 제자리 달리기는
/// 위치의 주인이 아무도 없는 씬에서만 성립한다. 흩어 두면 전투 씬에 실려 갈 자리가 생긴다.</para>
///
/// <para><b>⚠ <c>Time.timeScale</c>을 건드리는 것이 이 씬에서는 이 클래스뿐이다.</b>
/// §7-3의 금지 근거는 <i>"판정·클립 정렬은 <c>Time.time</c>인데 채보는 <c>audioSource.time</c>으로 돌고
/// 오디오는 timeScale 밖이라 차이가 영구 누적된다"</i>인데, <b>튜토리얼 씬에는 그 오디오 시계가 없다</b> —
/// <c>ChartPlayer</c>가 꺼져 있고 드릴은 <c>Time.time</c>으로만 시각을 잡는다(<c>PatternDrillStep.Feed</c>).
/// 시계가 하나뿐이라 어긋날 곳이 없다. §14(<c>FinaleSilhouetteDirector</c>)가 쓰는 것과 같은 경계다 —
/// <b>규칙은 "두 시계가 어긋날 수 있는 동안 금지"이지 "언제나 금지"가 아니다.</b>
/// 곡이 도는 씬으로 이 컴포넌트를 옮기면 그 순간 위반이 된다.</para>
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("허물을 세우는 곳. 비우면 무대 준비만 조용히 비활성된다.")]
    [SerializeField] private EnemyDirector enemyDirector;

    [Tooltip("제자리 달리기의 애니메이터 주인. 비우면 달리기만 조용히 비활성된다.")]
    [SerializeField] private CharacterActionPlayer actionPlayer;

    [Tooltip("기습을 지정한 드릴에서만 내보낼 때 쓴다. 그쪽 requireArm이 켜져 있어야 뜻이 있다. " +
             "비우면 기습 무장만 조용히 비활성된다.")]
    [SerializeField] private DodgeDirector dodgeDirector;

    [Tooltip("마지막 드릴을 곡의 마지막 패턴처럼 취급해 마무리 실루엣을 터뜨릴 때 쓴다. " +
             "비우면 마무리 실루엣만 조용히 비활성된다.")]
    [SerializeField] private FinaleSilhouetteDirector finaleDirector;

    [Tooltip("이 무대가 평생 스폰할 허물의 총수. 드릴이 내는 공격 패턴 수와 같아야 한다 - " +
             "패턴 하나당 처치 하나이므로, 그래야 마지막 베기가 마지막 허물이 되고 잔여가 0이 된다. " +
             "기본 7 = 1 + 1 + 사슬 4 + 연타 1. 드릴을 늘리거나 줄이면 이 값도 같이 고친다. " +
             "0 이하면 무제한(기존 동작 - 곡은 끝까지 사망 1 : 스폰 1로 보충한다).")]
    [SerializeField] private int spawnBudget = 7;

    [Header("Run In Place")]
    [Tooltip("달리기 클립에 넘길 이동 속도(m/s). exploreRunReferenceSpeed(4.5)와 맞춰 두면 발이 안 미끄러진다. " +
             "⚠ 위치는 한 밀리미터도 옮기지 않는다 - 달려가는 느낌은 카메라 구도가 만든다.")]
    [Min(0.1f)]
    [SerializeField] private float runSpeed = 4.5f;

    [Tooltip("제자리 달리기 동안 뒤로 흘려보낼 배경 루트(Map/RunBackdrop). 비우면 배경이 서 있는다. " +
             "⚠ 움직이는 것은 배경이지 플레이어가 아니다. ⚠ 여기에 Stage나 바닥을 넣으면 무대가 통째로 흘러간다.")]
    [SerializeField] private Transform scrollRoot;

    [Tooltip("배경의 반복 주기(m). 이만큼 흐르면 그 거리만큼 되돌려 순환시킨다 - " +
             "배경이 이 길이로 <b>똑같이 반복</b>돼 있어야 되돌리는 순간이 안 보인다. 0이면 순환 없이 계속 흘러간다.")]
    [Min(0f)]
    [SerializeField] private float scrollLoopLength = 7f;

    [Header("Run To")]
    [Tooltip("실제로 달려갈 목적지. 무대 중심(Stage)을 배선한다 - 도착 지점이 곧 허물이 서 있는 자리다.")]
    [SerializeField] private Transform runDestination;

    [Tooltip("이 거리 안에 들면 도착으로 본다.")]
    [Min(0.05f)]
    [SerializeField] private float arriveRadius = 0.3f;

    [Tooltip("목적지를 향해 도는 데 걸리는 시간(초).")]
    [Min(0.01f)]
    [SerializeField] private float turnDuration = 0.25f;

    [Tooltip("달리는 동안 미오를 놓치지 않게 뒤따라올 카메라(Cam_Call). 비우면 제자리에 선 채로 멀어진다.")]
    [SerializeField] private Transform runCamera;

    [Tooltip("이 거리(m)까지는 카메라가 제자리에 서서 미오가 멀어지는 것을 보여 주고, " +
             "그보다 벌어지면 그 거리를 유지하며 따라간다. 높이는 안 건드린다.")]
    [Min(1f)]
    [SerializeField] private float runCameraMaxDistance = 9f;

    [Header("Slow Motion")]
    [Tooltip("첫 노드에서 내려갈 시간 배율.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float slowScale = 0.25f;

    [Tooltip("첫 노드 입력 시각 이 만큼 앞에서 감속을 시작한다(초).")]
    [Min(0f)]
    [SerializeField] private float slowLead = 0.35f;

    [Tooltip("안 눌렀을 때 원래 속도로 돌아오기까지의 여유(초). 슬로우는 보험이지 감옥이 아니다.")]
    [Min(0f)]
    [SerializeField] private float slowRelease = 0.2f;

    private bool running;
    private bool moving;
    private float scrolled;
    private bool staged;

    private bool armed;
    private bool hasNode;
    private float nodeTime;
    private bool slowed;

    void OnEnable()
    {
        if (handler == null) return;

        handler.OnJudgeTargetBegan += HandleJudgeTargetBegan;
        handler.OnNodeConnected += HandleNodeConnected;
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnJudgeTargetBegan -= HandleJudgeTargetBegan;
            handler.OnNodeConnected -= HandleNodeConnected;
        }

        // ⚠ 복구 경로 셋 중 하나. 빠지면 게임이 슬로우로 굳는다.
        RestoreTimeScale();
    }

    void Update()
    {
        // ⚠ 매 프레임 불러야 한다 - 이 메서드가 convergeUntil 래치를 갱신해 base 레이어의 주인을 쥔다.
        // 호출이 멎으면 래치가 만료되며 저절로 Idle로 돌아간다(그래서 "멈춰라"를 따로 구현하지 않는다).
        if (running && actionPlayer != null) actionPlayer.SetExploreLocomotion(runSpeed, true);
        // ⚠ 배경은 <b>제자리 달리기 구간에만</b> 흐른다. 실제로 이동하는 동안에도 흘리면
        // 세상이 두 번 움직여 속도가 배로 보인다 - 대사가 끝나 RunTo가 시작되면 골목이 멎는다.
        if (running && !moving) TickScroll();
        if (moving) TickRunTo();

        TickSlowMo();
    }

    // ── 무대 준비 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 허물을 세운다. <b>⚠ 프리웜(<c>Instantiate</c>)이 여기서 일어나므로 전투가 아니라 대사 구간에서 부른다</b> —
    /// 곡 도중의 히치는 그대로 판정 손실이다(CLAUDE.md §5).
    /// </summary>
    public void PrepareStage(int clusterSize)
    {
        if (enemyDirector == null) return;

        // ⚠ EnemyDirector.PrepareStage는 멱등이 아니다 - 무리 리스트만 비우고 이전 적을 풀에 안 돌려주며
        // ring은 계속 늘어난다. 그래서 두 번 부르면 <b>무대의 적이 그대로 두 배가 되고</b>
        // 앞의 무리는 아무 목록에도 없는 채로 서 있는다. 한 번만 세운다.
        if (staged) return;
        staged = true;

        // 무리는 <b>EnemyDirector의 무대 중심</b>을 기준으로 선다(EnemyDirector.PrepareStage).
        // 그래서 미오가 아직 골목에 있는 이 시점에 불러도 허물은 도착 지점에 모인다 —
        // 예전에는 기준이 플레이어라 플레이어를 한 프레임 순간이동시켜 우회했지만,
        // runDestination이 비면 그 우회가 통째로 걸리지 않아 코앞에 무리가 섰다.

        // ⚠ 순서가 계약이다 - 프리웜과 무리 배치가 이 값에서 파생되므로 뒤에 바꾸면 인원과 배치가 어긋난다.
        enemyDirector.SetClusterSize(clusterSize);
        enemyDirector.SetSpawnBudget(spawnBudget > 0 ? spawnBudget : -1);
        enemyDirector.PrepareStage();

        // ⚠ 세워 두되 재운다. EnemyDirector.TickWander가 <b>플레이어 위치를 중심으로</b> 궤도 슬롯을
        // 매 프레임 나눠 주므로(standoffDistance), 그냥 두면 허물이 25m 밖의 미오에게 걸어온다.
        // 컴포넌트를 끄면 슬롯 배정이 멎어 <b>배회가 아예 시작되지 않고</b>(wandering이 false인 채로 남는다),
        // EnemyView는 자기 Update로 계속 도므로 <b>등장 이동(walk-in)은 그대로 마친 뒤 그 자리에 선다</b>.
        // 도착하면 SetRunning(false)가 깨운다.
        enemyDirector.enabled = false;
    }

    // ── 제자리 달리기 ────────────────────────────────────────────────────────

    /// <summary>
    /// 달리는 자세를 켜고 끈다. <b>대사 구간에는 안 쓴다</b> — 미오는 서서 전화를 받는다.
    /// 제자리 달리기는 <b>다리만 움직이고 세상이 서 있어</b> 달리는 것으로 안 읽혔고,
    /// 실제 이동(<see cref="RunTo"/>)이 그 자리를 대신한다.
    /// </summary>
    public void SetRunning(bool on)
    {
        running = on;

        // ⚠ 켜는 프레임에 <b>블렌드 없이</b> 한 번 더 부른다. 안 그러면 애니메이터 기본 스테이트(Katana_Idle)에서
        // baseCrossFadeDuration만큼 Idle이 비쳐, 시작하자마자 달리는 그림이 안 된다.
        if (on)
        {
            if (actionPlayer != null) actionPlayer.SetExploreLocomotion(runSpeed, true, true);
            return;
        }

        moving = false;

        // 도착했다 - 재워 둔 무대를 깨운다(위 PrepareStage 참조). 이제 배회가 미오를 중심으로 돌아도 맞다.
        if (enemyDirector != null) enemyDirector.enabled = true;
    }

    /// <summary>
    /// 제자리 달리기를 <b>실제 이동으로 바꾼다</b>. 대사가 끝나고 미오가 무대로 달려가는 구간이다.
    ///
    /// <para><b>⚠ 여기서 플레이어의 <c>transform</c>을 옮기는 것이 안전한 이유</b>: 이 구간에는 패턴이 하나도
    /// 큐에 없어 <c>OnDuelScheduled</c>가 안 나고, 따라서 <c>PlayerCombatMover</c>도 거리 커브도
    /// 한 프레임도 위치를 안 쓴다(CLAUDE.md §11-2 — 위치의 주인이 하나뿐이다).
    /// <c>PatternDrillStep</c>이 다다미로 걸어갈 때 쓰던 것과 같은 근거다.</para>
    /// </summary>
    public void RunTo()
    {
        running = true;
        moving = runDestination != null && actionPlayer != null;
    }

    /// <summary>목적지에 닿았는가. <c>TutorialCueStep</c>의 <c>RunTo</c>가 이걸로 기다린다.</summary>
    public bool RunArrived => !moving;

    /// <summary>
    /// 배경을 플레이어 뒤쪽으로 흘려보낸다. <b>제자리 달리기의 나머지 절반</b>이다 —
    /// 다리만 움직이고 세상이 서 있으면 달리는 것으로 안 읽힌다.
    ///
    /// <para><b>⚠ 미는 것은 배경이다.</b> 플레이어를 옮기면 위치의 주인이 생겨 결투 계획·거리 커브와 싸운다(§11-2).</para>
    ///
    /// <para><b>⚠ 순환은 배경이 <see cref="scrollLoopLength"/> 주기로 똑같이 반복돼 있을 때만 안 보인다.</b>
    /// 그래서 오프셋이 언제나 (−L, 0]에 머무르고, 대사가 언제 끝나든 골목이 최대 L만큼만 뒤로 밀려 있다.</para>
    /// </summary>
    private void TickScroll()
    {
        if (scrollRoot == null || actionPlayer == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(actionPlayer.transform.forward, Vector3.up);
        if (forward.sqrMagnitude <= 1e-6f) return;

        forward.Normalize();

        float step = runSpeed * Time.deltaTime;
        scrollRoot.position -= forward * step;
        scrolled += step;

        if (scrollLoopLength <= 0f || scrolled < scrollLoopLength) return;

        scrolled -= scrollLoopLength;
        scrollRoot.position += forward * scrollLoopLength;
    }

    private void TickRunTo()
    {
        Transform player = actionPlayer.transform;

        Vector3 toTarget = Vector3.ProjectOnPlane(runDestination.position - player.position, Vector3.up);
        float distance = toTarget.magnitude;

        if (distance <= arriveRadius)
        {
            moving = false;
            return;
        }

        Vector3 direction = toTarget / distance;
        player.position += direction * Mathf.Min(runSpeed * Time.deltaTime, distance);
        player.rotation = Quaternion.Slerp(player.rotation, Quaternion.LookRotation(direction),
                                           Mathf.Clamp01(Time.deltaTime / turnDuration));

        TrailCamera(player.position);
    }

    /// <summary>
    /// 카메라를 <b>고삐로만</b> 끈다 — <see cref="runCameraMaxDistance"/>까지는 제자리에 서서 미오가 멀어지는 것을
    /// 보여 주고, 그보다 벌어지면 딱 그 거리를 유지하며 뒤따른다.
    ///
    /// <para><b>왜 <c>CinemachineFollow</c>가 아닌가</b>: Follow를 붙이면 <b>처음부터</b> 따라붙어
    /// "제자리에 선 카메라를 두고 달려 나간다"는 그림이 아예 안 나온다. 조준은 vcam의
    /// <c>RotationComposer</c>(LookAt = 플레이어)가 이미 하므로 여기서는 위치만 민다.</para>
    ///
    /// <para><b>⚠ 높이는 안 건드린다.</b> y를 같이 따라가게 하면 지면 기복이 그대로 카메라에 실린다.</para>
    /// </summary>
    private void TrailCamera(Vector3 playerPosition)
    {
        if (runCamera == null) return;

        Vector3 flat = playerPosition - runCamera.position;
        flat.y = 0f;

        float distance = flat.magnitude;
        if (distance <= runCameraMaxDistance) return;

        runCamera.position += flat / distance * (distance - runCameraMaxDistance);
    }

    // ── 기습 무장 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 다음 기습 하나를 무장한다. <b>어느 드릴에서 기습이 오는지를 아는 것은 시퀀스뿐</b>이라
    /// 무장을 여기서 건다(첫 노드 슬로우와 같은 근거).
    /// </summary>
    public void ArmAmbush() => dodgeDirector?.ArmNext();

    // ── 마무리 실루엣 ────────────────────────────────────────────────────────

    /// <summary>
    /// 다음 성공 패턴을 곡의 마지막처럼 취급하게 무장한다(<see cref="ArmAmbush"/>와 같은 관용구).
    /// <b>"어느 드릴이 마지막인가"를 아는 것은 시퀀스뿐</b>이라 무장을 여기서 건다.
    /// </summary>
    public void ArmFinale() => finaleDirector?.ArmNextSuccess();

    /// <summary>
    /// 실루엣이 걷힐 때까지 기다려야 하는 스텝(<c>ScreenFadeStep</c>)의 통로.
    /// 시퀀스에 슬롯을 하나 더 뚫는 대신 이 글루를 거친다 — 스텝은 이미 이 슬롯을 들고 있다.
    /// </summary>
    public FinaleSilhouetteDirector Finale => finaleDirector;

    // ── 첫 노드 슬로우 ───────────────────────────────────────────────────────

    /// <summary>
    /// <b>다음</b> 판정 대상의 첫 노드에서 한 번만 감속한다. 무장을 드릴이 거는 이유는
    /// <b>"드릴의 첫 노드"를 아는 것이 드릴뿐</b>이기 때문 — 사슬 드릴은 패턴 넷이 각각 판정 대상이 되므로
    /// 이벤트만 보면 넷 다 느려진다.
    /// </summary>
    public void ArmSlowMo()
    {
        armed = true;
        hasNode = false;
    }

    private void HandleJudgeTargetBegan(PatternSpace.JudgeTargetInfo info)
    {
        if (!armed) return;

        armed = false;
        hasNode = true;
        nodeTime = info.FirstNodeTime;
    }

    private void HandleNodeConnected(int index, Vector3 world)
    {
        // 복구 경로 ① - 그 노드를 눌렀다.
        if (!hasNode) return;

        hasNode = false;
        RestoreTimeScale();
    }

    private void TickSlowMo()
    {
        if (!hasNode)
        {
            RestoreTimeScale();
            return;
        }

        // 복구 경로 ② - 창을 지났다. 안 눌렀어도 원래 속도로 돌아온다.
        if (Time.time > nodeTime + slowRelease)
        {
            hasNode = false;
            RestoreTimeScale();
            return;
        }

        if (Time.time < nodeTime - slowLead) return;

        if (!slowed)
        {
            slowed = true;
            Time.timeScale = slowScale;
        }
    }

    private void RestoreTimeScale()
    {
        if (!slowed) return;

        slowed = false;
        Time.timeScale = 1f;
    }
}
