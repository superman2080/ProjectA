# Research — 패턴 실패 후 재접근이 "미끄러짐"으로 보이는 문제

## 현상

패턴을 실패하면 적이 뒤로 물러나고(`failRetreatDistance`) 플레이어가 그 앞으로 다시 다가간다.
이때 **로코모션 애니메이션이 안 보이고, 플레이어가 선 자세 그대로 천천히 앞으로 미끄러진다.**

전제(사용자 확인): **플레이어가 공격자(`Attacker.Player`)인 패턴은 실패해도 Hit 클립을 재생하지 않는다.**
따라서 이 문제는 "Hit 클립이 길어서"가 아니다 — Hit이 아예 없는 경로에서도 똑같이 재현된다.

---

## 관련 파일

| 파일 | 역할 |
|---|---|
| `Enemy/EnemyDirector.cs` | `DuelPlan` 생성·발행(`OnDuelScheduled`), `cruiseSpeed`·`playerShare`·`failRetreatDistance` 소유 |
| `Character/PlayerCombatMover.cs` | `DuelPlan.PlayerPosition`까지 **위치**를 옮긴다 |
| `Character/CharacterActionPlayer.cs` | 같은 `DuelPlan`을 받아 **수렴 로코모션**(Sprint/Quickshift)을 건다 + Attack Layer 웨이트 관리 |
| `Enemy/EnemyView.cs` | 적 후퇴(`Resolve` → `ScheduleMoveAfter`) |

## 데이터 흐름

```
패턴 실패
  └ PatternHandler.OnPatternComplete
      └ EnemyDirector.ResolveReservation
          ├ opponent.Resolve(...)          → 적이 failRetreatDistance 만큼 후퇴
          └ BindNextReservation → BindReservation
              ├ arriveTime = attack.ResolveScheduleStart(impactTime, now)   // = 적 클립 시작 시각
              ├ BuildDuelPlan(opponent, template, arriveTime)               // opponent.Destination(= 후퇴 도착점) 기준
              └ OnDuelScheduled(plan)
                   ├ PlayerCombatMover.HandleDuelScheduled  → ScheduleMove(target, ArriveTime - now)
                   └ CharacterActionPlayer.HandleDuelScheduled → Sprint 또는 Quickshift
```

`DuelPlan`은 `PlayerPosition` / `EnemyPosition` / `ArriveTime` 셋뿐이다.
**"언제 출발하는가"라는 개념이 없다** — 구독자 둘 다 "지금부터 `ArriveTime`까지"를 창으로 쓴다.

---

## 원인 1 — 수렴 모션이 Attack Layer에 가려진다

`CharacterActionPlayer.Update`의 복귀 경로:

| 라인 | 구간 | 웨이트 |
|---|---|---|
| `Time.time < actionEndTime` | 공격/Hit 트림 재생 | `ApplyBlendIn()` (1) |
| `Time.time < recoveryEndTime` | 마무리 동작 노출 `recoveryHoldDuration` 0.25초 | `ApplyBlendIn()` (1) |
| `IsLinkedToNextAction()` | 연계 | 여기 **안에만** `else if (Time.time < convergeUntil) ApplyBlendOut();` 양보가 있다 |
| 연계 X | `TriggerRelease()` → `releaseDuration` 0.9초 | `ApplyBlendIn()` (1) |

실패는 `HandleJudgeTargetFirstMiss`에서 `hasPending = false`로 예약을 취소하므로
`IsLinkedToNextAction()`이 **거짓**이다 → 양보가 있는 유일한 분기를 못 탄다.

결과: 트림 끝 이후 `0.25 + 0.9 = 1.15초` 동안 Attack Layer 웨이트가 1로 유지된다.
base `Running Layer`에 건 Sprint/Quickshift는 그 아래에 깔려 **화면에 한 프레임도 안 나온다.**

`SwitchBaseStateUnlessConverging`(`convergeUntil` 양보)은 **base 스테이트 선택**만 막아 준다.
**레이어 웨이트**는 못 막는다 — 그래서 "스테이트는 올바른데 안 보인다"가 된다.

> 참고: `Attacker.Enemy` 패턴이면 여기에 Hit 클립 길이까지 더해져 더 길어진다.
> 원인은 같고, 길이만 다르다.

### Hit 게이트 실사 결과 (코드 잔재 없음)

"플레이어 공격 패턴은 실패해도 Hit을 재생하지 않는다"는 **코드에 정확히 한 곳으로 구현돼 있다.**

```
CharacterActionPlayer.HandleJudgeTargetFirstMiss
    if (currentAttacker != Attacker.Enemy) { RaiseSwingEnded(); return; }
```

`OnPlayerHit`은 `TryStartPendingHit`에서만 발행되므로, 그 이벤트를 구독하는
`CameraDirector`(`PatternMiss` 쉐이크)와 `PlayerHealth`(체력 감소)도 **같은 게이트를 자동으로 탄다.**
우회 경로 없음.

파생 결과 — `Attacker.Player` 패턴 실패 시:

- 카메라 `PatternMiss` 큐가 안 난다(`PatternFailure`만 Deadline에 난다)
- **체력이 안 깎인다**

둘 다 "안 맞았다"의 자연스러운 귀결이라 의도로 본다.

남은 잔재는 전부 주석·문서다(Plan Step 0):
`CharacterActionPlayer` 클래스 요약 / `hitClips` Tooltip / `CameraCueCatalog.PatternMiss` 주석 /
`CameraDirector` 클래스 주석 / `CLAUDE.md` §6·§7-1.

## 원인 2 — 실제 이동 속도가 느리다

**실패 경로는 표적 선택을 안 거친다.** `BindReservation`은 `currentOpponent == null`일 때만
`TakeTargetForWindow(창, duelDistance)`를 부른다. 실패하면 같은 적이 살아 있으므로 그대로 이어 싸운다.

즉 §11-2의 `목표거리 = cruiseSpeed × 창 / playerShare + duelDistance` 규칙이 **안 걸린다.**
거리를 정하는 것은 `failRetreatDistance`(1.5m) 하나다.

- 후퇴 뒤 둘 사이 간격 ≈ `duelDistance + failRetreatDistance`
- `BuildDuelPlan`: 플레이어 몫 = `간격 × playerShare − duelDistance × playerShare` ≈ **1.3m**
- 창은 음악이 정한다(실측 0.5~2.1초, 평균 1.48초)

→ 실제 속도 ≈ **1.3m / 1.4s ≈ 0.9 m/s**. `cruiseSpeed`(4.5)의 1/5이다.

`HandleDuelScheduled`의 로코모션 선택은 **창으로 갈린다**:

- 창(1.4초) > `quickshiftClip.length`(1초) → **Sprint**
- Sprint 배속 = `(거리/창) / sprintReferenceSpeed` = `0.9 / 4.5` ≈ **0.2배**

0.2배속 Sprint = 슬로모션 달리기. 원인 1로 안 보이지만, **보이게 만들어도 이 자체가 어색하다.**
두 원인은 독립적이라 **둘 다 고쳐야 한다.**

---

## 원인 3 — 이동 데드라인 자체가 틀렸다 (`Attacker.Player` 한정)

`EnemyDirector.cs:607`:

```
attack = Template.Attacker == Attacker.Enemy ? Template.EnemyAttack : null
```

`Attacker.Player` 패턴은 적 클립이 없다 → `BindReservation`에서 `arriveTime = r.impactTime`.
**실패 후 재접근은 거의 항상 이 경우다.**

한편 플레이어 자기 스윙은 `pendingScheduleStart = max(impactAlignTime − impactSpan/speed, FirstNodeTime)`.
임팩트가 트림 끝 근처라 스팬이 커서 **임팩트보다 한참 앞**이다.

```
        스윙 시작                                  임팩트 = ArriveTime
   ────────┬────────────────────────────────────────────┬────
           └──────────── 이동이 여기까지 계속된다 ──────┘
```

즉 **플레이어는 자기 칼을 휘두르는 내내 미끄러진다.** 원인 1로 다리가 안 보이니 더 그렇게 읽힌다.
진짜 데드라인은 `min(ArriveTime, 스윙 시작)`이어야 한다 — 도착해서 서고 나서 베야 한다.

⚠ 이걸 고치려면 순서 문제가 걸린다: `PatternHandler.cs:657→668`에서
`OnPatternComplete`(→ `OnDuelScheduled`)가 `OnJudgeTargetBegan`보다 **먼저** 발행되므로,
계획이 도착하는 시점에 `pendingScheduleStart`는 아직 이전 패턴 값이다.

**→ 이동을 없애면(제자리 패링) 원인 3도 함께 사라진다.** 그래서 채택된 Plan은 이 경로를 안 건드린다.

## 제약 (고칠 때 어기면 안 되는 것들)

1. **`ArriveTime`까지 반드시 도착해야 한다.** 결투 앵커(`duelAnchor`)가 플레이어의 자식이라,
   적 클립이 시작된 뒤 플레이어가 움직이면 적이 겨냥하던 지점이 뒤늦게 바뀌어 칼이 빗나간다.
   → **출발을 늦출 수는 있어도 도착을 늦출 수는 없다.**
2. **앞당기는 건 언제나 안전하다** — `EnemyView.EarliestArrival`이 적 쪽에서 이미 같은 판단을 했다
   (§11-2: 도착 시각까지 끌지 않고 `moveSpeed`로 빨리 가서 선다). 플레이어 쪽만 아직 창을 다 쓴다.
3. **`cruiseSpeed`의 소유자는 `EnemyDirector` 하나다.** 두 구독자가 각자 같은 식을 들면
   §11-2가 경고한 "모델이 둘" 상태로 되돌아간다.
4. **`convergeUntil`의 규율은 유지한다** — 수렴 구간에는 복귀 로직이 base를 되찾아가지 않는다.
5. **Release는 없애면 안 된다.** 곡 공백에서 마무리 동작이 나오는 유일한 경로다(§6).
   수렴 때문에 무조건 생략하면 공백 구간의 복귀가 통째로 사라진다.
