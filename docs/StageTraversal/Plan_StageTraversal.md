# Plan — StageTraversal

근거: `docs/StageTraversal/Research_StageTraversal.md`

## 목표

**원형 무대를 월드 원점에 고정하고, 플레이어가 무대 이곳저곳을 빠르게 이동하며 적을 벤다.**

적은 자기 자리에 서 있다. 스폰은 플레이어 시야 밖에서. 카메라는 범위 밖(시점 변경 예정).

```
지금   적이 플레이어에게 달려온다        플레이어 이동 1.5m → 1.0 m/s (걷기보다 느림)
이후   플레이어가 적에게 달려간다        창이 고른 거리   → 3~6 m/s
```

## 설계 결정

### D-1. `Center`를 **월드 원점 상수**로 바꾼다 — 이 한 줄이 변경의 축이다

```csharp
private Vector3 Center => arenaCenter != null ? arenaCenter.position : transform.position;
//                                     ↑ 씬에서 플레이어를 가리킨다
```

`arenaCenter` 배선을 끊고 **무대 중심(월드 원점)** 을 가리키는 씬 오브젝트로 바꾼다. `stageRadius`(기본 8)를 새로 둔다.

**파생 전체가 자동으로 따라온다** — 배치·스폰·기즈모가 이미 `Center`에서 나온다. 고쳐야 할 것은 `Center`가 상수가 되면서 **의미를 잃는 것들**(D-5)이지, 좌표 계산이 아니다.

**표류 버그 부류가 원천 소멸한다**(Research 2-1). `ArenaOrigin`을 `Awake`에 캐시해 막았던 것도 `Center`가 상수면 필요 없다 — **모델이 하나가 된다.**

>>> 

### D-2. 만나는 지점을 **저작 비율(`playerShare`)** 로 정한다 — 기본 0.85 (8:2 ~ 9:1)

수렴 구조는 살린다. 바뀌는 건 **하드코딩된 0.5가 저작값이 되는 것**뿐이다.

```csharp
Vector3 mid = playerPos + toEnemy * playerShare;   // 0.5 → playerShare(0.85)
```

**적도 조금은 마중 나온다.** 완전히 정지하면 "가만히 서서 베이기를 기다리는 표적"이 되고, 한 걸음이라도 나오면 **교전으로 읽힌다.** 그 한 걸음이 15%다.

간격 8m · 결투거리 1m 기준:

| 비율 | 플레이어 | 적 |
|---|---|---|
| 0.5 (지금) | 3.5m | 3.5m |
| **0.85** | **5.95m** | **1.05m** |
| 0.9 | 6.3m | 0.7m |

적 몫은 창(1.48초)에 1m 남짓이라 그대로 보간하면 **초속 0.7m로 기어간다.** `EarliestArrival`(`moveSpeed` 3 m/s)이 이미 이걸 막는다 — **0.35초에 가서 서고 나머지는 플레이어를 바라본다.** 마중 나온 뒤 기다리는 그림이라 맞다.

`minMoveDistance`(0.3m) 게이트도 그대로 산다 — 창이 짧아 적 몫이 0.3m 미만이면 로코모션을 안 켜고 조용히 붙는다.

**리시(`maxOffset`)는 제거한다.** 무대 자체가 경계이고, 표적이 무대 안이므로 플레이어는 자동으로 무대 안에 남는다.

>>> 

### D-3. **다음 표적은 창이 고른다** — 여기가 속도감의 심장

창은 음악이 정하고 못 바꾼다(Research 5: 0.50 ~ 2.10초). 거리를 고정하면 **속도가 창에 따라 4배 흔들린다.** 반대로 하면 된다:

```
플레이어 이동량 = playerShare × (적까지 거리 − duelDistance)      ← D-2
              = cruiseSpeed × 창                              ← 원하는 것

∴ 목표거리 = clamp(cruiseSpeed × 창 / playerShare + duelDistance,
                  minTargetDistance, stageRadius × 2)
다음 표적  = 그 거리에 가장 가까운 적
```

**`playerShare`로 나눠야 한다.** 플레이어는 간격의 85%만 가므로, 그 몫이 `cruiseSpeed × 창`이 되려면 간격은 그보다 커야 한다. 안 나누면 비율을 올릴수록 오히려 느려진다.

`cruiseSpeed`(기본 4.5 m/s) 하나로 **체감 속도를 일정하게 유지**하면서, 짧은 구간은 근거리 난타 / 긴 구간은 무대를 가로지르는 대시로 갈린다. **음악 밀도가 곧 이동 리듬이 된다.**

기존 `PickNextOpponent`(각도 반시계)는 **거리를 아예 안 본다**(Research 3) — 플레이어 중심에서는 전부 등거리였기 때문. 무대 고정에서는 거리가 실재하므로 **거리 기반 선택으로 교체**한다. `EnemyRing`에 새 함수를 넣고 테스트도 같이 쓴다.

앞서 검토한 "전방 가중"은 **버린다.** "이곳저곳"은 방향이 꺾여야 나오고, 방향은 창이 고른 거리에서 자연히 따라온다.

>>> 

### D-4. 스폰은 **시야 밖**, 판정은 각도가 아니라 **절두체**로

지금 `PickSpawnAngle`은 `cos(각차)`로 등 뒤를 선호한다(Research 3). **무대가 플레이어 중심일 때만 성립하는 근사**다 — 이제 적은 무대 어디에나 있고 플레이어도 무대 어디에나 있으므로, 각도만으로는 화면 안인지 알 수 없다.

→ **카메라 절두체로 직접 판정한다**(`GeometryUtility.CalculateFrustumPlanes` + `TestPlanesAABB`).

배치는 **2차원 후보 샘플링**으로 바꾼다(지금은 각도 1차원):

```
후보 N개를 무대 원 안에 무작위 생성
  ├ 절두체 안이면 탈락
  ├ 기존 적과 minSpacing 미만이면 탈락
  ├ 플레이어와 minPlayerDistance 미만이면 탈락
  └ 남은 것 중 '기존 적과의 최소거리'가 최대인 것 선택
전부 탈락하면 → 카메라 전방과 가장 각차가 큰 후보로 폴백
```

**시야 밖이면 걸어 들어올 이유가 없다** → `entryDistanceMul`·`entryDuration` 제거, 즉시 배치. 프리웜 부담과 팝인 연출이 같이 사라진다.

⚠ 플레이어가 홱 돌면 방금 나타난 적이 보일 수 있다. **감수한다** — 무대가 넓고 스폰이 드물어 확률이 낮고, 막으려면 예측이 필요해 비용이 크다.

>>> 

### D-5. "적을 데려오는 장치"를 **전부 제거한다** — 순 감소다

| 제거 | 이유 |
|---|---|
| `maxOffset` / `arenaOrigin` / `ArenaOrigin` | 리시. 무대가 경계다 |
| `StageOnDeck` / `stagingDistance` / `bindToImpactWindow` | 대기석 전진 배치 |
| `minConvergeTime` | 수렴 자체가 없다 |
| `onDeck` | 미리 뽑아 미리 옮기는 장치 |
| `entryDistanceMul` / `entryDuration` | 시야 밖 스폰이면 불필요 |
| `EnemyView.ReturnToRing` | 링이 없다. 적은 싸운 자리에 남는다 |
| `BuildDuelPlan`의 리시 클램프 | 무대가 경계다 |

**새로 느는 개념은 "무대 영역"(중심+반경) 하나뿐이다.**

**남기는 것** — 적이 15%는 움직이므로(D-2) 이동 기반은 그대로 산다:

- `EnemyView.MoveSpeed` / `EarliestArrival` — 적 몫이 1m 남짓이라 창 전체로 늘이면 기어간다. 빨리 가서 서야 한다
- `ScheduleMoveAfter` — 실패 후퇴 뒤 다음 접근을 잇는 데 계속 쓴다
- `Resolve`의 짧은 후퇴(`failRetreatDistance`) — 회피 연출. **후퇴한 그 자리에 선다**(제자리 복귀 없음, 플레이어가 다시 찾아간다)
- `RingPosition` — 이름만 무대 배치 위치로 의미가 바뀐다. 복귀 목적지로는 더 안 쓴다
- `BuildDuelPlan`의 중점 계산 — `0.5` → `playerShare`로 바뀔 뿐 구조는 유지
- `AssignAttack`의 이동 인자 — 적이 여전히 자기 몫을 간다

>>> 

### D-6. 상대 선택을 `PromoteOpponent` → `BindReservation`으로 옮긴다

**창을 알아야 표적을 고를 수 있는데**(D-3), 창은 배정 시점(`BindReservation`)에서야 나온다. 지금 선택은 처치 시점(`KillOpponent` → `PromoteOpponent`)이다.

둘은 `ResolveReservation` **한 호출 안**이라 **같은 프레임**이다 — 옮겨도 `OnOpponentChanged` 발행 타이밍이 실질적으로 바뀌지 않는다(카메라 프레이밍 무영향).

`PromoteOpponent`는 "죽은 상대를 놓아준다"만 남고, **고르는 일은 배정이 한다.** §11-1의 규율("상대 배정은 판정 대상 승계 시점")과도 더 일관된다.

>>> 

### D-7. 플레이어 로코모션 분기를 **거리 → 창**으로

Research 6 — Quickshift 클립이 1초인데 평균 창이 1.48초다. 배속 하한이 1이라 **클립이 먼저 끝나고 나머지는 미끄러진다.** 이동 거리를 늘리면 이 구멍이 커진다.

```
창 > 클립 길이  → Sprint     (루프라 길이에 상관없이 채운다)
창 ≤ 클립 길이  → Quickshift (버스트, 배속으로 압축 — 상한 없음)
```

거리가 아니라 **"클립이 창을 채우는가"**로 가른다. Sprint에는 이동 거리에 맞춘 배속이 필요하다(지금 Sprint는 배속이 아예 없어 제자리걸음처럼 미끄러진다).

>>> 

### D-8. 무대 메쉬는 나중, **논리 영역은 지금** 정의한다

맵 메쉬는 나중에 생성된다. 그래도 **중심+반경은 지금 확정해야** 적을 흩뿌릴 수 있다. 나중 메쉬가 이 영역에 맞춰 생성되면 된다 — 순서가 반대면 또 어긋난다.

씬에 `Stage` 빈 오브젝트를 원점에 두고 `EnemyDirector.arenaCenter`가 그걸 가리킨다. 기즈모가 `stageRadius` 원을 그린다.

>>> 

---

## 단계

- [x] **Step 1 — 무대 영역 정의 (D-1, D-8)**
  `stageRadius`(8) 추가. `arenaCenter`를 원점 `Stage` 오브젝트로 재배선(씬). `ringRadius`/`radiusJitter`/`minAngleGap`은 Step 3에서 대체되므로 아직 둔다.
  기즈모가 `Center` + `stageRadius` 원을 그리도록.

- [x] **Step 2 — `EnemyRing`에 2D 배치·거리 선택 추가 (D-3, D-4)**
  `Assets/02. Scripts/Enemy/Core/EnemyRing.cs`(asmdef, 순수 함수):
  - `PickStagePosition(occupied, stageCenter, stageRadius, playerPos, minSpacing, minPlayerDistance, isVisible, candidateCount)` — 절두체 판정은 **델리게이트로 주입**해 Core가 카메라를 모르게 한다(테스트 가능성 유지)
  - `PickTargetByDistance(candidatePositions, from, desiredDistance)` — 목표거리에 가장 가까운 것
  - `EnemyRingTests`에 케이스 추가: 간격 보장 / 전부 가려졌을 때 폴백 / 목표거리 선택 / 후보 0개

- [x] **Step 3 — 스폰을 무대 배치로 교체 (D-4)**
  `SpawnIntoRing` → `SpawnIntoStage`. 각도 대신 위치를 받고 **즉시 배치**(`entryDuration` 제거).
  절두체 판정은 `viewCamera`(비면 `Camera.main`)에서 `GeometryUtility.CalculateFrustumPlanes`.
  `EnemyView.Setup`의 `ringAngle`/`enterPosition`/`enterDuration` 인자 정리.

- [x] **Step 4 — 표적 선택을 창 기반으로 + 배정 시점으로 이동 (D-3, D-6)**
  `cruiseSpeed`(4.5) / `minTargetDistance`(2) 추가.
  `BindReservation`이 `arriveTime`으로 목표거리를 구해 상대를 고르고 `OnOpponentChanged`를 발행.
  `PromoteOpponent`는 죽은 상대 정리만. `onDeck` 제거.

- [x] **Step 5 — `BuildDuelPlan`에 `playerShare` 도입 (D-2)**
  `playerShare`(0.85, `[Range(0.5, 1)]`) 추가. `toEnemy * 0.5f` → `toEnemy * playerShare`.
  리시 클램프(`ArenaOrigin`/`maxOffset`)와 `minConvergeTime` 분기 제거.
  `DuelPlan`/`OnDuelScheduled`/`AssignAttack` 시그니처는 **그대로** — 적이 여전히 자기 몫을 간다.

- [x] **Step 6 — 죽은 장치 제거 (D-5)**
  `maxOffset` · `arenaOrigin` · `ArenaOrigin` · `StageOnDeck` · `stagingDistance` · `bindToImpactWindow` · `minConvergeTime` · `entryDistanceMul` · `entryDuration` · `EnemyView.ReturnToRing` 정리.
  **남기는 것**(D-5 후반): `MoveSpeed` · `EarliestArrival` · `ScheduleMoveAfter` · `Resolve`의 짧은 후퇴 · `RingPosition`(의미만 '무대 배치 위치'로).

- [x] **Step 7 — 플레이어 로코모션 분기 교체 (D-7)**
  `CharacterActionPlayer`: `sprintMaxDistance` → 창 기준 분기. Sprint에 `sprintSpeedParam`/`sprintClip` 추가(이동 거리에 맞춘 배속).
  애니메이터 `Sprint` 스테이트 Speed Multiplier 배선.

- [x] **Step 8 — 문서 갱신**
  - `CLAUDE.md` §11-1/§11-2 재작성 — **"플레이어 중앙 고정, 전진 이동 없음"이 폐기**됐음을 명시
  - `docs/DuelConverge/`, `docs/OpponentBinding/`의 폐기된 결정에 표시(D-5 목록)
  - `docs/EnemyCombat/Plan_EnemyCombat.md:17`의 무대 정의 갱신
  - `docs/!Guides/Guide_EnemyCombat.md` 튜닝 손잡이 교체

- [ ] **Step 9 — 검증(플레이)**
  - 플레이어가 무대를 **가로질러 다닌다**. 같은 자리로 돌아오지 않는다
  - **창이 짧으면 가까운 적, 길면 먼 적**으로 간다(`cruiseSpeed` 조정)
  - 체감 속도가 구간마다 크게 흔들리지 않는다
  - **적이 한 걸음 마중 나온다** — 정지 표적으로 보이지 않는다(`playerShare` 0.85 ↔ 0.9 비교)
  - 그 마중이 **기어가지 않는다** — 빨리 가서 서고 플레이어를 바라본다(`EarliestArrival`)
  - 적이 **화면 안에서 튀어나오지 않는다**(D-4)
  - 적이 겹쳐 서지 않는다(`minSpacing`)
  - 플레이어가 무대 밖으로 안 나간다(표적이 무대 안이므로 자동)
  - 로코모션 클립이 창을 **끝까지 채운다** — 미끄러지는 구간 없음(D-7)
  - 실패 시 적이 짧게 물러난 **그 자리**에 서 있고 플레이어가 다시 찾아간다
  - 곡 전체에서 적 인스턴스·시체가 쌓이지 않는다

## 범위 밖

- **카메라** — 시점 변경 예정이라 이번엔 안 건드린다. 다만 현재 `CinemachineFollow` 댐핑(1.0)과 `dropoffDistance`(6)로는 **먼 표적이 안 담기고 대시를 못 따라간다.** 시점 작업에서 반드시 같이 본다.
- 무대 메쉬 생성
- 노드 단위 피격 반응
- 래그돌
