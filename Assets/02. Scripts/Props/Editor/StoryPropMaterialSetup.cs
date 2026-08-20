using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StoryProps.EditorTools
{
    /// <summary>
    /// Assets/06. Models/Props 에 들어온 배경 프롭 FBX들의
    /// 임포트 설정과 URP 머티리얼을 한 번에 정리한다.
    ///
    /// 프롭 FBX 는 Blender 에서 베이스 컬러만 들고 나오므로(§2-0),
    /// 여기서 URP/Lit 공용 머티리얼을 만들고 각 FBX 슬롯에 리맵해 준다.
    /// 두 번 이상 실행해도 결과가 같다(기존 머티리얼은 값만 갱신).
    /// </summary>
    public static class StoryPropMaterialSetup
    {
        const string ModelFolder = "Assets/06. Models/Props";
        const string MaterialFolder = "Assets/07. Materials/Props";
        const string LitShader = "Universal Render Pipeline/Lit";

        /// <summary>표면 종류 — 금속성·거칠기를 이 값으로만 가른다.</summary>
        enum Surface { Rough, Semi, Metal, Emissive }

        readonly struct Entry
        {
            public readonly string Name;
            public readonly Color Color;      // Blender Base Color (linear) 그대로
            public readonly Surface Surface;

            public Entry(string name, float r, float g, float b, Surface surface)
            {
                Name = name;
                Color = new Color(r, g, b, 1f);
                Surface = surface;
            }
        }

        // Blender 팔레트와 1:1. 값이 바뀌면 StoryProps.blend 쪽도 같이 바꾼다.
        static readonly Entry[] Palette =
        {
            new Entry("Stone",        0.55f, 0.54f, 0.51f, Surface.Rough),
            new Entry("StoneDark",    0.40f, 0.39f, 0.37f, Surface.Rough),
            new Entry("Concrete",     0.62f, 0.62f, 0.60f, Surface.Rough),
            new Entry("WoodLight",    0.70f, 0.55f, 0.36f, Surface.Semi),
            new Entry("WoodDark",     0.30f, 0.21f, 0.14f, Surface.Semi),
            new Entry("Cloth",        0.47f, 0.25f, 0.21f, Surface.Rough),
            new Entry("Leather",      0.22f, 0.18f, 0.16f, Surface.Semi),
            new Entry("CanvasNavy",   0.20f, 0.23f, 0.32f, Surface.Rough),
            new Entry("TapeA",        0.38f, 0.45f, 0.42f, Surface.Rough),
            new Entry("Bamboo",       0.85f, 0.76f, 0.50f, Surface.Semi),
            new Entry("Metal",        0.72f, 0.73f, 0.75f, Surface.Metal),
            new Entry("MetalDark",    0.35f, 0.36f, 0.38f, Surface.Metal),
            new Entry("Paper",        0.88f, 0.86f, 0.80f, Surface.Rough),
            new Entry("Ink",          0.16f, 0.15f, 0.14f, Surface.Rough),
            new Entry("PlasticWhite", 0.86f, 0.87f, 0.86f, Surface.Semi),
            new Entry("Fluid",        0.80f, 0.84f, 0.82f, Surface.Semi),
            new Entry("BookCover",    0.34f, 0.42f, 0.40f, Surface.Semi),
            new Entry("BookEdge",     0.86f, 0.84f, 0.78f, Surface.Rough),
            new Entry("TagWhite",     0.90f, 0.90f, 0.88f, Surface.Semi),
            new Entry("TagGreen",     0.30f, 0.46f, 0.34f, Surface.Semi),
            new Entry("Wrap",         0.80f, 0.82f, 0.80f, Surface.Semi),
            new Entry("CordWhite",    0.84f, 0.82f, 0.74f, Surface.Rough),
            // 자판기 — "간판은 켜져 있고 자판기도 돌아간다"(§Narrative 3-1)의 발광 지점
            new Entry("Glow",         0.95f, 0.93f, 0.82f, Surface.Emissive),
            new Entry("GlowPanel",    0.86f, 0.90f, 0.94f, Surface.Emissive),
        };

        const float EmissionIntensity = 1.6f;

        /// <summary>
        /// 아직 한 번도 정리한 적이 없으면(= 팔레트 머티리얼이 없으면) 에디터가 켜질 때 한 번만 돌린다.
        /// ⚠ 이미 있으면 아무것도 하지 않는다 — 사용자가 손으로 고친 값을 덮어쓰면 안 되기 때문.
        /// 값을 다시 밀어 넣고 싶으면 Tools/Story Props/Setup Materials 를 직접 실행한다.
        /// </summary>
        [InitializeOnLoadMethod]
        static void AutoRunOnce()
        {
            EditorApplication.delayCall += () =>
            {
                if (!Directory.Exists(ModelFolder)) return;

                foreach (var entry in Palette)
                {
                    if (AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{entry.Name}.mat") == null)
                    {
                        Debug.Log("[StoryProps] 프롭 머티리얼이 없어 최초 1회 자동 정리를 실행한다.");
                        Run();
                        return;
                    }
                }
            };
        }

        [MenuItem("Tools/Story Props/Setup Materials", priority = 300)]
        public static void Run()
        {
            var shader = Shader.Find(LitShader);
            if (shader == null)
            {
                Debug.LogError($"[StoryProps] 셰이더를 못 찾았다: {LitShader}. URP 가 설치돼 있는지 확인.");
                return;
            }

            if (!Directory.Exists(ModelFolder))
            {
                Debug.LogError($"[StoryProps] 모델 폴더가 없다: {ModelFolder}");
                return;
            }

            EnsureFolder(MaterialFolder);

            var materials = new Dictionary<string, Material>(Palette.Length);
            int created = 0, updated = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var entry in Palette)
                {
                    string path = $"{MaterialFolder}/{entry.Name}.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)
                    {
                        mat = new Material(shader) { name = entry.Name };
                        AssetDatabase.CreateAsset(mat, path);
                        created++;
                    }
                    else
                    {
                        if (mat.shader != shader) mat.shader = shader;
                        updated++;
                    }

                    Apply(mat, entry);
                    EditorUtility.SetDirty(mat);
                    materials[entry.Name] = mat;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();

            int models = RemapModels(materials);

            AssetDatabase.Refresh();
            Debug.Log($"[StoryProps] 머티리얼 {created} 생성 / {updated} 갱신, FBX {models}개 리맵 완료. " +
                      $"머티리얼 위치: {MaterialFolder}");
        }

        static void Apply(Material mat, Entry entry)
        {
            SetColorIfPresent(mat, "_BaseColor", entry.Color);
            SetColorIfPresent(mat, "_Color", entry.Color);

            float metallic, smoothness;
            switch (entry.Surface)
            {
                case Surface.Metal: metallic = 0.85f; smoothness = 0.55f; break;
                case Surface.Semi:  metallic = 0f;    smoothness = 0.25f; break;
                case Surface.Emissive: metallic = 0f; smoothness = 0.35f; break;
                default:            metallic = 0f;    smoothness = 0.08f; break;
            }

            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            bool emissive = entry.Surface == Surface.Emissive;
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                SetColorIfPresent(mat, "_EmissionColor", entry.Color * EmissionIntensity);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                SetColorIfPresent(mat, "_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            if (mat.HasProperty("_EmissionEnabled")) mat.SetFloat("_EmissionEnabled", emissive ? 1f : 0f);
        }

        static void SetColorIfPresent(Material mat, string prop, Color value)
        {
            if (mat.HasProperty(prop)) mat.SetColor(prop, value);
        }

        static int RemapModels(Dictionary<string, Material> materials)
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder });
            int count = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                ConfigureImporter(importer);

                foreach (var pair in materials)
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                    importer.AddRemap(id, pair.Value);
                }

                importer.SaveAndReimport();
                count++;
            }

            return count;
        }

        static void ConfigureImporter(ModelImporter importer)
        {
            // 1 blender unit = 1 m 로 만들었으므로 파일 스케일을 그대로 쓴다(§2-0)
            importer.useFileScale = true;
            importer.globalScale = 1f;

            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;

            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.addCollider = false;      // 배경층이라 콜라이더가 필요 없다(§2-1)
            importer.weldVertices = true;
            importer.generateSecondaryUV = true; // 베이크 라이팅 대비

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
