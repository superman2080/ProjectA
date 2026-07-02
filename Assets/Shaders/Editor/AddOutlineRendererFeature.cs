using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;

public class AddOutlineRendererFeature : EditorWindow
{
    [MenuItem("Tools/Cel Shader/Add Outline Renderer Feature")]
    public static void AddFeature()
    {
        // PC Renderer 로드
        string rendererPath = "Assets/Settings/PC_Renderer.asset";
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"PC_Renderer.asset not found at {rendererPath}");
            return;
        }

        // 이미 추가된 CharacterOutline Feature가 있으면 스킵
        foreach (var feature in rendererData.rendererFeatures)
        {
            if (feature != null && feature.name == "CharacterOutline")
            {
                Debug.Log("CharacterOutline feature already exists.");
                return;
            }
        }

        // RenderObjects Feature 생성
        var outlineFeature = ScriptableObject.CreateInstance<RenderObjects>();
        outlineFeature.name = "CharacterOutline";

        // Feature 설정
        outlineFeature.settings.Event = RenderPassEvent.AfterRenderingOpaques;
        outlineFeature.settings.filterSettings.LayerMask = 1 << 8; // Layer 8 = Character

        // 아웃라인 머티리얼 할당
        string matPath = "Assets/Shaders/Materials/CelOutlineMaterial.mat";
        var outlineMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (outlineMat == null)
        {
            Debug.LogError($"CelOutlineMaterial.mat not found at {matPath}");
            return;
        }
        outlineFeature.settings.overrideMaterial = outlineMat;

        // Renderer에 Feature 추가
        rendererData.rendererFeatures.Add(outlineFeature);
        AssetDatabase.AddObjectToAsset(outlineFeature, rendererData);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();

        Debug.Log("CharacterOutline RenderObjects feature added to PC_Renderer.");
    }
}
