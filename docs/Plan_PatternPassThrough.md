# Plan_PatternPassThrough.md — 통과 포인트 자동 인식 구현

> 기반 문서: `Research_PatternPassThrough.md`
> 가정(피드백 필요):
> 1. 통과 노드는 라인 시각 효과뿐 아니라 `AddPattern` 판정에도 실제로 지나간 것과 동일하게 반영된다.
> 2. 이미 방문(연결)한 노드가 통과 지점이면 다시 추가하지 않고 건너뛴다.
> 3. 이 기능은 마우스/터치 드래그(라인이 그려지는) 경로에만 적용되고, 키보드 직접 입력 경로(`InputHandler`)에는 적용하지 않는다.

## 설계 요약
- 두 인덱스 사이의 "통과 노드"를 계산하는 순수 함수(격자 좌표 기반, row/col 짝수 판정)를 `PatternHandler`에 추가한다.
- `Point`에 `Down()`을 외부에서 안전하게 트리거할 수 있는 `ForceDown()`을 추가한다 (이미 busy면 무시).
- `PatternHandler.OnPointPressed(int index)`에서, 직전 연결 노드와 새 노드 사이에 통과 노드가 있고 아직 방문하지 않았다면, 새 노드를 처리하기 *전에* 통과 노드의 `ForceDown()`을 호출한다. 이 호출은 `Point.OnPointDown` 이벤트를 발생시켜 `OnPointPressed`가 통과 노드에 대해 재귀적으로 실행되고, 기존 라인 추가(`AppendPointToLine`) + 판정(`AddPattern`) 로직을 그대로 재사용한다. 새로운 파이프라인을 만들지 않고 기존 경로를 그대로 태우는 방식.

## 단계별 구현 계획

### Step 1 — `Point`에 외부 트리거 진입점 추가
- [x] `Assets/02. Scripts/Pattern/Handler/Point.cs`에 `public void ForceDown() { if (!isBusy) Down(); }` 추가
- [x] 기존 `OnPointerDown`/`OnPointerEnter`/`Down()`/`Up()` 로직은 변경하지 않음

### Step 2 — 통과 노드 계산 함수 추가
- [x] `PatternHandler`에 `private static int GetPassThroughIndex(int a, int b)` 추가
  - `rowA=a/3, colA=a%3, rowB=b/3, colB=b%3`
  - `(rowA+rowB)`와 `(colA+colB)`가 모두 짝수이면 `((rowA+rowB)/2)*3 + (colA+colB)/2` 반환
  - 아니면 `-1`(없음) 반환
  - 3x3 고정 격자를 전제로 한 계산이며, 결과는 항상 0개 또는 1개

### Step 3 — `OnPointPressed`에서 통과 노드 자동 연결
- [x] `OnPointPressed(int index)` 수정: `connectedIndices`가 비어있지 않을 때(= 드래그 중 이미 최소 1개 노드가 연결된 상태) `int passIndex = GetPassThroughIndex(connectedIndices[^1], index);` 계산
- [x] `passIndex != -1 && !connectedIndices.Contains(passIndex)` 이면, 실제 `index` 처리(`AppendPointToLine`/`AddPattern`) 전에 `patternPoints[passIndex].ForceDown()` 호출
  - `ForceDown()` → `Point.Down()` → `OnPointDown?.Invoke(passIndex)` → `PatternHandler.OnPointPressed(passIndex)` 재귀 호출 → 통과 노드가 먼저 라인에 추가되고 먼저 판정됨 (시간적으로 올바른 순서)
- [x] 재귀 호출 시 `connectedIndices`가 비어있지 않은 상태(첫 노드는 이미 있음)이므로 무한 재귀 없이 종료됨 (3x3 격자에서는 통과 노드끼리 또 다른 통과 노드를 갖지 않음 — Research 문서 표 참고)

### Step 4 — 회귀 확인
- [x] 기존 인접 노드 드래그(예: 1→2→3)는 통과 노드가 없으므로(`GetPassThroughIndex`가 -1 반환) 기존 동작과 동일하게 유지되는지 확인 (코드 리뷰로 확인 — dedupe/-1 분기 로직상 영향 없음)
- [x] 키보드 입력 경로(`InputHandler.OnKeyPressed → AddPattern`)는 `Point`를 거치지 않으므로 이번 변경의 영향을 받지 않음을 코드 리뷰로 확인
- [x] 컴파일 및 Play 모드 진입 시 콘솔 에러 없는지 확인 (에러 없음). MCP로 실제 드래그 시뮬레이션은 불가하므로, 실제 드래그를 통한 시각적 확인은 사용자가 에디터에서 직접 수행 필요

## 진행 상태 표기 규칙
구현 시작 후 각 항목을 완료할 때마다 `- [ ]` → `- [x]`로 갱신하며, 전 단계가 끝날 때까지 중단 없이 진행합니다.

>>> 여기에 피드백을 남겨주세요.
