# 카툰 렌더링 셰이더 사용 가이드

## 개요

이 가이드는 `Custom/CelShader` + `Custom/CelOutline` 셰이더를 캐릭터에 적용하는 방법을 설명합니다.

---

## 셰이더 구성

| 셰이더 | 경로 | 역할 |
|--------|------|------|
| `Custom/CelShader` | `Assets/Shaders/CelShader.shader` | 3단계 셀 셰이딩 + 림라이트 (캐릭터 바디용) |
| `Custom/CelOutline` | `Assets/Shaders/CelOutline.shader` | 버텍스 익스트루전 아웃라인 (렌더 피처로 자동 적용) |

---

## 최초 설정 (프로젝트당 1회)

Unity Editor Console에 에러가 없는 상태에서 순서대로 실행합니다.

### 1단계: 아웃라인 렌더 피처 추가

```
메뉴 → Tools → Cel Shader → Add Outline Renderer Feature
```

- `Assets/Settings/PC_Renderer.asset`에 `CharacterOutline` RenderObjects Feature를 추가합니다.
- 이미 추가된 경우 자동으로 스킵됩니다.

---

## School Katana Girl 적용 (자동)

### 2단계: 씬 열기

School Katana Girl 캐릭터가 배치된 씬을 엽니다.

### 3단계: 캐릭터 레이어 설정

```
메뉴 → Tools → Cel Shader → Set Katana Girl to Character Layer
```

- 씬에서 `School_Katana` 루트 오브젝트와 하위 오브젝트 전체를 **Layer 8 (Character)** 로 변경합니다.

### 4단계: 머티리얼 일괄 교체

```
메뉴 → Tools → Cel Shader → Upgrade School Katana Girl Materials
```

- `Materials/Cloth`, `Materials/Hair`, `Materials/Body`, `Materials/Weapon` 폴더의 머티리얼 전체를 `Custom/CelShader`로 교체합니다.
- 기존 `_MainTex` 텍스처를 `_BaseMap`으로 자동 복사합니다.
- Console 로그에서 결과 확인: `[CelShaderUpgrader] Done. N/N materials upgraded.`

---

## 다른 캐릭터 수동 적용

School Katana Girl 외 캐릭터(예: Humanoid Bot)에 적용할 때는 수동으로 진행합니다.

### 머티리얼 셰이더 교체

1. Project 창에서 대상 머티리얼 선택
2. Inspector → Shader → `Custom/CelShader` 선택
3. `Base Map`에 기존 텍스처 할당

### 캐릭터 레이어 설정

1. Hierarchy에서 캐릭터 루트 오브젝트 선택
2. Inspector → Layer → `Character` (Layer 8) 선택
3. "Change children" 팝업에서 **Yes, change children** 클릭

---

## 셰이더 파라미터 조정

머티리얼 Inspector에서 캐릭터마다 개별 조정 가능합니다.

### CelShader 파라미터

| 파라미터 | 기본값 | 설명 |
|----------|--------|------|
| Base Map | — | 기본 텍스처 |
| Base Color | (1,1,1,1) | 텍스처에 곱하는 색상 |
| Step 1 Threshold | 0.5 | 밝은 면 → 중간 그림자 경계 (값 ↑ = 밝은 면 넓어짐) |
| Step 2 Threshold | 0.2 | 중간 그림자 → 어두운 그림자 경계 |
| Shadow Color 1 | (0.75,0.75,0.8,1) | 1차 그림자 색 |
| Shadow Color 2 | (0.55,0.55,0.65,1) | 2차 그림자 색 |
| Rim Color | (1,1,1,1) | 림라이트 색상 |
| Rim Power | 3.0 | 림라이트 범위 (값 ↑ = 얇아짐) |
| Rim Intensity | 0.4 | 림라이트 강도 |

### CelOutline 파라미터 (CelOutlineMaterial)

| 파라미터 | 기본값 | 설명 |
|----------|--------|------|
| Outline Color | (0.1,0.1,0.1,1) | 아웃라인 색상 |
| Outline Width | 0.005 | 아웃라인 두께 (월드 공간 단위) |

> **참고:** 아웃라인은 `Assets/Shaders/Materials/CelOutlineMaterial.mat` 한 곳에서 전역 제어됩니다. 캐릭터별 두께/색상 분리가 필요하면 머티리얼을 복제해 캐릭터 레이어별로 별도 RenderObjects Feature를 추가하세요.

---

## 아웃라인이 보이지 않을 때

1. **Character 레이어 확인** — 캐릭터가 Layer 8(Character)에 있는지 확인
2. **Renderer Feature 확인** — `Assets/Settings/PC_Renderer.asset`을 Inspector에서 열어 `CharacterOutline` Feature가 활성화되어 있는지 확인
3. **Outline Width 확인** — 값이 너무 작으면 보이지 않을 수 있음 (0.003~0.01 권장)
4. **카메라 거리** — 아웃라인은 오브젝트 공간 기준이므로 카메라가 멀어질수록 화면상 두께가 얇아짐

---

## 관련 파일

```
Assets/Shaders/
├── CelShader.shader
├── CelOutline.shader
├── Materials/
│   └── CelOutlineMaterial.mat
└── Editor/
    ├── AddOutlineRendererFeature.cs
    ├── SetCharacterLayer.cs
    └── CelShaderUpgrader.cs

Assets/Settings/
└── PC_Renderer.asset

ProjectSettings/
└── TagManager.asset  (Layer 8 = Character)
```
