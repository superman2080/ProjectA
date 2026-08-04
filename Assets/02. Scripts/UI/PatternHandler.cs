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

    [Header("Focus Ring")]
    [SerializeField] private RectTransform focusRingParent;
    [SerializeField] private Color[] focusRingColorPalette;
    [Tooltip("링의 시작 크기 배율. 노브 크기의 몇 배에서 줄어들기 시작할지.")]
    [SerializeField] private float focusRingStartScale = 2f;

    [Header("Knob")]
    [Tooltip("노브(Point/Visual) 표시·숨김 페이드 시간(초).")]
    [SerializeField] private float knobFadeDuration = 0.12f;

#if UNITY_EDITOR
    [Header("Debug (Editor Only)")]
    [SerializeField] private Pattern debugTestPattern;
    [Tooltip("씬 시작에 위 패턴을 자동 투입한다. 끄면 우클릭 메뉴 Debug: Set Test Pattern으로만 투입된다.\n" +
             "켜면 EnemyDirector.PrepareStage보다 먼저 터져 적 연출이 빠진 채 판정만 돈다.")]
    [SerializeField] private bool debugAutoRunOnStart = false;
    [Tooltip("첫 노드까지의 여유(초). 링을 보고 준비할 시간.")]
    [SerializeField] private float debugLeadTime = 1.0f;
    [Tooltip("노드 간 간격(초). 채보 최소 간격이 0.4초다.")]
    [SerializeField] private float debugNodeInterval = 0.4f;
    [Tooltip("비우거나 노드 수와 다르면 위 두 값으로 자동 생성한다. 특정 리듬을 시험할 때만 채운다.")]
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

    /// <summary>
    /// 채보 없이 패턴 하나를 지금 투입한다. <c>debugInputTimes</c>가 비었거나 노드 수와 안 맞으면
    /// <b>균등 간격으로 자동 생성</b>한다 — 패턴을 바꿀 때마다 배열을 손보지 않아도 되게.
    ///
    /// <para>적 연출까지 보려면 <b>먼저 <c>EnemyDirector</c>의 <c>Debug: Prepare Stage</c></b>를 눌러야 한다.
    /// 정상 재생에서는 <c>ChartPlayer</c>가 카운트다운에서 무대를 세우지만 이 경로엔 그게 없다.</para>
    /// </summary>
    [ContextMenu("Debug: Set Test Pattern")]
    private void DebugSetTestPattern()
    {
        if (debugTestPattern == null)
        {
            Debug.LogWarning("[PatternHandler] debugTestPattern이 비어 있습니다.", this);
            return;
        }

        int nodeCount = debugTestPattern.AllData.Count;
        float[] times = debugInputTimes != null && debugInputTimes.Length == nodeCount
            ? debugInputTimes
            : BuildDebugTimes(nodeCount);

        SetPattern(debugTestPattern, times,
            null,
            debugExposureDurations != null && debugExposureDurations.Length == nodeCount ? debugExposureDurations : null);

        Debug.Log($"[PatternHandler] 디버그 투입 '{debugTestPattern.name}' — 노드 {nodeCount}개, " +
                  $"첫 입력 +{times[0]:0.00}s, 마지막 +{times[nodeCount - 1]:0.00}s.", this);
    }

    /// <summary>균등 간격 입력 시각(투입 시각 기준 상대). 채보 최소 간격(0.4초)을 기본으로 삼는다.</summary>
    private float[] BuildDebugTimes(int nodeCount)
    {
        var times = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++) times[i] = debugLeadTime + i * debugNodeInterval;
        return times;
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

    /// <summary>
    /// Good 판정 윈도우(초). 외부 연출이 <c>Deadline = LastNodeTime + GoodWindow</c>로
    /// 성패 확정 시각을 만드는 데 쓴다 — 표적 절단·칼날 임팩트가 맞춰지는 그 시각이다.
    /// </summary>
    public float GoodWindow => goodWindow;

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
    private readonly bool[] knobUsage = new bool[9];
    private Canvas canvas;
    private Camera canvasCamera;
    private bool isKeyboardStroke;

    private struct ScheduledSpawn
    {
        public ActivePattern owner;
        public int position;
        public float spawnTime;
        public float shrinkDuration; // 링이 줄어드는 데 걸리는 시간(= 노출 시간)
    }

    private struct ActiveNode
    {
        public ActivePattern owner;
        public int position;
        public FocusRingView view;
    }

    private readonly List<ScheduledSpawn> scheduledSpawns = new List<ScheduledSpawn>();
    private readonly List<ActiveNode> activeFocusRings = new List<ActiveNode>();
    private int spawnCounter;

    public event Action<JudgementResult, int> OnJudged;
    public event Action<PatternCompletionInfo> OnPatternComplete; // 패턴 완료(완주/만료) 순간의 페이로드. 성공/실패·타이밍·다음 패턴 정보를 담는다.

    /// <summary>확장 포인트: 패턴이 <b>큐에 투입되는 순간</b>(판정 대상이 되기 훨씬 전). 등장에 시간이 걸리는 연출(베이는 표적)이 구독한다.</summary>
    public event Action<PatternQueuedInfo> OnPatternQueued;
    /// <summary>확장 포인트: <see cref="ClearAllPatterns"/>로 전부 정리된 순간(곡 중단 등). 외부 연출이 잔존물을 회수하는 데 쓴다.</summary>
    public event Action OnAllPatternsCleared;

    /// <summary>판정 대상이 선두가 되는 순간(최초 투입/승계). 캐릭터 액션이 성공 애니 조기 시작을 예약하는 데 쓴다.</summary>
    public event Action<JudgeTargetInfo> OnJudgeTargetBegan;
    /// <summary>판정 대상의 AllCorrect가 처음 깨지는 순간(오답/타이밍Miss 공통, 패턴당 1회). 힛 애니 재생·성공 애니 취소에 쓴다.</summary>
    public event Action OnJudgeTargetFirstMiss;
    /// <summary>확장 포인트: 새 노드가 라인에 연결될 때마다 (index, 월드 좌표) 전달.</summary>
    public event Action<int, Vector3> OnNodeConnected;

    /// <summary>확장 포인트(캐릭터 액션): 포커스 링이 스폰될 때 (Point 인덱스, 노드 타입, 월드 좌표) 전달.</summary>
    public event Action<int, NodeType, Vector3> OnFocusRingSpawned;
    /// <summary>확장 포인트(캐릭터 액션): 플레이어가 포커스 링을 실제로 맞췄을 때 (Point 인덱스, 노드 타입, 월드 좌표, 판정 결과) 전달.</summary>
    public event Action<int, NodeType, Vector3, JudgementResult> OnFocusRingResolved;
    /// <summary>확장 포인트(캐릭터 액션): 포커스 링이 맞지 않은 채 수축을 끝냈을 때 (Point 인덱스, 노드 타입, 월드 좌표) 전달.</summary>
    public event Action<int, NodeType, Vector3> OnFocusRingMissedArrival;

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
        ApplyKnobVisibility(0f); // 패턴 없는 초기 상태 → 9개 전부 숨김. 첫 프레임 깜빡임을 막으려 즉시 적용한다.

        // 자동 투입은 옵트인이다. 켜 두면 적 무대(EnemyDirector.PrepareStage)가 서기 전에 패턴이 터져
        // 전투 연출이 통째로 빠진 채 판정만 도는 그림이 된다.
        if (debugAutoRunOnStart) DebugSetTestPattern();
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
        ProcessFocusRingSpawns();

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
    /// 없으면(디버그/수동 테스트 경로) <paramref name="exposureDurations"/>(없으면 기본값)를 그대로 수축 시간으로 쓴다.
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
            float shrinkDuration;

            if (spawnTimes != null && i < spawnTimes.Count)
            {
                spawnOffset = spawnTimes[i];
                shrinkDuration = active.GetInputTime(i) - spawnOffset;
            }
            else
            {
                float exposureDuration = exposureDurations != null && i < exposureDurations.Count ? exposureDurations[i] : DefaultExposureDuration;
                shrinkDuration = ComputeFallDuration(pointIndex, exposureDuration);
                spawnOffset = active.GetInputTime(i) - shrinkDuration;
            }

            scheduledSpawns.Add(new ScheduledSpawn
            {
                owner = active,
                position = i,
                spawnTime = active.StartTime + spawnOffset,
                shrinkDuration = shrinkDuration
            });
        }

        bool becomesJudgeTarget = activePatterns.Count == 0;
        activePatterns.Add(active);

        // 판정 대상이 되는지와 무관하게 호출한다 — 큐에 얹히기만 한 패턴도 링은 지금부터 스폰되므로,
        // 그 링이 앉을 노브가 같은 시각에 떠 있어야 한다(감춰진 노브 위에서 링이 줄어들면 타이밍 단서가 깨진다).
        ApplyKnobVisibility(knobFadeDuration);

        // 큐 투입 이벤트는 becomesJudgeTarget 분기보다 먼저 낸다 — 두 이벤트의 순서가 얽히지 않게.
        OnPatternQueued?.Invoke(new PatternQueuedInfo(
            active.Template, active.StartTime, active.FirstNodeTime, active.LastNodeTime, active.Deadline,
            active.BuildNodeTimes()));

        if (becomesJudgeTarget)
        {
            RefreshJudgeTargetVisuals();
            RaiseJudgeTargetBegan();
        }
    }

    /// <summary>현재 판정 대상이 있으면 "판정 대상 시작" 이벤트를 발행한다(없으면 아무것도 안 함).</summary>
    private void RaiseJudgeTargetBegan()
    {
        var target = JudgeTarget;
        if (target != null)
            OnJudgeTargetBegan?.Invoke(new JudgeTargetInfo(target.Template, target.FirstNodeTime, target.LastNodeTime, target.Deadline));
    }

    /// <summary>곡 중단 등으로 진행 중인 모든 패턴과 포커스 링을 정리한다.</summary>
    public void ClearAllPatterns()
    {
        for (int i = activeFocusRings.Count - 1; i >= 0; i--)
            ReleaseFocusRing(activeFocusRings[i].view);
        activeFocusRings.Clear();
        scheduledSpawns.Clear();
        activePatterns.Clear();

        ResetPointColors();
        TriggerLineFadeOut();
        RefreshJudgeTargetVisuals();
        ApplyKnobVisibility(knobFadeDuration);

        OnAllPatternsCleared?.Invoke();
    }

    /// <summary>씬/캔버스 참조가 아직 없으면(에디터에서 Play 모드 없이 굽는 툴이 호출하는 경우 포함) 초기화한다.</summary>
    private void EnsureLayoutInitialized()
    {
        if (canvas != null) return;
        canvas = GetComponentInParent<Canvas>();
        canvasCamera = canvas != null ? canvas.worldCamera : null;
    }

    /// <summary>
    /// 노출 시간을 그대로 수축 시간으로 돌려준다. 링은 Point 자리에서 크기만 줄어들 뿐 이동하지 않으므로
    /// 화면 기하가 개입할 여지가 없다 — 낙하 노드 시절 행마다 속도를 역산하던 계산은 사라졌다.
    /// <paramref name="pointIndex"/>는 쓰이지 않지만 굽기 툴(PatternChartWindow)이 호출하는 시그니처라 유지한다.
    /// </summary>
    public float ComputeFallDuration(int pointIndex, float exposureDuration)
    {
        return exposureDuration;
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

    /// <summary>
    /// 살아 있는 <b>모든</b> 패턴(<see cref="activePatterns"/>)이 쓰는 Point의 노브만 표시한다.
    ///
    /// 판정 대상(<see cref="JudgeTarget"/>) 기준인 <see cref="ApplyHitAreas"/>와 기준이 다르다는 점에 주의 —
    /// 링은 큐에 얹히기만 한 패턴에도 스폰되므로, 판정 대상만 기준으로 하면 아직 감춰진 노브 위에서
    /// 다음 패턴의 링이 줄어드는 구간이 생긴다. 판정 영역 축소는 입력 오인 방지가 목적이라 지금처럼
    /// 판정 대상 기준을 유지하고, 노브 표시는 시선 유도가 목적이라 합집합으로 잡는다.
    ///
    /// 합집합이라 "이전 패턴의 마지막 노드 = 다음 패턴의 첫 노드"가 같은 Point인 인수인계 구간에서도
    /// 노브가 꺼졌다 켜지지 않는다.
    /// </summary>
    private void ApplyKnobVisibility(float duration)
    {
        for (int i = 0; i < knobUsage.Length; i++)
            knobUsage[i] = false;

        foreach (var active in activePatterns)
        {
            foreach (var data in active.Template.AllData)
                knobUsage[data.index] = true;
        }

        for (int i = 0; i < patternPoints.Length; i++)
            patternPoints[i].SetKnobVisible(knobUsage[i], duration);
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
            bool wasCorrect = target.AllCorrect;
            target.MarkIncorrect();
            if (wasCorrect) OnJudgeTargetFirstMiss?.Invoke();
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
            bool wasCorrect = target.AllCorrect;
            target.MarkIncorrect();
            if (wasCorrect) OnJudgeTargetFirstMiss?.Invoke();
            lineRenderer?.SetCorrectState(false);
        }

        patternPoints[index].SetJudgementColor(result);

        int position = target.CurrentPosition;
        NodeType nodeType = target.GetNodeType(position);
        Vector3 worldPosition = patternPoints[index].transform.position;

        ReleaseRingOf(target, position);

        OnJudged?.Invoke(result, index);
        OnFocusRingResolved?.Invoke(index, nodeType, worldPosition, result);

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
        ApplyKnobVisibility(knobFadeDuration); // 남은 패턴들이 안 쓰는 노브만 페이드아웃
        RaiseJudgeTargetBegan(); // 승계된 다음 판정 대상의 성공 애니 예약 (다음이 없으면 no-op)
    }

    private void ResetPointColors()
    {
        foreach (var p in patternPoints)
            p.ResetColor();
    }

    private void ProcessFocusRingSpawns()
    {
        for (int i = scheduledSpawns.Count - 1; i >= 0; i--)
        {
            if (Time.time < scheduledSpawns[i].spawnTime) continue;

            SpawnFocusRing(scheduledSpawns[i].owner, scheduledSpawns[i].position, scheduledSpawns[i].shrinkDuration);
            scheduledSpawns.RemoveAt(i);
        }
    }

    private void SpawnFocusRing(ActivePattern owner, int position, float duration)
    {
        int pointIndex = owner.GetPointIndex(position);
        Color color = focusRingColorPalette != null && focusRingColorPalette.Length > 0
            ? focusRingColorPalette[spawnCounter % focusRingColorPalette.Length]
            : Color.white;
        spawnCounter++;

        Vector3 worldPosition = patternPoints[pointIndex].transform.position;
        Vector2 targetLocalPos = WorldToLocal(focusRingParent, worldPosition);
        NodeType nodeType = owner.GetNodeType(position);

        var ring = Pool.Instance.Get<FocusRingView>(PoolKey.FocusRing, r =>
        {
            r.transform.SetParent(focusRingParent, false);
            r.Initialize(pointIndex + 1, color, nodeType, targetLocalPos, focusRingStartScale, duration);
        });
        ring.OnArrived += HandleFocusRingArrived;

        activeFocusRings.Add(new ActiveNode { owner = owner, position = position, view = ring });

        OnFocusRingSpawned?.Invoke(pointIndex, nodeType, worldPosition);
    }

    private void HandleFocusRingArrived(FocusRingView ring)
    {
        ring.OnArrived -= HandleFocusRingArrived;

        RemoveRingEntry(ring);

        Vector3 worldPosition = patternPoints[ring.PointIndex].transform.position;
        OnFocusRingMissedArrival?.Invoke(ring.PointIndex, ring.Type, worldPosition);

        Pool.Instance.Return(PoolKey.FocusRing, ring);
    }

    private void RemoveRingEntry(FocusRingView ring)
    {
        for (int i = 0; i < activeFocusRings.Count; i++)
        {
            if (activeFocusRings[i].view != ring) continue;

            activeFocusRings.RemoveAt(i);
            return;
        }
    }

    /// <summary>판정된 링 하나를 회수한다.</summary>
    private void ReleaseRingOf(ActivePattern owner, int position)
    {
        for (int i = 0; i < activeFocusRings.Count; i++)
        {
            if (activeFocusRings[i].owner != owner || activeFocusRings[i].position != position) continue;

            ReleaseFocusRing(activeFocusRings[i].view);
            activeFocusRings.RemoveAt(i);
            return;
        }
    }

    /// <summary>패턴 완료/만료 시 그 패턴에 속한 포커스 링과 남은 스폰 예약을 즉시 정리한다.</summary>
    private void ClearNodesOf(ActivePattern owner)
    {
        for (int i = activeFocusRings.Count - 1; i >= 0; i--)
        {
            if (activeFocusRings[i].owner != owner) continue;

            ReleaseFocusRing(activeFocusRings[i].view);
            activeFocusRings.RemoveAt(i);
        }

        for (int i = scheduledSpawns.Count - 1; i >= 0; i--)
        {
            if (scheduledSpawns[i].owner != owner) continue;

            scheduledSpawns.RemoveAt(i);
        }
    }

    private void ReleaseFocusRing(FocusRingView ring)
    {
        ring.OnArrived -= HandleFocusRingArrived;
        Pool.Instance.Return(PoolKey.FocusRing, ring);
    }
}
