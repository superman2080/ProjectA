# Research — OpponentBinding (예약이 상대를 잘못 잡는 문제)

## 1. 증상

플레이 중 **결투 위치에 도달하지 않은 적이 갈라진다.** 플레이어 앞에 서 있어야 할 적은 멀쩡하고, 멀리 있는 개체가 베인다.

## 2. 원인 — 두 시각이 뒤집혀 있다

`EnemyDirector`에서 **"이 패턴의 상대는 누구인가"가 정해지는 시각**과 **상대가 교체되는 시각**이 서로 다른 이벤트에 걸려 있고, 순서가 뒤집힌다.

### 2-1. 상대를 잡는 시각 = 패턴 큐 투입

`HandlePatternQueued`(`Assets/02. Scripts/Enemy/EnemyDirector.cs:570` 부근)가 `OnPatternQueued`에서 한 번에 다 한다:

```csharp
var reservation = new Reservation { …, opponent = currentOpponent, … };   // :586
…
var plan = BuildDuelPlan(currentOpponent, info.Template, arriveTime);      // :600
OnDuelScheduled?.Invoke(plan);                                            // :602  ← 플레이어 수렴도 여기서
currentOpponent.AssignAttack(attack, impactTime, plan.EnemyPosition, …);  // :605
projectileDirector.Reserve(…, currentOpponent.RingPosition);              // :610
StageOnDeck(arriveTime);                                                  // :613
```

### 2-2. 상대가 교체되는 시각 = 이전 패턴 완료

`PromoteOpponent()`는 `KillOpponent` 안에만 있다(`:697`). `KillOpponent`는 `ResolveReservation`(`:655`)에서, 그건 `HandlePatternComplete`에서 온다.

### 2-3. 순서

`exposureDuration`(0.5초) 리드타임이 엔트리 간 입력 간격보다 길면 **다음 패턴이 이전 패턴 완료보다 먼저 큐에 오른다.**

```
t = L − 0.1   패턴 N+1 큐        currentOpponent = A   → 예약·AssignAttack·투사체·수렴계획 전부 A 기준
t = L         패턴 N 완료         A 처치 확정 → PromoteOpponent() → currentOpponent = B
t = L + 0.1   A 갈라짐
```

**B는 `AssignAttack`을 한 번도 못 받는다.** 서 있던 자리(대기석 2m 또는 링 6m) 그대로다.

### 2-4. 연쇄 피해

- **N+1의 예약 주인이 A다.** N+1을 성공하면 `ResolveReservation` → `KillOpponent(r.opponent)` = **A를 다시 죽인다**(`:649`, `:655`).
- A는 `SwapToCorpse`에서 이미 **풀로 반납**됐고(`:753` — *"산 적은 여기서 풀로 돌아간다"*), `KillOpponent`가 `SpawnIntoRing`으로 링을 즉시 보충하므로(`:696`) **A가 링에 다시 서 있을 수 있다.** 그 개체가 멀리서 갈라진다.
- `BuildDuelPlan`도 A의 위치로 계산되므로 **플레이어까지 엉뚱한 지점으로 달려간다**(`OnDuelScheduled` → `CharacterActionPlayer`/`PlayerCombatMover`).
- A는 자기 임팩트 0.1초 전에 N+1의 `AssignAttack`을 받아 이동 목표가 덮어씌워진다(`EnemyView.cs:263`) → **A조차 제 위치에 못 닿는다.**

## 3. 실측 — 얼마나 자주인가

현재 채보에서 "다음 패턴 큐 시각 < 이전 패턴 마지막 노드 시각"인 쌍을 셌다:

```
Dreamer_Lv10.asset   entries=87
어긋난 쌍  79/86 (92%)   최대 선행 1.100초
```

**첫 패턴만 정상이고 사실상 전부 어긋난다.** 구조적 겹침(CLAUDE.md §3 패턴 겹침 규칙)이라 채보를 다시 구워도 사라지지 않는다.

## 4. 왜 단순 해결이 안 되는가

**"승격을 큐 시점으로 옮긴다"가 가장 짧은 수정이지만 규칙을 깬다.**

큐 시점에는 **이전 패턴의 성패를 모른다.** 그런데 상대 교체 여부가 성패에 달려 있다 —
`ResolveReservation`(`:653`):

```csharp
if (r.cue.killOnSuccess && playerSucceeded) { KillOpponent(…); return; }
opponent.Resolve(playerSucceeded, …);   // 실패 → 안 죽고 교전이 이어진다
```

사용자 확정 규칙: **한 패턴에서 실패하면 계속 그 상대와 교전한다.** 큐 시점 승격은 이 규칙과 양립할 수 없다.

## 5. 활용 가능한 기존 구조

- **`pendingTokens`가 FIFO Queue**(`:205`)다. `HandlePatternComplete`가 `Dequeue`(`:621`) 후 `ResolveReservation`. 즉 **예약은 판정 순서 그대로 흐른다.**
- CLAUDE.md §3: *"판정 대상은 선두 패턴이 완료/만료될 때 **같은 프레임에 즉시 승계**된다."* → **이전 패턴의 완료가 곧 다음 패턴이 판정 대상이 되는 순간**이다. 별도 이벤트가 필요 없다.
- **`StageOnDeck`이 이미 있다**(`:594`). 대기석을 현재 패턴 입력 중에 미리 결투 지점 쪽으로 들여보낸다. **이동을 미리 시작시키는 장치가 이미 존재한다** — 상대 확정과 무관하게 동작한다.
- `Reservation`(`:161`)에 `resolved` 플래그가 이미 있다. 상태 필드를 하나 더 두는 데 무리가 없다.
- `HandleAllCleared`(`:780`)가 `pendingTokens`·`reservations`를 통째로 비운다 — 미확정 예약 정리 지점이 이미 있다.

## 6. 실패 후 후퇴 — 지금 상태

`ResolveReservation` 실패 경로(`:659`) → `EnemyView.Resolve`(`EnemyView.cs:267`):

```csharp
if (!playerSucceeded) CrossFadeReaction(evadeStateName);
…
Current = Phase.Recover;
ScheduleFace(RingPosition + (RingPosition - transform.position));
ScheduleMove(transform.position, RingPosition, Time.time, Time.time + Mathf.Max(returnDuration, 0.01f));
```

**후퇴 목적지가 `RingPosition`이다 — 한 걸음이 아니라 링(6m)까지 통째로 돌아간다.** 0.5초(`returnDuration`) 만에.

### 6-1. `ScheduleMove`는 구간을 하나만 든다

`EnemyView.cs:152`:

```csharp
moveFrom = from;  moveTo = to;  moveStart = startTime;  moveEnd = …;
```

필드 한 벌뿐이라 **두 번째 호출이 첫 번째를 통째로 덮어쓴다.** "후퇴한 다음 다시 붙는다"를 표현할 수 없다.

### 6-2. 배정을 완료 시점으로 옮기면 후퇴가 아예 안 보인다

새 설계에서 `ResolveReservation` 안에서 실패 처리와 다음 예약 배정이 **같은 프레임**에 일어난다:

```
opponent.Resolve(false, …)      → ScheduleMove(…, RingPosition, …)     ← 후퇴 예약
BindNextReservation()           → AssignAttack → ScheduleMove(…, 결투위치, …)  ← 같은 프레임에 덮어씀
```

**후퇴가 한 프레임도 렌더되지 않는다.** 회피 애니메이션은 `reactionUntil` 홀드(`EnemyView.cs:180`) 덕에 재생되지만, 몸은 제자리에서 결투 위치로 갈 뿐이다.

즉 지금 그대로면 **"뒤로 물러나서 회피"가 "제자리에서 회피"가 된다.**

### 6-3. 계획이 '지금 위치'로 계산된다

`BuildDuelPlan`(`:479`)은 `opponent.transform.position`을 읽는다. 적이 **이동 중이면 그 값은 곧 떠날 위치**다.

후퇴든 대기석 진입(`StageOnDeck`)이든 링 등장(`SpawnIntoRing`)이든, 배정 순간 적이 움직이는 중이면 **플레이어의 수렴 목적지가 실제 만날 지점과 어긋난다.** 후퇴의 경우 특히 나쁘다 — 적이 멀어질 것을 모른 채 "이동 불필요"로 계산해 플레이어가 안 붙는다.

## 7. 제약

- **첫 패턴에는 선행 패턴이 없다.** 완료 시점에 배정하는 규칙만 두면 첫 패턴이 영영 배정되지 않는다.
- **여러 패턴이 동시에 큐에 있을 수 있다**(`activePatterns`). 배정 규칙은 2개 이상에서도 성립해야 한다.
- `PrepareStage`(`:287`)가 곡 시작 전에 이미 한 번 승격해 둔다.
- 배정을 미루면 적의 이동 창이 짧아진다 — 최소 엔트리 간격(0.4초) 수준. **`StageOnDeck`이 이 손실을 메워야 한다.**
