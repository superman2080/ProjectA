# Task 2 Brief: CelOutline.shader + 아웃라인 머티리얼 생성

## 목표
버텍스 익스트루전 방식의 아웃라인 셰이더와 이를 사용하는 머티리얼을 생성한다.

## 글로벌 제약
- 렌더 파이프라인: URP
- 셰이더 이름: `Custom/CelOutline` (정확히 이 이름)
- 셰이더 저장 경로: `Assets/Shaders/CelOutline.shader`
- 머티리얼 저장 경로: `Assets/Shaders/Materials/CelOutlineMaterial.mat`

## Task 1 완료 상태
- `Assets/Shaders/CelShader.shader` 생성 완료

## 생성할 파일

### 1. `Assets/Shaders/CelOutline.shader`

**Properties:**
| Property | Type | Default |
|---|---|---|
| `_OutlineColor` | Color | (0.1, 0.1, 0.1, 1) |
| `_OutlineWidth` | Range(0, 0.05) | 0.005 |

**셰이더 로직:**
- `Cull Front` — 뒷면만 렌더링해 아웃라인 생성
- `ZTest Less` — 기본 메시 앞에만 표시
- 버텍스 셰이더: `posOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth`
- 프래그먼트 셰이더: `return _OutlineColor`
- `Queue = Geometry-1` (메인 메시보다 먼저 렌더)
- `RenderPipeline = UniversalPipeline` 태그 필수

### 2. `Assets/Shaders/Materials/CelOutlineMaterial.mat`

Unity `.mat` 파일 형식으로 직접 생성한다. YAML 형식:

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: CelOutlineMaterial
  m_Shader: {fileID: 4800000, guid: 00000000000000000000000000000000, type: 3}
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs: []
    m_Ints: []
    m_Floats:
    - _OutlineWidth: 0.005
    m_Colors:
    - _OutlineColor: {r: 0.1, g: 0.1, b: 0.1, a: 1}
  m_BuildTextureStacks: []
```

**중요:** `m_Shader`의 guid는 `CelOutline.shader` 파일이 Unity에 임포트된 후 생성되는 GUID를 사용해야 한다. `.meta` 파일에서 읽거나, guid를 `0000...`으로 두고 Unity에서 Inspector를 통해 셰이더를 수동 할당하는 방법도 가능하다.

## 구현 방법

**방법 A (권장):** 셰이더 파일만 생성하고 머티리얼은 YAML로 작성. 셰이더 guid를 얻기 위해 `.meta` 파일을 읽거나 생성.

**방법 B (대안):** 셰이더 파일 생성 후 `manage_asset(action="create", asset_type="Material", path="...")` MCP 도구로 머티리얼 생성. MCP 연결이 없으면 수동 YAML 작성.

셰이더 파일은 `Write` 도구로 직접 생성 가능하다.

## 완료 기준
- `Assets/Shaders/CelOutline.shader` 존재
- `Assets/Shaders/Materials/CelOutlineMaterial.mat` 존재 (셰이더 참조 포함)
- HLSL 문법 오류 없음 (정적 분석)

## 보고서 파일
`.superpowers/sdd/task-2-report.md`에 작성:
- 생성된 파일 목록
- 셰이더 GUID 처리 방법
- 우려사항

마지막 줄: `STATUS: DONE` 또는 `STATUS: DONE_WITH_CONCERNS`
