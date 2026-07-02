# 카툰 렌더링 셰이더 설계 문서

**날짜**: 2026-06-26  
**대상 프로젝트**: ProjectA (URP)  
**적용 대상**: School Katana Girl 캐릭터 머티리얼

---

## 개요

URP Shader Graph를 사용해 커스텀 Cel(카툰) 셰이더를 제작하고, School Katana Girl의 기존 머티리얼에 적용한다. 깨진 UTS2 셰이더 참조를 교체하는 것이 목적이다.

---

## 구성 요소

### 1. CelShader.shadergraph
`Assets/Shaders/CelShader.shadergraph`

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `_BaseMap` | Texture2D | 기본 텍스처 |
| `_BaseColor` | Color | 기본 색상 곱 |
| `_Step1Threshold` | Float (0–1) | 1단계→2단계 경계 |
| `_Step2Threshold` | Float (0–1) | 2단계→3단계 경계 |
| `_ShadowColor1` | Color | 1차 그림자 색 |
| `_ShadowColor2` | Color | 2차 그림자 색 |
| `_RimColor` | Color | 림라이트 색상 |
| `_RimPower` | Float | 림라이트 범위 (Fresnel 지수) |
| `_RimIntensity` | Float | 림라이트 강도 |

**3단계 셰이딩 로직:**
1. `NdotL = dot(Normal, LightDir)` 계산
2. `NdotL >= Step1Threshold` → BaseColor
3. `NdotL >= Step2Threshold` → BaseColor * ShadowColor1
4. 그 외 → BaseColor * ShadowColor2
5. 결과에 림라이트(Fresnel 기반) 추가

### 2. CelOutline.shader
`Assets/Shaders/CelOutline.shader`

버텍스 노멀 방향으로 메시를 익스트루전하는 Back-face 아웃라인 셰이더.

**파라미터:**

| 이름 | 타입 | 설명 |
|------|------|------|
| `_OutlineColor` | Color | 아웃라인 색상 |
| `_OutlineWidth` | Float | 아웃라인 두께 (뷰 공간 기준) |

**구현 방식:**
- URP Renderer Feature `RenderObjects`를 통해 별도 패스로 렌더
- Cull Front / ZWrite On
- 버텍스를 노멀 방향으로 `_OutlineWidth`만큼 이동

### 3. URP Renderer Feature 설정
`Assets/Settings/PC_Renderer.asset` 에 RenderObjects Feature 추가:
- Layer Mask: 캐릭터 레이어 (기본 전체)
- Override Material: CelOutline 머티리얼
- Event: BeforeRenderingOpaques

### 4. 머티리얼 교체 대상

School Katana Girl (~15개):
- `Materials/Cloth/Katana_Cloth 1~5.mat`
- `Materials/Hair/Katana_Hair 1~5.mat`
- `Materials/Body/Body.mat`, `Face.mat`, `Eye.mat`
- `Materials/Weapon/Weapon_Katana.mat`, `Weapon_Katana_02.mat`

각 머티리얼의 `m_Shader` 참조를 CelShader로 교체하고 기존 텍스처(`_MainTex`)를 `_BaseMap`에 연결한다.

---

## 미포함 사항

- Humanoid Bot 캐릭터 적용 (추후 가이드 문서로 대응)
- Mobile_Renderer 설정 변경 (별도 최적화 작업)
- 배경/환경 오브젝트 카툰 처리

---

## 추후 작업

- `docs/cartoon-shader-application-guide.md` 작성 (다른 캐릭터 적용 방법 안내)
