using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StoryProps.EditorTools
{
    /// <summary>
    /// Assets/06. Models/Props 의 FBX 안에 들어 있는 오브젝트들을
    /// 개별 프리팹(Assets/03. Prefabs/StoryProps/)으로 뽑아낸다.
    ///
    /// FBX 하나에 여러 프롭이 들어 있는 경우가 많아(예: Prop_CairnSet 5종)
    /// 배치 단계에서 바로 쓰려면 낱개 프리팹이 있어야 한다.
    /// 이미 있는 프리팹은 건드리지 않는다 — 씬 배치가 끊기면 안 되기 때문.
    /// </summary>
    public static class StoryPropPrefabExtractor
    {
        const string ModelFolder = "Assets/06. Models/Props";
        const string PrefabFolder = "Assets/03. Prefabs/StoryProps";

        [MenuItem("Tools/Story Props/Extract Prefabs", priority = 310)]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(ModelFolder))
            {
                Debug.LogError($"[StoryProps] 모델 폴더가 없다: {ModelFolder}");
                return;
            }

            EnsureFolder(PrefabFolder);

            var guids = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder });
            int made = 0, skipped = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                if (instance == null) continue;

                try
                {
                    foreach (var part in RootParts(instance))
                    {
                        string prefabPath = $"{PrefabFolder}/{part.name}.prefab";
                        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                        {
                            skipped++;
                            continue;
                        }

                        var copy = Object.Instantiate(part);
                        copy.name = part.name;

                        // ⚠ 위치만 원점으로 되돌린다(한 FBX 에 여러 프롭을 나란히 담아 내보냈으므로).
                        //   회전·스케일은 손대지 않는다 — FBX 임포트가 축 보정을 걸어 놨을 수 있고,
                        //   그걸 identity 로 밀면 모델이 통째로 넘어간다.
                        copy.transform.localPosition = Vector3.zero;

                        PrefabUtility.SaveAsPrefabAsset(copy, prefabPath);
                        Object.DestroyImmediate(copy);
                        made++;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[StoryProps] 프리팹 {made}개 생성, {skipped}개는 이미 있어 건너뜀. 위치: {PrefabFolder}");
        }

        /// <summary>
        /// 모델 루트가 프롭 하나면 그 자신을, 여러 개를 담고 있으면 자식들을 돌려준다.
        /// Blender 에서 여러 오브젝트를 한 FBX 로 내보내면 후자가 된다.
        /// </summary>
        static IEnumerable<GameObject> RootParts(GameObject instance)
        {
            var children = new List<GameObject>();
            foreach (Transform child in instance.transform) children.Add(child.gameObject);

            if (children.Count == 0)
            {
                yield return instance;
                yield break;
            }

            // 자식이 하나뿐이고 이름이 루트와 같으면 그것 하나만 프롭이다
            if (children.Count == 1 && children[0].name == instance.name)
            {
                yield return children[0];
                yield break;
            }

            foreach (var child in children) yield return child;
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
