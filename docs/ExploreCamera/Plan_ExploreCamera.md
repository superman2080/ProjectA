# Plan — 탐색 씬 카메라 워킹(마우스 시점 회전)

목표 셋: ① 탐색 중 커서 잠금·숨김 ② 마우스로 플레이어를 축 삼아 카메라 궤도 회전 ③ 키보드 이동이 그 카메라 방향을 따른다.

③은 이미 구현돼 있다(`PlayerExploreMover.ResolveDirection`) — **배선 확인만 한다.**

---

- [x] **Step 1 — 커서를 `PlayerModeDirector.ApplyMode`의 노브로 추가**

`ApplyMode`에 두 줄. 이 클래스의 규율(**모든 전이에서 모든 노브를 무조건 대입**)에 그대로 얹는다.

```csharp
// 잠그는 것은 탐색 하나뿐이다 - 전투는 패턴인풋을 마우스로 이어 긋는다(§1).
Cursor.lockState = explore ? CursorLockMode.Locked : CursorLockMode.None;
Cursor.visible = !explore;
```

⚠ `Awake`가 `ApplyMode(Combat)`을 부르므로 **아무것도 안 하는 씬은 커서가 예전 그대로**다(회귀 0).
⚠ 조건 대입만 쓴다(`if (explore) 잠그기` 금지) — 반대 전이에서 푸는 것을 빠뜨리면 곡 중에 커서가 사라진다.

- [x] **Step 2 — `PlayerExploreMover`에 시점 회전 추가**

필드 셋과 `Update` 말미 한 블록.

```csharp
[Header("Look")]
[Tooltip("마우스 시점으로 돌릴 탐색 vcam의 궤도. 비우면 시점 회전이 통째로 비활성된다.")]
[SerializeField] private CinemachineOrbitalFollow orbit;

[Tooltip("마우스 픽셀당 회전 각도. 마우스 델타는 이미 프레임 단위 이동량이라 deltaTime을 곱하지 않는다.")]
[SerializeField] private float lookSensitivity = 0.15f;

[SerializeField] private bool invertY = false;
```

```csharp
// ponytail: 마우스(<Mouse>/delta) 기준 감도. 스틱은 축 값이라 deltaTime을 곱해야 맞는데,
// 지금 탐색은 마우스 전용이다. 패드를 실제로 지원할 때 장치별로 가른다.
private void TickLook()
{
    if (orbit == null) return;

    Vector2 look = inputHandler.LookInput * lookSensitivity;
    if (look.sqrMagnitude < 1e-6f) return;

    orbit.HorizontalAxis.Value += look.x;
    orbit.VerticalAxis.Value += invertY ? look.y : -look.y;
    orbit.HorizontalAxis.Validate();
    orbit.VerticalAxis.Validate();
}
```

- 위치 대입(`transform.position +=`)과 **같은 프레임 안**이라 이동이 즉시 새 yaw를 따른다.
- 모드 가드가 없다 — 이 컴포넌트는 탐색에서만 `enabled`이고, 그것도 모자라 맵이 꺼지면 `LookInput`이 0이다.
- `Validate()`가 축의 범위·랩어라운드를 적용한다(수평 −180~180 wrap / 수직 클램프).

- [x] **Step 3 — `Cam_Explore`의 `CinemachineFollow`를 `CinemachineOrbitalFollow`로 교체**

씬 작업(`Explore_Stage1.unity`). `CinemachineRotationComposer`는 **그대로 둔다** — 수직 축이 카메라를 올리고 내리면 컴포저가 플레이어를 화면에 잡아 줘서 피치 코드가 0줄이 된다.

| 항목 | 값 | 근거 |
|---|---|---|
| `OrbitalStyle` | `Sphere` | 궤도 하나면 충분하다. 3-Ring은 저작 필드가 늘 뿐 |
| `Radius` | 4.5 | 지금 `FollowOffset.z = −4.5` 그대로 |
| `HorizontalAxis` | Range −180~180, **Wrap on**, Recentering **off** | 무한 회전 |
| `VerticalAxis` | Range −10~60, Wrap off, Recentering **off**, 초기값 20 | 땅 밑으로 안 들어가고 정수리만 보지 않는다 |
| `TrackerSettings.PositionDamping` | (1,1,1) 유지 | 지금 값 |

⚠ **리센터링을 켜지 않는다** — 손을 떼면 카메라가 제 마음대로 돌아가 "내가 본 쪽"이 안 남는다.

- [x] **Step 4 — `PlayerExploreMover` 배선 확인**

- `cameraTransform` → **Main Camera**(Brain). `Cam_Explore`를 꽂아도 대개 같은 값이지만, 블렌드 중에는 화면에 보이는 것이 Brain 쪽이다.
- `orbit` → `Cam_Explore`의 `CinemachineOrbitalFollow`.
- `inputHandler` → 씬의 `InputHandler`.
- `PlayerModeDirector.exploreCamera` → `Cam_Explore`(이미 배선돼 있으면 그대로).

- [ ] **Step 5 — 확인**

`Explore_Stage1`을 열고 재생 → `PlayerModeDirector` 컨텍스트 메뉴 `Set Mode / Explore`.

1. 커서가 사라지고 화면 밖으로 안 나간다. Esc로 풀린다.
2. 마우스를 좌우로 → 카메라가 플레이어를 중심으로 돈다. 위아래는 −10~60에서 멈춘다.
3. 카메라를 뒤로 돌린 뒤 W → **화면 위쪽**으로 걸어가고 캐릭터가 그쪽을 본다.
4. `Set Mode / Combat` → 커서가 다시 보이고, 마우스를 흔들어도 카메라가 안 돈다.
5. 콘솔에 `[PlayerModeDirector]` 에러가 없다(무버 둘이 동시에 켜진 상태).

⚠ 유닛테스트를 만들지 않는다 — 새 순수 계산이 0개다(각도 누적·클램프는 Cinemachine의 `InputAxis`가 든다).

---

- [x] **Step 6 — 카메라 디오클루더(추가 요청)**

`Cam_Explore`에 `CinemachineDeoccluder`. **코드 0줄** — 벽에 카메라가 박히거나 통과하는 것은 Cinemachine이 이미 푸는 문제다.

| 항목 | 값 | 근거 |
|---|---|---|
| `CollideAgainst` | `Default` | 씬의 벽·바닥이 그 레이어다. `SlicePiece`(8)·`AmbushOutline`(9)·`Silhouette`(10)은 전투 전용이라 넣지 않는다 |
| `IgnoreTag` | `Player` | ⚠ **이게 없으면 카메라가 플레이어에게 빨려 든다** — 스피어캐스트가 `LookAt` 지점(= 플레이어 캡슐 **안**)에서 출발해 거리 0으로 즉시 히트한다. 이를 위해 `Player` 오브젝트에 `Player` 태그를 붙였다 |
| `CameraRadius` | 0.3 | 근평면이 벽을 뚫기 전에 먼저 걸리게 |
| `MinimumDistanceFromTarget` | 0.8 | 벽에 붙어도 머리 속으로 안 들어간다 |
| `DampingWhenOccluded` | 0 | 가려질 때는 즉시 당긴다(늦으면 그 프레임 동안 벽이 화면을 덮는다). 풀려날 때만 `Damping` 0.4로 부드럽게 |
| `SmoothingTime` | 0.2 | 모서리에서 앞뒤로 떠는 것 방지 |

⚠ **디오클루더는 콜라이더가 있는 것만 피한다.** 지금 `Explore_Stage1`의 콜라이더는 셋뿐이다(Ground `MeshCollider` · `Encounter` 트리거 `BoxCollider` · 플레이어 캡슐). 벽·프롭에 콜라이더를 붙이기 전까지는 **바닥 관통 방지**만 실제로 동작한다.

⚠ `ShotQualityEvaluation`은 껐다 — 그것은 vcam 여럿 중 고르는 기능이라 탐색 vcam 하나짜리 구성에서는 계산만 늘린다.

---

- [x] **Step 7 — 탐색 걷기/달리기 클립(추가 요청)**

`LShift`를 누르면 `Run.anim`, 아니면 `Walk.anim`. **새 스테이트도 새 파라미터도 만들지 않는다** — Sprint 스테이트의 클립만 갈아 끼운다(백스텝이 `Quickshift` 클립만 갈아 끼우는 §11-2의 관용구 그대로).

1. `IngameInputs.inputactions` — Explore 맵에 `Sprint` 액션(`<Keyboard>/leftShift` + `<Gamepad>/leftStickPress`).
2. `InputHandler.SprintHeld` — `Interact`와 같은 `FindAction` 관용구(래퍼 재생성에 컴파일이 안 걸린다). **이벤트가 아니라 상태**로 낸다 — "눌린 동안"이 곧 상태라 누름/뗌 두 이벤트로 재조립하면 모드 전환 도중 놓친 뗌이 굳는다. 맵이 꺼지면 저절로 `false`다.
3. `CharacterActionPlayer` — `sprintClip`(교체 키) · `exploreWalkClip` · `exploreRunClip` · 클립별 기준 속도 둘. `ApplySprintClip`은 `ApplyQuickshiftClip`의 사본이다.
4. `PlayerExploreMover.sprintSpeedMultiplier`(1.75) — 클립만 바꾸고 속도를 그대로 두면 달리기가 슬로모션으로 보인다. **1을 넣으면 속도는 그대로**다.

⚠ **결투 대시가 같은 스테이트를 쓴다.** 그래서 전투의 Sprint 분기에서 `ApplySprintClip(sprintClip)`으로 **매번 되돌린다** — 안 되돌리면 탐색을 다녀온 뒤 결투 대시가 걷기 클립으로 나온다(`ApplyQuickshiftClip`이 "양쪽 분기에서 매번 대입"하는 것과 같은 이유).

⚠ 배선은 `Explore_Stage1`의 Player **인스턴스**에만 했다. `sprintClip`이 비면 `ApplySprintClip`이 조기 반환하므로 `BattleScene`은 회귀가 없다.

기준 속도 기본값은 걷기 1.6 / 달리기 4.5다 — 발이 지면을 긁으면(다리가 이동보다 빠르면) 이 값을 올린다.
