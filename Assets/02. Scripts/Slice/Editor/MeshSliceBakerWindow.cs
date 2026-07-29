using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SliceSpace.EditorTools
{
    /// <summary>
    /// 메쉬를 평면으로 미리 절단해 조각 프리팹과 <see cref="SliceSet"/>을 굽는 에디터 툴.
    ///
    /// <para>절단 평면은 <b>창 안의 프리뷰에서 직선 획을 그어</b> 만든다(획 하나 = 평면 한 장).
    /// <see cref="PreviewRenderUtility"/>로 창 내부에 렌더하므로 사용자 씬을 건드리지 않고,
    /// 씬 뷰의 선택/기즈모 조작과도 충돌하지 않는다(`AnimationClipTrimmerWindow` 선례).</para>
    ///
    /// <para>여러 획이 교차하면 가로+세로 = ┼ → 4조각으로 갈라진다. 획의 <b>길이는 무시</b>되고
    /// 각도와 위치만 쓰인다(무한 평면).</para>
    ///
    /// <para>같은 이름으로 다시 구우면 에셋을 <b>제자리 수정</b>해 GUID를 유지한다. 카탈로그가 없어
    /// 패턴 → SliceSet 직접 참조가 유일한 배선이므로, GUID를 잃으면 배선이 통째로 끊긴다.</para>
    /// </summary>
    public class MeshSliceBakerWindow : EditorWindow
    {
        private const string PieceMeshPrefix = "Piece_";
        private const float PreviewHeight = 320f;

        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private string setName = "";
        [SerializeField] private SliceShape shape = SliceShape.Horizontal;
        [SerializeField] private Material capMaterial;
        [SerializeField] private string outputFolder = "Assets/04. Datas/Slice";

        [SerializeField] private float capUvScale = 1f;
        [SerializeField] private float weldEpsilon = 1e-4f;
        [SerializeField] private float minPieceVolumeRatio = 0.001f;

        [SerializeField] private List<SlicePlane> planes = new List<SlicePlane>();
        [SerializeField] private int selectedPlane = -1;

        // 프리뷰 (AnimationClipTrimmerWindow와 같은 구성)
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

        // 분해 프리뷰 결과(굽기 전 확인용). 창이 닫히거나 다시 계산할 때 파기한다.
        private List<SlicedPiece> previewPieces;

        [MenuItem("Tools/Mesh Slice Baker")]
        public static void Open() => GetWindow<MeshSliceBakerWindow>("Mesh Slice Baker");

        void OnDisable()
        {
            ClearPreviewPieces();
            previewUtil?.Cleanup();
            previewUtil = null;
        }

        // ── 창 ──────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawPreview();

            scroll = EditorGUILayout.BeginScrollView(scroll);

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

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("절단", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            shape = (SliceShape)EditorGUILayout.EnumPopup("Shape (라벨/프리셋)", shape);
            if (EditorGUI.EndChangeCheck() && shape != SliceShape.Custom)
            {
                if (EditorUtility.DisplayDialog("프리셋 적용",
                        $"'{shape}' 프리셋 평면으로 획 목록을 대체할까요? 지금 그은 획은 사라집니다.", "대체", "유지"))
                    ApplyPreset();
            }

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

        /// <summary>프리셋과 어긋난 채 'Cross' 같은 라벨로 저장되는 혼동을 막는다.</summary>
        private void MarkCustom()
        {
            if (shape != SliceShape.Custom) shape = SliceShape.Custom;
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
                EditorGUI.LabelField(previewRect, "표적 프리팹을 지정하면 여기에 표시됩니다.", EditorStyles.centeredGreyMiniLabel);
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

            // 분해 프리뷰가 있으면 조각을, 없으면 원본을 그린다.
            if (previewPieces != null && previewPieces.Count > 0)
            {
                Material capMat = capMaterial != null ? capMaterial : (materials.Length > 0 ? materials[0] : null);
                Vector3 center = mesh.bounds.center;

                foreach (var piece in previewPieces)
                {
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

        /// <summary>평면 단면과 그리는 중인 획을 창 좌표로 투영해 겹쳐 그린다.</summary>
        private void DrawPreviewOverlay(Mesh mesh)
        {
            if (Event.current.type != EventType.Repaint) return;

            Handles.BeginGUI();

            Bounds b = mesh.bounds;
            float extent = b.extents.magnitude;

            for (int i = 0; i < planes.Count; i++)
            {
                var p = planes[i];
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
                bool visible = true;
                for (int c = 0; c < 4; c++)
                {
                    if (!WorldToGui(corners[c], out Vector2 g)) { visible = false; break; }
                    pts[c] = g;
                }
                if (!visible) continue;
                pts[4] = pts[0];

                Handles.color = i == selectedPlane ? Color.yellow : new Color(0.3f, 0.85f, 1f, 0.9f);
                Handles.DrawAAPolyLine(i == selectedPlane ? 3f : 2f, pts);
            }

            if (dragging)
            {
                Handles.color = Color.yellow;
                Handles.DrawAAPolyLine(4f, (Vector3)dragStart, (Vector3)dragCurrent);
            }

            Handles.EndGUI();
        }

        private void HandlePreviewInput(Rect rect, Mesh mesh)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition) && !dragging) return;

            switch (e.type)
            {
                // 좌드래그 = 획 긋기
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

                // 우/중간 드래그 = 회전
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

        /// <summary>
        /// 창 좌표 획 → 무한 평면. 프리뷰는 메쉬를 원점(identity)에 그리므로 프리뷰 월드가 곧 메쉬 로컬이다
        /// — 씬 방식과 달리 트랜스폼 변환이 필요 없다.
        /// </summary>
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

        /// <summary>창 좌표 → 프리뷰 카메라 광선. 카메라는 Repaint 중에만 유효하므로 캐시한 포즈로 직접 계산한다.</summary>
        private Ray GuiPointToRay(Vector2 guiPoint)
        {
            float tanHalf = Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad);
            float aspect = previewRect.width / Mathf.Max(previewRect.height, 1f);

            float nx = (guiPoint.x - previewRect.x) / previewRect.width * 2f - 1f;
            float ny = 1f - (guiPoint.y - previewRect.y) / previewRect.height * 2f;

            Vector3 dirCam = new Vector3(nx * tanHalf * aspect, ny * tanHalf, 1f);
            return new Ray(camPos, (camRot * dirCam).normalized);
        }

        /// <summary>월드(=메쉬 로컬) → 창 좌표. 카메라 뒤면 false.</summary>
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

            // 기존 세트를 열면 프리셋이 아니라 저장된 평면을 로드한다(재굽기 재현).
            if (found.BakedPlanes != null && found.BakedPlanes.Length > 0)
            {
                planes.Clear();
                planes.AddRange(found.BakedPlanes);
            }
            shape = found.Shape;
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

        // ── 검증 / Preview / Bake ───────────────────────────────────────────────

        private Mesh GetSourceMesh()
        {
            if (sourcePrefab == null) return null;
            var filter = sourcePrefab.GetComponentInChildren<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private Material[] GetSourceMaterials()
        {
            var renderer = sourcePrefab != null ? sourcePrefab.GetComponentInChildren<MeshRenderer>() : null;
            return renderer != null ? renderer.sharedMaterials : new Material[0];
        }

        private string GetBlockingReason()
        {
            if (sourcePrefab == null) return "표적 프리팹을 지정하세요.";
            if (string.IsNullOrEmpty(setName)) return "세트 이름을 입력하세요.";

            var mesh = GetSourceMesh();
            if (mesh == null) return "프리팹에서 MeshFilter/Mesh를 찾을 수 없습니다.";

            string invalid = MeshSliceBaker.Validate(mesh);
            if (invalid != null) return invalid;

            var mats = GetSourceMaterials();
            if (mats.Length == 0 || mats[0] == null)
                return "원본 머티리얼이 비어 있거나 [0]이 null입니다. 단면 폴백 대상이 없어 구울 수 없습니다.";

            if (planes.Count == 0) return "절단 평면이 없습니다. 프리뷰에서 획을 긋거나 프리셋을 적용하세요.";
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
            ClearPreviewPieces();

            var mesh = GetSourceMesh();
            previewPieces = MeshSliceBaker.Slice(mesh, planes, BuildOptions(), out int discarded);

            var mats = GetSourceMaterials();
            int capIndex = mesh.subMeshCount;
            Material capMat = capMaterial != null ? capMaterial : mats[0];
            string capSource = capMaterial != null ? "지정" : $"폴백(원본[0] '{mats[0].name}')";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"조각 {previewPieces.Count}개" + (discarded > 0 ? $" (퇴화 {discarded}개 폐기)" : ""));
            sb.AppendLine($"머티리얼 슬롯: {mats.Length + 1}개 — 캡은 인덱스 {capIndex}, '{capMat.name}' [{capSource}]");

            for (int i = 0; i < previewPieces.Count; i++)
            {
                var p = previewPieces[i];
                sb.AppendLine($"  Piece_{i}: 정점 {p.mesh.vertexCount}, 삼각형 {p.mesh.triangles.Length / 3}, 캡 루프 {p.capLoopCount}");
            }

            previewText = sb.ToString();
            explode = 0.35f; // 갈라진 모습이 바로 보이게
            Repaint();
        }

        private void Bake()
        {
            var mesh = GetSourceMesh();
            var options = BuildOptions();
            var pieces = MeshSliceBaker.Slice(mesh, planes, options, out int discarded);

            if (pieces.Count == 0)
            {
                EditorUtility.DisplayDialog("Bake 실패", "조각이 하나도 나오지 않았습니다. 평면 위치를 확인하세요.", "확인");
                return;
            }

            var sourceMaterials = GetSourceMaterials();
            Material capMat = capMaterial != null ? capMaterial : sourceMaterials[0];

            if (capMaterial == null)
            {
                if (sourceMaterials.Length > 1)
                    Debug.LogWarning($"[MeshSliceBaker] 원본 머티리얼이 {sourceMaterials.Length}개입니다 — 캡에 [0] '{sourceMaterials[0].name}'을 적용했습니다. 의도와 다르면 단면 머티리얼을 지정하세요.");
                else
                    Debug.Log($"[MeshSliceBaker] 단면 머티리얼 미지정 — 원본 머티리얼 '{sourceMaterials[0].name}'을 캡에 적용했습니다.");
            }

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

            Debug.Log($"[MeshSliceBaker] '{setName}' 굽기 완료 — 조각 {pieces.Count}개" +
                      (discarded > 0 ? $", 퇴화 {discarded}개 폐기" : "") +
                      $". 이 세트를 쓰려면 패턴 에셋의 sliceTargets에 배선하세요.", set);

            previewText = $"Bake 완료: 조각 {pieces.Count}개 → {folder}";
            Selection.activeObject = set;
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
