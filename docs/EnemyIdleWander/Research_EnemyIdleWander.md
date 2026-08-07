# Research — EnemyIdleWander (교전 대기 중인 적을 살아 있게)

## 문제

무리 안의 적 중 **지금 싸우는 상대(`currentOpponent`)가 아닌 적들이 완전히 정지**해 있어 심심하다.
`clusterSize`가 4면 화면에 항상 3명이 마네킹처럼 서 있다.

---

## 현재 상태

### 이미 살아 있는 부분

- **시선 추적** — `EnemyView.gazeTarget`이 플레이어를 향해 `gazeTurnSpeed`(360도/초)로 계속 돈다. 플레이어가 움직이면 대기 중인 적들도 고개를 돌린다.
- **크기 지터** — `Setup`에서 `transform.localScale`에 랜덤 지터를 준다(같은 모델 반복을 흐린다).

즉 **완전한 정지는 아니고, 발이 안 움직일 뿐이다.**

### 로코모션 구조 (`EnemyView`)

| 필드 | 값 | 의미 |
|---|---|---|
| `idleStateName` | `"Idle"` | 기본 자세 |
| `moveStateName` | `"Run"` | **이동 중에만** 드러내는 스테이트 |
| `minMoveDistance` | 0.3m | 이보다 짧게 움직이면 로코모션을 안 켠다 |
| `moveSpeed` | 3 m/s | 결투 접근 속도 |

```csharp
// ScheduleMove가 거리를 보고 Idle/Run을 정한다.
wantsLocomotion = 이동거리 >= minMoveDistance
```

> ⚠ **스테이트가 `Idle`과 `Run` 둘뿐이다.** 0.3m 미만은 **애니메이션 없이 위치만 미끄러지고**, 0.3m 이상은 **전력 질주(Run)**가 나온다. 배회에 쓸 중간 단계가 없다.

### `EnemyAnimator.controller` 스테이트

`Idle` · `Run` · `Attack` · `Death` · `Evade` · `KnockBack` · `Parry` — **Walk 없음.**

### ⚠ `Destination` 오염 — 가장 위험한 지점

```csharp
public Vector3 Destination => hasQueuedMove ? queuedTo : (moving ? moveTo : transform.position);
```

`EnemyDirector.BuildDuelPlan`이 **`transform.position`이 아니라 `Destination`으로 결투를 계획한다**(§11-1 — "배정 순간 적이 이동 중이면 현재 위치는 곧 떠날 위치다").

**배회에 `ScheduleMove`를 쓰면 그 배회 목적지가 결투 계획의 기준이 된다.** 플레이어가 적이 잠깐 들를 자리를 향해 달려가고, 적은 이미 딴 데 있다. 조용히 어긋나는 부류다.

### 배회하면 안 되는 적

| 상태 | 이유 |
|---|---|
| `currentOpponent` | 지금 싸운다 |
| `Phase.Dying` / `dissolving` | 죽는 중 |
| `hasPendingAttack` (공격·견제 예약) | `Pattern.EnemyFeint`가 그 구간을 이미 채운다(§11-4) |
| `hasPendingReaction` / `reactionUntil` 이내 | 리액션 클립 재생 중 |
| 집결지로 아직 걸어오는 중 | 도착이 우선 |

### 무리 제약

배회 반경이 `clusterRadius`(2m)를 넘으면 무리가 흐트러진다. `minSpacing`(2.5m)을 무시하면 서로 겹친다.
⚠ `clusterRadius`(2m) < `minSpacing`(2.5m)이라 **이미 4명이 반경 2m 안에 서로 2.5m 간격을 지킬 수 없다** — `PlaceInCluster`가 `minSpacing`을 반경 하한으로만 쓰는 이유다. 배회도 같은 규율을 따라야 한다.

### 걷기 클립을 새로 들이려면

팩에 `Special/Sp_Walk.fbx`(칼 뽑은 걷기)가 있다. 하지만:

> ⚠ **`Sp_Walk`는 이동 클립이라 무기 본이 안 따라온다.** 실측된 문제 그대로다 — `Sp_Run`에서 몸은 2.51m 전진하는데 칼은 0.07m만 움직였다(`docs/WeaponBoneBake`). 쓰려면 **프로젝트 `.anim`으로 복사 → `Weapon Bone Bake`(참조 `Sp_Idle`, 120fps)** 를 먼저 돌려야 한다.
> 그리고 `EnemyAnimator`에 `Walk` 스테이트와 전이를 추가해야 한다.

### 다른 방향 — 위치를 안 옮기는 방법

`Idle` 스테이트의 클립만 바꿔도 "살아 있음"은 만들 수 있다. 프로젝트에 이미 있는 대기 계열: `Katana_Idle` · `Samurai_Idle` · `BlockIdle`.
위치가 안 바뀌므로 `Destination` 오염도, 로코모션 불일치도, 무리 이탈도 **원리적으로 없다.**

---

## 정리 — 선택지 셋

| | 위치 이동 | 새 클립 | `Destination` 위험 | 무리 이탈 위험 |
|---|---|---|---|---|
| **A. 제자리 자세 변주** | 없음 | 불필요(기존 Idle 계열 3종) | 없음 | 없음 |
| **B. 소폭 드리프트** | 0.3m 미만 | 불필요(애니메이션 없이 미끄러짐) | **있음** | 낮음 |
| **C. 걸어서 배회** | 0.5~1.5m | **`Sp_Walk` 필요**(굽기 선행) | **있음** | 있음 |

B는 "애니메이션 없이 미끄러진다"가 그대로 결함이다 — 0.3m 미만은 발이 안 움직이는데 몸이 흐른다.
