# Research — 반격 패턴 (PatternCounter)

## 요구

**`Attacker.Player` 패턴(플레이어가 무방비한 적을 베는 패턴)에서 실패하면 플레이어가 대미지를 입는다.**

현재는 그 반대다 — CLAUDE.md §6:

> **⚠ 실패는 역할로 갈린다.** … **`Attacker.Player`면 피격 자체가 없다** — 적이 애초에 휘두르지 않았으므로
> 헛스윙으로 끝나고, `OnPlayerHit`이 안 나므로 **카메라 피격 큐(`PatternMiss`)도 체력 감소도 없다**.

즉 지금 `Attacker.Player` 실패의 화면상 사실은 "적이 막았다(패링)" 또는 "적이 물러났다(회피)" 뿐이고,
플레이어는 아무 대가도 치르지 않는다.

## 현재 흐름 (실패 시)

실패는 **두 시각**에 관측된다. 이 둘이 반격 설계의 뼈대다.

| 시각 | 이벤트 | 구독자 | 하는 일 |
|---|---|---|---|
| **첫 미스 순간** (임팩트보다 최대 `goodWindow`+α 이르다) | `OnJudgeTargetFirstMiss` | `CharacterActionPlayer.HandleJudgeTargetFirstMiss` | 예약된 베기 취소 · **`Attacker.Enemy`면** `pendingHitTime = impactTime`에 피격 예약 |
| | | `EnemyDirector.HandleFirstMiss` | 투사체만 즉시 실패 확정. **적 리액션은 안 건드린다** |
| **패턴 완료(Deadline)** | `OnPatternComplete` | `EnemyDirector.HandlePatternComplete` → `ResolveReservation(r, false)` | 후퇴 거리 결정 → `EnemyView.Resolve` (패링/회피) → `OnEnemyReacted` |

### 플레이어 쪽 — `CharacterActionPlayer.cs:1195`

```csharp
if (currentAttacker != EnemySpace.Attacker.Enemy)
{
    RaiseSwingEnded(); // 적 무방비 — 맞지 않는다. 진행 중이던 스윙만 끊는다.
    return;
}

hasPendingHit = true;
pendingHitTime = pendingImpactAlignTime;
```

- `currentAttacker`는 `SchedulePendingSuccess`(:838)에서 **`enemyDirector.CurrentAttacker`로 캐시**된다. 템플릿 자체는 `info.Template`으로 그 자리에 이미 있다.
- `TryStartPendingHit()` → `PlayHitReaction()` → `hitClips` 재생 + **`OnPlayerHit` 발행**.
- `PlayerHealth`는 `OnPlayerHit`만 구독한다(`PlayerHealth.cs:35`). **체력·카메라 피격 큐는 그 이벤트에 이미 매달려 있어 새 배선이 0이다.**

### 적 쪽 — `EnemyDirector.cs`

- `BindReservation` → `AssignEngageClip`: `Attacker.Player`면 `Pattern.EnemyFeint`(견제)를 걸고, 아니면 `AssignAttack(r.attack, …)`.
- `r.attack`은 `Attacker.Enemy`일 때만 채워진다(:1374).
- `ResolveReservation(r, false)`: `ResolveRetreatDistance`가 창을 보고 `failRetreatDistance` 또는 0(제자리 패링)을 돌려주고, `EnemyView.Resolve`가 그 값으로 `evade`/`parry` 스테이트를 고른다. 거기서 `OnEnemyReacted(Parry|Evade)`가 난다.

## 빈 슬롯이 이미 있다

`Pattern.enemyAttack`은 **`Attacker.Enemy` 전용**이다. `Attacker.Player` 패턴에 배선하면
`Pattern.OnValidate`(`Pattern.cs:511-528`)가 "쓰이지 않는 슬롯"이라고 **경고만 하고 무시**한다.

즉 **`Attacker.Player` + `enemyAttack` 배선 = 지금 표현 불가능한(무의미한) 조합**이고,
그 조합에 "실패하면 적이 이 클립으로 반격한다"는 의미를 주면 **새 필드가 0개**다.
(§2-1의 "슬롯을 비우면 마감 = Deadline … 분기가 아니라 데이터로 갈린다"와 같은 관용구.)

## 제약 — 반격 클립의 창은 매우 짧다

반격을 걸 수 있는 가장 이른 시각은 **첫 미스 순간**이다(그전엔 실패가 확정되지 않았다).
임팩트까지 남는 실시간은 최악의 경우 `goodWindow`(0.1초) + `impactOffset`뿐이다.

이것은 **사망 클립과 완전히 같은 제약**이고 이미 문서화돼 있다(CLAUDE.md §11-3):

> 재생은 처치 확정 즉시 시작한다. … 그래서 **사망 클립의 `ImpactTime`은 트림 시작 근처에 찍어야 한다.**

반격 클립도 같다 — **`ImpactTime`을 트림 시작 근처에 찍는다.** 안 그러면 `ClipAlignment`가
배속으로 벌충하려 하고 `maxAttackSpeed` 상한에 걸려 칼이 늦게 도착한다.

첫 미스가 **첫 노드**에서 나면 창은 오히려 넉넉하다(패턴 길이 전체). 즉 창은 0.1초 ~ 패턴 길이로 흔들리며,
`EnemyView.TryStartAttack`이 시작 시점에 남은 시간으로 배속을 재계산하므로 **자기 수정된다.**

## 충돌 지점 — 실패의 결말이 둘이 될 수 없다

`ResolveReservation`의 실패 분기는 지금 **반드시** 패링 또는 회피로 끝난다.
반격 패턴에서 그걸 그대로 두면 적이 **반격 클립을 재생하다가 Deadline에 패링 모션으로 갈아탄다.**

따라서 반격 패턴은:
- `ResolveRetreatDistance`가 **0을 돌려줘야** 한다(물러나면 반격 칼이 안 닿는다).
- `reactionClip`이 **null이어야** 한다(`EnemyParry`로 덮이면 안 된다).
- `OnEnemyReacted`(Parry/Evade)를 **안 내야** 한다 — 반격은 막은 것도 피한 것도 아니다.
  `EffectCondition.Always` 큐는 그대로 나므로 이펙트 저작 경로는 살아 있다(§7-4).

## 손대는 파일

| 파일 | 변경 |
|---|---|
| `Pattern/Pattern.cs` | `CountersOnFail` 게터 하나 + `OnValidate` 경고 조건 완화 |
| `Character/CharacterActionPlayer.cs` | `HandleJudgeTargetFirstMiss`의 역할 가드에 반격 경로 추가 |
| `Enemy/EnemyDirector.cs` | `HandleFirstMiss`에서 반격 배정 · `ResolveReservation`/`ResolveRetreatDistance`의 반격 분기 |

`PlayerHealth`·`CameraDirector`·`PatternHandler`·`EnemyView`는 **한 줄도 안 고친다.**

---

## 개정 — 반격이 아니라 **상호 공격**이다

첫 미스에 반격을 배정하는 방식은 창이 `goodWindow`(0.1초)까지 쪼그라드는 문제를 안고 있었다.
**적이 패턴 시작부터 같이 휘두르면** 그 문제가 통째로 사라진다 — 창이 `Attacker.Enemy` 패턴과 완전히 같아진다.

> **견제(`EnemyFeint`)를 진짜 공격으로 바꾼 것**이 이 설계의 전부다.
> 견제는 "표적이 된 순간부터 임팩트까지 적이 하는, **닿지 않는** 동작"이고(§11-4),
> 상호 공격은 같은 구간에 **닿는** 동작을 놓는 것이다. 구간도, 정렬 시각도, 배정 지점도 같다.

### 성공/실패가 이미 갈려 있다

`ResolveReservation`의 기존 코드를 다시 읽으면 **성공 경로는 한 줄도 안 고쳐도 된다**:

```csharp
ClipAlignment reactionClip = playerSucceeded
    ? r.template?.EnemyHit                                   // ← 적 대미지 모션. 이미 있다.
    : (retreat <= 0f ? r.template?.EnemyParry : null);
```

`EnemyView.Resolve`가 그 리액션을 `impactTime - ReactionLead`에 예약하므로,
**적의 공격 클립은 자기 임팩트에 닿기 직전에 피격 모션으로 끊긴다.** 정확히 "플레이어가 더 빨랐다"의 그림이다.
`killOnSuccess`면 `KillOpponent`가 `EnemyDeath`로 끊는다 — 이것도 기존 경로다.

### 그래서 손댈 곳은 실패 경로 하나다

실패에서 적은 **아무것도 안 해야 한다** — 자기 공격 클립을 끝까지 재생하면 그게 곧 플레이어가 맞는 그림이다.
그런데 `EnemyView.Resolve`(:845)는 실패에서 **반드시** 뭔가로 크로스페이드한다:

```csharp
if (!playerSucceeded) CrossFadeReaction(parried ? parryStateName : evadeStateName);
```

⚠ `Deadline`(= Resolve 시각)과 임팩트는 `ImpactOffset`밖에 안 떨어져 있어,
**적의 칼이 닿기 직전 프레임에 회피 모션으로 튄다.** 이 크로스페이드를 건너뛰는 것이 유일한 뷰 수정이다.

### ⚠ `Phase.Windup`을 빠져나와야 한다

`ApplyLocomotion`(:348)이 `Phase.Windup`에서 스스로 물러나므로, `Resolve`를 아예 안 부르면
적은 **Windup에 영구히 갇힌다**(빠져나오는 경로가 `Resolve`/`MarkDying`뿐이다).
그렇다고 즉시 `Phase.Recover`로 내리면 로코모션이 따라베기 잔여를 덮는다.

기존 노브가 이미 그 자리에 있다 — `reactionUntil`(:1043 등). 그 값을 걸면
`ApplyLocomotion`·상체 마스크가 그 시간만큼 물러난다. **새 상태 필드가 0개다.**

### 이펙트 조건

`OnEnemyReacted`는 실패에서 `parried ? Parry : Evade`를 낸다. 상호 공격 실패는 넉백이 0이라
그대로 두면 `Parry`가 나오는데, 적은 막은 게 아니라 **베었다**. `Evade`로 보낸다 —
`Attacker.Player` 패턴에서 `Evade`의 뜻이 이미 "플레이어가 맞았다"이므로(§7-4) 저작 규칙이 안 늘어난다.
