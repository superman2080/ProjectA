# Plan — 캔버스 파티클 이펙트 시스템 (CanvasEffect)

근거 문서: [Research_CanvasEffect.md](./Research_CanvasEffect.md)

## 확정 설계 요약

- `EffectManager`(신규)가 `PatternHandler`의 기존 이벤트만 구독 → 카탈로그 매핑 → 좌표 변환 → 재생. `PatternHandler`는 **수정하지 않는다**.
- 데이터 기반 카탈로그(`List<EffectEntry>`), EffectManager **자체 풀링**(전역 `PoolKey` 미사용), `IPoolable` 패턴 준수.
- 렌더 레이어 분리: `AmbientEffectLayer`(뒤), `EffectOverlayLayer`(앞).
- 앰비언트: 상시 루프 + `SetIntensity(float)` 공개 API(계산은 추후).
- 판정 트리거 소스: `OnFallingNodeResolved`.

---

## 구현 단계 (Step)

### 사전 — 패키지
- [x] **Step 0: UIParticle 패키지 설치**
  - `Packages/manifest.json`에 `com.coffee.ui-particle` (UPM Git URL `https://github.com/mob-sakai/ParticleEffectForUGUI.git`) 추가 완료.
  - (대기) Unity가 에디터 포커스 시 자동 리졸브 → `UIParticle` 컴포넌트 사용 가능 여부는 에디터에서 확인 필요.

### 데이터/뷰 계층
- [x] **Step 1: 트리거 enum + 카탈로그 엔트리 정의**
  - `Assets/02. Scripts/Effect/EffectCatalog.cs` — `EffectTrigger` enum + `EffectEntry` struct.
  - `EffectTrigger` enum: `Perfect`, `Good`, `Miss`, `PatternCompleteFull`, `PatternComplete`, `NodeConnected`.
  - `[Serializable] struct EffectEntry { EffectTrigger trigger; GameObject prefab; int initialSize; int maxSize; }`.
  - 파일 위치: `Assets/02. Scripts/Effect/` (신규 폴더).

- [x] **Step 2: `CanvasEffectView` 구현 (`IPoolable`)** — `Assets/02. Scripts/Effect/CanvasEffectView.cs`
  - 루트에 `UIParticle` + 하위 `ParticleSystem`(들)을 가진 프리팹의 래퍼.
  - `OnSpawn()`: 활성화 + 파티클 재생(`Play`).
  - 재생 종료 감지: `ParticleSystem.main.duration`(+startLifetime) 경과 또는 `IsAlive(true)` 폴링 → 자신을 소유 매니저에 **자동 반환**(콜백/이벤트로 반환 요청).
  - `OnDespawn()`: 파티클 정지·클리어 + 비활성화 + 반환 이벤트 해제.
  - 위치 지정 API: `SetLocalPosition(Vector2)` (오버레이 레이어 로컬 좌표).

### 매니저 계층
- [x] **Step 3: `EffectManager` — 풀링 골격** — `Assets/02. Scripts/Effect/EffectManager.cs` (BuildPools/CreateView/GetView/ReturnView, WorldToLocal 자체 보유)
  - 인스펙터: `List<EffectEntry> catalog`, `RectTransform overlayLayer`, `RectTransform ambientLayer`, 앰비언트 프리팹/개수, `PatternHandler handler` 참조.
  - `Start()`에서 카탈로그를 순회해 프리팹별 `Queue<CanvasEffectView>`를 `initialSize`만큼 미리 생성(비활성).
  - `Get(prefab)`/`Return(prefab, view)` 내부 메서드. `maxSize` 초과 반환분은 파기(또는 상한까지만 보관).
  - 좌표 변환: `PatternHandler.WorldToLocal`과 동일 로직을 자체 보유(`canvas`/`canvasCamera`는 `GetComponentInParent<Canvas>()`).

- [x] **Step 4: `EffectManager` — 이벤트 구독 & 재생 배선** (OnFallingNodeResolved→결과별 트리거, OnNodeConnected, OnPatternComplete→allCorrect 분기)
  - `OnEnable`/`Start`에서 구독, `OnDisable`/`OnDestroy`에서 해제:
    - `handler.OnFallingNodeResolved += (index, type, worldPos, result)` → result에 따라 `Perfect/Good/Miss` 트리거로 재생.
    - `handler.OnPatternComplete += allCorrect` → `PatternCompleteFull`/`PatternComplete`. (위치: 패턴 중심 또는 화면 중앙 — 인스펙터 지정)
    - `handler.OnNodeConnected += (index, worldPos)` → `NodeConnected` 트리거로 재생.
  - 재생 흐름: 트리거 → 카탈로그에서 엔트리 조회(없으면 무연출) → 프리팹 풀에서 `Get` → 월드좌표→오버레이 로컬 변환 후 `SetLocalPosition` → `OnSpawn`.

### 앰비언트 계층
- [x] **Step 5: `AmbientEffectController` + 상시 루프 배치** — `Assets/02. Scripts/Effect/AmbientEffectController.cs` + `EffectManager.SpawnAmbients()`/`SetIntensity()`
  - `AmbientEffectController`: 앰비언트 프리팹에 붙는 컴포넌트. `SetIntensity(float 0~1)` — emission rate / 색 보간 등 파티클 파라미터 조정. 기본 intensity = 0.
  - `EffectManager.Start()`에서 `ambientLayer`에 상시 루프 인스턴스를 지정 개수만큼 배치(풀링 X, 계속 재생).
  - `EffectManager.SetIntensity(float)` 공개 API → 배치된 `AmbientEffectController`들에 전달. (실제 호출자는 추후 점수 플랜에서 연결)

### 씬/프리팹 배선 (Unity MCP로 완료)
- [x] **Step 6: 씬 구성**
  - `Canvas` 하위에 `AmbientEffectLayer`(형제 인덱스 0 = 뒤), `EffectOverlayLayer`(마지막 형제 = 앞) 풀스트레치 RectTransform 컨테이너 생성. `EffectManager` GameObject도 `Canvas` 하위 배치(GetComponentInParent<Canvas> 위해).
  - `EffectManager` 인스펙터 참조 연결: `handler`(PointBackground의 PatternHandler), `overlayLayer`, `ambientLayer`, `catalog`(5행), `ambientPrefabs`(1개).
  - 검증용 임시 UIParticle 프리팹 6종 제작(`Assets/03. Prefabs/Effects/`): `fx_perfect`/`fx_good`/`fx_miss`/`fx_connect`/`fx_complete_full`(one-shot+CanvasEffectView), `fx_ambient`(AmbientEffectController). 공용 머티리얼 `FX_Particle.mat`(`Sprites/Default`, URP 호환).
  - **URP 이슈 해결 기록**: 빌트인 `Default-ParticleSystem` 셰이더는 URP에서 빨갛게 깨짐 → `Sprites/Default` 머티리얼로 교체. UIParticle `m_Scale3D` 기본값 (10,10,10)이 파티클을 거대·희소하게 만듦 → (1,1,1)로 수정.

### 검증 (Unity MCP로 완료)
- [x] **Step 7: 동작 검증**
  - Play 모드 스크린샷으로 확인: Perfect(금)·Good(초록)·NodeConnected(시안)가 각 Point 위치에 정확히, PatternCompleteFull(금)이 화면 중앙에 재생. Miss(회색)도 트리거됨(배경과 색이 겹쳐 육안 구분만 약함).
  - 앰비언트 흰 반짝임이 배경(패턴인풋·캐릭터 뒤) 전체에 상시 재생 확인.
  - 이벤트 구독 확인: `OnFallingNodeResolved`/`OnNodeConnected`/`OnPatternComplete` 각각 구독자 1개(EffectManager). 풀 프리웜 인스턴스 21개 재사용.
  - `SetIntensity(1f)` 호출 시 앰비언트 emission rate가 40으로 상승(반응형 API 동작) 확인.
  - 컴파일·런타임 콘솔 에러/경고 0.

---

## 범위 밖 (이번 플랜에서 하지 않음)

- 점수·콤보 시스템 및 그에 따른 intensity 자동 계산 → 추후 별도 플랜(`SetIntensity` API만 뚫어둠).
- 실제 파티클 아트/프리팹 디자인(임시 프리팹으로 파이프라인만 검증).
- `PatternHandler` 판정/입력 로직 변경(구독만 하고 건드리지 않음).

---

## 피드백
> 이 플랜에 대한 피드백은 아래에 `>>>` 로 남겨주세요. 반영해 재작성하겠습니다.
