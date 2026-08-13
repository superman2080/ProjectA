# Research — 카메라 진정(CameraCalm)

## 문제

플레이 중 카메라가 어지럽다. **어느 하나가 과한 게 아니라 큰 움직임이 층층이 겹쳐 기준선이 없다.**

한 패턴(≈1.38초) 안에서 동시에 도는 층:

| 층 | 값 | 주기 | 코드 |
|---|---|---|---|
| yaw 추종(등 뒤) | `cameraTurnDamping` 0.45s, 회전량 **최대 180°** | 상대 교체마다 | `CameraDirector.UpdateFraming` (`CameraDirector.cs:725`) |
| 그룹 프레이밍 돌리 | `weightDamping` 0.35s, 3~6m 연속 | 매 패턴 | `ResolveWeight` (`:748`) |
| 줌 인/아웃 | −8° / 리드 0.3s / 복귀 0.12s | 매 패턴(노드 3개 이상) | `zoomFovDelta` (`:102`) |
| 쉐이크 | freq 1.6, 카탈로그별 진폭 | 매 판정 | `UpdateShake` (`:792`) |
| FOV 펀치 | 카탈로그별 | 매 판정 | `UpdatePunch` (`:594`) |
| 히트스톱 Brain 정지 | 0.05~0.1s | 매 성공 | `HoldForHitStop` |
| **앵글 교체** | 0.4s Spherical 블렌드 | **6초 = 약 4패턴마다** | `CameraAngleSwitcher.switchInterval` (`CameraAngleSwitcher.cs:42`) |

여기에 플레이어가 매 패턴 3~8m 대시한다(§11-2). **화면이 조용한 프레임이 없다.**

## 현재 구현에서 확인한 사실

### 앵글 교체 (`CameraAngleSwitcher`)
- 3단계다 — 쿨다운(자격) → 패턴 경계(`OnPatternBoundary`, 예약) → 카메라 큐 종료(`Tick`, 발사).
  **임팩트 구간을 덮지 않는 규율은 이미 있다.** 문제는 *언제* 자격이 생기냐다.
- 자격은 **`switchInterval` 6초 타이머 하나**뿐이다(`:70`). 음악과도 무대와도 무관하다.
- 후보는 **현재 것을 뺀 균등 랜덤**(`PickOther`, `:99`). 정반대 앵글이 그대로 뽑힌다 —
  스위처는 앵글이 무엇인지 모르므로(설계상) 각도 조건을 걸 지점이 지금은 없다.
- 앵글 = 각 vcam의 `CinemachineFollow.FollowOffset` 벡터 하나. 그룹이 `RotationMode = Manual`이고
  vcam이 `LockToTarget`이라 **그 오프셋은 전부 같은(플레이어 기준) 좌표계**다 →
  오프셋의 yaw를 재면 앵글끼리 각도 비교가 성립한다. **지금은 아무도 안 읽는다.**

### yaw 추종 (`UpdateFraming` `:725`)
- `targetYaw = 플레이어 yaw`를 `SmoothDampAngle(cameraTurnDamping 0.45)`로 뒤따른다.
- 상대가 무대 반대편으로 바뀌면 **0.45초에 반 바퀴**를 돈다. 감쇠는 "빠른 회전"을 "조금 느린 회전"으로
  바꿀 뿐 **회전이라는 사실 자체는 안 바꾼다** — 사람 눈이 못 따라가는 구간은 그대로 남는다.
- 회전 주체는 `targetGroup.transform.rotation` 하나(`ApplyFraming` `:789`)라 개입 지점이 명확하다.

### 프레이밍 가중치 (`ResolveWeight` `:748`)
- `1 − clamp01((거리 − 3) / (6 − 3))` — **3~6m 사이가 전부 연속값**이다.
- 결투 간격이 1m 남짓이고 다음 표적이 3~8m라, 매 패턴 이 구간을 통과한다 →
  `GroupFraming`이 돌리를 **매 프레임** 다시 계산한다.
- 연속값을 쓴 근거는 §7-2의 "승격 즉시 적이 링(6m)에 있어 이진값이면 확 물러났다 붙는다"인데,
  그 문제는 **히스테리시스로도 똑같이 해결된다**(두 임계가 이미 있다).

### 대시 구간
- `EnemyDirector.OnDuelScheduled`의 `DuelPlan.PlayerArriveTime`이 **대시가 끝나는 시각**이다.
  카메라는 지금 이 이벤트를 구독하지 않는다(구독자는 `PlayerCombatMover`·`DodgeDirector`).
- 대시 중 플레이어가 회전하고(먼저 상대를 보고 달린다) 카메라가 그 yaw를 뒤따르므로,
  **이동 + 회전이 같은 구간에 겹친다.** 화면에는 시차가 없어 속도감 없이 흔들림만 남는다.

### 사용자 옵션
- 지금 있는 토글(`shakeEnabled`/`punchEnabled`/`framingEnabledOption`/`introEnabled`/`zoomEnabled`/
  `switchEnabled`)은 **개발용**이다 — 배선 누락처럼 "그 층만 죽인다"는 규율의 산물.
- 플레이어 옵션은 성격이 다르다(접근성). 루트 Canvas가 `ScreenSpaceOverlay`라
  **카메라 연출을 전부 꺼도 게임이 온전하다** — 옵션 비용이 구조적으로 0이다.

## 제약

- 판정·이펙트·히트스톱은 건드리지 않는다. 카메라는 순수 연출층이고 그 경계를 넘지 않는다(§7-1).
- `Cinemachine` 타입은 `ApplyShake`·`ApplyFraming`·`IntroRoutine` 세 이음매에만 등장한다 —
  새 기능도 그 안에서 끝나야 한다.
- 기존 배선이 빠지면 그 기능만 조용히 비활성된다는 규율을 유지한다.
