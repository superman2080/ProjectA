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
    /// <summary>
    /// 굽기 밀도. <b>참조 클립 해상도가 아니라 대상 커브의 키 간격을 정한다</b> — 여기가 오차의 주범이다.
    ///
    /// <para>손은 휴머노이드 머슬 커브로 매끄럽게 가는데 무기 커브는 키 사이를 다르게 보간해,
    /// 빠른 스윙에서 키와 키 사이가 벌어진다. <c>Swipe_5To2</c> 실측(401샘플): 30fps 21.5cm /
    /// 60fps 2.9cm / 120fps 0.9cm. 정상 클립의 무기 커브가 30fps라고 여기도 30을 쓰면 안 된다.</para>
    /// </summary>
    [SerializeField] private int sampleFps = 120;
    [SerializeField] private bool backupBeforeBake = true;

    /// <summary>그립을 뜰 참조 클립의 시각(초). 음수면 전 구간 평균. 편차가 큰 데이터에서 눈으로 맞추는 노브다.</summary>
    [SerializeField] private float gripTime = -1f;
    [SerializeField] private bool useGripTime;

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
            "검집(add_weapon_l): 안 함=커브 없음 / 허리 고정=pelvis를 따라감 / 손 그립=hand_l을 따라감.\n" +
            "참조 클립은 세 경우 모두 필요합니다 — 허리 오프셋도 프리팹 포즈가 아니라 참조 클립에서 역산합니다.\n" +
            "⚠ 참조 클립은 그 무기가 실제로 그 본에 붙어 있는 구간이어야 합니다. 칼이 검집에 꽂힌 클립" +
            "(Run/Release/Sprint_Forward/Katana_Idle)을 칼 참조로 쓰면 편차가 팔 궤적만큼 커집니다.", MessageType.Info);

        characterPrefab = (GameObject)EditorGUILayout.ObjectField("캐릭터 프리팹", characterPrefab, typeof(GameObject), false);
        referenceClip = (AnimationClip)EditorGUILayout.ObjectField("참조 클립(정상)", referenceClip, typeof(AnimationClip), false);
        targetClip = (AnimationClip)EditorGUILayout.ObjectField("대상 클립(결함)", targetClip, typeof(AnimationClip), false);

        EditorGUILayout.Space();
        bakeKatana = EditorGUILayout.Toggle("칼 굽기 (add_weapon_r)", bakeKatana);
        sheathMode = (WeaponBoneBaker.SheathMode)EditorGUILayout.Popup("검집 (add_weapon_l)", (int)sheathMode, SheathModeLabels);
        // 상한 240 — SlashCombo처럼 루트모션이 큰 콤보는 120fps에서도 키 사이 잔차가 2.2cm 남는다.
        sampleFps = Mathf.Clamp(EditorGUILayout.IntField(
            new GUIContent("샘플링 fps", "대상 커브의 키 간격. 낮추면 빠른 스윙에서 키 사이가 벌어진다(30fps=21cm, 120fps=0.9cm). 굽기 후 '되읽기 잔차'를 보고 올려라."),
            sampleFps), 5, 240);
        backupBeforeBake = EditorGUILayout.Toggle("굽기 전 백업", backupBeforeBake);

        // 오프셋이 진짜 상수가 아닐 때 평균은 어느 프레임에서도 안 맞는다. 가장 잘 보이는 한 프레임을 집는 노브.
        EditorGUILayout.BeginHorizontal();
        useGripTime = EditorGUILayout.Toggle("그립 기준 프레임 지정", useGripTime);
        using (new EditorGUI.DisabledScope(!useGripTime))
        {
            gripTime = Mathf.Max(0f, EditorGUILayout.FloatField(Mathf.Max(gripTime, 0f)));
            if (referenceClip != null) GUILayout.Label("/ " + referenceClip.length.ToString("F3") + "초", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();

        // 검집 허리 고정도 이제 참조 클립에서 역산한다 — 프리팹 저장 포즈는 무기 본에 대해 아무 정보가 아니다.
        bool canRun = characterPrefab != null && targetClip != null && referenceClip != null;
        float resolvedGripTime = useGripTime ? Mathf.Max(gripTime, 0f) : -1f;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!canRun))
        {
            if (GUILayout.Button("오프셋 검사만 (굽지 않음)"))
                report = WeaponBoneBaker.Inspect(characterPrefab, referenceClip, bakeKatana, sheathMode, sampleFps, resolvedGripTime);
            if (GUILayout.Button("굽기 실행"))
                report = WeaponBoneBaker.Bake(characterPrefab, referenceClip, targetClip, bakeKatana, sheathMode, sampleFps, backupBeforeBake, resolvedGripTime);
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
