# Task 4 Brief: CelShaderUpgrader.cs 생성 및 머티리얼 일괄 교체

## 목표
`Assets/Shaders/Editor/CelShaderUpgrader.cs` Editor 스크립트를 생성한다.
이 스크립트는 Unity 메뉴에서 실행되어 School Katana Girl의 머티리얼 약 15개를 `Custom/CelShader`로 일괄 교체하고, `_MainTex`를 `_BaseMap`에 복사한다.

## 글로벌 제약
- 저장 경로: `Assets/Shaders/Editor/CelShaderUpgrader.cs`
- 대상 셰이더 이름: `Custom/CelShader` (Shader.Find에서 이 정확한 이름 사용)
- 대상 머티리얼 경로: `Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Materials/`
- `_MainTex` → `_BaseMap` 텍스처 복사 필수

## 이전 Task 완료 상태
- `Assets/Shaders/CelShader.shader` — `Custom/CelShader` 셰이더 (Task 1)
- `Assets/Shaders/Editor/` 폴더 존재 (Task 3)

## 대상 머티리얼 (총 약 15개)
```
Materials/Cloth/Katana_Cloth 1.mat
Materials/Cloth/Katana_Cloth 2.mat
Materials/Cloth/Katana_Cloth 3.mat
Materials/Cloth/Katana_Cloth 4.mat
Materials/Cloth/Katana_Cloth 5.mat
Materials/Cloth/Katana_Glass.mat
Materials/Cloth/Katana_Glass_Alpha.mat
Materials/Hair/Katana_Hair 1.mat
Materials/Hair/Katana_Hair 2.mat
Materials/Hair/Katana_Hair 3.mat
Materials/Hair/Katana_Hair 4.mat
Materials/Hair/Katana_Hair 5.mat
Materials/Body/Body.mat
Materials/Body/Face.mat
Materials/Body/Eye.mat
Materials/Weapon/Weapon_Katana.mat
Materials/Weapon/Weapon_Katana_02.mat
```

## 생성할 파일

`Assets/Shaders/Editor/CelShaderUpgrader.cs`:

```csharp
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
```

## 완료 기준
- `Assets/Shaders/Editor/CelShaderUpgrader.cs` 존재
- MenuItem 경로: `Tools/Cel Shader/Upgrade School Katana Girl Materials`
- `Shader.Find("Custom/CelShader")` 실패 시 에러 후 return
- `_MainTex` → `_BaseMap` 텍스처 복사 로직 포함
- `AssetDatabase.SaveAssets()` 호출로 변경사항 저장

## 보고서
`.superpowers/sdd/task-4-report.md` 작성 후 `STATUS: DONE`
