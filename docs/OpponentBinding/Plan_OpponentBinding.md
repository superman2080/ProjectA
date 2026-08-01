# Plan — OpponentBinding

근거: `docs/OpponentBinding/Research_OpponentBinding.md`

## 목표

패턴이 **실제로 자기 상대에게** 배정되게 한다. 그 상대가 결투 위치까지 도달한 뒤 갈라지게 한다.

**지켜야 할 규칙**(사용자 확정): 한 패턴에서 실패하면 **계속 그 상대와 교전**한다.

## 설계 결정

### D-1. 큐 접수와 상대 배정을 **분리한다**

지금 `HandlePatternQueued`가 한 번에 하는 일을 둘로 쪼갠다.

| 도막 | 시각 | 하는 일 |
|---|---|---|
| **접수** | 패턴 큐 투입 (`OnPatternQueued`) | 토큰 발급, cue 소비, 예약 생성(**상대 미정**), 대기석 미리 이동(`StageOnDeck`) |
| **배정** | 이 패턴이 판정 대상이 되는 순간 | 상대 확정, `BuildDuelPlan`, `OnDuelScheduled`, `AssignAttack`, 투사체 예약 |

**성패를 알아야 상대를 알 수 있다.** 이전 패턴이 성공했으면 그 적은 죽고 다음 적이 상대이며, 실패했으면 같은 적이 이어진다. 그 답은 이전 패턴이 완료돼야 나온다 — 그래서 배정을 거기까지 미룬다.

**반대로 접수는 미룰 수 없다.** 토큰·cue는 `ChartPlayer`가 밀어 넣는 FIFO와 짝이 맞아야 하고(`EnqueueCue` ↔ `pendingTokens`), 대기석 이동은 시간이 걸리는 일이라 일찍 시작해야 한다.

>>> 

### D-2. 배정 시점은 **선행 예약이 확정되는 순간**이다 — 새 이벤트를 만들지 않는다

CLAUDE.md §3: *"판정 대상은 선두 패턴이 완료/만료될 때 **같은 프레임에 즉시 승계**된다."*

즉 **이전 패턴의 `OnPatternComplete`가 곧 다음 패턴이 판정 대상이 되는 순간**이다. `PatternHandler`에 "판정 대상이 됐다" 이벤트를 새로 뚫을 필요가 없다.

규칙 하나로 적는다:

> **선두 예약은 접수 즉시 배정한다. 뒤따르는 예약은 앞 예약이 확정될 때 배정한다.**

- 첫 패턴 / 곡 중간 공백 뒤 → 앞에 미확정 예약이 없으므로 **즉시 배정**(제약 6-1 해결)
- 2개 이상이 동시에 큐에 있어도 성립 — `ResolveReservation`이 끝날 때마다 **다음 미배정 예약 하나**를 배정한다. `pendingTokens`가 FIFO라 순서가 보장된다(제약 6-2 해결)

>>> 

### D-3. 승격은 **지금 자리에 그대로 둔다**

`PromoteOpponent()`는 `KillOpponent` 안에 남긴다. 옮기지 않는다.

배정이 완료 시점으로 내려오면 **승격과 배정이 같은 호출 흐름 안에서 이 순서로 일어난다**:

```
HandlePatternComplete(N)
  └ ResolveReservation(N)
      ├ 성공 → KillOpponent(A) → PromoteOpponent() → currentOpponent = B
      │  실패 → A.Resolve(...)  → 승격 없음        → currentOpponent = A   ← 규칙 유지
      └ BindNextReservation()   → currentOpponent를 N+1에 배정
```

**두 시각이 하나의 호출로 합쳐지므로 뒤집힐 수가 없다.** 원인(Research 2-3)이 구조적으로 제거된다. 실패 규칙은 `ResolveReservation`의 기존 분기가 그대로 지킨다 — **이 계획은 그 분기를 건드리지 않는다.**

>>> 

### D-4. 이동 창 손실은 `StageOnDeck`이 메운다

배정이 늦어지면 적이 걸을 시간이 줄어든다. 배정 후 임팩트까지는 최소 엔트리 간격(약 0.4~0.5초)뿐이다.

`StageOnDeck`은 **상대 확정과 무관하게** 대기석을 미리 들여보내므로 이 손실을 정확히 메운다. 다만 지금 조건이 어긋난다:

```csharp
if (radial.magnitude - stagingDistance <= approachSpeed * window) return;   // window = 지금 패턴의 창
```

**`window`를 지금 패턴의 창으로 재면 안 된다.** 배정 후 실제로 주어지는 시간은 그게 아니라 **다음 패턴의 배정~임팩트 구간**이다. 긴 패턴이 지나갈 때 "여유 있다"고 판단해 링에 두면, 정작 배정 직후엔 0.4초밖에 없다.

→ **보수적인 고정 창(`bindToImpactWindow`, 기본 0.45초)으로 판단한다.** 배정 후 항상 그만큼만 있다고 보고 미리 들여보낼지 정한다.

>>> 

### D-5. 미배정 예약은 **버릴 때 같이 버린다**

`HandleAllCleared`가 이미 `reservations`를 통째로 비운다. 배정 여부와 무관하게 정리되므로 추가 처리가 없다.

곡이 끝나 배정되지 못한 예약이 남아도 다음 곡에 새 리스트로 시작한다.

>>> 

### D-6. 실패 후 후퇴를 **살린다** — `EnemyView`가 이동 구간을 둘 든다

Research 6 — 지금은 후퇴가 표현 자체가 안 된다. 두 가지가 겹쳐 있다:

1. 후퇴 목적지가 **`RingPosition`(6m)**이다. 한 걸음이 아니라 링까지 통째로 돌아간다.
2. `ScheduleMove`가 **구간을 하나만** 들어서, 같은 프레임의 `AssignAttack`이 후퇴를 통째로 덮어쓴다. → 후퇴가 한 프레임도 안 보인다.

고칠 것 둘:

- **후퇴는 짧게.** `failRetreatDistance`(기본 1.5m)만큼 뒤로 물러난다. `RingPosition` 복귀는 **상대 자격을 잃었을 때만** 한다(처치되거나, 다른 적에게 자리를 넘길 때). 상대가 이어지는데 링까지 가는 건 그 자체로 틀렸다.
- **`EnemyView`가 예약 구간을 하나 더 든다.** 리액션/후퇴가 진행 중일 때 들어온 `AssignAttack`은 현재 구간을 덮지 않고 **그 뒤에 이어 붙는다**. 후퇴가 끝나는 시각에 접근이 시작된다.

```
t=L        실패 확정 → 후퇴 구간 [지금 위치 → 뒤로 1.5m], retreatDuration 동안
t=L        AssignAttack → 접근 구간 예약 [후퇴 끝 지점 → 결투 위치], 시작=후퇴 끝, 종료=arriveTime
t=L+0.25   후퇴 끝 → 접근 시작
t=arrive   결투 위치 도착
```

**두 구간이면 충분하다.** 접근 도중에 또 다른 이동이 끼어들 일이 없다 — 다음 배정은 이 패턴이 확정된 뒤에야 온다.

>>> 

### D-7. 계획은 적의 **'갈 곳'**으로 세운다 — 이게 "플레이어가 붙는" 실체다

`BuildDuelPlan`이 `opponent.transform.position`을 읽는데(Research 6-3), 배정 순간 적이 이동 중이면 **그건 곧 떠날 위치**다.

후퇴에서 특히 나쁘다 — 배정 시점에 적은 아직 결투 위치에 서 있으므로 계획이 *"둘 다 제자리"*로 나온다. 그 직후 적은 1.5m 물러나고, **플레이어는 안 붙는다.** D-6만으로는 사용자가 지적한 문제가 그대로 남는다.

→ **`EnemyView`에 `Destination`을 노출하고**(이동 중이면 최종 목적지, 아니면 현재 위치) `BuildDuelPlan`이 그걸 읽는다.

그러면 후퇴 목적지 기준으로 중점이 다시 잡히고, 중점이 플레이어에게서 **멀어지므로 플레이어가 전진한다.** 이게 요청한 "플레이어가 붙는다"의 구현이다.

**후퇴만의 문제가 아니다.** 대기석 진입(`StageOnDeck`)·링 등장 중에 배정이 걸릴 때도 같은 어긋남이 있다. 한 곳을 고쳐 세 경우가 같이 맞는다.

>>> 

### D-8. Quickshift 배속 **상한을 없앤다**

후퇴 직후 플레이어가 붙어야 할 거리는 `failRetreatDistance`(1.5m)라 `sprintMaxDistance`(1.0m)를 넘는다 → **Quickshift 분기**다. 그런데 지금 배속이 잘린다.

`CharacterActionPlayer.HandleDuelScheduled`(`:384`):

```csharp
float speed = Mathf.Clamp(clipLength / window, 1f, maxQuickshiftSpeed);   // maxQuickshiftSpeed = 4
```

클립이 1초인데 창이 0.15초면 필요한 배속은 6.7배다. 4로 잘리면 **클립이 창 안에 완주하지 못하고**, 몸은 도착했는데 다리는 대시 도중에 끊긴다. 후퇴처럼 창이 짧은 상황일수록 이 절단이 확실하게 걸린다.

→ **상한 제거.** `speed = Max(1, clipLength / window)`.

**상한이 필요 없는 이유**: 배속이 올라가는 만큼 **화면에 남는 시간이 같이 줄어든다.** 8배속 Quickshift는 0.12초짜리라 "튀는 클립"이 아니라 **순식간에 붙는 그림**으로 읽힌다. 요청한 "매우 빠르게"가 이거다.

하한 1은 유지한다 — 느리게 늘이면 진짜로 미끄러진다.

`maxQuickshiftSpeed` 필드는 **지운다.** 남겨 두면 "여기 상한이 있다"는 잘못된 신호가 인스펙터에 남는다.

>>> 

---

## 단계

- [x] **Step 1 — `Reservation`에 배정 상태 추가**
  `public bool bound;` 한 줄. `resolved`와 같은 성격의 상태 필드다.

- [x] **Step 2 — `HandlePatternQueued`를 접수만 하도록 축소**
  남기는 것: 토큰 발급, cue 소비, `impactTime` 계산, 예약 생성(`opponent = null`), `StageOnDeck`.
  빼는 것: `BuildDuelPlan` / `OnDuelScheduled` / `AssignAttack` / 투사체 `Reserve` → 전부 Step 3으로.
  끝에서 **"앞에 미배정·미확정 예약이 없으면 지금 배정"**(D-2 전반부).

- [x] **Step 3 — `BindReservation(Reservation)` 신설**
  Step 2에서 빼낸 네 가지를 여기서 한다. `currentOpponent`를 예약에 기록하고, 계획을 세워 발행하고, `AssignAttack`과 투사체를 건다.
  `currentOpponent`가 null이면(링 고갈) 배정하지 않고 `bound`만 세워 다시 시도하지 않는다 — 지금도 무연출로 지나가는 경로다.
  `arriveTime` 계산은 기존 식을 그대로 옮긴다(적 클립이 있으면 `ResolveScheduleStart`, 없으면 `impactTime`).

- [x] **Step 4 — `ResolveReservation` 끝에서 다음 예약 배정**
  기존 성공/실패 분기는 **손대지 않는다**(D-3 — 실패 규칙의 소유자다).
  분기가 끝난 뒤 `BindNextReservation()` 호출: `reservations`에서 **가장 오래된 미배정 예약 하나**를 찾아 `BindReservation`.
  `KillOpponent`가 이미 `PromoteOpponent`를 마친 뒤라 `currentOpponent`가 정답이다.

- [x] **Step 5 — `StageOnDeck` 판단 창 교체 (D-4)**
  `arriveTime` 인자를 없애고 `[SerializeField] float bindToImpactWindow = 0.45f`로 판단한다.
  이동 종료 시각은 계속 `arriveTime`이 필요하므로 인자는 유지하되 **판단에만 고정 창을 쓴다**.

- [x] **Step 6 — `EnemyView`에 예약 구간 + `Destination` (D-6, D-7)**
  - `Destination` 프로퍼티: 이동 중이면 `moveTo`(예약 구간이 있으면 그쪽의 목적지), 아니면 `transform.position`
  - `ScheduleMoveAfter(to, endTime)`: 현재 구간이 끝나는 시각부터 시작하는 **두 번째 구간**을 예약한다. `TickMove`가 현재 구간을 마치면 이어받는다
  - `AssignAttack`이 리액션/후퇴 중이면 `ScheduleMove` 대신 `ScheduleMoveAfter`를 쓴다
  - `Resolve` 실패 경로: 목적지를 `RingPosition`이 아니라 **뒤로 `failRetreatDistance`(기본 1.5m)**, 시간은 `retreatDuration`(기본 0.25초).
    `RingPosition` 복귀는 **상대 자격을 잃을 때만** — `PromoteOpponent`에서 이전 상대를 링으로 돌려보낸다
  - `BuildDuelPlan`이 `opponent.transform.position` 대신 `opponent.Destination`을 읽는다 (D-7)

- [x] **Step 7 — Quickshift 배속 상한 제거 (D-8)**
  `CharacterActionPlayer`:
  - `speed = Mathf.Max(1f, clipLength / window)` — `Mathf.Clamp(..., maxQuickshiftSpeed)` 대체
  - `maxQuickshiftSpeed` 필드 삭제 + 주석에서 "상한을 두는 이유는 창이 아주 짧을 때 튀기 때문" 문장 갱신
  - 씬에 남은 직렬화 값은 필드가 사라지면 자동으로 무시된다(별도 정리 불필요)
  ※ Sprint 분기는 건드리지 않는다. 후퇴 수렴은 거리상 항상 Quickshift로 간다(D-8).

- [x] **Step 8 — 문서 갱신**
  - `CLAUDE.md` §6(EnemyCombat 관련 서술)과 §3 겹침 규칙에 "상대 배정은 판정 대상 승계 시점" 한 줄
  - `docs/!Guides/Guide_EnemyCombat.md`에 같은 내용 반영

- [ ] **Step 9 — 검증(플레이)**
  - 연속 성공 — 매 패턴마다 **다른** 적이, **결투 위치에서** 갈라진다
  - **실패 → 같은 적과 다시 교전한다**(규칙 유지 확인)
  - 실패 시 적이 **눈에 보이게 물러났다가** 다시 붙는다(D-6 — 한 프레임 만에 사라지지 않는다)
  - 그때 **플레이어가 전진해 거리를 좁힌다**(D-7 — 적만 왕복하지 않는다)
  - 그 전진이 **Quickshift 고배속으로 순식간에** 붙는다 — 대시가 도중에 끊기지 않는다(D-8). 로그의 `Quickshift x?.??`가 4를 넘는지 확인
  - 연속 실패 2~3회 — 적이 점점 뒤로 밀려나 링까지 가지 않는다(후퇴가 누적되지 않는지)
  - 실패 직후 성공 — 그 적이 죽고 다음으로 넘어가는지
  - 링에 서 있는 적이 갈라지는 일이 **없다**
  - 플레이어 수렴 목적지가 실제 상대 쪽인지(엉뚱한 방향으로 안 뜀)
  - 곡 중단(`Stop`) 후 재시작 시 미배정 예약 잔존 없음
  - 짧은 패턴(1~2노드) 연속 구간에서 적이 제때 도달하는지 (D-4 창 값 조정)

## 범위 밖

- `killOnSuccess = false` 엔트리의 연출 (지금 채보는 전부 true)
- 링 인원·반경 튜닝
- 승격 규칙 자체의 변경 (D-3 — 건드리지 않는다)

> **일부 폐기(2026-08-02).** `StageOnDeck`/`bindToImpactWindow`는 무대 고정으로 제거됐다.
> **상대 배정을 판정 대상 승계 시점에 한다는 규칙은 그대로 살아 있고**, 창으로 표적을 고르게 되면서 오히려 더 중요해졌다. → `docs/StageTraversal/`
