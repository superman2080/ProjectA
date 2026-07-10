# Research.md — 패턴 연결선(Line) 객체

## 목적
패턴인풋(3x3, 9개 Point)에서 플레이어가 노드를 드래그하여 이을 때, 지나온 노드들을 잇는 **선(Line) 시각 효과**를 구현하기 위한 기존 코드/구조 분석.

## 관련 파일 분석

### `Assets/02. Scripts/Pattern/Handler/Point.cs`
- `MonoBehaviour`, `IPointerDownHandler`, `IPointerUpHandler`, `IPointerEnterHandler` 구현.
- uGUI 기반 (RectTransform, `PointerEventData` 사용) — 3D 월드 오브젝트가 아니라 UI 요소.
- `index` (0~8), `handler`(PatternHandler 참조) 보유.
- `isBusy` 로 현재 눌림 상태 관리. `ResetBusy()`로 외부에서 리셋 가능.
- `OnPointerEnter`: `handler.IsDragging && !isBusy` 일 때 `Down()` 호출 → **드래그 중 다른 노드 위로 진입 시 자동으로 눌림 처리**되는 방식 (모바일 잠금패턴과 동일한 UX).
- `Down()` / `Up()` 시 `OnPointDown`/`OnPointUp` 이벤트(Action<int>) 발행. **현재 이 이벤트들을 구독하는 곳은 `PatternHandler` 뿐** (선 렌더링용 구독자 없음).
- 노드의 위치 정보(RectTransform.anchoredPosition)에 대한 직접 접근자는 없음 — 필요시 `transform`(RectTransform)으로 외부에서 접근 가능.

### `Assets/02. Scripts/UI/PatternHandler.cs`
- `Point[9] patternPoints` 보유, 전체 패턴 입력의 중앙 관리자.
- `IsDragging` (public get) — 현재 드래그 중인지 여부. `OnPointPressed`(Down 시 true) / `EndDrag()`(Up 또는 Update에서 마우스 감지 실패 시 false)로 관리.
- `Update()`에서 매 프레임 `Mouse.current.leftButton.isPressed` 체크 — 포인트 밖에서 마우스를 뗀 경우도 드래그 종료 처리.
- `AddPattern(int index)`: 이미 구현되어 있음 (CLAUDE.md의 "미구현" 설명은 **outdated** — 현재는 정답/오답 판정 및 `Pattern.Input()` 진행 로직이 존재함). 정오답 여부와 무관하게 매 입력마다 호출됨.
- `OnJudged` (Action<JudgementResult, int>), `OnPatternComplete` (Action<bool>) 이벤트 보유 — 선 색상(정답/오답) 판단에 활용 가능한 판정 이벤트.
- 선 렌더링에 필요한 "현재까지 지나온 인덱스 목록"을 추적하는 로직은 없음.

### `Assets/02. Scripts/Pattern/Pattern.cs`
- ScriptableObject. `PatternData[]` (index, inputTime) 순서대로 진행.
- 선 그리기와 직접적 연관 없음 — 다만 `ExpectedPointIndex`로 "다음에 눌러야 할 노드"를 알 수 있어, 오답 시 선 색을 다르게 표시하는 등에 참고 가능.

### `Assets/02. Scripts/Input/InputHandler.cs`
- 키보드 입력(1~9)을 `PatternHandler.AddPattern`에 직접 연결. 마우스 드래그와는 별개 경로.
- 키보드 입력 시에는 `Point.OnPointDown/Up` 이벤트가 발생하지 않으므로, **키보드 입력만으로는 선이 그려지지 않음** (마우스/터치 드래그 시에만 Point의 Pointer 이벤트 발생).

### 좌표계 (CLAUDE.md 기준)
- 9개 Point는 200px 간격의 3x3 격자, anchoredPosition 기준 로컬 좌표:
  - Point_1(0): (-200,-200), Point_2(1): (0,-200), Point_3(2): (200,-200)
  - Point_4(3): (-200,0), Point_5(4): (0,0), Point_6(5): (200,0)
  - Point_7(6): (-200,200), Point_8(7): (0,200), Point_9(8): (200,200)
- 모두 같은 부모(PatternHandler가 속한 Canvas/패널) 하위에 배치된 UI 요소로 추정.

### 선(Line) 렌더링 관련 기존 자산
- 프로젝트 내 `LineRenderer`, `UILineRenderer` 등 선 그리기 관련 스크립트/프리팹 **없음** (신규 구현 필요).
- UI 컨텍스트이므로 3D `LineRenderer`보다는 **UI Image(회전/스케일로 세그먼트 그리기)** 또는 **UGUI용 커스텀 `Graphic`(예: `MaskableGraphic` 기반 커스텀 메시)** 방식이 적합.
- 외부 에셋(`99. External Assets`)에는 캐릭터/환경 관련 에셋만 있고 UI 라인 관련 에셋 없음.

## 확인이 필요한 미결 사항 (Plan 작성 전 가정)
1. 드래그 중 손가락(마우스)이 노드 사이 빈 공간에 있을 때, 마지막 노드에서 현재 커서 위치까지 이어지는 "living line"을 그릴지 여부 — 잠금패턴 UX 상 일반적으로 필요.
2. 정답/오답에 따라 선 색상을 다르게 할지 여부 (`OnJudged` 활용 가능).
3. 패턴 완료/드래그 종료 시 선을 즉시 지울지, 잠시 유지 후 페이드 아웃할지.
4. 선 두께, 색상 등 비주얼 스펙은 미정 (기획 확인 필요) → Plan에서는 SerializeField로 노출.
