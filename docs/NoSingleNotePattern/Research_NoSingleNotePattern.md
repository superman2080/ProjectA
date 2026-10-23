# Research — 1노트 패턴 제거 (NoSingleNotePattern)

> 요구: **노트가 1개인 패턴을 없애고, 앞으로 채보를 만들 때 1노트 패턴이 생기지 않게 한다.**

---

## 1. 현재 존재하는 1노트 패턴

`Assets/04. Datas/Patterns/Templates/`에서 `patternDatas`가 1칸인 에셋은 셋이다.

| 에셋 | GUID | Attacker | 성격 |
|---|---|---|---|
| `PatternBlock(4)` | `e08c2b98…` | `Enemy`(0) | 적 공격 막기 1노트 |
| `PatternChain(4)` | `c9de0e66…` | `Player`(1) | 사슬용 1노트 |
| `PatternMash(Stage1Enemy, 20Hit)` | `34f81ebb…` | `Player` | **연타** — `patternDatas` 1칸은 노드가 아니라 **게이지 자리**다(§2-1, `OnValidate`가 정확히 1칸을 강제). 실제로는 온셋 2개(창 시작·끝)를 먹고 20타를 친다 |

⚠ **연타는 "1노트 패턴"이 아니다.** `patternDatas.Count == 1`이라는 형식만 같다. 이미 `PatternTemplateLibrary.Register`가 `IsMash`를 자동 배정에서 빼고 있고, 채보 엔트리의 `onsetTimes`도 2개다. → 이번 작업의 대상에서 제외한다(Plan 결정 사항 D1).

## 2. 1노트 패턴의 참조처

| 참조처 | `PatternBlock(4)` | `PatternChain(4)` | 비고 |
|---|---|---|---|
| `BattleScene.unity` — `PatternEffectDirector.prewarmPatterns` | O | O | 프리웜 목록. 빠져도 동작 영향 없음 |
| `Tutorial.unity` — 같은 필드 | O | O | 동일 |
| `Seq_Tutorial.asset` — `PatternDrillStep.patterns` | | O | `(3,4)` → `(4,6)` → `(6,4)` → **`(4)`** 4타 드릴의 마지막. 드릴은 **마지막 패턴에만 `killOnSuccess`**를 준다(`PatternDrillStep` 335~347행) → 빼면 `(6,4)`가 자동으로 마무리가 된다 |
| `…Dreamer…_Lv10.asset` | pool + 엔트리 | pool + 엔트리 | 88엔트리 중 1노트 **5개**(처치 2) |
| `…Dreamer…_Lv20.asset` | pool + 엔트리 | | 220엔트리 중 1노트 **127개**(전부 처치), 첫 엔트리 포함 |
| `LinearHeleen…_Lv10.asset` | pool + 엔트리 | | 101엔트리 중 1노트 **23개**(처치 0), 첫 엔트리 포함 |

에셋을 그냥 지우면 위 전부가 **Missing 참조**가 된다. 특히 채보 엔트리의 `template`이 null이면 `ChartPlayer`가 그 엔트리를 투입하지 못한다.

### 2-1. 기존 채보의 1노트 엔트리 위치(실측)

| 채보 | 직전 엔트리와의 간격 | 다음 엔트리와의 간격 |
|---|---|---|
| Lv10 | 0.4s ×5 | 0.4 ×2 · 1.2 ×1 · 1.6 ×2 |
| Lv20 | 0.25 ×54 · 0.5 ×66 · 1.25~1.75 ×6 | 0.25 ×70 · 0.5 ×55 · 1.25~1.5 ×2 |
| Linear Lv10 | 0.4 ×16 · 1.2~1.6 ×6 | 0.4 ×22 · 1.6 ×1 |

→ **대부분이 같은 온셋 그룹 안의 자투리**다(간격이 그리드 스텝 1~2칸). 쉼표로 고립된 단독 온셋은 소수다. 이 분포가 원인(§3)을 그대로 보여 준다.

## 3. 채보 생성 경로에서 1노트가 생기는 지점

### 3-1. Linear 굽기 (`PatternChartWindow.cs` 352~380행)
```
OnsetGrouper.Group → (그룹마다) OnsetChunkSplitter.Split(group.Count, library.AvailableNodeCounts)
                   → 청크마다 library.GetNextTemplate(size)
```
1. **`AvailableNodeCounts`에 1이 들어 있다** — `PatternTemplateLibrary.Register`가 `count <= 0`만 거른다. 그러면 `Split`의 라운드로빈이 **1, 2, 3, 4, 5, 1, 2…** 순으로 돌아 1청크를 **주기적으로** 만든다. Lv20의 127개가 이것이다.
2. **`Split`의 자투리 폴백** — 남은 개수가 순환 크기보다 작으면 "남은 개수 이하 최대 크기"를 쓰고, 그것도 없으면 **남은 개수 그대로**를 쓴다. 1이 라이브러리에서 빠져도 **남은 1개는 1청크로 남는다**(템플릿 미배정 → 빨간 구간).
3. **그룹 자체가 1개** — 앞뒤로 `maxGroupGapSteps`보다 먼 단독 온셋. `Split(1, …)`이 무엇을 하든 1이다.

`OnsetChunkSplitter`는 `ChartGen.Core`(asmdef, Unity 비의존)이고 `OnsetChunkSplitterTests`가 있다. 기존 테스트 `Split_SingleAvailableSize_RemainderUsedDirectly`는 **`[3,3,1]`을 기대값으로 고정**하고 있다 — 이번 요구와 정면으로 충돌한다.

### 3-2. 수동 편집 (Linear)
| 경로 | 1노트가 생기는 조건 |
|---|---|
| 행의 **쪼개기** UI (933~941행) | `splitAt`을 `[1, nodeCount-1]`로 클램프 → 앞 조각이나 뒤 조각이 1 |
| `ManualAssign` → `SplitDraft(index, want)` | 그룹보다 1 작은 패턴을 배정하면 뒤 조각이 1 |
| `Reassign`(무작위 재배정) | 노드 수 기준이라 1노트 엔트리면 1노트 템플릿을 고른다 |
| `MergeUnassigned` | 합친 결과가 1이면 1노트 템플릿을 배정 |

### 3-3. Loop 저작 (`PatternChartWindow.Loop.cs`)
- `PlaceLoopEntry`는 `RequiredOnsets(loopTemplate)`개의 온셋을 만든다 — **템플릿이 1노트면 1노트 엔트리**. 템플릿 선택 `ObjectField`(220행)는 아무 `Pattern`이나 받는다.

### 3-4. 저장 검증 (`ValidateForSave`, Loop.cs 630행~)
- 노드 수를 보는 항목이 없다. 1노트 엔트리는 그대로 저장된다.

### 3-5. 패턴 에셋 자체 (`Pattern.OnValidate`)
- 중복 인덱스만 막고 **최소 노드 수를 보지 않는다.** 연타는 `ValidateMash`가 "정확히 1칸"을 따로 강제한다.

## 4. 다른 경로(채보 외)
- `PatternDrillStep`(튜토리얼 드릴)은 패턴 배열을 직접 들고 채보를 거치지 않는다. 1노트 패턴을 꽂을 수 있는 또 하나의 진입점이다.
- 런타임(`PatternHandler`·`ChartPlayer`)은 노드 수 1을 특별 취급하지 않는다 — **런타임 수정은 불필요**하다. 1노트를 막는 것은 저작 계층의 일이다.

## 5. 제약
- **Linear 굽기는 "한 줄도 안 고쳤다"가 지난 개편의 전제였다**(§5). 이번 작업은 그 파이프라인(분할기)을 실제로 고친다 — 요구 자체가 생성 규칙 변경이라 피할 수 없다. 영향은 "1청크를 안 만든다"로 한정한다.
- 기존 채보 셋을 **재굽기하면 수동 편집과 cue(처치 지시)가 인덱스로 밀린다**(`preservedCues[drafts.Count]`). 그래서 기존 채보는 재굽기가 아니라 **엔트리 단위 이관**이 안전하다.
- 1노트 엔트리를 이웃과 합치면 그 이웃의 노드 수가 바뀌어 **이웃의 템플릿도 다시 골라야** 한다(같은 크기 템플릿이 풀에 없으면 미배정이 된다). 풀의 노드 수 분포: 2·3·4·5(템플릿 기준).
- `chainKillRatio`·사슬 경계는 `killOnSuccess`로 표현된다 — 합칠 때 **뒤 엔트리의 cue가 살아남는 기존 `MergeWithNext` 규칙**을 따르면 사슬 마무리가 보존된다.
