# 카툰 렌더링 셰이더 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** URP용 커스텀 Cel 셰이더(3단계 셰이딩 + 림라이트 + 버텍스 익스트루전 아웃라인)를 제작하고 School Katana Girl 캐릭터 머티리얼 ~15개에 일괄 적용한다.

**Architecture:** `CelShader.shader`(HLSL)가 3단계 셰이딩과 Fresnel 림라이트를 처리하고, `CelOutline.shader`(Cull Front 버텍스 익스트루전)가 아웃라인을 처리한다. URP RenderObjects Renderer Feature로 아웃라인 패스를 캐릭터 레이어에만 별도 렌더링하며, Editor 스크립트 `CelShaderUpgrader.cs`로 기존 깨진 UTS2 머티리얼을 CelShader로 일괄 교체한다.

**Tech Stack:** Unity URP 14+, HLSL ShaderLab, C# Editor Scripting, MCP for Unity

## Global Constraints

- 렌더 파이프라인: URP (`Assets/Settings/PC_Renderer.asset`)
- 셰이더 저장 위치: `Assets/Shaders/`
- 대상 머티리얼 경로: `Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Materials/`
- 아웃라인 레이어 이름: `Character` (Layer 8)
- 아웃라인 기본값: Width=0.005, Color=(0.1, 0.1, 0.1, 1)

---

### Task 1: CelShader.shader 생성

**Files:**
- Create: `Assets/Shaders/CelShader.shader`

**Interfaces:**
- Produces: `Custom/CelShader` 셰이더. Properties: `_BaseMap (Texture2D)`, `_BaseColor (Color)`, `_Step1Threshold (Float)`, `_Step2Threshold (Float)`, `_ShadowColor1 (Color)`, `_ShadowColor2 (Color)`, `_RimColor (Color)`, `_RimPower (Float)`, `_RimIntensity (Float)`

- [ ] **Step 1: CelShader.shader 작성**

MCP `create_script` 도구로 `Assets/Shaders/CelShader.shader` 생성:

```hlsl
Shader "Custom/CelShader"
{
    Properties
    {
        _BaseMap        ("Base Map",        2D)            = "white" {}
        _BaseColor      ("Base Color",      Color)         = (1,1,1,1)
        _Step1Threshold ("Step 1 Threshold", Range(0,1))   = 0.5
        _Step2Threshold ("Step 2 Threshold", Range(0,1))   = 0.2
        _ShadowColor1   ("Shadow Color 1",  Color)         = (0.75,0.75,0.8,1)
        _ShadowColor2   ("Shadow Color 2",  Color)         = (0.55,0.55,0.65,1)
        _RimColor       ("Rim Color",       Color)         = (1,1,1,1)
        _RimPower       ("Rim Power",       Range(0.1,8))  = 3.0
        _RimIntensity   ("Rim Intensity",   Range(0,1))    = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Step1Threshold;
                half   _Step2Threshold;
                half4  _ShadowColor1;
                half4  _ShadowColor2;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                Light mainLight = GetMainLight();
                half3 normalWS  = normalize(IN.normalWS);
                half  NdotL     = dot(normalWS, normalize(mainLight.direction));

                half3 shadedColor;
                if (NdotL >= _Step1Threshold)
                    shadedColor = texColor.rgb;
                else if (NdotL >= _Step2Threshold)
                    shadedColor = texColor.rgb * _ShadowColor1.rgb;
                else
                    shadedColor = texColor.rgb * _ShadowColor2.rgb;

                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                half  rim       = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimIntensity;
                half3 rimColor  = _RimColor.rgb * rim;

                return half4(shadedColor + rimColor, texColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Step1Threshold;
                half   _Step2Threshold;
                half4  _ShadowColor1;
                half4  _ShadowColor2;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings shadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _MainLightPosition.xyz));
                return OUT;
            }

            half4 shadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 2: 컴파일 완료 대기**

`mcpforunity://editor/state` 읽어 `is_compiling == false` 될 때까지 대기

- [ ] **Step 3: 컴파일 에러 확인**

```
read_console(types=["error"], count=10)
```
에러 없으면 통과. 에러 있으면 수정 후 재업로드.

- [ ] **Step 4: 커밋**

```bash
git add Assets/Shaders/CelShader.shader
git commit -m "feat: add URP CelShader with 3-step shading and rim light"
```

---

### Task 2: CelOutline.shader + 아웃라인 머티리얼 생성

**Files:**
- Create: `Assets/Shaders/CelOutline.shader`
- Create: `Assets/Shaders/Materials/CelOutlineMaterial.mat`

**Interfaces:**
- Produces: `Custom/CelOutline` 셰이더 + `CelOutlineMaterial.mat` (Task 3에서 RenderObjects에 할당)

- [ ] **Step 1: CelOutline.shader 작성**

`Assets/Shaders/CelOutline.shader`:

```hlsl
Shader "Custom/CelOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color)         = (0.1, 0.1, 0.1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.05)) = 0.005
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-1" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest Less

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                half  _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posOS  = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 2: Materials 폴더 생성 및 CelOutlineMaterial 생성**

```
manage_asset(action="create", asset_type="Material", path="Assets/Shaders/Materials/CelOutlineMaterial.mat")
```
생성 후 셰이더를 `Custom/CelOutline`으로 설정:
```
manage_asset(action="set_shader", path="Assets/Shaders/Materials/CelOutlineMaterial.mat", shader="Custom/CelOutline")
```
(도구가 지원하지 않으면 Inspector에서 직접 설정)

- [ ] **Step 3: 컴파일 에러 확인**

```
read_console(types=["error"], count=10)
```

- [ ] **Step 4: 커밋**

```bash
git add Assets/Shaders/CelOutline.shader Assets/Shaders/Materials/
git commit -m "feat: add CelOutline shader and material for vertex extrusion outline"
```

---

### Task 3: Character 레이어 + RenderObjects Feature 설정

**Files:**
- Modify: `ProjectSettings/TagManager.asset` (Character 레이어 추가)
- Modify: `Assets/Settings/PC_Renderer.asset` (RenderObjects Feature 추가)

**Interfaces:**
- Consumes: `Assets/Shaders/Materials/CelOutlineMaterial.mat` (Task 2)

- [ ] **Step 1: "Character" 레이어 추가**

```
manage_editor(action="add_layer", layer_name="Character")
```
또는 Project Settings > Tags and Layers에서 Layer 8을 "Character"로 설정.

- [ ] **Step 2: School Katana Girl 루트 오브젝트를 Character 레이어로 변경**

씬에서 캐릭터 루트 오브젝트 탐색:
```
find_gameobjects(search_term="School_Katana", search_method="by_name")
```
찾은 오브젝트들 레이어 변경 (include_children=true):
```
manage_gameobject(action="modify", target=<instance_id>, layer=8, apply_to_children=true)
```

- [ ] **Step 3: PC_Renderer에 RenderObjects Feature 추가**

```
manage_graphics(
    action="add",
    feature_type="RenderObjects",
    renderer_path="Assets/Settings/PC_Renderer.asset",
    settings={
        "name": "CharacterOutline",
        "event": "AfterRenderingOpaques",
        "layerMask": 256,
        "overrideMaterial": "Assets/Shaders/Materials/CelOutlineMaterial.mat",
        "overrideMaterialPassIndex": 0
    }
)
```

- [ ] **Step 4: 스크린샷으로 아웃라인 확인**

씬을 플레이 또는 에디터에서:
```
manage_camera(action="screenshot", include_image=True, max_resolution=512)
```
캐릭터 실루엣에 검은 아웃라인이 보이면 성공.

- [ ] **Step 5: 커밋**

```bash
git add ProjectSettings/TagManager.asset Assets/Settings/PC_Renderer.asset
git commit -m "feat: add Character layer and RenderObjects outline feature to PC_Renderer"
```

---

### Task 4: Editor 스크립트로 머티리얼 일괄 업그레이드

**Files:**
- Create: `Assets/Shaders/Editor/CelShaderUpgrader.cs`
- Modify: `Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Materials/**/*.mat` (약 15개)

**Interfaces:**
- Consumes: `Custom/CelShader` (Task 1)
- Produces: 15개 머티리얼이 `Custom/CelShader` 사용. `_MainTex` 값이 `_BaseMap`으로 복사됨.

- [ ] **Step 1: Editor 폴더 생성 및 CelShaderUpgrader.cs 작성**

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

- [ ] **Step 2: 컴파일 완료 대기 + 에러 확인**

```
// editor/state → is_compiling == false
read_console(types=["error"], count=10)
```

- [ ] **Step 3: 업그레이더 실행**

```
execute_menu_item(menu_path="Tools/Cel Shader/Upgrade School Katana Girl Materials")
```

- [ ] **Step 4: 콘솔 결과 확인**

```
read_console(types=["log", "error"], count=30)
```
`[CelShaderUpgrader] Done. N/N materials upgraded.` 로그 확인. N ≥ 14 이상이어야 정상.

- [ ] **Step 5: 서라운드 스크린샷으로 최종 시각 확인**

```
manage_camera(
    action="screenshot",
    batch="surround",
    view_target="School_Katana_FullBody",
    max_resolution=512,
    include_image=True
)
```
3단계 셰이딩 경계, 림라이트, 아웃라인이 모두 보이면 성공.

- [ ] **Step 6: 커밋**

```bash
git add Assets/Shaders/Editor/CelShaderUpgrader.cs
git add "Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Materials/"
git commit -m "feat: upgrade School Katana Girl materials to CelShader"
```
