using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputHandler : MonoBehaviour
{
    private IngameInputs inputActions;
    public bool[] Inputs => inputs;
    private bool[] inputs = new bool[9];

    public event Action<int> OnKeyPressed;

    void Awake()
    {
        inputActions = new IngameInputs();
    }

    void Start()
    {
        var playerMap = inputActions.Player.Get();
        for (int i = 0; i < inputs.Length; i++)
        {
            int index = i;
            InputAction action = playerMap.FindAction($"Input{i + 1}");
            action.performed += (ctx) =>
            {
                inputs[index] = true;
                OnKeyPressed?.Invoke(index);
            };
            action.canceled += (ctx) => inputs[index] = false;
        }
        inputActions.Player.Enable();
    }

    void OnDestroy()
    {
        inputActions.Player.Disable();
        inputActions.Dispose();
    }

    void Update() { }
}

