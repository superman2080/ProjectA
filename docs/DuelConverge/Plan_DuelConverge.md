# Plan: 결투 수렴 — 플레이어와 적이 중간에서 만난다

> 관련: `docs/EnemyCombat/` · `docs/DuetPreview/Plan_DuetPreview.md` · `docs/!Guides/Guide_EnemyCombat.md`
> 피드백은 이 문서에 `>>>`로 남겨주세요. 확정 전까지 구현하지 않습니다.

---

## 0. 문제

지금은 **적만 달려온다.** 링 반경이 6m라 적이 그 거리를 통째로 이동하는 동안
플레이어는 제자리에서 0.6m 스텝만 밟는다(`PlayerCombatMover.stepForward`).

두 가지가 나쁘다:

1. **적의 이동 거리가 너무 길다.** 클립 시작 전에 도착해야 하는데(`pendingScheduleStart`),
   패턴이 짧으면 시간이 모자라 순간이동처럼 미끄러진다.
2. **플레이어가 무대 장치처럼 보인다.** 적이 찾아오고 플레이어는 서 있는다 — 무쌍의 그림이 아니다.

**둘이 마주 달려 중간에서 만나면** 각자의 이동 거리가 절반이 되고, 화면에도 교전이 성립한다.

---

## 1. 설계 결정

### 1-1. 만남 지점은 **두 위치의 중점**, 거리는 `duelDistance`로 벌린다

```
mid    = (playerPos + enemyPos) / 2
dir    = normalize(flat(enemyPos − playerPos))
half   = duelDistance / 2

playerTarget = mid − dir * half
enemyTarget  = mid + dir * half
```

- 대칭이라 **누가 얼마나 움직였는지가 화면에서 공평하게 읽힌다.**
- `duelDistance`는 기존 규약 그대로 **기준선 + `Pattern.DuelDistanceOffset`**
  (`DuetPreview` 결정 1-10). 모션 리치가 그대로 반영된다.

>>> (완료) 확정 (2026-08-01)

### 1-2. 도착 시각은 **클립 시작 전**(`pendingScheduleStart`)이다 — 기하 동결 규율 유지

`PlayerCombatMover` 클래스 주석의 규율을 그대로 지킨다:

> 스윙 도중 플레이어가 전진하면 앵커가 따라 움직여 적이 겨냥하던 지점이 늦게 바뀐다 → 칼이 어긋난다.
> **패턴 중 기하를 얼려야 정렬이 성립한다.**

수렴도 같다. **클립이 시작되기 전에 둘 다 도착해 있어야 한다.**

>>> (완료) 확정 (2026-08-01)

### 1-3. 계산 주체는 `EnemyDirector`, 플레이어는 **이벤트로 받는다**

두 위치를 동시에 아는 곳은 디렉터뿐이고, 이미 `AssignAttack`으로 적의 목표를 정하고 있다.
플레이어를 직접 움직이면 계층이 뒤집히므로 **이벤트로 알린다**:

```csharp
public readonly struct DuelPlan
{
    public readonly Vector3 playerPosition;   // 플레이어가 설 자리
    public readonly Vector3 enemyPosition;    // 적이 설 자리
    public readonly float   arriveTime;       // 둘 다 이 시각까지 도착
}
public event Action<DuelPlan> OnDuelScheduled;
```

`PlayerCombatMover`가 구독해 자기 몫만 움직인다. **디렉터는 플레이어를 모른다.**

>>> (완료) 확정 (2026-08-01)

### 1-4. 시간이 모자라면 **수렴을 생략하고 적만 붙인다**

`pendingScheduleStart`까지 여유가 `minConvergeTime` 미만이면:

- 플레이어는 **안 움직인다**(지금도 스텝을 생략하는 규율과 같다).
- 적은 **여전히 목표로 간다** — 안 그러면 칼이 닿지 않는다.
  이 경우 만남 지점은 중점이 아니라 **플레이어 앞 `duelDistance`** 로 폴백한다.

> 순간이동처럼 보이는 것보다 "플레이어가 못 움직인 채 적이 달려든다"가 낫다.
> 어느 쪽이든 **거리는 항상 `duelDistance`로 맞춘다** — 그게 깨지면 칼이 빗나간다.

>>> (완료) 확정 (2026-08-01)

### 1-5. 리시(중앙 이탈)는 **수렴 지점에 클램프**로 건다

중점으로 가면 플레이어가 매 교전마다 링 쪽으로 끌려간다(누적 표류).
`PlayerCombatMover.maxOffset`(1.5m) 규율을 유지하되, **스텝을 생략하는 대신 목표를 클램프**한다:

```
playerTarget = center + clamp(playerTarget − center, maxOffset)
```

생략하면 거리가 어긋나지만, 클램프하면 **플레이어가 덜 가고 적이 그만큼 더 온다**
(적 목표는 플레이어의 실제 목표에서 다시 계산한다 — 순서가 중요하다).

#### ⚠ 기준점은 **고정 원점**이어야 한다 (구현 중 발견)

`arenaCenter`가 씬에서 **플레이어 자신**을 가리키고 있어, `Center`가 플레이어와 함께 움직였다.
그러면 이 식이 "중앙에서의 이탈"이 아니라 **"한 걸음의 길이"** 를 재게 되어 표류를 전혀 못 막는다.
→ `EnemyDirector.Awake`에서 `arenaOrigin`을 한 번 잡아 **그것으로만** 잰다.

#### 교전 전환 시 중앙 복귀는 **제거했다** (사용자 요청, 2026-08-01)

원안은 "교전 사이 중앙 복귀로 표류를 리셋"이었으나, **벤 자리에서 다음 적을 향해 나아가는 것이
무쌍의 흐름**이고 매번 원점으로 끌려가면 왕복하는 그림이 된다.
위 클램프가 고정 원점 기준으로 제대로 동작하므로 **복귀 없이도 표류가 묶인다.**
`PlayerCombatMover`는 이제 상대가 바뀔 때 **선 자리에서 방향만 돌린다.**

>>> (완료) 확정 (2026-08-01)

### 1-6. `duelAnchor`는 **기준 거리의 출처**로만 남는다

지금은 "적이 설 자리"를 직접 지정하지만, 수렴에서는 자리가 매번 계산된다.
앵커는 **플레이어 자식**이므로 `|flat(anchor − player)|`가 곧 기준 결투 거리다 — 그 값만 읽는다.

- 씬에서 앵커를 앞뒤로 밀면 기준 거리가 바뀐다(기존 저작 방식 유지).
- 툴(`짝 에디터`·`슬라이서`)의 `기준 결투 거리`에 **같은 값**을 넣는다.
- 상대가 없을 때(디버그)는 앵커를 그대로 목표로 쓴다.

>>> 앵커에서 파생하지 말고 디렉터에 명시 필드를 두는 편이 나으면 알려주세요.

### 1-7. 적의 로코모션은 이미 붙어 있다
`ScheduleMove`가 거리를 보고 Run/Idle을 고르므로(이번 세션 작업) 추가 작업이 없다.
**플레이어는 base 레이어가 이미 Sprint 경로를 갖고 있다** — 수렴 구간이 그 노출 창과 겹치는지만 확인한다.

>>> (완료) 확정 (2026-08-01)

---

## 2. 비목표

- 회피/이동 중 충돌 처리. 둘은 서로를 통과해도 무방하다(도착 지점이 겹치지 않는다).
- 적이 여럿 동시에 달려드는 그림. **교전 상대는 언제나 하나**다(대기석 규율 유지).
- 카메라 추종 변경. 만남 지점이 중앙 근처로 수렴하므로 지금 화각이 그대로 성립한다고 본다 —
  실측 후 필요하면 별도 판단.

---

## 3. 단계

### Step 1 — `DuelPlan` 계산을 디렉터로 모은다
- [x] `EnemyDirector`에 `DuelPlan` 구조체 + `OnDuelScheduled` 이벤트 신설.
- [x] `DuelDistance(template)` — `|flat(duelAnchor − arenaCenter)|` + `template.DuelDistanceOffset`.
      (앵커가 플레이어 자식이므로 `arenaCenter`가 곧 플레이어 위치다)
- [x] `BuildDuelPlan(opponent, template, arriveTime)`:
      중점 → 방향 → 대칭 배치 → **플레이어 목표를 `maxOffset`으로 클램프** → 적 목표를 재계산(결정 1-5).
- [x] `HandlePatternQueued`에서 `AssignAttack` 직전에 계획을 만들고 이벤트를 발행한다.
      적에게는 `plan.enemyPosition`을 넘긴다.
- [x] 상대가 없거나 시간이 모자라면 **폴백 계획**(플레이어 제자리 + 적은 플레이어 앞 `duelDistance`).

### Step 2 — `PlayerCombatMover`를 수렴 소비자로 바꾼다
- [x] `OnDuelScheduled` 구독. `plan.playerPosition`으로 `plan.arriveTime`까지 이동.
- [x] **`stepForward` / `stepBackward` 제거** — 수렴이 대체한다.
      `stepDuration`은 `minConvergeTime`으로 이름을 바꿔 "이보다 여유가 없으면 생략" 규율만 남긴다.
- [x] 회전은 유지하되 **목표를 적 쪽으로** 잡는다(지금은 `next.RingPosition`을 본다 — 수렴 지점으로 바꾼다).
- [x] `HandleJudgeTargetBegan` 구독 제거(수렴이 `OnPatternQueued` 타이밍으로 옮겨간다).
- [x] `HandleOpponentChanged`는 **방향만 돌린다** — 중앙 복귀 제거(결정 1-5 개정). 표류는 클램프가 막는다.

### Step 3 — 적 진입 거리 축소 확인
- [x] `AssignAttack`이 받는 목표가 링이 아니라 수렴 지점이 되도록 배선(코드).
- [ ] `pendingScheduleStart`까지 도착 못 하는 경우가 줄어드는지 **실측**.
- [ ] 여전히 모자라면 `entryDistanceMul`·`ringRadius`를 조정하거나 결정 1-4 폴백을 탄다.

### Step 4 — 기즈모
- [x] `EnemyDirector` 기즈모에 **현재 수렴 계획**(플레이어 목표 · 적 목표 · 중점)을 플레이 중에 그린다.
      숫자로는 안 보이는 문제다.
- [x] 편집 중에는 기준 거리 원만 그린다(계획은 런타임에만 존재).

### Step 5 — 문서
- [x] `Guide_EnemyCombat` 4단계 — `ImpactAnchor`가 이제 **기준 거리의 출처**임을 반영.
- [x] `CLAUDE.md` EnemyCombat 항목 갱신.

### Step 6 — 검증

**확인 완료**
- [x] `read_console` 에러/경고 **0건**.
- [x] 씬 배선 확인 — `PlayerCombatMover.enemyDirector` 배선됨, `duelAnchor = ImpactAnchor`,
      **기준 결투 거리 1.00m**, `minConvergeTime 0.25`, `maxOffset 1.5`.

**플레이에서 확인 필요**
- [ ] 둘이 마주 달려 만나는지, 만난 거리가 `duelDistance`인지.
      → 플레이 중 `EnemyDirector` 선택하면 **수렴 계획이 기즈모로 뜬다**(두 목표·간격·남은 시간).
- [ ] 패턴이 짧을 때(여유 부족) 폴백이 도는지 — 플레이어 정지 + 적만 접근, 거리는 유지.
- [ ] 연속 처치에서 플레이어가 링 쪽으로 표류하지 않는지 — **복귀가 없어졌으므로 클램프 하나로 막는다**.
- [ ] 클립 시작 시점에 **둘 다 정지**해 있는지(기하 동결).
- [x] 툴 기본값을 씬 실측(1.00m)에 맞춤. **이미 열어본 창은 옛 값(2.0)을 들고 있으니 손으로 고칠 것.**

---

## 4. 위험과 대비

| 위험 | 징후 | 대비 |
|---|---|---|
| **클립 시작 후에도 이동** | 칼이 어긋난다 — 정렬 전체가 깨진다 | 결정 1-2. 도착 시각 = `pendingScheduleStart` |
| 만난 거리가 `duelDistance`가 아님 | 칼이 빗나가거나 관통 | 클램프 후 **적 목표를 재계산**(결정 1-5, 순서 중요) |
| 플레이어 표류 누적 | 몇 번 교전 후 링 밖으로 | `maxOffset` 클램프 **(고정 원점 기준)**. 복귀는 제거됐으니 이게 유일한 장치다 |
| 여유 부족 시 순간이동 | 미끄러지듯 붙는다 | 결정 1-4 — 수렴 생략, 적만 접근 |
| 툴과 런타임 거리 불일치 | 짝 에디터에서 닿는데 게임에서 안 닿음 | 결정 1-6 — 앵커가 단일 출처, 툴에 같은 값 |
| 카메라가 만남 지점을 놓침 | 교전이 화면 밖 | Step 6 실측. 필요하면 별도 판단(비목표) |

---

## 5. 피드백

>>> 여기에 `>>>`로 의견을 남겨 주세요.

> **폐기됨(2026-08-02).** 리시(`maxOffset`/`ArenaOrigin`)와 대기석 전진 배치는 무대가 월드에 고정되면서 사라졌다.
> 수렴 자체는 남았고 비율만 `playerShare`(0.85)로 저작값이 됐다. → `docs/StageTraversal/`
