# Research — 실패해도 칼은 나간다 (FailSwing)

## 1. 증상

`Attacker.Player` 패턴에서 노드를 하나라도 놓치면 **베기 애니메이션이 아예 안 나온다.**
빠른 미스일수록 확실히 안 나오고, 마지막 노드 근처에서 놓치면 나오기도 한다 — 그 비대칭이 원인을 그대로 가리킨다.

## 2. 원인 — 예약을 취소하는 두 줄

`CharacterActionPlayer`는 **패턴 완료가 아니라 판정 대상 승계 시점에** 베기를 예약한다
(`OnJudgeTargetBegan` → `SchedulePendingSuccess`). ⚠ CLAUDE.md §6의 *"`OnPatternComplete` 구독"*은 낡은 서술이다 —
이 클래스는 그 이벤트를 구독하지 않는다.

```
pendingScheduleStart = max(impact − freeze − authored, FirstNodeTime)
impact = Deadline + ImpactOffset = LastNodeTime + goodWindow + ImpactOffset
```

와인드업(`authored`) 실측 p50이 0.26초, `goodWindow`가 0.1초이므로 **재생 시작은 대개 마지막 노드보다 ~0.16초 이르다.**
즉 **칼은 패턴이 끝나기 전에 이미 나가기 시작한다.**

첫 미스가 나면 `HandleJudgeTargetFirstMiss`가 그 예약을 버린다:

| 위치 | 코드 | 효과 |
|---|---|---|
| `HandleJudgeTargetFirstMiss` | `hasPending = false` | 예약 파기 |
| `TryStartPendingSuccess` | `if (!hasPending \|\| missedThisTarget \|\| ...) return;` | 예약이 살아 있어도 두 번째 가드가 막는다 |

그래서 **미스가 `pendingScheduleStart`보다 이르면 클립이 통째로 안 나오고, 늦으면 이미 시작한 클립이 그대로 이어진다.**
증상의 비대칭이 정확히 이것이다.

해당 메서드의 주석이 그 의도를 적어 두고 있다 — *"플레이어가 공격자였다면 피격 자체가 없다 … 헛스윙으로 끝난다."*
**그런데 지금 화면에는 헛스윙조차 없다.** 팔을 안 휘두르고 그냥 서 있다.

`missedThisTarget`은 **그 가드 한 곳에서만** 읽힌다(필드 선언·리셋·세팅을 빼면 사용처가 없다).

## 3. 적 쪽 — 이미 막을 줄 안다

`EnemyDirector.ResolveRetreatDistance` → `EnemyView.Resolve`가 실패 반응을 고른다.

```
parried = !playerSucceeded && attacker != Attacker.Enemy && retreat <= 0
```

- `retreat <= 0` → **패링**(`Pattern.EnemyParry`, 없으면 `parryStateName`). 임팩트 시각에 정렬해 예약된다.
- `retreat > 0` → **회피**(뒷구르기 + 후퇴 이동).

`Attacker.Player` 실패에서 둘 중 무엇이 나올지는 **다음 패턴의 창**이 정한다
(`arriveTime − now >= retreatDuration + travel + retreatWindowMargin`이면 회피).

⚠ 그 분기의 근거가 지금 **성립하지 않는다** — 원래 전제는 *"플레이어가 헛스윙한다"*였는데
화면에는 스윙 자체가 없으므로, 적이 뒤로 구르면 **아무도 안 휘둘렀는데 혼자 피하는 그림**이 된다.
그리고 §2-1(연타)과 §11-10(상호 공격)은 **이미 이 창 판단을 건너뛰고 `return 0f`** 한다 —
그 주석이 이유를 적어 두고 있다: *"같은 사건이 다음 패턴의 간격에 따라 어떤 때는 후퇴, 어떤 때는 패링으로 보인다."*

즉 **패링 경로는 이미 있고, 이 경우에 그것을 고르게만 하면 된다. 새 클립도 새 스테이트도 필요 없다.**

## 4. 취소를 유지해야 하는 경우 — 둘

`HandleJudgeTargetFirstMiss`의 나머지 분기는 그대로 옳다.

- **`Attacker.Enemy`**: 플레이어 클립은 `PlayerParry`(막는 동작)다. 못 막았으니 그 동작이 나오면 안 되고, `impactTime`에 피격이 예약된다.
- **상호 공격**(`Pattern.CountersOnFail`, §11-10): 적 칼이 실제로 닿는다. 같은 이유로 피격 예약.

두 경우의 조건이 **지금 코드의 분기와 정확히 같다**(`currentAttacker != Attacker.Enemy && !currentCountersOnFail`) —
그래서 "스윙을 살릴 조건"은 새로 쓸 필요 없이 그 식을 뒤집으면 된다.

## 5. 타이밍이 안 깨지는 근거

- 미스가 나도 **`Deadline`은 안 움직인다**(`SetPattern` 시점에 고정). 오답은 패턴을 정체시키고 만료로 끝나며,
  정답 노드 위의 타이밍 Miss는 진행만 한다. **어느 쪽이든 `impact = Deadline + ImpactOffset`이 그대로다** →
  §6의 임팩트 정렬이 실패 경로에서도 그대로 성립한다.
- 패턴이 (실패인 채로) 끝까지 입력돼 완료되면 다음 패턴의 `OnJudgeTargetBegan`이 `hasPending`을 덮는다.
  하지만 그때는 **이 패턴의 클립이 이미 시작된 뒤**다(위의 −0.16초) — 재생 중인 클립은 예약 필드와 무관하므로 끊기지 않는다.
- 절단·처치는 `playerSucceeded`만 보므로 **실패에서 적이 갈라질 일이 없다**(`EnemyDirector`는 한 줄도 안 고친다).
- 히트스톱은 성공에서만 예약된다(§7-3) — 막힌 스윙에는 정지가 안 걸린다. 그대로 둔다.

## 6. 곁가지 — `RaiseSwingEnded`

지금 그 분기는 `RaiseSwingEnded()`를 부르고 끝난다(*"진행 중이던 스윙만 끊는다"*).
스윙을 살리면 그 호출도 빠져야 한다 — 안 그러면 `swingActive`가 false가 되어
**진행 중인 히트스톱 가드가 풀리고**(§2-1) 칼날 트레일도 중간에 꺼진다.

## 7. 영향 범위

| 파일 | 변경 |
|---|---|
| `Character/CharacterActionPlayer.cs` | 첫 미스 분기 뒤집기 + `missedThisTarget` 가드 제거 |
| `Enemy/EnemyDirector.cs` | `Attacker.Player` 실패를 창 판단 없이 패링(0)으로 |

`EnemyView` · `PatternHandler` · `HitStopDirector` · `PatternEffectDirector`는 **한 줄도 안 고친다.**
