# Research — 기습 예보 (AmbushLookahead)

목표: **모든 기습 경고를 언제나 같은 시간(기본 1.0초) 동안 보여준다.** 지금은 0.4~0.8초 사이에서
공백 길이에 따라 흔들리고, 하한 0.4초는 반응하기에 너무 짧다.

관련: `docs/EnemyAmbushDodge/` (기습·회피 본체) · `docs/AmbushVisibility/` (가시성 — 닷지 포인트 고정·아웃라인)

---

## 1. 지금 구조 — 왜 시간이 흔들리나

`DodgeDirector.HandleIdleWindow(start, end)` 한 곳에서 전부 정해진다:

```csharp
// DodgeDirector.cs:240~282
float standing = end - Mathf.Max(start, Time.time);
float impact   = end - (rollMoveDuration + margin);   // 0.25 + 0.2
float lead     = impact - Time.time;
...
ringDuration = Mathf.Clamp(lead, minRingExposure, maxRingExposure);  // 0.4 ~ 0.8
fireTime = Time.time;                                  // 즉시 발동
```

**결정 시점이 공백이 열리는 순간이다.** 그래서 `lead`가 공백 길이에 갇히고, 노출도 그만큼만 나온다.

### 1-1. 공백은 어디서 오나

`CharacterActionPlayer.RaiseIdleWindow` (`CharacterActionPlayer.cs:660`):

```csharp
float start = Mathf.Max(Mathf.Max(recoveryEndTime, convergeUntil), Time.time);
float end   = hasPending ? pendingScheduleStart : info.Deadline;
```

`pendingScheduleStart`는 바로 위 `SchedulePendingSuccess`가 정한다 (`:709`):

```csharp
float playTime = pendingImpactSpan / pendingBaseSpeed;
pendingScheduleStart = Mathf.Max(impactAlignTime - pendingFreeze - playTime, info.FirstNodeTime);
```

호출 순서는 `HandleJudgeTargetBegan` 안에서 **예약 → 이벤트**로 고정돼 있다(`:637` 주석).
즉 **공백의 끝 = 다음 공격 클립이 시작되는 시각**이고, 그 값은 판정 대상이 승계되는 순간에야 확정된다.

### 1-2. 그래서 `minRingExposure`를 못 올린다

```csharp
// DodgeDirector.cs:328
private float RequiredLead(ClipAlignment clip) =>
    Mathf.Max(clip.ResolvedImpactSpan / clip.Speed, minRingExposure);
```

노출 하한이 **발동 자격**에 직접 들어간다. 1.0으로 올리면 `lead ≥ 1.0`인 공백에서만 뜬다 —
실측(`Dreamer_Lv10`)에서 공백 ≥0.8초가 25%뿐이라 기능이 사실상 죽는다.
`docs/AmbushVisibility/Plan_AmbushVisibility.md` Step 4가 "min은 건드리지 마라"라고 못박은 근거가 이것이다.

---

## 2. 결정적 관찰 — 노출은 공백 안에 있을 필요가 없다

공백(`standing`)에 **정말로** 요구되는 것은 셋뿐이다:

| 항목 | 왜 공백이어야 하나 |
|---|---|
| `dodgeWindow` (0.15) | 입력 마감 |
| `rollMoveDuration` (0.25) | 구르기 이동이 다음 스윙의 기하가 얼기 전에 끝나야 한다 |
| `margin` (0.2) | 여유 |

**링 노출은 여기 없다.** 링은 루트 Canvas가 `ScreenSpaceOverlay`라 카메라·플레이어 이동과 완전히 독립이고,
`DodgeDirector.HandleIdleWindow`의 주석이 이미 그렇게 적고 있다:

> ⚠ 즉시 발동이다(정정 5) — start를 기다리면 와인드업이 창 안으로 되돌아온다.
> **적 모션과 링은 플레이어가 대시 중이어도 성립한다**(링은 ScreenSpaceOverlay라 이동과 무관하다).

같은 이유로 **적 텔레그래프 클립도 공백 밖에서 시작해도 된다** — 이미 그렇게 하고 있다(`fireTime = Time.time`,
공백 시작 `start`를 기다리지 않음).

> **결론: 노출을 늘리려면 공백을 늘릴 게 아니라 '결정'을 앞당기면 된다.**
> 공백에 남는 요구는 `minIdleWindow`(0.6초)뿐이고 그건 지금과 똑같다 — **발동 빈도가 안 깎인다.**

---

## 3. 예보에 필요한 값이 이미 전부 정적 데이터인가

공백의 끝을 미리 알려면 §1-1의 식을 미리 계산해야 한다. 입력을 하나씩 보면:

| 값 | 출처 | 미리 알 수 있나 |
|---|---|---|
| `impactAlignTime` = `Deadline + ImpactOffset` | `Deadline` = 마지막 온셋 + `goodWindow`, `ImpactOffset` = `Pattern.ImpactOffset` | **예** — 채보 + 패턴 에셋 |
| `playTime` = `ResolvedImpactSpan / Speed` | `Pattern.PlayerAttack` 또는 `PlayerParry`의 `ClipAlignment` | **예** — 어느 슬롯인지는 `Pattern.Attacker`가 정하고 그것도 패턴 소유(§5) |
| `pendingFreeze` | `ClipAlignment.TotalFreeze(HitStopDirector.HitStopDuration)` | **예** |
| `FirstNodeTime` | 엔트리의 첫 온셋 | **예** |

**전부 정적이다.** 런타임 상태(입력·판정·적 위치)가 하나도 안 들어간다.

### 3-1. 식을 다시 쓰지 않아도 된다

`ClipAlignment`가 이미 그 식을 소유한다:

```csharp
// ClipAlignment.cs:131
public float ResolveScheduleStart(float impactAlignTime, float earliest)
    => Mathf.Max(impactAlignTime - ResolvedImpactSpan / Speed, earliest);

// ClipAlignment.cs:88
public float TotalFreeze(float perStop)
```

`CharacterActionPlayer.cs:709`가 같은 계산을 **손으로 펼쳐 쓴 것**이다(`impactAlign - freeze - playTime`).
예보는 `alignment.ResolveScheduleStart(impactAlign - alignment.TotalFreeze(perStop), firstNodeTime)`로
**같은 클래스의 같은 메서드**를 부르면 된다 — 새 수학이 없고, 진실의 원천이 늘지 않는다.
(오히려 `CharacterActionPlayer` 쪽을 이 메서드로 접어 넣으면 지금 있는 중복이 하나 줄어든다.)

---

## 4. 예보의 출처 — 두 후보

### 4-A. `OnPatternQueued` (리드 = `exposureDuration` 0.5초)

`PatternQueuedInfo`가 `Template`·`FirstNodeTime`·`LastNodeTime`·`Deadline`·`NodeTimes`를 전부 들고 온다.
**추가 배선이 0**이라는 게 장점.

⚠ 그러나 **리드가 보장되지 않는다.** 큐 투입은 그 패턴의 첫 노드보다 0.5초 앞설 뿐이고,
우리가 필요한 것은 *다음* 패턴의 클립 시작(= 공백 끝)보다 1.0초 + 구르기 예산만큼 앞선 시점이다.
패턴이 짧으면(마지막 노드 − 첫 노드가 0.4초 남짓) 확보되는 리드가 1.0초에 못 미친다.
**"언제나 정확히 1초"라는 요구를 만족시키지 못한다.**

### 4-B. `ChartPlayer`의 엔트리 선행 조회 (리드 = 원하는 만큼)

```csharp
// ChartPlayer.cs:128
pendingEntries = ActiveChart.entries
    .Where(e => e.spawnTimes != null && e.spawnTimes.Length > 0)
    .OrderBy(e => e.spawnTimes[0])
    .ToList();
```

곡 시작 시점에 **모든 엔트리가 시각순으로 정렬돼 있다.** `pendingEntries[0]`, `[1]`을 보면
아직 큐에도 안 올라간 패턴의 온셋·템플릿을 알 수 있다. 리드는 채보가 허락하는 만큼 — 요구를 만족한다.

⚠ **시계가 둘이다.** 채보 시각은 `audioSource.time`(오디오 시계), 회피·판정·클립은 `Time.time`이다.
변환은 조회 순간 한 번: `realtime ≈ Time.time + (audioTime - audioSource.time)`.
1~2초 앞을 보는 용도라 드리프트는 무시할 수준이지만, **변환은 `ChartPlayer`가 해야 한다** —
오디오 시계를 아는 유일한 클래스이고, 다른 곳이 `audioSource`를 만지기 시작하면 §7-3의 timeScale 함정과 같은 부류가 된다.

---

## 5. 예보가 틀릴 수 있는 경로

### 5-1. 플레이어가 미스하면 예약이 취소된다 — **안전한 방향이다**

```csharp
// CharacterActionPlayer.cs:881
hasPending = false; // 예약 취소 → 원래 나올 베기 안 나옴
```

그러면 `RaiseIdleWindow`의 `end`가 `pendingScheduleStart`에서 `info.Deadline`으로 바뀐다.
그런데 `pendingScheduleStart ≈ impactAlign − 와인드업 < Deadline`이므로 **공백이 오히려 길어진다.**
예보가 잡은 임팩트는 실제보다 이르고, 이르게 끝나는 것은 언제나 안전하다(`EnemyView.EarliestArrival`과 같은 규율).

### 5-2. 적을 다른 시스템이 데려간다 — **지금도 있는 구멍, 다만 보이게 된다**

`Fire()`가 `ambusher.BusyReasonBy(impactTime)`으로 취소한다. 지금은 링이 뜨기 전이라 조용하지만,
예보로 링을 먼저 띄우면 **취소가 화면에 보인다**(링만 떴다가 아무 일도 안 일어남).
**이 문서가 다루는 진짜 위험.**

경합 경로를 끝까지 세어 보면 **다섯**이고, 그중 **하나만** 새로 막아야 한다:

| # | 경로 | 지금 막히나 |
|---|---|---|
| 1 | **상대 배정** `TakeTargetForWindow` (`EnemyDirector.cs:907`) | **아니오** ← 유일한 구멍 |
| 2 | 무리 집결 이동 (`DesignateStagedCenter` · `SpawnAt`) | 예 — `moving && moveEnd > t` |
| 3 | 처치 승격 (`KillOpponent` → `PromoteOpponent` `:862`) | 예 — 승격 후 1번을 거친다 |
| 4 | 실패 후퇴·링 복귀 (`ReturnToRing`) | 예 — 이동 |
| 5 | 사망 (`Phase.Dying`) | 예 |

2·4·5는 `BusyReasonBy(impactTime)`가 이미 잡는다. 판정 기준이 **임팩트 시점**이라 예보로 앞당겨도 그대로 유효하다.

**1번만 다르다.** 후보 루프가 무리 소속과 **거리만** 본다:

```csharp
// EnemyDirector.cs:925~941
bool restrict = clusterEnabled && activeCluster.Count > 0;
foreach (var e in ring)
{
    if (restrict && !activeCluster.Contains(e)) continue;
    candidateScratch.Add(e);      // ← 바쁜지 안 본다
    positionScratch.Add(e.transform.position);
}
int index = EnemyRing.PickTargetByDistance(positionScratch, from, desired);
var picked = candidateScratch[index];
ring.Remove(picked);
```

⚠ **비대칭이 핵심이다.** 기습자를 고르는 `PickIdleAmbusher`는 `view.IsIdle`을 보는데(`:469`),
반대 방향인 상대 배정은 아무것도 안 본다. 그래서 `AssignAttack`을 앞당겨 예약을 걸어도 **1번은 안 막힌다.**

게다가 **하필 잘 뽑히는 자리에 있다** — 기습자는 `stageDistance`(2.5m)로 플레이어 옆에 붙여 두는데,
짧은 창에서는 `desired = cruiseSpeed × 창 / playerShare + duelDistance`가 작아져
**그 거리가 정확히 상대 선택이 찾는 값**이 된다.

### 5-3. 슬롯이 빈 패턴

`SchedulePendingSuccess`는 `alignment.IsUsable`이 아니면 예약을 안 만든다 → `end = Deadline`.
예보도 같은 분기를 타야 한다(같은 `ClipAlignment`를 보므로 자연히 따라온다).

### 5-4. 곡의 마지막 엔트리

다음 엔트리가 없으면 공백 끝을 정의할 수 없다. 지금도 `hasPending == false` 경로로 `Deadline`을 쓴다.

---

## 6. 건드리게 되는 파일

| 파일 | 무엇 |
|---|---|
| `ChartGen/ChartPlayer.cs` | 선행 엔트리 조회 + 오디오→실시간 변환 (읽기 전용 공개면) |
| `Enemy/DodgeDirector.cs` | 결정 시점 이동, `ringDuration` 고정, `RequiredLead` 의미 변경 |
| `Character/CharacterActionPlayer.cs` | (선택) `:709`를 `ClipAlignment.ResolveScheduleStart`로 접기 |
| `Pattern/Core/ClipAlignment.cs` | 변경 없음 — 이미 필요한 메서드를 다 갖고 있다 |
| `UI/PatternHandler.cs` | `goodWindow` 읽기 공개면이 필요할 수 있다(현재 private) |

---

## 7. 미해결 — Plan에서 정할 것

1. **늦은 취소를 어떻게 보이게 할 것인가**(§5-2). 링을 끝까지 완주시킬지, 즉시 걷을지.
2. **`minIdleWindow` 검사를 예보 시점에도 할 것인가.** 공백의 *시작*은 `recoveryEndTime`·`convergeUntil`에서
   나오는데 이 둘은 예보 시점에 확정 전이다. 끝만 정확하고 시작은 추정이다.
3. **`minRingExposure`/`maxRingExposure`를 고정 노브 하나로 합칠 것인가**(요구가 "언제나 정확히 1초"이므로
   두 값을 남길 이유가 사라진다). 값은 바뀔 수 있어야 하므로 **인스펙터 노브 하나**로 남긴다.
4. **쿨다운·후보 선정(`PickIdleAmbusher`)을 예보 시점으로 옮길지**, 지금처럼 `OnDuelScheduled`에 둘지.
