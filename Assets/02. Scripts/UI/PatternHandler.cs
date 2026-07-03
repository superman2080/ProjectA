using System;
using PatternSpace;
using UnityEngine;
using UnityEngine.InputSystem;

public class PatternHandler : MonoBehaviour
{
    [SerializeField] private Point[] patternPoints = new Point[9];
    [SerializeField] private InputHandler inputHandler;

    [Header("Judgement Windows (seconds)")]
    [SerializeField] private float perfectWindow = 0.05f;
    [SerializeField] private float goodWindow = 0.10f;

    public bool IsDragging { get; private set; }

    private Pattern nowPattern;
    private float patternStartTime;
    private bool allCorrect;

    public event Action<JudgementResult, int> OnJudged;
    public event Action<bool> OnPatternComplete; // bool: 전체 정답 시 true (보너스 점수)

    void Start()
    {
        inputHandler = gameObject.GetComponent<InputHandler>();

        foreach (var point in patternPoints)
        {
            point.OnPointDown += OnPointPressed;
            point.OnPointUp += OnPointReleased;
        }

        if (inputHandler != null)
            inputHandler.OnKeyPressed += AddPattern;
    }

    void OnDestroy()
    {
        foreach (var point in patternPoints)
        {
            point.OnPointDown -= OnPointPressed;
            point.OnPointUp -= OnPointReleased;
        }

        if (inputHandler != null)
            inputHandler.OnKeyPressed -= AddPattern;
    }

    void Update()
    {
        // 포인트 밖에서 마우스를 뗀 경우 드래그 종료
        if (IsDragging && !(Mouse.current != null && Mouse.current.leftButton.isPressed))
            EndDrag();
    }

    public void SetPattern(Pattern pattern)
    {
        if (nowPattern != null)
            nowPattern.OnExit -= HandlePatternComplete;

        nowPattern = pattern;
        nowPattern.Initialize();
        nowPattern.OnExit += HandlePatternComplete;
        patternStartTime = Time.time;
        allCorrect = true;
    }

    private void OnPointPressed(int index)
    {
        IsDragging = true;
        AddPattern(index);
    }

    private void OnPointReleased(int index)
    {
        EndDrag();
    }

    private void EndDrag()
    {
        IsDragging = false;
        foreach (var p in patternPoints)
            p.ResetBusy();
    }

    public void AddPattern(int index)
    {
        if (nowPattern == null) return;

        // 틀린 인덱스는 보너스만 취소하고 계속 진행
        if (index != nowPattern.ExpectedPointIndex)
        {
            allCorrect = false;
            return;
        }

        float expectedTime = patternStartTime + nowPattern.ExpectedTime;
        float delta = Mathf.Abs(Time.time - expectedTime);
        JudgementResult result = Judge(delta);

        if (result == JudgementResult.Miss)
            allCorrect = false;

        OnJudged?.Invoke(result, index);
        nowPattern.Input();
    }

    private JudgementResult Judge(float delta)
    {
        if (delta <= perfectWindow) return JudgementResult.Perfect;
        if (delta <= goodWindow) return JudgementResult.Good;
        return JudgementResult.Miss;
    }

    private void HandlePatternComplete()
    {
        nowPattern.OnExit -= HandlePatternComplete;
        OnPatternComplete?.Invoke(allCorrect);
        nowPattern = null;
    }
}
