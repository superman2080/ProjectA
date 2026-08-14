# Research — 결투 거리 커브 (DuelDistanceCurve)

목표: 지금 패턴 하나당 **상수 하나**인 결투 간격을, 패턴 진행 중 **시간에 따라 변하는 값**으로 바꾼다.
간격이 음수가 되면 플레이어가 적을 지나쳐 뒤로 나가며 베는 연출이 나온다.
저작은 `Tools/Animation Clip Trimmer`에서, 그 창의 프리뷰가 실제 거리를 그대로 보여 준다.

---

## 1. 지금 거리가 정해지는 경로

| 단계 | 위치 | 하는 일 |
|---|---|---|
| 기준 거리 | `EnemyDirector.cs:29` `duelAnchor` | 씬의 `ImpactAnchor`(플레이어 자식). 카메라 구도가 정한다 |
| 기준 거리 산출 | `EnemyDirector.DuelDistanceOf` (`EnemyDirector.cs:1131`) | `앵커–부모 평면거리 + Pattern.DuelDistanceOffset`, `Mathf.Max(…, 0.1f)` |
| 패턴 보정 | `Pattern.cs:71` `duelDistanceOffset` / `Pattern.cs:143` `DuelDistanceOffset` | 이 모션의 리치 ±m |
| 표적 선택 | `EnemyDirector.TakeTargetForWindow` (`:1076`) | `desired = cruiseSpeed × 창 / playerShare + duelDistance` |
| 배치 계산 | `EnemyDirector.BuildDuelPlan` (`:1156`) | `playerTarget = meet − dir × (distance × playerShare)`, `enemyPos = playerTarget + dir × distance` |
| 발행 | `BindReservation` (`:1319~1343`) | `duelDistance`를 한 번 구해 계획을 만들고 `OnDuelScheduled` 발행 |
| 플레이어 이동 | `PlayerCombatMover.HandleDuelScheduled` (`:70`) | `ScheduleMove(plan.PlayerPosition, plan.PlayerArriveTime − now)` → `Update`의 `SmoothStep` Lerp (`:210~215`) |

**핵심 사실: 거리는 `BindReservation`에서 한 번 계산되고 그 패턴 동안 변하지 않는다.**
`DuelPlan`(`EnemyDirector.cs:213~238`)은 `PlayerPosition` / `EnemyPosition` / `ArriveTime` / `PlayerArriveTime`
네 값만 들고, 전부 스칼라·점이라 시간 함수가 들어갈 자리가 없다.

`PlayerCombatMover`는 도착(`m >= 1f`)하면 `moving = false`가 되어 **그 뒤로는 플레이어 위치를 아무도 안 건드린다**.
즉 지금은 "패턴 사이에만 이동, 패턴 안에서는 정지"가 코드 구조 그 자체다.

## 2. 왜 지금까지 정지였나 — 앵커 제약

`PlayerCombatMover` 주석(`:11~15`)이 근거를 든다: 결투 앵커는 플레이어의 자식이고
`EnemyDirector.PlayerPosition => duelAnchor.position`(`:435`), 적의 시선 타깃도 `duelAnchor`(`:767`, `:826`)다.
스윙 도중 플레이어가 움직이면 **적이 겨냥하던 지점이 늦게 바뀌어 칼이 어긋난다.**

이 제약이 새 설계에서 깨지지 않는 이유:
- **적을 고정하고 플레이어만 움직인다.** 적 자리는 `BuildDuelPlan`이 한 번 잡고 그대로 둔다.
- 간격은 `enemyPos − player(t)`이므로 **커브가 곧 실제 간격**이다(파생이 아니라 정의).
- `Attacker.Player` 패턴에서 베는 쪽은 플레이어이고, 플레이어 칼의 도달 지점은 자기 클립이 든다 —
  간격만 커브대로면 임팩트 프레임에서 칼이 닿는 거리가 저작값과 일치한다.
- `Attacker.Enemy`는 반대다. 적 칼은 예약 시점(`AssignEngageClip`)에 그 자리를 겨냥하고, 시선만
  앵커를 따라간다 → **임팩트 순간 간격이 저작값과 다르면 칼이 빗나간다.** 사용자 결정: 허용하되 저작자 책임
  (툴 프리뷰가 그 어긋남을 그대로 보여 준다).

## 3. 지금 값이 음수가 될 수 없는 지점

- `DuelDistanceOf`의 `Mathf.Max(baseDistance + offset, 0.1f)` — 커브 경로에서는 이 클램프를 타면 안 된다.
- `TakeTargetForWindow`의 `desired`는 `Mathf.Clamp(desired, minTargetDistance, stageRadius × 2)`로 따로 묶여 있어
  음수 간격이 표적 선택을 망가뜨리지는 않는다(도착 시각의 간격만 쓰면 충분).

## 4. 툴 현황

### 4-1. `Tools/Animation Clip Trimmer` (`AnimationClipTrimmerWindow.cs`, 975줄)
- 시간축이 **임팩트 기준 상대시간 `t`**(`t = 0`이 임팩트). 클래스 주석 `:13~16`.
- 거리: `duelBaseDistance`(수동 입력, 기본 1) + `duelDistanceOffset`(패턴 저장값) → `DuelDistance`(`:137`).
  **씬 앵커를 안 읽는다** — 사람이 손으로 같은 값을 넣어야 하고, 안 넣으면 프리뷰와 게임이 조용히 어긋난다
  (`:242`의 툴팁이 "가이드 4단계에서 잡은 값을 넣는다"라고 말하는 것이 그 증거).
- 배치: `PlaceActors`(`:368~374`) — 플레이어 원점, 적 `Vector3.forward * DuelDistance`, `t`와 무관한 상수.
- 카메라 프레이밍: `CombinedBounds`(`:483`)가 `DuelDistance × 0.5`를 중심으로 쓴다.
- 저장: `ApplyToPattern`(`:889~`)이 `SerializedProperty("duelDistanceOffset")`에 쓴다.
  동기 표시는 `DrawSyncState`(`:860~`)가 같은 프로퍼티를 비교한다.
- **커브를 얹기에 이 창이 맞는 이유**: 시간축이 이미 임팩트 기준이고, 스크럽·재생·두 배우 프리뷰가 이미 있다.
  키 시간 단위를 그대로 쓸 수 있어 변환이 생기지 않는다.

### 4-2. `Tools/Pattern Effect Tool` (`PatternEffectWindow.cs`)
- `duelDistance = 1f` 하드코딩 필드(`:64`, `:548`), 배치 `:734`, 바운드 `:813`.
- 이번 범위 밖(사용자 결정). 나중에 같은 헬퍼를 부르면 된다.

## 5. 충돌 지점 — 플레이어 위치를 건드리는 다른 주인

`PlayerCombatMover.Update`(`:194~216`)의 우선순위는 `rolling > turning/moving`이다.
- `RollArc`(`:148`)는 회피 성공이 부르며 `moving = false`로 이동을 끊는다.
- `HandleDuelScheduled`(`:73`)는 반대로 `rolling = false`로 구르기를 끊는다(도착 시각이 더 중요).
- `DodgeDirector`는 `CharacterActionPlayer.OnIdleWindow`(도착 뒤 ~ 다음 클립 시작 전)에만 기습을 건다.
  커브가 마지막 키 뒤에서 값을 홀드하므로 그 구간에는 이동이 없다 → 기존 공백 판정과 싸우지 않는다.
  단 구르는 중에는 커브 구동이 위치를 덮으면 안 된다 → `rolling`이 여전히 최우선이어야 한다.

## 5-2. 뒤로 빠지는 경우 — 지금은 분기가 없다

목표 간격이 지금 서 있는 간격보다 넓으면 플레이어는 **뒤로** 가야 한다. 현재 코드 어디에도 그 분기가 없다.

- `BuildDuelPlan`(`:1156`): `playerTarget = enemyDest − dir × distance`. 부호를 안 본다 —
  현재 간격이 좁으면 이 점이 플레이어 뒤에 놓인다.
- `PlayerCombatMover.HandleDuelScheduled`(`:70`): `ScheduleTurn`은 **언제나 적을 보게** 건다
  (`facing = EnemyPosition − target`) → 적을 마주본 채 뒷걸음질.
- `CharacterActionPlayer.HandleDuelScheduled`(`:455`): `distance`가 `magnitude`(부호 없음)라
  Sprint/Quickshift **전진 클립**이 걸린다 → 문워크. `distance < convergeMinDistance`(0.15m)면
  로코모션 자체를 안 걸어(`:462`) 조용히 미끄러진다 — 뒤로 빠지는 양이 작아 실제로는 이 경로가 많다.
- 언제 생기나: 사슬·재접근(`TakeTargetForWindow`를 안 거쳐 거리가 창에 안 맞춰짐),
  다음 패턴의 `duelDistanceOffset`이 현재 간격보다 큼. 새 표적 선택 경로는 `desired`가 커서 잘 안 생긴다.

거리 커브가 들어오면 **의도적으로** 지나쳤다가 되돌아오는 구간이 생겨 이 경로가 상시화된다.
쓸 수 있는 클립은 이미 있다: `Assets/05. Animations/Clip/Move/Quickshift_B.anim`
(전진은 `Quickshift_F.anim`이 `Quickshift` 스테이트에 물려 있다).
`CharacterActionPlayer`는 이미 `AnimatorOverrideController`를 들고 있으므로(`:297`)
**새 스테이트 없이 클립만 교체**하면 된다.

## 6. 시간 기준

`BindReservation`이 아는 시각은 `r.impactTime`(= `Deadline + Pattern.ImpactOffset`)와
`arriveTime`(= 클립 시작, `attack.ResolveScheduleStart`)이다.
툴의 `t`도 임팩트 기준이므로 **키 시간 = `worldTime − impactTime`**으로 변환이 한 줄이고,
`Tools/Animation Clip Trimmer`·`Pattern Effect Tool`·히트스톱 저작이 쓰는 규약과 같다.

`AnimationCurve.Evaluate`는 첫 키 이전/마지막 키 이후에 **끝 키의 값을 그대로 돌려준다**
(기본 WrapMode = Clamp). 사용자가 고른 "앞뒤 끝값 홀드"가 추가 코드 없이 성립한다.

**커브가 실제로 구동되는 구간 = 플레이어 클립 재생 구간이다.** 도착 시각(`arriveTime`) =
`ClipAlignment.ResolveScheduleStart` = `임팩트 − 트림길이/배속`이므로, 구동 구간 길이는 **클립 트림 길이**로 정해진다
— 트리머 타임라인에 이미 그려져 있는 바로 그 길이다. 채보의 창 길이(0.5~2.1초)와는 무관하다.

단 **재생 배속은 상수가 아니다**:
- `AttackSpeed` 압축(`maxAttackSpeed` 2.5) — 다음 패턴이 촉박하면 클립 전체가 빨라진다.
- 히트스톱(§7-3) — `AttackSpeed = 0`으로 헤드가 얼지만 `Time.time`은 계속 흐른다.
- 다중 히트스톱 예산(§7-3-1) — 재생을 정지 예산만큼 **일찍 시작**하고 해제마다 배속을 재계산한다.

따라서 커브 시각을 `Time.time − impactTime`으로 잡으면 세 경우 전부 어긋난다.
`CharacterActionPlayer`가 이미 이 셋을 전부 들고 있으므로 **재생 헤드에서 파생한 값 하나**를 내보내는 것이 옳다.

## 7. 제약 요약

1. 커브가 비면 기존 경로 그대로 — 기존 패턴 에셋 전부 회귀 없음.
2. 적은 안 움직인다. 간격 = `enemyPos − player(t)`.
3. 플레이어 회전은 안 건드린다(음수 간격 = 등 돌린 채 지나침).
4. 커브 구동은 **도착 이후**에만 돈다. 도착 목표 간격 = `curve.Evaluate(arriveTime − impactTime)`이라 이음매가 연속.
5. `rolling`이 커브 구동보다 우선.
6. `Attacker.Enemy` 패턴은 허용하되 칼 정렬은 저작자 책임.
