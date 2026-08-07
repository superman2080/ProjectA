# Plan — 실패 리액션을 "제자리 패링"으로 바꿔 재접근 자체를 없앤다

근거: `docs/FailConverge/Research_FailConverge.md`

## 방향

원인 1(Attack Layer가 수렴 모션을 덮는다)과 원인 2(이동이 0.9 m/s로 기어간다)는 둘 다
**"적이 1.5m 물러났으니 플레이어가 다시 다가가야 한다"**는 전제 위에 서 있다.

전제를 없앤다. **실패하면 적이 제자리에서 패링한다.**

- 다음 계획의 플레이어 이동 거리 ≈ 0 → `CharacterActionPlayer.convergeMinDistance`(0.15m) 가드에 걸려
  **수렴 로코모션을 아예 안 건다** → 원인 2 소멸
- 가려야 할 이동이 없으므로 Attack Layer 웨이트 1이 무해해진다(오히려 Release가 나오는 게 맞는 그림)
  → 원인 1 소멸
- `EnemyView.ScheduleMove`가 이미 `wantsLocomotion = (to−from)² > minMoveDistance²`로 0거리를 거른다
  → **제자리 Run도 안 난다**(추가 가드 불필요)

연출도 이쪽이 맞다. 지금은 "베려다 실패 → 적이 뒷걸음 → 다시 걸어감"이라 리듬이 끊긴다.
패링이면 "베려다 막힘 → 그 자리에서 바로 다음 소절"이라 **무쌍의 흐름이 안 끊긴다.**

### 폐기 — `DuelPlan.DepartTime` 안

이전 Plan은 `DepartTime = ArriveTime − 거리/cruiseSpeed`로 출발을 늦춰 `cruiseSpeed`를 지키려 했다.
검토 중 **더 깊은 문제**가 드러났다(Research 참고):

- `Attacker.Player` 패턴은 적 클립이 없어(`EnemyDirector.cs:607`) `arriveTime = impactTime`이다
- 반면 플레이어 자기 스윙은 `impactAlignTime − impactSpan/speed`에 시작한다 → **훨씬 이르다**
- 즉 출발을 늦추면 대시가 **자기 스윙 한복판**으로 들어간다. 칼 휘두르며 미끄러지는 그림
- 고치려면 이동 데드라인을 `min(ArriveTime, 스윙 시작)`으로 좁혀야 하는데,
  `OnDuelScheduled`가 `OnJudgeTargetBegan`보다 **먼저** 나므로(`PatternHandler.cs:657→668`)
  계획 시점에는 스윙 시작을 모른다 → 이벤트 재배치 또는 컴포넌트 간 새 이벤트가 필요

**이동을 없애면 이 전부가 필요 없다.** 그래서 폐기한다.
(이동이 남는 다른 경로 — 성공 후 다음 적으로 가는 주 경로 — 는 `TakeTargetForWindow`가
`cruiseSpeed × 창`으로 거리를 잡으므로 애초에 이 문제가 없다.)

---

## Step 0 — "실패 = 피격" 잔재 정리 (문서만, 코드 변경 없음)

**코드에는 잔재가 없다.** 게이트는 `CharacterActionPlayer.HandleJudgeTargetFirstMiss`의
`if (currentAttacker != Attacker.Enemy)` 한 줄뿐이고, `OnPlayerHit`이 `TryStartPendingHit`에서만
발행되므로 `CameraDirector`(PatternMiss)·`PlayerHealth`(체력 감소)가 **자동으로 같은 게이트를 탄다.**
확인된 파생 결과(의도된 것으로 본다): `Attacker.Player` 패턴 실패는 **카메라 피격 큐도, 체력 감소도 없다.**

- [x] `CharacterActionPlayer.cs` 클래스 요약: "미스(첫 미스 1회): **그 순간** 힛 클립을 재생" →
      **`Attacker.Enemy`일 때만, 그리고 `impactTime`에 예약해서** 재생한다로 고친다(두 군데가 동시에 낡았다).
- [x] `CharacterActionPlayer.cs` `hitClips` Tooltip: "패턴 실패 시" → "**적이 공격자인** 패턴을 실패했을 때".
- [x] `CameraCueCatalog.cs`의 `PatternMiss` 주석: "첫 미스 순간 — 피격 애니메이션과 동기" →
      "적 칼이 닿는 순간(`OnPlayerHit`). **플레이어가 공격자인 패턴에서는 발행되지 않는다.**"
- [x] `CameraDirector.cs` 클래스 주석의 "피격은 `OnJudgeTargetFirstMiss` 순간에 즉시" → `OnPlayerHit`(임팩트) 기준으로.
- [x] `CLAUDE.md` §6의 "완주 성공이면 베기 클립, **실패면 공용 피격(Hit) 클립**" →
      실패의 피격은 `Attacker.Enemy`에서만이고, 플레이어 공격 패턴의 실패는 **헛스윙으로 끝난다**를 명시.
- [x] `CLAUDE.md` §7-1의 "피격은 `OnJudgeTargetFirstMiss` 순간 **즉시**" → 같은 정정.

## Step 1 — 실패 리액션을 역할로 가른다

지금 `EnemyView.Resolve`(:386)는 **공격자와 무관하게** 회피다:

```
if (!playerSucceeded) CrossFadeReaction(evadeStateName);
```

주석은 "화면에 남는 사실은 '베지 못했다' 하나뿐이라 반응을 나눌 이유가 없다"고 적혀 있지만,
**두 실패는 사실이 다르다** — 플레이어 공격 실패는 "적이 막았다", 적 공격 실패는 "플레이어가 맞았다"다.

- [x] `[SerializeField] private string parryStateName = "Parry";` 추가(`evadeStateName` 옆).
- [x] 분기를 바꾼다:

```
if (!playerSucceeded)
    CrossFadeReaction(attacker == Attacker.Enemy ? evadeStateName : parryStateName);
```

- [x] `Attacker.Enemy` 실패는 **건드리지 않는다**(적이 벤 뒤 물러나는 기존 그림 유지).
      이번 변경의 대상은 `Attacker.Player` 실패 하나뿐이다.
- [x] 메서드 주석의 "짧게 물러난다"를 역할별로 갈린다는 서술로 고친다.

## Step 2 — 패링에서는 후퇴하지 않는다

- [x] 같은 분기에서 후퇴 거리를 0으로 만든다. **`Resolve` 안에서 정한다** —
      디렉터가 `failRetreatDistance`를 통째로 0으로 두면 `Attacker.Enemy` 실패의 후퇴까지 같이 죽는다.

```
float distance = (playerSucceeded || attacker == Attacker.Enemy) ? retreatDistance : 0f;
```

- [x] `ScheduleMove`는 **그대로 호출한다**(거리만 0). `retreatUntil`이 다음 `AssignAttack`의
      `ScheduleMoveAfter` 인수인계 타이밍을 잡고 있으므로, 호출을 빼면 그 구조가 흔들린다.
      거리 0이면 `wantsLocomotion`이 false라 로코모션은 안 뜬다(이미 구현돼 있음).
- [x] `reactionHoldDuration`(0.6초)이 패링 클립보다 짧으면 로코모션이 패링을 덮는다 —
      클립 길이를 보고 필요하면 인스펙터에서 맞춘다(코드 변경 아님).

## Step 3 — 패링 클립·애니메이터 (에셋 작업)

- [x] 적 애니메이터에 `Parry` 스테이트 추가. `Evade`/`KnockBack`과 같은 레이어·같은 방식.
- [x] 패링 클립 배선. **임팩트 정렬은 필요 없다** — 패링은 예약 재생이 아니라
      실패 확정 순간의 리액션이라 `ClipAlignment` 슬롯을 안 쓴다(`Pattern`에 새 슬롯 추가 불필요).
- [x] 스테이트 이름이 `parryStateName` 기본값과 다르면 인스펙터에서 맞춘다.
      **배선이 틀리면 `CrossFade`가 조용히 무시돼 포즈가 굳는다** — 씬에서 한 번 확인한다.

## Step 5 — 창이 감당하면 회피, 아니면 패링 (조건부)

패링 고정은 짧은 구간에만 맞다. 창은 음악이 정해 **0.5~2.1초로 4배 흔들리므로**,
넉넉한 구간에서는 물러났다 달려오는 그림이 더 낫다(그 구간은 `cruiseSpeed`로 달려와 회피가 회피로 보인다).
**음악이 정한 창이 곧 연출을 고른다.**

판정 시점이 이미 존재한다 — `ResolveReservation`은 `reservations.Remove(r)` **뒤**에 `Resolve`를 부르므로
그 자리의 `reservations[0]`이 곧 다음 패턴이고, 거기서 `arriveTime`을 뽑을 수 있다.

- [x] `EnemyDirector.ResolveRetreatDistance(r, playerSucceeded)` 추가. 반환값이 곧 결정이다 —
      **0이면 패링, 양수면 회피.** 뷰는 창을 모르므로 판단을 디렉터가 든다.

```
needed = retreatDuration + (failRetreatDistance × playerShare / cruiseSpeed) + retreatWindowMargin
물러난다 ⟺ (arriveTime − now) >= needed
```

- [x] `[SerializeField] float retreatWindowMargin = 0.35f;` 추가(회피↔패링 문턱, 인스펙터 튜닝).
- [x] **다음 예약이 없으면(곡 공백) 물러나지 않는다** — 돌아올 사람이 없어 물러난 채로 남는다.
- [x] `Attacker.Enemy` 실패는 **언제나 물러난다**(회피가 아니라 벤 뒤의 여파이고, 플레이어는 피격 클립에 묶인다).
- [x] `EnemyView.Resolve`는 거리로 리액션을 고른다: `parried = !playerSucceeded && attacker != Enemy && distance <= 0`.

> `ponytail:` `arriveTime`은 **낙관적 데드라인**이다. 진짜 마감은 '플레이어 자기 스윙 시작'이라 더 이르지만
> (원인 3), 그 값은 `OnJudgeTargetBegan` 뒤에야 알 수 있어 이 시점에 못 본다.
> 그 간격을 `retreatWindowMargin`이 흡수한다 — 회피가 빠듯해 보이면 키운다.

## Step 4 — 문서 갱신

- [x] `CLAUDE.md` §11-2의 "**실패한 적은 짧게 물러난 그 자리에 선다**(`failRetreatDistance`)" →
      역할로 갈린다고 고친다: **플레이어 공격 실패 = 적이 제자리에서 패링**(이동 없음),
      **적 공격 실패 = 기존대로 짧게 후퇴**. 그리고 그 이유 —
      "물러나면 플레이어가 다시 다가가야 하는데, 그 재접근은 `TakeTargetForWindow`를 안 거쳐
      거리가 `failRetreatDistance`로만 정해진다 → 창에 늘어져 0.9 m/s로 기어간다."
- [x] `docs/StageTraversal/`의 후퇴 관련 서술에 같은 내용 반영.

---

## 검증

- [ ] 플레이어 공격 패턴을 일부러 실패 → **적이 제자리에서 패링하고 플레이어가 안 움직이는지.**
- [ ] `logConvergeDecision` 로그에 **"거리 부족"**이 찍히는지(= 수렴을 아예 안 걸었다는 증거).
      Sprint/Quickshift가 찍히면 아직 이동이 남아 있다는 뜻.
- [ ] 적 공격 패턴 실패(맞는 경우)에서 **기존 회피·후퇴가 그대로인지**(회귀 확인).
- [ ] 성공 → 다음 적 이동(주 경로 ≈95%)이 그대로인지.
- [ ] 패링 직후 다음 패턴의 적 클립/임팩트 정렬이 안 밀렸는지 — `reactionUntil`과
      `ScheduleMoveAfter` 인수인계가 거리 0에서도 같은 시각에 끝나는지 확인.
