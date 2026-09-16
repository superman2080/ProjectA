# Plan — 1960~70년대 일본 애니메이션 룩 (채도 저하 + 외곽 세로선 노이즈)

근거: `Research_AnimeFilmLook.md`

## 0. 설계 요지

**둘을 한 덩어리로 만들지 않는다.** 채도는 URP가 이미 가진 기능(`ColorAdjustments`)이고 세로선만 수단이 없다. 한 셰이더에 둘 다 넣으면 **이미 있는 색 보정을 다시 구현**하는 동시에, 채도를 조정할 때마다 화면 전체를 복사하는 패스(`fetchColorBuffer`)를 켜야 한다.

| 층 | 수단 | 새 코드 |
|---|---|---|
| 채도 | **전용 Volume**(`FilmLookVolume`)에 `ColorAdjustments` 한 줄 | 0줄 |
| 세로선 | 셰이더 1 + 머티리얼 1 + URP 내장 `FullScreenPassRendererFeature` | 0줄 |
| 마무리 실루엣 동안 끄기 | `FinaleSilhouetteDirector`에 필드 둘 | **약 6줄** |

**C#이 6줄인 근거**: "가끔씩"은 시간의 함수이고 셰이더는 `_Time.y`를 이미 안다 — 매 프레임 값을 밀어 줄 MonoBehaviour가 필요 없다. 코드가 나가는 곳은 **게임 상태에 반응하는 지점 하나**, 즉 마무리 실루엣뿐이다. 그리고 그 클래스는 이미 렌더러 피처를 이름으로 켜고 끄며(`SetFeatures`) 복구 경로 셋(`Restore`)을 들고 있어, **새 상태도 새 복구 경로도 안 생긴다.**

**⚠ 그래서 채도는 `SampleSceneProfile`에 넣지 않는다.** 거기엔 `Bloom`·`Tonemapping`·`MotionBlur`가 같이 살아서 weight를 내리면 그것들까지 꺼진다. 필름 룩은 자기 Volume에 산다 — `ComboPostFxVolume`이 `Global Volume`을 안 건드리는 것과 같은 근거(§12).

**여전히 안 만드는 것**: 곡 구간별·콤보별 세기 조절. 그것이 필요해지면 `ComboPostFxView`를 그대로 복제하면 되고, Step 1이 이미 그 형태(전용 Volume)로 자리를 잡아 둔다.

---

## Step 1 — 채도: 전용 Volume + `ColorAdjustments`
- [x] `Assets/Settings/FilmLookProfile.asset` 생성(Volume Profile).
- [x] `BattleScene`에 `FilmLookVolume` 게임오브젝트 — `Volume`(Is Global, **Priority 5**, Weight 1, Profile = 위 에셋).
  - ⚠ Priority는 `Global Volume`(0)보다 **높고** `ComboPostFxVolume`(10)보다 **낮다**. 셋이 들고 있는 오버라이드가 서로 겹치지 않아 실제 충돌은 없지만, 순서가 곧 "무엇이 무엇을 덮는가"의 문서다.
- [x] 프로파일에 `Color Adjustments` 추가:
  - `Saturation` = **−25**(1960~70년대 셀 필름의 바랜 느낌. −40 이상 내리면 캐릭터 톤이 회색으로 눕는다)
  - `Contrast` = **+5**, `Post Exposure` = **−0.05** — 채도를 내리면 밋밋해 보이므로 대비를 아주 조금 되돌린다
  - **`Color Filter`는 건드리지 않는다**(Step 2에서 판단)
- [x] ⚠ `SampleSceneProfile`에도 `DefaultVolumeProfile`에도 넣지 않는다. 앞은 weight로 못 끄게 되고(Bloom·Tonemapping이 딸려 꺼진다), 뒤는 곡 선택 씬·탐색 씬(§9)까지 전부 따라온다.

## Step 2 — 채도: 필름 색조 판단(선택)
- [ ] Step 1만으로 "바랜 필름"이 안 되면 `White Balance` 오버라이드를 추가해 `Temperature` **+8** 정도로 살짝 따뜻하게 민다(퇴색한 필름은 노랗게 뜬다).
- [ ] ⚠ **`Color Filter`로 색을 입히지 않는다.** 필터는 곱셈이라 §14 마무리 실루엣의 빨강까지 물들이고, 그 층은 색이 곧 사실이다(§Research 5-2).
- [ ] 여기까지 새 자산·새 코드 0.

## Step 3 — 세로선: 셰이더
- [x] `Assets/Shaders/AnimeFilmScratch.shader` 작성. 이름: `Hidden/AnimeFilmScratch`.
- [x] 구조 — 기존 `EnemySpawnWarp.shader`와 같은 손으로 쓴 HLSL. 풀스크린 정점은 URP의 `Blit.hlsl`(`Vert` / `Varyings`)을 그대로 쓴다.
- [x] **⚠ 화면을 읽지 않는다.** 선만 그려 **하드웨어 블렌드로 얹는다**(`Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off`, `ZTest Always`, `Cull Off`). 그래야 Step 4에서 `fetchColorBuffer`를 끌 수 있고 **전체 화면 복사 패스가 통째로 사라진다**.
- [x] 알고리즘 — 곱해지는 마스크 셋. 새 개념이 없다:
  1. **열 선택** `col = floor(uv.x * _Columns)` — 화면을 `_Columns`(기본 220)칸으로 나눈 세로 띠. 칸 안에서의 위치로 선 굵기를 만든다(`_LineWidth`, 픽셀이 아니라 칸 대비 비율이라 해상도가 바뀌어도 굵기가 따라온다).
  2. **간헐성** `slot = floor(_Time.y * _FlickerRate)` (기본 12 = 초당 12칸, 24fps 필름의 2프레임). `hash(col, slot) < density`인 열만 그 슬롯 동안 보인다. **`_Time.y` 하나로 "가끔씩"이 전부 표현된다** — 난수 생성기도, 코루틴도, 상태 필드도 없다.
     - **⚠ 노브는 확률이 아니라 `_ScratchesPerSecond`다**(기본 0.6 = 평균 1.7초에 한 줄, `density = _ScratchesPerSecond / (columns × rate)`). 확률로 두면 쓸 만한 값이 0.0002대라 슬라이더로 손댈 수가 없다. **구현 후 사용 중에 드러나 고친 지점이다.**
     - **⚠ 해시의 마지막 연산이 곱이면 안 된다** — 균일한 두 값의 곱은 0 근처에 몰려(`P(xy < t) = t(1 - ln t)`) 작은 문턱에서 7배쯤 자주 걸린다. 실측: 목표 0.6/초에 실제 4.32/초였다. 마지막이 합인 해시로 바꾸니 0.3/0.6/2/6 전부 오차 5% 안에 들어왔다.
  3. **외곽 한정** `edge = smoothstep(_EdgeStart, 1.0, abs(uv.x * 2 - 1))` (기본 `_EdgeStart` 0.62). 화면 중앙은 0이라 **패턴인풋 주변에는 절대 안 뜬다**.
  4. (선택) **세로 구간 자르기** — 같은 해시로 `y` 시작·끝을 뽑아 화면을 다 관통하지 않는 짧은 긁힘도 섞는다. **Step 3에서는 넣지 않는다**(전체 관통이 필름 긁힘의 기본형이고, 부족하면 그때 한 줄 추가).
- [x] 출력 = `_LineColor`(기본 흰색에 가까운 밝은 회색) · `alpha = 위 마스크들의 곱 × _Intensity`(기본 0.35).
- [x] 프로퍼티는 **전부 인스펙터에 노출**한다(`_Columns` `_LineWidth` `_FlickerRate` `_ScratchesPerSecond` `_EdgeStart` `_Intensity` `_LineColor`). ⚠ 실물 튜닝 값을 코드가 못 정한다 — 화면을 봐야 정해지는 값이다.

## Step 4 — 세로선: 머티리얼 + 렌더러 배선
- [x] `Assets/Shaders/Materials/AnimeFilmScratchMaterial.mat` 생성(셰이더 = `Hidden/AnimeFilmScratch`).
- [x] `PC_Renderer.asset`에 **`Full Screen Pass Renderer Feature`** 추가. 이름 `FilmScratch`, **리스트 맨 뒤**(`WatercolorFx` · `FlowFx` 결과 위에 얹힌다).
  - `Pass Material` = 위 머티리얼
  - `Injection Point` = **`After Rendering Post Processing`** (채도 보정을 거친 화면 위에 긁힘이 얹힌다 — 필름은 이미 색이 정해진 뒤에 긁힌다)
  - `Fetch Color Buffer` = **끔** ⚠ 켜면 매 프레임 화면 복사가 붙는데 이 셰이더는 화면을 안 읽는다
  - `Requirements` = `None`, `Bind Depth-Stencil` = 끔
- [x] `Mobile_Renderer.asset`에도 같은 피처를 **같은 이름·같은 값**으로 추가. ⚠ 이름이 다르면 Step 5의 끄기가 한쪽에서만 듣는다(`SetFeatures`가 이름으로 찾는다).

## Step 5 — 마무리 실루엣(§14) 동안 필름 룩을 끈다
`FinaleSilhouetteDirector`에만 손댄다. **새 상태 필드도, 새 복구 경로도 안 만든다** — `Restore()`가 이미 노출 종료 · 곡 중단 · `OnDisable` 셋에서 불린다.

- [x] 필드 둘 추가:
  - `[SerializeField] private Volume filmLookVolume;` — 비면 이 층만 조용히 죽는다(기존 배선 규율).
  - `[SerializeField] private string scratchFeatureName = "FilmScratch";`
- [x] `SetFeatures(bool on)` 안, 기존 이름 비교 옆에 한 줄:
  - 이름이 `scratchFeatureName`이면 `feature.SetActive(!on)` — **부호가 반대다**. 실루엣이 올라가면 긁힘이 내려간다.
  - ⚠ 기존 `continue` 가드를 고쳐야 한다(지금은 배경·배우 이름이 아니면 건너뛴다).
- [x] `Fire()`에서 `restoreFilmWeight = filmLookVolume.weight; filmLookVolume.weight = 0f;`
  - ⚠ **0을 대입하고 1로 되돌리지 않는다.** `restoreTimeScale`·`restoreAudioPitch`와 같은 관용구 — 원래 값을 캐시했다 되돌린다(나중에 이 Volume을 다른 데서 밀 수 있다).
- [x] `Restore()`에서 `filmLookVolume.weight = restoreFilmWeight;`
  - ⚠ `Restore()`는 **여러 번 불려도 안전해야 한다**(헤더에 못박혀 있다). `active` 가드 뒤에 두면 그 성질이 유지된다.
- [x] 씬에서 `renderers[]`에 `PC_Renderer`가 이미 꽂혀 있는지 확인(배경·배우 피처가 그 배열로 돌고 있다). 꽂혀 있으면 긁힘 피처는 **배선 0**이다.
- [x] **⚠ 이렇게 하면 §14의 빨강을 보정할 필요가 없다.** 채도가 안 걸리니 머티리얼 색은 그대로 둔다 — 값 둘을 짝으로 묶지 않는 것이 이 Step의 목적이다.

## Step 6 — 검증
- [ ] `BattleScene` 플레이 → 게임뷰 캡처로 확인할 것 넷:
  1. 화면 전체 채도가 내려갔다.
  2. 세로선이 **좌우 외곽에만** 뜨고 중앙(패턴인풋·포커스 링 영역)에는 안 뜬다.
  3. 선이 **상시가 아니라 간헐적**이다(몇 초에 몇 번).
  4. **포커스 링·HUD가 채도·세로선의 영향을 안 받는다**(`ScreenSpaceOverlay`라 구조적으로 그래야 한다 — 영향을 받으면 Canvas 설정이 바뀐 것이다).
- [ ] **⚠ 마무리 실루엣(§14) 확인** — 마지막 패턴을 성공으로 끝내 빨간 화면을 띄운다. 확인할 것 셋:
  1. 빨강이 **안 바랬다**(Volume weight가 0으로 내려갔다).
  2. 세로선이 **안 뜬다**(피처가 꺼졌다).
  3. 노출이 끝나면 **둘 다 돌아온다**. 이어서 곡을 중단(`Esc`·목숨 0)해도, 플레이를 멈춰도 돌아온다 — **복구 경로 셋을 전부 밟는다**(§14의 *"하나라도 빠지면 게임이 0.1배속으로 굳는다"*와 같은 검증).
- [x] 히트스톱 구간에서 세로선이 계속 흐르는지 확인 — **흐르는 것이 정상이다**(§Research 5-3).

## Step 7 — CLAUDE.md 갱신
- [x] 이 절을 §12(`ComboPostFxView`) 뒤에 짧게 추가. 담을 사실 셋:
  - 채도는 **전용 `FilmLookVolume`**의 `ColorAdjustments`가 소유한다. ⚠ `Global Volume`에 넣으면 §14가 그것을 못 끈다(Bloom·Tonemapping이 딸려 꺼진다).
  - 세로선은 `FullScreenPassRendererFeature` + `AnimeFilmScratch` 머티리얼이 전부다(C# 0줄, `_Time.y` 해시가 간헐성을 만든다).
  - ⚠ 마무리 실루엣(§14)이 **둘 다 끈다** — Volume weight와 피처 `SetActive`, 복구는 기존 `Restore()` 세 경로가 든다.
- [x] §14 항목에도 한 줄 — 이 디렉터가 끄는 대상이 배경·배우 피처 **셋**(긁힘 포함)과 필름 룩 Volume이 됐다는 사실.

---

## 하지 않는 것 (요청받으면 그때)
- 커스텀 `ScriptableRendererFeature` C# — 내장 피처가 같은 일을 한다.
- 커스텀 `VolumeComponent` — 값이 씬마다 달라져야 할 때 비로소 필요하다.
- **카메라의 `renderPostProcessing`을 끄는 방식** — 한 줄이라 더 싸 보이지만 `Tonemapping`(ACES)까지 같이 꺼져 §14의 빨강이 **다른 색으로** 바뀐다. 게다가 카메라를 만지는 일은 `CameraDirector` 관할이다(§7-3의 층 분리).
- 세로선 강도의 콤보 연동 · 곡 구간별 on/off — 켜고 끄는 자리는 Step 5가 이미 만들었다.
- 가로 방향 긁힘 · 먼지 반점 · 프레임 흔들림(게이트 위브) — 필름 룩의 다른 축이고 지금 요구에 없다.
