# Research — 연타 노트 (MashNote)

> 요구: **패턴의 아무 노드나 눌러 발동하는 연타 구간.** 지정 타수를 채우면 성공, 못 채우면 실패.
> `Attacker.Player` 전용. **입력마다 지정 클립의 `ImpactTime`부터** 써는 모션이 나온다.
> 지정 타수까지는 입력마다 점수가 오르고, 그 이상은 **애니메이션만 나오고 점수는 안 오른다**.
> 레퍼런스: 뮤즈대쉬 샌드백 · 태고의 달인 연타.
>
> 이 문서는 설계가 아니라 **현재 구조가 무엇을 전제하고 있는지**와 **그 전제를 깨는 지점**을 정리한다.

---

## 0. 이 요구가 기존 구조와 정면으로 다른 점 하나

**패턴의 진행은 "노드의 나열"이다.** `ActivePattern`은 `CurrentPosition`을 하나씩 밀며 `ExpectedPointIndex`·`ExpectedTime`을 내놓고, `PatternHandler.AddPattern`은 **딱 그 인덱스, 딱 그 시각**만 본다. 연타에는 그 셋이 전부 없다 — **어느 인덱스든 되고, 정해진 시각도 없고, 위치가 밀리지도 않는다**(타수만 센다).

그래서 이 기능은 §7·§12가 지켜 온 "**`PatternHandler`는 판정의 단일 소유자이고 새 연출은 이벤트만 구독한다**"로 **덮을 수 없다.** 연타는 연출이 아니라 **판정의 두 번째 종류**다. 이 문서의 나머지는 "그 두 번째 종류를 어디에 두어야 기존 하나가 안 흔들리는가"를 다룬다.

---

## 1. 현재 판정 한 사이클

```
Point.OnPointDown → PatternHandler.OnPointPressed(index)
   ├ connectedIndices 중복 가드          ← 같은 Point 재입력 차단(패턴마다 클리어)
   ├ GetPassThroughIndex → ForceDown     ← 통과 노드 자동 인식
   └ AddPattern(index)
        ├ index != target.ExpectedPointIndex → MarkIncorrect + Miss 색 + **return**(진행 안 함)
        ├ delta = |Time.time − target.ExpectedTime| → Judge(delta)
        ├ OnJudged(result, index) / OnFocusRingResolved(...)
        ├ target.Advance()                ← CurrentPosition++
        └ target.IsComplete → CompletePattern(target)

Update → ExpireOverduePatterns()
        while (Time.time > JudgeTarget.Deadline) → MarkIncorrect + CompletePattern
```

`CompletePattern`은 큐에서 빼고 `OnPatternComplete(PatternCompletionInfo)`를 쏘고 **다음 패턴을 같은 프레임에 승계**한다.

---

## 2. 연타가 재사용할 수 있는 것 (예상보다 많다)

| 기존 것 | 연타에서 그대로 쓰이는가 | 근거 |
|---|---|---|
| `activePatterns` 큐 · `JudgeTarget` 승계 | O 그대로 | 연타도 "한 번에 하나가 판정 대상"이다 |
| `Deadline` | O 그대로 | 연타 창의 끝이 곧 `Deadline`이다 |
| `AllCorrect` | O 그대로 | 타수 달성 = `true`, 미달 = `false` |
| `OnPatternComplete` → `EnemyDirector.ResolveReservation` | O **한 줄도 안 고침** | 성패 bool 하나만 본다. `killOnSuccess`·사슬·리액션이 공짜로 따라온다 |
| `OnPatternQueued` → 상대 배정(§11-1) · 표적(§11) · 이펙트(§7-4) | O 그대로 | 페이로드가 시각뿐이다 |
| `OnJudged(result, index)` → `ScoreDirector` | O **한 줄도 안 고침** — §3 참조 | 점수는 "친 노트 수"만 센다 |
| `ImpactTime()` = `Deadline + ImpactOffset` | O 그대로 | 절단·히트스톱·카메라·소리가 전부 이 식이다 |
| `CharacterActionPlayer.PlaySlot` | O 재사용 | 임팩트부터 재생 = `startOffset`을 `ImpactTime`으로 주는 것 |

**이 표가 이 기능의 좋은 소식 전부다** — 연타는 "패턴이 성공했는가"라는 한 bool로 바깥과 이야기하므로, **판정 하류(적·점수·연출·카메라)는 연타를 몰라도 된다.**

---

## 3. 점수 — `ScoreDirector`를 안 고치고 되는가 (⚠ 결론: 된다, 단 조건이 있다)

현재 셈법:

```
totalNotes  = Σ entry.onsetTimes.Length                    ← 곡 시작에 한 번
OnJudged 1회 = 노트 1개 획득(Perfect/Good 카운터 + 콤보)
패턴 완료   = MissedNotes(Template.AllData.Count, judgedInPattern)  ← 뺄셈
```

연타를 여기 얹으면:

- **타수 하나 = `OnJudged` 하나**로 발행하면 점수·콤보가 **자동으로** 오른다. "입력마다 점수가 오른다"가 그대로 성립한다.
- **지정 타수 초과분은 `OnJudged`를 안 쏘면** 점수가 안 오른다. "그 이상은 애니메이션만"이 **분기 하나**로 끝난다.
- 채보의 `onsetTimes`에 **타수만큼** 시각을 넣으면 `totalNotes`가 맞는다.

**⚠ 단 하나가 걸린다 — 뺄셈의 좌변이 `Template.AllData.Count`다.**

`ScoreMath.MissedNotes(nodeCount, judgedCount)`의 `nodeCount`가 `Pattern.AllData.Count`인데, **연타의 목표 타수를 `patternDatas`로 표현할 수 없다**:

> `Pattern.OnValidate`가 **중복 인덱스를 에러로 막는다.** 노드는 9개뿐이라 20타 연타를 `patternDatas` 20칸으로 적는 것은 원리적으로 불가능하다.

→ 목표 타수는 **별도 필드**여야 하고, 그러면 `ScoreDirector.HandlePatternComplete`의 뺄셈 좌변만은 연타를 알아야 한다. **(그 한 줄이 `ScoreDirector`가 이 기능 때문에 바뀌는 전부다.)**

또 하나:

- **`ScoreDirector`는 `OnJudged`의 `index`를 안 본다** — 카운터와 콤보만 올린다. 연타가 어느 Point를 눌렀는지는 점수에 무의미하므로 그대로 흘려도 된다.
- **`SSS` 판정의 `MaxCombo == totalNotes`**는 연타에서도 성립한다(타수마다 콤보가 오르므로).

---

## 4. 깨지는 것 — 여기가 이 설계의 전부다

### 4-1. `AddPattern`의 세 전제

| 전제 | 연타에서 |
|---|---|
| `index != ExpectedPointIndex` → **오답, 진행 안 함** | **모든 인덱스가 정답**이다 |
| `delta = |now − ExpectedTime|` → `Judge` | **정해진 시각이 없다.** 창 안이면 전부 유효타 |
| `Advance()` → `IsComplete` → `CompletePattern` | **타수를 채워도 끝나면 안 된다** — §4-2 |

### 4-2. ⚠ 연타는 목표 타수에 도달해도 **완료되면 안 된다**

일반 패턴은 마지막 노드 판정 자리에서 `CompletePattern` → **큐에서 빠지고 다음 패턴이 즉시 승계된다.** 연타가 그러면 **초과 타격이 다음 패턴의 입력으로 흘러 들어간다** — "그 이상 타격하면 애니메이션만 나온다"가 성립할 수 없을 뿐 아니라, **다음 패턴을 오답으로 오염시킨다**(그 패턴의 `AllCorrect`가 깨진다).

> **연타의 완료 시각은 언제나 `Deadline` 하나다** — 성공이든 실패든. 이것이 일반 패턴과의 가장 큰 행동 차이이고, `ExpireOverduePatterns`가 이미 그 경로를 갖고 있다(지금은 "만료 = 실패"라고 가정할 뿐이다).

### 4-3. 중복 입력 가드가 연타를 죽인다

`OnPointPressed`의 `connectedIndices.Contains(index) → return`은 **"같은 Point는 패턴당 한 번"**이다. 연타는 정의상 **같은 Point를 계속 누른다** — 이 가드에 그대로 걸려 2타부터 전부 삼켜진다.

같은 지점에서 **통과 노드 자동 인식**(`GetPassThroughIndex` → `ForceDown`)도 연타에서는 의미가 없고, 오히려 **한 번 눌렀는데 두 타로 세어질 수 있다**(1→3을 그으면 2가 자동 입력된다).

### 4-4. 링·노브·가이드라인이 전부 `AllData`를 읽는다

| 코드 | 지금 하는 일 | 연타에서 |
|---|---|---|
| `SetPattern`의 스폰 루프 | 노드마다 `GetPointIndex(i)` 자리에 링 예약 | 목표 20타면 **링 20개**가 아무 데나 뜬다 |
| `ApplyHitAreas` | 패턴이 안 쓰는 Point의 판정 영역 축소 | **9개 전부 살아 있어야** 한다 |
| `ApplyKnobVisibility` | 쓰는 Point의 노브만 표시 | **9개 전부 켜져야** 한다("아무 데나 눌러라") |
| `ShowGuideLine` | 노드를 순서대로 잇는 캡슐 경로 | 이을 순서가 없다 — **꺼야 한다** |
| `AppendPointToLine` / 입력 라인 | 그은 획을 그린다 | 연타는 획이 아니다 |

**⚠ `SetPattern`의 입구 검증도 막는다**: `inputTimes.Count != pattern.AllData.Count`면 에러 후 리턴한다. 연타는 `inputTimes`(= 타수)와 `AllData`(= 표현 불가)의 개수가 애초에 다르다.

### 4-5. ⚠ 입력마다 임팩트부터 재생 = **정렬 모델이 통째로 빠진다**

§6의 계약은 "**클립의 임팩트 프레임이 `Deadline + ImpactOffset`에 온다**"이고, 시작 시각과 배속을 역산해 그것을 맞춘다. 연타 타격은 **언제 올지 모른다**(플레이어가 정한다) — 역산할 대상이 없다.

그래서 연타 타격 클립은 **정렬 대상이 아니다.** `CharacterActionPlayer`에 이미 그 성질의 경로가 있다:

```csharp
public void PlayOneShot(AnimationClip clip, float speed = 1f)   // 회피 구르기가 쓴다
    → PlaySlot(clip, 0f, clip.length, speed, isSwing: false);
```

연타 타격은 이것의 변형이다 — `startOffset`을 `alignment.ImpactTime`으로, 길이를 `트림 끝 − ImpactTime`으로 주면 **"임팩트부터 써는 모션"**이 정확히 나온다. `isSwing`은 **true**여야 한다(§7-3의 `ApplyHitStop` 가드가 `swingActive`를 본다 — 연타에 타격감을 붙이려면 필요하다).

**⚠ 다만 이것이 방금 넣은 클립 시퀀스(§6-1)와 부딪힌다**: 연타 타격은 `PlaySlot(continuesSequence: false)`이므로 **진행 중인 시퀀스를 끊는다**. 연타 패턴은 리드인을 쓰지 않는다는 규칙이 필요하거나, 아니면 연타 자체를 "리드인 없는 패턴"으로 제한해야 한다.

**⚠ 그리고 `impactAuthored = 0`이 되므로 `DuelCurveTime`이 `NaN`이다** → §11-9 결투 거리 커브가 연타 구간에서 **구동되지 않는다**(마지막 값 홀드). 이건 오히려 옳다 — 연타 중에 플레이어가 커브를 따라 미끄러지면 안 된다. **버그가 아니라 자동으로 맞는 지점**이지만 문서에 남겨야 한다.

### 4-6. 히트스톱·이펙트·카메라

- `HitStopDirector`는 `OnPatternComplete`(성공)와 `OnExtraImpact`만 듣는다. **연타 타격마다 멈추려면 새 경로가 필요하다** — 다만 `OnExtraImpact(float fireTime)`가 이미 "임의 시각에 한 번 멈춰라"는 계약이라 **그대로 재사용할 수 있다**(연타 타격 순간 = `Time.time`).
  - **⚠ `minHitStopGap`이 연타를 걸러 낸다**(기본 = `hitStopDuration` 0.1초). 연타는 초당 5~10타라 대부분의 정지가 버려진다. 그게 옳을 수도 있다(전부 멈추면 게임이 슬라이드쇼가 된다) — **의도된 동작인지 결정이 필요하다.**
- `PatternEffectDirector`(§7-4)는 `OnPatternQueued`에서 예약을 다 만든다. 연타 타격은 **예약할 수 없는 시각**이라 큐 모델에 안 들어간다 → 타격 이펙트는 별도 경로거나, 아니면 없다.
- `CameraDirector`는 `OnPatternComplete`만 듣는다 → **그대로 동작**(연타 종료 시 한 번).

### 4-7. 적은 무엇을 하는가

- `EnemyDirector`는 연타를 몰라도 된다(§2). 다만 **`Pattern.EnemyFeint`**(§11-4)가 "표적이 된 순간~임팩트"를 채우는데, 연타 창은 길다 — 견제 클립이 그 구간을 어떻게 메울지는 열려 있다.
- **`Pattern.EnemyHit`**(§11-5 리액션)은 임팩트 한 번에만 재생된다. **연타 타격마다 적이 젖혀지게 하려면** 새 경로가 필요하다 — 뮤즈대쉬 샌드백의 핵심 그림이 그것이라 **요구에 들어 있는지 확인이 필요하다**.

---

## 5. 채보는 연타를 어떻게 담는가

`SongChartEntry`는 `template` + `onsetTimes[]` + `exposureDurations[]` + `spawnTimes[]` + `enemyCue`다. 연타에 필요한 것은 **시작 시각과 끝 시각**이고, 타수는 패턴 에셋이 든다.

- `onsetTimes`를 **타수만큼** 채우면 `ScoreDirector.totalNotes`가 자동으로 맞고(§3), `ActivePattern.Deadline`(= 마지막 + `goodWindow`)도 자동으로 맞는다. **채보 스키마를 안 바꿔도 된다.**
- 그 시각들은 **판정에 쓰이지 않는다**(연타는 시각을 안 본다) — 오직 개수와 마지막 값만 쓰인다. ⚠ 이 "쓰는 것과 안 쓰는 것이 한 배열에 섞이는" 상태는 저작자에게 설명이 필요하다.
- `spawnTimes[0]`이 `ChartPlayer`의 투입 트리거라 그대로 산다.
- **굽기 툴**(`PatternChartWindow`)은 온셋 분석으로 그룹을 만든다 — 연타 엔트리는 **손으로 만드는 것**이 자연스럽다(음악의 온셋이 연타를 정하지 않는다).

---

## 6. 설계 후보

### 후보 A — `ActivePattern`을 다형화한다 (`MashPattern : ActivePattern`)

`PatternHandler`가 `JudgeTarget`에게 **묻는다**: "이 입력을 받아 줄래?"

```
abstract bool TryConsume(int index, float now, out JudgementResult result, out bool scored);
abstract bool ShouldCompleteOnConsume { get; }
abstract bool WantsPerNodeRings { get; }
```

- 분기가 **다형 호출 하나**에 모여 `AddPattern`에 `if (isMash)`가 흩어지지 않는다.
- §4-4의 시각 계층은 여전히 `if`가 필요하다(`Template`을 읽으므로).
- `ActivePattern`이 지금 `sealed`가 아니고 소비자가 `PatternHandler` **하나뿐**이라(위 grep) 비용이 작다.

### 후보 B — `PatternKind` enum + `PatternHandler` 안의 분기

- 가장 직접적이고 읽기 쉽다. 대신 분기가 `AddPattern`·`SetPattern`·`ApplyHitAreas`·`ApplyKnobVisibility`·`ShowGuideLine`·`OnPointPressed`·`ExpireOverduePatterns` **일곱 군데**로 흩어진다.
- 흩어진 분기 하나를 빠뜨리면 **조용히 어긋난다**(링 20개, 연타 2타부터 삼킴 등).

### 후보 C — 별도 디렉터 (`MashDirector`, `DodgeDirector` 관례)

- `DodgeDirector`는 자기 시계로 판정하며 `PatternHandler`를 안 건드린다 — 그 관례가 여기 맞는가?
- **맞지 않는다.** 회피는 패턴 **밖의** 사건(애니메이션 공백)이라 큐·승계·성패 보고가 필요 없었다. 연타는 **패턴 그 자체**라 `activePatterns` 큐에 들어가야 하고 `OnPatternComplete`로 성패를 보고해야 한다. 밖에 두면 큐 승계·상대 배정·점수 총량을 전부 복제하게 된다.

---

## 7. 열린 질문 (Plan 전에 답이 필요하다)

1. **초과 타격의 "애니메이션만 변경"이 무슨 뜻인가?**
   (a) 초과분은 **다른 클립**을 쓴다, 또는 (b) 같은 클립이 계속 나오되 **점수만 안 오른다**.
   → 지금 문서는 (b)로 읽었다. (a)면 클립 슬롯이 하나 더 필요하다.
2. **타격 클립이 하나인가 여러 개인가?** 한 클립만 반복하면 연타가 한 동작의 되감기로 보인다. `hitClips[]`(피격 리액션)처럼 **번갈아 재생**하는 전례가 이미 있다.
3. **적이 연타마다 반응하는가?** 뮤즈대쉬 샌드백의 그림이 그것이다. §4-7 — `EnemyHit`은 지금 임팩트 한 번에만 걸린다.
4. **연타 타격마다 히트스톱을 거는가?** `minHitStopGap`(0.1초)이 대부분을 버린다 — 그대로 둘지, 연타 전용 값을 둘지(§4-6).
5. **타이밍 판정이 아예 없는가?** 지금 문서는 "창 안이면 전부 Perfect"로 읽었다. 그러면 연타 구간은 **점수 효율이 일반 패턴보다 좋다**(Good이 안 나온다) — 밸런스상 의도인가?
6. **연타 실패의 의미는?** 타수 미달 = `AllCorrect = false`이고, 그러면 §11-2대로 적이 물러나거나 패링한다. 연타에 어울리는 그림인가, 아니면 다른 실패 연출이 필요한가?
7. **시각 표현**: 링 하나가 창 전체에 걸쳐 수축(`FocusRingView` 재사용, `indexLabel`에 타수 표시)인가, 새 게이지 뷰인가?
8. **연타 패턴은 리드인 클립(§6-1)을 쓸 수 있는가?** §4-5 — 지금 구조로는 타격이 시퀀스를 끊는다. 금지하는 편이 단순하다.

---

## 참고 파일

| 파일 | 왜 |
|---|---|
| `Assets/02. Scripts/UI/PatternHandler.cs` | 판정·큐·링·노브·가이드라인 — §4의 전부 |
| `Assets/02. Scripts/Pattern/ActivePattern.cs` | 진행 상태. 소비자가 `PatternHandler` 하나뿐(후보 A의 근거) |
| `Assets/02. Scripts/Pattern/Pattern.cs` | ⚠ `OnValidate`가 중복 인덱스를 막는다(§3의 결정적 제약) |
| `Assets/02. Scripts/Score/ScoreDirector.cs` | 뺄셈의 좌변 한 줄만 연타를 알면 된다(§3) |
| `Assets/02. Scripts/Score/Core/ScoreMath.cs` | `MissedNotes` — 고칠 필요 없음 |
| `Assets/02. Scripts/Character/CharacterActionPlayer.cs` | `PlayOneShot`/`PlaySlot` — 타격 재생의 재사용 지점(§4-5) |
| `Assets/02. Scripts/Enemy/EnemyDirector.cs` | 성패 bool 하나만 본다 — 안 고쳐도 된다(§2) |
| `Assets/02. Scripts/HitStop/HitStopDirector.cs` | `OnExtraImpact` 재사용 가능 · `minHitStopGap` 함정(§4-6) |
| `Assets/02. Scripts/ChartGen/ChartPlayer.cs` · `SongChart.cs` | 채보 스키마를 안 바꿔도 되는 근거(§5) |
