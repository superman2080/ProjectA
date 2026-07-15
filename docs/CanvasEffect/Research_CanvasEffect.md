# Research — 캔버스 파티클 이펙트 시스템 (CanvasEffect)

## 1. 목적

Canvas(UGUI) 위에서 동작하는 파티클/이펙트 연출 시스템을 도입한다. 규모는 "작은 반짝임"부터 "때때로 화려한 파티클 다발"까지 아우른다. 다음 순간에 이펙트를 붙인다:

- **노드 판정** (Perfect / Good / Miss)
- **패턴 완성** (전체 정답 여부)
- **라인에 노드가 이어질 때**
- **배경 앰비언트** (상시 루프 + 상태 반응형 API)

## 2. 렌더링 기술 결정

Unity 기본 `ParticleSystem`은 `MeshRenderer` 기반이라 UGUI Canvas(`Graphic` 기반)에 자연스럽게 얹히지 않는다(렌더 순서·마스킹 문제). 세 방식을 검토했다:

| 방식 | 결론 |
|------|------|
| A. Coffee **UIParticle** (`com.coffee.ui-particle`) | **채택.** ParticleSystem을 Canvas에서 정상 렌더(정렬/마스킹 지원). 작은 반짝임~화려한 다발을 하나의 파티클 데이터로 관리. |
| B. 순수 UGUI 풀링(Image/트윈) | 미채택. 화려한 다발 연출이 결국 파티클 시스템 재발명이 됨. |
| C. 하이브리드 | 미채택. 두 시스템 관리 부담. |

- UIParticle은 UPM Git URL로 `Packages/manifest.json`에 추가한다.
- 각 이펙트 프리팹 = 루트에 `UIParticle` + 하위 `ParticleSystem`(들).

## 3. 관련 기존 코드 분석

### 3.1 `PatternHandler` (`Assets/02. Scripts/UI/PatternHandler.cs`)
이펙트가 구독할 **확장 포인트 이벤트**를 이미 노출한다. 이펙트 도입을 위해 이 클래스를 수정할 필요가 없다(관심사 분리).

- `event Action<JudgementResult,int> OnJudged` — 판정 순간(index만).
- `event Action<int,NodeType,Vector3,JudgementResult> OnFallingNodeResolved` — 낙하 노드를 맞췄을 때. **월드 좌표 + 노드타입 + 결과**를 모두 제공.
- `event Action<int,NodeType,Vector3> OnFallingNodeMissedArrival` — 미입력 자연 도착.
- `event Action<int,NodeType,Vector3> OnFallingNodeSpawned` — 노드 스폰.
- `event Action<bool> OnPatternComplete` — 패턴 완성(true=전체 정답).
- `event Action<int,Vector3> OnNodeConnected` — 라인에 노드 이어질 때(index + 월드 좌표).

**좌표 변환 참고**: `PatternHandler.WorldToLocal(RectTransform, Vector3)` 가 `RectTransformUtility.WorldToScreenPoint` → `ScreenPointToLocalPointInRectangle` 조합으로 월드→레이어 로컬 좌표를 계산한다. `canvas`/`canvasCamera` 참조는 `GetComponentInParent<Canvas>()`로 확보. EffectManager도 동일 로직이 필요하다.

**판정 트리거 소스 결정**: `OnFallingNodeResolved`를 사용한다. 월드 좌표를 이미 제공해 이펙트 위치를 바로 잡을 수 있고, 노드 타입별 분기 여지도 있다. 현재 구조상 판정은 항상 낙하 노드와 함께 발생하므로 `OnJudged` 병행은 불필요.

### 3.2 `Pool` / `PoolKey` (`Assets/02. Scripts/Pool/Pool.cs`)
- `Pool : Singleton<Pool>`. `enum PoolKey`(현재 `FallingNode`만)로 키잉.
- 인스펙터: `SerializedDictionary<PoolKey,GameObject> prefabs`, `SerializedDictionary<PoolKey,int> initialSizes`.
- API: `T Get<T>(PoolKey, Action<T> initializer)`, `Return(PoolKey, IPoolable)`. `Get` 시 `initializer` 실행 후 `OnSpawn()`, `Return` 시 `OnDespawn()` 호출.

**제약**: 키가 `enum`이라 이펙트가 늘 때마다 enum을 늘려야 한다 → "코드 수정 없이 프리팹만 추가" 원칙과 충돌. 따라서 **전역 Pool을 쓰지 않고** EffectManager가 자체 풀을 관리한다(아래 4절). 단 `IPoolable`(`OnSpawn`/`OnDespawn`) 패턴은 그대로 따라 프로젝트 톤을 유지한다.

### 3.3 `IPoolable` / `FallingNodeView` (`Assets/02. Scripts/UI/FallingNodeView.cs`)
- `IPoolable`: `OnSpawn()`(활성화 + 동작 시작), `OnDespawn()`(코루틴 정지 + 이벤트 해제 + 비활성화).
- `FallingNodeView`가 참고 모델: 대여 콜백에서 `Initialize(...)`로 데이터 주입 → `OnSpawn()`에서 코루틴 시작 → 완료 시 `OnArrived` 발행 후 반환. `CanvasEffectView`도 같은 수명주기(재생 시작 → 재생 종료 시 자동 반환)를 따른다.

### 3.4 렌더 순서 규칙 (`PatternHandler`/씬)
- 기존 규칙: 가이드라인 = `PointBackground`의 **첫 자식**(아래), 입력 라인 `PatternLine` = **마지막 자식**(위).
- 이펙트는 이 규칙과 충돌하지 않도록 **전용 컨테이너**를 별도로 둔다:
  - `AmbientEffectLayer` — 패턴인풋 **뒤**(하이어라키 앞쪽, 먼저 렌더).
  - `EffectOverlayLayer` — 판정/라인연결 이펙트 **앞**(하이어라키 뒤쪽, 맨 위).

## 4. 설계 방향 요약 (확정)

- **EffectManager** (신규): `PatternHandler` 이벤트 구독 → 카탈로그에서 프리팹 선택 → 좌표 변환 → 재생. 이펙트의 유일한 관리 지점.
- **데이터 기반 카탈로그**: `List<EffectEntry>` 를 인스펙터에 노출. 한 행 = `{ trigger, prefab, initialSize, maxSize }`. 이펙트 추가 = 행 추가(코드 수정 0). 프리팹 미지정 트리거는 무연출(점진 도입).
- **자체 풀링**: 전역 `PoolKey` enum 대신 EffectManager가 `Dictionary<프리팹, Queue<CanvasEffectView>>`를 카탈로그로부터 `Start()`에서 구성. `IPoolable` 패턴 준수.
- **CanvasEffectView** (신규, `IPoolable`): UIParticle 프리팹 래퍼. 재생 후 스스로 매니저에 반환.
- **앰비언트**: `AmbientEffectLayer`에 상시 루프 인스턴스(풀링 X). `SetIntensity(float 0~1)` 공개 API → `AmbientEffectController.SetIntensity()`. intensity 계산(콤보/점수)은 **추후 별도 플랜**.

## 5. 미결/추후 과제

- 점수·콤보 시스템은 아직 없음. 반응형 앰비언트의 intensity 계산은 이번 범위 밖 — `SetIntensity` API만 뚫어두고 추후 플랜에서 연결.
- 실제 파티클 아트(프리팹) 제작은 시스템 완성 후 별도 진행. 초기엔 임시 프리팹으로 파이프라인 검증.
