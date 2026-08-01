using System.Collections.Generic;
using System.IO;
using PatternSpace;
using UnityEditor;
using UnityEngine;

namespace SliceSpace.EditorTools
{
    /// <summary>
    /// 메쉬를 평면으로 미리 절단해 조각과 <see cref="SliceSet"/>을 굽는 에디터 툴.
    ///
    /// <para><b>모드가 둘이다.</b>
    /// <list type="bullet">
    /// <item><c>일반 메쉬</c> — 정적 프롭(투사체 표적). 기존 동작 그대로.</item>
    /// <item><c>휴머노이드</c> — 스킨드 캐릭터(적). 스키닝을 보존해 자르고 <b>시체 프리팹</b>으로 굽는다.</item>
    /// </list>
    /// 두 모드를 나눈 이유는 입력이 근본적으로 다르기 때문이다 — 휴머노이드는 <see cref="Pattern"/>에서
    /// 포즈(<c>enemyDeath</c>)와 칼 궤적(<c>playerAttack</c>)을 읽는다.</para>
    ///
    /// <para>절단 평면은 <b>창 안의 프리뷰에서 직선 획을 그어</b> 만든다(획 하나 = 평면 한 장).
    /// <see cref="PreviewRenderUtility"/>로 창 내부에 렌더하므로 사용자 씬을 건드리지 않는다.
    /// 획의 <b>길이는 무시</b>되고 각도와 위치만 쓰인다(무한 평면).</para>
    ///
    /// <para>같은 이름으로 다시 구우면 에셋을 <b>제자리 수정</b>해 GUID를 유지한다.
    /// 패턴 → SliceSet 직접 참조가 유일한 배선이므로, GUID를 잃으면 배선이 통째로 끊긴다.</para>
    /// </summary>
    public class MeshSliceBakerWindow : EditorWindow
    {
        private const string PieceMeshPrefix = "Piece_";
        private const float PreviewHeight = 320f;

        /// <summary>칼 본 이름. <c>WeaponBoneBake</c>가 커브를 굽는 그 본이다.</summary>
        private const string WeaponBoneName = "add_weapon_r";

        private enum BakeMode { StaticMesh, Humanoid }

        private static readonly string[] ModeLabels = { "일반 메쉬", "휴머노이드" };

        [SerializeField] private BakeMode mode = BakeMode.StaticMesh;

        // ── 공통 ────────────────────────────────────────────────────────────────
        [SerializeField] private string setName = "";
        [SerializeField] private Material capMaterial;
        [SerializeField] private string outputFolder = "Assets/04. Datas/Slice";
        [SerializeField] private float capUvScale = 1f;
        [SerializeField] private float weldEpsilon = 1e-4f;
        [SerializeField] private float minPieceVolumeRatio = 0.001f;
        [SerializeField] private List<SlicePlane> planes = new List<SlicePlane>();
        [SerializeField] private int selectedPlane = -1;

        // ── 일반 메쉬 모드 ───────────────────────────────────────────────────────
        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private SliceShape shape = SliceShape.Horizontal;

        // ── 휴머노이드 모드 ──────────────────────────────────────────────────────
        [Tooltip("포즈(enemyDeath)와 칼 궤적(playerAttack)의 출처.")]
        [SerializeField] private Pattern targetPattern;
        // 프리팹이 아니라 정의를 받는다 — 구운 세트가 그 정의의 프리팹에서 나왔다는 것이
        // 굽는 시점에 보장되고, 배선도 자동으로 그리로 간다(모델 불일치가 구성상 불가능해진다).
        [SerializeField] private EnemySpace.EnemyDefinition enemyDefinition;

        private GameObject enemyPrefab => enemyDefinition != null ? enemyDefinition.Prefab : null;
        [SerializeField] private int enemyRendererIndex;
        [SerializeField] private GameObject playerPrefab;

        [Tooltip("씬 결투 앵커의 기준 거리(m). 가이드 4단계에서 잡은 값을 넣는다. " +
                 "씬 참조를 끌어오지 않기 위한 손잡이다 — 툴이 씬에 의존하면 프리팹만으로 굽지 못한다.")]
        [SerializeField] private float duelBaseDistance = 1f;

        /// <summary>
        /// 실제 배치 거리 = 기준선 + 패턴 보정. <b>런타임·합주 프리뷰와 같은 식이어야 한다</b> —
        /// 여기가 어긋나면 유도된 절단 평면 자체가 틀린다.
        /// </summary>
        private float DuelDistance =>
            duelBaseDistance + (targetPattern != null ? targetPattern.DuelDistanceOffset : 0f);

        // 프리뷰
        private PreviewRenderUtility previewUtil;
        private float yaw = 130f;
        private float pitch = 15f;
        private float zoom = 1.2f;
        private float explode;

        // 획 긋기
        private bool dragging;
        private Vector2 dragStart;
        private Vector2 dragCurrent;

        // 프리뷰 카메라 캐시 — 창 좌표 ↔ 월드 변환에 쓴다(카메라는 Repaint 중에만 유효하므로 값만 들고 있는다).
        private Vector3 camPos;
        private Quaternion camRot;
        private float camFov = 30f;
        private Rect previewRect;

        private SliceSet existingSet;
        private Vector2 scroll;
        private string previewText = "";

        private List<SlicedPiece> previewPieces;

        // 유도된 칼 평면(휴머노이드). 획 목록에 넣기 전까지는 오버레이로만 보인다.
        private bool hasBladePlane;
        private SlicePlane bladePlane;

        [MenuItem("Tools/Mesh Slice Baker")]
        public static void Open() => GetWindow<MeshSliceBakerWindow>("Mesh Slice Baker");

        void OnDisable()
        {
            ClearPreviewPieces();
            ClearPoseCache();
            previewUtil?.Cleanup();
            previewUtil = null;
        }

        // ── 창 ──────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawModeTabs();
            DrawPreview();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (mode == BakeMode.StaticMesh) DrawStaticInputs();
            else DrawHumanoidInputs();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("절단", EditorStyles.boldLabel);

            if (mode == BakeMode.StaticMesh) DrawShapePreset();
            DrawPlaneList();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("머티리얼", EditorStyles.boldLabel);
            capMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("단면 머티리얼", "비우면 원본 머티리얼[0]을 단면에도 그대로 씁니다(정상 경로)."),
                capMaterial, typeof(Material), false);
            capUvScale = EditorGUILayout.FloatField(
                new GUIContent("Cap UV Scale", "캡 UV는 평면 투영값이라 오브젝트 크기에 따라 텍셀 밀도가 달라집니다. 겉면과 맞추는 수동 손잡이."),
                capUvScale);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("고급", EditorStyles.boldLabel);
            weldEpsilon = EditorGUILayout.FloatField("Weld Epsilon", weldEpsilon);
            minPieceVolumeRatio = EditorGUILayout.FloatField(
                new GUIContent("Min Piece Volume", "원본 부피 대비 이 비율 미만인 조각은 퇴화로 보고 폐기합니다."),
                minPieceVolumeRatio);

            EditorGUILayout.Space();

            string blocker = GetBlockingReason();
            using (new EditorGUI.DisabledScope(blocker != null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Preview", GUILayout.Height(28))) RunPreview();
                if (GUILayout.Button("Bake", GUILayout.Height(28))) Bake();
                EditorGUILayout.EndHorizontal();
            }

            if (blocker != null) EditorGUILayout.HelpBox(blocker, MessageType.Warning);
            if (!string.IsNullOrEmpty(previewText)) EditorGUILayout.HelpBox(previewText, MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawModeTabs()
        {
            EditorGUI.BeginChangeCheck();
            mode = (BakeMode)GUILayout.Toolbar((int)mode, ModeLabels, GUILayout.Height(24));
            if (!EditorGUI.EndChangeCheck()) return;

            // 모드가 바뀌면 이전 모드의 프리뷰·캐시·세트 로드가 남지 않도록 전부 비운다.
            // 남으면 정적 프리뷰가 휴머노이드 탭에 보여 오인을 부른다.
            ClearPreviewPieces();
            ClearPoseCache();
            existingSet = null;
            hasBladePlane = false;
            planes.Clear();
            selectedPlane = -1;
            GUI.FocusControl(null);
            Repaint();
        }

        // ── 입력: 일반 메쉬 ──────────────────────────────────────────────────────

        private void DrawStaticInputs()
        {
            EditorGUILayout.LabelField("원본", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            sourcePrefab = (GameObject)EditorGUILayout.ObjectField("표적 프리팹", sourcePrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (string.IsNullOrEmpty(setName) && sourcePrefab != null) setName = sourcePrefab.name + "_" + shape;
                ClearPreviewPieces();
                TryLoadExistingSet();
                Repaint();
            }

            setName = EditorGUILayout.TextField("세트 이름", setName);
            outputFolder = EditorGUILayout.TextField("출력 경로", outputFolder);

            if (existingSet != null)
                EditorGUILayout.HelpBox($"기존 세트 '{existingSet.name}'를 갱신합니다(GUID 유지). 저장된 평면 {existingSet.BakedPlanes?.Length ?? 0}장을 로드했습니다.", MessageType.Info);
        }

        private void DrawShapePreset()
        {
            EditorGUI.BeginChangeCheck();
            shape = (SliceShape)EditorGUILayout.EnumPopup("Shape (라벨/프리셋)", shape);
            if (EditorGUI.EndChangeCheck() && shape != SliceShape.Custom)
            {
                if (EditorUtility.DisplayDialog("프리셋 적용",
                        $"'{shape}' 프리셋 평면으로 획 목록을 대체할까요? 지금 그은 획은 사라집니다.", "대체", "유지"))
                    ApplyPreset();
            }
        }

        // ── 입력: 휴머노이드 ─────────────────────────────────────────────────────

        private void DrawHumanoidInputs()
        {
            EditorGUILayout.LabelField("원본", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            targetPattern = (Pattern)EditorGUILayout.ObjectField("Pattern", targetPattern, typeof(Pattern), false);
            enemyDefinition = (EnemySpace.EnemyDefinition)EditorGUILayout.ObjectField(
                "적 정의 (EnemyDefinition)", enemyDefinition, typeof(EnemySpace.EnemyDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (string.IsNullOrEmpty(setName) && enemyPrefab != null) setName = enemyPrefab.name + "_Death";
                ClearPreviewPieces();
                ClearPoseCache();
                TryLoadExistingSet();

                // 정의를 바꾸면 렌더러 선택을 몸통으로 되돌린다 — 이전 프리팹의 인덱스가 남으면 엉뚱한 걸 굽는다.
                var picked = GetEnemyRenderers();
                if (picked.Length > 0) enemyRendererIndex = DefaultRendererIndex(picked);

                Repaint();
            }

            var renderers = GetEnemyRenderers();
            if (renderers.Length > 1)
            {
                var names = new string[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) names[i] = renderers[i].name;

                EditorGUI.BeginChangeCheck();
                enemyRendererIndex = EditorGUILayout.Popup("자를 렌더러", Mathf.Clamp(enemyRendererIndex, 0, renderers.Length - 1), names);
                if (EditorGUI.EndChangeCheck()) { ClearPreviewPieces(); ClearPoseCache(); }
            }
            else enemyRendererIndex = 0;

            setName = EditorGUILayout.TextField("세트 이름", setName);
            outputFolder = EditorGUILayout.TextField("출력 경로", outputFolder);

            if (existingSet != null)
                EditorGUILayout.HelpBox($"기존 세트 '{existingSet.name}'를 갱신합니다(GUID 유지).", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("칼 궤적", EditorStyles.boldLabel);

            playerPrefab = (GameObject)EditorGUILayout.ObjectField("플레이어 프리팹", playerPrefab, typeof(GameObject), false);
            duelBaseDistance = EditorGUILayout.FloatField(
                new GUIContent("기준 결투 거리", "씬 결투 앵커의 거리. 가이드 4단계에서 잡은 값을 넣는다."),
                duelBaseDistance);

            // 실제 배치는 기준선 + 패턴 보정이다. 합계를 보여줘야 저작자가 무엇으로 굽는지 안다.
            if (targetPattern != null)
            {
                EditorGUILayout.LabelField(" ",
                    $"실제 배치 거리 {DuelDistance:0.00}m  (기준 {duelBaseDistance:0.00} + 패턴 보정 {targetPattern.DuelDistanceOffset:+0.00;-0.00;0.00})",
                    EditorStyles.miniLabel);
            }

            DrawBladePlaneBlock();
            DrawPoseInfo();
        }

        /// <summary>
        /// 임팩트 프레임의 칼 궤적에서 평면을 유도한다. <b>가이드일 뿐</b> — 획 목록에 넣은 뒤 손으로 조정할 수 있다.
        /// </summary>
        private void DrawBladePlaneBlock()
        {
            if (targetPattern == null)
            {
                EditorGUILayout.HelpBox("Pattern을 지정하면 임팩트 프레임의 칼 궤적에서 평면을 유도할 수 있습니다. 지정하지 않아도 획만으로 구울 수 있습니다.", MessageType.None);
                return;
            }

            var attack = targetPattern.PlayerAttack;
            if (attack == null || attack.Clip == null)
            {
                EditorGUILayout.HelpBox("Pattern에 playerAttack 클립이 없습니다. 칼 궤적을 뜰 수 없습니다.", MessageType.Warning);
                return;
            }

            // 임팩트가 미오서링(0 이하)이면 ClipAlignment가 트림 끝으로 폴백하므로 유도값이 임팩트와 무관해진다.
            if (attack.ImpactTime <= 0f)
            {
                EditorGUILayout.HelpBox(
                    "playerAttack의 ImpactTime이 지정되지 않았습니다(0 이하). 트림 끝으로 폴백되어 " +
                    "유도된 평면이 실제 임팩트와 무관해집니다. Tools/Animation Clip Trimmer로 임팩트를 먼저 찍으세요.",
                    MessageType.Error);
                return;
            }

            using (new EditorGUI.DisabledScope(playerPrefab == null || enemyPrefab == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("칼 평면 유도")) DeriveBladePlane();
                using (new EditorGUI.DisabledScope(!hasBladePlane))
                {
                    if (GUILayout.Button("이 평면 사용"))
                    {
                        planes.Add(bladePlane);
                        selectedPlane = planes.Count - 1;
                        ClearPreviewPieces();
                        Repaint();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            if (hasBladePlane)
            {
                EditorGUILayout.LabelField("유도 평면",
                    $"n=({bladePlane.normal.x:0.00}, {bladePlane.normal.y:0.00}, {bladePlane.normal.z:0.00})  d={bladePlane.distance:0.000}");
            }
        }

        private void DrawPoseInfo()
        {
            if (targetPattern == null) return;

            var death = targetPattern.EnemyDeath;
            if (death == null || death.Clip == null)
            {
                EditorGUILayout.HelpBox(
                    "Pattern에 enemyDeath 클립이 없습니다. 바인드 포즈(T-Pose)로 굽습니다. " +
                    "포즈는 정합성 요구가 아니라 저작 보조지만, 죽는 자세와 가까울수록 관절 뒤틀림이 줄어듭니다.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("굽기 포즈", $"{death.Clip.name} @ {death.ImpactTime:0.000}s");
        }

        // ── 획 목록 ─────────────────────────────────────────────────────────────

        private void DrawPlaneList()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"절단 평면 {planes.Count}장", EditorStyles.miniBoldLabel);

            if (GUILayout.Button("전체 삭제", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                planes.Clear();
                selectedPlane = -1;
                MarkCustom();
                ClearPreviewPieces();
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < planes.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                bool isSelected = selectedPlane == i;
                if (GUILayout.Toggle(isSelected, $"획 {i + 1}", EditorStyles.miniButton, GUILayout.Width(50)) != isSelected)
                    selectedPlane = isSelected ? -1 : i;

                if (GUILayout.Button("삭제", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    planes.RemoveAt(i);
                    if (selectedPlane >= planes.Count) selectedPlane = planes.Count - 1;
                    MarkCustom();
                    ClearPreviewPieces();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                var p = planes[i];
                Vector3 n = EditorGUILayout.Vector3Field("법선", p.normal);
                float d = EditorGUILayout.FloatField("거리", p.distance);
                if (EditorGUI.EndChangeCheck())
                {
                    planes[i] = new SlicePlane(n.sqrMagnitude < 1e-8f ? Vector3.up : n, d);
                    MarkCustom();
                    ClearPreviewPieces();
                }

                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>프리셋과 어긋난 채 'Cross' 같은 라벨로 저장되는 혼동을 막는다. 휴머노이드는 항상 Custom이다.</summary>
        private void MarkCustom()
        {
            if (mode == BakeMode.StaticMesh && shape != SliceShape.Custom) shape = SliceShape.Custom;
        }

        private void ApplyPreset()
        {
            var mesh = GetSourceMesh();
            if (mesh == null) return;

            planes.Clear();
            planes.AddRange(shape.ToPlanes(mesh.bounds));
            selectedPlane = -1;
            ClearPreviewPieces();
        }

        // ── 창 내부 프리뷰 ──────────────────────────────────────────────────────

        private void DrawPreview()
        {
            previewRect = GUILayoutUtility.GetRect(position.width, PreviewHeight, GUILayout.ExpandWidth(true));

            var mesh = GetSourceMesh();
            if (mesh == null)
            {
                EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f));
                EditorGUI.LabelField(previewRect,
                    mode == BakeMode.StaticMesh
                        ? "표적 프리팹을 지정하면 여기에 표시됩니다."
                        : "적 프리팹을 지정하면 여기에 표시됩니다.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            HandlePreviewInput(previewRect, mesh);

            if (Event.current.type == EventType.Repaint)
            {
                previewUtil ??= new PreviewRenderUtility();
                SetupCamera(mesh.bounds);

                previewUtil.BeginPreview(previewRect, GUIStyle.none);
                RenderPreviewContents(mesh);
                previewUtil.Render(true);
                GUI.DrawTexture(previewRect, previewUtil.EndPreview(), ScaleMode.StretchToFill, false);
            }

            DrawPreviewOverlay(mesh);
            DrawPreviewToolbar();
        }

        private void RenderPreviewContents(Mesh mesh)
        {
            var materials = GetSourceMaterials();

            if (previewPieces != null && previewPieces.Count > 0)
            {
                Material capMat = capMaterial != null ? capMaterial : (materials.Length > 0 ? materials[0] : null);
                Vector3 center = mesh.bounds.center;

                for (int i = 0; i < previewPieces.Count; i++)
                {
                    var piece = previewPieces[i];

                    // 조각 피벗은 무게중심이므로, 원본 위치로 되돌린 뒤 바깥 방향으로 explode만큼 민다.
                    Vector3 outward = piece.centroid - center;
                    Vector3 pos = piece.centroid + outward.normalized * (outward.magnitude * explode);
                    var matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

                    for (int s = 0; s < piece.mesh.subMeshCount; s++)
                    {
                        Material m = s < materials.Length ? materials[s] : capMat;
                        if (s == piece.capSubMesh) m = capMat;
                        if (m == null) continue;
                        previewUtil.DrawMesh(piece.mesh, matrix, m, s);
                    }
                }
                return;
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                Material m = s < materials.Length ? materials[s] : null;
                if (m == null) continue;
                previewUtil.DrawMesh(mesh, Matrix4x4.identity, m, s);
            }
        }

        private void SetupCamera(Bounds b)
        {
            var cam = previewUtil.camera;
            cam.fieldOfView = camFov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

            float radius = Mathf.Max(b.extents.magnitude, 0.1f);
            float dist = radius / Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad) * zoom;

            camRot = Quaternion.Euler(pitch, yaw, 0f);
            camPos = b.center + camRot * new Vector3(0f, 0f, -dist);

            cam.transform.position = camPos;
            cam.transform.rotation = camRot;

            previewUtil.ambientColor = new Color(0.45f, 0.45f, 0.45f);
            if (previewUtil.lights.Length > 0)
            {
                previewUtil.lights[0].intensity = 1.1f;
                previewUtil.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            }
            if (previewUtil.lights.Length > 1)
            {
                previewUtil.lights[1].intensity = 0.6f;
                previewUtil.lights[1].transform.rotation = Quaternion.Euler(-20f, -120f, 0f);
            }
        }

        private void DrawPreviewToolbar()
        {
            var bar = new Rect(previewRect.x + 6f, previewRect.yMax - 24f, previewRect.width - 12f, 20f);
            GUILayout.BeginArea(bar);
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label(previewPieces != null ? "분해" : "", EditorStyles.miniLabel, GUILayout.Width(28));
            using (new EditorGUI.DisabledScope(previewPieces == null))
                explode = GUILayout.HorizontalSlider(explode, 0f, 1f, GUILayout.Width(120));

            GUILayout.FlexibleSpace();
            GUILayout.Label("좌드래그=획 · 우드래그=회전 · 휠=줌 · Shift=스냅", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>평면 단면·유도된 칼 평면·그리는 중인 획을 창 좌표로 투영해 겹쳐 그린다.</summary>
        private void DrawPreviewOverlay(Mesh mesh)
        {
            if (Event.current.type != EventType.Repaint) return;

            Handles.BeginGUI();

            Bounds b = mesh.bounds;
            float extent = b.extents.magnitude;

            for (int i = 0; i < planes.Count; i++)
                DrawPlaneOutline(planes[i], b, extent, i == selectedPlane ? Color.yellow : new Color(0.3f, 0.85f, 1f, 0.9f), i == selectedPlane ? 3f : 2f);

            // 유도된 칼 평면은 아직 획이 아니므로 다른 색으로 구분해 그린다.
            if (hasBladePlane && mode == BakeMode.Humanoid)
                DrawPlaneOutline(bladePlane, b, extent, new Color(1f, 0.45f, 0.2f, 0.95f), 3f);

            if (dragging)
            {
                Handles.color = Color.yellow;
                Handles.DrawAAPolyLine(4f, (Vector3)dragStart, (Vector3)dragCurrent);
            }

            Handles.EndGUI();
        }

        private void DrawPlaneOutline(SlicePlane p, Bounds b, float extent, Color color, float width)
        {
            p.GetTangentBasis(out Vector3 tangent, out Vector3 bitangent);
            Vector3 center = b.center - p.normal * p.SignedDistance(b.center);

            Vector3[] corners =
            {
                center + (tangent + bitangent) * extent,
                center + (tangent - bitangent) * extent,
                center + (-tangent - bitangent) * extent,
                center + (-tangent + bitangent) * extent
            };

            var pts = new Vector3[5];
            for (int c = 0; c < 4; c++)
            {
                if (!WorldToGui(corners[c], out Vector2 g)) return;
                pts[c] = g;
            }
            pts[4] = pts[0];

            Handles.color = color;
            Handles.DrawAAPolyLine(width, pts);
        }

        private void HandlePreviewInput(Rect rect, Mesh mesh)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition) && !dragging) return;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    dragging = true;
                    dragStart = dragCurrent = e.mousePosition;
                    e.Use();
                    break;

                case EventType.MouseDrag when dragging && e.button == 0:
                    dragCurrent = ApplySnap(dragStart, e.mousePosition, e.shift);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when dragging && e.button == 0:
                    dragging = false;
                    AddPlaneFromStroke(dragStart, ApplySnap(dragStart, e.mousePosition, e.shift), mesh);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 1 || e.button == 2:
                    yaw += e.delta.x;
                    pitch = Mathf.Clamp(pitch - e.delta.y, -89f, 89f);
                    e.Use();
                    Repaint();
                    break;

                case EventType.ScrollWheel:
                    zoom = Mathf.Clamp(zoom + e.delta.y * 0.05f, 0.2f, 5f);
                    e.Use();
                    Repaint();
                    break;
            }
        }

        /// <summary>Shift를 누르면 화면 축(수평/수직/45°)으로 스냅한다 — 정확한 ┼/X를 손떨림 없이 긋기 위함.</summary>
        private static Vector2 ApplySnap(Vector2 start, Vector2 end, bool snap)
        {
            if (!snap) return end;

            Vector2 delta = end - start;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(angle / 45f) * 45f * Mathf.Deg2Rad;
            return start + new Vector2(Mathf.Cos(snapped), Mathf.Sin(snapped)) * delta.magnitude;
        }

        /// <summary>창 좌표 획 → 무한 평면. 프리뷰는 메쉬를 원점(identity)에 그리므로 프리뷰 월드가 곧 메쉬 로컬이다.</summary>
        private void AddPlaneFromStroke(Vector2 screenStart, Vector2 screenEnd, Mesh mesh)
        {
            if ((screenEnd - screenStart).sqrMagnitude < 25f) return; // 클릭에 가까운 미세 드래그는 무시

            Vector3 forward = camRot * Vector3.forward;
            var depthPlane = new Plane(forward, mesh.bounds.center);

            Ray r0 = GuiPointToRay(screenStart);
            Ray r1 = GuiPointToRay(screenEnd);
            if (!depthPlane.Raycast(r0, out float e0) || !depthPlane.Raycast(r1, out float e1)) return;

            Vector3 p0 = r0.GetPoint(e0);
            Vector3 p1 = r1.GetPoint(e1);

            Vector3 normal = Vector3.Cross(p1 - p0, forward);
            if (normal.sqrMagnitude < 1e-8f) return;

            planes.Add(SlicePlane.FromPointNormal(p0, normal.normalized));
            selectedPlane = planes.Count - 1;
            MarkCustom();
            ClearPreviewPieces();

            Repaint();
        }

        private Ray GuiPointToRay(Vector2 guiPoint)
        {
            float tanHalf = Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad);
            float aspect = previewRect.width / Mathf.Max(previewRect.height, 1f);

            float nx = (guiPoint.x - previewRect.x) / previewRect.width * 2f - 1f;
            float ny = 1f - (guiPoint.y - previewRect.y) / previewRect.height * 2f;

            Vector3 dirCam = new Vector3(nx * tanHalf * aspect, ny * tanHalf, 1f);
            return new Ray(camPos, (camRot * dirCam).normalized);
        }

        private bool WorldToGui(Vector3 world, out Vector2 gui)
        {
            Vector3 local = Quaternion.Inverse(camRot) * (world - camPos);
            gui = default;
            if (local.z <= 0.0001f) return false;

            float tanHalf = Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad);
            float aspect = previewRect.width / Mathf.Max(previewRect.height, 1f);

            float nx = local.x / (local.z * tanHalf * aspect);
            float ny = local.y / (local.z * tanHalf);

            gui = new Vector2(
                previewRect.x + (nx * 0.5f + 0.5f) * previewRect.width,
                previewRect.y + (1f - (ny * 0.5f + 0.5f)) * previewRect.height);
            return true;
        }

        private void ClearPreviewPieces()
        {
            if (previewPieces != null)
            {
                foreach (var p in previewPieces)
                    if (p.mesh != null) DestroyImmediate(p.mesh);
                previewPieces = null;
            }
            explode = 0f;
            previewText = "";
        }

        private void TryLoadExistingSet()
        {
            existingSet = null;
            if (string.IsNullOrEmpty(setName)) return;

            var found = AssetDatabase.LoadAssetAtPath<SliceSet>(SetAssetPath());
            if (found == null) return;

            existingSet = found;

            if (found.BakedPlanes != null && found.BakedPlanes.Length > 0)
            {
                planes.Clear();
                planes.AddRange(found.BakedPlanes);
            }
            if (mode == BakeMode.StaticMesh) shape = found.Shape;
            if (capMaterial == null) capMaterial = FindCapMaterial(found);
        }

        private static Material FindCapMaterial(SliceSet set)
        {
            if (set.PiecePrefabs == null || set.PiecePrefabs.Length == 0) return null;
            var renderer = set.PiecePrefabs[0] != null ? set.PiecePrefabs[0].GetComponent<MeshRenderer>() : null;
            if (renderer == null) return null;

            var mats = renderer.sharedMaterials;
            return mats.Length > 0 ? mats[mats.Length - 1] : null;
        }

        // ── 소스 메쉬 / 포즈 ─────────────────────────────────────────────────────
        //
        // 휴머노이드는 '저작된 사망 포즈'에서 BakeMesh로 정적 메쉬를 뜬 뒤,
        // 원본의 boneWeights와 '그 포즈에서 재계산한 bindposes'를 도로 실어 준다.
        // 그래야 절단 코어가 스키닝을 관통시키고, 조각이 다시 스킨드 메쉬가 된다.

        private Mesh posedCache;
        private GameObject posedSource;
        private AnimationClip posedClip;
        private float posedTime = -1f;
        private int posedRendererIndex = -1;

        private void ClearPoseCache()
        {
            if (posedCache != null) DestroyImmediate(posedCache);
            posedCache = null;
            posedSource = null;
            posedClip = null;
            posedTime = -1f;
            posedRendererIndex = -1;
        }

        private SkinnedMeshRenderer[] GetEnemyRenderers()
        {
            if (enemyPrefab == null) return new SkinnedMeshRenderer[0];
            return enemyPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        /// <summary>
        /// 기본 선택은 <b>본이 가장 많은 렌더러</b>다 — 그게 몸통이다.
        /// 계층 순서로 0번을 잡으면 본 한두 개짜리 칼·검집이 걸릴 수 있고
        /// (예: <c>T-Pose.FBX</c>의 첫 렌더러는 본 1개짜리 <c>BladeL</c>),
        /// 그걸로 구우면 <b>칼 한 자루가 시체가 된다.</b>
        /// </summary>
        private int DefaultRendererIndex(SkinnedMeshRenderer[] renderers)
        {
            int best = 0;
            for (int i = 1; i < renderers.Length; i++)
                if (renderers[i].bones.Length > renderers[best].bones.Length) best = i;
            return best;
        }

        private Mesh GetSourceMesh()
        {
            if (mode == BakeMode.StaticMesh)
            {
                if (sourcePrefab == null) return null;
                var filter = sourcePrefab.GetComponentInChildren<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }

            return GetPosedSkinnedMesh();
        }

        /// <summary>사망 포즈에서 스키닝 메쉬를 뜬다. <c>OnGUI</c>가 반복 호출하므로 결과를 캐시한다.</summary>
        private Mesh GetPosedSkinnedMesh()
        {
            var renderers = GetEnemyRenderers();
            if (renderers.Length == 0) return null;

            int index = Mathf.Clamp(enemyRendererIndex, 0, renderers.Length - 1);
            var death = targetPattern != null ? targetPattern.EnemyDeath : null;
            var clip = death?.Clip;
            float time = death != null ? death.ImpactTime : 0f;

            bool valid = posedCache != null
                && posedSource == enemyPrefab
                && posedClip == clip
                && posedRendererIndex == index
                && Mathf.Approximately(posedTime, time);
            if (valid) return posedCache;

            ClearPoseCache();

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
            if (temp == null) return null;

            try
            {
                temp.hideFlags = HideFlags.HideAndDontSave;

                // 클립이 없으면 바인드 포즈 그대로(T-Pose). 포즈는 정합성 요구가 아니라 저작 보조라 이래도 동작한다.
                if (clip != null) clip.SampleAnimation(temp, time);

                var skinned = temp.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (index >= skinned.Length) return null;

                var target = skinned[index];
                if (target.sharedMesh == null) return null;

                var baked = new Mesh { name = $"{enemyPrefab.name}_Posed" };
                target.BakeMesh(baked, true);

                // BakeMesh 산출물에는 스키닝이 없다 — 정점 수·순서가 보존되므로 원본에서 그대로 옮긴다.
                var weights = target.sharedMesh.boneWeights;
                if (weights != null && weights.Length == baked.vertexCount)
                {
                    baked.boneWeights = weights;
                    baked.bindposes = ComputeBindposes(target);
                }
                else
                {
                    Debug.LogWarning(
                        $"[MeshSliceBaker] '{enemyPrefab.name}'의 boneWeights 길이({weights?.Length ?? 0})가 " +
                        $"BakeMesh 정점 수({baked.vertexCount})와 다릅니다. 스키닝 없이 굽습니다.", enemyPrefab);
                }

                posedCache = baked;
                posedSource = enemyPrefab;
                posedClip = clip;
                posedTime = time;
                posedRendererIndex = index;
                return baked;
            }
            finally
            {
                DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// 지금 포즈를 rest로 삼는 bindposes. <c>bindpose[i] = bones[i].worldToLocal * meshTransform.localToWorld</c>
        /// — 그래야 조각이 "이 포즈를 기본 자세로 갖는 정상 스킨드 메쉬"가 되어 스켈레톤을 따라간다.
        /// </summary>
        private static Matrix4x4[] ComputeBindposes(SkinnedMeshRenderer renderer)
        {
            var bones = renderer.bones;
            var result = new Matrix4x4[bones.Length];
            Matrix4x4 meshToWorld = renderer.transform.localToWorldMatrix;

            for (int i = 0; i < bones.Length; i++)
            {
                result[i] = bones[i] != null
                    ? bones[i].worldToLocalMatrix * meshToWorld
                    : Matrix4x4.identity;
            }

            return result;
        }

        private Material[] GetSourceMaterials()
        {
            if (mode == BakeMode.StaticMesh)
            {
                if (sourcePrefab == null) return new Material[0];
                var renderer = sourcePrefab.GetComponentInChildren<MeshRenderer>();
                return renderer != null ? renderer.sharedMaterials : new Material[0];
            }

            var renderers = GetEnemyRenderers();
            if (renderers.Length == 0) return new Material[0];
            return renderers[Mathf.Clamp(enemyRendererIndex, 0, renderers.Length - 1)].sharedMaterials;
        }

        // ── 검증 / Preview / Bake ───────────────────────────────────────────────

        private string GetBlockingReason()
        {
            if (mode == BakeMode.StaticMesh)
            {
                if (sourcePrefab == null) return "표적 프리팹을 지정하세요.";
            }
            else
            {
                if (enemyDefinition == null) return "적 정의(EnemyDefinition)를 지정하세요.";
                if (enemyPrefab == null) return $"'{enemyDefinition.name}'에 프리팹이 배선되지 않았습니다.";
                if (GetEnemyRenderers().Length == 0) return "적 프리팹에서 SkinnedMeshRenderer를 찾을 수 없습니다.";
            }

            // 적이 베어지는 건 플레이어가 공격자일 때뿐이다. Enemy 패턴에 구우면 절대 재생되지 않는 에셋이 생긴다.
            if (mode == BakeMode.Humanoid && targetPattern != null
                && targetPattern.Attacker == EnemySpace.Attacker.Enemy)
            {
                return $"'{targetPattern.name}'의 attacker가 Enemy입니다(적이 공격 → 플레이어 패링). " +
                       "이 역할에서는 적이 베어지지 않으므로 구워도 재생되지 않습니다. " +
                       "패턴의 attacker를 Player로 바꾸거나 다른 패턴을 지정하세요.";
            }

            if (string.IsNullOrEmpty(setName)) return "세트 이름을 입력하세요.";

            var mesh = GetSourceMesh();
            if (mesh == null) return "원본 메쉬를 얻을 수 없습니다.";

            string invalid = mode == BakeMode.StaticMesh
                ? MeshSliceBaker.ValidateStatic(mesh)
                : MeshSliceBaker.ValidateSkinned(mesh);
            if (invalid != null) return invalid;

            var mats = GetSourceMaterials();
            if (mats.Length == 0 || mats[0] == null)
                return "원본 머티리얼이 비어 있거나 [0]이 null입니다. 단면 폴백 대상이 없어 구울 수 없습니다.";

            if (planes.Count == 0) return "절단 평면이 없습니다. 프리뷰에서 획을 긋거나 프리셋/칼 평면을 쓰세요.";
            return null;
        }

        private SliceOptions BuildOptions() => new SliceOptions
        {
            weldEpsilon = weldEpsilon,
            minPieceVolumeRatio = minPieceVolumeRatio,
            capUvScale = capUvScale
        };

        private void RunPreview()
        {
            var mesh = GetSourceMesh();
            var mats = GetSourceMaterials();

            ClearPreviewPieces();
            previewPieces = MeshSliceBaker.Slice(mesh, planes, BuildOptions(), out int discarded);

            int capIndex = mesh.subMeshCount;
            Material capMat = capMaterial != null ? capMaterial : mats[0];
            string capSource = capMaterial != null ? "지정" : $"폴백(원본[0] '{mats[0].name}')";

            int rootPiece = mode == BakeMode.Humanoid ? FindRootPiece(previewPieces) : -1;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"조각 {previewPieces.Count}개" + (discarded > 0 ? $" (퇴화 {discarded}개 폐기)" : ""));
            sb.AppendLine($"머티리얼 슬롯: {mats.Length + 1}개 — 캡은 인덱스 {capIndex}, '{capMat.name}' [{capSource}]");

            for (int i = 0; i < previewPieces.Count; i++)
            {
                var p = previewPieces[i];
                string bones = mode == BakeMode.Humanoid ? $", 영향 본 {CountInfluencingBones(p.mesh)}개" : "";
                string root = i == rootPiece ? "  ◀ 루트(스킨드로 남음 · 래그돌 대상)" : "";
                sb.AppendLine($"  Piece_{i}: 정점 {p.mesh.vertexCount}, 삼각형 {p.mesh.triangles.Length / 3}, 캡 루프 {p.capLoopCount}{bones}{root}");
            }

            if (mode == BakeMode.Humanoid && rootPiece < 0)
                sb.AppendLine("⚠ 루트 조각을 찾지 못했습니다. 절단이 루트 본을 통째로 잘라냈는지 확인하세요.");

            previewText = sb.ToString();
            explode = 0.35f; // 갈라진 모습이 바로 보이게
            Repaint();
        }

        private static int CountInfluencingBones(Mesh mesh)
        {
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return 0;

            var set = new HashSet<int>();
            foreach (var w in weights)
            {
                if (w.weight0 > 0f) set.Add(w.boneIndex0);
                if (w.weight1 > 0f) set.Add(w.boneIndex1);
                if (w.weight2 > 0f) set.Add(w.boneIndex2);
                if (w.weight3 > 0f) set.Add(w.boneIndex3);
            }
            return set.Count;
        }

        /// <summary>
        /// 루트 본(인덱스 0 기준 — <c>bones[0]</c>이 리그 루트다)의 가중치 총합이 가장 큰 조각.
        /// 이 조각만 스킨드로 남아 스켈레톤을 따라가고(래그돌 대상), 나머지는 굳어 날아간다.
        /// </summary>
        private int FindRootPiece(List<SlicedPiece> pieces)
        {
            if (pieces == null || pieces.Count == 0) return -1;

            int rootBone = FindRootBoneIndex();
            int best = -1;
            float bestWeight = 0f;

            for (int i = 0; i < pieces.Count; i++)
            {
                float sum = 0f;
                var weights = pieces[i].mesh.boneWeights;
                if (weights == null) continue;

                foreach (var w in weights)
                {
                    if (w.boneIndex0 == rootBone) sum += w.weight0;
                    if (w.boneIndex1 == rootBone) sum += w.weight1;
                    if (w.boneIndex2 == rootBone) sum += w.weight2;
                    if (w.boneIndex3 == rootBone) sum += w.weight3;
                }

                if (sum <= bestWeight) continue;
                bestWeight = sum;
                best = i;
            }

            // 루트 본에 걸린 조각이 없으면(절단이 루트를 비껴갔다) 가장 큰 조각을 쓴다 — 무연출보다 낫다.
            if (best >= 0) return best;

            float bestVolume = 0f;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].volume <= bestVolume) continue;
                bestVolume = pieces[i].volume;
                best = i;
            }
            return best;
        }

        private int FindRootBoneIndex()
        {
            var renderers = GetEnemyRenderers();
            if (renderers.Length == 0) return 0;

            var renderer = renderers[Mathf.Clamp(enemyRendererIndex, 0, renderers.Length - 1)];
            var bones = renderer.bones;
            if (bones == null || bones.Length == 0) return 0;

            var root = renderer.rootBone != null ? renderer.rootBone : bones[0];
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] == root) return i;

            return 0;
        }

        // ── 칼 평면 유도 ────────────────────────────────────────────────────────

        /// <summary>
        /// 임팩트 프레임의 칼 궤적에서 절단 평면을 만든다.
        ///
        /// <para><b>법선 = 칼날 장축 × 진행 방향.</b> 두 벡터가 만드는 면이 곧 칼이 지나간 면이다.
        /// 진행 방향은 임팩트 전후 프레임의 칼 위치 차분으로 잡는다(<c>WeaponBoneBake</c>의 롤 보정과 같은 방식).</para>
        ///
        /// <para>적 배치는 <see cref="DuelDistance"/>(기준선 + 패턴 보정)로 재현한다 — 씬 참조를 끌어오면
        /// 프리팹만으로 굽지 못하게 된다.</para>
        /// </summary>
        private void DeriveBladePlane()
        {
            var attack = targetPattern != null ? targetPattern.PlayerAttack : null;
            if (attack?.Clip == null || playerPrefab == null || enemyPrefab == null) return;

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            var enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
            if (player == null || enemy == null)
            {
                if (player != null) DestroyImmediate(player);
                if (enemy != null) DestroyImmediate(enemy);
                return;
            }

            try
            {
                player.hideFlags = HideFlags.HideAndDontSave;
                enemy.hideFlags = HideFlags.HideAndDontSave;

                // 플레이어는 원점에서 +Z를 보고, 적은 결투 거리(기준선 + 패턴 보정) 앞에서 마주 본다.
                player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                enemy.transform.SetPositionAndRotation(Vector3.forward * DuelDistance, Quaternion.Euler(0f, 180f, 0f));

                float dt = 1f / Mathf.Max(attack.Clip.frameRate > 0f ? attack.Clip.frameRate : 30f, 1f);

                if (!SampleWeapon(player, attack.Clip, attack.ImpactTime, out Vector3 at, out Vector3 axis)) return;
                SampleWeapon(player, attack.Clip, Mathf.Max(attack.ImpactTime - dt, 0f), out Vector3 before, out _);
                SampleWeapon(player, attack.Clip, Mathf.Min(attack.ImpactTime + dt, attack.Clip.length), out Vector3 after, out _);

                Vector3 travel = after - before;
                if (travel.sqrMagnitude < 1e-8f) travel = Vector3.up; // 정지 프레임 — 세로 궤적으로 가정

                Vector3 normal = Vector3.Cross(axis, travel);
                if (normal.sqrMagnitude < 1e-8f)
                {
                    Debug.LogWarning("[MeshSliceBaker] 칼날 장축과 진행 방향이 나란해 평면을 만들 수 없습니다. 획을 직접 그으세요.");
                    return;
                }

                // 월드 평면 → 적 메쉬 로컬. 같은 포즈 공간이라 강체 변환 하나로 정확히 옮겨진다.
                var skinned = enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int index = Mathf.Clamp(enemyRendererIndex, 0, skinned.Length - 1);
                var meshTransform = skinned[index].transform;

                Vector3 localPoint = meshTransform.InverseTransformPoint(at);
                Vector3 localNormal = meshTransform.InverseTransformDirection(normal).normalized;

                bladePlane = SlicePlane.FromPointNormal(localPoint, localNormal);
                hasBladePlane = true;
                Repaint();
            }
            finally
            {
                DestroyImmediate(player);
                DestroyImmediate(enemy);
            }
        }

        /// <summary>클립을 샘플링해 칼의 월드 위치와 장축을 얻는다.</summary>
        /// <summary>
        /// 클립을 샘플링해 칼의 월드 위치와 장축을 얻는다.
        ///
        /// <para><b>배치를 샘플링 뒤에 복원한다.</b> <see cref="AnimationClip.SampleAnimation"/>은
        /// 루트 트랜스폼을 클립 값으로 덮어쓰므로, 그냥 두면 플레이어가 딴 자리로 옮겨진 채
        /// 칼의 월드 위치를 읽게 된다 — 그 위치로 만든 절단 평면은 <b>엉뚱한 데를 자른다</b>.</para>
        /// </summary>
        private static bool SampleWeapon(GameObject player, AnimationClip clip, float time, out Vector3 position, out Vector3 axis)
        {
            position = Vector3.zero;
            axis = Vector3.up;

            var tr = player.transform;
            Vector3 placement = tr.position;
            Quaternion facing = tr.rotation;

            clip.SampleAnimation(player, time);

            // 루트 모션은 버리고 배치를 되돌린다 — 두 배우의 상대 배치가 곧 절단 평면의 기준이다.
            tr.SetPositionAndRotation(placement, facing);

            Transform weapon = FindDeep(player.transform, WeaponBoneName);
            if (weapon == null)
            {
                Debug.LogWarning($"[MeshSliceBaker] 플레이어 프리팹에서 '{WeaponBoneName}' 본을 찾지 못했습니다.");
                return false;
            }

            position = weapon.position;

            // 장축은 칼 메쉬 바운즈의 최장 축이다 — 어느 로컬 축이 날 길이인지 추측하지 않는다.
            var renderer = weapon.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                position = renderer.bounds.center;

                Vector3 ext = renderer.localBounds.extents;
                Vector3 localAxis = ext.x >= ext.y && ext.x >= ext.z ? Vector3.right
                    : ext.y >= ext.z ? Vector3.up
                    : Vector3.forward;
                axis = renderer.transform.TransformDirection(localAxis).normalized;
            }
            else axis = weapon.up;

            return true;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        // ── Bake ────────────────────────────────────────────────────────────────

        private void Bake()
        {
            if (mode == BakeMode.StaticMesh) BakeStatic();
            else BakeHumanoid();
        }

        private void BakeStatic()
        {
            var mesh = GetSourceMesh();
            var pieces = MeshSliceBaker.Slice(mesh, planes, BuildOptions(), out int discarded);

            if (pieces.Count == 0)
            {
                EditorUtility.DisplayDialog("Bake 실패", "조각이 하나도 나오지 않았습니다. 평면 위치를 확인하세요.", "확인");
                return;
            }

            var sourceMaterials = GetSourceMaterials();
            Material capMat = capMaterial != null ? capMaterial : sourceMaterials[0];
            WarnCapMaterial(sourceMaterials);

            string folder = EnsureFolder();
            var pieceMaterials = BuildMaterialSlots(sourceMaterials, capMat);

            var piecePrefabs = new GameObject[pieces.Count];
            var offsets = new Vector3[pieces.Count];
            var scatterDirs = new Vector3[pieces.Count];

            Vector3 sourceCenter = mesh.bounds.center;

            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                var savedMesh = SaveMeshPreservingGuid(piece.mesh, $"{folder}/{PieceMeshPrefix}{i}.asset");
                piecePrefabs[i] = SavePiecePrefabPreservingGuid(savedMesh, pieceMaterials, $"{folder}/{PieceMeshPrefix}{i}.prefab");

                offsets[i] = piece.centroid;
                scatterDirs[i] = piece.centroid - sourceCenter;
            }

            CleanupRemovedPieces(folder, pieces.Count);

            var set = SaveSetPreservingGuid(SetAssetPath());
            set.EditorAssign(sourcePrefab, piecePrefabs, offsets, scatterDirs, planes.ToArray(), shape);
            EditorUtility.SetDirty(set);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            existingSet = set;
            previewText = $"Bake 완료: 조각 {pieces.Count}개 → {folder}";
            Debug.Log($"[MeshSliceBaker] '{setName}' 굽기 완료 — 조각 {pieces.Count}개" +
                      (discarded > 0 ? $", 퇴화 {discarded}개 폐기" : "") +
                      ". 이 세트를 쓰려면 패턴 에셋에 배선하세요.", set);
            Selection.activeObject = set;
        }

        /// <summary>
        /// 휴머노이드 굽기 — 산출물은 <b>시체 프리팹 하나</b>다(조각 프리팹 N개가 아니라).
        /// 시체가 자기 스켈레톤 사본을 가져야 교체 시 산 적을 풀로 반납할 수 있다.
        /// </summary>
        private void BakeHumanoid()
        {
            var mesh = GetSourceMesh();
            var pieces = MeshSliceBaker.Slice(mesh, planes, BuildOptions(), out int discarded);

            if (pieces.Count == 0)
            {
                EditorUtility.DisplayDialog("Bake 실패", "조각이 하나도 나오지 않았습니다. 평면 위치를 확인하세요.", "확인");
                return;
            }

            var sourceMaterials = GetSourceMaterials();
            Material capMat = capMaterial != null ? capMaterial : sourceMaterials[0];
            WarnCapMaterial(sourceMaterials);

            string folder = EnsureFolder();
            var pieceMaterials = BuildMaterialSlots(sourceMaterials, capMat);

            var savedMeshes = new Mesh[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                // 조각 피벗을 무게중심으로 옮겨 두면 스키닝 좌표계가 어긋난다 — 원래 자리로 되돌린다.
                var restored = TranslateMesh(pieces[i].mesh, pieces[i].centroid);
                savedMeshes[i] = SaveMeshPreservingGuid(restored, $"{folder}/{PieceMeshPrefix}{i}.asset");
            }

            CleanupRemovedPieces(folder, pieces.Count);

            int rootPiece = FindRootPiece(pieces);
            var corpsePrefab = BuildCorpsePrefab(savedMeshes, pieceMaterials, rootPiece, $"{folder}/{setName}_Corpse.prefab");
            if (corpsePrefab == null) return;

            var death = targetPattern != null ? targetPattern.EnemyDeath : null;

            var set = SaveSetPreservingGuid(SetAssetPath());
            set.EditorAssignSkinned(enemyPrefab, corpsePrefab, rootPiece, planes.ToArray(), death?.Clip, death?.ImpactTime ?? 0f);
            EditorUtility.SetDirty(set);

            WireToDefinition(set);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            existingSet = set;
            previewText = $"Bake 완료: 조각 {pieces.Count}개 (루트 {rootPiece}) → {folder}";
            Debug.Log($"[MeshSliceBaker] '{setName}' 휴머노이드 굽기 완료 — 조각 {pieces.Count}개" +
                      (discarded > 0 ? $", 퇴화 {discarded}개 폐기" : "") +
                      $", 루트 조각 {rootPiece}.", set);
            Selection.activeObject = set;
        }

        private void WarnCapMaterial(Material[] sourceMaterials)
        {
            if (capMaterial != null) return;

            if (sourceMaterials.Length > 1)
                Debug.LogWarning($"[MeshSliceBaker] 원본 머티리얼이 {sourceMaterials.Length}개입니다 — 캡에 [0] '{sourceMaterials[0].name}'을 적용했습니다. 의도와 다르면 단면 머티리얼을 지정하세요.");
            else
                Debug.Log($"[MeshSliceBaker] 단면 머티리얼 미지정 — 원본 머티리얼 '{sourceMaterials[0].name}'을 캡에 적용했습니다.");
        }

        /// <summary>조각 메쉬를 원본 좌표계로 되돌린다. 스킨드 조각은 피벗을 옮기면 스키닝이 어긋난다.</summary>
        private static Mesh TranslateMesh(Mesh mesh, Vector3 delta)
        {
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] += delta;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 시체 프리팹을 조립한다 — <b>적 프리팹의 본 계층을 그대로 복제</b>하고 렌더러만 조각으로 갈아 끼운다.
        /// 계층을 복제하므로 본 배열의 순서가 원본과 같고, 런타임 포즈 전사가 인덱스 루프로 끝난다.
        /// </summary>
        private GameObject BuildCorpsePrefab(Mesh[] meshes, Material[] materials, int rootPiece, string path)
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
            if (temp == null) return null;

            try
            {
                temp.hideFlags = HideFlags.HideAndDontSave;
                PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                temp.name = $"{setName}_Corpse";

                var sourceRenderers = temp.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int index = Mathf.Clamp(enemyRendererIndex, 0, sourceRenderers.Length - 1);
                var source = sourceRenderers[index];

                var bones = source.bones;
                var rootBone = source.rootBone;

                // 원본 렌더러는 전부 걷어낸다 — 시체에 남는 것은 스켈레톤과 조각뿐이다.
                foreach (var r in temp.GetComponentsInChildren<Renderer>(true))
                    DestroyImmediate(r);

                // 애니메이터도 제거한다. 시체는 클립이 아니라 물리로 움직인다.
                foreach (var a in temp.GetComponentsInChildren<Animator>(true)) DestroyImmediate(a);

                var pieces = new SlicePiece[meshes.Length];
                for (int i = 0; i < meshes.Length; i++)
                {
                    var go = new GameObject($"{PieceMeshPrefix}{i}");
                    go.transform.SetParent(temp.transform, false);

                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = meshes[i];
                    smr.sharedMaterials = materials;
                    smr.bones = bones;
                    smr.rootBone = rootBone;
                    // 컬링 기준을 조각 bounds로 맞춘다. 안 하면 특정 각도에서 조각이 통째로 사라진다.
                    smr.localBounds = meshes[i].bounds;
                    smr.updateWhenOffscreen = false;

                    pieces[i] = go.AddComponent<SlicePiece>();
                }

                var view = temp.AddComponent<CorpseView>();
                view.EditorAssign(pieces, bones, rootPiece);

                var saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
                return saved;
            }
            finally
            {
                DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// 굽기 결과를 <b>적 정의에</b> 자동 배선한다. 이미 다른 세트가 물려 있으면 확인을 받는다.
        /// 세트는 그 정의의 프리팹에서 구워졌으므로 여기 말고 갈 곳이 없다.
        /// </summary>
        private void WireToDefinition(SliceSet set)
        {
            if (enemyDefinition == null) return;

            var so = new SerializedObject(enemyDefinition);
            var prop = so.FindProperty("deathSliceSet");
            if (prop == null) return;

            var current = prop.objectReferenceValue as SliceSet;
            if (current != null && current != set)
            {
                if (!EditorUtility.DisplayDialog("적 정의 배선 교체",
                        $"'{enemyDefinition.name}'에 이미 '{current.name}'이 물려 있습니다. '{set.name}'으로 바꿀까요?",
                        "교체", "유지"))
                    return;
            }

            prop.objectReferenceValue = set;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(enemyDefinition);
        }

        private Material[] BuildMaterialSlots(Material[] sourceMaterials, Material capMat)
        {
            // 서브메쉬는 '원본 M개 + 캡 1개'이므로 머티리얼도 항상 M+1개다. null이 들어가는 경로는 없다.
            var slots = new Material[sourceMaterials.Length + 1];
            for (int i = 0; i < sourceMaterials.Length; i++)
                slots[i] = sourceMaterials[i] != null ? sourceMaterials[i] : sourceMaterials[0];
            slots[sourceMaterials.Length] = capMat;
            return slots;
        }

        // ── 에셋 저장(GUID 보존) ─────────────────────────────────────────────────

        private string SetAssetPath() => $"{outputFolder}/{setName}/{setName}.asset";

        private string EnsureFolder()
        {
            string folder = $"{outputFolder}/{setName}";
            if (AssetDatabase.IsValidFolder(folder)) return folder;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
            return folder;
        }

        /// <summary>
        /// 같은 경로에 CreateAsset을 다시 호출하면 삭제 후 재생성되어 <b>GUID가 바뀐다</b>.
        /// 기존 에셋이 있으면 Clear 후 제자리 기입해 GUID를 지킨다.
        ///
        /// <para><b>boneWeights/bindposes도 함께 옮긴다</b> — 빠뜨리면 재굽기부터 조용히 스키닝이 사라져
        /// 조각이 스켈레톤을 따라가지 않는다.</para>
        /// </summary>
        private static Mesh SaveMeshPreservingGuid(Mesh baked, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(baked, path);
                return baked;
            }

            existing.Clear();
            existing.indexFormat = baked.indexFormat;
            existing.SetVertices(new List<Vector3>(baked.vertices));
            existing.SetNormals(new List<Vector3>(baked.normals));
            existing.SetUVs(0, new List<Vector2>(baked.uv));
            existing.SetTangents(new List<Vector4>(baked.tangents));

            existing.subMeshCount = baked.subMeshCount;
            for (int s = 0; s < baked.subMeshCount; s++)
                existing.SetTriangles(baked.GetTriangles(s), s, false);

            var weights = baked.boneWeights;
            if (weights != null && weights.Length == baked.vertexCount)
            {
                existing.boneWeights = weights;
                existing.bindposes = baked.bindposes;
            }

            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);

            Object.DestroyImmediate(baked); // 임시 메쉬는 버린다
            return existing;
        }

        private static GameObject SavePiecePrefabPreservingGuid(Mesh mesh, Material[] materials, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var filter = existing.GetComponent<MeshFilter>();
                var renderer = existing.GetComponent<MeshRenderer>();
                if (filter != null) filter.sharedMesh = mesh;
                if (renderer != null) renderer.sharedMaterials = materials;
                if (existing.GetComponent<SlicePiece>() == null) existing.AddComponent<SlicePiece>();

                EditorUtility.SetDirty(existing);
                return existing;
            }

            var temp = new GameObject(Path.GetFileNameWithoutExtension(path));
            temp.AddComponent<MeshFilter>().sharedMesh = mesh;
            temp.AddComponent<MeshRenderer>().sharedMaterials = materials;
            temp.AddComponent<SlicePiece>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        private static SliceSet SaveSetPreservingGuid(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SliceSet>(path);
            if (existing != null) return existing;

            var created = CreateInstance<SliceSet>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        /// <summary>획을 지워 조각 수가 줄어든 재굽기에서 남는 에셋을 정리한다(조각은 SliceSet만 참조하므로 안전).</summary>
        private static void CleanupRemovedPieces(string folder, int keepCount)
        {
            for (int i = keepCount; i < keepCount + 32; i++)
            {
                string meshPath = $"{folder}/{PieceMeshPrefix}{i}.asset";
                string prefabPath = $"{folder}/{PieceMeshPrefix}{i}.prefab";

                bool any = false;
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) { AssetDatabase.DeleteAsset(meshPath); any = true; }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) { AssetDatabase.DeleteAsset(prefabPath); any = true; }
                if (!any) break;
            }
        }
    }
}
