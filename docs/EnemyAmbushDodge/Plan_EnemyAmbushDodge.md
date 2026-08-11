# Plan — EnemyAmbushDodge (플레이어 애니메이션 공백에 들어오는 기습과 회피)

근거: `docs/EnemyAmbushDodge/Research_EnemyAmbushDodge.md`

## 정정 — "공백"의 정의가 바뀌었다

초안은 공백을 **채보 공백**(패턴이 하나도 안 살아 있는 구간)으로 잡았다. **아니다.**
여기서 말하는 공백은 **적에게 도달한 뒤 ~ 다음 액션 클립이 시작되기 전까지, 플레이어가 서 있기만 하는 구간**이다.

```
공백 시작 = max(recoveryEndTime, convergeUntil, Time.time)      // 마무리 노출도 끝나고 도착도 끝난 시점
공백 끝   = pendingScheduleStart (클립 슬롯이 비면 Deadline)     // 다음 액션 클립이 시작되는 시각
            ⚠ 첫 노드로 자르지 않는다 — 정정 3 참조(그 캡이 창을 0.15초 상수로 만들었다)
```

이 변경의 결과:

- **`ChartPlayer`는 이 기능과 무관해졌다** — 초안의 `TimeUntilNextEntry`는 폐기한다.
- **`PatternHandler`도 안 건드린다** — 초안의 `HasActivePattern`도 폐기.
- **공백을 아는 클래스는 `CharacterActionPlayer` 하나다**(네 값을 동시에 아는 유일한 곳, Research §1).
  → 새 이벤트 **`OnIdleWindow(start, end)`** 하나가 이 기능 전체의 트리거다. 폴링이 사라진다.
- **§11-6 무리 배치가 이 공백을 키운 원인**이다 — 이동이 0에 가까워지면서 대시가 채우던 시간이 통째로 정지가 됐다.

## 정정 2 — 적 이동은 링과 병렬이 아니다 · 텔레그래프를 먼저 보여준다

초안은 "링이 수축하는 동안 적이 달려오면 되니 이동은 공짜"라고 했다. **틀렸다** — `ApproachDuel`은
**클립 시작 전에** 이동을 끝낸다(스윙 중 기하가 바뀌면 칼이 어긋나므로). 이동·와인드업·판정·구르기가 전부 **직렬**이라
실제 요구는 1.85초가 아니라 **≈2.3초**다. 그 창은 거의 없다.

그래서 구조를 셋으로 고친다(Research §4-2):

- **(A) 링 노출을 적 클립에서 뗀다** — 적 클립은 `impact − 와인드업`에 먼저 시작해 **텔레그래프**가 되고,
  링은 `impact − ringExposure`(0.4초)에 **늦게** 뜬다. **모션이 "온다"를, 링이 "지금"을 맡는다.**
  링 수축 속도가 패턴 링과 같아져 학습한 감각이 유지된다. ⚠ 이것만으로는 총 예산이 안 준다.
- **(B) 이동을 창 밖으로 뺀다 — 사전 접근** — 기습할 적은 지금 상대가 아니라 아무도 안 쓴다.
  **⚠ 기준점은 플레이어의 '지금 위치'가 아니라 '갈 위치'다.** `EnemyDirector.OnDuelScheduled`의
  `DuelPlan.PlayerPosition`/`PlayerArriveTime`(둘 다 이미 public)으로 **역산해서** 붙인다.
  현재 위치로 붙이면 플레이어가 `cruiseSpeed × 창`만큼 대시해 **적만 뒤에 남고**, 이동이 창 안으로 되돌아온다.
  `OnPatternQueued`는 **못 쓴다** — 그 시점엔 상대 배정 자체가 안 끝났다(§11-1).
  둘 다 그냥 이동이라 이번엔 **진짜로 병렬**이고, **0.67초가 창에서 통째로 빠진다.**
- **(C) 구르기 압축** — `PlayOneShot(clip, 1.6)`로 0.8 → 0.5초.

→ 필요시간 `와인드업 0.5 + 판정 0.15 + 구르기 0.5 + margin 0.2 ≈ 1.35초`.

## 정정 3 — 첫 노드 캡이 기능을 죽였다 (플레이 실측 후)

1차 구현은 **한 번도 발동하지 않았다.** 로그 전부 `무시(창 부족)`, 창 0.10~0.50초.

**원인은 예산이 아니라 창 계산의 캡이었다.** `end = min(pendingScheduleStart, FirstNodeTime)`의 `FirstNodeTime`.

채보 실측(`Dreamer_Lv10`, 89개 연결):

| 구간 | min | p50 | p90 | max |
|---|---|---|---|---|
| 앞 패턴 마지막 노드 → **다음 첫 노드** (= 그 캡) | 0.40 | **0.40** | 0.40 | 1.60 |
| 앞 패턴 마지막 노드 → 이번 임팩트 (W) | 0.50 | 1.30 | 2.10 | 2.10 |

**89개 중 83개가 정확히 0.40초**다. 거기서 `recoveryHoldDuration`(0.25)을 빼면 로그의 0.10~0.28이 그대로 나온다 —
필요시간을 0.15초로 줄여도 영원히 안 뜬다. **캡이 창을 상수로 만들었다.**

시간의 공급처는 전부 **노드를 입력하는 구간 안**에 있다. 플레이어 공격 클립은 첫 노드가 아니라
`임팩트 − 와인드업`(템플릿 실측 p50 **0.26초**)에 시작하므로, 첫 노드부터 클립 시작까지 캐릭터는 서 있기만 한다.

**A. 캡 제거** — 창의 끝을 `pendingScheduleStart` 하나로 잡는다(클립 슬롯이 비면 `Deadline`).
결과: 빈 구간 p50 **0.54초**, ≥0.8초 45%, ≥1.0초 25%.
**대가는 회피가 패턴 입력 도중에 뜬다는 것**이고, 실측상 그걸 피하면 기능이 존재할 수 없으므로 요구로 받아들인다.

**B. 구르기를 예산에서 뺀다** — "피했다"는 임팩트에서 이미 성립하므로 클립 뒷부분은 다음 공격 크로스페이드에 끊겨도 된다.
창 안에 들어가야 하는 것은 **자리를 옮기는 시간**(`rollMoveDuration` 0.25초)뿐이다 — 그건 다음 스윙의 기하가 얼기 전에 끝나야 한다.

⚠ **예산과 재생을 나눈다.** 예산은 0.25초만 요구하되 **실제 이동은 창이 허락하는 만큼 늘려 쓴다**
(`min(클립 길이, 다음 클립 시작 − margin)`, 하한 0.25초). Apply Root Motion이 꺼져 있어 이동은 코드가 하므로,
이동이 클립보다 짧으면 **남은 절반을 제자리에서 구른다** — 늘리면 둘이 맞고, 짧은 창에서는 0.25초로 줄 뿐 회피는 그대로 성립한다.
반대로 클립 전체를 예산에 넣으면 발동 구간이 **25% → 4%**로 떨어진다.

**D. 필요시간 계산 버그** — `ringExposure`가 빠져 있었다. 배속을 올려 와인드업(0.20초)이 링(0.4초)보다 짧아지면
**링이 적 모션보다 먼저 뜬다** = 텔레그래프/타이밍 단서의 분리(정정 2 A)가 뒤집힌다.
→ `max(와인드업, ringExposure)`.

```
필요시간 = max(와인드업, ringExposure) + dodgeWindow + rollMoveDuration + margin
         = max(0.20, 0.4) + 0.15 + 0.25 + 0.2 = 1.00초
```
`minIdleWindow` 1.2 → **0.8**(실측 45% 지점).

## 정정 4 — 사전 접근이 자기가 만든 창을 실격시킨다 (2차 구현 실측 후)

정정 3의 캡 제거는 **효과가 있었다** — 창이 0.40초 상수에서 벗어났다. 그런데 발동은 여전히 **0회**다.
사유가 `창 부족`에서 **`대기 후보 없음`**으로 옮겨갔을 뿐이다.

콘솔 실측(17개 공백):

| 사유 | 건수 | 창 길이 |
|---|---|---|
| 창 부족 (`< minIdleWindow` 0.8) | 11 | 0.10 ~ 0.77 |
| **대기 후보 없음** | **6** | 0.89 · 1.04 · 1.05 · 1.07 · 1.37 · 1.38 |

**예산은 이제 문제가 아니다.** 자격을 통과한 창이 35%이고 전부 요구시간(≈1.0초)을 넘는다. 1.38초짜리도 죽었다.
`AmbushAttacks` 저작(Step 6)도 완료돼 있어 클립 문제도 아니다.

**원인은 `IsIdle`을 보는 시점이다.** 한 프레임 안의 호출 순서 —

```
OnPatternComplete (PatternHandler)
  → OnDuelScheduled (EnemyDirector)
      → DodgeDirector.HandleDuelScheduled → stagedAmbusher.ScheduleMove(...)   // moving = true
  → RaiseJudgeTargetBegan
      → CharacterActionPlayer.OnIdleWindow
          → DodgeDirector.HandleIdleWindow → stagedAmbusher.IsIdle             // moving 이므로 false
```

`ScheduleMove`는 **거리와 무관하게** `moving`을 켜고(거리는 `wantsLocomotion`만 가른다),
`IsIdle`(= `CanWander`)은 `moving`이면 무조건 false다. `moving`은 `Time.time >= moveEnd`에서만 내려가는데
`moveEnd >= plan.PlayerArriveTime`이고 **공백 시작 자체가 `convergeUntil` = `PlayerArriveTime`**이다.
즉 **창이 열리는 순간 기습자는 정의상 이동 중**이고, `OnIdleWindow`는 폴링이 없는 일회성이라 재시도가 없다.

**사전 접근이 걸어 둔 이동을, 사전 접근이 만든 창에서 실격 사유로 쓰고 있다.**
`IsIdle`은 원래 *"사전 접근을 **못 따라온** 적을 거른다"*는 **발동 시점** 필터로 설계됐는데(흐름 0의 마지막 줄),
구현이 그걸 **예약 시점**에 걸었다. 예약 프레임에 이동 중인 것은 **정상**이고,
`fireTime`(= 플레이어 도착 시각)에 서 있는 것이 **정상**이다 — 실격 판정은 후자에서 해야 한다.

## 정정 5 — 와인드업도 창 밖으로 뺀다 (3차 실측 후)

정정 4를 고치자 사유가 갈렸다(12개 공백, 발동 0회):

| 사유 | 건수 | 창 |
|---|---|---|
| 창 부족 (`< minIdleWindow` 0.8) | 8 | 0.10 ~ 0.77 |
| 후보가 늦게 도착 | 2 | 1.05 · 1.06 |
| 후보 없음 | 1 | 1.07 |

**창이 줄어든 게 아니다** — 분포 중심은 2차와 같다(0.7대 다수 + 1.0대 소수). 줄어 보이는 이유는 다른 데 있다:
로그의 그 숫자는 `end − max(start, Time.time)`이고 `start = max(recoveryEndTime, convergeUntil, now)`이라
**지금부터 플레이어가 도착할 때까지의 대시 시간이 통째로 빠져 있다.** 그 구간이 1초든 1.5초든 예산에 한 푼도 안 들어온다.

**근거를 다시 보면 그 배제가 과하다.** `fireTime = max(start, Time.time)`의 이유는
*"도착 전에 띄우면 달리면서 회피하게 된다"*였는데, 그건 **판정에만 걸리는 제약**이다:

| 순간 | 플레이어가 서 있어야 하나 | 왜 |
|---|---|---|
| 적 클립 시작(와인드업 = 텔레그래프) | **아니다** | 적 모션이다. 플레이어가 대시 중이어도 화면에 그대로 보인다 |
| 링 수축 | **아니다** | 루트 Canvas가 `ScreenSpaceOverlay`라 카메라·이동과 완전 무관하다(§7-5) |
| 임팩트 · 입력 판정 · 원호 구르기 | **그렇다** | 구르기가 대시와 싸우면 안 되고, 다음 스윙의 기하가 얼기 전에 끝나야 한다 |

**그래서 와인드업을 창 밖(대시 구간)으로 뺀다.** 정정 2 B가 *이동*을 창 밖으로 뺀 것과 같은 수법을
*와인드업*에 적용하는 것이고, 이번에도 새 시계를 만들지 않는다.

```
지금:  fireTime = max(start, Time.time)
       필요시간 = max(와인드업, ringExposure) + dodgeWindow + rollMove + margin ≈ 1.00s   ← 전부 창 안
       → minIdleWindow 0.8 · 실측 통과 3/12

제안:  fireTime = Time.time                            // 즉시. 와인드업이 대시 구간을 먹는다
       impactTime = end − (rollMove + margin)          // 그대로
       ① 창 안 요구   = dodgeWindow + rollMove + margin = 0.15 + 0.25 + 0.2 = 0.60s
       ② 텔레그래프   = impactTime − Time.time >= max(와인드업, ringExposure)   // ⚠ '지금' 기준
       → minIdleWindow 0.8 → 0.6 · 실측 통과 10/12 (0.68·0.71·0.74·0.75·0.75·0.76·0.77이 살아난다)
```

**요구가 둘로 갈리고 시계가 서로 다르다**는 게 이 정정의 전부다 — ①은 창(서 있는 구간) 기준,
②는 지금 시각 기준이다. 하나로 합치면 정정 3에서 캡이 창을 상수로 만든 것과 같은 부류의 오류가 된다.

**⚠ 딸려오는 함정 둘** (둘 다 이미 겪은 실수의 재발이다)

1. `Fire`가 `lungeSpot`을 **플레이어의 현재 위치**로 잡는다. 도착 후 발동일 때는 맞았지만, 당기면
   **플레이어가 아직 출발도 안 한 자리로 적이 찌른다** — 정정 2 B와 같은 실수다.
   `HandleDuelScheduled`에서 `plan.PlayerPosition`을 캐시해 그걸 쓴다(이미 들고 있는 값, 새 계산 없음).
2. Step 8-3의 `!ambusher.IsIdle` 실격이 **항상 참**이 된다(당기면 적도 사전 접근 이동 중이다).
   진짜 요구는 *"임팩트 때 서 있는가"*이므로 `ReadyBy(impactTime)`으로 바꾼다.

## 정정 6 — 공백의 공급을 막는 것은 예산이 아니라 '도착 시각'이다 (4차 실측 후)

정정 5까지 고친 뒤에도 발동 0회. 18개 창의 사유가 다음처럼 갈렸다:

| 사유 | 건수 | 예 (공백/리드/도착까지) |
|---|---|---|
| 창 부족 (`< 0.6`) | 8 | 0.46/0.17 · 0.48/0.20 |
| **후보 이동 중** | **8** | 0.76/0.43 · 1.04/0.78 · 1.39/1.10 |
| 후보 없음 | 3 | 1.05/0.78 · 1.37/1.08 |

그리고 역산된 `start − now`가 **0.12~0.31초뿐**이었다 — 정정 5가 전제한 "대시 1~1.5초"가 실측에 없다.
원인이 둘로 갈리고, **둘 다 '도착 시각'을 어떻게 정하느냐의 문제**다.

### A. 플레이어 도착 시각에 속도 상한이 없다 (공급 측)

```csharp
// PlayerCombatMover.cs:83
ScheduleMove(target, Mathf.Max(plan.PlayerArriveTime - Time.time, 0.01f));
```

**이동 시간 = 남은 시간 전부.** 거리가 0.3m든 8m든 도착은 언제나 `PlayerArriveTime`, 즉 **가능한 가장 늦은 시각**이다.
무리 반경 2m를 `cruiseSpeed` 4.5로 가면 0.44초면 되는데 창이 1.3초면 1.3초를 다 쓴다(실효 0.1~1 m/s).

**적에게는 이 장치가 이미 있다** — `EnemyView.EarliestArrival`이 `min(latest, now + distance/moveSpeed)`로 앞당기고,
그 주석이 이유를 그대로 적어 놨다: *"이게 없으면 적이 도착하는 순간이 곧 베이는 순간이라 서 있는 구간이 아예 없다"*,
*"앞당기는 건 언제나 안전하다"*. **플레이어에만 없다.**

안 앞당기는 근거는 `ResolvePlayerArriveTime`의 주석이다 — *"`TakeTargetForWindow`가 `cruiseSpeed × 창`으로
거리를 잡아 이미 체감 속도가 일정하다"*. **§11-6 무리 배치가 그 전제를 깼다**: 후보가 `activeCluster`로 묶여
거리가 무리 반경(2m) 안에 갇히므로 창에 비례하지 않는다. §11-6 스스로 *"무리 안에서는 플레이어 이동이 0에 가깝다"*고
적어 둔 상태다 — **거리는 상수, 창은 4배로 흔들리니 속도가 창에 반비례해 기어간다.**

그게 공백을 직접 지운다:

```
CharacterActionPlayer:445   convergeUntil = plan.PlayerArriveTime    // '실제 도착'이 아니라 '예정 도착'
CharacterActionPlayer:642   start = max(recoveryEndTime, convergeUntil, now)
CharacterActionPlayer:645   if (end - start <= 0) return;           // 조용히 발행조차 안 한다
```

공백은 `pendingScheduleStart − PlayerArriveTime` 잔여뿐이고, `Attacker.Player`는 `r.attack`이 null이라
`arriveTime = r.impactTime`(EnemyDirector.cs:1121)인데 `end = impact − 플레이어 와인드업`이므로 **그 값이 음수**다.

**⚠ 그래서 로그에 남은 18개는 전부 `distance < convergeMinDistance`(0.15m)로 `convergeUntil` 갱신을 건너뛴 경우**
(CharacterActionPlayer:440)다 — 플레이어가 사실상 안 움직인 패턴만 창이 열렸다. 조금이라도 움직이는 패턴은
창이 0이라 로그에조차 안 남는다. **회피 조건 ③(`Attacker.Player`)과 창 존재 조건이 거의 배타적이 된 상태다.**

### B. 사전 접근 마감이 임팩트보다 뒤다 (자격 측 — 우리 코드 한 줄)

```csharp
// DodgeDirector.cs:170
stagedAmbusher.ScheduleMove(pos, target, Time.time, plan.PlayerArriveTime);
```

`moveEnd = PlayerArriveTime = r.impactTime`인데 자격 기준은 `impact = end − 0.45`이고 `end = pendingScheduleStart < r.impactTime`이다.
→ **`moveEnd > end > impact`가 항상 성립**하므로 `BusyReasonBy(impact)`가 예외 없이 `"이동 중"`.
로그의 `후보 이동 중` 8건 전부가 이것이다.

`EnemyDirector.ApproachDuel`은 `EarliestArrival`을 거치는데 **`DodgeDirector`가 `ScheduleMove`를 직접 불러 그 앞당김을 우회했다** —
Step 5 구현의 누락이다.

## 정정 7 — 발동은 되는데 읽히지 않는다 (5차 실측 · 문제 셋)

정정 6까지 고치자 **회피가 실제로 발동한다.** 로그의 발동 2회:

```
공백 0.99s (리드 1.21s · 도착까지 +0.66s) → 발동
기습 발동 — MediumAttk1 (트림 0.84s ×2.0) · 와인드업 0.30s vs 링 0.40s (⚠ 링이 먼저) · 임팩트까지 1.21s
회피 실패 — 피격(창 ±0.15s 안에 입력 없음)

공백 0.76s (리드 0.54s · 도착까지 +0.23s) → 발동
기습 발동 — Swipe_4To6 (트림 0.28s ×2.0) · 와인드업 0.10s vs 링 0.40s (⚠ 링이 먼저) · 임팩트까지 0.54s
```

남은 문제는 셋이고, **전부 연출·가독성 문제**다(발동 조건은 더 이상 병목이 아니다).

### 문제 1 — 기습이 화면에 늦게·짧게 나타난다

**배선은 정상이다**(추측 배제): `DodgePoint`는 `PatternHandler`의 **마지막 자식**(최상단),
부모 pivot이 캔버스 중심이라 좌표계가 일치하고 마스크가 없다. `FocusRingView`는 자기 코루틴으로 선형 수축하며
풀 대여 순서가 `initializer → OnSpawn`이라 파라미터가 안 섞인다. 프리팹 `endSize 90x90` = 닷지 포인트 아이콘 90x90.

원인은 **시각 두 개가 다 늦다**는 것:

| | 노출 | 비고 |
|---|---|---|
| 링 | **0.40s 고정** (`ringExposure`) | 리드가 1.21s여도 0.4s만 |
| 적 클립 | **0.30s / 0.10s** | 에셋이 `speed: 2`로 저작돼 와인드업이 반토막 |

두 발동 모두 `⚠ 링이 먼저`다 — §4-2 A의 *"모션이 '온다'를, 링이 '지금'을 맡는다"*가 **뒤집혔다**.
`RequiredLead`의 `max(와인드업, ringExposure)`는 이걸 못 막는다: 그건 "리드가 충분한가"만 보고
**클립 시작 시각을 앞당기지 않는다**. 정정 3 D가 예고한 상황이 그대로 실현됐다.

⚠ 오토퍼펙트 탓이 아니다. 임팩트 첫 프레임에 `Succeed → Hide()`라 닷지 포인트가 정확히 `ringExposure`만큼 보이지만,
수동 입력이어도 최대 `+dodgeWindow`(0.15s) 늘 뿐이다.

### 문제 2 — 기습자 카메라 포커스가 즉각적이지 않다

`SetAmbusher`가 대상 교체 시 **가중치를 0으로 리셋**하고(§7-2 규율 — 이월하면 카메라가 밖으로 튄다),
`Damp`가 `weightDamping = 0.35초` 시간상수로 올린다. 임팩트 시점 가중치를 계산하면:

| 발동 | 리드 | 임팩트 시점 가중치 |
|---|---|---|
| Swipe_4To6 | 0.54s | **79%** |
| MediumAttk1 | 1.21s | 97% |

거기에 `GroupFraming`의 돌리 감쇠가 더 붙는다. **0.35초는 상대 승격(다음 패턴 내내 유지)에 맞춘 값**인데
기습은 수명이 0.5~1.2초라 감쇠가 사건 전체와 같은 스케일이다.
⚠ 발동을 더 당길 여지는 없다 — 정정 5 이후 `Fire`는 `HandleIdleWindow`와 **같은 프레임**이다.

### 문제 3 — 기습한 적이 끝나고 구르기(회피) 모션을 한다

**우리가 시킨 것이다.**

```csharp
// EnemyDirector.ReleaseAmbusher
view.Resolve(false, Attacker.Enemy, failRetreatDistance, retreatDuration, retreatTarget);

// EnemyView.Resolve
bool parried = !playerSucceeded && attacker != Attacker.Enemy && distance <= 0f;   // → false
if (!playerSucceeded) CrossFadeReaction(parried ? parryStateName : evadeStateName); // → evade = 뒷구르기
```

Step 5 흐름 6이 뒷정리에 **"실패한 적의 반응" 경로를 통째로 재사용**했다. 그 경로는 `playerSucceeded = false`를
*"적이 베이려다 피했다"*로 읽어 회피 클립을 튼다. **기습에는 성패 개념이 없다** — 찌르고 물러나기만 하면 되는데
리액션 크로스페이드가 딸려 왔다.

### 해결 방향

**1. 링 노출을 리드에 맞춘다 — 크기는 상수로 둔다.**
"미리 아는 것은 스폰 시각뿐"이라는 게 패턴 링의 구조다(노드도 입력 시각 `exposureDuration` 전에 스폰돼 수축한다).
**⚠ 시작 크기를 키워서 늘리면 안 된다** — 크기로 남은 시간을 읽는 학습이 깨진다.

```
minRingExposure = 0.4   // 후보 자격(RequiredLead)이 쓰던 값 그대로. 이보다 짧으면 못 읽는다
maxRingExposure = 0.8   // 수축 속도 편차를 1.6배로 묶는다

실제 노출    = clamp(리드, min, max)
링 스폰 시각 = impact − 실제 노출
```

| | 노출 | 시작 지름 | 수축 속도 |
|---|---|---|---|
| 패턴 링 | 0.5s | 450px | 720 px/s |
| 회피 링 (지금) | 0.4s 고정 | 450px | 900 px/s |
| 리드 0.54s → | **0.54s (전 구간)** | 450px | 667 px/s |
| 리드 1.21s → | **0.80s** | 450px | 450 px/s |

**포기하는 것**: 「모션 먼저 / 링 나중」(§4-2 A). 그 규칙은 예산이 빠듯할 때 만든 것이고,
링이 전 구간을 덮으면 타이밍 단서는 링이 전담한다(패턴 입력과 같은 규율이라 오히려 일관적이다).
에셋 `speed: 2`를 1로 낮추는 것은 **저작 판단이라 코드와 분리**한다.

**2. 기습자 전용 가중치 감쇠.** `ambusherWeightDamping`(0.12초) 하나. 상대 칸은 0.35 유지 —
그 감쇠에는 이유가 있다(승격 구간의 계단식 튐 방지). 계산식(`ResolveWeight`)은 계속 공유하므로 두 칸이 어긋나지 않는다.

**3. `EnemyView.ScheduleWithdraw(target, duration)`** — `Resolve`에서 **리액션 크로스페이드만 뺀** 판
(`StopWander` + 예약 해제 + `Phase.Recover` + `ScheduleMove`). `ReleaseAmbusher`가 그것을 쓴다.
구르기가 사라지고 물러남은 유지된다.

## 정정 8 — 기습자는 물러나지 않는다 · 닷지 포인트 좌표 (6차 실측)

### 문제 1 — 닷지 포인트가 화면 좌하단에 고정된다

```csharp
// DodgePointView.cs
canvasCamera = canvas.worldCamera;                                          // Overlay → null
Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvasCamera, follow.position + worldOffset);
```

**`RectTransformUtility.WorldToScreenPoint(cam, world)`는 `cam`이 null이면 투영하지 않고 `(world.x, world.y)`를 그대로 돌려준다.**
적이 월드 (2.3, 1.7, 5.0)이면 스크린 좌표 (2.3, 1.7)**픽셀** = 좌하단 구석. 관측과 정확히 일치한다.

Overlay에서 null이 맞는 것은 **두 번째 호출**(`ScreenPointToLocalPointInRectangle`)뿐이다.
첫 번째는 **월드를 화면에 투영하는 단계라 실제 렌더링 카메라가 필요하다** — 두 호출이 같은 변수를 쓴 것이 실수다.
앵글 교체(§7-5)와는 무관하다(Cinemachine이 모는 Unity 카메라는 하나다).
⚠ 적이 카메라 뒤에 있으면 좌표가 뒤집혀 반대편에 찍히므로 그 가드도 같이 필요하다.

### 문제 2 — 기습자가 물러나면 안 된다 (요구 정정)

Step 5 흐름 6은 *"성패와 무관하게 적은 찌르고 물러난다"*로 적혀 있었다. **그 요구가 틀렸다.**
기습은 무리에서 한 명이 튀어나와 찌르는 사건이고, **찌른 뒤에는 그 자리에 남아 배회로 돌아가면 된다.**
물러남은 원래 *"베이려다 피했다"*(회피)의 후속 동작이라 기습에는 붙을 이유가 없다.

정정 7에서 구르기 클립을 뗀 뒤 남은 증상 — *"플레이어 반대편으로 밀려난다"* — 은 그 잘못된 요구의 직접 결과다:

- `ReleaseAmbusher`가 실패 후퇴 파라미터를 재사용한다: `failRetreatDistance` 1.5m ÷ `retreatDuration` 0.25s = **6 m/s**
  (적 통상 이동 `moveSpeed` 3 m/s의 두 배). 그 값은 **뒷구르기 클립 전용 튜닝**이었고, 클립이 사라지자 정당화가 없어졌다.
- 리액션이 없어지면서 `ScheduleMove`의 `wantsLocomotion`이 켜진다(1.5m > `minMoveDistance` 0.3m) →
  **정면 Run을 틀면서 몸은 뒤로 6 m/s로 간다.**

→ 값을 줄이는 게 아니라 **이동을 없앤다.** `ScheduleWithdraw`(정정 7에서 만든 것)를 이동 없는 해제로 바꾸고,
`EnemyDirector`의 무대 계산(`PickRetreatTarget`)도 이 경로에서 뺀다.

⚠ `Resolve`가 *"거리가 0이어도 `ScheduleMove`는 부른다"*고 한 이유(`retreatUntil`이 곧 이어지는
`AssignAttack`의 `ScheduleMoveAfter` 인수인계 시각)는 **결투 경로 전용**이다 — 기습자는 상대가 아니므로
뒤에 `AssignAttack`이 따라오지 않는다. 그래서 여기서는 안 불러도 안전하다.

## 설계 요약

```
자리   : 플레이어 애니메이션 공백 (§11-4 견제가 채운 '적 쪽 공백'의 반대편, 같은 창)
사전   : OnDuelScheduled에서 DuelPlan.PlayerPosition(= 플레이어가 '갈 자리')으로 기습 후보를 미리 붙인다
         마감은 PlayerArriveTime — 플레이어와 같은 시각에 도착한다(이동을 창 밖으로 뺀다)
조건   : ① 공백 길이 >= 필요시간   ② 사전 접근한 후보가 아직 Idle이고 화면 안이다
         ③ Attacker.Player 패턴일 것 (적 칼이 이미 오는 구간에는 안 띄운다 — Research §5)
발동   : 적 클립 먼저(텔레그래프) → 링은 impact − ringExposure에 늦게 뜬다
판정   : |입력 − impact| <= dodgeWindow → 성공 / 없으면 impact + window에 실패
성공   : 구르기 클립(Dodge_Left/Right) + 중심(현재 적 ?? 기습한 적)을 축으로 한 원호 이동
실패   : 기존 피격 클립 + OnPlayerHit 재발행 (카메라 큐·체력이 공짜로 따라온다)
```

**새로 만드는 것은 둘** — `DodgeDirector`(유일 관리 지점) · `DodgePointView`(원형 UI).
나머지는 전부 이미 있는 값을 밖에서 읽게 여는 수준이다. **새 애니메이터 스테이트·레이어 없음**(§11-4와 같은 규율).

---

## 단계

### - [~] Step 0 — 사전 실측 (2·3 완료 / 1은 플레이 필요)

**결과**
- ✅ **2. Apply Root Motion = 꺼짐**(`Char_School_Katana_FullBody-Magica cloth2.prefab: m_ApplyRootMotion: 0`) — 구르기가 이중으로 안 간다.
- ✅ **3. Attack Layer 아바타 마스크 없음**(`PlayerAnimator.controller`의 Attack Layer `m_Mask: {fileID: 0}`) — 전신이라 구르기가 다리까지 걸린다.
- ⏳ **1. 공백 길이 분포** — 런타임 값이라 정적 계산 불가. `DodgeDirector.logWindows`(기본 켜짐)가
  공백마다 `[DodgeDirector] 공백 1.34s → 발동/무시(사유)`를 한 줄씩 찍는다. **한 곡 돌려 그 로그로 `minIdleWindow`를 확정할 것.**
  현재 기본값 **1.2초**는 예산(와인드업 0.5 + 판정 0.15 + 구르기 0.5 + margin 0.2 ≈ 1.35초)에서 역산한 잠정값이다.

**원래 항목**

1. **공백 길이의 분포** — 채보(`Dreamer_Lv10`) 전 엔트리에 대해
   `pendingScheduleStart − max(recoveryEndTime, convergeUntil)`. **완료** — 정정 3의 표가 그 결과다.
   가장 싼 방법: `OnIdleWindow`를 Step 1에서 먼저 넣고 **한 곡 플레이하며 로그로 히스토그램**을 뽑는다
   (정적 계산은 `convergeUntil`·클립 배속이 런타임 값이라 재현이 안 된다).
   → `minIdleWindow` 기본값의 유일한 근거. 1.85초 이상이 0개면 **예산을 깎는다**(구르기 배속 압축 → `exposureDuration` 축소 순).
2. **플레이어 `Animator`의 Apply Root Motion** — 켜져 있으면 구르기가 코드 이동과 이중으로 간다.
3. **`Attack Layer`의 아바타 마스크가 전신인가** — 상체면 구르기가 다리에 안 걸린다.

### - [x] Step 1 — `CharacterActionPlayer`: 공백을 알리고, 일회성 리액션을 연다

이 기능의 트리거 전부가 여기 있다.

- **`public event Action<float, float> OnIdleWindow`** — `HandleJudgeTargetBegan` **끝**에서 발행.
  - `start = max(recoveryEndTime, convergeUntil, Time.time)`, `end = pendingScheduleStart`
    ⚠ **첫 노드로 자르지 않는다**(정정 3 A) — 그 캡이 창을 0.15초 상수로 만들어 기능을 죽였다.
  - `end - start <= 0`이면 발행하지 않는다.
  - ⚠ **`OnDuelScheduled`보다 뒤라는 순서에 의존한다**(`convergeUntil`이 그 핸들러에서 갱신되므로).
    이미 보장돼 있다 — `OnPatternComplete`(디렉터) → `RaiseJudgeTargetBegan`. 코드에 주석으로 못박는다.
  - 슬롯이 비어 무연출인 패턴(`hasPending == false`)에서는 `pendingScheduleStart`가 낡은 값이다 →
    그 경우 `end = info.Deadline`(이 패턴이 끝나는 시각)으로 잡는다.
- **`public void PlayHitReaction()`** — `TryStartPendingHit`의 재생 부분을 그대로 추출(중복 없음).
  `OnPlayerHit` 발행이 그 안에 남아 카메라 큐·체력이 따라온다.
- **`public void PlayOneShot(AnimationClip clip, float speed = 1f)`** — `PlaySlot(clip, 0, clip.length, speed, isSwing:false)`.
  구르기가 쓴다. `speed`는 예산이 빠듯할 때 구르기를 압축하는 노브다(Research §4).
- ⚠ 둘 다 **`hasPending`을 안 건드린다.** 다음 공격은 자기 `Deadline`에서 독립 예약이라 제시각에 시작하고,
  겹치면 크로스페이드가 회피 클립을 끊을 뿐 §6 정렬은 안 깨진다.

### - [x] Step 2 — 나머지 접점을 연다 (읽기 전용·소규모)

- `EnemyDefinition` — **`AmbushAttacks`(`ClipAlignment[]`) 배열 추가**(§2-6).
  **적 종류가 기습 모션의 소유자다** — 클립은 리그·무기에 종속이라 `DeathSliceSet`이 여기 사는 것과 같은 논리다.
  패턴에 두면 안 된다(기습은 패턴 밖 사건이라 어느 패턴에도 안 묶인다).
  **배열인 이유**: 같은 적이 곡 내내 똑같이 찌르면 두 번째부터는 사건이 아니라 리듬이 된다.
- `EnemyView` — `public bool IsIdle => CanWander();`
  조건이 정확히 일치한다(`Dying/dissolving 아님 && 예약 없음 && reactionUntil 지남 && moving 아님`). **새 판정을 만들지 않는다.**
- `EnemyDirector` —
  - `public Func<Vector3,bool> BuildVisibilityTest()` 무인자 오버로드(카메라 폴백 포함).
  - `public EnemyView PickIdleAmbusher(Func<Vector3,bool> isVisible)` —
    `activeCluster` 중 `currentOpponent 아님 && IsIdle && 화면 안 && 기습 클립 있음`인 것 중 **랜덤 하나**(사용자 요구: 무리에서 랜덤).
    ⚠ 클립 조건은 **후보 단계에서** 건다 — 무연출 기습은 "회피할 대상이 없는 닷지 포인트"라 존재할 수 없다
    (다른 무연출 폴백들과 성질이 다르다). 판단은 "`Definition.AmbushAttacks`에 쓸 수 있는 게 하나라도 있는가,
    없으면 `fallbackAmbushClips`가 비지 않았는가".
    ⚠ 후보를 `activeCluster`로 묶는 이유는 §11-6과 같다 — `staged`를 집으면 집결 중인 적이 이탈해 무리가 깨진다.
    ⚠ 이동 시간 조건은 없다 — **사전 접근(Step 5 흐름 0)이 이동을 창 밖으로 뺐기 때문**이다.
  - 사전 접근은 **`view.ScheduleMove(현재, 목표, now, plan.PlayerArriveTime)`** 하나면 된다(이미 public).
    목표는 `plan.PlayerPosition`에서 `stageDistance` 떨어진 지점이고, 그 계산은 `DodgeDirector`가 한다 —
    **`EnemyDirector`에 새 메서드를 만들지 않는다**(무대 반경 클램프가 필요하면 그때 옮긴다).
  - `public void ReleaseAmbusher(EnemyView view, float retreatDistance, float retreatDuration)` —
    `EnemyRing.PickRetreatTarget`으로 무대 안 자리를 잡아 `view.Resolve(false, Attacker.Enemy, …)`.
    무대 중심·반경을 아는 곳이 여기뿐이라 여기 둔다.
- `PatternHandler` — 기존 `#if UNITY_EDITOR` 블록 **안에** 읽기 프로퍼티 하나:
  `public bool DebugAutoPerfect => debugInputEnabled && debugAutoPerfect;`
  **새 토글을 만들지 않는다**(§3-1) — 회피가 자기 토글을 들면 "패턴만 오토 / 회피만 오토"라는 아무도 안 원하는 조합이 생긴다.
  빌드에는 안 들어간다(§8의 기존 규율 그대로).
- `ChartPlayer` — **수정 없음**(초안에서 폐기).

### - [x] Step 2-b — `CameraDirector`: 기습적을 담을 3번째 고정 칸

요구 "공격하는 적이 있으면 카메라 타깃에 바로 추가"의 구현. **⚠ `AddMember`로 넣고 빼면 안 된다** —
§7-2가 멤버를 **고정 2칸**으로 못박은 이유가 *"넣었다 뺐다 하면 바운드가 계단식으로 튄다"*이기 때문이다.

- `SetupFraming` — `AddMember(null, 0f, 1f)` **한 줄 추가**(2번 칸). 평소엔 대상 null·가중치 0이라 구도에 영향이 없다.
- `UpdateFraming` — 상대 가중치 계산을 **공통 함수로 뽑아**(`ResolveWeight(Transform)`: `fullFrameDistance`/`dropoffDistance` 식)
  두 칸이 같은 식·같은 `weightDamping`을 쓰게 한다. **계산이 하나라 두 칸이 어긋날 수가 없다.**
- `ApplyFraming` — 가드를 `Count < 2` → `< 3`으로 올리고 2번 칸도 같이 갱신.
- `public void SetAmbusher(EnemySpace.EnemyView view)` — `SetOpponent`과 **같은 규율**:
  대상이 바뀌면 **가중치를 0으로 리셋**한다(§7-2 — 대상 위치는 순간이동하므로 이월하면 카메라가 밖으로 튄다).
- `[SerializeField] float ambusherMaxWeight = 1f` — 기습자는 `stageDistance`(2.5m)라 가중치가 거의 1이라
  화면이 크게 물러난다. 너무 넓으면 `fullFrameDistance`가 아니라 **이 상한**으로 조인다(다른 구도에 영향 없음).
- `framingEnabled == false`(배선 없음)면 이 경로도 통째로 조용히 죽는다 — 기존 규율 그대로.

### - [x] Step 3 — `PlayerCombatMover`: 원호 이동

- `public void RollArc(Vector3 center, float signedDegrees, float duration)`
  - 시작 반경·각도를 잡아 두고 **각도를 보간**한다. 직선 Lerp면 반경 1.5m·60°에서 0.2m 파고든다(Research §2-4).
  - 높이 유지, 회전은 매 프레임 중심을 바라보게 직접 갱신(`ScheduleTurn` 0.15초가 0.8초 구르기와 싸운다).
  - `rolling` 동안 `moving` 보간은 건너뛴다. **`HandleDuelScheduled`가 오면 구르기를 즉시 중단하고 이동에 양보한다** —
    결투 도착 시각은 칼이 맞는 시각이라 놓칠 수 없다.

### - [x] Step 4 — `DodgePointView` (원형 UI + 포커스 링 + 입력)

씬의 `Canvas` 아래(형제: `FocusRingParent` 옆)에 **하나만** 둔다. 동시에 둘이 뜰 일이 없어 풀이 필요 없다.

- 구성: `RectTransform` + 원형 `Image`(Point 노브와 **같은 스프라이트·같은 지름 90px** — 링 `endSize`와 어긋나면
  "딱 맞았다"가 거짓이 된다) + `IPointerDownHandler`.
- `Show(Transform follow, Vector3 worldOffset, float duration)` — 활성화 + `Pool.Instance.Get<FocusRingView>(PoolKey.FocusRing, …)`을
  **자기 자식**으로 붙인다(`targetLocalPos = (0,0)`) → 닷지 포인트만 움직이면 링이 따라온다.
  링의 `OnArrived`는 구독하지 않는다 — 판정 시각의 소유자는 `DodgeDirector`다(시계를 둘로 만들지 않는다).
- `LateUpdate`에서 `follow.position + worldOffset` → 화면 → 캔버스 로컬.
  **`LateUpdate`인 이유**: 카메라 확정 뒤여야 한 프레임 안 밀린다(앵글 교체 중에는 매 프레임 카메라가 움직인다).
- `Hide()` — 링 반납 + 비활성. `public event Action OnPressed`.

### - [x] Step 5 — `DodgeDirector` (유일 관리 지점)

`Assets/02. Scripts/Enemy/DodgeDirector.cs` (`EnemySpace`). `EnemyDirector`와 같은 오브젝트.

| 필드 | 기본 | 뜻 |
|---|---|---|
| `dodgeEnabled` | on | 끄면 이 층만 죽는다(기존 연출 토글 규율) |
| `fallbackAmbushClips` (`ClipAlignment[]`) | — | **`EnemyDefinition.AmbushAttacks`가 진짜 출처**(§2-6). 이건 미배선 종류용 폴백이며, 둘 다 비면 그 적은 후보에서 빠진다 |
| `dodgeClipLeft/Right` | Dodge_Left/Right | 구르기(0.8초) |
| `rollSpeed` | 1.6 | 구르기 <b>클립</b> 배속. 클립은 예산에 안 들어간다(정정 3 B) |
| `rollMoveDuration` | 0.25 | 원호로 <b>자리를 옮기는</b> 시간. 예산에 들어가는 건 이것뿐 |
| `ringExposure` | 0.4 | **링 수축 시간만**. 적 클립 길이와 무관하다(§4-2 A) — 패턴 링과 같은 감각을 유지한다 |
| `dodgeWindow` | 0.15 | 임팩트 **이후**의 유예(초). 입력 창은 **링 등장 ~ `impactTime + dodgeWindow`가 통째로 하나**이고 그 안이면 언제 눌러도 성공 — ±정밀 판정 없음(노드는 손가락, 회피는 온몸). 링이 보이는데 안 먹히면 "이르다"가 아니라 "버그"로 읽힌다 |
| `minIdleWindow` | 0.8 | 이보다 짧은 공백에서는 발동하지 않는다(실측 45% 지점) |
| `margin` | 0.2 | 공백 끝과의 여유 |
| `cooldown` | 6 | 연속 발동 방지 |
| `stageDistance` | 2.5 | **사전 접근** 대기 거리. 결투 거리(≈1m)보다 확실히 커야 현재 상대로 안 보인다 |
| `lungeDistance` | 1.5 | 기습 순간 마저 좁혀 멈춰 서는 거리 |
| `rollDegrees` | 60 | 원호 각 |
| `inputHandler` | (씬) | 회피 입력의 출처. 키는 `IngameInputs`의 `Player/Dodge`(`<Keyboard>/space`)가 소유한다 — 인스펙터 키 필드는 없다 |

흐름:

0. **사전 접근** — `EnemyDirector.OnDuelScheduled(DuelPlan)` 구독.
   - 쿨다운이 지났고 대기 후보가 없으면 `PickIdleAmbusher(BuildVisibilityTest())`로 하나 고른다.
   - 자리는 **`plan.PlayerPosition`(플레이어가 갈 자리)에서 `stageDistance` 떨어진 지점**,
     마감은 **`plan.PlayerArriveTime`**(플레이어와 같은 도착 시각). **이동만 건다 — 공격 예약은 아직 안 건다.**
     ⚠ `plan.PlayerPosition`이지 플레이어의 현재 위치가 아니다. 현재 위치로 붙이면 적만 뒤에 남는다(정정 2 B).
   - 이 적을 `stagedAmbusher`로 들고 있는다. **발동 여부는 여기서 안 정한다** — 창 길이를 아직 모른다.
   - ⚠ 붙여 놓고 발동을 안 해도 손해가 없다. 그냥 배회로 돌아간다(§11-6 "보이는 이동은 아무 일도 아니다").
   - 못 따라온 경우는 1단계의 `IsIdle` 조건이 알아서 거른다 — **새 예외 처리 없음**.
     (그리고 그런 구간은 애초에 플레이어가 대시 중이라 공백이 없다.)
1. **트리거** — `CharacterActionPlayer.OnIdleWindow(start, end)` 구독. **폴링 없음.**
   - **기습 클립 목록** = `stagedAmbusher.Definition.AmbushAttacks`(비면 `fallbackAmbushClips`) — §2-6, 적 종류가 소유한다.
   - `클립별 필요시간 = 와인드업(ResolvedImpactSpan / Speed) + dodgeWindow + rollDuration/rollSpeed + margin`
     ⚠ **클립마다 와인드업이 달라 필요시간도 다르다** — 상수로 굳히면 안 된다.
   - ⚠ **랜덤은 예산 뒤가 아니라 예산 안에서 뽑는다**(§2-6): 남은 창에 **들어가는 클립만 추린 뒤 그중 랜덤**.
     먼저 뽑고 안 맞으면 포기하는 방식은 긴 클립을 뽑을 때마다 이벤트가 통째로 사라진다.
     추린 목록이 비면 이번 공백은 발동하지 않는다. 후보가 둘 이상이면 **직전에 쓴 클립은 제외**한다
     (`NextHitClip`이 번갈아 돌리는 것과 같은 목적 — 같은 모션이 연달아 나오면 랜덤으로 안 읽힌다).
   - `end − max(start, Time.time) < 필요시간` → 무시(대기 후보는 그대로 두거나 놓아준다).
   - `enemyDirector.CurrentAttacker == Attacker.Enemy` → **무시**(Research §5 — 적 칼이 이미 오는 구간).
   - `stagedAmbusher`가 null이거나 `!IsIdle`이거나 화면 밖이면 무시.
   - 발동 시각 = `max(start, Time.time)`. `start`가 미래면 그때까지 대기한다(도착 전에 띄우면 달리면서 회피하게 된다).
2. **발동** — `impact = end − (rollDuration/rollSpeed + margin)`, 즉 **구르기가 창 안에 끝나도록 임팩트를 역산한다.**
   - 적: `AssignAttack(기습 클립, impact, 플레이어에서 lungeDistance 떨어진 자리, 플레이어, maxAttackSpeed)`.
     `ClipAlignment`가 시작 시각·배속을 스스로 역산하므로 **와인드업이 먼저 보이는 것은 공짜다**(§4-2 A).
     자리는 **적 → 플레이어 방향** 위에서 잡는다(플레이어 쪽으로만 오므로 무대를 안 넘는다).
   - **카메라: `cameraDirector.SetAmbusher(view)`를 여기서 부른다**(Step 2-b).
     ⚠ 흐름 0(사전 접근)에서 부르면 안 된다 — 발동 없이 끝날 수도 있는 후보를 담으면
     **매 패턴 화면이 넓어졌다 좁아졌다** 한다. "공격하는 적"이 확정된 순간이 여기다.
   - UI: `impact − ringExposure`에 `prompt.Show(view.transform, Vector3.up * 1.7f, ringExposure)`.
     **링은 여기서 처음 뜬다** — 그 전 구간은 적 모션만 보인다.
3. **입력** — `prompt.OnPressed` 또는 `InputHandler.OnDodgePressed`.
   **링이 떠 있는 동안이면 언제 눌러도 성공이다** — 창은 `promptShown`부터 `impact + dodgeWindow`까지
   **하나로 이어지고**, 그 안에 ±판정이 없다. 실패는 창을 넘기는 것 하나뿐이다(`Fail`).
   ⚠ 링이 뜨기 전(텔레그래프 구간)의 입력은 여전히 무시다 — 그게 곧 "링이 유일한 단서"라는 계약이다.
   이른 입력을 실패로 치지 않는 이유도 같다(연타로 자멸한다).
   - **오토퍼펙트**(§3-1): `#if UNITY_EDITOR` 안에서 `handler.DebugAutoPerfect`가 켜져 있으면
     `Time.time >= impact`가 되는 **첫 프레임에 성공 처리**한다(= delta ≈ 0, 곧 Perfect).
     기존 오토플레이가 `ExpectedTime`에 `DebugForceInput`을 거는 것과 **같은 규율**이다.
     닷지 포인트·링은 그대로 띄운다 — 오토플레이는 연출을 보려고 켜는 것이다.
4. **성공** — `prompt.Hide()` + 구르기
   - 중심 = `enemyDirector.CurrentOpponent ?? 기습한 적`. **반경이 보존돼 `duelAnchor`(ImpactAnchor)가 안 벗어난다**(Research §5).
   - 방향: `±rollDegrees` 두 후보 중 **구른 뒤 위치에서 가장 가까운 적까지의 거리**가 큰 쪽. 무대 밖이면 감점.
   - `mover.RollArc(center, 부호각, rollDuration/rollSpeed)` + `action.PlayOneShot(부호 > 0 ? Right : Left, rollSpeed)`
5. **실패** — `dodgeTime + dodgeWindow`에 `prompt.Hide()` + `action.PlayHitReaction()`.
   카메라 피격 큐·체력은 `OnPlayerHit` 구독자가 이미 처리한다(새 배선 없음).
6. **뒷정리** — 성패와 무관하게 `enemyDirector.ReleaseAmbusher(view, failRetreatDistance, retreatDuration)`.
   어느 쪽이든 적은 찌르고 물러난다 — 가를 이유가 없다.
   **`cameraDirector.SetAmbusher(null)`도 여기서**(구르기·피격이 끝나는 시점). 대상만 비우면
   가중치가 감쇠로 빠져 **컷이 안 생긴다** — 칸 자체는 계속 남아 있다(Step 2-b).
7. **곡 정리** — `PatternHandler.OnAllPatternsCleared` 구독 → 진행 중이면 닷지 포인트·예약 회수(잔존물 규율).

### - [x] Step 6 — 씬 배선  *(완료 — 기습 클립 저작 포함)*

**⚠ 아래 "남은 것"은 해소됐다.** `Samurai_Male_Diagonal` · `Samurai_Male_Dice` · `Samurai_Male_DoubleSlice`
세 에셋 모두 `ambushAttacks`가 채워져 있다(확인: 각 2개 이상, 첫 항목 `duration 0.49` / `impactTime 0.40` / `speed 2`).
**그래서 지금의 미발동은 클립 문제가 아니다** — 정정 4를 볼 것.


**완료된 배선**(BattleScene 저장됨)
- `UI/Canvas/PatternHandler/DodgePoint` 생성 — `Image`(Knob 스프라이트, 90x90, raycastTarget) + `DodgePointView`,
  `FocusRingParent`와 같은 층. `ringStartScale`은 씬의 `focusRingStartScale`과 같은 **5**로 맞춤.
- `EnemyDirector` 오브젝트에 `DodgeDirector` 추가 — actionPlayer·mover(플레이어) / enemyDirector / prompt / handler / cameraDirector 전부 배선.
- 구르기 클립 `Dodge_Left.anim`·`Dodge_Right.anim` 배선.

**남은 것: `AmbushAttacks` 저작** — `Assets/04. Datas/EnemyDefinitions/Samurai_Male_Diagonal.asset`의 배열이 비어 있어
**지금은 후보 자격 미달로 한 번도 발동하지 않는다**(무연출 기습을 만들지 않기 위한 의도된 동작).
클립 선택은 저작 판단이라 비워 뒀다 — `Stab_5` / `LightAttk1~4` / `SideKick` 같은 짧은 것을 2~3개, **길이를 섞어** 넣을 것.

- `Canvas` 아래 `DodgePoint`(Image + `DodgePointView`), `FocusRingParent`와 같은 층.
- `EnemyDirector` 오브젝트에 `DodgeDirector` 추가 → action / enemyDirector / mover / prompt / handler 배선.
- **기습 클립은 `EnemyDefinition` 에셋마다 배선한다**(`AmbushAttacks` 배열, 종류당 2~3개 권장).
  기존 적 공격 클립을 `Tools/Animation Clip Trimmer`로 잘라 쓰면 새 클립 저작이 없다.
  `ImpactTime`을 안 찍으면 **트림 끝이 임팩트**로 폴백된다 — 찌르는 동작을 통째로 보여 주고 끝나는 순간이 닿는 순간이라
  기본값으로 자연스럽다(견제와 같은 성질).
  ⚠ **길이를 섞어 넣는 게 유리하다** — 창이 짧을 때는 짧은 클립만 후보에 남으므로,
  긴 것만 넣으면 좁은 공백에서 아예 안 뜬다.
  종류별로 비면 `DodgeDirector.fallbackAmbushClips`가 대신하고, 둘 다 비면 그 종류는 기습을 안 한다.

### - [~] Step 8 — 자격 판정 시점 교정 (정정 4)  *(8-1~8-3 완료 / 8-4는 플레이 필요)*

**새 상태도, 새 이벤트도 만들지 않는다.** 조건 하나를 시각의 함수로 바꾸고, 검사를 두 시점으로 나눈다.

- [x] **8-1. `EnemyView` — `CanWander()`를 시각의 함수로 일반화**
  `Time.time` 하나로 굳어 있던 판정을 `IsFreeBy(float t)`로 뽑고, 기존 두 용도는 그 위에 얹는다.
  **술어가 하나라 배회 조건과 기습 조건이 어긋날 수가 없다**(`IsIdle`의 원래 미덕 그대로).

  ```csharp
  /// <summary>t 시점에 이 적이 자유로운가. IsIdle은 t = 지금인 특수해다.</summary>
  private bool IsFreeBy(float t)
  {
      if (Current == Phase.Dying || dissolving) return false;
      if (hasPendingAttack || hasPendingReaction) return false;
      if (reactionUntil > t) return false;
      if (moving && moveEnd > t) return false;   // ⚠ moving 자체가 아니라 '그때까지 안 끝나는가'
      return true;
  }

  public bool IsIdle => IsFreeBy(Time.time);
  /// <summary>t까지 지금의 이동·리액션이 끝나 다른 일을 시킬 수 있는가.</summary>
  public bool ReadyBy(float t) => IsFreeBy(t);

  private bool CanWander() => IsIdle;
  ```

  ⚠ `IsIdle`의 의미가 미세하게 넓어진다 — 이번 프레임에 `moveEnd`를 지났지만 `TickMove`가 아직 안 돈 경우가
  false에서 true로 바뀐다. 배회 입장에서는 **한 프레임 일찍 풀리는 것뿐**이라 무해하다(어차피 다음 프레임에 풀린다).

- [x] **8-2. `DodgeDirector.HandleIdleWindow` — 예약 시점 조건을 `ReadyBy(from)`으로**
  `from`은 이미 계산돼 있는 발동 시각(`max(start, Time.time)`)이다. **새 값이 없다.**
  **사유 문자열도 둘로 쪼갠다** — 지금은 `stagedAmbusher == null`과 `!IsIdle`이 한 메시지라
  로그만 보고는 "후보를 못 골랐다"와 "후보가 이동 중이다"가 안 갈린다(정정 4의 남은 불확실 1건).

  ```csharp
  else if (stagedAmbusher == null) reason = "후보 없음";
  else if (!stagedAmbusher.ReadyBy(from)) reason = "후보가 늦게 도착";
  ```

- [x] **8-3. `DodgeDirector.Fire` — 진짜 실격 판정은 여기서**
  *(구현 시 추가: 취소도 `LogResult("발동 취소 — …")`로 한 줄 남긴다. 조용히 접히면 8-4에서 "왜 안 떴는지"가 안 보인다.)*
  *"사전 접근을 못 따라온 적을 거른다"*는 원래 의도가 사는 자리다. 이 시점에 이동 중이면
  달리면서 찌르게 되므로 그냥 접는다(다음 공백에서 새로 고른다).

  ```csharp
  private void Fire()
  {
      if (!ambusher.IsIdle) { Abort(); return; }   // 못 따라왔다 — 달리면서 찌르지 않는다
      fired = true;
      ...
  }
  ```

  ⚠ `Abort()`는 `fired == false`라 `ReleaseAmbusher`를 안 부른다(맞다 — 아직 아무 예약도 안 걸었다).
  쿨다운도 안 걸린다. **다음 공백이 곧바로 다시 시도한다** — 발동이 0으로 돌아가지 않게 하는 조건이다.

- [ ] **8-4. 재실측** — 한 곡 돌려 로그를 다시 본다. 이제 사건당 최대 세 줄이 남는다(토글은 `logWindows` 하나) —
  **공백**(기존) · **발동**(`LogFire`: 클립·실효 배속·`와인드업 vs 링` 순서 판정·임팩트까지 남은 시간) ·
  **결과**(성공은 오차·이동시간·방향·중심·**반경**까지 — 원호가 실제로 원호인지 Step 7 검증용).
  ⚠ 발동 로그의 배속은 authored 값이 아니라 `ResolvePlaySpeed`(`maxAttackSpeed` 클램프 포함)다 —
  `AssignAttack`이 다시 역산하므로 그쪽을 찍어야 로그와 화면이 같은 얘기를 한다. 기대값: `후보 없음`/`후보가 늦게 도착`이 남으면
  그때야 `PickIdleAmbusher` 쪽(무리가 전부 이동 중·화면 밖)이 진짜 병목이라는 뜻이다.
  ⚠ **`minIdleWindow`는 이번에 건드리지 않는다** — 실측 11건 중 4건이 0.74~0.77로 문턱에 붙어 있지만,
  그건 발동이 실제로 일어난 뒤에 조일 값이다. 두 노브를 같이 움직이면 어느 쪽이 들은 건지 모른다.

### - [~] Step 9 — 시전 시작을 당긴다 (정정 5)  *(9-1~9-4 완료 / 9-5는 플레이 필요)*

- [x] **9-1. `EnemyView` — 실격 사유를 문자열로 낸다**
  `ReadyBy`가 5개 항을 뭉쳐 놓아 로그에서 "이동이 안 끝났다"와 "리액션 중"이 안 갈린다(정정 5의 남은 의심 2건).
  `IsFreeBy`를 `BusyReasonBy(float t)` 위에 얹어 **술어를 계속 하나로 유지**한다.
  ⚠ **문자열 보간을 쓰지 않는다** — `IsIdle`은 `TickWander`가 매 프레임 부르므로 상수 문자열만 돌려준다.

- [x] **9-2. `DodgeDirector` — `plan.PlayerPosition` 캐시**
  `HandleDuelScheduled`에서 `stagedPlayerSpot`에 담는다(함정 1). `Fire`의 `lungeSpot` 기준점을 그것으로 바꾸고,
  없으면 기존대로 플레이어 현재 위치로 폴백한다.

- [x] **9-3. `DodgeDirector.HandleIdleWindow` — 요구를 둘로 나눈다**
  - `impactTime = end − (rollMoveDuration + margin)`을 **후보 선별보다 먼저** 계산한다(②가 그 값을 쓴다).
  - ① 창 안 요구: `standing = end − max(start, now) >= dodgeWindow + rollMove + margin`
    (`minIdleWindow`가 이 하한을 겸한다 — 별도 식을 만들지 않는다).
  - ② 텔레그래프 요구: `RequiredTime`을 `max(와인드업, ringExposure)`만 남기고,
    비교 대상을 `window`가 아니라 **`impactTime − Time.time`(리드타임)**으로 바꾼다.
  - `fireTime = Time.time` — 즉시 발동. `start`를 기다리지 않는다.
  - 대기 후보 조건도 `ReadyBy(impactTime)`으로(창 시작이 아니라 임팩트 때 서 있으면 된다).
  - 로그에 **리드타임과 서 있는 구간을 같이** 찍는다. 두 숫자가 갈렸으므로 하나만 보면 원인 추적이 안 된다.

- [x] **9-4. `minIdleWindow` 0.8 → 0.6**
  ①의 요구값(0.60)과 같다. **이번엔 노브 두 개를 같이 움직이는 게 아니다** —
  0.8은 애초에 정정 3의 옛 예산에서 역산한 값이고, 그 예산이 여기서 바뀐다.

- [ ] **9-5. 재실측** — 기대: `창 부족`이 0.6 미만(실측 12건 중 2건)으로 줄고, 발동이 실제로 뜬다.
  이후 남는 사유가 `후보 리액션 중`류면 그건 §11-6 무리 상태 문제이지 예산 문제가 아니다.

### - [~] Step 10 — 도착 시각을 앞당긴다 (정정 6)  *(10-1~10-4 완료 / 10-5는 플레이 필요)*

**둘 다 이미 있는 장치를 쓴다** — 새 시계도, 새 상태도 없다.

- [x] **10-1. `EnemyView.ScheduleApproach(to, latest)`** — `EarliestArrival`을 통과하는 public 진입점.
  `ApproachDuel`이 내부에서 쓰던 규율(`ScheduleMove(pos, to, now, EarliestArrival(...))`)을 그대로 한 줄로 노출한다.
  `EarliestArrival`은 private으로 남는다(뷰 밖에서 시각을 계산할 이유가 없다).

- [x] **10-2. `DodgeDirector` 사전 접근이 그걸 쓴다** — `ScheduleMove(...)` → `ScheduleApproach(target, plan.PlayerArriveTime)`.
  마감은 그대로 두되 **실제 도착은 적 자기 `moveSpeed`가 정한다.** `후보 이동 중` 8건이 여기서 풀린다.

- [x] **10-3. `EnemyDirector` — 플레이어 도착도 거리로 앞당긴다**
  `BuildDuelPlan`이 `playerTarget`을 계산한 **직후** 그 거리로 마감을 조인다
  (`ResolvePlayerArriveTime`은 재접근 비율 전용이라 그대로 두고, 그 뒤에 한 번 더 `min`을 건다 — 둘 다 앞당김이라 순서 무관).

  ```csharp
  float arrive = ResolvePlayerArrival(playerArriveTime, distanceToPlayerTarget);
  // min(latest, now + distance / cruiseSpeed + playerArriveSlack)
  ```

  **⚠ 여기 한 곳만 고치면 `PlayerCombatMover`(이동 배속)와 `CharacterActionPlayer`(`convergeUntil`)가
  같은 값을 읽으므로 둘이 어긋날 수가 없다.** 적이 `EarliestArrival`로 하는 것과 같은 수법이다.

- [x] **10-4. `playerArriveSlack` 노브(기본 0.15초)** — 0으로 두면 플레이어가 `cruiseSpeed` 딱 그대로 달려
  "천천히 다가가는 압박감"이 사라진다. 여유를 남겨 그 감각을 조절할 수 있게 둔다.
  ⚠ **이 값을 창 길이로 만들면 안 된다** — 그러면 정정 6 A의 "창에 반비례하는 속도"가 되돌아온다. 상수여야 한다.

- [ ] **10-5. 재실측** — 기대: ① `후보 이동 중`이 사라진다 ② **로그 줄 수 자체가 늘어난다**
  (지금은 움직이는 패턴이 `end − start <= 0`으로 발행조차 안 되므로, 그게 열리면 새 창이 로그에 등장한다).
  `도착까지` 값이 0.12~0.31에서 벗어나는지가 A가 실제로 들었는지의 지표다.

### - [~] Step 11 — 발동한 기습을 읽히게 만든다 (정정 7)  *(11-1~11-3 완료 / 11-4는 플레이 필요)*

- [x] **11-1. `DodgeDirector` — 링 노출을 리드에 맞춘다**
  - `ringExposure`(상수 0.4) → `minRingExposure`(0.4) · `maxRingExposure`(0.8) 둘로 나눈다.
  - `RequiredLead`는 `max(와인드업, minRingExposure)` — **식은 그대로**, 상수 이름만 바뀐다.
  - 발동 시 `ringDuration = clamp(impact − Time.time, min, max)`을 잡아 두고
    `Time.time >= impact − ringDuration`에 `prompt.Show(..., ringDuration)`.
  - ⚠ **시작 크기(`ringStartScale`)는 안 건드린다** — 크기로 남은 시간을 읽는 학습이 깨진다.
  - 발동 로그에 `실제 노출`을 같이 찍는다(리드마다 다른 값이므로 로그에 없으면 추적이 안 된다).

- [x] **11-2. `CameraDirector` — 기습자 전용 감쇠**
  `[SerializeField] float ambusherWeightDamping = 0.12f`. `UpdateFraming`에서 기습자 칸만 이 값으로 `Damp`.
  `Damp(current, target, damping)` 오버로드 하나 추가 — **`ResolveWeight`는 계속 공유**한다(두 칸이 어긋날 수 없다는 §7-2 규율 유지).

- [x] **11-3. `EnemyView.ScheduleWithdraw(target, duration)`** — 리액션 없는 물러남.
  `Resolve`에서 크로스페이드 분기만 뺀 판이며 나머지(예약 해제·`Phase.Recover`·`retreatUntil`·`ScheduleMove`)는 동일.
  `EnemyDirector.ReleaseAmbusher`가 `Resolve` 대신 이것을 호출한다.
  ⚠ `Resolve`는 그대로 둔다 — 실패 반응 경로는 이것과 요구가 다르다(그쪽은 리액션이 목적이다).

- [ ] **11-4. 재실측** — ① 링이 리드 전 구간(짧은 경우) 보이는가 ② 발동 순간 카메라가 기습자를 바로 잡는가
  ③ 적이 찌른 뒤 **구르지 않고** 물러나는가.
  ⚠ 에셋 `AmbushAttacks`의 `speed: 2`는 이번에 안 건드린다 — 저작 판단이고, 링이 전 구간을 덮으면
  급한 문제가 아니다. 그래도 모션이 안 읽히면 그때 1로 낮춘다(노브 하나, 회귀 없음).

### - [~] Step 12 — 제자리 기습 · 닷지 포인트 좌표 (정정 8)  *(12-1~12-3 완료 / 12-4는 플레이 필요)*

- [x] **12-1. `DodgePointView` — 투영 카메라를 분리한다**
  `[SerializeField] Camera worldCamera` + `Camera.main` 폴백. **첫 호출(월드→화면)에만** 쓰고,
  두 번째(화면→캔버스 로컬)는 Overlay라 계속 null이다.
  카메라 뒤(`WorldToViewportPoint().z <= 0`)면 아이콘을 감춘다 — 좌표가 뒤집혀 반대편에 찍히는 것을 막는다.

- [x] **12-2. `EnemyView.ScheduleWithdraw` → `ReleaseAction()`** — **이동을 없앤다.**
  배회 정지·예약 해제·`Phase.Recover`까지만 하고 `ScheduleMove`를 부르지 않는다.
  `moving`이 false이므로 `CanWander`가 통과해 **다음 틱에 배회로 복귀**한다(무리 안에서 자연히 자리를 되찾는다).

- [x] **12-3. `EnemyDirector.ReleaseAmbusher` 축소** — `PickRetreatTarget`·`failRetreatDistance`·`retreatDuration`을
  이 경로에서 전부 뺀다. 무대 계산이 필요 없어졌으므로 **메서드가 한 줄이 된다**.
  ⚠ `Resolve`와 실패 후퇴 경로는 그대로 둔다 — 그쪽 요구는 여전히 "물러난다"이다.

- [ ] **12-4. 재실측** — ① 닷지 포인트가 기습자 머리 위에 붙는가(앵글 교체 중에도) ② 적이 찌른 뒤 제자리에 남는가
  ③ 남은 적이 다음 배회에 자연스럽게 섞이는가.

### - [ ] Step 7 — 검증

- Step 0의 로그로 **실제 발동 빈도**를 확인한다. 한 곡에 0번이면 예산을 깎는다(구르기 배속 → `ringExposure` 순).
- **적 모션이 링보다 먼저 보이는가**(§4-2 A의 목적 — 텔레그래프와 타이밍 단서의 분리).
- **사전 접근한 적이 현재 상대로 안 보이는가**(`stageDistance` 검증). 발동을 안 했을 때 조용히 배회로 돌아가는가.
- **카메라**: 발동 순간 기습자가 구도에 들어오고 끝나면 감쇠로 빠지는가. **컷·튐이 없는가**(가중치 0 리셋 검증).
  발동 안 한 패턴에서 화면이 안 흔들리는가(사전 접근 구간을 안 담은 게 실제로 지켜지는가).
- 링 수축이 끝나는 순간과 적 칼이 닿는 순간이 같은가.
- 아이콘이 적을 정확히 따라가는가 — **앵글 교체 중에도**(`LateUpdate` 검증).
- 구른 뒤 플레이어–현재 적 거리가 유지되는가(원호가 실제로 원호인가), 적이 다시 플레이어를 보는가(`TickGaze`).
- 구르기가 다음 공격 클립 시작 전에 끝나는가. 실패 경로에서 카메라 피격 큐가 나오는가.
- **오토퍼펙트를 켜고 한 곡 돌렸을 때 한 번도 안 맞는가**(연출 확인용 오토플레이가 실제로 쓸 수 있는가).
  오토퍼펙트를 끄면 자동 회피도 같이 꺼지는가(토글이 하나라는 게 지켜지는가).

---

## 안 하는 것 (지금은)

- **`Attacker.Enemy` 패턴에서의 발동** — 적 칼이 이미 오는 구간이라 기하가 얼어 있어야 하고, 둘을 동시에 피하게 된다.
- **회피 전용 애니메이터 스테이트·레이어** — 기존 듀얼 슬롯으로 충분하다(§11-4와 같은 판단).
- **히트스톱·전용 카메라 큐** — 회피 성공은 "안 맞았다"라 멈출 임팩트가 없다. 실패는 기존 피격 큐를 그대로 탄다.
- **동시 다중 기습** — 닷지 포인트가 둘이면 어느 링이 어느 적 것인지 읽을 수 없다.
- **회피 성공 보상(점수·게이지)** — 판정 시스템에 손대는 순간이라 별도 논의.
</content>
