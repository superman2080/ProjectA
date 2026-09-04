# Research — 튜토리얼 씬

목표: 카시마의 대사로 패턴을 하나씩 가르치고, 중간에 실제 패턴을 치게 한다.
표적은 `Assets/03. Prefabs/Target_TatamiRoll.prefab`이며 **실패하면 잘리지 않고 그 패턴을 다시 친다.**

## 1. 지금 있는 것 (전부 재사용 가능)

| 필요한 일 | 이미 있는 것 | 상태 |
|---|---|---|
| 대사 줄줄이 재생 | `SequenceSpace.DialogStep` + `DialogUI`(정적 `Instance`) | 그대로 씀 |
| 대사·연출 순서 저작 | `SequenceAsset` + `SequenceRunner`(슬롯 배선) | 그대로 씀 |
| 패턴 하나를 지금 투입 | `PatternHandler.SetPattern(pattern, inputTimes, spawnTimes=null, exposureDurations=null)` | 공개 API, 채보 없이 호출 가능 |
| 성패 통보 | `PatternHandler.OnPatternComplete(PatternCompletionInfo.AllCorrect)` | 그대로 씀 |
| 표적 등장·절단·실패 | `SliceTargetDirector.Reserve(token, set, startTime, impactTime, offset, spawnOverride)` / `Resolve(token, bool)` | 공개 API |
| 성공 시 베기 모션 | `CharacterActionPlayer`가 `OnPatternComplete` 구독 | 적이 없어도 돈다 |
| 진행도 플래그 | `UnlockStep` → `GameProgress.SetFlag` | 그대로 씀 |

### 1-1. 실패 = "잘리지 않는다"는 이미 구현돼 있다
`SliceTargetDirector.Update`의 임팩트 처리가 `outcome`으로 갈린다 —
`Success`면 `SliceTarget`(조각 교체), 아니면 `CrushTarget`(`SliceTargetView.Crush`, 소멸).
즉 **실패 시 절단이 안 일어나는 것은 새 코드가 아니라 기존 분기다.** 우리가 할 일은
`Resolve(token, false)`를 부르는 것뿐이다.

### 1-2. 재투입은 `SetPattern`을 다시 부르면 끝난다
`ActivePattern`은 `SetPattern` 호출 시각을 `StartTime`으로 잡으므로(§3),
같은 `Pattern` 에셋과 같은 상대 시각 배열로 다시 부르면 그대로 한 번 더 굴러간다.
큐가 비어 있으면 즉시 판정 대상이 된다. **재시도용 상태가 `PatternHandler`에 필요 없다.**

### 1-3. 임팩트 시각은 `OnPatternQueued`가 준다
`PatternQueuedInfo`에 `StartTime`·`Deadline`이 있고, 공용 확장 `info.ImpactTime()`
(= `Deadline + Pattern.ImpactOffset`)이 세 페이로드에 다 붙어 있다.
그래서 표적 예약에 `PatternHandler.goodWindow`(비공개)를 볼 필요가 없다 —
`EnemyDirector`가 투사체를 예약하는 것과 **같은 경로**다.

## 2. 없는 것

1. **"패턴 하나를 내고 성공할 때까지 기다린다"는 스텝** — 시퀀스 스텝 6종(Wait/MoveTo/Dialog/WaitFlag/Timeline/Unlock) 중 없다. 이것이 유일한 신규 코드다.
2. **표적 `SliceSet`** — `Assets/04. Datas/Slice`에 `WoodLog_*`, `Prop_Pouffes_06_Diagonal`, 적 시체 세트만 있고 타타미가 없다.
   - ⚠ `Target_TatamiRoll.prefab`은 자식이 셋이다(`StandBase`·`Stand` = 유니티 기본 Cube, `TatamiRoll` = 실제 메쉬).
     `MeshSliceBakerWindow`는 `sourcePrefab.GetComponentInChildren<MeshFilter>()` **첫 번째**를 자르므로 지금 그대로 구우면 **받침대를 자른다.**
3. **튜토리얼 씬** — `01. Scenes`에 `BattleScene` / `Explore_Stage1` / `SongSelectScene`뿐.
4. **`DialogUI`가 `BattleScene`에 없다** — `Explore_Stage1`의 Canvas에만 `Dialog`가 있다.

## 3. 제약 (어기면 조용히 깨지는 것들)

- **전투 오브젝트 일습은 한 벌만 유지한다**(CLAUDE.md §9). 씬을 복제하면 노브 하나가 두 씬 수정이 된다 — 튜토리얼 씬은 그 규율에 대한 **의도적 예외**이거나, `BattleScene` 안에서 모드로 갈려야 한다(§5의 결정 사항).
- **`Time.timeScale` 금지**(§7-3). 대사 중 멈춤은 `SequenceAsset.holdMode`가 대신한다.
  단 튜토리얼은 **패턴을 치는 동안 입력이 살아 있어야** 하므로 `holdMode = Overlay`를 쓰면 안 된다 —
  `PlayerModeDirector.ApplyMode`가 `Overlay`에서 `combatMover.enabled = false`로 내리고
  `inputHandler.SetMode(Explore)`로 갈아 **패턴 입력이 죽는다**. `Keep`(기본 `Combat`)으로 둔다.
- **`EnemyDirector`가 없어도 판정은 돈다.** 다만 `Pattern.Attacker`가 `Enemy`면 적 칼이 닿는 연출·`OnPlayerHit`이 예약되므로 **튜토리얼 패턴은 전부 `Attacker.Player`**여야 한다(실패해도 안 맞는다 — §6).
- **`SliceTargetDirector`의 토큰은 `EnemyDirector`가 발급한다.** 튜토리얼이 직접 예약하면 토큰이 겹칠 수 있으므로 음수 카운터를 쓴다(같은 씬에 `EnemyDirector`를 남겨 둘 경우의 방어).
- **표적은 스폰 지점에서 임팩트 지점으로 날아온다**(`SliceTargetView.Setup`). 짚단이 서 있길 원하면 씬의 `spawnAnchor`를 임팩트 근처에 두어 이동을 0에 가깝게 만든다 — 새 코드가 아니라 배선 값이다.
- **`SequenceStep`은 값 타입 런타임 상태만 둔다**(얕은 복사). 이벤트 구독은 `Enter`에서 걸고 `Exit`에서 **반드시** 푼다(`Stop()`·`OnDisable` 경로 포함).

## 4. 데이터 흐름 (제안)

```
SequenceRunner(Tutorial 씬)
   ├ DialogStep      "노드를 순서대로 이어 그어라"        (DialogUI)
   ├ PatternDrillStep(pattern, tatamiSet)  ──SetPattern──▶ PatternHandler
   │        ▲                              ──Reserve────▶ SliceTargetDirector
   │        └──OnPatternComplete(AllCorrect)───────────────┤
   │             성공 → Resolve(true) → 절단, 다음 스텝
   │             실패 → Resolve(false) → 소멸, retryDelay 뒤 재투입
   ├ DialogStep      "이번엔 통과 노드가 있다"
   ├ PatternDrillStep(...)
   └ UnlockStep      "tutorial_done"
```

## 5. 결정이 필요한 것 (Plan의 `>>>` 대상)

1. **씬을 어떻게 호스팅하나** — (a) `BattleScene` 복제본 `Tutorial.unity`(권장: 배선 0, 대신 리그가 두 벌) / (b) `BattleScene` 안에서 `GameSession` 플래그로 갈리기(리그 한 벌, 대신 두 모드가 한 씬에 섞임).
2. **커리큘럼** — 몇 개의 패턴을, 어떤 순서로 가르치나(직선 → 꺾임 → 통과 노드 → 연타?).
3. **끝난 뒤 어디로** — `Explore_Stage1`로 넘기나, 그냥 플래그만 세우고 멈추나.
