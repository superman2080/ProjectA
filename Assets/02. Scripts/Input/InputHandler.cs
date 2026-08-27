using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어 입력의 모드. <b>정확히 한 액션 맵만 켜진다</b>(<see cref="InputHandler.SetMode"/>).
///
/// <para>구독자마다 "탐색 중이면 무시" 가드를 다는 대신 맵 자체를 끈다 —
/// 탐색 중에는 노드 입력 이벤트가 <b>애초에 발행되지 않는다</b>.</para>
/// </summary>
public enum PlayerInputMode
{
    /// <summary>패턴 노드(1~9) + 회피(Space). 무대 위에서만 켜진다.</summary>
    Combat,

    /// <summary>이동(WASD) + 시점(마우스). 무대 밖에서만 켜진다.</summary>
    Explore,
}

/// <summary>
/// <b>게임플레이 입력의 유일한 출처.</b> <c>IngameInputs</c>(Input System 에셋)를 소유하고,
/// 원시 입력을 의미 있는 이벤트로만 밖에 내보낸다 — 노드 입력(1~9)과 회피(Space), 그리고 탐색 이동.
///
/// <para><b>단일 책임</b>: 여기는 "무슨 키가 눌렸나"만 안다. 그 입력이 판정인지 회피인지,
/// 창 안인지 밖인지는 구독자(<c>PatternHandler</c> · <c>DodgeDirector</c>)가 정한다.
/// 그래서 키 바인딩 변경은 <c>IngameInputs.inputactions</c> 한 곳에서 끝나고,
/// 구독자는 <c>UnityEngine.InputSystem</c>에 의존하지 않는다.</para>
///
/// <para><b>모드가 하나 더 있다</b>(<see cref="PlayerInputMode"/>). 탐색과 전투는 같은 키보드를 쓰지만
/// 같은 시각에 둘 다 유효해서는 안 된다 — 걷는 동안 숫자키가 판정으로 흘러 들어가면 안 되고,
/// 곡 도중 WASD가 플레이어를 무대 밖으로 끌고 나가면 위치의 주인이 둘이 된다(CLAUDE.md §11-2).
/// <b>그 배타성은 액션 맵이 표현한다</b> — Input System이 그러라고 있는 기능이다.</para>
/// </summary>
public class InputHandler : MonoBehaviour
{
    private const int PointCount = 9;

    private IngameInputs inputActions;

    /// <summary>패턴인풋 노드 입력. 인자는 인덱스(0~8). <c>Combat</c> 모드에서만 발행된다.</summary>
    public event Action<int> OnKeyPressed;

    /// <summary>회피 입력(Player/Dodge = <c>&lt;Keyboard&gt;/space</c>). <c>Combat</c> 모드에서만 발행된다.</summary>
    public event Action OnDodgePressed;

    /// <summary>지금 켜져 있는 맵.</summary>
    public PlayerInputMode Mode { get; private set; } = PlayerInputMode.Combat;

    /// <summary>
    /// 탐색 이동 입력(정규화 전 원시 Vector2). <b>맵이 꺼져 있으면 Input System이 0을 돌려준다</b> —
    /// 호출부에 모드 가드가 필요 없는 이유다.
    /// </summary>
    public Vector2 MoveInput => inputActions != null ? inputActions.Explore.Move.ReadValue<Vector2>() : Vector2.zero;

    /// <summary>탐색 시점 입력(마우스 델타 / 스틱).</summary>
    public Vector2 LookInput => inputActions != null ? inputActions.Explore.Look.ReadValue<Vector2>() : Vector2.zero;

    void Awake()
    {
        inputActions = new IngameInputs();
    }

    void Start()
    {
        var playerMap = inputActions.Player.Get();
        for (int i = 0; i < PointCount; i++)
        {
            int index = i;
            InputAction action = playerMap.FindAction($"Input{i + 1}");
            action.performed += (ctx) => OnKeyPressed?.Invoke(index);
        }
        inputActions.Player.Dodge.performed += HandleDodgePerformed;

        // 기본값은 전투다 — 배선만 하고 모드를 안 바꾼 씬은 예전과 똑같이 돈다.
        SetMode(Mode);
    }

    /// <summary>
    /// 켜져 있는 액션 맵을 갈아끼운다. <b>언제나 정확히 한 맵만 켜진다</b> —
    /// 두 맵을 동시에 켤 수 있는 경로를 두지 않는 것이 이 메서드의 요지다.
    /// </summary>
    public void SetMode(PlayerInputMode mode)
    {
        Mode = mode;
        if (inputActions == null) return;

        // ⚠ 조건 대입만 쓴다. "if (전투면 켜기)" 형태로 쓰면 반대 전이에서 끄는 것을 빠뜨릴 수 있다.
        SetMapEnabled(inputActions.Player.Get(), mode == PlayerInputMode.Combat);
        SetMapEnabled(inputActions.Explore.Get(), mode == PlayerInputMode.Explore);
    }

    private static void SetMapEnabled(InputActionMap map, bool on)
    {
        if (on) map.Enable();
        else map.Disable();
    }

    void OnDestroy()
    {
        inputActions.Player.Dodge.performed -= HandleDodgePerformed;
        inputActions.Player.Disable();
        inputActions.Explore.Disable();
        inputActions.Dispose();
    }

    private void HandleDodgePerformed(InputAction.CallbackContext _) => OnDodgePressed?.Invoke();
}
