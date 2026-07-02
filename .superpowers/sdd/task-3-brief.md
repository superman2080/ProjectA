# Task 3 Brief: Character 레이어 + RenderObjects Feature 설정

## 목표
PC_Renderer.asset에 RenderObjects Renderer Feature를 추가해 Character 레이어 오브젝트에 아웃라인을 렌더링한다.

## 글로벌 제약
- 대상 렌더러: `Assets/Settings/PC_Renderer.asset`
- 아웃라인 머티리얼: `Assets/Shaders/Materials/CelOutlineMaterial.mat`
- 레이어 이름: `Character` (Layer 8)

## Task 1, 2 완료 상태
- `Assets/Shaders/CelShader.shader` 완료
- `Assets/Shaders/CelOutline.shader` 완료
- `Assets/Shaders/Materials/CelOutlineMaterial.mat` 완료 (GUID: `b2856ec1a64142fab87173e4dc888946` — 머티리얼의 `m_Shader` guid)
- `Assets/Shaders/CelOutline.shader.meta`의 guid: `b2856ec1a64142fab87173e4dc888946`

## 해야 할 작업

### 1. TagManager.asset에 "Character" 레이어 추가
`ProjectSettings/TagManager.asset`을 읽고, Layer 8 슬롯에 "Character"를 추가한다.

현재 파일에서 layers 섹션을 찾아 Layer 8이 비어있으면 채운다.

### 2. PC_Renderer.asset에 RenderObjects Feature 추가

`Assets/Settings/PC_Renderer.asset`를 읽어 현재 `m_RendererFeatures` 목록을 확인한다.

RenderObjects Feature를 추가하는 것은 복잡한 Unity 내부 구조가 필요하므로, **이 Task는 MCP 도구가 없는 환경에서 직접 파일 수정으로 처리하기 어렵다.**

대신 다음을 수행한다:

**대안 접근법: ScriptableObject 생성 스크립트**

Unity Editor에서 실행할 수 있는 Editor 스크립트를 생성해 Renderer Feature를 프로그래밍적으로 추가한다.

`Assets/Shaders/Editor/AddOutlineRendererFeature.cs` 생성:

```csharp
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
        if (outlineMat != null)
            outlineFeature.settings.overrideMaterial = outlineMat;

        // Renderer에 Feature 추가
        rendererData.rendererFeatures.Add(outlineFeature);
        AssetDatabase.AddObjectToAsset(outlineFeature, rendererData);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();

        Debug.Log("CharacterOutline RenderObjects feature added to PC_Renderer.");
    }
}
```

**참고:** `RenderObjects` 타입은 `UnityEngine.Rendering.Universal` 네임스페이스에 있다. URP 패키지에 포함되어 있으므로 추가 패키지 설치 불필요.

### 3. School Katana Girl 레이어 변경 안내 스크립트

`Assets/Shaders/Editor/SetCharacterLayer.cs` 생성:

```csharp
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
```

## TagManager.asset 수정 방법

`ProjectSettings/TagManager.asset`을 Read하고, layers 배열에서 인덱스 8 위치(0~7은 Unity 예약)에 "Character"를 추가한다. 파일 구조:

```yaml
layers:
- 
- 
- 
- 
- 
- 
- 
- 
- Character   ← 이 줄 추가 (인덱스 8)
```

## 완료 기준
- `ProjectSettings/TagManager.asset`에 Layer 8 = "Character" 추가됨
- `Assets/Shaders/Editor/AddOutlineRendererFeature.cs` 생성됨
- `Assets/Shaders/Editor/SetCharacterLayer.cs` 생성됨

## 보고서
`.superpowers/sdd/task-3-report.md` 작성 후 `STATUS: DONE`
