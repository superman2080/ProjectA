# Research — StageTraversal (월드 고정 원형 무대 + 플레이어 이동)

## 1. 요구

- 원형 무대를 **월드 원점에 고정**한다. 플레이어를 따라다니지 않는다.
- 적은 무대 위 자기 자리에 서 있고, **플레이어가 찾아간다**.
- 적 스폰은 **플레이어 시야 밖**에서 일어난다.
- 카메라는 이번 범위 밖(시점 변경 예정).

## 2. 지금은 정반대다 — 모든 것이 플레이어 기준

```csharp
private Vector3 Center => arenaCenter != null ? arenaCenter.position : transform.position;
```

씬에서 `arenaCenter`는 **플레이어 Transform**이다(`DefaultScene.unity:3668`이 vcam의 `TrackingTarget`과 같은 fileID를 가리킨다).

→ **`Center`가 매 프레임 플레이어를 따라간다.** 그리고 아래가 전부 `Center`에서 파생된다:

| 소비처 | 위치 | 하는 일 |
|---|---|---|
| `SpawnIntoRing` | `:402` | `EnemyRing.AngleToPosition(Center, angle, radius)` — 링 위치 |
| `StageOnDeck` | `:685` | 대기석을 `Center` 기준 `stagingDistance`로 |
| `BuildDuelPlan` | `:530~` | 중점·리시 전부 `Center` 기준 |
| `DuelDirection` | | `duelAnchor − Center` |
| 기즈모 | `:1134` | 링 그리기 |

원설계 문서(`docs/EnemyCombat/Plan_EnemyCombat.md:17`)에 명시돼 있다:

> | 무대 | **원형 아레나.** 플레이어 중앙 고정, 적은 링 외곽에 랜덤 배치. **전진 이동 없음** |

**"전진 이동 없음"이 전제였고, 이번 요구가 그 전제를 뒤집는다.**

### 2-1. 표류 버그는 이 모순의 증상이었다

`maxOffset` 리시는 **월드 고정점(`ArenaOrigin`)** 개념인데 나머지는 전부 플레이어 기준이다. `Center`로 재는 순간 "중앙 이탈"이 아니라 "한 걸음 길이"를 재게 되어 매 교전 링 쪽으로 흘렀다. `Awake`에 원점을 캐시해 막았지만, **모델이 둘인 상태 자체는 남아 있다.**

무대를 월드에 고정하면 `Center`가 상수가 되고 **이 부류의 버그가 원천적으로 사라진다.**

## 3. 배치·선택 코어 (`Assets/02. Scripts/Enemy/Core/EnemyRing.cs`, asmdef + 테스트)

### `PickSpawnAngle(occupiedAngles, viewYaw, minAngleGap, candidateCount = 36)`
```csharp
float score = Mathf.Cos((angle - viewYaw) * Mathf.Deg2Rad);   // 낮을수록 등 뒤
bool spaced = HasClearance(occupiedAngles, angle, minAngleGap);
```
- 후보 36개를 원주에 균등 배치 → **간격을 지키는 후보만 경쟁** → 그중 시야에서 먼 각도
- **1차원(각도)이다.** 원 위의 점만 고르고 반경은 따로(`ringRadius ± radiusJitter`)
- 시야 판정이 **각도 근사**다 — 실제 절두체가 아니라 `cos(각차)`. 무대가 플레이어 중심이라 성립했다

### `PickNextOpponent(candidateAngles, currentAngle)`
```csharp
float delta = Mathf.Repeat(candidateAngles[i] - currentAngle, 360f);   // 반시계 한 방향
```
- 다음 상대를 **각도 반시계 순**으로. *"한 바퀴 훑는 그림"*(원설계)
- **거리를 전혀 안 본다** — 플레이어 중심에서는 모두 등거리였기 때문

`EnemyRingTests.cs`가 두 함수를 덮고 있다. 규칙이 바뀌면 테스트도 같이 바뀐다.

## 4. 이동 계획 (`BuildDuelPlan`)

```csharp
Vector3 enemyPos = opponent.Destination;
Vector3 toEnemy  = ProjectOnPlane(enemyPos - center, up);
Vector3 mid      = center + toEnemy * 0.5f;             // ← 0.5 하드코딩
Vector3 playerTarget = mid - dir * (distance * 0.5f);
// 리시: ArenaOrigin에서 maxOffset(1.5m)으로 클램프
return new DuelPlan(playerTarget, playerTarget + dir * distance, arriveTime);
```

- `center`가 플레이어 위치이므로 **`toEnemy`는 플레이어→적 벡터**, `mid`는 그 중점
- **둘 다 움직인다**는 전제. 적 몫은 `AssignAttack(… plan.EnemyPosition …)`으로 전달
- `OnDuelScheduled(DuelPlan)` 구독자: `PlayerCombatMover`(위치), `CharacterActionPlayer`(로코모션)

## 5. 실측 — 창은 음악이 정한다

현재 채보(`Dreamer_Lv10`, 87엔트리)에서 배정(이전 패턴 마지막 노드) → 도착(이번 Deadline):

```
표본 86   최소 0.50s / 중앙 1.30s / 평균 1.48s / 최대 2.10s
```

플레이어 현재 이동 ≤ 1.5m → **1.0 m/s**(사람 걷기 1.4 m/s보다 느리다).

거리별 체감 속도:

| 거리 | 속도 | |
|---|---|---|
| 1.5m | 1.0 m/s | 지금 |
| 4.0m | 2.7 m/s | 조깅 |
| 7.4m | 5.0 m/s | 전력질주 |

**창은 못 바꾸므로 거리가 유일한 레버다.** 동시에 **최소 창 0.5초**가 상한을 건다 — 그 구간에 10m를 가면 20 m/s(순간이동)다.

## 6. 이동 실행 (`EnemyView` / 플레이어)

- `ScheduleMove(from, to, start, end)` — 선형 보간, 구간 하나 + 예약 구간 하나
- `EarliestArrival` — 적은 `moveSpeed`(3 m/s)로 **빨리 가서 선다**
- 플레이어 로코모션(`CharacterActionPlayer.HandleDuelScheduled`)은 **거리로 분기**한다:
  ```csharp
  if (distance <= sprintMaxDistance) Sprint;            // 1m 이하, 배속 없음
  else Quickshift;  speed = Max(1, clipLength / window) // 클립 1초
  ```
  → **평균 창 1.48초 > 클립 1초**라 배속 하한 1에 걸려 **클립이 먼저 끝나고 0.48초는 미끄러진다.** 거리를 늘리면 이 구멍이 커진다.

## 7. 지금 "적을 플레이어에게 데려오는" 장치들

플레이어가 가는 모델에서는 존재 이유가 사라진다:

| 대상 | 위치 |
|---|---|
| `maxOffset` / `arenaOrigin` / `ArenaOrigin` | 리시 |
| `StageOnDeck` / `stagingDistance` / `bindToImpactWindow` | 대기석 전진 |
| `minConvergeTime` | 수렴 생략 판단 |
| `BuildDuelPlan`의 중점·클램프 | 절반씩 나누기 |
| `onDeck` | 미리 뽑아 미리 이동 |
| `entryDistanceMul` / `entryDuration` | 링 밖에서 걸어 들어옴 |
| `EnemyView.ReturnToRing` / `RingPosition` 복귀 | 링으로 돌아감 |
| `EnemyView.MoveSpeed` / `EarliestArrival` | 적 접근 속도 |

## 8. 제약

- **`OnDuelScheduled`는 유지해야 한다.** `PlayerCombatMover`·`CharacterActionPlayer`가 이것만 보고 움직인다(디렉터는 플레이어를 모른다는 규율).
- **상대 선택은 `PromoteOpponent`(처치 시점)에서 일어나고, 창은 `BindReservation`(배정 시점)에서야 알 수 있다.** 창으로 표적을 고르려면 선택을 배정 쪽으로 옮겨야 한다. 둘은 `ResolveReservation` 한 호출 안에 있어 **같은 프레임**이다.
- 적이 안 움직이면 `TickGaze`가 항상 돈다(`moving`이 false라서) → 전원이 플레이어를 바라본다. **의도한 그림이고 추가 작업 없음.**
- 시야 밖 스폰이면 걸어 들어올 이유가 없다 → **즉시 배치 가능**(프리웜·팝인 비용 감소).
- `Pattern.duelDistanceOffset`(모션 리치)은 그대로 살아야 한다 — 플레이어가 적 앞 어디에 서는지를 정한다.
