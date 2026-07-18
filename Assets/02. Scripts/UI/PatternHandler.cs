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

    [Header("Hit Area")]
    [Range(0.1f, 1f)][SerializeField] private float inactiveHitAreaRatio = 0.5f;

    [Header("Guide Line")]
    [SerializeField] private PatternLineRenderer guideLineRenderer;
    [SerializeField] private float guideFadeDuration = 0.2f;

    [Header("Falling Node")]
    [SerializeField] private RectTransform fallingNodeParent;
    [SerializeField] private float fallSpawnPositionY = 500f;
    [SerializeField] private Color[] fallingNodeColorPalette;

#if UNITY_EDITOR
    [Header("Debug (Editor Only)")]
    [SerializeField] private Pattern debugTestPattern;
    [SerializeField] private float[] debugInputTimes;
    [SerializeField] private float[] debugExposureDurations;

    [Header("Debug Input (Editor Only)")]
    [SerializeField] private bool debugInputEnabled = false; // 마스터 On/Off (수동·자동 모두 이 스위치에 종속)
    [SerializeField] private Key debugPerfectKey = Key.F1;
    [SerializeField] private Key debugGoodKey = Key.F2;
    [SerializeField] private Key debugMissKey = Key.F3;
    [SerializeField] private bool debugAutoPerfect = false; // 자동 Perfect(오토플레이) 토글 상태

    /// <summary>디버그 강제 입력 시 <see cref="AddPattern"/>이 이 값을 사용해 판정을 대체한다. 세팅→ForceDown→즉시 클리어.</summary>
    private JudgementResult? debugForcedResult;

    /// <summary>인스펙터 하이라이트용: 마지막으로 발동한 디버그 판정 모드.</summary>
    private JudgementResult? debugLastUsedMode;
    public JudgementResult? DebugLastUsedMode => debugLastUsedMode;

    [ContextMenu("Debug: Set Test Pattern")]
    private void DebugSetTestPattern()
    {
        if (debugTestPattern == null || debugInputTimes == null || debugInputTimes.Length == 0)
            return;
        SetPattern(debugTestPattern, debugInputTimes, null, debugExposureDurations);
    }

    /// <summary>판정 대상의 다음 노드를 <paramref name="forced"/> 판정으로 강제 입력한다. 기존 입력 파이프라인을 그대로 재사용한다.</summary>
    public void DebugForceInput(JudgementResult forced)
    {
        if (!debugInputEnabled) return;

        var target = JudgeTarget;
        if (target == null) return;

        int index = target.ExpectedPointIndex;
        debugForcedResult = forced;
        debugLastUsedMode = forced;
        patternPoints[index].ForceDown(); // OnPointPressed → AddPattern 경로를 그대로 탄다
        debugForcedResult = null;         // 실제 유저 입력에 잔류 영향이 없도록 즉시 클리어
    }

    /// <summary><see cref="Update"/> 맨 앞에서 호출. 디버그 수동 키/자동 Perfect를 처리한다(기존 로직 흐름은 유지).</summary>
    private void ProcessDebugInput()
    {
        if (!debugInputEnabled) return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current[debugPerfectKey].wasPressedThisFrame)
                DebugForceInput(JudgementResult.Perfect);
            if (Keyboard.current[debugGoodKey].wasPressedThisFrame)
                DebugForceInput(JudgementResult.Good);
            if (Keyboard.current[debugMissKey].wasPressedThisFrame)
                DebugForceInput(JudgementResult.Miss);
        }

        if (debugAutoPerfect)
        {
            var target = JudgeTarget;
            if (target != null && Time.time >= target.ExpectedTime)
                DebugForceInput(JudgementResult.Perfect); // 도달 타이밍에 맞춰 프레임당 1노드씩 자동 진행
        }
    }
#endif

    /// <summary>스트로크(마우스 드래그 또는 키보드 연속 입력) 진행 중 여부.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>마우스로 끌고 있는 중인지. Point가 '지나가며 입력'을 인정할지 판단하는 데 쓴다 — 키보드 스트로크 중 마우스 호버는 입력이 아니다.</summary>
    public bool IsMouseDragging => IsDragging && !isKeyboardStroke;

    /// <summary>
    /// 살아 있는 패턴들(투입 순서). 채보는 다음 패턴의 노드를 이전 패턴이 끝나기 전에 스폰해야 하므로
    /// 여러 패턴이 동시에 '연출 중'일 수 있다. 반면 입력(판정) 시각은 서로 겹치지 않으므로
    /// 판정 대상(<see cref="JudgeTarget"/>)은 언제나 선두 하나뿐이다.
    /// </summary>
    private readonly List<ActivePattern> activePatterns = new List<ActivePattern>();

    /// <summary>현재 입력을 받는 패턴. 선두 패턴이 완료/만료되면 즉시 다음 패턴으로 승계된다.</summary>
    private ActivePattern JudgeTarget => activePatterns.Count > 0 ? activePatterns[0] : null;

    private readonly List<int> connectedIndices = new List<int>();
    private readonly bool[] hitAreaUsage = new bool[9];
    private Canvas canvas;
    private Camera canvasCamera;
    private bool isKeyboardStroke;

    private struct ScheduledSpawn
    {
        public ActivePattern owner;
        public int position;
        public float spawnTime;
        public float fallDuration; // 이 노드가 실제로 낙하하는 데 걸리는 시간(행마다 노출 시간을 맞추기 위해 개별 계산됨)
    }

    private struct ActiveNode
    {
        public ActivePattern owner;
        public int position;
        public FallingNodeView view;
    }

    private readonly List<ScheduledSpawn> scheduledSpawns = new List<ScheduledSpawn>();
    private readonly List<ActiveNode> activeFallingNodes = new List<ActiveNode>();
    private int spawnCounter;
    private float screenTopY;

    public event Action<JudgementResult, int> OnJudged;
    public event Action<PatternCompletionInfo> OnPatternComplete; // 패턴 완료(완주/만료) 순간의 페이로드. 성공/실패·타이밍·다음 패턴 정보를 담는다.
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
        EnsureLayoutInitialized();

        for (int i = 0; i < patternPoints.Length; i++)
        {
            patternPoints[i].Initialize(i, this); // 배열 순서가 Point 인덱스의 진실의 원천이다
            patternPoints[i].OnPointDown += OnPointPressed;
            patternPoints[i].OnPointUp += OnPointReleased;
        }

        if (inputHandler != null)
            inputHandler.OnKeyPressed += OnKeyboardInput;

        RefreshJudgeTargetVisuals();

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
        patternPoints[index].ForceDown();
    }

    void Update()
    {
#if UNITY_EDITOR
        ProcessDebugInput();
#endif

        ExpireOverduePatterns();
        ProcessFallingNodeSpawns();

        if (!IsDragging) return;

        if (isKeyboardStroke)
        {
            var target = JudgeTarget;
            if (target == null || Time.time > target.Deadline)
                EndStroke();
            return;
        }

        // 포인트 밖에서 마우스를 뗀 경우 드래그 종료
        if (!(Mouse.current != null && Mouse.current.leftButton.isPressed))
        {
            EndStroke();
            return;
        }

        if (lineRenderer != null && Mouse.current != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                lineRenderer.rectTransform, Mouse.current.position.ReadValue(), canvasCamera, out Vector2 localPoint);
            lineRenderer.SetLiveEndPoint(localPoint);
        }
    }

    /// <summary>입력 시한(마지막 입력 시각 + goodWindow)이 지난 패턴을 종료한다. 남은 노드는 미입력이므로 전체 정답 보너스를 취소한다.</summary>
    private void ExpireOverduePatterns()
    {
        while (JudgeTarget != null && Time.time > JudgeTarget.Deadline)
        {
            var expired = JudgeTarget;
            if (!expired.IsComplete)
                expired.MarkIncorrect();

            CompletePattern(expired);
        }
    }

    private const float DefaultExposureDuration = 0.5f;

    /// <summary>
    /// 패턴을 큐에 추가한다. 진행 중인 패턴이 있어도 <b>파기하지 않고</b>, 새 패턴의 노드 스폰만 즉시 예약한다.
    /// 판정 대상은 선두 패턴이 완료/만료될 때 승계된다.
    ///
    /// <paramref name="spawnTimes"/>가 주어지면(채보 재생 경로) 이미 구운 스폰 시각을 그대로 사용하고,
    /// 없으면(디버그/수동 테스트 경로) <paramref name="exposureDurations"/>(없으면 기본값)로 <see cref="ComputeFallDuration"/>을 그 자리에서 계산한다.
    /// </summary>
    public void SetPattern(Pattern pattern, IReadOnlyList<float> inputTimes, IReadOnlyList<float> spawnTimes = null, IReadOnlyList<float> exposureDurations = null)
    {
        if (inputTimes == null || inputTimes.Count != pattern.AllData.Count)
        {
            Debug.LogError($"[PatternHandler] '{pattern.name}' 입력 시각 개수({inputTimes?.Count ?? 0})가 노드 개수({pattern.AllData.Count})와 다릅니다. 패턴을 설정하지 않습니다.", this);
            return;
        }

        // StartTime은 '투입 시각'으로 고정한다 — inputTimes/spawnTimes가 이 시점 기준 상대시간이므로,
        // 나중에 판정 대상으로 승계될 때 다시 잡으면 판정 시각이 통째로 밀린다.
        var active = new ActivePattern(pattern, inputTimes, Time.time, goodWindow);

        for (int i = 0; i < active.NodeCount; i++)
        {
            int pointIndex = active.GetPointIndex(i);
            float spawnOffset;
            float fallDuration;

            if (spawnTimes != null && i < spawnTimes.Count)
            {
                spawnOffset = spawnTimes[i];
                fallDuration = active.GetInputTime(i) - spawnOffset;
            }
            else
            {
                float exposureDuration = exposureDurations != null && i < exposureDurations.Count ? exposureDurations[i] : DefaultExposureDuration;
                fallDuration = ComputeFallDuration(pointIndex, exposureDuration);
                spawnOffset = active.GetInputTime(i) - fallDuration;
            }

            scheduledSpawns.Add(new ScheduledSpawn
            {
                owner = active,
                position = i,
                spawnTime = active.StartTime + spawnOffset,
                fallDuration = fallDuration
            });
        }

        bool becomesJudgeTarget = activePatterns.Count == 0;
        activePatterns.Add(active);

        if (becomesJudgeTarget)
            RefreshJudgeTargetVisuals();
    }

    /// <summary>곡 중단 등으로 진행 중인 모든 패턴과 낙하 노드를 정리한다.</summary>
    public void ClearAllPatterns()
    {
        for (int i = activeFallingNodes.Count - 1; i >= 0; i--)
            ReleaseFallingNode(activeFallingNodes[i].view);
        activeFallingNodes.Clear();
        scheduledSpawns.Clear();
        activePatterns.Clear();

        ResetPointColors();
        TriggerLineFadeOut();
        RefreshJudgeTargetVisuals();
    }

    /// <summary>루트 Canvas의 실제 화면 상단 경계를 fallingNodeParent 로컬 좌표로 변환한다 (Canvas는 Screen Space Overlay라 이 경계 밖은 Mask 없이도 실제로 렌더링되지 않는다).</summary>
    private float ComputeScreenTopY()
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Vector3 topWorld = canvasRect.TransformPoint(new Vector3(0f, canvasRect.rect.yMax, 0f));
        return WorldToLocal(fallingNodeParent, topWorld).y;
    }

    /// <summary>씬/캔버스 참조가 아직 없으면(에디터에서 Play 모드 없이 굽는 툴이 호출하는 경우 포함) 초기화한다.</summary>
    private void EnsureLayoutInitialized()
    {
        if (canvas != null) return;
        canvas = GetComponentInParent<Canvas>();
        canvasCamera = canvas != null ? canvas.worldCamera : null;
        screenTopY = ComputeScreenTopY();
    }

    /// <summary>
    /// 생성 위치(fallSpawnPositionY, fallingNodeParent 로컬 좌표 기준 절대 Y, 사용자가 직접 설정)는 모든 행에서
    /// 동일하게 유지하되, 화면 실제 경계(screenTopY) 밖에서 시작하는 상단 행 노드는 화면 밖 구간이 길어 노출
    /// 시간이 짧아지므로, 경계 안쪽 구간(visibleDistance)만 exposureDuration 동안
    /// 이동하도록 전체 낙하 시간을 역산한다 — 결과적으로 행마다 낙하 속도가 달라진다.
    /// </summary>
    public float ComputeFallDuration(int pointIndex, float exposureDuration)
    {
        EnsureLayoutInitialized();
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
            BeginStroke();

        // 이번 패턴에서 이미 입력된 Point는 무시한다 (드래그 재진입 / 통과 노드 중복 방지).
        // connectedIndices는 패턴이 끝날 때마다 클리어되므로, 다음 패턴에서 같은 Point를 다시 쓸 수 있다.
        // (예전에는 이 역할을 Point.isBusy가 했는데, 그건 스트로크가 끝나야만 풀려 패턴 경계에서 입력이 삼켜졌다.)
        if (connectedIndices.Contains(index))
            return;

        if (connectedIndices.Count > 0)
        {
            int passIndex = GetPassThroughIndex(connectedIndices[^1], index);
            if (passIndex != -1)
                patternPoints[passIndex].ForceDown(); // 재귀적으로 이 메서드를 다시 타며 위 가드로 중복이 걸러진다
        }

        AppendPointToLine(index);
        AddPattern(index);
    }

    private void BeginStroke()
    {
        IsDragging = true;
        connectedIndices.Clear();

        isKeyboardStroke = !(Mouse.current != null && Mouse.current.leftButton.isPressed);
        if (isKeyboardStroke)
            lineRenderer?.ClearLiveEndPoint();
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
        EndStroke();
    }

    private void EndStroke()
    {
        IsDragging = false;

        foreach (var p in patternPoints)
            p.ResetColor();

        lineRenderer?.ClearLiveEndPoint();
        TriggerLineFadeOut(); // connectedIndices도 여기서 클리어된다
    }

    /// <summary>가이드라인·판정 영역·라인 색을 현재 판정 대상 패턴 기준으로 다시 적용한다. 판정 대상이 없으면 전부 원상복구한다.</summary>
    private void RefreshJudgeTargetVisuals()
    {
        var target = JudgeTarget;

        ApplyHitAreas(target?.Template);

        if (target != null)
        {
            ShowGuideLine(target.Template);
            lineRenderer?.SetCorrectState(true);
        }
        else
        {
            HideGuideLine();
        }
    }

    /// <summary>패턴이 지나갈 Point들을 순서대로 잇는 가이드 경로를 표시한다.</summary>
    private void ShowGuideLine(Pattern pattern)
    {
        if (guideLineRenderer == null) return;

        var localPoints = new List<Vector2>(pattern.AllData.Count);
        foreach (var data in pattern.AllData)
            localPoints.Add(WorldToLocal(guideLineRenderer.rectTransform, patternPoints[data.index].transform.position));

        guideLineRenderer.SetPoints(localPoints);
    }

    private void HideGuideLine()
    {
        if (guideLineRenderer == null) return;

        guideLineRenderer.FadeOutAndClear(guideFadeDuration);
    }

    /// <summary>
    /// 판정 대상 패턴에 포함되지 않은 Point의 판정 영역을 <see cref="inactiveHitAreaRatio"/>만큼 축소한다.
    /// <paramref name="pattern"/>이 null이면(판정 대상 없는 대기 구간) 9개 모두 원래 크기로 복구한다.
    /// </summary>
    private void ApplyHitAreas(Pattern pattern)
    {
        if (pattern == null)
        {
            foreach (var p in patternPoints)
                p.ResetHitArea();
            return;
        }

        for (int i = 0; i < hitAreaUsage.Length; i++)
            hitAreaUsage[i] = false;

        foreach (var data in pattern.AllData)
            hitAreaUsage[data.index] = true;

        for (int i = 0; i < patternPoints.Length; i++)
            patternPoints[i].SetHitAreaRatio(hitAreaUsage[i] ? 1f : inactiveHitAreaRatio);
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

    /// <summary>라인을 페이드아웃시키고 '이번 패턴에서 입력된 Point' 기록을 비운다 — 이 기록이 중복 입력 방지의 진실의 원천이다.</summary>
    private void TriggerLineFadeOut()
    {
        if (connectedIndices.Count == 0) return;

        lineRenderer?.FadeOutAndClear(lineFadeDuration);
        connectedIndices.Clear();
    }

    public void AddPattern(int index)
    {
        var target = JudgeTarget;
        if (target == null) return;

        // 틀린 인덱스는 보너스만 취소하고 계속 진행
        if (index != target.ExpectedPointIndex)
        {
            target.MarkIncorrect();
            lineRenderer?.SetCorrectState(false);
            patternPoints[index].SetJudgementColor(JudgementResult.Miss);
            return;
        }

        float delta = Mathf.Abs(Time.time - target.ExpectedTime);
#if UNITY_EDITOR
        JudgementResult result = debugForcedResult ?? Judge(delta);
#else
        JudgementResult result = Judge(delta);
#endif

        if (result == JudgementResult.Miss)
        {
            target.MarkIncorrect();
            lineRenderer?.SetCorrectState(false);
        }

        patternPoints[index].SetJudgementColor(result);

        int position = target.CurrentPosition;
        NodeType nodeType = target.GetNodeType(position);
        Vector3 worldPosition = patternPoints[index].transform.position;

        ReleaseNodeOf(target, position);

        OnJudged?.Invoke(result, index);
        OnFallingNodeResolved?.Invoke(index, nodeType, worldPosition, result);

        target.Advance();

        // 마지막 노드가 판정된 그 자리에서 완료 처리 → 다음 패턴을 같은 프레임에 즉시 승계한다.
        if (target.IsComplete)
            CompletePattern(target);
    }

    private JudgementResult Judge(float delta)
    {
        if (delta <= perfectWindow) return JudgementResult.Perfect;
        if (delta <= goodWindow) return JudgementResult.Good;
        return JudgementResult.Miss;
    }

    /// <summary>패턴을 종료하고 그 패턴에 속한 노드를 즉시 회수한 뒤, 다음 패턴을 판정 대상으로 승계한다.</summary>
    private void CompletePattern(ActivePattern pattern)
    {
        activePatterns.Remove(pattern);
        ClearNodesOf(pattern);

        // Remove가 먼저 실행됐으므로 이 시점의 선두(activePatterns[0])가 곧 다음 대기 패턴이다.
        // 다음 패턴이 없으면(채보상 공백) -1f → 겹침 방지 속도 제약 없음.
        float nextLastNodeTime = activePatterns.Count > 0 ? activePatterns[0].LastNodeTime : -1f;
        OnPatternComplete?.Invoke(new PatternCompletionInfo(pattern.AllCorrect, pattern.Template, pattern.LastNodeTime, nextLastNodeTime));

        // 키보드 스트로크는 여기서 끝낸다. 마우스 드래그는 끊지 않는다 —
        // 다음 패턴으로 이어 긋는 중일 수 있고, connectedIndices는 아래에서 어차피 비워지므로 이어져도 안전하다.
        if (IsDragging && isKeyboardStroke)
            EndStroke();

        ResetPointColors(); // 판정 색(Perfect/Good/Miss)이 다음 패턴까지 남지 않도록 되돌린다
        TriggerLineFadeOut();
        RefreshJudgeTargetVisuals();
    }

    private void ResetPointColors()
    {
        foreach (var p in patternPoints)
            p.ResetColor();
    }

    private void ProcessFallingNodeSpawns()
    {
        for (int i = scheduledSpawns.Count - 1; i >= 0; i--)
        {
            if (Time.time < scheduledSpawns[i].spawnTime) continue;

            SpawnFallingNode(scheduledSpawns[i].owner, scheduledSpawns[i].position, scheduledSpawns[i].fallDuration);
            scheduledSpawns.RemoveAt(i);
        }
    }

    private void SpawnFallingNode(ActivePattern owner, int position, float duration)
    {
        int pointIndex = owner.GetPointIndex(position);
        Color color = fallingNodeColorPalette != null && fallingNodeColorPalette.Length > 0
            ? fallingNodeColorPalette[spawnCounter % fallingNodeColorPalette.Length]
            : Color.white;
        spawnCounter++;

        Vector3 worldPosition = patternPoints[pointIndex].transform.position;
        Vector2 targetLocalPos = WorldToLocal(fallingNodeParent, worldPosition);
        NodeType nodeType = owner.GetNodeType(position);

        var node = Pool.Instance.Get<FallingNodeView>(PoolKey.FallingNode, n =>
        {
            n.transform.SetParent(fallingNodeParent, false);
            n.Initialize(pointIndex + 1, color, nodeType, targetLocalPos, fallSpawnPositionY, duration);
        });
        node.OnArrived += HandleFallingNodeArrived;

        activeFallingNodes.Add(new ActiveNode { owner = owner, position = position, view = node });

        OnFallingNodeSpawned?.Invoke(pointIndex, nodeType, worldPosition);
    }

    private void HandleFallingNodeArrived(FallingNodeView node)
    {
        node.OnArrived -= HandleFallingNodeArrived;

        RemoveNodeEntry(node);

        Vector3 worldPosition = patternPoints[node.PointIndex].transform.position;
        OnFallingNodeMissedArrival?.Invoke(node.PointIndex, node.Type, worldPosition);

        Pool.Instance.Return(PoolKey.FallingNode, node);
    }

    private void RemoveNodeEntry(FallingNodeView node)
    {
        for (int i = 0; i < activeFallingNodes.Count; i++)
        {
            if (activeFallingNodes[i].view != node) continue;

            activeFallingNodes.RemoveAt(i);
            return;
        }
    }

    /// <summary>판정된 노드 하나를 회수한다.</summary>
    private void ReleaseNodeOf(ActivePattern owner, int position)
    {
        for (int i = 0; i < activeFallingNodes.Count; i++)
        {
            if (activeFallingNodes[i].owner != owner || activeFallingNodes[i].position != position) continue;

            ReleaseFallingNode(activeFallingNodes[i].view);
            activeFallingNodes.RemoveAt(i);
            return;
        }
    }

    /// <summary>패턴 완료/만료 시 그 패턴에 속한 낙하 노드와 남은 스폰 예약을 즉시 정리한다.</summary>
    private void ClearNodesOf(ActivePattern owner)
    {
        for (int i = activeFallingNodes.Count - 1; i >= 0; i--)
        {
            if (activeFallingNodes[i].owner != owner) continue;

            ReleaseFallingNode(activeFallingNodes[i].view);
            activeFallingNodes.RemoveAt(i);
        }

        for (int i = scheduledSpawns.Count - 1; i >= 0; i--)
        {
            if (scheduledSpawns[i].owner != owner) continue;

            scheduledSpawns.RemoveAt(i);
        }
    }

    private void ReleaseFallingNode(FallingNodeView node)
    {
        node.OnArrived -= HandleFallingNodeArrived;
        Pool.Instance.Return(PoolKey.FallingNode, node);
    }
}
