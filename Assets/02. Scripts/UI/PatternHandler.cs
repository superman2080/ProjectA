using System;
using System.Collections.Generic;
using PatternSpace;
using UnityEngine;
using UnityEngine.InputSystem;

public class PatternHandler : MonoBehaviour
{
    [SerializeField] private Point[] patternPoints = new Point[9];
    [SerializeField] private InputHandler inputHandler;
    [SerializeField] private PatternLineRenderer lineRenderer;

    [Header("Judgement Windows (seconds)")]
    [SerializeField] private float perfectWindow = 0.05f;
    [SerializeField] private float goodWindow = 0.10f;

    [Header("Line")]
    [SerializeField] private float lineFadeDuration = 0.2f;
    [SerializeField] private float keyboardInputTimeout = 0.5f;

    public bool IsDragging { get; private set; }

    private Pattern nowPattern;
    private float patternStartTime;
    private bool allCorrect;

    private readonly List<int> connectedIndices = new List<int>();
    private Canvas canvas;
    private Camera canvasCamera;
    private bool isKeyboardStroke;
    private float lastKeyboardInputTime;

    public event Action<JudgementResult, int> OnJudged;
    public event Action<bool> OnPatternComplete; // bool: 전체 정답 시 true (보너스 점수)
    /// <summary>확장 포인트: 새 노드가 라인에 연결될 때마다 (index, 월드 좌표) 전달.</summary>
    public event Action<int, Vector3> OnNodeConnected;

    void Start()
    {
        inputHandler = gameObject.GetComponent<InputHandler>();
        canvas = GetComponentInParent<Canvas>();
        canvasCamera = canvas != null ? canvas.worldCamera : null;

        foreach (var point in patternPoints)
        {
            point.OnPointDown += OnPointPressed;
            point.OnPointUp += OnPointReleased;
        }

        if (inputHandler != null)
            inputHandler.OnKeyPressed += OnKeyboardInput;
    }

    void OnDestroy()
    {
        foreach (var point in patternPoints)
        {
            point.OnPointDown -= OnPointPressed;
            point.OnPointUp -= OnPointReleased;
        }

        if (inputHandler != null)
            inputHandler.OnKeyPressed -= OnKeyboardInput;
    }

    private void OnKeyboardInput(int index)
    {
        lastKeyboardInputTime = Time.time;
        patternPoints[index].ForceDown();
    }

    void Update()
    {
        if (!IsDragging) return;

        if (isKeyboardStroke)
        {
            // 일정 시간 키 입력이 없으면 패턴(스트로크) 완성으로 간주하고 종료
            if (Time.time - lastKeyboardInputTime >= keyboardInputTimeout)
                EndDrag();
            return;
        }

        // 포인트 밖에서 마우스를 뗀 경우 드래그 종료
        if (!(Mouse.current != null && Mouse.current.leftButton.isPressed))
        {
            EndDrag();
            return;
        }

        if (lineRenderer != null && Mouse.current != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                lineRenderer.rectTransform, Mouse.current.position.ReadValue(), canvasCamera, out Vector2 localPoint);
            lineRenderer.SetLiveEndPoint(localPoint);
        }
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
        lineRenderer?.SetCorrectState(true);
    }

    private void OnPointPressed(int index)
    {
        if (!IsDragging)
        {
            connectedIndices.Clear();
            isKeyboardStroke = !(Mouse.current != null && Mouse.current.leftButton.isPressed);
            if (isKeyboardStroke)
                lineRenderer?.ClearLiveEndPoint();
        }

        IsDragging = true;

        if (connectedIndices.Count > 0)
        {
            int passIndex = GetPassThroughIndex(connectedIndices[^1], index);
            if (passIndex != -1 && !connectedIndices.Contains(passIndex))
                patternPoints[passIndex].ForceDown();
        }

        AppendPointToLine(index);
        AddPattern(index);
    }

    /// <summary>3x3 격자에서 a→b 직선이 정확히 통과하는 다른 노드의 인덱스를 반환한다. 없으면 -1.</summary>
    private static int GetPassThroughIndex(int a, int b)
    {
        int rowA = a / 3, colA = a % 3;
        int rowB = b / 3, colB = b % 3;

        if ((rowA + rowB) % 2 != 0 || (colA + colB) % 2 != 0)
            return -1;

        return (rowA + rowB) / 2 * 3 + (colA + colB) / 2;
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

        lineRenderer?.ClearLiveEndPoint();
        TriggerLineFadeOut();
    }

    private void AppendPointToLine(int index)
    {
        if (connectedIndices.Count > 0 && connectedIndices[^1] == index)
            return; // 연속 동일 인덱스 재진입 방지

        connectedIndices.Add(index);
        RefreshLinePoints();

        OnNodeConnected?.Invoke(index, patternPoints[index].transform.position);
    }

    private void RefreshLinePoints()
    {
        if (lineRenderer == null) return;

        var localPoints = new List<Vector2>(connectedIndices.Count);
        foreach (var idx in connectedIndices)
            localPoints.Add(WorldToLineLocal(patternPoints[idx].transform.position));

        lineRenderer.SetPoints(localPoints);
    }

    private Vector2 WorldToLineLocal(Vector3 worldPosition)
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvasCamera, worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            lineRenderer.rectTransform, screenPoint, canvasCamera, out Vector2 localPoint);
        return localPoint;
    }

    private void TriggerLineFadeOut()
    {
        if (lineRenderer == null || connectedIndices.Count == 0) return;

        lineRenderer.FadeOutAndClear(lineFadeDuration);
        connectedIndices.Clear();
    }

    public void AddPattern(int index)
    {
        if (nowPattern == null) return;

        // 틀린 인덱스는 보너스만 취소하고 계속 진행
        if (index != nowPattern.ExpectedPointIndex)
        {
            allCorrect = false;
            lineRenderer?.SetCorrectState(false);
            return;
        }

        float expectedTime = patternStartTime + nowPattern.ExpectedTime;
        float delta = Mathf.Abs(Time.time - expectedTime);
        JudgementResult result = Judge(delta);

        if (result == JudgementResult.Miss)
        {
            allCorrect = false;
            lineRenderer?.SetCorrectState(false);
        }

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
        TriggerLineFadeOut();
    }
}
