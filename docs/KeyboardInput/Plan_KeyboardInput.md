# Plan_KeyboardInput.md — 1~9번 키보드 입력 활성화 (v2)

> 기반 문서: `Research_KeyboardInput.md`
> 원인 요약: 코드/액션 바인딩은 완전하지만, `InputHandler` 컴포넌트가 씬에 붙어있지 않아 `PatternHandler`가 이를 찾지 못해 `OnKeyPressed` 구독이 일어나지 않음.
>
> **(v2) 사용자 피드백 반영**: 키보드 입력도 마우스 드래그처럼 라인이 그려져야 하고, 마우스의 "손 뗌"에 해당하는 종료 신호가 없으므로 "일정 시간 입력이 없으면 패턴(스트로크) 완성"으로 대체한다.

## 설계 요약

- 키보드로 눌린 인덱스를 더 이상 `AddPattern`에 직접 연결하지 않고, `patternPoints[index].ForceDown()`을 호출한다. 이는 이미 통과 노드 자동 인식 기능에서 쓰인 것과 동일한 진입점으로, `Point.Down()` → `OnPointDown` 이벤트 → 기존 `PatternHandler.OnPointPressed(index)`를 그대로 태운다. 즉 **라인 추가·통과 노드 판정·`AddPattern` 판정 로직을 한 글자도 새로 만들 필요 없이 그대로 재사용**한다.
- 유일하게 새로 필요한 것은 "지금 진행 중인 스트로크가 마우스 드래그인지 키보드 입력인지"에 따라 **종료 조건**을 다르게 판단하는 것뿐이다.
  - 마우스: 기존과 동일하게 `Mouse.current.leftButton.isPressed`가 꺼지면 종료.
  - 키보드: 마지막 키 입력 후 `keyboardInputTimeout`초 동안 새 입력이 없으면 종료(= 패턴 완성/스트로크 종료).
- 스트로크 시작 시점(`!IsDragging`이었다가 새로 노드가 연결되는 순간)에 "그 순간 마우스 왼쪽 버튼이 눌려있는가"로 이번 스트로크가 마우스 스트로크인지 키보드 스트로크인지를 1회 판별해 고정한다(`isKeyboardStroke` 플래그). 이후 종료(`EndDrag`) 로직(라인 페이드아웃, busy 초기화 등)은 마우스/키보드 공통으로 그대로 재사용한다.

## 단계별 구현 계획

### Step 1 — `InputHandler` 컴포넌트를 씬에 부착 (기존 Step, 변경 없음)
- [x] `PatternHandler`가 붙어있는 `PointBackground` GameObject에 `InputHandler` 컴포넌트를 추가 (Unity Editor/MCP)
- [x] `InputHandler.cs`/`IngameInputs.inputactions` 코드 변경 없음

### Step 2 — `PatternHandler`에 스트로크 소스 구분 상태 추가
- [x] `[SerializeField] private float keyboardInputTimeout = 0.5f;` 필드 추가
- [x] `private bool isKeyboardStroke;`, `private float lastKeyboardInputTime;` 필드 추가

### Step 3 — 키보드 입력을 기존 파이프라인에 연결
- [x] `Start()`/`OnDestroy()`에서 `inputHandler.OnKeyPressed += AddPattern;` / `-= AddPattern;` 를 제거하고, 새 메서드 `OnKeyboardInput`으로 교체
- [x] `private void OnKeyboardInput(int index)`:
  - `lastKeyboardInputTime = Time.time;`
  - `patternPoints[index].ForceDown();` 호출 (busy 상태면 `ForceDown()` 내부 가드로 자동 무시되어 중복 처리 없음)

### Step 4 — 스트로크 시작 시 소스 판별
- [x] `OnPointPressed(int index)`의 `if (!IsDragging) { connectedIndices.Clear(); ... }` 분기에서, 새 스트로크 시작 시점의 `isKeyboardStroke`를 결정:
  - `isKeyboardStroke = !(Mouse.current != null && Mouse.current.leftButton.isPressed);`
  - (마우스 버튼이 눌려있으면 마우스 스트로크, 아니면 키보드 스트로크로 간주)
  - 키보드 스트로크로 시작하는 경우 `lineRenderer?.ClearLiveEndPoint();`도 함께 호출해 이전 마우스 스트로크의 잔여 커서추적 좌표가 남지 않도록 함

### Step 5 — 종료 조건 분기 (`Update()`)
- [x] `Update()`를 다음과 같이 분기:
  - `if (!IsDragging) return;`
  - `isKeyboardStroke`가 true면: `if (Time.time - lastKeyboardInputTime >= keyboardInputTimeout) EndDrag();` 만 수행 (커서 추적 없음)
  - false면: 기존 마우스 로직 그대로 (버튼 뗌 감지 → `EndDrag()`, 드래그 중 `SetLiveEndPoint` 갱신)

### Step 6 — 회귀/정합성 확인
- [x] 마우스 드래그 경로는 `isKeyboardStroke = false`로 시작하므로 기존 동작(커서 추적선, 버튼 뗌 종료)이 그대로 유지되는지 코드 리뷰로 확인
- [x] 키보드로 예: `1`→`9` 입력 시, 기존 통과 노드 로직(`GetPassThroughIndex`)이 그대로 적용되어 `5`번이 자동으로 라인/판정에 포함되는지 확인 (동일 파이프라인 재사용이므로 자연히 적용됨)
- [x] 컴파일 및 Play 모드 진입 시 콘솔 에러 없는지 확인 (에러 없음)
- [x] MCP로는 실제 키 입력을 시뮬레이션할 수 없으므로, 실제 키보드 입력을 통한 최종 시각 확인(라인이 그려지는지, `keyboardInputTimeout` 후 자연스럽게 페이드아웃되는지)은 사용자가 에디터에서 직접 수행 필요

## 진행 상태 표기 규칙
구현 시작 후 각 항목을 완료할 때마다 `- [ ]` → `- [x]`로 갱신하며, 전 단계가 끝날 때까지 중단 없이 진행합니다.

>>> 여기에 피드백을 남겨주세요.
