# Research_PatternPassThrough.md — 통과 포인트 자동 인식

## 목적
드래그로 패턴을 그을 때, 두 노드를 잇는 직선이 3x3 격자상 다른 노드의 정확히 위(중간 지점)를 지나간다면, 그 노드를 실제로 호버하지 않았더라도 "그어진 것"으로 자동 인식해야 한다. (Android 잠금패턴의 "통과 노드 자동 선택"과 동일한 UX.)

예: Point_1(index 0) → Point_9(index 8) 로 드래그 시, 두 점의 정중앙에 Point_5(index 4)가 위치하므로 5번을 실제로 지나가지 않아도(빠른 드래그로 프레임상 호버 이벤트를 놓치는 경우 등) 5번이 패턴에 포함되어야 한다.

## 관련 코드 분석

### `Point.cs` (`Assets/02. Scripts/Pattern/Handler/Point.cs`)
- `OnPointerDown` → `Down()` 무조건 호출 (isBusy 무시)
- `OnPointerEnter` → `handler.IsDragging && !isBusy` 일 때만 `Down()` 호출 — 즉 드래그 중 새 노드 진입시에만 눌림 처리, 이미 눌린 노드는 재호출 안 됨
- `Down()`/`Up()`은 `private` — 외부(PatternHandler)에서 직접 호출할 방법이 현재 없음
- `isBusy`는 `Up()` 또는 `ResetBusy()`로만 해제됨

### `PatternHandler.cs`
- `OnPointPressed(int index)` — `Point.OnPointDown` 이벤트의 유일한 구독자. 드래그 시작/중 노드 진입 시마다 호출되며, 현재는:
  1. `connectedIndices`에 append + 라인 갱신 (`AppendPointToLine`)
  2. `AddPattern(index)` 호출 (정오답 판정 + `Pattern.Input()` 진행)
- `connectedIndices`(List<int>)로 드래그 중 지나온 인덱스를 순서대로 보관 — 이번 기능의 "이미 방문한 노드인지" 판단에 그대로 재사용 가능
- 키보드 입력 경로(`InputHandler.OnKeyPressed → AddPattern`)는 `Point`를 전혀 거치지 않으므로, 이번 "통과 인식" 기능과 무관 (라인이 그려지는 마우스 드래그 상황에서만 의미가 있음)

### 격자 좌표 (CLAUDE.md 기준, index 0~8)
```
0(-200,-200) 1(0,-200) 2(200,-200)
3(-200,0)    4(0,0)    5(200,0)
6(-200,200)  7(0,200)  8(200,200)
```
row = index/3, col = index%3 로 표현 가능.

## 통과 노드 판별 공식
두 인덱스 a, b에 대해 row/col을 구했을 때:
- `(rowA+rowB)`와 `(colA+colB)`가 모두 짝수이면, 중간 지점에 정확히 다른 노드가 존재함
- 그 노드의 index = `((rowA+rowB)/2) * 3 + (colA+colB)/2`
- 홀수인 축이 하나라도 있으면 정확히 중간에 위치한 노드가 없음 (통과 노드 없음)

검증:
| a→b | 통과 |
|---|---|
| 0→2 | 1 |
| 0→6 | 3 |
| 0→8 | 4 |
| 2→6 | 4 |
| 2→8 | 5 |
| 6→8 | 7 |
| 1→7 | 4 |
| 3→5 | 4 |
| 0→4, 0→1, 0→3 등 인접/대각 1칸 | 없음 |

3x3 격자이므로 한 번의 이동에 통과 노드는 **최대 1개**만 존재한다 (그 이상 떨어진 인덱스 쌍이 없음).

## 확인이 필요한 미결 사항 (Plan 작성 전 가정)
1. **통과 노드가 판정(`AddPattern`)에도 반영되어야 하는가, 단순히 라인 시각 효과에만 반영되어야 하는가?**
   → 가정: "그어진 것으로 인식"이라는 표현상, 라인뿐 아니라 `AddPattern`(정오답 판정 + `Pattern.Input()` 진행)에도 동일하게 포함시킨다. 즉 실제로 손가락/마우스로 지나간 것과 완전히 동일하게 취급.
2. **이미 방문한(사용된) 노드가 통과 지점이면?**
   → 가정: Android 잠금패턴과 동일하게, 이미 `connectedIndices`에 있는 노드는 다시 추가/판정하지 않고 건너뛴다 (중복 카운트 방지).
3. **통과 노드 판별을 위해 `Point.Down()`을 외부에서 트리거할 방법이 필요** — 현재 `private`. `ForceDown()` 같은 public/internal 진입점을 추가해 기존 `OnPointDown` 이벤트/라인/판정 파이프라인을 그대로 재사용하는 것이 가장 적은 변경으로 일관성을 유지하는 방법으로 보임.
