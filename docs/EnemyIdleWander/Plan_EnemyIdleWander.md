# Plan — EnemyIdleWander (교전 대기 중인 적이 플레이어 주위를 배회)

근거: `docs/EnemyIdleWander/Research_EnemyIdleWander.md`
v1(제자리 자세 변주 우선) 폐기 — 4방향 걷기 클립이 이미 있어 **서클링이 바로 성립**한다.

## 전제 정정

- 이동 클립 4종이 **이미 있다**: `Clip/Move/WalkForward.anim` · `WalkLeft_HS` · `WalkRight_HS` · `WalkBackward_HS` (각 1.0초).
- **무기 커브는 신경 쓰지 않는다** — 적 프리팹은 `Enemy_Samurai_Male`이고 그 리그 기준으로 이미 구워져 있다. v1의 "굽기 선행" 단계는 통째로 삭제.
- v1의 "옆걸음 클립이 없어 서클링 불가"는 **오판이었다.**

---

## 그림

**교전 중이 아닌 적은 플레이어를 바라본 채 일정 거리를 두고 주위를 돈다.** 플레이어가 다가오면 살짝 물러나 간격을 지킨다.

```
        (적)
          ↖ 옆걸음
   (적) ← ● 플레이어 → (적)
          ↙
        (적)     ← 전부 플레이어를 바라본 채 접선 방향으로 이동
```

- **바라보는 방향은 안 바뀐다**(플레이어 고정). 그래서 이동 방향이 **자기 로컬 기준 좌/우/뒤/앞**이 되고, 4방향 클립이 그대로 맞는다.
- 시선 추적은 `EnemyView.gazeTarget`이 **이미 하고 있다** — 새로 만들 것이 없다.

---

## ⚠ 무리 시스템과의 경계 (가장 중요)

방금 만든 `EnemyCluster`(§11-6)는 적을 **집결지 슬롯**에 세운다. 플레이어 중심 서클링은 그 자리를 벗어난다. **둘이 싸우면 안 된다.**

| 대상 | 행동 | 이유 |
|---|---|---|
| `active` 무리, 교전 중 아님 | **플레이어 주위를 배회** | 플레이어가 이미 그 무리 안에 있어 집결지와 사실상 같은 자리다 |
| `active` 무리, `currentOpponent` | 배회 없음 | 지금 싸운다 |
| **`staged` 무리** | **배회 없음 — 집결지에 서 있는다** | 여기가 배회하면 무리가 안 모인다. 집결 자체가 무너진다 |

**즉 배회는 `active` 무리 전용이다.** 승격되는 순간 배회가 시작되고, 상대로 배정되는 순간 멈춘다.

---

## Step 1 — 애니메이터: 4방향 블렌드

- [ ] `EnemyAnimator.controller`에 `Walk` 스테이트 추가 — **2D Freeform Directional 블렌드 트리**, 파라미터 `MoveX` / `MoveY`.
  - 배치: `WalkForward`(0,1) · `WalkBackward_HS`(0,-1) · `WalkLeft_HS`(-1,0) · `WalkRight_HS`(1,0)
  - ⚠ **CrossFade로 클립을 갈아끼우지 않는다.** 방향이 연속으로 바뀌므로 스냅되면 발이 튄다. 블렌드 트리가 이 문제의 정답이다.
- [ ] `Idle` ↔ `Walk` 전이(코드가 `CrossFadeInFixedTime`으로 직접 몬다 — 기존 `Idle`/`Run` 규율과 동일).
- [ ] 기존 `Run` 스테이트는 **그대로 둔다** — 결투 접근·후퇴·집결 이동이 계속 쓴다. 배회만 `Walk`를 쓴다.

## Step 2 — `EnemyView`: 배회 실행

- [ ] 필드: `walkStateName`(기본 `"Walk"`) · `moveXParam`/`moveYParam` · `wanderBlendDamp`(0.15초).
- [ ] `public void SetWander(Vector3 target, float speed)` / `public void StopWander()`.
- [ ] ⚠ **`ScheduleMove`를 쓰지 않는다.** `moving`/`moveTo`가 켜지면 `Destination`이 배회 목적지를 돌려주고 **§11-1 결투 계획이 오염된다**(플레이어가 적이 잠깐 들를 자리로 달려간다). 별도 경로(`wanderTarget`)를 두고 `Destination`은 안 건드린다.
- [ ] 매 프레임: 목표로 `wanderSpeed`만큼 이동 → 실제 변위를 **자기 로컬 좌표로 변환** → `MoveX`/`MoveY`에 `SmoothDamp`로 대입.
  - 로컬 변환이 핵심이다. 플레이어를 바라본 채 접선으로 움직이면 로컬 X가 커져 자동으로 옆걸음 클립이 나온다.
- [ ] 정지 시 `MoveX`/`MoveY`를 0으로 damp하고 `Idle`로 CrossFade.
- [ ] ⚠ **배회 금지 조건** (전부 `EnemyView`가 이미 들고 있다 — 뷰 안에서 닫힌다):
  `Phase.Dying` / `dissolving` / `hasPendingAttack` / `hasPendingReaction` / `reactionUntil` 이내 / `moving`(진짜 이동 우선).
- [ ] ⚠ `AssignAttack` · `AssignFeint` 진입 시 **즉시 `StopWander()`**. 배회 보간과 결투 접근 보간이 겹치면 위치가 떨린다.

## Step 3 — `EnemyDirector`: 궤도 계산

- [ ] 필드:
  - `wanderEnabled`(bool, true) — 끄면 예전대로 정지. **회귀 없음.**
  - `standoffDistance`(3.5m) — 플레이어와 유지할 거리. ⚠ `duelDistance`보다 커야 한다(교전 시 좁혀 들어가는 여지).
  - `wanderSpeed`(1.2 m/s) — 걷기 클립 속도에 맞춰 튜닝. 안 맞으면 발이 미끄러진다.
  - `orbitSpeed`(15도/초) · `orbitReverseInterval`(3~7초 랜덤) — 한 방향으로만 돌면 회전목마로 보인다.
  - `wanderJitter`(0.4m) — 궤도에 개체별 랜덤 오프셋. 없으면 4명이 정확한 원 위에 서서 인공적이다.
- [ ] `EnemyRing`에 순수 계산 추가:
  `PickOrbitSlots(playerPosition, count, standoffDistance, phase, stageCenter, stageRadius)` →
  플레이어 주위 `count`개 각도를 **고르게 분산**해 무대 안으로 클램프. 각도는 `phase`(시간에 따라 도는 값)로 회전.
  - 고르게 분산하는 것이 겹침 방지다 — `minSpacing` 반발 계산을 따로 두지 않는다(그게 원래 흩어짐의 원인이었다).
- [ ] `Update`에서 `activeCluster`의 비교전 적들에게 슬롯을 배정하고 `SetWander` 호출.
  - **슬롯 배정은 인덱스 고정**이다. 매 프레임 다시 배정하면 적끼리 자리를 바꾸며 서로를 가로지른다.
- [ ] 무대 밖으로 나가지 않게 클램프(`stageRadius`).

## Step 4 — 기즈모

- [ ] 플레이어 주위 `standoffDistance` 원.
- [ ] 각 배회 적의 현재 위치 → 궤도 슬롯 선.
- [ ] 배회 안 하는 적은 다른 색 + 이유 라벨(교전 중 / 예약 있음 / staged).

## Step 5 — 테스트

- [ ] `PickOrbitSlots` — 결과가 플레이어에서 `standoffDistance`(무대 클램프 시 그 이하), 서로 고르게 벌어짐, 무대 안, 같은 입력에 같은 출력.
- [ ] 기존 `EnemyRingTests` 전부 통과(회귀).

## Step 6 — 플레이 검증

- [ ] **발 미끄러짐** — `wanderSpeed`와 클립 이동량이 맞는가. 이 시스템에서 가장 먼저 티 나는 결함이다.
- [ ] `Destination` 오염 없음 — 배회 중인 적을 상대로 배정했을 때 플레이어가 실제 위치로 가는가.
- [ ] `staged` 무리가 배회하지 않고 집결지에 모이는가.
- [ ] 4명이 서로 가로지르지 않는가.

---

## 열린 위험

- **발 미끄러짐이 주 위험이다.** 클립의 실제 이동 속도와 `wanderSpeed`가 어긋나면 즉시 보인다. 클립 길이가 1.0초이므로 **한 사이클의 루트 이동량을 재서 `wanderSpeed` 기본값을 정하는 게 Step 6의 첫 항목**이다.
- **`standoffDistance`가 표적 선택을 평평하게 만든다.** `active` 전원이 같은 거리에 서므로 `TakeTargetForWindow`의 거리 기반 선택이 무의미해진다 — 이미 §11-6에서 받아들인 "무리 안 정지 구간"과 같은 성질이라 새 문제는 아니다.
- **배회가 `minPlayerDistance`(4m)보다 가까울 수 있다.** 그 값은 *스폰* 제약이라 무관하지만, `standoffDistance`(3.5m)가 그보다 작다는 점은 의도적으로 확인해 둘 것.

---

## 피드백

`>>>`로 의견 남겨주면 반영한다. 확정 전에는 구현하지 않는다.
