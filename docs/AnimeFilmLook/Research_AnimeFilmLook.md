# Research — 1960~70년대 일본 애니메이션 룩 (채도 저하 + 외곽 세로선 노이즈)

> **세로선 노이즈** — 필름이 영사기를 지나며 긁혀 생기는 얇은 세로 줄. 화면을 세로로 관통하며 몇 프레임만 나타났다 사라진다. 이 문서·플랜에서 "세로선"은 전부 이것을 가리킨다(대응 자산: 아래 §4의 `AnimeFilmScratch.shader`).

## 1. 요구 둘의 계층이 다르다

| 요구 | 성질 | 이미 있는 수단 |
|---|---|---|
| 원래 색보다 낮은 채도 | 색 보정 — **화면 전체에 상시** | URP `ColorAdjustments.Saturation` (Volume 오버라이드) |
| 외곽에 가끔 얇은 세로선 | 화면 위에 덧그리는 그림 — **간헐적·위치 한정** | 없음 |

채도 쪽은 **엔진이 이미 가진 기능**이라 새로 만들 것이 없다. 세로선만 수단이 없다. 둘을 한 덩어리 "커스텀 포스트 프로세싱"으로 묶으면 이미 있는 기능을 다시 구현하게 된다.

## 2. 현재 렌더링 구성

- Unity **6000.3.11f1**, URP **17.3.0**(`com.unity.render-pipelines.universal`), Shader Graph 17.3.0.
- RP 에셋 둘: `Assets/Settings/PC_RPAsset.asset` / `Mobile_RPAsset.asset`. 렌더러는 `PC_Renderer.asset` / `Mobile_Renderer.asset`.
- `PC_Renderer`의 렌더러 피처 7개 — 활성 상태가 갈린다:

| 이름 | 타입 | 활성 | 출처 |
|---|---|---|---|
| `WatercolorFx` | 외부 에셋 | 1 | `99. External Assets/Watercolor` |
| `FlowFx` | 외부 에셋 | 1 | 외부 |
| `ScreenSpaceAmbientOcclusion` | URP 내장 | 1 | — |
| `AmbushOutline` | `RenderObjects` | 1 | §11-8 기습 강조 |
| `CharacterOutline` | `RenderObjects` | 0 | — |
| `FinaleActors` / `FinaleBackground` | `RenderObjects` | 0 | §14, 런타임에 `SetActive` |

  ⚠ **`RenderObjects` 피처 넷은 전부 `AfterRenderingOpaques`**라 포스트 프로세싱보다 **앞**이다. 즉 §14 마무리 실루엣의 빨강/검정은 **색 보정을 그대로 통과한다**(§5 참조).

- **URP에 `FullScreenPassRendererFeature`가 내장돼 있다**(`Runtime/RendererFeatures/FullScreenPassRendererFeature.cs`). 전체 화면에 셰이더 한 장을 찍는 일에 **C# 렌더러 피처를 새로 쓸 이유가 없다**. 주요 필드:
  - `injectionPoint` — `BeforeRenderingTransparents` / `BeforeRenderingPostProcessing` / **`AfterRenderingPostProcessing`** 셋.
  - `fetchColorBuffer` — **화면 내용을 읽어야 할 때만** 켠다. 끄면 색 복사 패스가 통째로 없어지고 하드웨어 블렌드로만 얹힌다(`requiresIntermediateTexture = fetchColorBuffer`).
  - `requirements` — Depth/Normal/Motion 요청. 기본 `None`.

## 3. Volume 구성

- 씬 `BattleScene`의 `Global Volume`(priority 0, weight 1) → **`Assets/Settings/SampleSceneProfile.asset`**.
  - 들어 있는 오버라이드: `Bloom` · `Vignette` · `Tonemapping` · `MotionBlur`. **`ColorAdjustments`가 없다** → 채도는 지금 아무도 안 건드린다.
- `ComboPostFxVolume`(priority 10) → `ComboPostFxProfile`: `Vignette` · `ChromaticAberration`뿐(§12). **채도를 안 들고 있으므로 충돌하지 않는다** — 콤보가 올라도 채도 값은 전역 프로파일 것이 그대로 유지된다.
- `Assets/Settings/DefaultVolumeProfile.asset`은 URP 전역 기본값이며 **씬이 아니라 파이프라인 전체**에 걸린다. 여기 채도를 넣으면 곡 선택 씬·탐색 씬까지 전부 따라온다.

## 4. 참고할 기존 관용구

- **`ComboPostFxView`**(§12) — *"이 클래스가 미는 것은 `Volume.weight` 하나뿐이다. 효과를 추가하는 일 = 프로파일에 오버라이드 한 줄 추가."* 런타임 제어가 필요해지는 순간의 정답이 이미 이 형태로 있다.
- **`FinaleSilhouetteDirector`**(§14) — **이미 렌더러 피처를 이름으로 켜고 끈다.** `UniversalRendererData[] renderers` + `SetFeatures(bool)`이 `backgroundFeatureName`/`actorFeatureName`과 일치하는 피처에 `SetActive`를 건다. 그리고 원복 규율이 이미 서 있다 — `Restore()`가 **노출 종료 · `OnAllPatternsCleared`(곡 중단) · `OnDisable`** 세 경로에서 불리고 *"여러 번 불려도 안전해야 한다"*가 헤더에 못박혀 있다. `restoreTimeScale`/`restoreAudioPitch`처럼 **원래 값을 캐시했다 되돌리는** 관용구도 그 안에 있다.
  → **필름 룩을 마무리 실루엣 동안 끄는 데 새 복구 경로가 필요 없다.** 이 클래스에 값 하나씩만 얹으면 세 경로가 공짜로 따라온다.
- **`AmbushOutline`**(§11-8) — 튜닝 값을 머티리얼 한 곳에 모으고 코드는 대상만 정한다.
- `Assets/Shaders/`에 손으로 쓴 HLSL 셰이더가 이미 넷 있다(`CelShader` · `CelOutline` · `EnemyDissolve` · `EnemySpawnWarp`) — 머티리얼은 `Assets/Shaders/Materials/`. **셰이더를 직접 쓰는 것이 이 프로젝트의 기존 방식**이고 Shader Graph는 `Dissolve.shadergraph` 하나뿐이다.

## 5. 제약 · 상호작용 (⚠ 설계에 직접 영향)

1. **루트 Canvas가 `ScreenSpaceOverlay`다**(§7-5). 패턴인풋·포커스 링·가이드라인·HUD는 **모든 포스트 프로세싱과 풀스크린 패스보다 뒤에 그려진다** → 채도 저하도 세로선도 **판정 단서를 1픽셀도 건드리지 않는다.** §12의 *"판정을 방해하는 순간 연출이 아니라 손해다"*가 여기서는 구조적으로 자동 충족된다.
2. **⚠ 마무리 실루엣(§14)이 채도 저하를 정통으로 맞는다.** `FinaleBackground`(빨강) / `FinaleActors`(검정)는 `AfterRenderingOpaques`라 색 보정보다 앞이고, 검정은 채도와 무관하지만 **빨강은 그만큼 바랜다**. 해법 둘 — 머티리얼 색을 그만큼 진하게 올리거나(값 하나), **그 구간 동안 필름 룩을 끈다**(§4의 `SetFeatures`·`Restore`에 얹는다). 후자가 낫다: 보정은 채도 값을 손볼 때마다 다시 맞춰야 하는 **두 값의 짝**을 만들고, 끄는 쪽은 그 짝이 애초에 안 생긴다. 게다가 마무리 실루엣은 *"화면이 뒤집힌다"*가 사건 자체라 **필름 긁힘까지 같이 걷히는 편이 연출에 맞는다**.
  → **⚠ 그러려면 채도가 `SampleSceneProfile`에 있으면 안 된다.** 그 프로파일에는 `Bloom`·`Tonemapping`·`MotionBlur`가 같이 살아서 weight를 내리면 그것들까지 꺼진다. **필름 룩은 자기 Volume에 있어야 한다**(`ComboPostFxVolume`이 `Global Volume`을 안 건드리는 것과 같은 근거, §12).
3. **⚠ 세로선이 히트스톱(§7-3)에 안 언다.** `Time.timeScale`을 안 쓰는 프로젝트이고 셰이더의 `_Time`은 어차피 `timeScale` 밖이다 — **필름 긁힘은 화면(영사기)의 사건이지 게임 세계의 사건이 아니므로 안 어는 것이 맞다.**
4. `WatercolorFx` · `FlowFx`가 이미 활성이다. 새 피처는 **리스트 맨 뒤**에 붙이고 주입 시점을 `AfterRenderingPostProcessing`으로 두면 그 둘의 결과 위에 얹힌다.
5. Mobile 렌더러는 별개 에셋이라 **같은 배선을 한 번 더** 해야 한다(현재 PC 기준으로 개발 중).

## 6. 정리 — 만들어야 하는 것

- 채도: **새 자산 0개, 새 코드 0줄.** `SampleSceneProfile`에 `ColorAdjustments` 오버라이드 추가.
- 세로선: 셰이더 1 + 머티리얼 1 + `FullScreenPassRendererFeature` 인스턴스 1. **C# 0줄** — 간헐성·위치·굵기를 전부 셰이더 안에서 `_Time.y` 해시로 만들면 매 프레임 값을 밀어 줄 주체가 필요 없다.
- 런타임 제어(콤보 연동 · 곡 구간별 on/off)는 **지금 요구에 없다.** 필요해지면 §4의 `ComboPostFxView` 관용구를 그대로 쓴다.
