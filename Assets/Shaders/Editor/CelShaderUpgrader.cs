using UnityEngine;
using UnityEditor;

public class CelShaderUpgrader : EditorWindow
{
    [MenuItem("Tools/Cel Shader/Upgrade School Katana Girl Materials")]
    public static void UpgradeMaterials()
    {
        Shader celShader = Shader.Find("Custom/CelShader");
        if (celShader == null)
        {
            Debug.LogError("[CelShaderUpgrader] Custom/CelShader not found. Ensure CelShader.shader compiled without errors.");
            return;
        }

        string folder = "Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Materials";
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folder });

        int upgraded = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;

            mat.shader = celShader;

            if (mainTex != null && mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", mainTex);

            EditorUtility.SetDirty(mat);
            upgraded++;
            Debug.Log($"[CelShaderUpgrader] Upgraded: {path}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[CelShaderUpgrader] Done. {upgraded}/{guids.Length} materials upgraded.");
    }
}
