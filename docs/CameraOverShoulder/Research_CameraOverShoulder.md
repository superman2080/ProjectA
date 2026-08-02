# Research — CameraOverShoulder

요구: **카메라가 플레이어 뒤에서 플레이어 로테이션을 따라가되, 교전 상대까지 한 화면에 담는다.**

---

## 1. 현재 씬 실측값 (BattleScene, 2026-08-02)

### 1-1. 게임플레이 vcam (`CinemachineCamera`, id 96196)

| 항목 | 값 |
|---|---|
| Transform | pos (0, 10, −25), euler (18.78, 0, 0) |
| Lens | FOV 25 (망원) |
| TrackingTarget | `CameraTargetGroup` |
| `CinemachineFollow.BindingMode` | **`LockToTarget`** (enum 3) |
| `CinemachineFollow.FollowOffset` | **(0, 10, −25)** |
| `TrackerSettings.PositionDamping` | (1, 1, 1) |
| `TrackerSettings.RotationDamping` | (1, 1, 1) |
| `CinemachineRotationComposer` | TargetOffset (0, 1.5, 0), Damping (0.5, 0.5), DeadZone 0.1, HardLimits 0.8×0.3 |
| `CinemachineBasicMultiChannelPerlin` | AmplitudeGain 0.1 (휴지값), 프로파일 `IngameCameraShake.asset` |
| `CinemachineGroupFraming` | FramingMode `HorizontalAndVertical`, FramingSize 0.6, Damping 1.0, **SizeAdjustment `DollyOnly`**, DollyRange (−4, +6), FovRange (1, 100) |

### 1-2. `CameraTargetGroup` (id 96182)

| 항목 | 값 |
|---|---|
| transform | pos (0,0,0), rot (0,0,0) |
| `PositionMode` | `GroupCenter` |
| **`RotationMode`** | **`Manual`** |
| `UpdateMethod` | `LateUpdate` |
| Targets | 0개 (런타임에 `CameraDirector.SetupFraming`이 2칸 채움) |

### 1-3. 인트로 vcam (`IntroCamera`, id 96418)

`CinemachineSplineDolly` + `CinemachineSplineDollyLookAtTargets`, LookAt = 플레이어 직접, Priority −10. **TargetGroup을 쓰지 않는다.** 이 작업의 영향권 밖.

---

## 2. ⚠ 문서와 씬의 불일치 — 그리고 그게 왜 무해했는가

`CLAUDE.md` §7-2와 `docs/CameraFraming/Plan_CameraFraming.md` D-4는 이렇게 못박고 있다:

> **`CinemachineFollow.BindingMode`는 반드시 `WorldSpace`.** 그룹 회전은 멤버 배치에서 파생되므로 `LockToTarget`이면 적이 링을 돌 때마다 구도가 통째로 회전한다.

**실제 씬은 `LockToTarget`이다.** 그런데 증상이 나타난 적이 없다. 이유:

- D-4의 전제 "그룹 회전은 **멤버 배치에서 파생**된다"는 `RotationMode = GroupAverage`일 때만 참이다.
- 실제 그룹은 **`RotationMode = Manual`** — 그룹의 회전은 그룹 GameObject의 `transform.rotation`이고, 그 값을 **아무도 쓰지 않아 항상 identity**다.
- identity 로컬 축 = 월드 축. 그래서 `LockToTarget`이 `WorldSpace`와 **수치적으로 동일하게** 동작해 왔다.

즉 D-4는 **잘못된 진단이었고, 잘못된 처방이 적용되지 않아 살아남았다.**

**이 불일치가 이번 요구의 발판이다.** `Manual` + `LockToTarget` 조합은 "그룹 transform 회전을 돌리면 카메라 궤도가 통째로 따라 돈다"는 뜻이고, 그게 정확히 요구하는 동작이다. **새 컴포넌트도, 새 vcam도 필요 없다 — 지금 아무도 안 쓰는 훅이 이미 배선돼 있다.**

---

## 3. 관련 코드

### 3-1. `CameraDirector` (`Assets/02. Scripts/Camera/CameraDirector.cs`)

- 세 기능(쉐이크·프레이밍·인트로)의 유일 관리 지점.
- **Cinemachine 타입은 `ApplyShake` / `ApplyFraming` / `IntroRoutine` 세 이음매에만** 등장한다는 봉인 규율.
- 프레이밍 상태: `opponentTransform`, `opponentWeight`, `framingEnabled`.
- `UpdateFraming()` (L337) — 평면 거리로 목표 가중치 계산 → `weightDamping`(0.35) 지수 감쇠 → `ApplyFraming`.
- `ApplyFraming(Transform, float)` (L362) — `targetGroup.Targets[1]`의 `Object`/`Weight`만 건드린다. **그룹 회전은 손대지 않는다.**
- 이미 들고 있는 참조: `actionPlayer.transform`(플레이어), `targetGroup`, `enemyDirector`. **새 참조 배선 불필요.**

### 3-2. `PlayerCombatMover` (`Assets/02. Scripts/Character/PlayerCombatMover.cs`)

- `turnDuration = 0.15f`. `ScheduleTurn`이 `Quaternion.Slerp` + `SmoothStep`으로 **0.15초 만에** 회전을 끝낸다.
- 회전 목표는 언제나 `Quaternion.LookRotation(planar)` — **Y축만 돈다** (pitch/roll 0).
- 회전 계기는 둘: `OnDuelScheduled`(결투 계획), `OnOpponentChanged`(상대 교체). 둘 다 상대 교체 순간에 몰린다.

**⚠ 여기가 이번 설계의 유일한 실질 위험이다.** 무대(반경 8m)에서 상대가 반대편으로 바뀌면 플레이어 yaw가 최대 180° 돈다. 그걸 **0.15초에** 카메라가 그대로 복사하면 화면이 통째로 휙 돈다.

### 3-3. `EnemyDirector` / 무대

- 무대는 월드 고정 원(`stageRadius` 8), 적은 원 **안**에 흩어진다.
- 결투 간격 ≈ 1m (`duelDistance`), 상대 승격 직후 다음 상대는 최대 무대 횡단 거리.
- `dropoffDistance` 6m / `fullFrameDistance` 3m — 이 범위에서만 상대가 구도에 개입한다.

---

## 4. 제약과 상호작용

### 4-1. 그룹 위치는 **가중 중심**이지 플레이어가 아니다

`PositionMode = GroupCenter`. 상대 가중치가 0→1로 오르면 그룹 중심이 플레이어에서 두 사람 중점 쪽으로 미끄러진다(결투 간격 1m 기준 ≈0.5m). 카메라는 "플레이어 뒤"가 아니라 **"교전 중심의 뒤"**에 선다.

결투 간격이 1m 남짓이라 실제 차이는 0.5m 미만이고, 방향이 플레이어 등 뒤로 고정되므로 **요구를 만족한다**. 오히려 상대가 화면 중앙에 더 잘 남는다.

### 4-2. `FollowOffset`이 지금 값 그대로면 안 된다

(0, 10, −25)는 **높이 10m, 거리 25m의 부감 롱샷**이다. FOV 25 망원이라 화면상으로는 성립하지만, 이 오프셋이 그룹 로컬 축으로 회전하기 시작하면 카메라가 25m 반경의 큰 원을 그린다 — **같은 각속도라도 선속도가 25배**다. 뒤에서 따라가는 구도라면 오프셋 거리를 크게 줄여야 한다.

`GroupFraming.DollyRange`(−4, +6)는 이 기준 거리 **위에서** 앞뒤로 미는 값이라 기준이 바뀌면 함께 봐야 한다.

### 4-3. 쉐이크·인트로와 채널이 겹치지 않는다

- 쉐이크는 Perlin(Noise 스테이지) → 그룹 회전과 무관.
- 인트로 vcam은 그룹을 안 쓴다 → 무관.
- `CinemachineRotationComposer`(Aim 스테이지)는 그룹을 **바라보는** 일만 한다. 그룹 회전은 Body 스테이지의 오프셋 축에만 쓰인다 → 조준은 그대로.

**세 기능 독립 규율이 유지된다.**

### 4-4. 실행 순서

`CinemachineTargetGroup.UpdateMethod = LateUpdate`, `CameraDirector.Update()`는 그보다 먼저 돈다. 그룹 회전을 `Update`에서 써도 같은 프레임에 반영된다. **추가 조치 불필요.**

### 4-5. 반려한 대안

| 대안 | 반려 이유 |
|---|---|
| `CinemachineThirdPersonFollow`로 교체 | Body 스테이지를 통째로 갈아치운다. `GroupFraming`이 기대는 `FollowOffset` 기준 거리가 사라져 **교전 상대 프레이밍이 죽는다.** 요구의 절반을 버리는 셈. |
| Follow 타겟을 플레이어로 되돌리고 프레이밍을 직접 계산 | `GroupFraming`이 하는 일(바운드→돌리)을 손으로 다시 짜는 것. 코드가 늘고 씬 튜닝 지점이 사라진다. |
| 그룹 `RotationMode = GroupAverage` | 회전이 **멤버 배치**에서 나온다 — 플레이어 로테이션이 아니라 "플레이어→적 방향"이 된다. 상대 가중치 0 구간에서는 정의되지 않아 구도가 튄다. |
| 플레이어를 그룹 GameObject의 부모로 | 그룹 **위치**까지 플레이어에 묶여 가중 중심(4-1)이 무의미해진다. |

---

## 5. 결론

필요한 변경은 **둘**이다.

1. **코드** — `CameraDirector`가 그룹 transform의 yaw를 플레이어 yaw로 몬다. 단, `PlayerCombatMover.turnDuration`(0.15초)을 그대로 복사하지 않고 **카메라 전용 감쇠**를 건다(3-2).
2. **씬** — `FollowOffset`을 부감 롱샷에서 어깨너머 거리로 재조정(4-2).

`BindingMode`는 **이미 `LockToTarget`이라 바꿀 것이 없다.** 대신 `CLAUDE.md` §7-2와 `Plan_CameraFraming` D-4의 "반드시 `WorldSpace`" 규칙은 **근거가 틀렸고 이제 정반대 요구가 되었으므로 갱신해야 한다.**
