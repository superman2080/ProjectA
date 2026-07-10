# Research_KeyboardStroke.md — 키보드 스트로크 종료 조건 변경

## 문제 정의

현재 키보드 스트로크는 **마지막 키 입력 이후 일정 시간(`keyboardInputTimeout`)이 지나면** 종료된다.  
이 방식은 음악 리듬과 무관한 임의의 타임아웃이라, 패턴을 모두 입력하기 전에 끊기거나 반대로 너무 오래 열려 있을 수 있다.

요청 사항: **현재 곡의 활성 패턴(`nowPattern`)이 존재하는 동안은 계속 입력을 받고, 패턴이 완료되면 드래그가 자연 종료**되도록 변경한다.

---

## 관련 코드 분석

### `PatternHandler` (`Assets/02. Scripts/UI/PatternHandler.cs`)

**키보드 입력 처리 흐름:**

```
OnKeyboardInput(index)
  └─ lastKeyboardInputTime = Time.time
  └─ patternPoints[index].ForceDown()
       └─ OnPointPressed(index)
            └─ isKeyboardStroke = true (마우스 버튼 안 눌린 경우)
            └─ IsDragging = true
            └─ AddPattern(index) → nowPattern.Input() → OnExit → HandlePatternComplete
```

**현재 타임아웃 로직 (`Update`, L118~L124):**
```csharp
if (isKeyboardStroke)
{
    if (Time.time - lastKeyboardInputTime >= keyboardInputTimeout)
        EndDrag();
    return;
}
```

**패턴 완료 시 (`HandlePatternComplete`, L389~L396):**
```csharp
nowPattern.OnExit -= HandlePatternComplete;
OnPatternComplete?.Invoke(allCorrect);
nowPattern = null;          // ← nowPattern이 null이 됨
TriggerLineFadeOut();       // connectedIndices.Clear() 포함
ClearFallingNodes();
// IsDragging은 여전히 true — EndDrag()가 호출되지 않음
```

**관찰:**
- 패턴이 완료돼도 `IsDragging`은 true로 남아 있음 — 현재는 `keyboardInputTimeout` 후에 `EndDrag()`가 뒤늦게 정리함
- 새 방식에서는 `nowPattern == null`이 되는 순간 `Update()`에서 `EndDrag()`를 호출하면 됨

### 제거 가능한 요소

| 요소 | 현재 역할 | 제거 후 |
|------|-----------|---------|
| `[SerializeField] float keyboardInputTimeout` | 타임아웃 임계값 | 불필요 |
| `float lastKeyboardInputTime` | 마지막 키 입력 시각 | 불필요 |
| `Update()` 타임아웃 조건 | 스트로크 종료 트리거 | `nowPattern == null` 조건으로 대체 |

---

## 변경 범위

| 파일 | 변경 내용 |
|------|-----------|
| `PatternHandler.cs` | `keyboardInputTimeout`, `lastKeyboardInputTime` 제거; `Update()` 키보드 종료 조건 변경 |

다른 파일 **무수정**.
