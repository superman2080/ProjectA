# Research — EnemyFeint (도착 후 임팩트까지 적이 하는 동작)

## 요구 (확정)

- 적이 결투 위치에 **도착한 뒤부터 임팩트까지** 지정된 애니메이션을 재생해 생동감을 만든다.
- 대기 포즈가 아니라 **공격 또는 그에 준하는 액션 클립**이다.
- 대상은 **교전 상대 하나**. 무대 위 군중은 별도 플랜에서 다룬다.
- **`Attacker.Enemy` 패턴에는 없다** — 그쪽은 이미 `EnemyAttack`이 임팩트에 정렬돼 재생된다.
- 즉 이 기능이 채우는 것은 **`Attacker.Player` 패턴에서 적이 무방비로 서 있던 구간**이다.

## 그 구간은 실제로 얼마나 되나 (실측)

`Dreamer_Lv10` 채보(87엔트리, 150 BPM)를 에디터에서 직접 계산했다.
**86개 전부 `Attacker.Player`** — 이 곡에서는 사실상 모든 엔트리가 견제 대상이다.

`W` = 표적이 되는 순간(직전 패턴 완료) → 임팩트

| | min | p10 | p50 | avg | p90 | max |
|---|---|---|---|---|---|---|
| **W** | 0.50 | 0.90 | 1.30 | 1.48 | 2.10 | 2.10 |
| 적 이동거리(share 0.85) | 0.40m | 0.71m | 1.03m | 1.18m | 1.67m | 1.67m |
| 적 이동시간(share 0.85) | 0.13s | 0.24s | 0.34s | 0.39s | 0.56s | 0.56s |
| **도착 후 남는 시간** | **0.37** | 0.66 | 0.96 | 1.09 | 1.54 | 1.54 |

⚠ **"도착 후"는 언제나 `0.735 × W`다.** `TakeTargetForWindow`가 목표 거리를 `cruiseSpeed × W / playerShare`로 잡아
적 이동량이 창에 **비례**하기 때문 — **창이 길어져도 여유가 안 생긴다.** 비율이 고정이다.

배속 없이 클립이 통째로 들어가는 비율:

| 트림 길이 | W 전체(적 정지) | 도착 후(0.735W) |
|---|---|---|
| 0.6s | 97% | 97% |
| 0.8s | **97%** | 77% |
| 1.0s | 77% | 49% |
| 1.5s | 49% | 23% |
| 2.0s | 23% | **0%** |

→ **일반적인 공격 클립(1.5~2초)은 어느 쪽으로도 안 들어간다.** 트림이 필수다.
→ 적을 세우면(`playerShare` 1.0) 0.8초 클립이 97%로 들어간다. Plan이 그 전제를 택했다.

## 관련 코드

| 위치 | 역할 |
|---|---|
| `Pattern.cs:55~105` | `ClipAlignment` 슬롯 4개(`enemyAttack`·`playerAttack`·`playerParry`·`enemyDeath`)를 이미 소유. **새 슬롯의 자리가 여기다** |
| `Pattern.cs:163~196` | `OnValidate`가 슬롯별 임팩트 검증 + 역할에 맞는 슬롯이 비었을 때 경고 |
| `ClipAlignment` (`Pattern/Core`) | 트림·임팩트·배속·시작시각 역산. asmdef 보유, 유닛테스트 있음 |
| `EnemyView.AssignAttack` (`:320`) | 공격 예약. `pendingAttack`/`pendingScheduleStart`/`hasPendingAttack`/`attackStarted` 상태를 들고 `TryStartAttack`이 시작 |
| `EnemyView.ApproachDuel` (`:347`) | 결투 위치로 이동. `moveEnd`(`:92`)가 **도착 시각**이다 |
| `EnemyView.EarliestArrival` (`:370`) | 도착을 앞당긴다 — 이 함수 때문에 "도착 후 남는 시간"이 생긴다 |
| `EnemyDirector.BindReservation` (`:642~`) | 상대·`impactTime`·`arriveTime`·`template`을 **한자리에서 다 아는 유일한 지점**. 여기서 배정한다 |
| `EnemyView` 애니메이터 슬롯 | `Attack`(+`AttackSpeed`, `attackPlaceholder`) / `Death`(+`DeathSpeed`) / 리액션 스테이트들 |

## 설계상 이미 풀려 있는 것

1. **정렬 수학이 이미 있다.** `ClipAlignment.ResolveScheduleStart(impactAlignTime, earliest)` + `ResolvePlaySpeed`가
   "이 시각에 임팩트 프레임이 오도록 시작 시점과 배속을 역산"한다. 그리고 **임팩트가 오서링되지 않으면 트림 끝으로 폴백한다**(§6).
   → 견제 클립은 임팩트를 안 찍으면 **클립 끝이 임팩트 시각에 맞는다.** 새 수학이 필요 없다.
2. **도착 시각을 뷰가 이미 안다.** `moveEnd`가 그것이고, `ResolveScheduleStart`의 `earliest` 인자에 그대로 넣으면
   "도착 전에는 시작하지 않는다"가 성립한다.
3. **애니메이터 슬롯이 비어 있다.** `Attacker.Player` 패턴에서는 `AssignAttack`이 `attack == null`로 들어와
   `Attack` 스테이트를 아무도 안 쓴다(`EnemyDirector.cs:612` — 적이 공격자일 때만 `EnemyAttack`을 싣는다).
   → **새 스테이트를 만들 필요가 없다.** 그 슬롯을 그대로 쓴다.

## 제약 · 함정

- ⚠ **창이 클립보다 길 때**: 배속을 낮춰 늘이면 슬로모션이 된다. **시작을 늦춰 클립이 제 속도로 끝에 붙게** 해야 한다
  (`ResolveScheduleStart`가 정확히 이 동작이다 — 늦게 시작해 임팩트에 맞춘다). 남는 앞부분은 서 있는 구간으로 남는다.
- ⚠ **창이 클립보다 짧을 때**: 배속으로 압축된다. 상한(`maxSpeed`)에 걸리면 `ResolveScheduleStart`가 `earliest`로 클램프되고
  클립이 임팩트를 넘겨 이어진다 — 그 뒤에 `Resolve`의 리액션(회피/패링)이 크로스페이드로 끊는다. 허용 가능.
- ⚠ **`Resolve`가 슬롯을 뺏는다.** 성패 확정 시 `CrossFadeReaction`이 즉시 리액션 스테이트로 넘어간다.
  견제 클립은 임팩트 직전까지만 보이면 되므로 문제 없지만, **`hasPendingAttack`을 반드시 같이 내려야** 다음 프레임에 되살아나지 않는다
  (`Resolve:401`이 이미 그렇게 한다 — 같은 필드를 쓰면 공짜로 지켜진다).
- ⚠ **이동과 겹치면 안 된다.** `ApplyLocomotion`(`:253`)이 `moving`인 동안 Run/Idle을 유지하므로,
  견제가 도착 전에 시작되면 로코모션이 덮어쓴다. `earliest = moveEnd`가 그 방어다.
- ⚠ **후퇴 인수인계.** 실패 직후 패턴에서는 `ApproachDuel`이 `ScheduleMoveAfter`로 후퇴 뒤에 붙는다.
  이때 `moveEnd`는 **후퇴가 끝난 뒤의 도착 시각**이어야 한다 — 예약 순서상 그렇게 되지만 검증 항목으로 남긴다.
- **비어 있으면 무연출.** 클립을 안 지정한 패턴은 지금과 완전히 동일하게 동작해야 한다(기존 규율).

## 이름

`Pattern`의 기존 슬롯이 전부 "누가 무엇을 하는가"로 이름 붙어 있다(`EnemyAttack`/`EnemyDeath`/`PlayerParry`).
이 슬롯은 **공격처럼 보이지만 실제로 닿지 않는 동작**이므로 `EnemyFeint`(견제)를 제안한다.
대안: `EnemyStandbyAction`, `EnemyEngageAction`. 저작자가 부를 이름으로 정하면 된다.
