using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 본(add_weapon_r/l) 트랜스폼 커브가 빠진 클립에 커브를 구워 넣는 에디터 전용 툴.
///
/// 이 캐릭터의 무기 본은 <b>root의 직계 자식</b>이라 손·허리를 따라가지 않는다. 클립이 키를 찍어줘야만 제자리에 간다.
/// 정상 클립(Swipe_* 등)은 이 커브를 갖고 있고, 그 값은 정확히 <b>손 회전 × 상수 그립 오프셋</b>이다(실측 회전 편차 0.00도).
/// 따라서 굽기는 "정상 클립에서 그립 오프셋을 역산 → 대상 클립의 손을 따라 재계산"으로 끝난다.
///
/// 상세: docs/WeaponBoneBake/
/// </summary>
public class WeaponBoneBakeWindow : EditorWindow
{
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private AnimationClip referenceClip;  // 그립 오프셋을 역산할 정상 클립
    [SerializeField] private AnimationClip targetClip;     // 커브를 구워 넣을 결함 클립
    [SerializeField] private bool bakeKatana = true;       // add_weapon_r ← hand_r
    [SerializeField] private bool bakeSheath = true;       // add_weapon_l ← hand_l
    [SerializeField] private int sampleFps = 30;
    [SerializeField] private bool backupBeforeBake = true;

    private Vector2 scroll;
    private string report = "";

    [MenuItem("Tools/Weapon Bone Bake")]
    private static void Open()
    {
        GetWindow<WeaponBoneBakeWindow>("Weapon Bone Bake").minSize = new Vector2(420f, 360f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("무기 본 커브 굽기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "정상 클립에서 손↔무기 상대 자세(그립 오프셋)를 역산해, 대상 클립의 손을 따라 무기 본 커브를 생성합니다.\n" +
            "칼: add_weapon_r ← hand_r / 검집: add_weapon_l ← hand_l", MessageType.Info);

        characterPrefab = (GameObject)EditorGUILayout.ObjectField("캐릭터 프리팹", characterPrefab, typeof(GameObject), false);
        referenceClip = (AnimationClip)EditorGUILayout.ObjectField("참조 클립(정상)", referenceClip, typeof(AnimationClip), false);
        targetClip = (AnimationClip)EditorGUILayout.ObjectField("대상 클립(결함)", targetClip, typeof(AnimationClip), false);

        EditorGUILayout.Space();
        bakeKatana = EditorGUILayout.Toggle("칼 굽기 (add_weapon_r)", bakeKatana);
        bakeSheath = EditorGUILayout.Toggle("검집 굽기 (add_weapon_l)", bakeSheath);
        sampleFps = Mathf.Clamp(EditorGUILayout.IntField("샘플링 fps", sampleFps), 5, 120);
        backupBeforeBake = EditorGUILayout.Toggle("굽기 전 백업", backupBeforeBake);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(characterPrefab == null || referenceClip == null || targetClip == null))
        {
            if (GUILayout.Button("오프셋 검사만 (굽지 않음)"))
                report = WeaponBoneBaker.Inspect(characterPrefab, referenceClip, bakeKatana, bakeSheath, sampleFps);
            if (GUILayout.Button("굽기 실행"))
                report = WeaponBoneBaker.Bake(characterPrefab, referenceClip, targetClip, bakeKatana, bakeSheath, sampleFps, backupBeforeBake);
        }
        using (new EditorGUI.DisabledScope(targetClip == null))
        {
            if (GUILayout.Button("대상 클립의 무기 커브 제거")) report = WeaponBoneBaker.RemoveWeaponCurves(targetClip);
        }

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }
}
