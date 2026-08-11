using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// <b>게임플레이 입력의 유일한 출처.</b> <c>IngameInputs</c>(Input System 에셋)를 소유하고,
/// 원시 입력을 의미 있는 이벤트로만 밖에 내보낸다 — 노드 입력(1~9)과 회피(Space).
///
/// <para><b>단일 책임</b>: 여기는 "무슨 키가 눌렸나"만 안다. 그 입력이 판정인지 회피인지,
/// 창 안인지 밖인지는 구독자(<c>PatternHandler</c> · <c>DodgeDirector</c>)가 정한다.
/// 그래서 키 바인딩 변경은 <c>IngameInputs.inputactions</c> 한 곳에서 끝나고,
/// 구독자는 <c>UnityEngine.InputSystem</c>에 의존하지 않는다.</para>
/// </summary>
public class InputHandler : MonoBehaviour
{
    private const int PointCount = 9;

    private IngameInputs inputActions;

    /// <summary>패턴인풋 노드 입력. 인자는 인덱스(0~8).</summary>
    public event Action<int> OnKeyPressed;

    /// <summary>회피 입력(Player/Dodge = <c>&lt;Keyboard&gt;/space</c>).</summary>
    public event Action OnDodgePressed;

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
        inputActions.Player.Enable();
    }

    void OnDestroy()
    {
        inputActions.Player.Dodge.performed -= HandleDodgePerformed;
        inputActions.Player.Disable();
        inputActions.Dispose();
    }

    private void HandleDodgePerformed(InputAction.CallbackContext _) => OnDodgePressed?.Invoke();
}
