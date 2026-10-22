# Research — 고정 앵글 카메라(CameraFixedAngle)

## 문제

전투 카메라가 **플레이어 yaw를 상시 추종**한다. 상대가 무대 반대편으로 바뀌면 플레이어가 0.15초에 돌고
(`PlayerCombatMover.turnDuration`) 카메라가 0.45초에 그 뒤를 따라 돌거나(`cameraTurnDamping`),
남은 각이 90°를 넘으면 그 자리에서 끊긴다(`yawCutThreshold`). 결과적으로 **화면이 곡 내내 돌고 있어
어느 쪽에서 무엇이 오는지 매번 다시 찾아야 한다** = 가시성 저하.

요구는 이렇다:
1. 플레이어는 화면 중앙에 고정.
2. 카메라는 플레이어 회전을 따라 돌지 않는다.
3. 카메라를 **둘**(플레이어 후면 / 전면) 둔다.
4. **활성** 카메라는 최초 고정 위치에서 플레이어만 중앙에 담고 **회전하지 않는다**.
5. **비활성** 카메라는 전면/후면 자리를 계속 따라 움직이고, **활성화되는 순간 그 회전값이 고정**된다.

## 현재 구조 (읽은 것)

### 회전의 출처는 한 곳이다

`CameraDirector.ApplyFraming`(`Camera/CameraDirector.cs:892`)의 한 줄:

```csharp
targetGroup.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
```

- 그룹은 `RotationMode = Manual`(씬 `CameraTargetGroup`, `BattleScene.unity:8695` → `0` = Manual).
- 앵글 vcam들은 전부 `CinemachineFollow.BindingMode = 3`(= `LockToTarget`, 예: `:1207`, `:7718`, `:8942`, `:9950`).
- 그래서 **`FollowOffset`이 그룹 로컬 축으로 해석**되고, 그룹을 돌리면 카메라 궤도가 통째로 따라 돈다.
- `yaw`의 값은 `UpdateFraming`(`:800`~`:834`)이 만든다 — `actionPlayer.transform.eulerAngles.y`를 목표로
  `SmoothDampAngle`, 큰 각은 컷(`yawCutThreshold`) + `angleSwitcher.Arm()`.

**이 줄 말고 그룹 회전을 쓰는 곳이 없다**(프로젝트 전역 grep 확인). 소유자가 하나라 바꾸기 쉽다.

### 플레이어 중앙 고정은 이미 다른 층이 한다

각 앵글 vcam에 `CinemachineRotationComposer`(그룹을 바라보기) + `CinemachineGroupFraming`
(`FramingMode: 2`, `SizeAdjustment: 1` = DollyOnly)이 붙어 있다. **화면 중앙에 담는 일은 이 둘이 하고
그룹 회전과 무관하다.** 즉 요구 1·4의 "중앙 고정"은 **코드 변경이 0**이고, 회전만 떼면 된다.

### 앵글 교체

`CameraAngleSwitcher`(`Camera/CameraAngleSwitcher.cs`)가 vcam 배열의 `Priority`만 갈아끼운다
(`Select`, `:196`). 이동은 Cinemachine 블렌드가 한다(`Brain.DefaultBlend`, 시간은
`CameraDirector.angleBlendDuration`이 `Awake`에 써 넣는다, `:353`).

- 발사 규율: **쿨다운(`switchInterval` 6초) → 상대 교체에 예약(`Arm`) → 카메라 큐 종료 후 발사(`Tick`)**.
- `CacheAngles`(`:85`)가 이미 각 vcam의 `CinemachineFollow`를 `GetComponent`로 집어 `FollowOffset`의
  yaw를 캐시한다 → **오프셋을 쓰는 코드를 새로 만들 필요가 없다. 같은 자리에 쓰기만 추가하면 된다.**
- `0번이 곡 시작 카메라`이고 랜덤이 아니다(`Setup`, `:72`).

### 지금 씬의 앵글 vcam 넷

| 이름 | `FollowOffset` | `PositionDamping` | 비고 |
|---|---|---|---|
| `Cam_RightLow` | (-7, 2, -7) | (0.8, 0.4, 2) | |
| `Cam_NearLowRight` | (7, 2, 7) | — | `RotationComposer.TargetOffset.y = 0` |
| `Cam_ShoulderBack` | (0, 5, -10) | (2, 0.4, 2) | **정후면** — 요구 3의 후면 카메라로 그대로 쓸 수 있다 |
| `Cam_FarShoulderRight` | (6, 3.5, -7) | — | |

전부 `Priority.m_Value = 0`이고 스위처가 `activePriority = 10` / `restingPriority = 0`을 쓴다
(`BattleScene.unity:1620`) → **배열에서 빼기만 하면 그 vcam은 0에 남아 절대 이기지 못한다. 씬에서 지울 필요가 없다.**
인트로는 `-10`(휴지) / `20`(인트로 중), 탐색 카메라는 `30`.

## 해결 경로와 그 근거

### 핵심: 그룹 회전을 고정하면 `LockToTarget`이 그대로 월드 좌표가 된다

그룹 회전을 **항등으로 한 번만** 써 두면 `FollowOffset`의 해석 좌표계가 월드와 같아진다.
→ **vcam의 `BindingMode`를 하나도 안 고치고** "회전하지 않는 카메라"가 성립한다(요구 2·4).

그 상태에서 비활성 카메라만 자리를 따라가게 하려면 **그 카메라의 `FollowOffset`을 매 프레임
플레이어 yaw로 회전시켜 써 주면 된다**(요구 5의 전반). 활성 카메라는 **그냥 안 쓰면 그 값에 굳는다**
— 즉 "활성화 시 회전값 고정"은 **쓰기를 멈추는 것**이 구현의 전부다(새 상태 필드가 0개).

즉 `FollowOffset`의 base 값은 **플레이어 기준**으로 저작되고(현 씬 값이 이미 그렇다 — `(0,5,-10)` = 등 뒤),
런타임이 `Quaternion.Euler(0, playerYaw, 0) * base`로 월드 오프셋을 만든다.

### ⚠ 함정 둘 (실제로 증상이 난다)

1. **`PositionDamping`이 "고정"을 깬다.** `Cam_ShoulderBack`은 (2, 0.4, 2)다. 비활성 추종에 감쇠가 걸려
   있으면 활성화 순간 그 카메라는 아직 목표에 못 닿은 상태이고, 활성화 뒤 쓰기를 멈추면 **남은 오차를
   2초 가까이 따라가며 플레이어 주위를 미끄러진다** = "고정"이 아니다.
   → `Select()`에서 그 vcam의 `PreviousStateIsValid = false`로 **감쇠를 한 번 건너뛰게** 한다
   (블렌드가 아직 시작 전이라 스냅 자체는 화면에 안 보이고, 전환의 부드러움은 블렌드가 담당한다).
2. **블렌드 중에 오프셋을 계속 쓰면 블렌드의 양 끝이 움직인다.** 나가는 쪽(방금 비활성이 된 카메라)이
   다시 추종을 시작하면 블렌드 소스가 흐른다 → 화면이 한 번 휘청인다.
   → `brain.IsBlending` 동안 오프셋 쓰기를 멈춘다. 끝나면 보이지 않는 곳에서 제 자리로 돌아간다.

### 죽는 노브 (제거하지 않고 분기 밖으로 둔다)

고정 모드에서 `cameraTurnDamping` · `yawCutThreshold`는 쓰이지 않는다. **지우지 않는다** —
`optionCameraMotion`처럼 끌 수 있는 기능이고, 예전 동작(추종)으로 되돌릴 경로를 남기는 것이
이 프로젝트의 기존 규율(배선이 빠진 기능만 조용히 비활성)과 같다.

### 각도 밴드는 2대에서 자동으로 물러난다

후면/전면은 180° 차이라 `minSwitchAngle 30 ~ maxSwitchAngle 120` 밴드에 후보가 없다
→ `PickOther`(`:168`)의 폴백(아무나)이 돈다. 결과는 맞지만 **의도를 명시하려면 `maxSwitchAngle = 180`**
으로 두는 편이 낫다(그 값 이상이면 밴드 검사 자체가 꺼진다).

## 범위 밖

- 씬의 `RotationComposer.TargetOffset.y`가 vcam마다 다르다(1.5 / 0 / 2.5 / 1.5). CLAUDE.md §7-2는
  `GroupFraming.CenterOffset`과 짝을 맞추라고 하는데 씬이 표류해 있다 — **이번 작업과 독립**이라 안 건드린다.
- 쉐이크·펀치·줌·인트로·히트스톱 잠금: 전부 채널이 달라 영향 없음.
- 탐색 카메라(`explorePriority: 30`)는 이 스위처 밖이다.
