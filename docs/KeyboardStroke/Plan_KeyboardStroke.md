# Plan_KeyboardStroke.md — 키보드 스트로크 종료 조건 변경

> 기반 문서: `Research_KeyboardStroke.md`

## 설계 요약

키보드 스트로크 종료 조건을 두 가지로 정의한다:

1. **정상 완료**: 플레이어가 모든 노드를 입력 → `nowPattern == null` → 다음 프레임에 즉시 `EndDrag()`
2. **타임아웃**: 패턴이 남아있어도 마지막 노드의 Good 판정 시간이 지나면 강제 종료
   - `strokeDeadline = patternStartTime + GetInputTime(마지막 노드 인덱스) + goodWindow`
   - `Time.time > strokeDeadline` → `EndDrag()`

임의적인 `keyboardInputTimeout`(키 입력 간 공백 기준)은 완전 제거한다.  
`goodWindow`는 이미 판정 로직에서 사용 중이므로 추가 필드 없이 재사용한다.

---

## 단계별 구현 계획

### Step 1 — 타임아웃 관련 필드 제거

- [x] `PatternHandler.cs`에서 다음 제거:
  - `[SerializeField] private float keyboardInputTimeout = 0.5f;`
  - `private float lastKeyboardInputTime;`

### Step 2 — `strokeDeadline` 필드 추가 및 `SetPattern()`에서 계산

- [x] `private float strokeDeadline;` 필드 추가
- [x] `SetPattern()` 내부에서 `nowPattern.SetInputTimes(inputTimes)` 호출 직후에 계산:
  ```csharp
  int lastIndex = nowPattern.AllData.Count - 1;
  strokeDeadline = patternStartTime + nowPattern.GetInputTime(lastIndex) + goodWindow;
  ```

### Step 3 — `OnKeyboardInput` 단순화

- [x] `OnKeyboardInput(int index)` 에서 `lastKeyboardInputTime = Time.time;` 라인 제거
  - 결과: `patternPoints[index].ForceDown();` 한 줄만 남음

### Step 4 — `Update()` 키보드 종료 조건 변경

- [x] `Update()` 내 키보드 스트로크 블록 변경:
  ```csharp
  // 변경 전
  if (isKeyboardStroke)
  {
      if (Time.time - lastKeyboardInputTime >= keyboardInputTimeout)
          EndDrag();
      return;
  }

  // 변경 후
  if (isKeyboardStroke)
  {
      if (nowPattern == null || Time.time > strokeDeadline)
          EndDrag();
      return;
  }
  ```

---

## 동작 시나리오

| 상황 | 변경 전 | 변경 후 |
|------|---------|---------|
| 패턴 입력 중 키 입력 없이 대기 | `keyboardInputTimeout` 후 강제 종료 | `strokeDeadline` 전까지 계속 대기 |
| 모든 노드 입력 완료 | 타임아웃 후 뒤늦게 종료 | 다음 프레임에 즉시 종료 |
| 마지막 노드 미입력, 판정 시간 초과 | 키 공백 `keyboardInputTimeout` 후 종료 | `마지막 노드 expectedTime + goodWindow` 후 종료 |
| 패턴 없이 키 입력 | 타임아웃 후 종료 | `strokeDeadline`이 0 또는 과거 → 다음 프레임 즉시 종료 |

---

>>> 여기에 피드백을 남겨주세요.
