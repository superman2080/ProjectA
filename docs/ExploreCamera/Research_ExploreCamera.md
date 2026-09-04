# Research — 탐색 씬 카메라 워킹(마우스 시점 회전)

## 1. 지금 있는 것

| 조각 | 자리 | 상태 |
|---|---|---|
| `Explore/Look` 액션 | `Assets/IngameInputs.inputactions` | **이미 있다** — `<Mouse>/delta` + `<Gamepad>/rightStick` |
| `InputHandler.LookInput` | `Input/InputHandler.cs:62` | **이미 있다.** ⚠ **구독자가 0이다** — 아무도 안 읽는다 |
| 카메라 기준 이동 | `PlayerExploreMover.ResolveDirection` | **이미 있다** — `cameraTransform.eulerAngles.y`로 입력을 회전시킨다 |
| 탐색 vcam | `Explore_Stage1.unity`의 `Cam_Explore` | `CinemachineCamera`(Priority 30) + `CinemachineFollow`(BindingMode 4 = WorldSpace, offset 0/2.2/−4.5) + `CinemachineRotationComposer`(TargetOffset y 1.5, Damping 0.5) |
| 모드 배타성 | `PlayerModeDirector.ApplyMode` | 무버 `enabled` · `InputHandler.SetMode` · `exploreCamera.Priority`를 **무조건 대입** |
| 커서 | — | **아무도 안 건드린다**(`Cursor.lockState` 검색 결과 0건) |

## 2. 그래서 실제로 없는 것은 셋뿐이다

1. **커서 잠금·숨김** — 코드베이스 어디에도 `Cursor` 호출이 없다.
2. **`LookInput` → 카메라 회전** — 값은 나오는데 읽는 쪽이 없다.
3. **회전 가능한 리그** — `CinemachineFollow`는 오프셋이 고정이라 궤도 회전이 표현 불가능하다. `CinemachineOrbitalFollow`가 그 자리다(수평·수직 축을 `InputAxis`로 들고 있어 값만 밀면 된다).

이동 쪽은 **이미 카메라 yaw 기준**이라 손댈 것이 없다 — 카메라가 돌기 시작하면 그 규칙이 자동으로 "카메라 보는 쪽으로 걷는다"가 된다. `cameraTransform` 배선이 실제 렌더링 카메라(Main Camera/Brain)를 가리키는지만 확인하면 된다.

## 3. 입력을 누가 미는가 — 두 갈래

**(A) `CinemachineInputAxisController`(패키지 기본)**
- 코드 0줄. 대신 `InputActionReference`로 `Explore/Look`을 직접 참조한다.
- ⚠ **입력 출처가 둘이 된다.** `InputHandler`는 `new IngameInputs()`로 **자기 인스턴스**를 만들어 쓰므로, 컨트롤러가 참조하는 에셋 액션은 **다른 인스턴스**다 → `InputHandler.SetMode(Combat)`이 그것을 못 끈다. 전투 중 마우스로 탐색 카메라가 도는 상태가 표현 가능해지고, 막으려면 `PlayerModeDirector`에 "컨트롤러 enabled" 노브를 하나 더 단다.
- `InputHandler` 헤더의 **"게임플레이 입력의 유일한 출처"**와 정면으로 어긋난다.

**(B) `PlayerExploreMover`가 `LookInput`을 읽어 `OrbitalFollow`의 축 값을 민다** ← 채택
- 새 파일 0개 · 새 노브 0개 · 배선 1개(vcam 참조).
- **모드 가드가 공짜다** — 이 컴포넌트는 탐색에서만 `enabled`이고, 그것도 모자라 맵이 꺼지면 `LookInput`이 0이다(이중 안전).
- 위치와 시점이 같은 컴포넌트에 사는 것이 오히려 맞다 — 둘 다 "탐색 중 플레이어 조작"이고 수명이 완전히 같다.

## 4. 함정

- **⚠ 마우스 델타에 `Time.deltaTime`을 곱하면 안 된다.** `<Mouse>/delta`는 이미 그 프레임에 움직인 픽셀 수라, 곱하면 프레임레이트에 따라 감도가 달라진다. 스틱(축 값)은 반대로 곱해야 맞다 — 지금은 마우스 기준으로만 만들고 그 한계를 주석으로 남긴다.
- **⚠ `Time.timeScale`을 쓰지 않는다**(§7-3) — 이 작업은 시계를 안 건드린다.
- **⚠ 커서는 전투에서 반드시 풀려 있어야 한다.** 패턴인풋을 마우스로 이어 긋는 것이 이 게임의 주 조작이다(§1). 잠그는 것은 `Explore` 하나뿐.
- **⚠ 에디터에서 잠긴 커서는 Esc로 풀린다**(Unity 기본). 별도 탈출구를 만들지 않는다.
- `OrbitalFollow`의 리센터링(Recentering)은 꺼 둔다 — 켜면 손을 떼는 순간 카메라가 제 마음대로 돌아가 "내가 본 쪽"이 유지되지 않는다.
- `RotationComposer`는 그대로 둔다. 수직 축이 카메라를 위아래로 옮기면 컴포저가 플레이어를 계속 화면에 잡아 주므로 **피치 코드가 0줄**이다.
