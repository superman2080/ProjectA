# Plan — 툰 단색화 (화면 전체 포스터라이즈)

근거: `Research_ToonPosterize.md` · 선행: `Plan_AnimeFilmLook.md`(구현 완료)

## 0. 설계 요지

**조명을 다시 계산하지 않고 이미 계산된 결과를 자른다.** 머티리얼 ~140개를 이관하는 대신 화면 층에서 한 번 끊는다 — 포인트 라이트 12개·베이크 GI·에셋팩 셰이더가 전부 그대로 살아 있고, 룩이 캐릭터·배경·이펙트에 **한 규칙으로** 걸린다.

| 항목 | 값 |
|---|---|
| 새 자산 | 셰이더 1 + 머티리얼 1 |
| 새 C# | **0줄** |
| 머티리얼 변경 | **0개** |
| 런타임 비용 | 화면 복사 1회 + 풀스크린 1패스 |

**세 층의 순서가 곧 설계다**:
```
[씬 렌더] → [채도 저하(Volume)] → [포스터라이즈] → [세로선(FilmScratch)]
```
채도보다 뒤인 것은 주입 시점이 자동으로 보장하고, 세로선보다 앞인 것은 **피처 리스트 순서**가 정한다.

**§14는 안 건드린다** — 실루엣 구간 화면은 이미 단색이라 계단화가 아무 일도 하지 않는다(Research §6).

---

## Step 1 — 셰이더
- [x] `Assets/Shaders/AnimeToonPosterize.shader` 작성. 이름 `Hidden/AnimeToonPosterize`.
- [x] 골격은 `AnimeFilmScratch.shader`와 같다(`Blit.hlsl`의 `Vert`/`Varyings`, `CBUFFER_START(UnityPerMaterial)`, 프로퍼티 전부 노출). **다른 점은 화면을 읽는다는 것 하나뿐**이다 — `SampleSourceTexture(uv)`로 `_BlitTexture`를 샘플.
- [x] 블렌드 없음(`Blend Off`), `ZWrite Off` · `ZTest Always` · `Cull Off`. 읽어서 고쳐 쓰는 패스라 얹는 게 아니다.
- [x] 계단 대상 둘을 **인스펙터 토글 하나**로 가른다(`[Toggle] _PosterizeRGB`, 셰이더 분기 한 줄):
  - **밝기만**(기본) — `lum = Luminance(rgb)` → `q = Quantize(lum)` → `rgb *= q / max(lum, 1e-4)`. 색조·채도가 보존된 채 음영만 끊긴다.
  - **RGB 전부** — 채널마다 `Quantize`. 색 수 자체가 줄어 인쇄물 느낌이 된다.
- [x] `Quantize(x)` — 분모가 `_Steps`가 아니라 **`_Steps - 1`**이어야 최상단 레벨이 1.0에 닿는다. **구현하며 둘이 바뀌었다**:
  - **`floor`가 아니라 반올림**이다. `floor`면 모든 픽셀이 반 밴드만큼 어두워져 화면 전체가 가라앉는다.
  - **⚠ 자르기 전에 지각 공간으로 옮긴다**(`LinearToSRGB` → 양자화 → `SRGBToLinear`). 이 시점의 화면 값은 **선형**이라(sRGB 변환은 마지막 블릿이 한다) 균등 간격으로 그냥 자르면 눈에 보이는 중간 밝기가 실제로는 0.01~0.05여서 **전부 최하위 밴드로 눌려 화면이 통째로 검어진다**. 실측으로 확인한 지점이다.
- [x] `_Softness`(기본 0.02) — 밴드 경계를 `smoothstep`으로 아주 조금 무르게 한다. 0이면 완전한 계단. **⚠ 이 노브가 없으면 하늘·안개에서 계단이 압축 오류처럼 보인다**(Research §7-1).
- [x] `_Strength`(기본 1) — 원본과 계단화 결과를 `lerp`. 0이면 원래 화면 그대로라 **A/B 비교와 부분 적용이 값 하나로 된다**.
- [x] 프로퍼티: `_Steps`(기본 8) · `_Softness` · `_Strength` · `_PosterizeRGB`. **실물 값은 화면을 봐야 정해진다.**

## Step 2 — 머티리얼 + 렌더러 배선
- [x] `Assets/Shaders/Materials/AnimeToonPosterizeMaterial.mat` 생성.
- [x] `PC_Renderer.asset`에 `Full Screen Pass Renderer Feature` 추가. 이름 **`ToonPosterize`**.
  - `Pass Material` = 위 머티리얼
  - `Injection Point` = `After Rendering Post Processing`
  - **`Fetch Color Buffer` = 켬** ⚠ 세로선과 반대다 — 이 패스는 화면을 읽는다. 끄면 `_BlitTexture`가 비어 화면이 검거나 쓰레기 값이 된다.
  - `Requirements` = `None`, `Bind Depth-Stencil` = 끔
- [x] **⚠ 리스트에서 `FilmScratch`보다 앞에 둔다.** 같은 주입 시점의 패스는 큐 순서대로 실행되므로 이것이 순서의 유일한 근거다. 뒤에 두면 **긁힘까지 계단화된다**.
- [x] `Mobile_Renderer.asset`에도 같은 이름·같은 값·같은 순서로 추가.

## Step 3 — 검증
- [x] 플레이 모드 + `source=screen` 캡처. ⚠ **에디터 게임뷰 캡처로는 안 보인다**(`FullScreenPassRendererFeature`가 `CameraType.Preview`에서 스스로 빠진다 — `FilmScratch`에서 겪은 그대로).
- [x] 확인할 것 넷:
  1. 벽·바닥의 빛 그라데이션이 **계단으로 끊긴다**.
  2. 포인트 라이트 빛웅덩이가 **여전히 있다**(사라졌으면 포스터라이즈가 아니라 조명이 죽은 것이다).
  3. **세로선이 계단화되지 않았다** — 얇은 선이 그대로 얇다(굵어졌거나 밴드에 먹혔으면 피처 순서가 뒤집힌 것이다).
  4. 포커스 링·HUD가 영향을 안 받는다(`ScreenSpaceOverlay`).
- [x] `_Steps`를 4·6·8·10으로, `_PosterizeRGB`를 켜고 꺼서 각각 캡처해 비교했다. **확정된 기본값: `_Steps = 8` · `_PosterizeRGB = 꺼짐` · `_Softness = 0.02` · `_Strength = 1`.**
  - `_Steps` 4는 이 씬(매우 어둡다)에서 대부분을 검정으로 눌러 과했고, 6은 더 강한 툰, 8이 단색 면과 거리 조명이 함께 사는 지점이었다.
  - `_PosterizeRGB`는 어두운 구간에서 **색조가 무너진다**(바닥이 빨강·노랑으로 튄다). 기능은 남기되 기본은 밝기만.
- [ ] **마무리 실루엣(§14) 확인** — 빨강·검정이 그대로인지. 눈에 띄게 어긋나면 그때 `scratchFeatureName`과 같은 관용구로 `SetFeatures`에 한 줄 단다(**지금은 안 단다**).

## Step 4 — CLAUDE.md 갱신
- [x] §12-1(필름 룩)에 포스터라이즈를 **셋째 층**으로 추가. 담을 사실 셋:
  - 머티리얼을 안 고치고 화면에서 자른다 — 그래서 포인트 라이트·베이크 GI·에셋팩 셰이더가 전부 살아 있다.
  - ⚠ **피처 순서가 곧 설계다**: 포스터라이즈 → 세로선. 뒤집히면 긁힘이 계단화된다.
  - ⚠ `Fetch Color Buffer`가 **세로선과 반대로 켜져 있다**(화면을 읽는 패스).
- [x] 이미 있는 툰 자산의 현황도 한 줄 — `Custom/CelShader`는 **쓰는 머티리얼이 0개**이고 메인 라이트만 봐서 드롭인이 아니다(누군가 다시 손대기 전에 알아야 한다).

---

## 하지 않는 것 (요청받으면 그때)
- 머티리얼별 툰 셰이더 이관 — 물체마다 다른 음영 단계가 필요해지는 순간의 길이고, 두 층은 겹쳐도 충돌하지 않는다.
- 노멀 기반 외곽선 — `Custom/CelOutline`과 `CharacterOutline` 피처(현재 비활성)가 이미 있다. **툰 단색화와 별개 축**이라 이번 요구에 없다.
- 디더링·색상 램프 LUT — `ColorLookup` Volume 오버라이드가 이미 URP에 있어 필요해지면 그쪽이 먼저다.
