using PatternSpace;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 애니메이션 클립의 실제 휘두르는 구간을 창 안에서 프레임 단위로 보며 찾고,
/// 그 start/duration(초)을 Pattern 에셋의 AnimationStartOffset/AnimationDuration에 바로 저장하는 에디터 툴.
/// </summary>
public class AnimationClipTrimmerWindow : EditorWindow
{
    private const float PreviewHeight = 320f;

    [MenuItem("Tools/Animation Clip Trimmer")]
    private static void Open() => GetWindow<AnimationClipTrimmerWindow>("Clip Trimmer");

    // 입력
    private AnimationClip clip;
    private GameObject previewModel;
    private Pattern targetPattern;

    // 스크럽 상태
    private float currentTime;
    private bool isPlaying;
    private double lastUpdateTime;

    // 마킹
    private float startTime;
    private float endTime;

    // 프리뷰
    private PreviewRenderUtility previewUtil;
    private GameObject previewInstance;
    private GameObject cachedModel;
    private float yaw = 120f;
    private float pitch = 10f;
    private float zoom = 1f;

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        DestroyPreviewInstance();
        previewUtil?.Cleanup();
        previewUtil = null;
    }

    private void OnEditorUpdate()
    {
        if (!isPlaying || clip == null) return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastUpdateTime);
        lastUpdateTime = now;

        currentTime += dt;
        if (currentTime > clip.length)
            currentTime = clip.length > 0f ? currentTime % clip.length : 0f;

        Repaint();
    }

    private void OnGUI()
    {
        lastUpdateTime = EditorApplication.timeSinceStartup; // 재생 dt 기준점 갱신(창 비활성 후 점프 방지)

        DrawInputFields();

        if (clip == null)
        {
            EditorGUILayout.HelpBox("트리밍할 AnimationClip을 지정하세요.", MessageType.Info);
            return;
        }

        DrawPreview();
        DrawTimeline();
        DrawMarking();
        DrawApply();
    }

    // ─────────────────────────── 입력 필드 ───────────────────────────

    private void DrawInputFields()
    {
        EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);

        clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", clip, typeof(AnimationClip), false);
        previewModel = (GameObject)EditorGUILayout.ObjectField("Preview Model (rig)", previewModel, typeof(GameObject), true);
        targetPattern = (Pattern)EditorGUILayout.ObjectField("Target Pattern", targetPattern, typeof(Pattern), false);

        using (new EditorGUI.DisabledScope(targetPattern == null))
        {
            if (GUILayout.Button("Load Clip / Marks from Pattern") && targetPattern != null)
                LoadFromPattern();
        }

        EditorGUILayout.Space();
    }

    private void LoadFromPattern()
    {
        var so = new SerializedObject(targetPattern);
        var clipProp = so.FindProperty("successAnimationClip");
        var offProp = so.FindProperty("animationStartOffset");
        var durProp = so.FindProperty("animationDuration");

        if (clipProp != null && clipProp.objectReferenceValue is AnimationClip c)
            clip = c;
        if (offProp != null)
            startTime = offProp.floatValue;
        if (durProp != null)
            endTime = startTime + Mathf.Max(durProp.floatValue, 0f);

        currentTime = startTime;
    }

    // ─────────────────────────── 프리뷰 ───────────────────────────

    private void DrawPreview()
    {
        Rect rect = GUILayoutUtility.GetRect(position.width, PreviewHeight, GUILayout.ExpandWidth(true));

        if (previewModel == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.LabelField(rect, "미리보기 모델(리그)을 지정하면 포즈가 표시됩니다.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        HandlePreviewInput(rect);

        if (Event.current.type != EventType.Repaint)
            return;

        EnsurePreview();
        if (previewInstance == null || previewUtil == null)
            return;

        clip.SampleAnimation(previewInstance, currentTime);

        previewUtil.BeginPreview(rect, GUIStyle.none);
        SetupCameraAndLights();
        previewUtil.Render(true);
        Texture tex = previewUtil.EndPreview();
        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
    }

    private void HandlePreviewInput(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDrag && e.button == 0)
        {
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

    private void EnsurePreview()
    {
        previewUtil ??= new PreviewRenderUtility();

        if (previewInstance != null && cachedModel == previewModel)
            return;

        DestroyPreviewInstance();

        previewInstance = Instantiate(previewModel);
        previewInstance.hideFlags = HideFlags.HideAndDontSave;
        previewUtil.AddSingleGO(previewInstance);
        cachedModel = previewModel;
    }

    private void DestroyPreviewInstance()
    {
        if (previewInstance != null)
            DestroyImmediate(previewInstance);
        previewInstance = null;
        cachedModel = null;
    }

    private void SetupCameraAndLights()
    {
        Bounds b = ComputeBounds(previewInstance);

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

    private static Bounds ComputeBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // ─────────────────────────── 타임라인 ───────────────────────────

    private void DrawTimeline()
    {
        EditorGUILayout.Space();

        float frameRate = clip.frameRate > 0f ? clip.frameRate : 30f;
        int totalFrames = Mathf.Max(Mathf.RoundToInt(clip.length * frameRate), 1);
        int curFrame = Mathf.RoundToInt(currentTime * frameRate);

        // 재생/프레임 컨트롤
        using (new EditorGUILayout.HorizontalScope())
        {
            isPlaying = GUILayout.Toggle(isPlaying, isPlaying ? "❚❚ Pause" : "▶ Play", "Button", GUILayout.Width(80f));

            if (GUILayout.Button("◀ Frame", GUILayout.Width(70f)))
            {
                isPlaying = false;
                currentTime = Mathf.Max((curFrame - 1) / frameRate, 0f);
            }
            if (GUILayout.Button("Frame ▶", GUILayout.Width(70f)))
            {
                isPlaying = false;
                currentTime = Mathf.Min((curFrame + 1) / frameRate, clip.length);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"time: {currentTime:0.000}s   (frame {curFrame} / {totalFrames})", EditorStyles.boldLabel, GUILayout.Width(240f));
        }

        // 스크럽 슬라이더 + 마킹 마커
        Rect sliderRect = GUILayoutUtility.GetRect(position.width, 22f);
        DrawMarkers(sliderRect);
        float newTime = GUI.HorizontalSlider(sliderRect, currentTime, 0f, clip.length);
        if (!Mathf.Approximately(newTime, currentTime))
        {
            currentTime = newTime;
            isPlaying = false;
            Repaint();
        }
    }

    private void DrawMarkers(Rect sliderRect)
    {
        if (clip.length <= 0f) return;

        DrawMarkerLine(sliderRect, startTime / clip.length, new Color(0.3f, 0.85f, 0.4f)); // start=초록
        DrawMarkerLine(sliderRect, endTime / clip.length, new Color(0.95f, 0.4f, 0.4f));   // end=빨강
    }

    private void DrawMarkerLine(Rect sliderRect, float t01, Color color)
    {
        if (t01 < 0f || t01 > 1f) return;
        float x = Mathf.Lerp(sliderRect.x + 4f, sliderRect.xMax - 4f, t01);
        EditorGUI.DrawRect(new Rect(x - 1f, sliderRect.y, 2f, sliderRect.height), color);
    }

    // ─────────────────────────── 마킹 ───────────────────────────

    private void DrawMarking()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Trim Marks", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Mark Start = 현재"))
                startTime = currentTime;
            if (GUILayout.Button("Mark End = 현재"))
                endTime = currentTime;
        }

        // 직접 편집도 허용
        startTime = Mathf.Max(EditorGUILayout.FloatField("Start Offset (s)", startTime), 0f);
        endTime = EditorGUILayout.FloatField("End (s)", endTime);

        float duration = endTime - startTime;
        EditorGUILayout.LabelField("Duration (s)", $"{duration:0.000}");

        if (duration <= 0f)
            EditorGUILayout.HelpBox("End가 Start보다 뒤여야 합니다 (Duration > 0).", MessageType.Warning);

        // 현재 시각이 휘두름 구간 안인지
        bool inSwing = duration > 0f && currentTime >= startTime && currentTime <= endTime;
        var style = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = inSwing ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);
        GUILayout.Box(inSwing ? "▶ IN SWING (휘두름 구간)" : "— (구간 밖)", style, GUILayout.Height(22f));
        GUI.backgroundColor = prev;
    }

    // ─────────────────────────── 저장 ───────────────────────────

    private void DrawApply()
    {
        EditorGUILayout.Space();

        float duration = endTime - startTime;

        using (new EditorGUI.DisabledScope(targetPattern == null || duration <= 0f))
        {
            if (GUILayout.Button("Apply to Pattern", GUILayout.Height(28f)))
                ApplyToPattern(duration);
        }

        if (targetPattern != null)
        {
            var so = new SerializedObject(targetPattern);
            float off = so.FindProperty("animationStartOffset")?.floatValue ?? 0f;
            float dur = so.FindProperty("animationDuration")?.floatValue ?? 0f;
            EditorGUILayout.LabelField("Pattern 현재값", $"offset {off:0.000}s / duration {dur:0.000}s");
        }
        else
        {
            EditorGUILayout.HelpBox("Target Pattern을 지정하면 잡은 값을 바로 저장할 수 있습니다.", MessageType.None);
        }
    }

    private void ApplyToPattern(float duration)
    {
        Undo.RecordObject(targetPattern, "Apply Clip Trim");

        var so = new SerializedObject(targetPattern);
        so.FindProperty("animationStartOffset").floatValue = startTime;
        so.FindProperty("animationDuration").floatValue = duration;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(targetPattern);
        AssetDatabase.SaveAssetIfDirty(targetPattern);
    }
}
