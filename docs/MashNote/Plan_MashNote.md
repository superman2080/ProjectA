# Plan — 연타 노트 (MashNote)

> 근거: `docs/MashNote/Research_MashNote.md`
> 요구: 아무 노드나 눌러 발동 · 지정 타수 달성 = 성공 · `Attacker.Player` 전용 ·
> 입력마다 클립의 `ImpactTime`부터 재생 · 타수까지는 입력마다 점수, 초과는 애니메이션만.

---

## 0. 설계 결정

### 0-1. 구조 — **후보 A(상속)를 쓰지 않는다.** 소비 규칙만 `ActivePattern` 안의 메서드 하나로 모은다

Research §6은 상속(`MashPattern : ActivePattern`)과 흩어진 `if` 둘을 놓고 저울질했다. **둘 다 안 쓴다**:

- 상속은 **구현이 둘뿐인 계층**을 만든다. 그리고 실제로 상속이 덮는 것은 §4-1·4-2 둘뿐이다 — 시각 계층(§4-4)은 `ActivePattern`이 아니라 **`Pattern` 에셋**을 읽으므로 어차피 `if`다.
- 대신 **소비 규칙(가장 조용히 어긋나는 부분)만** `ActivePattern.ConsumeMashHit()` 하나에 넣는다. `PatternHandler`에 남는 것은 "연타면 이 길로"라는 **갈림길 하나**다.

### 0-2. 데이터 — 타수는 **패턴**이, 창은 **채보**가 든다

```
Pattern.isMash          : bool          ← 이 패턴이 연타인가
Pattern.mashTargetHits  : int           ← 성공에 필요한 타수 (패턴의 성질)
Pattern.mashHitClips    : List<ClipAlignment>  ← 타격마다 번갈아 재생
SongChartEntry.onsetTimes = [시작, 끝]  ← 연타 창 (채보 순간의 성질)
```

- **`patternDatas`는 정확히 1칸**이다 = 게이지가 앉을 자리. **20타를 20칸으로 못 적는다** — `OnValidate`가 중복 인덱스를 에러로 막고 노드는 9개뿐이다(Research §3). 타수는 별도 필드일 수밖에 없다.
- **`onsetTimes`를 타수만큼 채우지 않는다.** 그러면 타수의 진실의 원천이 둘(패턴·채보)이 되어 어긋난 조합이 조용히 만들어진다. **두 값(시작·끝)만 적고 타수는 패턴에서 읽는다.** `Deadline = 끝 + goodWindow`가 그대로 성립하므로 `ActivePattern`은 안 고친다.
- **채보 스키마는 한 필드도 안 바뀐다.**

### 0-3. 완료 시각은 **언제나 `Deadline`** 하나다

일반 패턴은 마지막 노드 판정 자리에서 완료되고 다음 패턴이 **같은 프레임에 승계**된다. 연타가 타수 달성에서 완료되면 **초과 타격이 다음 패턴의 입력으로 흘러 들어가 그 패턴의 `AllCorrect`를 깬다**(Research §4-2). 성공이든 실패든 창 끝까지 판정 대상으로 남는다 — `ExpireOverduePatterns`가 이미 그 경로다.

### 0-4. 타이밍 판정이 없다 — 창 안의 모든 타격이 `Perfect`

연타는 "언제"가 아니라 "몇 번"을 묻는다. **⚠ 그래서 연타 구간은 점수 효율이 일반 패턴보다 좋다**(Good이 안 나온다). 밸런스는 `mashTargetHits`와 창 길이로 잡는다 — 판정으로 잡으면 "빨리 치면 손해"가 되어 연타가 아니게 된다.

### 0-5. 초과 타격은 **같은 클립 리스트를 계속 돈다**

"애니메이션만 변경되고 점수는 안 오른다" = **클립 리스트를 계속 순환**하고 `OnJudged`만 안 쏜다. 초과 전용 클립 슬롯을 따로 두지 않는다 — 필요해지면 리스트를 하나 더 다는 한 필드짜리 확장이다.

### 0-6. 타격 재생은 **정렬 대상이 아니다**

§6의 계약("임팩트 프레임이 `Deadline + ImpactOffset`에 온다")은 **역산할 시각이 있을 때만** 성립한다. 연타 타격 시각은 플레이어가 정하므로 역산 대상이 없다. `PlayOneShot`과 같은 성질의 **일회성 재생**이고, `startOffset`을 `ImpactTime`으로 주는 것이 곧 "임팩트부터 써는 모션"이다.

- **⚠ `isSwing: true`여야 한다** — `ApplyHitStop`의 가드가 `swingActive`를 본다(§7-3).
- **⚠ 그 결과 `impactAuthored = 0` → `DuelCurveTime`이 `NaN`**이 되어 결투 거리 커브(§11-9)가 연타 구간에 **구동되지 않는다**. 이것은 옳다(연타 중에 플레이어가 미끄러지면 안 된다) — 자동으로 맞는 지점이지만 문서에 남긴다.
- **⚠ 연타 패턴은 리드인 클립(§6-1)을 쓰지 않는다.** 타격이 `PlaySlot(continuesSequence: false)`라 시퀀스를 끊는다. `OnValidate`가 막는다.

### 0-7. 적은 연타의 **성패**를 모른다 (반응만 안다)

`EnemyDirector`는 `OnPatternComplete`의 성패 bool 하나만 본다 — `killOnSuccess`·사슬(§11-5)·후퇴·절단이 **한 줄도 안 고치고** 따라온다. 아래 두 절이 더하는 것은 **반응**뿐이고, 성패 경로는 그대로다.

### 0-8. 적은 **타격마다 젖혀진다**

- **클립은 `Pattern.EnemyHit`을 그대로 쓴다**(§11-5). 그 슬롯의 정의가 이미 "맞았는데 죽지 않은 적의 리액션"이라 연타 타격과 **의미가 정확히 같다** — 새 슬롯을 만들면 같은 뜻의 필드가 둘이 된다.
- **⚠ 시각을 예약하지 않는다.** `Resolve`의 리액션은 `impactTime − ReactionLead`에 예약하는데(확정이 칼보다 `goodWindow`만큼 이르기 때문), 연타 타격은 **맞는 순간이 곧 지금**이라 예약할 것이 없다. 즉시 재생이 옳다.
- **경로는 `EnemyDirector`를 거친다.** 뷰를 `PatternHandler`가 직접 부르면 "적이 어디에 몇이나 있는지는 `EnemyDirector`만 안다"(§11)가 깨진다. 디렉터가 `OnMashHit`을 구독해 `currentOpponent`에게 넘긴다.
- **⚠ 클립은 재생을 끝내지 못한다.** 초당 8타면 타격 간격이 0.125초라 **보이는 것은 클립의 앞 ~0.12초뿐**이고 계속 처음부터 다시 시작한다. 그게 샌드백의 그림이지만 **저작은 그 전제로 해야 한다** — 젖혀지는 동작이 클립 맨 앞에 와야 한다(§11-5의 "임팩트에 시작한다"와 같은 규율이라 기존 `EnemyHit` 저작 관례와 충돌하지 않는다).
- **⚠ 적은 히트스톱에 안 언다**(Step 7이 `isMainImpact: false`다) — 적 클립도 같은 `impactAlignTime`에 정렬돼 있어 함께 얼리면 적 쪽 정렬만 밀린다(§7-3-1). 연타에서는 얼릴 정렬이 없지만 **규칙을 예외 없이 유지**한다.

### 0-9. 연타 실패는 **물러나지 않는다** — 제자리 패링으로 받는다 (창을 보지 않는다)

`ResolveRetreatDistance`는 `Attacker.Player` 실패에서 **다음 패턴의 창이 후퇴+재접근을 감당할 때만** 물러나고, 못 감당하면 제자리 패링한다(§11-2). **연타는 그 판단을 건너뛰고 언제나 거리 0**이다 — 즉 **언제나 제자리 패링**이다.

- 거리 0이 곧 패링이라는 규칙은 이미 `EnemyView.Resolve` 안에 있다(`parried = !playerSucceeded && attacker != Enemy && distance <= 0f`). `reactionClip` 선택식도 `retreat <= 0f ? EnemyParry : null`이라 **`Pattern.EnemyParry`가 자동으로 골라진다.** 배선이 비면 뷰의 `parryStateName`으로 폴백한다(§11-5 기존 동작 그대로).
- **⚠ 새 코드가 사실상 0줄이다** — 창 판단을 건너뛰는 한 줄뿐이고, 패링 그림·클립 선택·폴백이 전부 기존 경로다.
- **그리고 `docs/FailConverge/`의 함정을 통째로 피한다.** 후퇴를 선택했다면 재접근이 `TakeTargetForWindow`를 안 거쳐 거리가 창에 안 맞춰지고, 다음 패턴이 촘촘할 때 플레이어가 **0.9 m/s로 기어서** 돌아갔을 것이다. 제자리면 이동 거리가 `convergeMinDistance`(0.15m) 아래라 **수렴 로코모션을 아예 안 건다** — 연타 뒤 채보 간격에 제약이 붙지 않는다.
- **⚠ 연타의 실패 그림은 "밀어냈다"가 아니라 "버텨냈다"**가 된다. 긴 창 동안 두들겼는데 타수가 모자라 적이 **제자리에서 칼을 받아 넘기는** 그림이다 — 요구대로다.

### 0-10. 절단은 자동으로 따라온다 — 다만 **마무리 일격 모션**은 창을 하나 요구한다

**절단 경로는 한 줄도 안 고친다.** 연타 성공 → `OnPatternComplete(AllCorrect: true)` → `ResolveReservation` → `killOnSuccess && chainSuccesses >= RequiredHits(1)` → `KillOpponent(..., r.impactTime, EnemyDeath)` → `AssignDeath` → `pendingKills` → 임팩트 프레임에 시체 교체·폭발(§11-3). **연타는 이 사슬 어디에도 등장하지 않는다.**

- 절단 시각 = `r.impactTime` = `Deadline + ImpactOffset` = **연타 창 끝**. ✓
- 사망 클립(`Pattern.EnemyDeath`)·히트스톱·카메라 쉐이크·임팩트음이 전부 그 한 시각에 붙는다. ✓
- 절단 각도는 `Pattern.BladePlane`으로 고르는데(§11-3), 그 평면은 **`playerAttack`의 임팩트 프레임에서 유도**된다 — 아래 마무리 일격이 곧 그 슬롯이다. 비면 적 정의의 기본 세트로 폴백한다(기존 동작).

**⚠ 그런데 마무리 일격 모션이 끊긴다.** `SchedulePendingSuccess`는 판정 대상이 되면 **패턴 종류와 무관하게** `PlayerAttack`을 임팩트에 정렬해 예약한다 — 연타에서도 그대로 돌아 **창 끝에 마무리 베기가 들어온다**(뮤즈대쉬의 샌드백 → 마무리 일격 그림이 공짜로 나온다). 문제는 그 뒤다:

> 마무리 클립은 `impact − 와인드업`에 시작하는데, **플레이어는 초과 타격을 계속 하도록 권장된다**(§0-5). 타격 하나하나가 `PlaySlot`이라 **마무리 일격을 매번 처음부터 끊는다** → 와인드업이 완주하지 못한 채 임팩트가 도착하고, **칼이 지나가지 않았는데 몸이 갈라진다**.

**해결: 연타 입력은 `Deadline`이 아니라 마무리 클립이 시작하는 시각에 닫는다.**

```
연타 입력 마감 = Deadline − FinisherLead
FinisherLead   = ClipSequence.AuthoredImpactSpan(PlayerAttack)   ← §6-1에서 이미 만든 함수
```

- **새 저작 필드가 0개다.** 와인드업은 이미 `playerAttack`의 트림·임팩트·배속에 들어 있고, 그 값을 뽑는 함수가 클립 시퀀스에서 이미 나왔다. 손으로 적으면 두 값이 언젠가 갈라진다.
- **`playerAttack`이 비면 `FinisherLead = 0`** → 마감이 곧 `Deadline`이고 **마무리 일격이 없다**(연타 모션 그대로 끝나고 절단만 일어난다). 저작자가 슬롯을 비우는 것이 곧 "마무리 없음"이라 **분기가 아니라 데이터로 갈린다**.
- **⚠ 링 수축도 이 마감에 맞춘다.** 창 전체로 그리면 **링이 거짓말을 한다** — 아직 줄고 있는데 입력이 안 먹는다.
- **⚠ 그래서 `mashTargetHits`는 창 전체가 아니라 마감까지 안에 들어가야 한다.** 굽기 툴이 그 값으로 `타/초`를 계산한다(Step 11).
- 마감 뒤의 입력은 **조용히 무시한다**(`OnJudged`도 `OnMashHit`도 안 난다). 이미 목표를 채웠으면 성공이 확정된 상태라 잃는 것이 없다.

---

## 1. 구현 단계

### - [x] Step 1 — `Pattern`에 연타 데이터

- `isMash` · `mashTargetHits`(`[Min(1)]`) · `mashHitClips`(`List<ClipAlignment>`) + 접근자.
- `IsMash` / `MashTargetHits` / `MashHitClips` / `HasMashHitClips`.
- `OnValidate` — 연타일 때만 보는 넷:
  - `patternDatas`가 정확히 1칸이 아니면 **에러**(게이지 자리는 하나다).
  - `attacker != Player`면 경고(요구가 `Attacker.Player` 전용이다).
  - `playerLeadInClips`가 비어 있지 않으면 경고(§0-6).
  - `mashHitClips`가 비면 경고(무연출 연타 = 아무 일도 안 일어난다).
- **`playerAttack`은 연타에서 '마무리 일격'을 뜻한다**(§0-10) — 비워도 되고(마무리 없음), 채우면 창 끝에 정렬돼 들어온다. `MashInputDeadlineLead => ClipSequence.AuthoredImpactSpan(playerAttack)` 접근자를 연다.
- 각 `mashHitClips` 원소에 `ValidateImpactTime(this, $"MashHit[{i}]")`.

### - [x] Step 2 — `ActivePattern`에 타수 상태

```csharp
public bool IsMash => Template.IsMash;
public int MashHits { get; private set; }
public bool MashReached => MashHits >= Template.MashTargetHits;

/// 타격 하나를 소비한다. 목표 이내면 true(= 점수 대상), 초과면 false(= 애니메이션만).
public bool ConsumeMashHit()
{
    MashHits++;
    return MashHits <= Template.MashTargetHits;
}
```

- **`MashInputDeadline`도 여기서 든다** = `Deadline − Template.MashInputDeadlineLead`(§0-10). 마감 시각을 아는 곳이 하나여야 입력 가드와 링 수축이 갈라지지 않는다.
- **`IsComplete`가 연타에서는 `MashReached`**다. `ExpireOverduePatterns`의 `if (!expired.IsComplete) MarkIncorrect()`가 그대로 "타수 미달 = 실패"가 된다 — **성패 판정 코드를 새로 쓰지 않는다.**
- `ExpectedPointIndex`·`ExpectedTime`은 연타에서 **부르지 않는다**(§Step 3이 그 길로 안 간다). 방어적으로 `CurrentPosition`을 0에 고정해 둔다.

### - [x] Step 3 — `PatternHandler`: 입력과 판정

**`SetPattern`** — 입구 검증과 링 예약이 갈린다:

```
연타: inputTimes.Count == 2 (시작·끝) 이어야 한다. 아니면 에러 후 리턴.
      링을 노드마다 예약하지 않고 '하나'만 — 자리는 AllData[0].index,
      ⚠ 수축 시간 = MashInputDeadline − 시작 (창 전체가 아니다, §0-10).
일반: 지금 그대로.
```

**`OnPointPressed`** — 연타가 판정 대상이면 **중복 가드와 통과 노드를 건너뛴다**:

- `connectedIndices.Contains` 가드는 "같은 Point는 패턴당 한 번"이라 **연타 2타부터 전부 삼킨다**(Research §4-3).
- `GetPassThroughIndex → ForceDown`은 한 번 입력이 **두 타로 세어지게** 한다.
- `AppendPointToLine`도 건너뛴다(연타는 획이 아니다).

**`AddPattern`** — 연타는 별도의 길로 간다:

```
if (target.IsMash) { HandleMashHit(index, target); return; }
... 기존 경로 그대로
```

```
HandleMashHit(index, target):
    // ⚠ 마무리 일격이 시작된 뒤의 입력은 조용히 버린다 — 안 그러면 매 타격이 그 클립을
    //    처음부터 끊어 '칼이 안 지나갔는데 몸이 갈라지는' 그림이 된다(§0-10).
    if (Time.time >= target.MashInputDeadline) return;

    bool scored = target.ConsumeMashHit();
    patternPoints[index].SetJudgementColor(Perfect);
    if (scored) OnJudged(Perfect, index);        ← 점수·콤보가 자동으로 오른다
    OnMashHit(new MashHitInfo(...));             ← 애니메이션·히트스톱·게이지·적·이펙트가 이걸 듣는다
    // ⚠ 완료 판정을 하지 않는다(§0-3). 링도 회수하지 않는다.
```

**⚠ 완료는 `ExpireOverduePatterns`만** — `AddPattern`의 `if (target.IsComplete) CompletePattern` 가드에 `&& !target.IsMash`를 더한다.

**새 이벤트**: `event Action<MashHitInfo> OnMashHit`. 인자를 나열하지 않고 **구조체 하나**를 넘긴다 — `PatternCompletionInfo`·`PatternQueuedInfo`·`JudgeTargetInfo`와 같은 관례이고, 구독자가 다섯이라 시그니처가 흔들리면 전부 깨진다.

```csharp
public readonly struct MashHitInfo
{
    public readonly Pattern Template;   // ⚠ 적·이펙트가 이걸 요구한다(Step 8·9)
    public readonly int PointIndex;
    public readonly bool Scored;        // 목표 이내인가(= 점수가 올랐는가)
    public readonly int Hits;           // 이번 타격까지의 누적 타수
    public readonly int Target;         // 목표 타수 — 게이지가 '현재/목표'를 그리는 데 쓴다
}
```

**⚠ `Template`이 페이로드에 있는 것이 이 구조체의 존재 이유다.** 없으면 `EnemyDirector`(리액션 클립)와 `PatternEffectDirector`(큐 목록)가 각자 `pendingTokens.Peek()`으로 **"지금 판정 대상"을 다시 유도**해야 하는데, 그 셋이 어긋나는 순간을 디버깅하게 된다(§11-1이 상대 배정에서 이미 겪은 부류다).

### - [x] Step 4 — `PatternHandler`: 시각 계층

연타는 "아무 데나 눌러라"이므로 셋이 갈린다:

| 메서드 | 연타에서 |
|---|---|
| `ApplyHitAreas` | **9개 전부 비율 1** (지금은 패턴이 안 쓰는 Point를 축소한다) |
| `ApplyKnobVisibility` | 연타가 큐에 있으면 **9개 전부 켠다** (합집합에 9개를 통째로 더한다) |
| `ShowGuideLine` | **끈다** (이을 순서가 없다) |

### - [x] Step 5 — `FocusRingView`에 타수 라벨

- `SetLabel(string)` 하나 추가 — `indexLabel`을 켜고 텍스트를 쓴다.
- ⚠ 일반 패턴의 `IndexLabel`은 계속 **비활성**이다(§4 — 링이 둘 겹치면 숫자가 뭉개진다). **연타는 링이 하나뿐이라 그 근거가 성립하지 않는다.**
- `PatternHandler`가 `OnMashHit`에서 `남은 타수` 또는 `현재/목표`를 갱신한다. 링 수축이 남은 시간을, 라벨이 남은 타수를 말한다 — **두 정보가 한 자리에 모인다.**

### - [x] Step 6 — `CharacterActionPlayer.PlayMashHit`

```csharp
/// 연타 타격 하나. 정렬 대상이 아니라 '임팩트부터 끝까지'를 일회성으로 재생한다(§0-6).
public void PlayMashHit(ClipAlignment alignment)
{
    if (alignment == null || !alignment.IsUsable) return;

    float from = alignment.ImpactTime > 0f ? alignment.ImpactTime : alignment.StartOffset;
    float end  = alignment.StartOffset + alignment.ResolvedDuration;
    if (end - from <= 0f) return;

    PlaySlot(alignment.Clip, from, end - from, alignment.Speed, isSwing: true);
}
```

- `PlaySlot(continuesSequence: false)`이라 **진행 중이던 시퀀스·예약이 자연히 끊긴다** — §6-1에서 이미 그 경로를 하나로 모아 뒀다.
- 구독은 `CharacterActionPlayer`가 직접 한다(`handler.OnMashHit`) — 클립 선택은 `Pattern.MashHitClips`를 **번갈아**(`hitClips`의 `hitIndex` 관용구 그대로). 커서는 패턴이 끝날 때 0으로 되돌린다.

### - [x] Step 7 — `HitStopDirector`: 타격마다 정지

- `handler.OnMashHit`을 구독해 `Schedule(Time.time, isMainImpact: false)`.
- **⚠ 새 노브를 만들지 않는다.** `minHitStopGap`(기본 = `hitStopDuration` 0.1초)이 이미 "너무 촘촘한 예약은 버린다"이고, 연타(초당 5~10타)가 정확히 그 대상이다 — **연타 정지 대부분이 버려지는 것이 의도된 동작**이다(전부 멈추면 화면이 슬라이드쇼가 된다).
- `isMainImpact: false`라 **절단을 밀지 않고 적도 안 얼린다**(§7-3-1) — 연타 중 적은 자기 정렬을 유지한다.

### - [x] Step 8 — 적: 타격마다 젖혀지기 + 연타 실패는 제자리 패링

**`EnemyView.PlayMashReaction(ClipAlignment reaction)`** — 예약 없는 즉시 리액션(§0-8):

```csharp
/// 연타 타격 하나에 대한 즉시 반응. Resolve의 리액션과 달리 <b>예약하지 않는다</b> —
/// 맞는 순간이 곧 지금이라 정렬할 시각이 없다.
public void PlayMashReaction(ClipAlignment reaction)
{
    if (Current == Phase.Dying) return;

    if (reaction != null && reaction.IsUsable) PlayAttack(reaction, reaction.Speed);
    else CrossFadeReaction(knockBackStateName);

    reactionUntil = Time.time + Mathf.Max(reactionHoldDuration, 0f);
}
```

- **재생 경로는 `TickPendingReaction`(1006~1013행)과 같은 두 줄**이다 — `PlayAttack` + `reactionUntil`. 예약 부분만 빠진 것이라 **새 재생 방식이 아니다.**
- **⚠ `Current`(Phase)를 건드리지 않는다.** `Resolve`는 `Phase.Recover`로 넘기지만 그건 교전이 끝났다는 뜻이다 — 연타는 아직 진행 중이라 넘기면 견제(`EnemyFeint`)·이동 상태가 끊긴다.
- **⚠ `StopWander`도 부르지 않는다.** 현재 상대는 배회 중이 아니고, `reactionUntil`이 이미 `ApplyLocomotion`·`TickUpperBody`(§11-7)를 막는다.

**`EnemyDirector`** — `handler.OnMashHit`을 구독해 현재 상대에게 넘긴다:

```
HandleMashHit(MashHitInfo info):
    currentOpponent?.PlayMashReaction(info.Template?.EnemyHit);
```

- **템플릿은 페이로드가 준다**(Step 3의 `MashHitInfo`) — 디렉터가 `pendingTokens.Peek()`으로 다시 유도하지 않는다.
- **⚠ `info.Scored`를 보지 않는다.** 초과 타격에서도 적은 젖혀져야 한다("애니메이션만 나온다"의 적 쪽 절반이다).

**`ResolveRetreatDistance`** — 한 줄 추가(§0-9):

```csharp
if (playerSucceeded) return 0f;

// 연타 실패는 물러나지 않는다 — 제자리 패링으로 받는다(창을 보지 않는다).
if (r.template != null && r.template.IsMash) return 0f;
```

- **거리 0 하나로 셋이 따라온다** — `EnemyView.Resolve`의 `parried`가 true가 되고, `reactionClip` 선택식이 `Pattern.EnemyParry`를 고르며, 비어 있으면 `parryStateName`으로 폴백한다. **패링 그림에 새 코드가 0줄이다.**
- **⚠ 이 줄은 창 판단(`arriveTime` 계산)을 건너뛰기 위해서만 있다.** 없으면 연타 실패가 **창에 따라 어떤 때는 물러나고 어떤 때는 패링**한다 — 같은 사건이 매번 다르게 보이는 것이 문제다.
- `playerSucceeded` 가드와 반환값이 같아 위치는 기능상 무관하지만, **실패 절에 둔다**(연타 성공의 0은 "사슬 중간이라 넉백 없음"이고 실패의 0은 "제자리 패링"이라 이유가 다르다).

### - [x] Step 9 — 타격마다 월드 이펙트 (`EffectTiming.MashHit`)

**⚠ 이 단계는 초안에서 "범위 밖"으로 잘못 적혀 있었다.** 근거가 "큐 모델이 예약 가능한 시각을 전제한다"였는데, **예약할 수 없는 것과 발사할 수 없는 것은 다르다** — `PatternEffectDirector.Fire(PatternEffectCue)`는 **큐 하나만 받는 독립 메서드**다(예약도 `fireTime`도 안 본다). 앵커 조회·풀 대여·포즈·배속·소리·히트스톱 동결이 전부 그 안에 이미 있다.

**`EffectTiming`에 값을 하나 더한다** — 기존 다섯(`PatternStart`/`FirstNode`/`Node`/`LastNode`/`Impact`)과 성질이 다르다: 나머지는 **시각을 계산**하지만 이것은 **사건에 붙는다**.

- **⚠ 반드시 enum 끝에 더한다.** `EffectTiming`은 명시 정수가 없어 서수가 곧 직렬화 키다 — 중간에 끼우면 **기존 패턴의 모든 큐가 한 칸씩 밀린다**.
- `PatternEffectCue.ResolveTime`은 이 값에서 **부르지 않는다**(부를 시각이 없다). `HandlePatternQueued`가 먼저 걸러낸다:
  ```
  if (cue.Timing == EffectTiming.MashHit) continue;   // 예약을 만들지 않는다
  ```
- **조건은 `Always`만 유효하다.** 타격 순간에는 성패가 아직 안 정해졌다(§0-3 — 연타는 `Deadline`에 끝난다). `NeedsOutcome`인 `MashHit` 큐는 런타임이 조용히 건너뛰고 `OnValidate`가 경고한다 — §7-4의 "결과 조건 큐는 `LastNode`보다 이를 수 없다"와 **같은 종류의 검증**이다.

**발사**: `PatternEffectDirector`가 `handler.OnMashHit`을 구독한다.

```
HandleMashHit(MashHitInfo info):
    foreach (cue in info.Template.EffectCues)
        if (cue.IsUsable && cue.Timing == MashHit && !cue.NeedsOutcome) Fire(cue);
```

- **⚠ `scored`를 보지 않는다** — 초과 타격에도 이펙트가 뜬다(Step 8의 적 반응과 같은 판단: "애니메이션만 나온다"의 화면 쪽 절반이다).
- **공짜로 따라오는 것들**: `EffectAnchor.Opponent`가 **발사 순간에** 조회되므로(§7-4) 현재 상대에 정확히 붙는다 · `PlayerWeapon` + `bladeT`가 휘두르는 칼날을 따라간다 · `Sfx`가 실려 있으면 **타격음도 같이** 난다 · `Prewarm`이 `Timing`을 안 보므로 **풀 예열이 이미 된다** · 히트스톱 창 안에서 태어난 것도 같이 언다(`if (frozen) view.OverrideSpeed(0f)`).
- **⚠ 풀 크기를 크게 잡아야 한다.** 초당 8타 × 이펙트 수명이 동시 인스턴스 수다 — `poolSize`가 모자라면 곡 도중 `Instantiate`가 나고 그 히치가 그대로 판정 손실이다(§5). 툴이 `poolSize × 8/초` 기준으로 경고한다.

**저작 툴**(`Pattern Effect Tool`): `MashHit` 큐는 타임라인에 **놓을 자리가 없다** — 막대 대신 `타격마다` 배지로 표시하고 스크럽에서 제외한다. 프리뷰는 버튼 하나로 1회 발사.

### - [x] Step 10 — `ScoreDirector`: 두 줄

```
CountChart:              totalNotes += t.IsMash ? t.MashTargetHits : entry.onsetTimes.Length;
HandlePatternComplete:   nodeCount   = t.IsMash ? t.MashTargetHits : t.AllData.Count;
```

- **같은 속성을 두 번 읽는 대칭 구조**라 하나만 고쳐지는 일이 눈에 띈다.
- `ScoreMath`는 **한 줄도 안 고친다** — `MissedNotes(목표타수, 친타수)`가 그대로 "못 채운 타수"다.
- 초과 타격은 `OnJudged`가 안 나므로 **점수·콤보에 자동으로 안 잡힌다**(§0-5).

### - [x] Step 11 — 저작 툴 (`PatternChartWindow`)

- 연타 엔트리 행에 **`연타 N타` 배지**.
- 경고 셋: `onsetTimes`가 2개가 아님 / 창 길이가 `mashTargetHits ÷ 최대연타속도`보다 짧음(= 물리적으로 불가능) / `killOnSuccess`가 꺼져 있는데 연타임(사슬 안에 연타를 섞는 것은 이번 범위 밖).
- **입력 마감까지의 길이와 요구 타수를 나란히** 보여 준다 — `20타 / 2.4초(마감까지) = 8.3타/초`. ⚠ 창 전체가 아니라 **마무리 일격 와인드업을 뺀 값**이다(§0-10). 저작자가 판단할 숫자 하나면 된다(§6-1의 `저작 N.NN s` 표시와 같은 결).

### - [x] Step 12 — 검증

**⚠ 유닛테스트를 만들지 않는다.** 이 기능에는 순수 계산 표면이 없다 — 소비 규칙이 `hits <= target` 한 줄이고, 나머지는 전부 **이벤트 순서와 분기 누락**이라 테스트로 잡히지 않는다. 대신 두 겹으로 막는다:

1. **에디터 검증**(Step 1의 `OnValidate` 넷 + Step 9의 경고 셋) — 잘못된 데이터가 런타임에 도달하지 못한다.
2. **런타임 체크리스트**(문서에 남긴다):
   - 같은 Point를 연타 → 2타부터 삼켜지지 않는가
   - 1→3 대각선으로 그으면 2가 자동 입력되지 않는가
   - 목표 타수 도달 후에도 **판정 대상이 유지**되는가(다음 패턴이 오염되지 않는가)
   - 초과 타격에서 **점수·콤보가 안 오르고 애니메이션은 나오는가**
   - 창 종료 시 성패가 적에게 전달되는가(`killOnSuccess` 동작) — **적이 실제로 절단되는가**
   - **마무리 일격이 끊기지 않고 완주해 임팩트에 칼이 지나가는가**(§0-10) — 창 끝까지 연타를 계속하면서 확인
   - `playerAttack`을 비운 연타에서 절단만 일어나는가(마무리 없음이 정상 경로)
   - 연타 직후 다음 패턴의 정렬이 안 밀리는가(§0-6의 시퀀스 끊김)
   - **타격마다 적이 젖혀지는가 · 이펙트가 뜨는가** — 초과 타격에서도 둘 다 나오는가
   - 연타 내내 이펙트 풀이 모자라지 않는가(곡 도중 `Instantiate` = 히치)
   - **연타 실패 시 적이 제자리에서 패링하는가**(물러나지 않는가) — `Pattern.EnemyParry`가 재생되는가, 비었으면 `parryStateName`으로 폴백하는가

### - [x] Step 13 — 문서

- `CLAUDE.md`에 **§2-1 "연타 노트"** 절 추가(§2 판정 시스템 바로 뒤). 담을 것: 완료는 언제나 `Deadline` · 타수는 패턴이 창은 채보가 · 타이밍 판정 없음 · 타격은 정렬 대상 아님 · 적은 성패를 모르고 반응만 한다 · 연타 실패는 언제나 제자리 패링 · `EffectTiming.MashHit`은 시각이 아니라 사건에 붙는다 · 절단은 자동이고 `playerAttack`이 마무리 일격이며 입력 마감이 그만큼 앞당겨진다.
- `docs/MashNote/`에 최종 결정 반영.

---

## 2. 안 하는 것 (범위 밖임을 명시한다)

| 안 함 | 이유 · 붙일 자리 |
|---|---|
| 초과 타격 전용 클립 슬롯 | §0-5 — 리스트 순환으로 충분하다. 필요하면 한 필드 확장 |
| 연타 전용 히트스톱 노브 | §Step 7 — `minHitStopGap`이 이미 그 노브다 |
| 사슬(§11-5) 안의 연타 | 사슬은 짧은 패턴을 잇는 리듬이고 연타는 긴 창이다. 섞으면 `chainKillRatio` 셈이 흔들린다 |
| 연타 타이밍 판정(Perfect/Good) | §0-4 |
| 굽기 툴의 연타 자동 생성 | 음악의 온셋이 연타를 정하지 않는다. **손으로 만드는 것**이 맞다 |

---

## 3. 회귀 위험 지점

1. **`if (target.IsMash)` 분기 누락** — Research §4가 센 일곱 군데 중 하나라도 빠지면 증상이 다 다르다(링 20개 / 2타부터 삼킴 / 다음 패턴 오염 / 가이드라인 헛것).
2. **`AddPattern`의 완료 가드** — `&& !target.IsMash`를 빠뜨리면 §0-3이 통째로 무너진다. **이 한 줄이 가장 위험하다.**
3. **`ScoreDirector`의 두 줄이 짝을 이루는가** — 하나만 고치면 총 노트와 뺄셈이 어긋나 **달성도가 1을 못 넘거나 넘어 버린다**(`SSS`가 영영 안 나온다).
4. **`OnMashHit` 구독자가 다섯**(캐릭터·히트스톱·게이지·적·이펙트) — 패턴이 끝날 때 클립 커서를 안 되돌리면 다음 연타가 중간 클립부터 시작한다.
5. **연타가 `Attacker.Enemy`로 저작되는 것** — `CharacterActionPlayer`가 `PlayerParry`를 고르고 `EnemyAttack`이 재생돼 그림이 통째로 어긋난다. Step 1의 경고가 유일한 방어선이다.
6. **`PlayMashReaction`이 `Phase`를 건드리는 것** — `Resolve`를 복사하다 `Current = Phase.Recover`까지 따라오면 **연타 도중 견제·이동이 끊긴다**(§Step 8).
7. **`EffectTiming.MashHit`을 enum 중간에 끼우는 것** — 명시 정수가 없어 서수가 곧 직렬화 키다. **기존 패턴의 모든 이펙트 큐가 한 칸씩 밀린다**(§Step 9).
8. **`HandlePatternQueued`에서 `MashHit` 큐를 안 거르는 것** — `ResolveTime`이 의미 없는 시각을 돌려줘 **타격과 무관한 때에 한 번 더 뜬다**.
9. **연타 입력 마감을 `Deadline`으로 두는 것** — 초과 타격이 마무리 일격을 매번 끊어 **칼이 안 지나갔는데 몸이 갈라진다**(§0-10). 링 수축까지 같은 값을 봐야 한다.
10. **`ResolveRetreatDistance`의 새 줄을 빠뜨리는 것** — 없어도 컴파일되고 대개는 패링이 나오지만, 다음 패턴의 창이 넉넉한 엔트리에서만 **적이 물러난다**. 같은 사건이 채보 위치에 따라 다르게 보이는데 원인을 짚기 어렵다.

---

## 4. 확정된 질문 (Research §7 여덟 개 전부)

| 질문 | 답 | 어디 |
|---|---|---|
| 초과 타격 "애니메이션만 변경"의 뜻 | 같은 리스트를 계속 순환, `OnJudged`만 안 쏨 | §0-5 |
| 타격 클립이 하나인가 여럿인가 | 리스트, 번갈아 재생(`hitClips` 관용구) | §0-2 · Step 6 |
| **적이 연타마다 반응하는가** | **한다.** `Pattern.EnemyHit`을 예약 없이 즉시 재생 | §0-8 · Step 8 |
| 타격마다 히트스톱을 거는가 | 건다. `minHitStopGap`이 촘촘한 것을 버리는 것이 의도 | Step 7 |
| 타이밍 판정이 있는가 | 없다. 창 안이면 전부 Perfect | §0-4 |
| **연타 실패의 그림** | **언제나 후퇴**(`evadeStateName`). 창을 보지 않는다 | §0-9 · Step 8 |
| 시각 표현 | `FocusRingView` 하나 + 타수 라벨 | Step 5 |
| 리드인 클립(§6-1)을 쓸 수 있는가 | 못 쓴다. `OnValidate`가 막는다 | §0-6 · Step 1 |

**남은 결정은 없다 — 이 Plan은 구현 가능한 상태다.**

---

## 5. 구현 후 기록 (Plan 대비 달라진 것)

1. **`MashInputDeadline`을 `ActivePattern`이 든다** — Plan은 `PatternHandler`가 계산하는 것처럼 읽혔는데, 입력 가드와 링 수축이 **같은 값**을 봐야 "아직 줄고 있는데 입력이 안 먹는" 거짓말이 안 생긴다. 마감 시각의 주인이 하나여야 한다.
2. **`ScheduleNodeRings` / `ScheduleMashRing`으로 쪼갰다.** `SetPattern` 안에 `if`를 넣는 대신 링 예약을 통째로 두 메서드로 나눴다 — 연타 링은 개수·자리·수축 시간이 전부 달라 한 루프 안에서 분기하면 읽히지 않는다.
3. **`FocusRingView.OnDespawn`에서 라벨을 끈다.** 안 끄면 **다음 대여가 남의 타수를 달고 나온다**(일반 패턴 링에 숫자가 보인다). 풀 재사용의 기존 함정과 같은 자리다.
4. **`PatternEffectWindow`를 다섯 군데 막았다.** Plan은 "배지로 표시하고 스크럽에서 제외"만 적었는데, `ResolveTime`이 `MashHit`에서 `default:`로 떨어져 **임팩트 시각을 돌려준다** — 목록·스크럽 범위·타임라인 막대·프리뷰 파티클·상세 패널이 전부 그 값을 쓰고 있었다.
5. **`PatternChartWindow.RecomputeSpawnTimes`도 갈라야 했다.** Plan에 없던 지점 — 연타는 온셋이 2개인데 템플릿 노드가 1개라 **"노드 수 불일치"로 저장이 막혔다**.
6. **`Pattern.ValidateEffectCues`에 `IsEventDriven` 가드를 넣었다.** 가짜 시각으로 순서를 검사하는 로직이 `MashHit`에서 뜻 없는 경고를 냈다.

**검증**: EditMode **188/188 통과**, 컴파일 에러 0. 런타임 체크리스트(§Step 12)는 실제 연타 패턴 에셋을 만들어 확인해야 한다 — 아직 안 했다.
