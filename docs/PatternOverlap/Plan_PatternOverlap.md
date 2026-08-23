# Plan: 패턴 겹침 처리 (스폰/연출과 판정 분리)

근거 문서: [Research_PatternOverlap.md](Research_PatternOverlap.md)

## 문제 요약
- 다음 패턴의 노드는 **판정 시각보다 ~1.0초 먼저** 스폰돼야 하는데, 엔트리 간 입력 간격은 **최소 0.4초**다.
- 그래서 `ChartPlayer`는 이전 패턴이 끝나기 전에 `SetPattern()`을 호출하고(채보의 **95%**), `SetPattern()`은 진행 중인 패턴을 **완료 처리 없이 파기**하고 낙하 중인 노드까지 회수한다.
- 반면 **입력(판정) 시각은 전혀 겹치지 않는다(0/111).** → 판정 대상은 언제나 하나. 겹치는 건 시각적 낙하 구간뿐.

## 해결 방향 (Research 4절 A안)
**"스폰/연출 파이프라인"과 "판정 파이프라인"을 분리한다.**
- 패턴을 **큐**로 받는다. 새 패턴이 들어오면 **즉시 노드 스폰 예약 + 가이드라인 표시**를 시작하되, **판정 대상(`nowPattern`)은 교체하지 않는다.**
- 판정 대상은 이전 패턴이 **완료되거나 만료되면** 다음 패턴으로 **승계**한다.
- 낙하 노드는 **패턴별로 독립 관리**한다. 패턴이 바뀌어도 이전 패턴의 노드는 제 수명대로 낙하를 마친다.

## 승계 타이밍 규칙 (사용자 확인 완료 — 가장 중요)

**"패턴의 마지막 노드 판정이 끝나면, 그 즉시 다음 패턴을 입력받을 수 있어야 한다."**

- 마지막 노드가 판정된 **같은 프레임에** `CompletePattern()` → 다음 패턴을 **즉시** `judgeTarget`으로 승계한다. 한 프레임도, 페이드 시간도 기다리지 않는다.
  - 라인 페이드아웃(`lineFadeDuration`)·가이드 페이드아웃(`guideFadeDuration`)은 **시각 연출일 뿐**이므로 승계를 막지 않는다. 연출이 끝나기를 기다리지 않는다.
- 다음 패턴은 이미 큐에 들어와 노드가 낙하 중인 상태이므로, 승계 즉시 **바로 입력을 받을 수 있다.**
- **`ActivePattern.startTime`은 큐에 투입된 시점(= `SetPattern()` 호출 시점)으로 고정한다. 승계 시점에 다시 잡지 않는다.**
  - `ChartPlayer`가 넘기는 `inputTimes`는 **그 패턴의 첫 스폰 시각 기준 상대시간**이다. 승계 시점으로 재기준하면 판정 시각이 통째로 밀려 타이밍이 깨진다.
- 큐가 비어 있으면 `judgeTarget = null`이 되고, 다음 `SetPattern()`이 들어오는 즉시 승계된다.

> 참고(기존 동작 유지): 현재 판정 대상이 남아 있는 동안 플레이어가 **다음 패턴에 속한 노드**를 미리 누르면, 지금과 마찬가지로 "틀린 인덱스"로 처리되어 보너스만 취소된다(`allCorrect = false`). 승계가 즉시 일어나므로 정상 플레이에서는 발생하지 않는다.

## 함께 고쳐야 하는 것 (Research 2절)
1. **`Pattern` 에셋의 런타임 상태 분리** — 두 패턴이 동시에 살아 있으면 같은 템플릿 에셋을 쓰는 엔트리끼리 `index`/`inputTimes`를 덮어쓴다. 실제 채보에 해당 케이스 존재(`[110]&[111]`).
2. **미입력 패턴 자동 만료** — 지금은 올바른 입력이 있어야만 패턴이 진행/종료된다. 무입력 시 영원히 안 끝난다. 지금까지는 다음 `SetPattern()`이 덮어써서 가려져 있었고, 교체를 없애면 그대로 드러난다.

---

## 구현 단계

- [x] **Step 1 — 런타임 패턴 상태를 에셋에서 분리 (`ActivePattern`)**
  - 새 클래스 `Assets/02. Scripts/Pattern/ActivePattern.cs` (namespace `PatternSpace`, 일반 C# 클래스):
    - 필드: `Pattern Template`(모양 원본), `float[] inputTimes`, `float[] spawnTimes`, `float startTime`, `int index`(진행 위치), `bool allCorrect`.
    - 프로퍼티: `ExpectedPointIndex`(= `Template.AllData[index].index`), `ExpectedTime`, `NodeCount`, `IsComplete`(`index >= NodeCount`), `Deadline`(= `startTime + 마지막 inputTime + goodWindow`).
    - 메서드: `Advance()`(index++), `GetNodeType(position)`(Template 위임).
  - **`Pattern.cs`는 "모양 데이터 원본"으로만 남긴다.** 진행 상태(`index`, `nowPattern`, `inputTimes`)와 `Initialize`/`Input`/`Next`/`SetInputTimes`/`OnInput`/`OnExit`는 `ActivePattern`으로 이관하고 제거한다. `AllData` / `GetNodeType` / `OnValidate`(중복 인덱스 검사)는 유지.
  - → 같은 템플릿 에셋을 두 패턴이 동시에 참조해도 상태가 충돌하지 않는다.

- [x] **Step 2 — `PatternHandler`: 패턴 큐 + 판정 대상 승계**
  - 필드 교체: `Pattern nowPattern` → `readonly List<ActivePattern> activePatterns` (스폰/연출 대상 전체) + `ActivePattern judgeTarget` (현재 판정 대상 = 리스트의 선두).
  - `SetPattern(...)` 동작 변경:
    - **기존 패턴을 파기하지 않는다.** `ClearFallingNodes()` / `ResetPointColors()` 호출 제거.
    - 새 `ActivePattern`을 만들어 `activePatterns`에 **추가**하고, 그 패턴의 스폰만 예약한다.
    - `judgeTarget`이 비어 있으면 즉시 선두를 판정 대상으로 승계.
    - 가이드라인/판정영역은 **판정 대상 기준이 아니라 "가장 최근에 들어온 패턴" 기준**으로 갱신할지, **판정 대상 기준**으로 갱신할지 결정 필요 → **판정 대상 기준으로 유지**한다(플레이어가 지금 입력해야 할 패턴을 가리켜야 하므로). 승계가 일어날 때 함께 갱신한다.
  - `Update()`에 만료 처리 추가:
    - `judgeTarget != null && Time.time > judgeTarget.Deadline` → `CompletePattern(judgeTarget)` (남은 노드는 미입력 = Miss 취급, `allCorrect = false`).
  - `CompletePattern(ActivePattern p)`:
    - `OnPatternComplete?.Invoke(p.allCorrect)` 발행, `activePatterns`에서 제거.
    - 라인 페이드아웃 / 가이드·판정영역 갱신 (연출은 승계를 **지연시키지 않는다** — 위 "승계 타이밍 규칙" 참조).
    - **다음 패턴이 있으면 같은 프레임에 즉시 `judgeTarget`으로 승계**하고, 가이드·판정영역을 그 패턴 기준으로 다시 적용한다. 큐가 비었으면 `judgeTarget = null` + 전체 복구.
  - `AddPattern(int index)`는 `judgeTarget` 기준으로 판정하도록 수정 (`nowPattern` → `judgeTarget`). **마지막 노드가 판정된 그 자리에서 바로 `CompletePattern()`을 호출**해 승계까지 끝낸다.
  - `judgeTarget == null`일 때의 입력은 무시(기존 `nowPattern == null` 가드와 동일).

- [x] **Step 3 — 낙하 노드를 패턴별로 분리 관리**
  - `ScheduledSpawn`에 소속 `ActivePattern` 참조 추가.
  - `activeFallingNodes`의 키를 `int position` → **(ActivePattern, position)** 조합으로 변경 (예: `Dictionary<ActivePattern, Dictionary<int, FallingNodeView>>` 또는 노드 뷰에 소속 패턴을 들려 보내고 리스트로 관리).
    - 현재는 position만 키라 **두 패턴의 노드가 동시에 살아 있으면 키가 충돌**한다 (Research 3절).
  - `ClearFallingNodes()`는 **특정 패턴의 노드만** 회수하도록 변경(`ClearFallingNodes(ActivePattern)`). 패턴 완료 시 그 패턴 노드만 정리한다.
    - 단, **이미 판정된 노드만 회수**하고 아직 낙하 중인 노드는 자연 도착까지 두는 게 맞는지 확인 필요 → 패턴이 만료되면 그 패턴의 남은 노드는 회수한다(현재 동작과 동일한 감각 유지).
  - `spawnCounter`(색상 팔레트 순환)는 전역 유지 — 패턴이 바뀌어도 색이 이어지도록.

- [x] **Step 4 — `ChartPlayer` 확인 (변경 최소화)**
  - `SetPattern()` 호출 방식은 그대로 둔다 (스폰 시각에 투입). 이제 `PatternHandler`가 큐로 받으므로 충돌하지 않는다.
  - `Stop()` 시 `PatternHandler`의 활성 패턴을 전부 정리하는 공개 메서드(`ClearAllPatterns()`)를 추가해 호출할지 검토 → **추가한다** (곡 중단 시 노드가 계속 떨어지는 것을 막기 위해).

- [x] **Step 5 — 검증** (Play 모드 실행, 실측 결과)
  - 컴파일 에러 0건.
  - **겹침 수용**: A(3노드) 진행 중 B 투입 → 큐 2, 판정 대상은 여전히 A(위치 1/3). 예전처럼 A가 파기되지 않음 (완료)
  - **즉시 승계**: A의 마지막 노드를 판정한 **같은 호출 안에서** 판정 대상이 B로 교체됨 (완료)
  - **같은 템플릿 동시 활성**(채보의 `[110]&[111]` 케이스): 같은 에셋으로 두 `ActivePattern` 생성 → 서로 다른 인스턴스, 앞 패턴만 위치 1로 진행하고 뒤 패턴은 위치 0 유지 (상태 충돌 없음) (완료)
  - **만료**: 무입력 패턴이 Deadline 경과 후 자동 종료되고 다음 패턴으로 승계. 만료된 패턴의 잔여 낙하 노드는 **즉시 회수**(종료된 패턴 소유 노드 0개) (완료)
  - **실제 채보 재생 300프레임 관측**: 동시 활성 패턴 큐 **평균 2.80 / 최대 4**, 큐가 빈 프레임 **0**, 낙하 노드 평균 4.09 / 최대 6 → 겹침이 상시 정상 수용되고 노드 낙하가 끊기지 않음 (완료)
  - **검증 중 발견한 버그 수정**: 무입력으로 만료된 패턴이 `allCorrect = true`로 완료 보고되어 **아무것도 안 눌러도 전체 정답 보너스**가 나갔다. 만료 시 미완료 패턴에 `MarkIncorrect()`를 호출하도록 수정 → 재검증에서 `allCorrect=False` 확인 (완료)

- [x] **Step 6 — 문서 갱신**
  - `CLAUDE.md`의 `Pattern` 클래스 설명을 갱신(모양 원본 vs 런타임 상태 분리), 패턴 큐/승계 규칙 추가.

---

## 설계 판단 (사용자 확인 완료)

1. **만료된(무입력) 패턴의 남은 노드** → **즉시 회수**한다. 패턴이 완료/만료되면 그 패턴에 속한 낙하 노드를 그 자리에서 풀에 반납한다.
2. **가이드라인/판정영역 기준** → **판정 대상 패턴 기준**. 다음 패턴 노드가 미리 떨어지는 것은 그대로 두되, 가이드는 지금 입력해야 할 패턴만 가리킨다.
3. **만료 시점** → **여유 없이 `마지막 입력 시각 + goodWindow`**. (= `ActivePattern.Deadline`)

---

## 피드백

> 이 아래에 `>>>` 로 피드백을 남겨주세요. 반영 후 Plan을 다시 작성합니다.
