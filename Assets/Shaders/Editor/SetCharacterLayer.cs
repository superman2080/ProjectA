using UnityEngine;
using UnityEditor;

public class SetCharacterLayer : EditorWindow
{
    [MenuItem("Tools/Cel Shader/Set Katana Girl to Character Layer")]
    public static void SetLayer()
    {
        // Layer 8 = Character (TagManager에 추가 필요)
        int characterLayer = 8;

        // 씬의 Katana Girl 루트 오브젝트 찾기
        string[] searchNames = { "School_Katana", "Katana", "School_Katana_Girl" };
        int count = 0;

        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            foreach (string name in searchNames)
            {
                if (go.name.Contains(name) && go.transform.parent == null)
                {
                    SetLayerRecursive(go, characterLayer);
                    count++;
                    Debug.Log($"Set layer for: {go.name}");
                    break;
                }
            }
        }

        if (count == 0)
            Debug.LogWarning("No Katana Girl objects found in scene. Open the scene with the character first.");
        else
            Debug.Log($"Done. {count} root object(s) set to Character layer.");
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
