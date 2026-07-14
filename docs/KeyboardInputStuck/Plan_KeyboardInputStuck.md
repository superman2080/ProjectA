# Plan: 키보드 입력이 삼켜지는 문제 수정 + 입력 계층 리팩토링 — v2

근거 문서: [Research_KeyboardInputStuck.md](Research_KeyboardInputStuck.md)

## 문제 요약
- 키보드로 누른 Point는 `isBusy = true`로 고착된다 (키 뗌이 Point까지 전달되지 않음).
- 고착을 푸는 유일한 경로인 `EndDrag()`는 "판정 대상 없음 / 시한 초과"일 때만 호출되는데, 패턴 겹침 수정 이후 패턴이 끊기지 않아 **그 조건이 거의 성립하지 않는다.**
- 결과: 연속된 두 패턴이 같은 Point를 쓰면 두 번째 입력이 `ForceDown()`의 `if (!isBusy)` 가드에 걸려 무시된다.

---

## v1에서 바뀐 이유 (기존 코드 점검 결과)

v1은 "키 뗌 이벤트(`OnKeyReleased`)를 추가해 `isBusy`를 푼다"는 **증상별 대응**이었다. 입력 계층 전체를 다시 보니 **같은 뿌리에서 나온 문제가 둘 더 있었고, v1으로는 그중 하나도 고쳐지지 않는다.**

### 추가 발견 (1) — 같은 버그가 마우스에도 있다
`isBusy`는 `EndDrag()`에서만 풀린다. **마우스로 두 패턴에 걸쳐 이어 긋는 경우**(드래그를 놓지 않은 채 다음 패턴으로 진입), 이전 패턴에서 지나간 Point는 여전히 `isBusy = true`다.
→ 다음 패턴이 그 Point를 쓰면 `OnPointerEnter`의 `!isBusy` 가드에 걸려 **마우스 입력도 똑같이 삼켜진다.**
→ v1(키 뗌 이벤트)로는 이 경로가 전혀 해결되지 않는다.

### 추가 발견 (2) — 키보드 스트로크 중 마우스를 올리기만 해도 오입력된다
```csharp
// Point.cs
public void OnPointerEnter(PointerEventData eventData)
{
    if (handler.IsDragging && !isBusy)   // ★ IsDragging은 '키보드 스트로크 중'에도 true다
        Down();
}
```
키보드로 입력하는 동안 `IsDragging = true`인데, 이때 **마우스 버튼을 누르지 않고 커서를 Point 위로 지나가기만 해도** `OnPointerEnter` → `Down()` → 입력으로 처리된다.
→ `IsDragging`이 "마우스 드래그 중"과 "키보드 스트로크 중"을 구분하지 못하는 것이 원인.

### 정리: 뿌리는 하나
**"이번 스트로크에서 이미 입력된 Point인가"라는 상태가 `Point.isBusy`(각 Point가 개별 보유)와 `PatternHandler.connectedIndices`(핸들러가 보유) **두 군데에 중복**으로 존재하고, 둘의 리셋 시점이 다르다.**
- `connectedIndices`는 **패턴이 끝날 때마다 클리어된다** (`CompletePattern` → `TriggerLineFadeOut` → `connectedIndices.Clear()`).
- `isBusy`는 **`EndDrag()`에서만** 풀린다 → 패턴 경계를 넘어 고착.

→ **중복 상태를 없애고 `connectedIndices` 하나로 일원화하면, 키보드/마우스 두 경로가 모두 한 번에 해결된다.** 리셋 시점도 자동으로 "패턴 단위"가 되어 의도와 맞는다.

---

## 해결 방향

1. **중복 입력 방지를 `PatternHandler.connectedIndices`로 일원화**하고 `Point.isBusy`를 제거한다. (근본 — 키보드/마우스 동시 해결)
2. **`IsDragging`을 마우스 드래그와 키보드 스트로크로 분리**해, 키보드 입력 중 마우스 호버가 입력으로 처리되지 않게 한다.
3. 입력 계층의 죽은 코드와 이중 소스를 정리한다.

---

## 구현 단계

### 핵심 수정

- [x] **Step 1 — `PatternHandler`: 중복 입력 방지를 한 곳으로 일원화**
  - `OnPointPressed(int index)` 진입부에 가드 추가:
    ```csharp
    // 이번 패턴에서 이미 입력된 Point는 무시한다 (드래그 중 재진입 / 통과 노드 중복 방지).
    // connectedIndices는 패턴이 끝날 때마다 클리어되므로, 다음 패턴에서 같은 Point를 다시 쓸 수 있다.
    if (IsDragging && connectedIndices.Contains(index))
        return;
    ```
  - 통과 노드 자동 인식(`GetPassThroughIndex` → `ForceDown()`)의 `!connectedIndices.Contains(passIndex)` 검사는 이 가드와 중복되므로 **가드 하나로 통합**한다.
  - `AppendPointToLine()`의 "연속 동일 인덱스 재진입 방지" 검사도 상위 가드가 흡수하므로 제거한다.

- [x] **Step 2 — `Point`: `isBusy` 제거**
  - `isBusy`, `IsBusy`, `ResetBusy()` 삭제. `Down()`/`Up()`은 이벤트 발행만 담당한다.
  - `ForceDown()`은 이름 그대로 유지하되 내부는 `Down()` 호출만 남긴다 (가드는 핸들러가 담당).
  - `OnPointerEnter`는 **마우스 드래그 중일 때만** `Down()` (Step 3의 새 프로퍼티 사용).
  - `EndDrag()`의 `p.ResetBusy()` 호출 제거 (색 리셋 `p.ResetColor()`는 유지).

- [x] **Step 3 — `PatternHandler`: 드래그 상태를 마우스/키보드로 분리**
  - `public bool IsDragging` 유지(스트로크 진행 중 여부) + `public bool IsMouseDragging => IsDragging && !isKeyboardStroke;` 추가.
  - `Point.OnPointerEnter`는 `handler.IsMouseDragging`을 본다 → **키보드 스트로크 중 마우스 호버가 입력되지 않는다** (추가 발견 2).

- [x] **Step 4 — 패턴 종료 시 키보드 스트로크 정리**
  - `CompletePattern()`에서 키보드 스트로크가 진행 중이면 `EndDrag()`를 호출한다.
    ```csharp
    if (IsDragging && isKeyboardStroke)
        EndDrag();
    ```
  - **마우스 드래그는 건드리지 않는다** — 여기서 끊으면 다음 패턴으로 이어 긋는 드래그가 중간에 잘린다. Step 1로 `connectedIndices`가 패턴마다 리셋되므로 마우스는 그대로 이어져도 안전하다.

### 리팩토링 (사용자 요청)

- [x] **Step 5 — `InputHandler` 정리**
  - **미사용 API 제거**: `bool[] inputs` / `public bool[] Inputs` — 코드 전체를 검색한 결과 **어디에서도 참조되지 않는 죽은 상태**다. 실제 입력 전달은 `OnKeyPressed` 이벤트로만 이뤄진다.
    - 다만 "현재 눌린 키" 조회가 향후 필요할 수 있으므로, 제거 대신 유지할지 판단은 아래 "결정 필요" 참조.
  - **빈 `Update()` 메서드 제거** (`void Update() { }` — 매 프레임 호출 오버헤드만 발생).
  - `action.performed`/`action.canceled` 람다에서 캡처 변수(`int index = i;`) 유지 — 정상.

- [x] **Step 6 — `Point`의 인덱스 이중 소스 정리**
  - 현재 `index`는 **직렬화 필드**이면서 `Start()`/`Reset()`에서 **게임오브젝트 이름 끝 글자를 파싱해 덮어쓴다**.
    ```csharp
    if (int.TryParse(gameObject.name[^1].ToString(), out int result))
        index = result - 1;
    ```
    → 이름을 바꾸면 인스펙터 값과 소리 없이 어긋나고, `Point_10` 같은 이름이면 잘못 파싱된다.
  - **`PatternHandler`가 `patternPoints` 배열 순서를 진실의 원천으로 삼아 인덱스를 주입**한다:
    ```csharp
    // PatternHandler.Start()
    for (int i = 0; i < patternPoints.Length; i++)
        patternPoints[i].Initialize(i);
    ```
    `Point.Initialize(int index)`가 인덱스를 설정하고, 이름 파싱 코드를 제거한다.
  - `Point.handler` 필드는 `IsMouseDragging` 조회에만 쓰이므로 유지하되, `Initialize()`에서 함께 주입해 `FindAnyObjectByType` 호출을 제거한다.

- [x] **Step 7 — 스트로크 수명주기를 명시적 메서드로 분리**
  - 현재 스트로크 시작 로직이 `OnPointPressed` 안에 인라인으로 섞여 있다(`if (!IsDragging) { connectedIndices.Clear(); isKeyboardStroke = ...; }`).
  - `BeginStroke()` / `EndDrag()`(→ `EndStroke()`로 개명) 두 메서드로 분리해 **스트로크 상태 전이가 한눈에 보이도록** 한다. 동작 변경 없음, 가독성 개선만.

### 검증·문서

- [x] **Step 8 — 검증** (Play 모드 실측)
  - **버그 재현 시나리오**(Research 1절): A(0,3,6)를 키보드로 완주 → B(0,4,8) 승계 → B의 첫 노드(Point_1, A에서도 쓴 Point) 입력 → **위치 0 → 1로 진행** ✅ (수정 전에는 0에서 멈춤)
  - **마우스 경로**(추가 발견 1): 드래그를 놓지 않고 A 완주 → B 승계 → 같은 Point_1 재진입 → **위치 0 → 1로 진행**, `IsMouseDragging`은 True로 유지되어 이어 긋기가 끊기지 않음 ✅
  - **키보드 중 마우스 호버**(추가 발견 2): 키보드 스트로크 중 `IsDragging=True` / `IsMouseDragging=False`. Point_5에 `OnPointerEnter`를 발생시켜도 **입력되지 않음** ✅
  - **중복 방지 보존**: 같은 패턴에서 Point_1을 두 번 입력 → 두 번째는 무시(위치 그대로) ✅
  - **통과 노드 자동 인식**: Point_1 → Point_7 입력 시 사이의 Point_4가 자동 인식되어 3노드 패턴이 완주됨 ✅
  - **실제 채보 오토플레이**(`Dreamer_Lv10`, 1500프레임): **입력 시도 33회 / 정상 진행 33회 / 삼켜진 입력 0회 / 완료된 패턴 13개** ✅

- [x] **Step 9 — 문서 갱신**
  - `CLAUDE.md`: 입력 계층 규칙 추가 — "중복 입력 방지의 진실의 원천은 `PatternHandler.connectedIndices`(패턴 단위로 리셋)", "`IsMouseDragging`과 `IsDragging`의 구분", "Point 인덱스는 `patternPoints` 배열 순서로 주입".

---

## 결정 필요 (구현 전 확인)

1. **`InputHandler.Inputs`(bool[9]) 제거 여부** — 현재 완전한 미사용 코드다. 제거를 권장하되, "키 홀드 상태 조회"가 향후 필요하면 남길 수 있다. **기본: 제거.**
2. **`EndDrag()` → `EndStroke()` 개명** — 마우스/키보드를 아우르는 이름이 정확하지만, 호출부가 여러 곳이다. **기본: 개명 진행.**

---

## 범위 밖 (이번에 하지 않음)

- 키보드 스트로크 종료 조건(`Deadline` 기반) 자체의 변경 — `docs/KeyboardStroke/` 확정 설계 유지
- 키 홀드(길게 누르기) 전용 노드 타입 등 신규 입력 기능
- 마우스/키보드 동시 입력(혼합 스트로크) 정책 정의

---

## 피드백

> 이 아래에 `>>>` 로 피드백을 남겨주세요. 반영 후 Plan을 다시 작성합니다.
