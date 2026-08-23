using System.Collections.Generic;
using EnemySpace;
using PatternSpace;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 패턴이 소유한 월드 이펙트 큐를 편집·저장하는 창.
///
/// <para><b>이 창의 핵심은 타임라인과 프리뷰다.</b> 이펙트 값 자체는 인스펙터로도 만질 수 있지만,
/// "칼이 어디를 언제 지나가는가"를 보지 않으면 붙이는 작업이 눈대중이 된다. 그래서
/// <b>애니메이션과 파티클을 같은 <c>t</c>로 함께 굴리고</b>, 칼날 경로를 선으로 그리고,
/// 그 선을 클릭하면 그 프레임 시각이 큐의 <c>timeOffset</c>으로 들어간다.</para>
///
/// <para><b>시간축은 임팩트 기준 상대시간</b>이다(<c>Tools/Animation Clip Trimmer</c>와 같은 규약).
/// 채보의 실제 노드 간격은 곡마다 다르므로 <see cref="nodeInterval"/>로 가정하고, 그 가정이
/// 화면에 그대로 적혀 있다 — 큐의 시각 계산 자체는 런타임과 <b>같은 함수</b>
/// (<see cref="PatternEffectCue.ResolveTime"/>)를 쓴다.</para>
/// </summary>
public class PatternEffectWindow : EditorWindow
{
    private const string TemplateFolder = "Assets/04. Datas/Patterns/Templates";
    private const float PreviewHeight = 300f;
    private const float TimelineHeight = 96f;
    private const int BladeSamples = 64;

    private const string PrefKeyPlayer = "PatternEffectWindow.PlayerPrefab";
    private const string PrefKeyEnemy = "PatternEffectWindow.EnemyPrefab";

    [MenuItem("Tools/Pattern Effect Tool")]
    public static void Open() => GetWindow<PatternEffectWindow>("Pattern Effect");

    /// <summary>패턴을 지정해 연다. 다른 툴에서 넘어올 때 쓴다.</summary>
    public static void Open(Pattern pattern)
    {
        var window = GetWindow<PatternEffectWindow>("Pattern Effect");
        window.Select(pattern);
    }

    // ── 대상 ────────────────────────────────────────────────────────────────
    private Pattern target;
    private SerializedObject serialized;
    private SerializedProperty cuesProperty;
    private ReorderableList cueList;

    private readonly List<Pattern> templates = new List<Pattern>();
    private Vector2 templateScroll;
    private Vector2 detailScroll;

    // ── 시간축 가정 ─────────────────────────────────────────────────────────
    [SerializeField] private float nodeInterval = 0.4f;
    [SerializeField] private float exposureDuration = 0.5f;
    [SerializeField] private float goodWindow = 0.1f;

    private float t;
    private bool isPlaying;
    private double lastUpdateTime;

    // ── 프리뷰 ──────────────────────────────────────────────────────────────
    private GameObject playerPrefab;
    private GameObject enemyPrefab;
    private string bladeNodeName = "Weapon_Katana_01_Blade";
    private float duelDistance = 1f;

    private PreviewRenderUtility previewUtil;
    private GameObject playerInstance;
    private GameObject enemyInstance;
    private GameObject cachedPlayerPrefab;
    private GameObject cachedEnemyPrefab;

    private BladePath bladePath;
    private Transform bladeNode;

    private readonly Dictionary<PatternEffectCue, GameObject> previewEffects = new Dictionary<PatternEffectCue, GameObject>();

    // 칼날 경로 캐시 — 매 리페인트마다 클립을 64번 샘플링하면 창이 느려진다.
    private readonly List<Vector3> bladeWorldPath = new List<Vector3>();
    private readonly List<float> bladePathTimes = new List<float>();
    private string bladeCacheKey;

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        lastUpdateTime = EditorApplication.timeSinceStartup;

        playerPrefab = LoadPref(PrefKeyPlayer);
        enemyPrefab = LoadPref(PrefKeyEnemy);

        ReloadTemplates();
        if (target == null && templates.Count > 0) Select(templates[0]);
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        ClearPreview();
        previewUtil?.Cleanup();
        previewUtil = null;
    }

    private static GameObject LoadPref(string key)
    {
        string guid = EditorPrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(guid)) return null;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void SavePref(string key, GameObject value)
    {
        string path = value != null ? AssetDatabase.GetAssetPath(value) : null;
        EditorPrefs.SetString(key, string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
    }

    private void OnEditorUpdate()
    {
        if (!isPlaying) return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastUpdateTime);
        lastUpdateTime = now;

        var (min, max) = ScrubRange();
        t += dt;
        if (t > max) t = min;

        Repaint();
    }

    // ── 대상 로딩 ───────────────────────────────────────────────────────────

    private void ReloadTemplates()
    {
        templates.Clear();

        string[] guids = AssetDatabase.IsValidFolder(TemplateFolder)
            ? AssetDatabase.FindAssets("t:Pattern", new[] { TemplateFolder })
            : AssetDatabase.FindAssets("t:Pattern");

        foreach (string guid in guids)
        {
            var pattern = AssetDatabase.LoadAssetAtPath<Pattern>(AssetDatabase.GUIDToAssetPath(guid));
            if (pattern != null) templates.Add(pattern);
        }

        templates.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    private void Select(Pattern pattern)
    {
        // 패턴을 갈아타기 전에 먼저 기록한다 — 안 그러면 '저장 안 됨' 표시가 다른 에셋을 가리키게 된다.
        if (hasUnsavedChanges && target != null && target != pattern) SaveChanges();

        target = pattern;
        ClearPreview();
        bladeCacheKey = null;

        if (target == null)
        {
            serialized = null;
            cuesProperty = null;
            cueList = null;
            return;
        }

        serialized = new SerializedObject(target);
        cuesProperty = serialized.FindProperty("effectCues");
        BuildCueList();

        var (min, _) = ScrubRange();
        t = min;
    }

    private void BuildCueList()
    {
        cueList = new ReorderableList(serialized, cuesProperty, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "이펙트 큐 (위에서 아래 순서는 의미 없음 — 시각은 각 큐가 든다)"),
            elementHeight = EditorGUIUtility.singleLineHeight + 4f,
            drawElementCallback = DrawCueRow
        };
    }

    private void DrawCueRow(Rect rect, int index, bool active, bool focused)
    {
        var cue = CueAt(index);
        if (cue == null) return;

        rect.y += 2f;
        rect.height = EditorGUIUtility.singleLineHeight;

        float times = ResolveCueTime(cue);
        bool valid = cue.IsTimingValid(times, LastNodeTime());
        bool wired = cue.IsUsable;

        var color = GUI.color;
        if (!wired) GUI.color = new Color(1f, 1f, 1f, 0.45f);
        else if (!valid) GUI.color = new Color(1f, 0.5f, 0.45f);

        float w = rect.width;
        var labelRect = new Rect(rect.x, rect.y, w * 0.30f, rect.height);
        var sfxRect = new Rect(labelRect.xMax, rect.y, w * 0.04f, rect.height);
        var condRect = new Rect(sfxRect.xMax, rect.y, w * 0.18f, rect.height);
        var timeRect = new Rect(condRect.xMax, rect.y, w * 0.26f, rect.height);
        var anchorRect = new Rect(timeRect.xMax, rect.y, w * 0.22f, rect.height);

        EditorGUI.LabelField(labelRect, cue.Label);
        // 소리 뱃지 — 그림 없는 큐가 목록에서 빈 줄로 보이면 안 된다.
        if (cue.Sfx != null) EditorGUI.LabelField(sfxRect, "♪");
        EditorGUI.LabelField(condRect, cue.Condition.ToString());
        // 연타 타격 큐는 시각이 없다 — 사건에 붙으므로 오프셋도 배지도 의미가 없다.
        EditorGUI.LabelField(timeRect, cue.IsEventDriven
            ? "타격마다"
            : $"{cue.Timing}{(cue.Timing == EffectTiming.Node ? $"[{cue.NodeIndex}]" : "")} {Signed(cue.TimeOffset)}");
        EditorGUI.LabelField(anchorRect, cue.Anchor + (cue.Follow ? " (follow)" : ""));

        GUI.color = color;
    }

    private static string Signed(float v) => v >= 0f ? $"+{v:0.00}s" : $"{v:0.00}s";

    private PatternEffectCue CueAt(int index)
    {
        if (target == null || target.EffectCues == null) return null;
        return index >= 0 && index < target.EffectCues.Count ? target.EffectCues[index] : null;
    }

    private PatternEffectCue SelectedCue() => cueList != null ? CueAt(cueList.index) : null;

    // ── 시간축 ──────────────────────────────────────────────────────────────
    //
    // 전부 '임팩트 기준 상대시간'이다. 실제 채보의 노드 간격은 곡마다 다르므로 가정값을 쓰고,
    // 그 가정을 화면에 적어 둔다 — 큐의 시각 계산 자체는 런타임과 같은 함수를 쓴다.

    private int NodeCount() => target != null && target.AllData != null ? Mathf.Max(target.AllData.Count, 1) : 1;
    private float DeadlineTime() => -(target != null ? target.ImpactOffset : 0f);
    private float LastNodeTime() => DeadlineTime() - goodWindow;
    private float FirstNodeTime() => LastNodeTime() - (NodeCount() - 1) * nodeInterval;
    private float StartTime() => FirstNodeTime() - exposureDuration;

    private float[] NodeTimes()
    {
        int count = NodeCount();
        var times = new float[count];
        float first = FirstNodeTime();
        for (int i = 0; i < count; i++) times[i] = first + i * nodeInterval;
        return times;
    }

    /// <summary>
    /// 이 큐가 발사되는 시각(임팩트 기준 상대초).
    ///
    /// <para><b>⚠ 연타 타격 큐는 시각이 없다</b> — 사건에 붙기 때문이다. 그래도 <b>0(임팩트)을 돌려준다</b>:
    /// "언제"는 못 보여 줘도 <b>"어디에 어떤 크기로 뜨는가"는 봐야 저작이 된다</b>. 3D 프리뷰가 이 값으로
    /// 파티클을 세우고, t를 0 이후로 끌면 수명이 흐르는 모습이 보인다.</para>
    ///
    /// <para>타임라인 막대와 스크럽 범위에서는 여전히 제외된다 — 그쪽에 그리면 "임팩트에 한 번 뜬다"는
    /// 거짓말이 되기 때문이다(<see cref="PatternEffectCue.IsEventDriven"/>).</para>
    /// </summary>
    private float ResolveCueTime(PatternEffectCue cue) => cue.IsEventDriven
        ? 0f
        : cue.ResolveTime(
            StartTime(), FirstNodeTime(), LastNodeTime(), DeadlineTime(),
            NodeTimes(), target != null ? target.ImpactOffset : 0f);

    private (float min, float max) ScrubRange()
    {
        float min = StartTime();
        float max = 0.6f;   // 임팩트 뒤 여운

        if (target != null && target.EffectCues != null)
        {
            foreach (var cue in target.EffectCues)
            {
                if (cue == null || !cue.IsUsable || cue.IsEventDriven) continue;

                float time = ResolveCueTime(cue);
                min = Mathf.Min(min, time);
                max = Mathf.Max(max, time + EffectDuration(cue));
            }
        }

        return (min, Mathf.Max(max, min + 0.2f));
    }

    /// <summary>
    /// 이 큐가 시간축을 차지하는 대략의 길이. 배속이 지속시간을 함께 줄인다.
    /// <b>소리 전용 큐는 클립 길이를 쓴다</b> — 0을 돌려주면 타임라인에 길이 0 막대로 떠서 안 보인다.
    /// </summary>
    private static float EffectDuration(PatternEffectCue cue)
    {
        if (cue.Prefab == null) return cue.Sfx != null ? cue.Sfx.length : 0f;

        float longest = 0f;
        foreach (var ps in cue.Prefab.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
        }

        return longest / Mathf.Max(cue.Speed, 0.01f);
    }

    // ── GUI ─────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        DrawToolbar();

        if (target == null)
        {
            EditorGUILayout.HelpBox("패턴을 선택하세요.", MessageType.Info);
            DrawTemplateList(position.height - 80f);
            return;
        }

        // 도메인 리로드를 지나면 target(UnityEngine.Object)은 살아남지만 SerializedObject·ReorderableList는 죽는다.
        // 컴파일 직후 창이 그려질 때 여기로 들어오므로, 없으면 조용히 다시 만든다.
        if (serialized == null || serialized.targetObject == null || cuesProperty == null || cueList == null)
        {
            Select(target);
            if (serialized == null) return;
        }

        serialized.Update();

        DrawPreview();
        DrawPreviewSettings();
        DrawTimeline();

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(200f)))
                DrawTemplateList(position.height - PreviewHeight - TimelineHeight
                    - (previewSettingsExpanded ? 230f : 130f));

            using (new EditorGUILayout.VerticalScope())
            {
                cueList.DoLayoutList();
                DrawSelectedCue();
            }
        }

        // 변경이 있었으면 에셋을 더티로 표시한다. 이게 없으면 값이 메모리에만 남고
        // 디스크에는 안 적힌다 — 기록은 Save가 한다(툴바 버튼 / Ctrl+S).
        if (serialized.ApplyModifiedProperties()) MarkDirty();
    }

    /// <summary>
    /// 저장할 것이 생겼음을 알린다. <b>창 제목에 * 가 붙고</b> 창을 닫을 때 Unity가 물어본다
    /// (<see cref="hasUnsavedChanges"/>는 EditorWindow 기본 기능이다 — 직접 만들지 않는다).
    /// </summary>
    private void MarkDirty()
    {
        if (target != null) EditorUtility.SetDirty(target);

        hasUnsavedChanges = true;
        saveChangesMessage = target != null
            ? $"'{target.name}'의 이펙트 큐 변경이 저장되지 않았습니다."
            : "이펙트 큐 변경이 저장되지 않았습니다.";
    }

    /// <summary>실제 디스크 기록. 창을 닫을 때 Unity가 부르고, 툴바 저장 버튼도 이걸 부른다.</summary>
    public override void SaveChanges()
    {
        if (target != null) AssetDatabase.SaveAssetIfDirty(target);
        else AssetDatabase.SaveAssets();

        hasUnsavedChanges = false;
        base.SaveChanges();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
            {
                if (GUILayout.Button(hasUnsavedChanges ? "저장 *" : "저장", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    SaveChanges();
            }

            if (GUILayout.Button("패턴 새로고침", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                ReloadTemplates();

            GUILayout.Space(8f);

            EditorGUI.BeginChangeCheck();
            isPlaying = GUILayout.Toggle(isPlaying, isPlaying ? "■ 정지" : "▶ 재생", EditorStyles.toolbarButton, GUILayout.Width(60f));
            if (EditorGUI.EndChangeCheck()) lastUpdateTime = EditorApplication.timeSinceStartup;

            GUILayout.FlexibleSpace();

            EditorGUIUtility.labelWidth = 70f;
            nodeInterval = EditorGUILayout.FloatField("노드 간격", nodeInterval, EditorStyles.toolbarTextField, GUILayout.Width(130f));
            exposureDuration = EditorGUILayout.FloatField("노출", exposureDuration, EditorStyles.toolbarTextField, GUILayout.Width(110f));
            goodWindow = EditorGUILayout.FloatField("Good창", goodWindow, EditorStyles.toolbarTextField, GUILayout.Width(120f));
            EditorGUIUtility.labelWidth = 0f;
        }
    }

    private void DrawTemplateList(float height)
    {
        using (var scope = new EditorGUILayout.ScrollViewScope(templateScroll, GUILayout.Height(Mathf.Max(height, 80f))))
        {
            templateScroll = scope.scrollPosition;

            foreach (var pattern in templates)
            {
                int count = pattern.EffectCues != null ? pattern.EffectCues.Count : 0;
                bool selected = pattern == target;

                using (new EditorGUILayout.HorizontalScope(selected ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (GUILayout.Button(pattern.name, EditorStyles.label))
                        Select(pattern);

                    GUILayout.Label(count > 0 ? $"● {count}" : "○", GUILayout.Width(28f));
                }
            }
        }
    }

    private void DrawSelectedCue()
    {
        if (cueList == null || cueList.index < 0 || cueList.index >= cuesProperty.arraySize)
        {
            EditorGUILayout.HelpBox("큐를 선택하면 상세가 여기 뜹니다.", MessageType.None);
            return;
        }

        var element = cuesProperty.GetArrayElementAtIndex(cueList.index);
        var cue = SelectedCue();

        using (var scope = new EditorGUILayout.ScrollViewScope(detailScroll))
        {
            detailScroll = scope.scrollPosition;

            EditorGUILayout.PropertyField(element.FindPropertyRelative("label"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("prefab"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("언제", EditorStyles.boldLabel);
            DrawConditionField(element, cue);
            EditorGUILayout.PropertyField(element.FindPropertyRelative("timing"));

            if (cue != null && cue.Timing == EffectTiming.Node)
                EditorGUILayout.PropertyField(element.FindPropertyRelative("nodeIndex"));

            EditorGUILayout.PropertyField(element.FindPropertyRelative("timeOffset"));

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.FloatField(new GUIContent("Pattern.ImpactOffset",
                    "패턴이 소유한 공통 앵커 보정. 칼·적·시체·투사체·카메라가 전부 이 값을 읽는다 — 여기서는 못 고친다."),
                    target.ImpactOffset);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("어디에", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(element.FindPropertyRelative("anchor"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("follow"));

            if (cue != null && cue.UsesBlade)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(element.FindPropertyRelative("bladeT"));
                if (EditorGUI.EndChangeCheck()) bladeCacheKey = null;   // 경로를 다시 그린다

                EditorGUILayout.PropertyField(element.FindPropertyRelative("alignToBlade"));
            }

            EditorGUILayout.PropertyField(element.FindPropertyRelative("positionOffset"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("rotationOffset"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("소리", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(element.FindPropertyRelative("sfx"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("sfxVolume"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("sfxPitch"));
            EditorGUILayout.HelpBox(
                "소리는 2D로 재생되고 앵커를 쓰지 않습니다 — 프리팹 없이 이것만 채우면 '소리 전용 큐'입니다.\n" +
                "프리뷰는 소리를 내지 않습니다(스크럽 되감기가 오디오에는 없어 저작을 방해합니다).",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("재생", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(element.FindPropertyRelative("scale"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("speed"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("poolSize"));

            DrawCueWarnings(cue);
        }
    }

    /// <summary>
    /// 조건 드롭다운. <b><c>Attacker.Enemy</c>에서는 <c>Parry</c>를 감춘다</b> —
    /// 그 역할에서는 적이 막지 않으므로 고를 수 있는 것 자체가 함정이다.
    /// </summary>
    private void DrawConditionField(SerializedProperty element, PatternEffectCue cue)
    {
        var property = element.FindPropertyRelative("condition");
        bool enemyAttacks = target.Attacker == Attacker.Enemy;

        var values = enemyAttacks
            ? new[] { EffectCondition.Always, EffectCondition.Success, EffectCondition.Evade }
            : new[] { EffectCondition.Always, EffectCondition.Success, EffectCondition.Parry, EffectCondition.Evade };

        var labels = new GUIContent[values.Length];
        for (int i = 0; i < values.Length; i++)
            labels[i] = new GUIContent($"{values[i]}  —  {DescribeCondition(values[i], enemyAttacks)}");

        int current = Mathf.Max(System.Array.IndexOf(values, (EffectCondition)property.enumValueIndex), 0);

        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(new GUIContent("condition"), current, labels);
        if (EditorGUI.EndChangeCheck())
            property.enumValueIndex = (int)values[picked];
    }

    private static string DescribeCondition(EffectCondition condition, bool enemyAttacks)
    {
        switch (condition)
        {
            case EffectCondition.Always: return "성패와 무관";
            case EffectCondition.Success: return enemyAttacks ? "받아쳐 밀어냄 (knockBack)" : "벤다";
            case EffectCondition.Parry: return "적이 제자리에서 막음 (parry)";
            default: return enemyAttacks ? "플레이어 피격 (evade)" : "적이 물러남 (evade)";
        }
    }

    private void DrawCueWarnings(PatternEffectCue cue)
    {
        if (cue == null) return;

        if (!cue.IsUsable)
        {
            EditorGUILayout.HelpBox("프리팹도 소리도 비어 있습니다 — 이 큐는 예약조차 되지 않습니다(정상적인 '무연출').", MessageType.None);
            return;
        }

        if (cue.IsEventDriven)
        {
            EditorGUILayout.HelpBox(
                "연타 타격마다 발사되는 큐입니다 — 시각이 아니라 사건에 붙으므로 timeOffset과 타임라인이 의미가 없습니다.\n" +
                "⚠ 조건은 Always여야 합니다(타격 순간에는 성패가 아직 안 정해집니다).\n" +
                "⚠ 풀 크기를 넉넉히: 초당 8타 × 이펙트 수명이 동시 인스턴스 수입니다.",
                cue.NeedsOutcome ? MessageType.Error : MessageType.Info);
            return;
        }

        float time = ResolveCueTime(cue);

        if (!cue.IsTimingValid(time, LastNodeTime()))
        {
            EditorGUILayout.HelpBox(
                $"조건이 {cue.Condition}인데 성패가 정해지는 LastNode({LastNodeTime():0.00}s)보다 이른 " +
                $"{time:0.00}s에 걸려 있습니다. 런타임이 폐기합니다 — 시각을 뒤로 옮기거나 조건을 Always로.",
                MessageType.Error);
        }

        // ⚠ 소리 전용 큐에서는 오탐이다 — 프리팹이 없는 것이 정상 상태다.
        if (cue.Prefab != null && cue.Prefab.GetComponentInChildren<ParticleSystem>(true) == null)
            EditorGUILayout.HelpBox("프리팹에 ParticleSystem이 없습니다 — 아무것도 보이지 않습니다.", MessageType.Warning);

        if (cue.Anchor == EffectAnchor.PlayerWeapon && !cue.Follow)
            EditorGUILayout.HelpBox("칼날 앵커인데 follow가 꺼져 있습니다 — 발사 순간 자리에만 남고 칼을 따라가지 않습니다.", MessageType.Info);
    }

    /// <summary>
    /// 프리뷰 배우 설정. <b>큐 상세 안이 아니라 프리뷰 바로 아래</b>에 산다 —
    /// 큐가 하나도 없는 새 패턴에서는 상세 영역 자체가 그려지지 않아, 거기 두면
    /// <b>프리팹을 지정할 방법이 없는 막다른 길</b>이 된다.
    /// </summary>
    private void DrawPreviewSettings()
    {
        // 아직 배우가 없으면 펼쳐 둔다 — 지금 필요한 것이 이것 하나뿐이다.
        if (playerPrefab == null) previewSettingsExpanded = true;

        previewSettingsExpanded = EditorGUILayout.Foldout(previewSettingsExpanded,
            playerPrefab != null ? $"프리뷰 배우  ({playerPrefab.name})" : "프리뷰 배우  — 플레이어 프리팹을 지정하세요", true);

        if (!previewSettingsExpanded) return;

        EditorGUI.BeginChangeCheck();
        playerPrefab = (GameObject)EditorGUILayout.ObjectField("플레이어 프리팹", playerPrefab, typeof(GameObject), false);
        enemyPrefab = (GameObject)EditorGUILayout.ObjectField("적 프리팹", enemyPrefab, typeof(GameObject), false);
        bladeNodeName = EditorGUILayout.TextField(new GUIContent("칼날 노드 이름",
            "같은 이름 노드가 2단이면 메쉬가 달린 '안쪽'을 찾는다."), bladeNodeName);
        duelDistance = EditorGUILayout.FloatField("결투 거리", duelDistance);

        if (EditorGUI.EndChangeCheck())
        {
            SavePref(PrefKeyPlayer, playerPrefab);
            SavePref(PrefKeyEnemy, enemyPrefab);
            bladeCacheKey = null;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            impactAnchorLocal = EditorGUILayout.Vector3Field(new GUIContent("임팩트 앵커 (플레이어 로컬)",
                "씬에서 ImpactAnchor는 플레이어의 자식이다. 그 로컬 좌표를 그대로 쓴다 — BattleScene 기본값은 (0, 0, 1), 즉 발밑 높이 1m 앞."),
                impactAnchorLocal);

            if (GUILayout.Button("씬에서 읽기", GUILayout.Width(90f)))
                PullImpactAnchorFromScene();
        }
    }

    // ── 타임라인 ────────────────────────────────────────────────────────────

    private void DrawTimeline()
    {
        Rect rect = GUILayoutUtility.GetRect(position.width, TimelineHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));

        var (min, max) = ScrubRange();
        float span = Mathf.Max(max - min, 0.01f);

        float X(float time) => rect.x + (time - min) / span * rect.width;

        // 기준점 눈금 — 패턴 진행 전체가 축이다(임팩트만이 아니다).
        DrawTick(rect, X(StartTime()), "start", new Color(0.5f, 0.5f, 0.5f));

        float[] nodes = NodeTimes();
        for (int i = 0; i < nodes.Length; i++)
            DrawTick(rect, X(nodes[i]), $"n{i}", new Color(0.45f, 0.6f, 0.75f));

        DrawTick(rect, X(LastNodeTime()), "last", new Color(0.9f, 0.75f, 0.35f));
        DrawTick(rect, X(0f), "impact", new Color(1f, 0.4f, 0.25f));

        // 큐 막대
        if (target.EffectCues != null)
        {
            float laneTop = rect.y + 28f;
            float laneHeight = 12f;

            for (int i = 0; i < target.EffectCues.Count; i++)
            {
                var cue = target.EffectCues[i];
                if (cue == null || !cue.IsUsable || cue.IsEventDriven) continue;   // 놓을 자리가 없다

                float time = ResolveCueTime(cue);
                float x0 = X(time);
                float x1 = X(time + EffectDuration(cue));
                float y = laneTop + (i % 4) * (laneHeight + 3f);

                bool valid = cue.IsTimingValid(time, LastNodeTime());
                Color color = valid ? ConditionColor(cue.Condition) : new Color(1f, 0.25f, 0.2f);

                EditorGUI.DrawRect(new Rect(x0, y, Mathf.Max(x1 - x0, 3f), laneHeight), color * new Color(1f, 1f, 1f, 0.55f));
                EditorGUI.DrawRect(new Rect(x0 - 1f, y, 2f, laneHeight), color);

                if (i == cueList.index)
                    EditorGUI.DrawRect(new Rect(x0 - 1f, y - 2f, 2f, laneHeight + 4f), Color.white);
            }
        }

        // 스크럽 헤드
        float headX = X(t);
        EditorGUI.DrawRect(new Rect(headX, rect.y, 1f, rect.height), Color.white);

        HandleTimelineInput(rect, min, span);

        var label = new Rect(rect.x + 4f, rect.yMax - 16f, rect.width - 8f, 14f);
        EditorGUI.LabelField(label,
            $"t = {t:0.000}s (임팩트 기준)   ·   노드 간격 {nodeInterval:0.00}s 가정   ·   드래그로 스크럽",
            EditorStyles.miniLabel);
    }

    private static Color ConditionColor(EffectCondition condition)
    {
        switch (condition)
        {
            case EffectCondition.Always: return new Color(0.6f, 0.6f, 0.65f);
            case EffectCondition.Success: return new Color(0.35f, 0.85f, 0.45f);
            case EffectCondition.Parry: return new Color(1f, 0.8f, 0.3f);
            default: return new Color(0.5f, 0.7f, 1f);
        }
    }

    private static void DrawTick(Rect rect, float x, string label, Color color)
    {
        EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height - 18f), color * new Color(1f, 1f, 1f, 0.7f));
        var labelRect = new Rect(x + 3f, rect.y + 2f, 60f, 14f);

        var previous = GUI.color;
        GUI.color = color;
        EditorGUI.LabelField(labelRect, label, EditorStyles.miniLabel);
        GUI.color = previous;
    }

    private void HandleTimelineInput(Rect rect, float min, float span)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown || (e.type == EventType.MouseDrag && e.button == 0))
        {
            t = min + (e.mousePosition.x - rect.x) / rect.width * span;
            isPlaying = false;
            e.Use();
            Repaint();
        }
    }

    // ── 프리뷰 ──────────────────────────────────────────────────────────────

    private void DrawPreview()
    {
        Rect rect = GUILayoutUtility.GetRect(position.width, PreviewHeight, GUILayout.ExpandWidth(true));

        if (playerPrefab == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.LabelField(rect, "바로 아래 '프리뷰 배우'에서 플레이어 프리팹을 지정하면 프리뷰가 뜹니다.",
                EditorStyles.centeredGreyMiniLabel);
            return;
        }

        HandlePreviewInput(rect);
        if (Event.current.type != EventType.Repaint) return;

        previewUtil ??= new PreviewRenderUtility();

        EnsureInstances();
        PoseActors();
        RefreshBladePath();
        SyncPreviewEffects();

        previewUtil.BeginPreview(rect, GUIStyle.none);
        SetupCameraAndLights();
        previewUtil.Render(true);
        var texture = previewUtil.EndPreview();
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);

        DrawBladeOverlay(rect);
    }

    private void EnsureInstances()
    {
        if (cachedPlayerPrefab != playerPrefab)
        {
            if (playerInstance != null) DestroyImmediate(playerInstance);
            playerInstance = null;
            cachedPlayerPrefab = playerPrefab;
            bladeCacheKey = null;

            if (playerPrefab != null)
            {
                playerInstance = Instantiate(playerPrefab);
                playerInstance.hideFlags = HideFlags.HideAndDontSave;
                previewUtil.AddSingleGO(playerInstance);
            }
        }

        if (cachedEnemyPrefab != enemyPrefab)
        {
            if (enemyInstance != null) DestroyImmediate(enemyInstance);
            enemyInstance = null;
            cachedEnemyPrefab = enemyPrefab;

            if (enemyPrefab != null)
            {
                enemyInstance = Instantiate(enemyPrefab);
                enemyInstance.hideFlags = HideFlags.HideAndDontSave;
                previewUtil.AddSingleGO(enemyInstance);
            }
        }
    }

    /// <summary>플레이어는 원점에서 +Z, 적은 결투 거리 앞에서 마주 본다(트리머·슬라이서와 같은 규약).</summary>
    private void PoseActors()
    {
        PoseActor(playerInstance, PlayerClip(), Vector3.zero, Quaternion.identity);
        PoseActor(enemyInstance, EnemyClip(), Vector3.forward * duelDistance, Quaternion.Euler(0f, 180f, 0f));
    }

    private ClipAlignment PlayerClip() =>
        target.Attacker == Attacker.Enemy ? target.PlayerParry : target.PlayerAttack;

    private ClipAlignment EnemyClip()
    {
        if (target.Attacker == Attacker.Enemy) return target.EnemyAttack;

        // 처치 확정(임팩트 0.1초 전)까지는 견제가, 그 뒤로는 사망이 애니메이터를 점유한다 — 런타임과 같은 진행.
        bool beforeHandoff = t < -goodWindow;
        var feint = target.EnemyFeint;
        var death = target.EnemyDeath;

        if (feint != null && feint.IsUsable && (beforeHandoff || death == null || !death.IsUsable)) return feint;
        return death;
    }

    /// <summary>
    /// 배우를 지금 <c>t</c>의 포즈로 세운다. <b>중립에서 샘플링한 뒤 배치를 합성</b>한다 —
    /// <see cref="AnimationClip.SampleAnimation"/>이 루트를 클립 값으로 덮어쓰기 때문이다(트리머와 같은 함정).
    /// </summary>
    private void PoseActor(GameObject instance, ClipAlignment alignment, Vector3 placement, Quaternion facing)
    {
        if (instance == null) return;

        var tr = instance.transform;
        tr.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        if (alignment != null && alignment.IsUsable)
        {
            float impact = alignment.StartOffset + alignment.ResolvedImpactSpan;
            float clipTime = DuetTimeline.ClampToTrim(
                DuetTimeline.ClipTimeOf(impact, alignment.Speed, t),
                alignment.StartOffset, alignment.StartOffset + alignment.ResolvedDuration);

            alignment.Clip.SampleAnimation(instance, clipTime);
        }

        Quaternion rootRotation = tr.localRotation;
        tr.SetPositionAndRotation(placement, facing * rootRotation);
    }

    private void SetupCameraAndLights()
    {
        Bounds b = CombinedBounds();

        var cam = previewUtil.camera;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 1000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

        float radius = Mathf.Max(b.extents.magnitude, 0.1f);
        float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * zoom;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 camPos = b.center + rot * new Vector3(0f, 0f, -dist);
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation(b.center - camPos);

        previewUtil.ambientColor = new Color(0.4f, 0.4f, 0.4f);
        if (previewUtil.lights.Length > 0)
        {
            previewUtil.lights[0].intensity = 1.1f;
            previewUtil.lights[0].transform.rotation = Quaternion.Euler(35f, 40f, 0f);
        }
        if (previewUtil.lights.Length > 1)
        {
            previewUtil.lights[1].intensity = 0.6f;
            previewUtil.lights[1].transform.rotation = Quaternion.Euler(-20f, -140f, 0f);
        }
    }

    private Bounds CombinedBounds()
    {
        bool any = false;
        var result = new Bounds(Vector3.forward * (duelDistance * 0.5f), Vector3.one * 2f);

        foreach (var instance in new[] { playerInstance, enemyInstance })
        {
            if (instance == null) continue;

            foreach (var r in instance.GetComponentsInChildren<Renderer>())
            {
                if (!any) { result = r.bounds; any = true; }
                else result.Encapsulate(r.bounds);
            }
        }

        return result;
    }

    private float yaw = 120f;
    private float pitch = 10f;
    private float zoom = 1.2f;
    private bool previewSettingsExpanded = true;

    /// <summary>임팩트 앵커의 <b>플레이어 로컬</b> 좌표. 씬 기본값(BattleScene) = 발밑 높이 1m 앞.</summary>
    private Vector3 impactAnchorLocal = new Vector3(0f, 0f, 1f);

    private void HandlePreviewInput(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDrag && e.button == 0 && !e.alt)
        {
            // Shift 드래그는 칼날 경로 클릭과 헷갈리지 않게 회전 전용으로 둔다.
            yaw += e.delta.x;
            pitch = Mathf.Clamp(pitch - e.delta.y, -89f, 89f);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.ScrollWheel)
        {
            zoom = Mathf.Clamp(zoom + e.delta.y * 0.05f, 0.2f, 5f);
            e.Use();
            Repaint();
        }
    }

    // ── 칼날 경로 ───────────────────────────────────────────────────────────

    private void RefreshBladePath()
    {
        if (playerInstance == null) return;

        if (bladeNode == null || !bladeNode.IsChildOf(playerInstance.transform))
        {
            bladeNode = FindBladeNode(playerInstance.transform);
            bladePath = new BladePath(bladeNode);
            bladeCacheKey = null;
        }

        var cue = SelectedCue();
        float bladeT = cue != null && cue.UsesBlade ? cue.BladeT : 1f;

        var clip = PlayerClip();
        string key = $"{(clip != null && clip.Clip != null ? clip.Clip.name : "-")}|{bladeT}|{duelDistance}|{playerInstance.GetInstanceID()}";
        if (key == bladeCacheKey) return;

        bladeCacheKey = key;
        bladeWorldPath.Clear();
        bladePathTimes.Clear();

        if (clip == null || !clip.IsUsable || bladePath == null || !bladePath.IsValid) return;

        // 트림 구간 전체를 훑어 '칼이 지나간 자리'를 만든다. 샘플링이 무거우므로 값이 바뀔 때만 다시 만든다.
        float impact = clip.StartOffset + clip.ResolvedImpactSpan;
        float relStart = DuetTimeline.RelativeOf(impact, clip.Speed, clip.StartOffset);
        float relEnd = DuetTimeline.RelativeOf(impact, clip.Speed, clip.StartOffset + clip.ResolvedDuration);

        for (int i = 0; i < BladeSamples; i++)
        {
            float rel = Mathf.Lerp(relStart, relEnd, i / (float)(BladeSamples - 1));
            PoseActor(playerInstance, clip, Vector3.zero, Quaternion.identity);

            float clipTime = DuetTimeline.ClampToTrim(
                DuetTimeline.ClipTimeOf(impact, clip.Speed, rel),
                clip.StartOffset, clip.StartOffset + clip.ResolvedDuration);
            clip.Clip.SampleAnimation(playerInstance, clipTime);

            bladeWorldPath.Add(bladePath.WorldPoint(bladeT));
            bladePathTimes.Add(rel);
        }

        PoseActors();   // 샘플링으로 흐트러진 포즈를 현재 t로 되돌린다
    }

    /// <summary>
    /// 칼날 노드를 찾는다. <b>같은 이름 노드가 2단</b>이라 이름만 보면 바깥(빈) 노드를 잡는다 —
    /// 렌더러를 가진 쪽을 고른다(<c>WeaponTrailController</c>가 겪은 그 함정).
    /// </summary>
    private Transform FindBladeNode(Transform root)
    {
        Transform fallback = null;

        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
        {
            if (!tr.name.Contains(bladeNodeName)) continue;

            fallback ??= tr;
            if (tr.GetComponentInChildren<Renderer>() != null) return tr;
        }

        return fallback;
    }

    /// <summary>
    /// 경로를 프리뷰 위에 겹쳐 그리고, <b>클릭하면 그 프레임의 시각을 큐의 <c>timeOffset</c>으로</b> 넣는다.
    /// 칼이 표적을 스치는 지점을 찍으면 타이밍이 그 자리에서 정해진다 — 숫자를 손으로 맞출 일이 없다.
    /// </summary>
    private void DrawBladeOverlay(Rect rect)
    {
        if (bladeWorldPath.Count < 2 || previewUtil?.camera == null) return;

        var cam = previewUtil.camera;
        var screen = new List<Vector2>(bladeWorldPath.Count);

        foreach (var world in bladeWorldPath)
        {
            Vector3 viewport = cam.WorldToViewportPoint(world);
            if (viewport.z <= 0f) { screen.Add(new Vector2(float.NaN, float.NaN)); continue; }

            screen.Add(new Vector2(rect.x + viewport.x * rect.width, rect.y + (1f - viewport.y) * rect.height));
        }

        Handles.BeginGUI();
        Handles.color = new Color(1f, 0.85f, 0.3f, 0.8f);

        for (int i = 1; i < screen.Count; i++)
        {
            if (float.IsNaN(screen[i].x) || float.IsNaN(screen[i - 1].x)) continue;
            Handles.DrawAAPolyLine(2f, screen[i - 1], screen[i]);
        }

        // 현재 t의 지점을 강조
        int nearestToNow = NearestSampleIndex(t);
        if (nearestToNow >= 0 && !float.IsNaN(screen[nearestToNow].x))
        {
            Handles.color = Color.white;
            Handles.DrawSolidDisc(screen[nearestToNow], Vector3.forward, 3.5f);
        }

        Handles.EndGUI();

        HandleBladeClick(rect, screen);
    }

    private int NearestSampleIndex(float time)
    {
        int best = -1;
        float bestDelta = float.MaxValue;

        for (int i = 0; i < bladePathTimes.Count; i++)
        {
            float delta = Mathf.Abs(bladePathTimes[i] - time);
            if (delta >= bestDelta) continue;

            bestDelta = delta;
            best = i;
        }

        return best;
    }

    private void HandleBladeClick(Rect rect, List<Vector2> screen)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 1) return;   // 우클릭 = 경로에서 시각 집기
        if (!rect.Contains(e.mousePosition)) return;

        var cue = SelectedCue();
        if (cue == null || cueList.index < 0) return;

        int best = -1;
        float bestDistance = 24f;

        for (int i = 0; i < screen.Count; i++)
        {
            if (float.IsNaN(screen[i].x)) continue;

            float d = Vector2.Distance(screen[i], e.mousePosition);
            if (d >= bestDistance) continue;

            bestDistance = d;
            best = i;
        }

        if (best < 0) return;

        // 집은 시각을 그 큐의 기준점 기준 오프셋으로 되돌린다 — 기준점이 바뀌어도 화면 위치가 유지된다.
        float picked = bladePathTimes[best];
        float baseTime = ResolveCueTime(cue) - cue.TimeOffset;

        var element = cuesProperty.GetArrayElementAtIndex(cueList.index);
        element.FindPropertyRelative("timeOffset").floatValue = picked - baseTime;
        if (serialized.ApplyModifiedProperties()) MarkDirty();

        t = picked;
        e.Use();
        Repaint();
    }

    // ── 프리뷰 파티클 ───────────────────────────────────────────────────────

    /// <summary>
    /// 큐 프리팹을 프리뷰 씬에 붙이고 <b><see cref="ParticleSystem.Simulate"/>로 그 시각의 상태를 그린다</b>.
    /// <c>Play()</c>로는 앞으로만 갈 수 있어 "임팩트 프레임에 스파크가 어디까지 퍼졌나"를 볼 수 없다.
    /// </summary>
    private void SyncPreviewEffects()
    {
        if (target.EffectCues == null) return;

        foreach (var cue in target.EffectCues)
        {
            if (cue == null || !cue.IsUsable) continue;

            float fireTime = ResolveCueTime(cue);
            float elapsed = (t - fireTime) * cue.Speed;

            if (elapsed < 0f)
            {
                if (previewEffects.TryGetValue(cue, out var hidden) && hidden != null)
                    hidden.SetActive(false);
                continue;
            }

            var instance = EnsurePreviewEffect(cue);
            if (instance == null) continue;

            Transform anchor = PreviewAnchor(cue.Anchor);
            if (anchor == null) { instance.SetActive(false); continue; }

            instance.SetActive(true);

            Vector3 localPosition = cue.PositionOffset;
            if (cue.UsesBlade && bladePath != null && bladePath.IsValid && anchor == bladeNode)
                localPosition += bladePath.LocalPoint(cue.BladeT);

            if (cue.Follow)
            {
                instance.transform.SetParent(anchor, false);
                instance.transform.localPosition = localPosition;
                instance.transform.localRotation = Quaternion.Euler(cue.RotationOffset);
            }
            else
            {
                instance.transform.SetParent(null, false);
                instance.transform.SetPositionAndRotation(
                    anchor.TransformPoint(localPosition),
                    anchor.rotation * Quaternion.Euler(cue.RotationOffset));
            }

            instance.transform.localScale = Vector3.one * cue.Scale;

            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(elapsed, true, true, false);
                ps.Pause();
            }
        }
    }

    private GameObject EnsurePreviewEffect(PatternEffectCue cue)
    {
        // ⚠ 소리 전용 큐는 프리뷰에 띄울 것이 없다(그리고 프리뷰는 소리를 내지 않는다 —
        // 스크럽 되감기가 오디오에는 없어 매 프레임 소리가 튀면 저작을 방해한다).
        if (cue.Prefab == null) return null;

        if (previewEffects.TryGetValue(cue, out var existing) && existing != null) return existing;

        var instance = Instantiate(cue.Prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        previewUtil.AddSingleGO(instance);
        previewEffects[cue] = instance;
        return instance;
    }

    /// <summary>⚠ <c>ImpactAnchor</c>는 씬 값이라 프리뷰에 없다 — 두 배우 사이로 근사한다.</summary>
    private Transform PreviewAnchor(EffectAnchor anchor)
    {
        switch (anchor)
        {
            case EffectAnchor.Player: return playerInstance != null ? playerInstance.transform : null;
            case EffectAnchor.PlayerWeapon: return bladeNode;
            case EffectAnchor.Opponent: return enemyInstance != null ? enemyInstance.transform : null;
            default: return EnsureImpactProxy();
        }
    }

    private Transform impactProxy;

    /// <summary>
    /// 임팩트 앵커의 대역. <b>씬에서 이 앵커는 플레이어의 자식</b>이므로(BattleScene 기준 로컬 <c>(0,0,1)</c>,
    /// 즉 <b>발밑 높이 y=0</b>) 프리뷰에서도 <b>플레이어에 붙여</b> 같은 로컬 좌표에 둔다.
    ///
    /// <para>예전에는 두 배우 사이 허공(<c>up * 1.2</c>)에 띄웠는데, 그러면 씬에서는 발밑에 뜨는 이펙트가
    /// 프리뷰에서만 가슴 높이로 보여 <b>맞춰 놓은 오프셋이 게임에서 어긋난다</b>.</para>
    /// </summary>
    private Transform EnsureImpactProxy()
    {
        if (impactProxy == null)
        {
            var go = new GameObject("[ImpactAnchorProxy]") { hideFlags = HideFlags.HideAndDontSave };
            previewUtil.AddSingleGO(go);
            impactProxy = go.transform;
        }

        if (playerInstance != null)
        {
            impactProxy.SetParent(playerInstance.transform, false);
            impactProxy.localPosition = impactAnchorLocal;
            impactProxy.localRotation = Quaternion.identity;
        }
        else
        {
            impactProxy.SetParent(null, false);
            impactProxy.SetPositionAndRotation(impactAnchorLocal, Quaternion.identity);
        }

        return impactProxy;
    }

    /// <summary>
    /// 열려 있는 씬의 <c>SliceTargetDirector.impactAnchor</c>를 읽어 프리뷰 값을 맞춘다.
    /// 값을 손으로 옮겨 적으면 씬을 고칠 때마다 조용히 어긋나므로, <b>버튼 하나로 다시 읽는다</b>.
    /// </summary>
    private void PullImpactAnchorFromScene()
    {
        var director = Object.FindFirstObjectByType<SliceSpace.SliceTargetDirector>(FindObjectsInactive.Include);
        if (director == null)
        {
            EditorUtility.DisplayDialog("임팩트 앵커",
                "열려 있는 씬에서 SliceTargetDirector를 찾지 못했습니다. BattleScene을 열고 다시 시도하세요.", "확인");
            return;
        }

        var anchor = new SerializedObject(director).FindProperty("impactAnchor").objectReferenceValue as Transform;
        if (anchor == null)
        {
            EditorUtility.DisplayDialog("임팩트 앵커", "SliceTargetDirector에 impactAnchor가 배선되어 있지 않습니다.", "확인");
            return;
        }

        // 씬에서 앵커는 플레이어의 자식이다 — 그 로컬 좌표가 곧 프리뷰가 쓸 값이다.
        impactAnchorLocal = anchor.parent != null ? anchor.localPosition : anchor.position;
        Repaint();
    }

    private void ClearPreview()
    {
        foreach (var pair in previewEffects)
            if (pair.Value != null) DestroyImmediate(pair.Value);
        previewEffects.Clear();

        if (impactProxy != null) DestroyImmediate(impactProxy.gameObject);
        impactProxy = null;

        if (playerInstance != null) DestroyImmediate(playerInstance);
        if (enemyInstance != null) DestroyImmediate(enemyInstance);
        playerInstance = enemyInstance = null;
        cachedPlayerPrefab = cachedEnemyPrefab = null;

        bladeNode = null;
        bladePath = null;
        bladeWorldPath.Clear();
        bladePathTimes.Clear();
        bladeCacheKey = null;
    }
}
