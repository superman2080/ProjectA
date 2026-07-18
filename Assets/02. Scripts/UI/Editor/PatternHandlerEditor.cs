using PatternSpace;
using UnityEditor;
using UnityEngine;

/// <summary>PatternHandler의 에디터 전용 디버그 입력 UI. On/Off, 키 매핑, 마지막 사용 모드 하이라이트, 자동 Perfect 토글, 수동 Force 버튼을 그린다.</summary>
[CustomEditor(typeof(PatternHandler))]
public class PatternHandlerEditor : Editor
{
    private static readonly Color PerfectColor = new Color(0.30f, 0.55f, 1f);
    private static readonly Color GoodColor = new Color(0.30f, 0.80f, 0.40f);
    private static readonly Color MissColor = new Color(1f, 0.40f, 0.40f);

    private SerializedProperty enabledProp;
    private SerializedProperty perfectKeyProp;
    private SerializedProperty goodKeyProp;
    private SerializedProperty missKeyProp;
    private SerializedProperty autoPerfectProp;

    private void OnEnable()
    {
        enabledProp = serializedObject.FindProperty("debugInputEnabled");
        perfectKeyProp = serializedObject.FindProperty("debugPerfectKey");
        goodKeyProp = serializedObject.FindProperty("debugGoodKey");
        missKeyProp = serializedObject.FindProperty("debugMissKey");
        autoPerfectProp = serializedObject.FindProperty("debugAutoPerfect");
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (enabledProp == null) return; // 안전장치: 디버그 필드가 없는 빌드 구성 등

        EditorGUILayout.Space();
        DrawSeparator();
        EditorGUILayout.LabelField("Debug Input (Editor Only)", EditorStyles.boldLabel);

        serializedObject.Update();

        EditorGUILayout.PropertyField(enabledProp, new GUIContent("Debug Input Enabled"));

        using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
        {
            EditorGUILayout.PropertyField(perfectKeyProp, new GUIContent("Perfect Key"));
            EditorGUILayout.PropertyField(goodKeyProp, new GUIContent("Good Key"));
            EditorGUILayout.PropertyField(missKeyProp, new GUIContent("Miss Key"));

            // 자동 Perfect: 눌린 상태가 유지되는 토글 버튼
            bool auto = GUILayout.Toggle(autoPerfectProp.boolValue, "Auto Perfect (자동 입력)", "Button", GUILayout.Height(24f));
            if (auto != autoPerfectProp.boolValue)
                autoPerfectProp.boolValue = auto;
        }

        serializedObject.ApplyModifiedProperties();

        DrawLastUsedMode();
        DrawForceButtons();
    }

    /// <summary>마지막으로 발동한 디버그 판정 모드를 색상 박스로 강조한다.</summary>
    private void DrawLastUsedMode()
    {
        var handler = (PatternHandler)target;
        JudgementResult? last = handler.DebugLastUsedMode;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last Used Mode", EditorStyles.miniBoldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawModeBox("Perfect", PerfectColor, last == JudgementResult.Perfect);
            DrawModeBox("Good", GoodColor, last == JudgementResult.Good);
            DrawModeBox("Miss", MissColor, last == JudgementResult.Miss);
        }

        // 플레이 중 마지막 사용 모드가 실시간 갱신되도록 다시 그린다.
        if (Application.isPlaying)
            Repaint();
    }

    private void DrawModeBox(string label, Color color, bool active)
    {
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = active ? color : new Color(color.r, color.g, color.b, 0.20f);

        var style = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = active ? FontStyle.Bold : FontStyle.Normal
        };
        GUILayout.Box(label, style, GUILayout.Height(24f), GUILayout.ExpandWidth(true));

        GUI.backgroundColor = prev;
    }

    /// <summary>키보드 없이 클릭으로 강제 입력하는 버튼 3개. 재생 중 + Debug Input Enabled일 때만 활성.</summary>
    private void DrawForceButtons()
    {
        var handler = (PatternHandler)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Manual Force Input", EditorStyles.miniBoldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying || !enabledProp.boolValue))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Force Perfect", GUILayout.Height(24f)))
                handler.DebugForceInput(JudgementResult.Perfect);
            if (GUILayout.Button("Force Good", GUILayout.Height(24f)))
                handler.DebugForceInput(JudgementResult.Good);
            if (GUILayout.Button("Force Miss", GUILayout.Height(24f)))
                handler.DebugForceInput(JudgementResult.Miss);
        }
    }

    private static void DrawSeparator()
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f, 1f));
    }
}
