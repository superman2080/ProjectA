using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>키보드 1~9 입력을 인덱스(0~8) 이벤트로 변환해 발행한다.</summary>
public class InputHandler : MonoBehaviour
{
    private const int PointCount = 9;

    private IngameInputs inputActions;

    public event Action<int> OnKeyPressed;

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
        inputActions.Player.Enable();
    }

    void OnDestroy()
    {
        inputActions.Player.Disable();
        inputActions.Dispose();
    }
}
