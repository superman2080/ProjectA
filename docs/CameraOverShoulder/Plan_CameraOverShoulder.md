# Plan — CameraOverShoulder

근거: `docs/CameraOverShoulder/Research_CameraOverShoulder.md`

## 목표

**카메라가 플레이어 등 뒤에서 플레이어 로테이션을 따라가되, 교전 상대는 지금처럼 한 화면에 담긴다.**

기존 프레이밍(거리 기반 가중치)·쉐이크·인트로는 **하나도 건드리지 않는다.** 이번에 새로 생기는 것은 **카메라 궤도의 방향** 하나뿐이다.

---

## 설계 결정

### D-1. 그룹 transform의 **yaw**를 몬다 — 새 컴포넌트도 새 vcam도 없다

Research 2. 씬은 이미 `BindingMode = LockToTarget` + `RotationMode = Manual`이다. 이 조합에서 `FollowOffset`은 **그룹 GameObject의 로컬 축**으로 해석되고, 그룹 회전은 그 오브젝트의 `transform.rotation`이다 — 지금은 identity로 방치돼 있다.

그러니 **`targetGroup.transform.rotation`에 플레이어 yaw를 써 넣기만 하면** 카메라 궤도 전체가 플레이어 등 뒤로 돈다.

- `GroupFraming`은 그대로 산다(바운드→돌리는 회전과 무관한 축).
- `RotationComposer`도 그대로 산다(그룹을 *바라보는* 일이라 궤도 방향과 무관).
- 쉐이크·인트로 무관(Research 4-3).

**확정.**

### D-2. **yaw만** 가져온다

`Quaternion.Euler(0, playerYaw, 0)`. 플레이어는 `LookRotation(planar)`로만 회전해 지금은 pitch/roll이 0이지만(Research 3-2), 훗날 피격 리액션 등으로 기울면 **카메라 궤도가 지면을 뚫거나 하늘로 솟는다.** 회전을 통째로 복사할 이유가 없다.

**확정.**

### D-3. ⚠ 플레이어의 `turnDuration`을 복사하지 않는다 — **카메라 전용 감쇠**

이번 설계의 **유일한 실질 위험**이다(Research 3-2).

플레이어는 상대가 바뀌면 **0.15초 만에** 최대 180° 돈다. 그건 캐릭터가 "먼저 상대를 보고 그 다음에 달린다"는 의도된 날카로움이다. 그러나 카메라가 그 각속도를 그대로 복사하면 **화면 전체가 0.15초에 반 바퀴 돈다** — 읽을 수 없고 멀미난다.

그래서 카메라는 **자기 시간상수로 뒤따른다**:

```
yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, cameraTurnDamping)
```

- `cameraTurnDamping` 기본 **0.45초** (플레이어 0.15초의 3배)
- `SmoothDampAngle` — 각도 랩어라운드를 알아서 처리하고, 속도를 들고 있어 목표가 도중에 또 바뀌어도 이어진다. 180° 전환이 몰리는 이 게임에서 `LerpAngle` 지수감쇠보다 정확하다.
- 0 이하면 즉시 스냅(감쇠 끄기).

**"플레이어가 먼저 돌고 카메라가 따라붙는다"**는 이 지연 자체가 연출이다 — 캐릭터의 전환이 화면에서 읽힌다.

**확정.**

### D-4. 소유자는 `CameraDirector`, 쓰기 지점은 `ApplyFraming`

CLAUDE.md가 규정한 "카메라 연출의 유일 관리 지점"이고 필요한 참조(`actionPlayer`, `targetGroup`)를 **이미 전부 들고 있다.**

봉인 규율도 유지된다 — 그룹 transform 쓰기는 **`ApplyFraming` 안**에서 한다. Cinemachine이 등장하는 세 이음매(`ApplyShake`/`ApplyFraming`/`IntroRoutine`)는 그대로다. 위층(`UpdateFraming`)은 각도 계산만 하고 Cinemachine을 모른다.

`framingEnabled`가 false면 회전도 쓰지 않는다 — **기능 하나가 배선 없이 죽으면 나머지는 산다**는 기존 규율 그대로.

**확정.**

### D-5. 씬 `FollowOffset` 재조정 — 부감 롱샷 → 어깨너머

Research 4-2. 현재 (0, 10, −25)는 높이 10m·거리 25m다. 이 오프셋이 회전하기 시작하면 카메라가 **반경 25m의 원**을 그려, 같은 각속도에서도 선속도가 과하다.

| 항목 | 현재 | 제안 |
|---|---|---|
| `FollowOffset` | (0, 10, −25) | **(0, 2.6, −5.5)** |
| `GroupFraming.DollyRange` | (−4, +6) | **(−2, +4)** |
| vcam Lens FOV | 25 | **40** |

FOV 25는 25m 거리를 전제로 잡힌 망원값이다. 거리를 5.5m로 줄이면 화각이 그대로일 때 화면에 인물 얼굴만 남는다. **거리를 줄이면 화각을 넓혀야 같은 크기로 담긴다.**

⚠ FOV를 넓히면 `SizeAdjustment = DollyOnly`의 근거("망원 구도라 Zoom이면 원근 왜곡")가 약해지지만, **결정은 유지한다** — 돌리는 여전히 원근을 보존하고, 어깨너머 구도에서 FOV가 흔들리면 왜곡이 더 눈에 띈다.

**이 세 값은 최종값이 아니라 출발점이다.** 씬 값이라 플레이하며 튜닝하는 게 정상 경로다. 코드는 이 값을 하나도 모른다.

**확정.**

### D-6. `CLAUDE.md` §7-2의 "`BindingMode`는 반드시 `WorldSpace`" 규칙 **폐기**

Research 2. 그 규칙의 근거("그룹 회전은 멤버 배치에서 파생")는 `RotationMode = GroupAverage`일 때만 참인데 실제 그룹은 `Manual`이다 — **진단이 틀렸고, 처방이 씬에 적용되지 않아 아무 일도 없었다.**

이제는 정반대 요구가 됐다. 규칙을 **"`RotationMode = Manual` + `BindingMode = LockToTarget`. 그룹 회전은 `CameraDirector`가 플레이어 yaw로 몬다. `GroupAverage`로 바꾸면 회전이 멤버 배치에서 파생돼 구도가 적을 따라 돈다"**로 갱신한다.

`docs/CameraFraming/Plan_CameraFraming.md` D-4에도 폐기 표시를 남긴다 — 뒤에 읽는 사람이 두 문서에서 반대 지시를 보고 헤매지 않도록.

**확정.**

---

## 구현 단계

- [x] **Step 1 — `CameraDirector`에 필드 추가**
  `[Header("Framing")]` 블록 끝에 `cameraTurnDamping`(기본 0.45f) 하나. 툴팁에 "플레이어 `turnDuration`(0.15초)보다 길어야 한다 — 같으면 화면이 0.15초에 반 바퀴 돈다"를 적는다. 상태 필드 `cameraYaw`, `cameraYawVelocity` 추가.

- [x] **Step 2 — `SetupFraming`에서 초기 yaw 동기**
  `framingEnabled = true` 직전에 `cameraYaw = actionPlayer.transform.eulerAngles.y`, `cameraYawVelocity = 0f`. 첫 프레임에 0°에서 감쇠가 시작돼 카메라가 한 바퀴 도는 것을 막는다.

- [x] **Step 3 — `UpdateFraming`에서 yaw 감쇠 계산**
  가중치 감쇠 바로 뒤에 `SmoothDampAngle`(D-3). `cameraTurnDamping <= 0`이면 즉시 대입. 계산된 yaw를 `ApplyFraming`에 셋째 인자로 넘긴다.

- [x] **Step 4 — `ApplyFraming`이 그룹 회전을 쓴다**
  시그니처를 `ApplyFraming(Transform opponent, float weight, float yaw)`로 넓히고, 기존 슬롯 갱신 뒤 `targetGroup.transform.rotation = Quaternion.Euler(0f, yaw, 0f)`. 조기 반환 가드(`Targets.Count < 2`)는 회전 **뒤로** 옮기지 않는다 — 그룹이 덜 세워졌으면 회전도 의미 없다.

- [x] **Step 5 — 클래스 XML 주석 갱신**
  "프레이밍"이 이제 **무엇을 담을지 + 어느 방향에서 담을지** 둘을 뜻함을 적는다. D-3의 위험(플레이어 0.15초 회전을 복사하면 안 되는 이유)을 주석으로 남긴다 — 나중에 "감쇠 왜 이렇게 느리지"에 대한 답.

- [x] **Step 6 — 씬 값 조정 (D-5)**
  `CinemachineFollow.FollowOffset` → (0, 2.6, −5.5), `CinemachineGroupFraming.DollyRange` → (−2, +4), Lens FOV → 40. `BindingMode`는 손대지 않았다(`LockToTarget` 유지 확인). `BattleScene.unity` 저장 완료.

- [x] **Step 7 — 문서 갱신 (D-6)**
  `CLAUDE.md` §7-2 프레이밍 문단 교체(회전 모델·감쇠 경고·`GroupCenter` 단서 추가), `docs/CameraFraming/Plan_CameraFraming.md` D-4에 폐기 블록 추가.

- [x] **Step 8a — 정적 검증 (완료)**
  - 컴파일 에러 0 (`read_console` error/warning: MCP 웹소켓 알림 1건뿐).
  - 리플렉션 확인: `cameraTurnDamping` 필드 존재, `ApplyFraming` 인자 3개.
  - 씬 인스펙터 확인: `cameraTurnDamping = 0.45`, `targetGroup`·`actionPlayer` 배선 살아 있음.

- [ ] **Step 8b — 플레이 검증 (사용자 확인 필요)**
  1. 플레이어가 상대를 향해 돌면 카메라가 **뒤따라** 등 뒤로 온다(즉시 아님).
  2. 상대가 무대 반대편으로 바뀌어 180° 전환이 나도 화면이 휙 돌지 않는다.
  3. 상대가 `dropoffDistance`(6m) 밖이면 화면에 플레이어만 남는다(기존 동작 유지).
  4. 쉐이크·인트로가 그대로 동작한다.

  **튜닝 지점**: 회전이 굼뜨면 `cameraTurnDamping`↓, 멀미나면 ↑. 구도가 멀/가까우면 `FollowOffset.z`, 인물이 작/크면 Lens FOV. 어깨너머 편심은 `FollowOffset.x`(코드 수정 불필요).

---

## 범위 밖 (하지 않는다)

- 어깨너머 좌우 오프셋(over-the-shoulder 편심). `FollowOffset.x`로 언제든 씬에서 줄 수 있다 — 코드가 필요 없다.
- 충돌 회피(`CinemachineDeoccluder`). 무대에 가릴 지오메트리가 없다.
- 마우스/스틱 카메라 조작. 이 게임의 입력은 패턴인풋 하나다.
