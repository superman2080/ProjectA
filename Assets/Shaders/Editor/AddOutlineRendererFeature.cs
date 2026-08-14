using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP <c>RenderObjects</c> 아웃라인 피처를 <c>PC_Renderer</c>에 배선하는 에디터 도구.
///
/// <para><b>피처 = 렌더러에 끼워 넣는 추가 패스</b>다. "언제(<c>Event</c>) · 무엇을(<c>LayerMask</c>) ·
/// 어떻게(<c>overrideMaterial</c>)" 세 값이 전부라, 적 프리팹·머티리얼·셰이더를 하나도 안 건드리고
/// <b>레이어만 바꿔서</b> 아웃라인을 켜고 끌 수 있다(<c>EnemyView.SetHighlight</c>).</para>
/// </summary>
public class AddOutlineRendererFeature : EditorWindow
{
    private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    private const string BaseOutlineMaterialPath = "Assets/Shaders/Materials/CelOutlineMaterial.mat";
    private const string AmbushMaterialPath = "Assets/Shaders/Materials/AmbushOutlineMaterial.mat";

    private const string CharacterFeatureName = "CharacterOutline";
    private const string AmbushFeatureName = "AmbushOutline";

    /// <summary>
    /// 기습 강조용 아웃라인 피처를 배선한다. <b>기존 <c>CharacterOutline</c>은 끈다</b> —
    /// 그 피처의 <c>LayerMask</c>는 레이어 8인데 <c>TagManager</c>의 레이어 8은 <c>SlicePiece</c>다
    /// (스크립트 주석의 "Layer 8 = Character"는 사실이 아니었다). 즉 지금 화면의 아웃라인은
    /// 캐릭터가 아니라 <b>베인 조각</b>에 걸려 있고, 켜 둔 채로 기습 아웃라인을 얹으면
    /// 원인을 둘 들고 디버깅하게 된다.
    /// </summary>
    [MenuItem("Tools/Cel Shader/Setup Ambush Outline")]
    public static void SetupAmbushOutline()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"PC_Renderer.asset not found at {RendererPath}");
            return;
        }

        int ambushLayer = LayerMask.NameToLayer("AmbushOutline");
        if (ambushLayer < 0)
        {
            Debug.LogError("Layer 'AmbushOutline' not found. Add it in Project Settings > Tags and Layers first.");
            return;
        }

        DisableCharacterOutline(rendererData);

        var material = LoadOrCreateAmbushMaterial();
        if (material == null) return;

        var existing = FindFeature(rendererData, AmbushFeatureName) as RenderObjects;
        var feature = existing != null ? existing : ScriptableObject.CreateInstance<RenderObjects>();
        feature.name = AmbushFeatureName;

        feature.settings.Event = RenderPassEvent.AfterRenderingOpaques;
        feature.settings.filterSettings.LayerMask = 1 << ambushLayer;
        feature.settings.overrideMaterial = material;
        feature.settings.overrideMaterialPassIndex = 0;

        // ⚠ 깊이 상태를 건드리지 않는다 — 셰이더의 ZTest Less가 이 아웃라인의 전제다.
        // CelOutline은 메쉬를 법선 방향으로 부풀린 뒤 <b>뒷면만</b>(Cull Front) 그려서 테두리를 만든다.
        // 즉 셸은 원래 앞면보다 뒤에 있고, 깊이 테스트가 그 겹치는 부분을 잘라내야 가장자리만 남는다.
        // CompareFunction.Always로 열면 셸이 앞면 위에 통째로 그려져 <b>실루엣 전체가 칠해진다</b>
        // (벽 너머로 비치게 하려면 스텐실로 안쪽을 파내야 하고, 그건 이 기능이 살 값이 아니다).
        feature.settings.overrideDepthState = false;

        if (existing == null)
        {
            rendererData.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, rendererData);
        }

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AmbushOutline] feature ready on PC_Renderer (layer {ambushLayer}, material {AmbushMaterialPath}).");
    }

    /// <summary>
    /// 기존 <c>CharacterOutline</c>을 비활성한다. 삭제가 아니라 <b>끄기</b>다 —
    /// 원래 의도(캐릭터 셀 아웃라인)를 나중에 되살릴 수 있고, 그때는 레이어만 고치면 된다.
    /// </summary>
    private static void DisableCharacterOutline(UniversalRendererData rendererData)
    {
        var feature = FindFeature(rendererData, CharacterFeatureName);
        if (feature == null || !feature.isActive) return;

        feature.SetActive(false);
        EditorUtility.SetDirty(feature);

        Debug.Log("[AmbushOutline] disabled CharacterOutline — its LayerMask (8) is SlicePiece, not Character.");
    }

    private static ScriptableRendererFeature FindFeature(UniversalRendererData data, string name)
    {
        foreach (var feature in data.rendererFeatures)
        {
            if (feature != null && feature.name == name) return feature;
        }

        return null;
    }

    /// <summary>
    /// 기습용 아웃라인 머티리얼. <b>기존 것을 복제해 색·두께만 바꾼다</b> —
    /// 상시 아웃라인과 기습 경고는 <b>보이는 이유가 달라</b> 값을 공유하면 한쪽을 튜닝할 수 없다.
    /// 이후 튜닝은 전부 이 머티리얼 인스펙터에서 한다(<c>_OutlineColor</c> · <c>_OutlineWidth</c>).
    /// </summary>
    private static Material LoadOrCreateAmbushMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(AmbushMaterialPath);
        if (existing != null) return existing;

        var basis = AssetDatabase.LoadAssetAtPath<Material>(BaseOutlineMaterialPath);
        if (basis == null)
        {
            Debug.LogError($"CelOutlineMaterial.mat not found at {BaseOutlineMaterialPath}");
            return null;
        }

        var material = new Material(basis);
        material.SetColor("_OutlineColor", new Color(1f, 0.35f, 0.25f));
        material.SetFloat("_OutlineWidth", 0.02f);

        AssetDatabase.CreateAsset(material, AmbushMaterialPath);

        return material;
    }
}
