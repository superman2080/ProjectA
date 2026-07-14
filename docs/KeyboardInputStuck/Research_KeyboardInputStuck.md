# Research: 키보드로 정확히 입력해도 패턴이 이어지지 않는 문제

## 증상
키보드로 정확한 노드를 눌렀는데도 패턴이 진행되지 않는 경우가 있다.

---

## 1. 재현 확인 (Play 모드 실측)

패턴 A(0, 3, 6) → 패턴 B(0, 4, 8)를 큐에 넣고, A를 키보드로 완주시킨 뒤 B의 첫 노드(인덱스 0 = Point_1)를 키보드로 입력했다.

| 단계 | 판정 대상 | IsDragging | isBusy인 Point |
| --- | --- | --- | --- |
| A·B 투입 | A 위치 0/3 | False | 없음 |
| A를 키보드로 완주 → B 승계 | B 위치 0/3 | **True** | **1, 4, 7** |
| **B의 첫 노드(Point_1) 키 입력** | **B 위치 0/3 — 진행 안 됨** | True | 1, 4, 7 |

**입력이 통째로 삼켜졌다.** 증상 재현 완료.

---

## 2. 근본 원인

### 원인 (1) — 키를 떼도 `isBusy`가 풀리지 않는다

`Point.cs`
```csharp
public void ForceDown()
{
    if (!isBusy)      // ★ isBusy면 입력을 통째로 무시한다
        Down();
}

private void Down() { isBusy = true;  ... }
private void Up()   { isBusy = false; ... }   // 마우스 OnPointerUp에서만 호출된다
```

`InputHandler.cs`
```csharp
action.performed += (ctx) => { inputs[index] = true; OnKeyPressed?.Invoke(index); };
action.canceled  += (ctx) => inputs[index] = false;   // ★ Point에 전달되지 않는다
```

- 마우스는 `IPointerUpHandler.OnPointerUp` → `Up()` → `isBusy = false`로 풀린다.
- **키보드는 뗌(release)이 Point까지 전달되지 않는다.** `InputHandler`에 뗌 이벤트(`OnKeyReleased`) 자체가 없다.
- 결과: 키보드로 누른 Point는 **`isBusy = true`로 고착**되고, 이후 그 Point에 대한 키 입력은 `ForceDown()`의 가드에 걸려 전부 무시된다.

### 원인 (2) — 패턴이 끝나도 스트로크가 종료되지 않는다

고착된 `isBusy`를 푸는 유일한 경로는 `EndDrag()`(내부에서 `ResetBusy()` 호출)인데, 키보드 스트로크에서 그 호출 조건은 `PatternHandler.Update()`의 이것뿐이다.

```csharp
if (isKeyboardStroke)
{
    var target = JudgeTarget;
    if (target == null || Time.time > target.Deadline)   // ★ 판정 대상이 없거나 시한이 지나야만
        EndDrag();
    return;
}
```

- `CompletePattern()`은 **`EndDrag()`를 호출하지 않는다.** 패턴이 완료돼도 `IsDragging`과 `isBusy`가 남는다.
- 채보 재생 중에는 패턴이 끊임없이 이어져 **`JudgeTarget`이 거의 항상 존재**하고(실측: 큐가 빈 프레임 0/300), 승계된 새 패턴의 `Deadline`은 미래다. **두 조건 모두 성립하지 않는다.**
- 따라서 곡이 진행되는 내내 `isBusy`가 풀리지 않고 계속 쌓인다.

### 왜 지금 표면화됐나

이전 구조에서는 `SetPattern()`이 진행 중인 패턴을 덮어쓰면서 스트로크가 사실상 리셋되곤 했다.
**패턴 겹침 수정(`docs/PatternOverlap/`)으로 패턴이 끊기지 않고 이어지게 되자 `EndDrag()`가 거의 호출되지 않게 되었고, 원래 있던 잠재 결함(원인 1)이 상시화됐다.**

---

## 3. 증상이 나타나는 조건

- **연속된 두 패턴이 같은 Point를 사용할 때** 두 번째 입력이 무시된다. Point는 9개뿐이고 패턴은 계속 이어지므로 매우 흔하다.
- 한 패턴 안에서는 인덱스 중복이 금지되므로(`Pattern.OnValidate`), **패턴 경계에서 주로 발생**한다.
- 마우스는 뗌 이벤트가 정상 동작하므로 영향이 없다 — **키보드 전용 증상.**

---

## 4. `isBusy`의 원래 역할 (없애면 안 되는 이유)

`isBusy`는 **드래그 중 같은 Point에서 중복 입력이 쏟아지는 것을 막는 장치**다.
- `Point.OnPointerEnter`: 드래그 중 포인터가 Point 위에 머물면 매 프레임 `Down()`이 불릴 수 있다. `isBusy`가 막는다.
- 통과 노드 자동 인식(`GetPassThroughIndex` → `ForceDown()`)의 중복도 막는다.

→ **`isBusy`는 유지하고, "언제 풀리는가"만 고쳐야 한다.**

---

## 5. 관련 코드

| 파일 | 관련 지점 |
| --- | --- |
| `Assets/02. Scripts/Input/InputHandler.cs` | `action.performed` / `action.canceled` — 뗌을 외부로 발행하지 않음 |
| `Assets/02. Scripts/Pattern/Handler/Point.cs` | `isBusy`, `ForceDown()`, `Down()`, `Up()`, `ResetBusy()` |
| `Assets/02. Scripts/UI/PatternHandler.cs` | `OnKeyboardInput()`, `Update()`의 키보드 스트로크 종료 조건, `EndDrag()`, `CompletePattern()` |
