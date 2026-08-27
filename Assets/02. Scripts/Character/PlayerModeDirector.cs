using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 플레이어가 지금 무엇을 하는 중인가. <b>대사는 여기 없다</b> —
/// 카시마의 브리핑은 통로 옆에 서서 한 줄 하고 물러나는 것이 전부이고 이동을 멈추지 않으므로
/// (<c>docs/Story/Story_Script.md</c> §2) 탐색 위에 얹히는 오버레이이지 모드가 아니다.
/// </summary>
public enum PlayerMode
{
    /// <summary>심상세계를 걸어 다닌다. <see cref="PlayerExploreMover"/>가 위치의 주인.</summary>
    Explore,

    /// <summary>무대 위 결투. <see cref="PlayerCombatMover"/>가 위치의 주인.</summary>
    Combat,

    /// <summary>엔딩 샷·몽타주. 아무도 위치를 안 건드린다(카메라·타임라인이 몬다).</summary>
    Cutscene,

    /// <summary>결과·파편 화면. 아무도 위치를 안 건드린다.</summary>
    Overlay,
}

/// <summary>
/// <b>플레이어 모드의 유일 관리 지점.</b> 위치의 주인이 언제나 정확히 하나임을 보장한다 —
/// CLAUDE.md §11-2가 이 개편의 <b>유일한 위험 지점</b>으로 지목한 그 배타성이다.
///
/// <para><b>왜 <c>IState</c>/<c>StateMachine</c>이 아닌가</b>: 네 모드 중 자기 매 프레임 로직을 가진 것은
/// 둘뿐이고, 그 둘은 이미 별개 컴포넌트(<see cref="PlayerExploreMover"/> · <see cref="PlayerCombatMover"/>)라
/// 상태 객체 둘이 빈 껍데기가 된다. 불법 전이도 없다(전이를 부르는 쪽이 하나다).
/// 프로젝트 관용구도 같은 쪽이다 — <c>EnemyView</c>가 6단계를 <c>enum Phase</c> + 가드로 굴린다.</para>
///
/// <para><b>⚠ 그리고 FSM의 <c>OnEnter</c>/<c>OnExit</c> 쌍이 바로 이 기능의 버그가 사는 자리다.</b>
/// 노브를 하나 늘리면 진입 넷에는 넣고 이탈 하나를 빠뜨린다 — 증상은 "컷신이 끝났는데 HUD가 안 돌아온다"처럼
/// <b>특정 전이 경로에서만</b> 난다. 그래서 여기서는 반대 규율을 쓴다:
/// <see cref="ApplyMode"/> 하나가 <b>모든 전이에서 모든 노브를 무조건 대입</b>한다.
/// 그러면 최종 상태가 출발지와 무관하게 목적지 하나로만 결정되어 그 버그가 표현 불가능해진다.</para>
///
/// <para><b>승격 조건</b>: <see cref="PlayerMode.Cutscene"/>이나 <see cref="PlayerMode.Overlay"/>가
/// 자기 매 프레임 로직을 갖게 되면 그때 <see cref="ApplyMode"/>를 상태 객체 디스패치로 바꾼다.
/// 전이 지점이 한 곳이라 그 교체는 국소적이다.</para>
/// </summary>
public class PlayerModeDirector : MonoBehaviour
{
    [SerializeField] private PlayerCombatMover combatMover;
    [SerializeField] private PlayerExploreMover exploreMover;
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("탐색 전용 vcam. 비우면 카메라는 그대로 두고 이동·입력만 갈린다.")]
    [SerializeField] private CinemachineVirtualCameraBase exploreCamera;

    [Tooltip("탐색 중 vcam 우선순위. 게임플레이 앵글(10)보다 높고 인트로(20)보다 낮을 이유가 없다.")]
    [SerializeField] private int explorePriority = 30;

    /// <summary>지금 모드.</summary>
    public PlayerMode Mode { get; private set; } = PlayerMode.Combat;

    /// <summary>직전 모드. 컷신·오버레이가 끝나고 되돌아갈 곳이다.</summary>
    public PlayerMode Previous { get; private set; } = PlayerMode.Combat;

    void Awake()
    {
        // 기본값은 전투다 — 모드를 아무도 안 바꾸는 씬은 예전과 똑같이 돈다(회귀 0).
        ApplyMode(PlayerMode.Combat);
    }

    /// <summary>모드를 바꾼다. 같은 모드로 다시 걸어도 노브는 전부 다시 대입된다(멱등).</summary>
    public void SetMode(PlayerMode mode)
    {
        if (mode != Mode) Previous = Mode;
        ApplyMode(mode);
    }

    /// <summary>컷신·오버레이가 끝나고 직전 모드로 되돌아간다.</summary>
    public void ReturnToPrevious() => SetMode(Previous);

    /// <summary>
    /// <b>모든 노브를 무조건 대입한다.</b> 조건 대입(<c>knob = (mode == ...)</c>)만 쓰고
    /// <c>if (mode == X) 켜기</c> 형태를 쓰지 않는다 — 그래야 어느 모드에서 왔든 결과가 같다.
    /// </summary>
    private void ApplyMode(PlayerMode mode)
    {
        Mode = mode;

        bool combat = mode == PlayerMode.Combat;
        bool explore = mode == PlayerMode.Explore;

        if (combatMover != null) combatMover.enabled = combat;
        if (exploreMover != null) exploreMover.enabled = explore;

        if (inputHandler != null)
            inputHandler.SetMode(combat ? PlayerInputMode.Combat : PlayerInputMode.Explore);

        if (exploreCamera != null)
            exploreCamera.Priority = new PrioritySettings { Enabled = true, Value = explore ? explorePriority : 0 };

        WarnIfBothMoversLive();
    }

    /// <summary>
    /// 위치의 주인이 둘이 된 상태를 즉시 잡는다. 이 상태의 증상은 결투 간격이 미묘하게 어긋나는 것이라
    /// <b>화면만 봐서는 원인을 못 짚는다</b> — 조용히 어긋나면 안 되는 종류다.
    /// </summary>
    private void WarnIfBothMoversLive()
    {
        if (combatMover == null || exploreMover == null) return;
        if (!combatMover.enabled || !exploreMover.enabled) return;

        Debug.LogError("[PlayerModeDirector] 전투 이동과 탐색 이동이 동시에 켜졌습니다 - " +
                       "플레이어 위치의 주인이 둘이면 결투 도착 시각과 거리 커브가 깨집니다.", this);
    }

    [ContextMenu("Set Mode / Explore")]
    private void DebugSetExplore() => SetMode(PlayerMode.Explore);

    [ContextMenu("Set Mode / Combat")]
    private void DebugSetCombat() => SetMode(PlayerMode.Combat);
}
