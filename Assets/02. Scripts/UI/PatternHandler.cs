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

    [Header("Falling Node")]
    [SerializeField] private RectTransform fallingNodeParent;
    [SerializeField] private float fallSpawnPositionY = 500f;
    [SerializeField] private Color[] fallingNodeColorPalette;

#if UNITY_EDITOR
    [Header("Debug (Editor Only)")]
    [SerializeField] private Pattern debugTestPattern;

    [ContextMenu("Debug: Set Test Pattern")]
    private void DebugSetTestPattern()
    {
        if (debugTestPattern != null)
            SetPattern(debugTestPattern);
    }
#endif

    public bool IsDragging { get; private set; }

    private Pattern nowPattern;
    private float patternStartTime;
    private bool allCorrect;

    private readonly List<int> connectedIndices = new List<int>();
    private Canvas canvas;
    private Camera canvasCamera;
    private bool isKeyboardStroke;
    private float lastKeyboardInputTime;

    private struct ScheduledSpawn
    {
        public int position;
        public float spawnTime;
        public float fallDuration; // 이 노드가 실제로 낙하하는 데 걸리는 시간(행마다 노출 시간을 맞추기 위해 개별 계산됨)
    }

    private readonly List<ScheduledSpawn> scheduledSpawns = new List<ScheduledSpawn>();
    private readonly Dictionary<int, FallingNodeView> activeFallingNodes = new Dictionary<int, FallingNodeView>();
    private int spawnCounter;
    private float screenTopY;

    public event Action<JudgementResult, int> OnJudged;
    public event Action<bool> OnPatternComplete; // bool: 전체 정답 시 true (보너스 점수)
    /// <summary>확장 포인트: 새 노드가 라인에 연결될 때마다 (index, 월드 좌표) 전달.</summary>
    public event Action<int, Vector3> OnNodeConnected;

    /// <summary>확장 포인트(캐릭터 액션): 낙하 노드가 스폰될 때 (Point 인덱스, 노드 타입, 월드 좌표) 전달.</summary>
    public event Action<int, NodeType, Vector3> OnFallingNodeSpawned;
    /// <summary>확장 포인트(캐릭터 액션): 플레이어가 낙하 노드를 실제로 맞췄을 때 (Point 인덱스, 노드 타입, 월드 좌표, 판정 결과) 전달.</summary>
    public event Action<int, NodeType, Vector3, JudgementResult> OnFallingNodeResolved;
    /// <summary>확장 포인트(캐릭터 액션): 낙하 노드가 맞지 않은 채 자연 도착했을 때 (Point 인덱스, 노드 타입, 월드 좌표) 전달.</summary>
    public event Action<int, NodeType, Vector3> OnFallingNodeMissedArrival;

    void Start()
    {
        inputHandler = gameObject.GetComponent<InputHandler>();
        canvas = GetComponentInParent<Canvas>();
        canvasCamera = canvas != null ? canvas.worldCamera : null;
        screenTopY = ComputeScreenTopY();

        foreach (var point in patternPoints)
        {
            point.OnPointDown += OnPointPressed;
            point.OnPointUp += OnPointReleased;
        }

        if (inputHandler != null)
            inputHandler.OnKeyPressed += OnKeyboardInput;

        DebugSetTestPattern();
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
        ProcessFallingNodeSpawns();

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

        ClearFallingNodes();
        ResetPointColors();

        nowPattern = pattern;
        nowPattern.Initialize();
        nowPattern.OnExit += HandlePatternComplete;
        patternStartTime = Time.time;
        allCorrect = true;
        lineRenderer?.SetCorrectState(true);

        spawnCounter = 0;
        for (int i = 0; i < nowPattern.AllData.Count; i++)
        {
            var data = nowPattern.AllData[i];
            float duration = ComputeFallDuration(data.index, data.visibleExposureDuration);
            float spawnTime = patternStartTime + data.inputTime - duration;
            scheduledSpawns.Add(new ScheduledSpawn { position = i, spawnTime = spawnTime, fallDuration = duration });
        }
    }

    /// <summary>루트 Canvas의 실제 화면 상단 경계를 fallingNodeParent 로컬 좌표로 변환한다 (Canvas는 Screen Space Overlay라 이 경계 밖은 Mask 없이도 실제로 렌더링되지 않는다).</summary>
    private float ComputeScreenTopY()
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Vector3 topWorld = canvasRect.TransformPoint(new Vector3(0f, canvasRect.rect.yMax, 0f));
        return WorldToLocal(fallingNodeParent, topWorld).y;
    }

    /// <summary>
    /// 생성 위치(fallSpawnPositionY, fallingNodeParent 로컬 좌표 기준 절대 Y, 사용자가 직접 설정)는 모든 행에서
    /// 동일하게 유지하되, 화면 실제 경계(screenTopY) 밖에서 시작하는 상단 행 노드는 화면 밖 구간이 길어 노출
    /// 시간이 짧아지므로, 경계 안쪽 구간(visibleDistance)만 exposureDuration(PatternData별 설정값) 동안
    /// 이동하도록 전체 낙하 시간을 역산한다 — 결과적으로 행마다 낙하 속도가 달라진다.
    /// </summary>
    private float ComputeFallDuration(int pointIndex, float exposureDuration)
    {
        Vector3 worldPosition = patternPoints[pointIndex].transform.position;
        Vector2 targetLocalPos = WorldToLocal(fallingNodeParent, worldPosition);

        float totalDistance = Mathf.Max(fallSpawnPositionY - targetLocalPos.y, 0f);
        if (totalDistance <= 0f)
            return exposureDuration;

        float visibleDistance = Mathf.Clamp(screenTopY - targetLocalPos.y, 0f, totalDistance);

        if (visibleDistance <= 0f)
            return exposureDuration;

        return totalDistance * exposureDuration / visibleDistance;
    }

    private void OnDrawGizmos()
    {
        if (fallingNodeParent == null) return;

        Gizmos.color = Color.yellow;
        Vector3 left = fallingNodeParent.TransformPoint(new Vector3(fallingNodeParent.rect.xMin, fallSpawnPositionY, 0f));
        Vector3 right = fallingNodeParent.TransformPoint(new Vector3(fallingNodeParent.rect.xMax, fallSpawnPositionY, 0f));
        Gizmos.DrawLine(left, right);
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
        {
            p.ResetBusy();
            p.ResetColor();
        }

        lineRenderer?.ClearLiveEndPoint();
        TriggerLineFadeOut();
    }

    private void ResetPointColors()
    {
        foreach (var p in patternPoints)
            p.ResetColor();
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
            localPoints.Add(WorldToLocal(lineRenderer.rectTransform, patternPoints[idx].transform.position));

        lineRenderer.SetPoints(localPoints);
    }

    private Vector2 WorldToLocal(RectTransform target, Vector3 worldPosition)
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvasCamera, worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            target, screenPoint, canvasCamera, out Vector2 localPoint);
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
            patternPoints[index].SetJudgementColor(JudgementResult.Miss);
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

        patternPoints[index].SetJudgementColor(result);

        int position = nowPattern.CurrentIndex;
        NodeType nodeType = nowPattern.GetNodeType(position);
        Vector3 worldPosition = patternPoints[index].transform.position;

        if (activeFallingNodes.TryGetValue(position, out var fallingNode))
        {
            ReleaseFallingNode(fallingNode);
            activeFallingNodes.Remove(position);
        }

        OnJudged?.Invoke(result, index);
        OnFallingNodeResolved?.Invoke(index, nodeType, worldPosition, result);
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
        ClearFallingNodes();
    }

    private void ProcessFallingNodeSpawns()
    {
        for (int i = scheduledSpawns.Count - 1; i >= 0; i--)
        {
            if (Time.time < scheduledSpawns[i].spawnTime) continue;

            SpawnFallingNode(scheduledSpawns[i].position, scheduledSpawns[i].fallDuration);
            scheduledSpawns.RemoveAt(i);
        }
    }

    private void SpawnFallingNode(int position, float duration)
    {
        var data = nowPattern.AllData[position];
        Color color = fallingNodeColorPalette != null && fallingNodeColorPalette.Length > 0
            ? fallingNodeColorPalette[spawnCounter % fallingNodeColorPalette.Length]
            : Color.white;
        spawnCounter++;

        Vector3 worldPosition = patternPoints[data.index].transform.position;
        Vector2 targetLocalPos = WorldToLocal(fallingNodeParent, worldPosition);
        NodeType nodeType = nowPattern.GetNodeType(position);

        var node = Pool.Instance.Get<FallingNodeView>(PoolKey.FallingNode, n =>
        {
            n.transform.SetParent(fallingNodeParent, false);
            n.Initialize(data.index + 1, color, nodeType, targetLocalPos, fallSpawnPositionY, duration);
        });
        node.OnArrived += HandleFallingNodeArrived;

        activeFallingNodes[position] = node;

        OnFallingNodeSpawned?.Invoke(data.index, nodeType, worldPosition);
    }

    private void HandleFallingNodeArrived(FallingNodeView node)
    {
        node.OnArrived -= HandleFallingNodeArrived;

        int position = FindPositionForNode(node);
        if (position >= 0)
            activeFallingNodes.Remove(position);

        Vector3 worldPosition = patternPoints[node.PointIndex].transform.position;
        OnFallingNodeMissedArrival?.Invoke(node.PointIndex, node.Type, worldPosition);

        Pool.Instance.Return(PoolKey.FallingNode, node);
    }

    private int FindPositionForNode(FallingNodeView node)
    {
        foreach (var kvp in activeFallingNodes)
            if (kvp.Value == node) return kvp.Key;
        return -1;
    }

    private void ReleaseFallingNode(FallingNodeView node)
    {
        node.OnArrived -= HandleFallingNodeArrived;
        Pool.Instance.Return(PoolKey.FallingNode, node);
    }

    private void ClearFallingNodes()
    {
        foreach (var node in activeFallingNodes.Values)
            ReleaseFallingNode(node);
        activeFallingNodes.Clear();
        scheduledSpawns.Clear();
    }
}
