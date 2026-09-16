# Research — 관통 패턴의 복귀 (실패하면 적을 뚫고 되돌아온다)

## 1. 증상

결투 거리 커브(§11-9)가 **음수로 끝나는** 패턴(`Pattern(4, 3, 1, 5, 7)`처럼 적 뒤로 넘어가는 획)을 **실패**하면, 플레이어가 적 뒤에 선 채로 다음 패턴을 맞이하지 않고 **적을 관통해 원래 앞자리로 되돌아온다.**

성공에서는 안 보인다 — `killOnSuccess`가 기본으로 켜져 있어 적이 죽고 상대가 바뀌기 때문이다(§4).

## 2. 기제 — 어느 쪽에 설지는 축 하나가 정한다

`EnemyDirector.BuildDuelPlan`(`EnemyDirector.cs:1411`)의 세 줄이 전부다:

```csharp
Vector3 toEnemy = Vector3.ProjectOnPlane(opponent.Destination - player, Vector3.up);
Vector3 dir     = DuelGap.ResolveAxis(toEnemy, opponent == duelAxisOwner ? duelAxis : Vector3.zero);
Vector3 meet         = player + toEnemy * playerShare;
Vector3 playerTarget = meet - dir * (distance * playerShare);
```

**`playerShare`가 1이라 플레이어 위치가 식에서 통째로 소거된다**:
```
meet         = player + (enemy − player) = enemy
playerTarget = enemy − dir × distance
```
→ **다음 패턴에서 플레이어가 적의 어느 쪽에 설지는 오직 `dir`이 정한다.** 플레이어가 지금 앞에 있든 뒤에 있든 상관없다.

그리고 `dir`은 `DuelGap.ResolveAxis`가 정하는데, 그 함수는 **새로 잰 방향이 직전 축과 반대면 직전 축을 유지한다**:

```csharp
return hasPrevious && Vector3.Dot(axis, previousAxis) < 0f ? previousAxis.normalized : axis;
```

관통 후 플레이어는 적 뒤에 있으므로 새로 잰 `toEnemy`는 직전 축과 정확히 반대다 → **직전 축이 유지된다** → `playerTarget = enemy − (원래 축) × distance` = **원래 앞자리**. 플레이어가 그리로 걸어가고, 그 직선이 적을 관통한다.

## 3. `ResolveAxis`가 직전 축을 지키는 이유는 따로 있다 (⚠ 이 규칙을 지우면 안 된다)

그 가드는 **순간 위치에서 축을 파생시키는 것**을 막으려고 있다. `BuildDuelPlan`은 패턴 완료 시점에 불리는데(§11-1) 그때 직전 커브가 **아직 관통 구간을 그리는 중**일 수 있다. 그 전이 상태에서 축을 새로 재면 **지금 진행 중인 교전의 커브가 거울로 돌아** — 물러나야 할 때 적을 뚫고, 음수인데 관통하지 않는다.

즉 문제는 "직전 축을 지킨다"가 아니라 **"그 축이 영원히 안 뒤집힌다"**는 것이다. 관통이 **저작된 최종 상태**일 때는 뒤집히는 것이 맞다.

- **전이(transient)**: 커브가 그리는 도중의 위치 — 축을 여기서 파생시키면 안 된다.
- **최종(final)**: 커브의 **마지막 키 값이 음수** — 그 패턴이 끝나면 플레이어는 반대편에 **서 있는 것이 정상**이다.

둘을 가르는 정보가 이미 데이터에 있다: **커브 끝 키의 부호.**

## 4. 왜 실패에서만 보이는가

`ResolveReservation`(`EnemyDirector.cs:1744`)이 상대의 생사를 가른다:

| 경로 | 상대 | `duelAxisOwner` | 결과 |
|---|---|---|---|
| 성공 + `killOnSuccess` + 사슬 임계 충족 | **죽는다** | 다음 상대와 불일치 → `previousAxis = zero` | 축을 새로 잡는다 → 지금 선 자리가 그대로 앞이 된다(증상 없음) |
| **실패** | 살아남는다 | 그대로 | **직전 축 유지 → 관통해서 복귀** |
| 성공 + `killOnSuccess = false`(사슬 §11-5) | 살아남는다 | 그대로 | **같은 증상**(사용자는 실패로 만났지만 원인이 같다) |

→ 고칠 조건은 **성패가 아니라 "상대가 살아남았다 + 커브가 음수로 끝난다"** 하나다. 성패로 가르면 사슬 경로만 다르게 동작하는 규칙이 하나 더 생긴다.

## 5. 공짜로 따라오는 것 둘 (새 코드가 필요 없다)

- **플레이어의 180° 회전** — `PlayerCombatMover.ApplyPlan`이 이미 `facing = plan.EnemyPosition − target`으로 회전을 건다. 축이 뒤집히면 `EnemyPosition`이 반대편에 잡히므로 **"다시 마주본다"가 자동으로 성립**한다(`turnDuration` 0.15초).
- **적의 회전** — `EnemyView.TickGaze`가 idle이고 `Windup`/`Dying`이 아닐 때 매 프레임 플레이어 쪽으로 돌아본다(`EnemyView.cs:663`). 플레이어가 뒤로 넘어가면 그 공백에 적이 알아서 돌아선다.

## 6. 시점 — 뒤집기는 `BindNextReservation`보다 먼저여야 한다

`ResolveReservation`의 마지막 줄이 `BindNextReservation()`이고(§11-1 "확정이 곧 다음 패턴이 판정 대상이 되는 순간"), 그 안에서 다음 `BuildDuelPlan`이 돈다. 따라서 축을 뒤집는 일은 **같은 호출 안, 그 줄보다 앞**에서 끝나야 한다.

**⚠ 축을 0으로 비우는 것(재파생)과 뒤집는 것은 다르다.** 비우면 `BuildDuelPlan`이 **그 순간의 플레이어 위치**를 믿게 되는데, 계획은 임팩트보다 이르게 잡히므로(완료 = 마지막 노드, 임팩트는 `goodWindow` 뒤) 그때 플레이어는 **아직 앞에 있을 수 있다** → 축이 안 뒤집혀 증상이 그대로 남는다. **뒤집으면 언제 계획이 잡히든 결과가 같다**:

| 계획 시점의 플레이어 | 새로 잰 방향 | 뒤집은 직전 축과의 dot | `ResolveAxis` 결과 |
|---|---|---|---|
| 아직 앞 (관통 전) | 원래 축 | < 0 | 뒤집은 축 |
| 이미 뒤 (관통 후) | 뒤집은 축 | > 0 | 뒤집은 축 |

## 7. 인수인계는 이미 관통이 끝나기를 기다린다

`PlayerCombatMover.ShouldDeferHandover`가 **커브가 마지막 키를 지날 때까지 계획 적용을 미룬다**(`PlayerCombatMover.cs`). 그래서 실제로 이동·회전이 걸리는 시점에는 플레이어가 **이미 뒤에 서 있다** — 뒤집은 축으로 잡은 목표는 그 자리에서 몇십 cm 거리이고, 화면에는 "제자리에서 돌아서서 간격만 맞춘다"로 보인다.

## 8. 손댈 곳

| 파일 | 무엇 |
|---|---|
| `Pattern/Core/DuelGap.cs` | 커브가 음수로 끝나는가를 묻는 순수 함수 하나. **asmdef 안이라 테스트된다** |
| `Pattern/Tests/DuelGapTests.cs` | 그 함수 + 뒤집은 축이 두 경우 모두 이기는지 |
| `Pattern/Pattern.cs` | 게터 한 줄(다른 `DuelCurve*` 게터와 같은 관용구) |
| `Enemy/EnemyDirector.cs` | `ResolveReservation`의 **상대 생존 분기**에서 축 뒤집기 한 줄 |

`PlayerCombatMover`·`ChartPlayer`·`PatternHandler`는 **한 줄도 안 고친다.** 저작 필드도 0개다 — 커브 끝 부호가 곧 의도다(§2-1의 "분기가 아니라 데이터로 갈린다", §11-10의 "클립 유무가 곧 의도"와 같은 관용구).
