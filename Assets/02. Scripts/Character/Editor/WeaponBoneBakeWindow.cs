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
    [SerializeField] private WeaponBoneBaker.SheathMode sheathMode = WeaponBoneBaker.SheathMode.Waist; // add_weapon_l
    [SerializeField] private int sampleFps = 30;
    [SerializeField] private bool backupBeforeBake = true;

    // 드롭다운 라벨. 인덱스가 SheathMode(None=0/Waist=1/Hand=2)와 일치해야 한다.
    private static readonly string[] SheathModeLabels = { "안 함", "허리 고정 (pelvis)", "손 그립 (hand_l)" };

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
            "칼: 정상 클립에서 손↔칼 그립을 역산해 hand_r을 따라 굽습니다(add_weapon_r).\n" +
            "검집(add_weapon_l): 안 함=커브 없음 / 허리 고정=바인드 위치를 유지한 채 pelvis를 따라 회전·이동 / 손 그립=참조 클립의 hand_l 자세.", MessageType.Info);

        characterPrefab = (GameObject)EditorGUILayout.ObjectField("캐릭터 프리팹", characterPrefab, typeof(GameObject), false);
        referenceClip = (AnimationClip)EditorGUILayout.ObjectField("참조 클립(정상)", referenceClip, typeof(AnimationClip), false);
        targetClip = (AnimationClip)EditorGUILayout.ObjectField("대상 클립(결함)", targetClip, typeof(AnimationClip), false);

        EditorGUILayout.Space();
        bakeKatana = EditorGUILayout.Toggle("칼 굽기 (add_weapon_r)", bakeKatana);
        sheathMode = (WeaponBoneBaker.SheathMode)EditorGUILayout.Popup("검집 (add_weapon_l)", (int)sheathMode, SheathModeLabels);
        sampleFps = Mathf.Clamp(EditorGUILayout.IntField("샘플링 fps", sampleFps), 5, 120);
        backupBeforeBake = EditorGUILayout.Toggle("굽기 전 백업", backupBeforeBake);

        // 참조 클립은 칼 또는 검집=손 그립일 때만 필요. 검집=허리 고정은 바인드 포즈만 쓴다.
        bool needReference = bakeKatana || sheathMode == WeaponBoneBaker.SheathMode.Hand;
        bool canRun = characterPrefab != null && targetClip != null && (!needReference || referenceClip != null);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!canRun))
        {
            if (GUILayout.Button("오프셋 검사만 (굽지 않음)"))
                report = WeaponBoneBaker.Inspect(characterPrefab, referenceClip, bakeKatana, sheathMode, sampleFps);
            if (GUILayout.Button("굽기 실행"))
                report = WeaponBoneBaker.Bake(characterPrefab, referenceClip, targetClip, bakeKatana, sheathMode, sampleFps, backupBeforeBake);
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
