# Task 1 Report: CelShader.shader 생성

## 생성된 파일
- `Assets/Shaders/CelShader.shader`

## 구현 내용

### 셰이더 구조
- **셰이더 이름**: `Custom/CelShader` (Task 4의 `Shader.Find` 호환)
- **SubShader Tags**: `RenderPipeline = UniversalPipeline`, `Queue = Geometry`
- **패스**: ForwardLit + ShadowCaster (2개)

### Properties (전체 구현)
| Property | Type | Default | 상태 |
|---|---|---|---|
| `_BaseMap` | 2D | "white" | 구현 |
| `_BaseColor` | Color | (1,1,1,1) | 구현 |
| `_Step1Threshold` | Range(0,1) | 0.5 | 구현 |
| `_Step2Threshold` | Range(0,1) | 0.2 | 구현 |
| `_ShadowColor1` | Color | (0.75,0.75,0.8,1) | 구현 |
| `_ShadowColor2` | Color | (0.55,0.55,0.65,1) | 구현 |
| `_RimColor` | Color | (1,1,1,1) | 구현 |
| `_RimPower` | Range(0.1,8) | 3.0 | 구현 |
| `_RimIntensity` | Range(0,1) | 0.4 | 구현 |

### CBUFFER 처리
- `_BaseMap_ST`가 ForwardLit 및 ShadowCaster 패스 모두의 `CBUFFER_START(UnityPerMaterial)` 안에 포함되어 `TRANSFORM_TEX` 정상 작동 보장
- URP SRP Batcher 호환성 확보

### ForwardLit 패스 로직
1. `GetMainLight(TransformWorldToShadowCoord(positionWS))`로 메인 라이트 획득
2. `NdotL = dot(normalWS, normalize(mainLight.direction))`
3. 3단계 셀 셰이딩:
   - `NdotL >= _Step1Threshold` → `texColor.rgb` (완전 밝음)
   - `NdotL >= _Step2Threshold` → `texColor.rgb * _ShadowColor1.rgb` (중간 그림자)
   - else → `texColor.rgb * _ShadowColor2.rgb` (깊은 그림자)
4. Fresnel 림라이트: `pow(1 - saturate(dot(normalWS, viewDir)), _RimPower) * _RimIntensity`
5. Fog 지원 포함 (`ComputeFogFactor` / `MixFog`)
6. `#pragma multi_compile` 키워드 variants: 그림자 캐스케이드, 소프트 섀도우

### ShadowCaster 패스
- `ShadowCasterPass.hlsl` include 없이 자체 구현
- `ApplyShadowBias(posWS, normWS, lightDir)` 적용 (self-shadowing 방지)
- Near-plane 클램핑 (`UNITY_REVERSED_Z` 조건부 처리): DirectX/OpenGL 양쪽 대응
- `ColorMask 0` / `ZWrite On` 표준 설정

## 컴파일 결과

MCP for Unity 도구가 이 Claude Code 세션에 연결되어 있지 않아 (`read_console`, `mcpforunity://editor/state` 직접 호출 불가) Unity 에디터 컴파일 결과를 자동 확인하지 못했습니다.

셰이더 파일은 `C:\Unity\ProjectA\Assets\Shaders\CelShader.shader`에 정상 생성되었으며, Unity 에디터가 실행 중이라면 AssetDatabase가 파일 변경을 감지하여 자동으로 임포트/컴파일합니다.

**컴파일 에러 가능성 검토 (정적 분석):**
- URP 14+ include 경로 정확 (`com.unity.render-pipelines.universal/ShaderLibrary/...`)
- `CBUFFER_START` / `CBUFFER_END` 패턴 올바름
- `TEXTURE2D` + `SAMPLER` 선언 분리 (SRP Batcher 요구사항)
- `UNITY_VERTEX_INPUT_INSTANCE_ID` / `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO` 포함
- `ApplyShadowBias` 함수: `Shadows.hlsl`에 선언됨
- `_MainLightPosition`: URP `Core.hlsl` → `Input.hlsl` 체인으로 자동 선언됨

## 우려사항

1. **MCP 컴파일 확인 불가**: Unity MCP 서버가 이 세션에서 활성화되지 않아 실제 컴파일 에러를 자동 감지하지 못했습니다. Unity 에디터에서 Console 창을 통해 직접 확인이 필요합니다.

2. **그림자 수신 조건부 처리**: ForwardLit 패스에서 `TransformWorldToShadowCoord`를 항상 호출하는 방식을 사용했습니다. URP 14에서는 이 패턴이 안전하게 동작하지만, 이전 버전에서는 조건부 처리가 필요할 수 있습니다.

3. **Assets/Shaders 폴더 신규 생성**: 기존에 없던 폴더를 새로 생성했습니다.

STATUS: DONE_WITH_CONCERNS
