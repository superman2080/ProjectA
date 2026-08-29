# Research — 인트로가 "정면을 바라보는" 문제 (CameraIntroBlend)

증상: `IntroCamera`는 스플라인 궤적을 따라 이동하는데 **플레이어를 안 보고 정면을 본다**.

---

## 아니었던 것 (측정으로 배제)

| 의심 | 측정 |
|---|---|
| `CinemachineSplineDollyLookAtTargets`가 안 먹는다 | 스플라인 6개 지점 전부에서 vcam forward와 "카메라→플레이어" 방향의 **dot = 1.000** |
| `LookAt`이 안 꽂혀 있다 | `intro.LookAt` · `Target.LookAtTarget` 모두 플레이어. `State.ReferenceLookAt = (0,1,0)` |
| 룩앳 데이터가 스플라인 앞부분만 덮는다(knot 0·1·2 / 총 7개) | 뒤 구간은 마지막 값으로 클램프되어 **여전히 플레이어를 본다** |
| `Cam_Explore`가 앵글 전환 목록에 섞였다 | `angleSwitcher.cameras`는 `Cam_ShoulderBack`·`Cam_NearLowRight`·`Cam_FarShoulderRight`·`Cam_RightLow` 넷뿐 |
| `Cam_Explore`가 우선순위를 뺏는다 | `PlayerModeDirector.ApplyMode(Combat)`가 0으로 내린다. 실측 `Cam_Explore: 0` |
| `PositionUnits`가 Normalized가 아니다 | Normalized(1). 경고도 안 뜬다 |

**즉 vcam은 처음부터 끝까지 정확히 플레이어를 조준하고 있었다.**

## 진짜 원인 — 블렌드가 인트로를 양쪽에서 먹는다

`IntroRoutine`은 `travel = duration − brain.DefaultBlend.BlendTime`으로 **마무리 블렌드(②)**만 창에서 뺀다. 그런데 우선순위를 올리는 순간 Brain은 게임플레이 vcam에서 인트로로 **들어오는 블렌드(①)**도 만든다. 그동안 화면 구도는 아직 게임플레이 카메라의 것이다 — **스플라인은 달리는데 어깨너머 구도로 정면을 보고 있다**가 그 그림이다.

씬 값 실측:

| 값 | 크기 |
|---|---|
| `countdownDuration` | 3초 |
| `Brain.DefaultBlend`(씬 직렬화) | EaseOut **0.4초** |
| `CameraDirector.angleBlendDuration` | **2초** |

⚠ **씬의 0.4는 런타임에 쓰이지 않는다.** `CameraDirector.ApplyBlendDuration`이 `Awake`에서 `angleBlendDuration`을 `Brain.DefaultBlend.Time`에 **써 넣는다**(의도된 설계 — 진실의 원천을 Brain 하나로 모으기 위해). 그래서 런타임 블렌드는 **2초**다.

프레임 로그(수정 전, 카운트다운 3초):

```
f3  ~ f144 : blend=True   dolly=0.00→0.03   pos=(0,3.5,-6.3)→(0.2,0.0,-2.0)
f147~      : blend=False  dolly=0.03→       pos=(0.2,0.1,-2.0)
```

- ①이 **2초** 도는 동안 카메라는 게임플레이 구도에서 스플라인 시작점으로 서서히 옮겨간다.
- 그런데 스플라인 주행은 `3 − 2 = 1초`짜리라 **①이 끝나기도 전에 끝난다.**
- 곧바로 ②(2초)가 시작된다.

**결과: 인트로 구도를 한 프레임도 못 본다.** `angleBlendDuration`이 0.4였을 때는 ①이 0.4초라 눈에 안 띄어 아무도 이 결함을 몰랐고, 2초로 올린 순간 드러났다.

## 수정 — 인트로에는 컷으로 들어간다

`IntroRoutine`에서 우선순위를 올린 **다음 프레임에 `brain.ActiveBlend = null`**. ①을 없애면:

- 창이 한 번만 깎인다(②만).
- `angleBlendDuration`을 얼마로 두든 인트로 구도가 반드시 보인다.
- 곡 시작은 어차피 `BattleSceneBootstrap`의 페이드가 검은 화면에서 열리므로 **컷이 보이지도 않는다**.

⚠ **한 프레임만 기다리면 안 된다.** Brain이 우선순위 변화를 알아채는 프레임은 호출 시점과 스크립트 실행 순서에 따라 밀린다 — 탐색 씬에서 넘어오는 경로에서는 **3프레임 뒤**에야 블렌드가 생겨서, 한 프레임 뒤에 끊은 것은 *아직 없는 것을 끊는 no-op*이 되고 그 다음 프레임에 생긴 블렌드가 그대로 2초를 다 돌았다. 그래서 **블렌드가 나타날 때까지 최대 `IntroCutGuardFrames`(8) 프레임 동안 지켜본다.**

실측 로그(수정 전, 탐색 씬 → 전투 씬):

```
f42  live=Cam_ShoulderBack blend=False prio=20 dolly=0.00 camY=3.49   ← 우선순위만 올라감(Brain 미반응) = 여기서 끊어 봐야 no-op
f45  live=IntroCamera      blend=True  prio=20 dolly=0.01 camY=3.44   ← 블렌드가 여기서 생김
...  blend=True 가 f114까지 지속. 스플라인은 바닥에서 시작하는데 화면은 어깨너머 높이(y 3.5)에서 출발한다
```

수정 후:

```
f40  live=IntroCamera blend=True  dolly=0.00 camPos=(0.0, 3.5, -6.2)  ← 블렌드 생성
f42  live=IntroCamera blend=False dolly=0.00 camPos=(0.0, 0.0, -2.0)  ← 컷, 스플라인 시작점(바닥)
f132 live=IntroCamera blend=False dolly=1.00 camPos=(0.0, 1.5, 2.0)   ← 전 구간 주행
f134 live=Cam_ShoulderBack blend=True                                  ← 마무리 블렌드
```

### ⚠ 측정 함정 — 에디터가 백그라운드면 프레임이 안 돈다

처음 이 문제를 재현하려 했을 때 로그가 한 줄도 안 나오고 씬 전환도 안 됐다. 원인은 코드가 아니라 **에디터가 포커스를 잃어 21초 동안 2프레임만 돈 것**이었다(`Time.frameCount = 2`, `Time.unscaledTime = 21.04`). 코루틴·`Application.onBeforeRender`가 전부 멈춘다. 자동화로 플레이 모드를 측정할 때는 **`Application.runInBackground = true`를 먼저 켠다.**

수정 후 프레임 로그:

```
f12 : live=IntroCamera blend=True  dolly=0.00 pos=(0.0, 2.9,-6.1)   ← 컷 직전
f15 : live=IntroCamera blend=False dolly=0.01 pos=(0.0, 0.0,-2.0)   ← 스플라인 시작점
...  전 구간 주행 ...
f84 : live=IntroCamera blend=False dolly=1.00 pos=(0.0, 1.5, 2.0)
f87 : live=Cam_ShoulderBack blend=True                              ← ② 시작
```

## 남은 튜닝 (코드 문제 아님)

`angleBlendDuration = 2`면 ②가 카운트다운 3초 중 2초를 먹어 **스플라인 주행이 1초**로 압축된다. 스윕을 길게 보고 싶으면 둘 중 하나다:

- `angleBlendDuration`을 내린다 — ⚠ 이 값은 **게임플레이 앵글 전환 속도와 공유**된다. `CLAUDE.md` §7-5는 0.4초를 근거와 함께 권한다(패턴 주기 1.38초라 1.0초면 73%를 이동에 쓴다).
- `countdownDuration`을 올린다 — 프리웜 창이자 인트로 창이라 늘려서 나쁠 게 없다(하한만 3초로 못박혀 있다).
