# Research_KeyboardInput.md — 1~9번 키보드 입력

## 목적
현재 1~9 숫자 키로 패턴 입력이 실제로 동작하지 않는 것으로 보임. 관련 코드와 씬 배선을 분석해 원인을 찾는다.

## 관련 코드 분석

### `Assets/02. Scripts/Input/InputHandler.cs`
- `IngameInputs`(Input System 자동생성 클래스)의 `Player` 액션맵에서 `Input1`~`Input9` 액션을 찾아 `performed`/`canceled` 콜백을 바인딩
- `performed` 시 `inputs[index] = true` + `OnKeyPressed?.Invoke(index)` 발행
- 코드 자체는 완전히 구현되어 있고 문제 없음

### `Assets/IngameInputs.inputactions`
- `Player` 맵에 `Input1`~`Input9` 액션이 있고, 각각 `<Keyboard>/1` ~ `<Keyboard>/9` 에 바인딩되어 있음 — 액션 정의/바인딩 자체는 정상

### `Assets/02. Scripts/UI/PatternHandler.cs`
- `Start()`에서 `inputHandler = gameObject.GetComponent<InputHandler>();` — **자기 자신(PatternHandler가 붙은 GameObject)에서** `InputHandler` 컴포넌트를 찾음
- `if (inputHandler != null) inputHandler.OnKeyPressed += AddPattern;` — 찾았을 때만 구독
- 키보드 입력은 `AddPattern(index)`를 **직접** 호출 — `Point`를 거치지 않으므로 `OnPointPressed`(라인 그리기, 통과 노드 판정 등)는 실행되지 않음. 이는 `Research_PatternPassThrough.md`에서도 이미 확인된 기존 설계 의도임(마우스 드래그 시에만 라인이 그려짐).

### 씬 배선 확인 (`Assets/01. Scenes/DefaultScene.unity`, 텍스트 검색)
- 씬 파일 전체에서 `InputHandler` 스크립트를 참조하는 컴포넌트가 **전혀 없음** (`grep "InputHandler"` 결과 0건)
- 즉 `InputHandler` 컴포넌트가 어떤 GameObject에도 붙어있지 않음
- `PatternHandler`가 붙은 `PointBackground`에도 `RectTransform/CanvasRenderer/Image/PatternHandler`만 있고 `InputHandler`는 없음

## 결론 (원인)
1~9 키 입력 코드/바인딩 자체는 완전하지만, **`InputHandler` 컴포넌트가 씬 어디에도 붙어있지 않아** `PatternHandler.Start()`의 `GetComponent<InputHandler>()`가 항상 `null`을 반환하고, 그 결과 `OnKeyPressed` 구독이 아예 일어나지 않는다. 따라서 키보드 입력은 현재 아무 효과가 없다.

## 확인이 필요한 미결 사항
1. **최소 수정**: `PatternHandler`와 같은 GameObject(`PointBackground`)에 `InputHandler` 컴포넌트만 추가하면 기존 코드 그대로 정상 동작한다. 코드 변경은 필요 없음.
2. **(선택) 시각 효과 연동 여부**: 키보드로 입력해도 마우스 드래그처럼 해당 Point가 눌린 것처럼 보이거나 라인이 그려지길 원하는지? 현재 설계(및 이전 Research_PatternPassThrough.md)는 "키보드 입력은 판정만 하고 시각효과는 없음"을 전제로 하고 있음 — 이번 Plan에서는 이 기존 전제를 유지하고 **최소 수정(컴포넌트 추가)만** 하는 것으로 가정. 시각 연동이 필요하면 별도 피드백 필요.
