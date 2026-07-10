# Plan.md — 패턴 연결선(Line) 객체 구현

> 기반 문서: `Research_PatternLine.md`
> 사용자 결정사항:
> 1. 드래그 중 마지막 노드 → 현재 커서 위치까지 이어지는 실시간 선 표시
> 2. 정답/오답에 따라 선 색상 다르게 표시 (Perfect/Good = 기본색, Miss = 경고색)
> 3. 드래그/패턴 종료 시 잠시 유지 후 페이드아웃
> 4. 렌더링 방식: 커스텀 UGUI `Graphic`(메시 기반)
> 5. (v2 피드백) 추후 UI에 파티클/이펙트가 추가될 수 있도록, 라인 관련 핵심 상태 변화 지점에 **이벤트 기반 확장 포인트**를 노출하는 방향으로 설계

## 설계 요약

- 새 클래스 `PatternLineRenderer` : `MaskableGraphic`을 상속받아 `OnPopulateMesh`로 좌표 리스트를 잇는 폴리라인(사각형 세그먼트 조합) 메시를 직접 그린다.
- `PatternHandler`가 드래그 중 지나온 Point들의 로컬 좌표 리스트를 관리하고, 매 프레임 `PatternLineRenderer`에 좌표 리스트(+ 현재 커서 좌표)를 갱신 요청한다.
- 색상 상태(`allCorrect`)는 `PatternHandler`에 이미 존재하는 필드를 재사용 — Miss 발생 시 라인 전체 색을 경고색으로 전환(세그먼트별 개별 색은 범위 밖).
- 드래그 종료(`EndDrag`) 또는 패턴 완료(`HandlePatternComplete`) 시 좌표 리스트를 즉시 비우지 않고, `PatternLineRenderer`에 페이드아웃 코루틴을 트리거 → 알파 0으로 서서히 감소 후 클리어.

### 확장성 설계 원칙 (파티클/이펙트 대비)

이번 스코프는 **선(Line) 렌더링까지만** 구현하고 파티클/이펙트 자체는 만들지 않는다. 다만 나중에 별도 컴포넌트(예: `PatternLineEffects`)가 아래 이벤트를 구독해서 파티클을 붙일 수 있도록, `PatternLineRenderer`와 `PatternHandler`가 핵심 상태 변화 시점마다 **C# 이벤트(Action)를 발행**하도록 설계한다.

- 원칙 1: 이펙트 관련 코드는 `PatternLineRenderer`/`PatternHandler` 내부에 넣지 않는다. 두 클래스는 "무슨 일이 일어났는지"만 이벤트로 알리고, "그 순간 무엇을 보여줄지"는 전적으로 구독자(미래의 이펙트 컴포넌트) 책임으로 분리한다.
- 원칙 2: 이벤트 payload는 이펙트가 월드/스크린 공간에 파티클을 스폰할 수 있도록 **로컬 좌표뿐 아니라 월드 좌표(또는 스크린 좌표)** 도 함께 전달한다 (uGUI 로컬 좌표만으로는 파티클 배치가 애매해짐).
- 원칙 3: 지금 당장 구독자가 없어도(= no-op) 이벤트 발행 자체는 성능에 영향이 없도록 null 체크만 하는 가벼운 `Action`/`Action<T>` 필드로 구현 (UnityEvent 직렬화는 지금 범위에서는 불필요 — 인스펙터 노출이 필요해지면 다음 단계에서 UnityEvent로 교체 가능).

## 단계별 구현 계획

### Step 1 — `PatternLineRenderer` 컴포넌트 신설
- [x] 파일: `Assets/02. Scripts/UI/PatternLineRenderer.cs` (네임스페이스 없음, 기존 `PatternHandler` 관례 따름)
- [x] `MaskableGraphic` 상속, `[RequireComponent(typeof(RectTransform))]`
- [x] `SerializeField float lineWidth = 8f`
- [x] `SerializeField Color normalColor`, `SerializeField Color missColor`
- [x] 내부 상태: `List<Vector2> points` (라인 렌더러 자신의 로컬 좌표계 기준), `bool useMissColor`
- [x] `public void SetPoints(IReadOnlyList<Vector2> pts)` — 좌표 리스트 갱신 + `SetVerticesDirty()`
- [x] `public void SetLiveEndPoint(Vector2 pt)` / `public void ClearLiveEndPoint()` — 커서 추적용 마지막 임시 좌표
- [x] `public void SetCorrectState(bool allCorrect)` — 색상 전환 + `SetVerticesDirty()` (색상은 vertex color로 처리하거나 material color로 처리)
- [x] `OnPopulateMesh(VertexHelper vh)` : `points`(+live end point 있으면 추가) 를 순회하며 각 세그먼트를 두께 `lineWidth`의 사각형(quad)으로 생성. 최소 2개 좌표 없으면 `vh.Clear()`만 수행.
- [x] `public void FadeOutAndClear(float duration)` — 코루틴으로 알파 1→0 보간 후 `points.Clear()`, `SetVerticesDirty()`
- [x] **(확장 포인트)** 이펙트 훅용 public 이벤트 추가:
  - `public event Action<Vector2> OnSegmentPointAdded` — `SetPoints`로 새 좌표가 추가될 때마다 마지막 좌표(로컬)를 전달 (선분이 늘어나는 시점 = 파티클 스폰 후보 지점)
  - `public event Action OnFadeStarted` / `public event Action OnFadeCompleted` — `FadeOutAndClear` 시작/종료 시점

### Step 2 — `PatternHandler`에 좌표 추적 로직 추가
- [x] `SerializeField PatternLineRenderer lineRenderer` 필드 추가
- [x] 드래그 시작 시(`OnPointPressed`) 좌표 리스트 초기화 후 첫 노드 좌표 추가
- [x] 이후 `Point.OnPointDown`이 새 인덱스로 호출될 때마다(= 드래그 중 다른 노드 진입) 해당 노드 좌표를 리스트에 append, `lineRenderer.SetPoints(...)` 호출
- [x] 각 `Point`의 화면 좌표를 `lineRenderer`의 RectTransform 로컬 좌표로 변환하는 헬퍼 메서드 작성 (`RectTransformUtility.WorldToScreenPoint` → `RectTransformUtility.ScreenPointToLocalPointInRectangle`)
- [x] 이미 리스트에 있는 인덱스로 재진입 시 중복 추가 방지 (연속 동일 인덱스만 스킵하면 충분 — 뒤로가는 케이스는 요구사항 범위 밖으로 간주)
- [x] **(확장 포인트)** `public event Action<int, Vector3> OnNodeConnected` 추가 — 새 노드가 라인에 연결될 때마다 `(index, 해당 Point의 월드 좌표)` 를 전달. 향후 이펙트 컴포넌트가 이 이벤트만 구독하면 노드 연결 시점마다 월드 좌표에 파티클을 스폰할 수 있음.

### Step 3 — 실시간 커서 추적선
- [x] `PatternHandler.Update()`에 `IsDragging`인 동안 매 프레임 `Mouse.current.position`을 읽어 로컬 좌표로 변환 후 `lineRenderer.SetLiveEndPoint(...)` 호출
- [x] 드래그 종료 시 `lineRenderer.ClearLiveEndPoint()` 호출

### Step 4 — 정답/오답 색상 반영
- [x] `AddPattern(int index)` 내 기존 `allCorrect = false;` 대입 지점(오답, Miss) 두 곳에서 `lineRenderer.SetCorrectState(false)` 호출 추가
- [x] `SetPattern(Pattern pattern)`에서 `allCorrect = true` 초기화 시점에 `lineRenderer.SetCorrectState(true)`도 함께 호출(다음 패턴 대비 리셋)
- [x] **(확장 포인트)** 오답/Miss 전환 시점은 이미 `PatternHandler.OnJudged` 이벤트로 노출되어 있음(Research 문서 확인) — 별도 신규 이벤트 없이 기존 `OnJudged`를 이펙트 컴포넌트가 함께 구독하면 "오답 시 다른 이펙트" 구현 가능. Plan상 신규 작업 없음(기존 이벤트 재사용 명시만).

### Step 5 — 드래그/패턴 종료 시 페이드아웃
- [x] `EndDrag()` 호출 시 좌표 리스트를 즉시 지우는 대신 `lineRenderer.FadeOutAndClear(fadeDuration)` 호출로 변경 (`fadeDuration`은 `PatternHandler`에 `SerializeField float lineFadeDuration = 0.2f` 추가)
- [x] `HandlePatternComplete()`에서도 잔여 라인이 있다면 동일하게 페이드아웃 트리거 (중복 트리거 방지 가드 포함)

### Step 6 — 씬/에디터 배치 (Unity Editor 작업, MCP 필요 시 활용)
- [x] 패턴인풋 패널 하위에 `PatternLineRenderer`를 부착한 UI GameObject(`PatternLine`) 생성 — `PointBackground` 마지막 자식으로 배치해 9개 Point 위(렌더링 순서상 위)에 그려지도록 함
- [x] `PatternHandler` 인스펙터에 `lineRenderer` 참조 연결
- [x] **(확장 포인트)** `PatternLine` 아래에 향후 파티클 이펙트 컴포넌트를 부착할 빈 자식 오브젝트 `EffectAnchor` 생성 (로직 없이 배치만)
- [x] Play 모드 진입 및 콘솔 에러 확인 완료 (에러 없음). 단, 실제 마우스 드래그를 통한 시각적 확인(선 그려짐/커서 추적/색상 전환/페이드아웃)은 MCP로 마우스 입력을 직접 시뮬레이션할 수 없어 **자동 검증하지 못함** — 에디터에서 직접 드래그해 보며 육안 확인 필요
- [x] (부수 발견) 씬 루트에 있던 미사용 `PatternHandler` 오브젝트(빈 `patternPoints`, Start()에서 NRE 발생)를 사용자 확인 후 삭제

## 진행 상태 표기 규칙
구현 시작 후 각 항목을 완료할 때마다 `- [ ]` → `- [x]`로 갱신하며, 전 단계가 끝날 때까지 중단 없이 진행합니다.

>>> 여기에 피드백을 남겨주세요.
