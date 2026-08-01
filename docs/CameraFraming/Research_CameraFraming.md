# Research — CameraFraming (적이 있으면 함께, 없으면 플레이어만)

## 1. 요구

- 현재 교전 상대(`EnemyDirector.currentOpponent`)가 있으면 **플레이어와 적을 한 화면에** 담는다.
- 없으면 **플레이어만** 비춘다.

## 2. 현재 카메라 구성 (`Assets/01. Scenes/DefaultScene.unity`)

Cinemachine **3.1.7** (`Packages/manifest.json:11`). 씬에 vcam은 **하나**뿐이다.

| 오브젝트 | 컴포넌트 | 설정 |
|---|---|---|
| `Main Camera` | `CinemachineBrain` | — |
| `CinemachineCamera` | `CinemachineCamera` | `TrackingTarget` = 플레이어 Transform(`&6328205791487977058`, 프리팹 인스턴스). `LookAtTarget` 비움 → 추적 대상을 그대로 본다. `FieldOfView` 25 |
| ⤷ | `CinemachineFollow` | `FollowOffset` (0, 4, −10), `BindingMode` 3(=`LockToTarget`), `PositionDamping` (1,1,1) |
| ⤷ | `CinemachineRotationComposer` | `TargetOffset` (0,1,0), `Damping` (0.5,0.5), DeadZone 0.1, HardLimits (0.8,0.3) |
| ⤷ | `CinemachineBasicMultiChannelPerlin` | `AmplitudeGain` 0.1 (상시), `FrequencyGain` 0 |

**핵심**: 지금은 **플레이어 한 점만 추적**한다. 적은 구도에 전혀 관여하지 않는다.

### 확인된 사실 둘

- **`TrackingTarget`은 `EnemyDirector.arenaCenter`와 같은 Transform이다**(`:3668`이 동일 fileID 참조). 즉 카메라 추적점과 아레나 기준점이 이미 한 오브젝트다.
- 씬에 `CameraCenter`라는 빈 오브젝트(`&1136364600`, 회전 x10°)가 있지만 **vcam이 참조하지 않는다.** 죽은 배선으로 보인다.

## 3. 관련 코드

### `CameraDirector` (`Assets/02. Scripts/Camera/CameraDirector.cs`)

CLAUDE.md가 규정한 **카메라 연출의 유일 관리 지점**. 현재는 쉐이크만 한다.

- 이미 보유한 참조: `handler`, `actionPlayer`(→ **플레이어 Transform을 여기서 얻을 수 있다**), `enemyDirector`, `perlin`
- 이미 구독 중: `OnPatternComplete`, `OnAllPatternsCleared`, `OnPlayerHit`, `OnEnemyKilled`
- 클래스 주석에 명시된 봉인 규율: *"Cinemachine 타입이 등장하는 곳도 여기(`ApplyShake`)뿐이라, 나중에 Impulse로 갈아끼울 때 위층은 그대로 둘 수 있다."*

### `EnemyDirector` (`Assets/02. Scripts/Enemy/EnemyDirector.cs`)

- `public event Action<EnemyView, EnemyView> OnOpponentChanged` (`:106`) — `(previous, current)`. `PromoteOpponent`(`:419`)에서만 발행.
- **`CurrentOpponent`를 노출하는 공개 프로퍼티가 없다.** `currentOpponent`는 private(`:210` 부근).
- 교전 상대 교체 흐름: `ExecuteKill` → `PromoteOpponent` → 이벤트. **처치 즉시 다음 상대로 넘어간다.**
- `OnDuelScheduled(DuelPlan)` (`:132`) — 플레이어/적의 **목표 위치와 도착 시각**을 준다.

## 4. 제약 · 위험

### 4-1. 승격 직후 적은 멀리 있다

`PromoteOpponent`는 처치 순간 즉시 다음 상대를 세운다. 그 적은 아직 **링(반경 6m)** 위에 있고, `StageOnDeck`이 미리 들여보냈어도 `stagingDistance`(2m)다. 즉 **교전 상대가 6m 밖인 구간이 실재한다.**

그대로 두 대상을 담으면 카메라가 확 물러났다가 적이 다가오며 다시 붙는다. **이 구간을 어떻게 다룰지가 이 작업의 유일한 실질적 설계 결정이다.**

### 4-2. `BindingMode` 3 = `LockToTarget`

추적 대상의 **회전을 그대로 따른다**. 추적 대상을 `CinemachineTargetGroup`으로 바꾸면 그룹의 회전(멤버에서 파생)을 따르게 되어 구도가 돌 수 있다. 그룹 추적으로 전환하면 이 값을 손봐야 한다.

### 4-3. 쉐이크와의 공존

`ApplyShake`는 `perlin`만 건드린다. 프레이밍은 `CinemachineFollow`/`GroupFraming` 쪽이라 **채널이 겹치지 않는다.** 상호 간섭 없음.

### 4-4. 시체

`ExecuteKill`은 죽은 적을 시체로 교체하고 **곧바로** 다음 상대를 승격한다. 프레이밍이 시체를 담을 일은 없다(그룹 멤버는 교전 상대만).

## 4-5. 곡 시작 전 구간 — **이미 존재한다**

`ChartPlayer.PlayRoutine`(`Assets/02. Scripts/ChartGen/ChartPlayer.cs:116`):

```csharp
enemyDirector?.PrepareStage();          // :128  프리웜(적 스폰 포함)

if (countdownDuration > 0f)             // :130  기본 3초
    yield return new WaitForSeconds(countdownDuration);

audioSource.clip = ActiveChart.song;
audioSource.Play();                     // :135
```

- **`countdownDuration`(기본 3초)이 곧 인트로 창이다.** 새 대기 구간을 만들 필요가 없다. 지금은 하한이 없어 0까지 내려간다.
- `PrepareStage()`가 대기 **전에** 끝나므로 인트로 동안 적은 이미 링에 서 있다.
- 이 구간의 존재 이유는 원래 프리웜이다(주석: *"곡 도중에는 Instantiate가 한 번도 일어나면 안 된다"*). 인트로 연출은 그 창을 **덤으로 쓰는 것**이지 창을 늘리는 게 아니다.
- **외부에 알리는 이벤트가 없다.** `OnSongEnded`(`:22`)만 있다. 카운트다운 시작을 알릴 이벤트를 새로 내야 한다.
- `Play()`는 `playOnStart`(기본 false) 또는 외부 호출로 들어온다.

## 5. Cinemachine 3에서의 표준 해법

`CinemachineTargetGroup`(가중치 있는 멤버 목록, 바운딩 구를 산출) + `CinemachineGroupFraming` 익스텐션(그룹이 화면에 들어오도록 거리 또는 FOV 조절).

- 멤버 **가중치 0**은 바운드에서 제외된다 → "적 없음"이 그룹 멤버 제거 없이 표현된다.
- `GroupFraming.FramingMode`: `Dolly`(거리 조절) / `Zoom`(FOV 조절) / `Both`.
- 대안인 **vcam 2대 + Priority 블렌드**는 씬 오브젝트가 늘고 블렌드가 컷처럼 느껴져 이 요구에는 과하다.

### 5-1. 인트로 — 스플라인 경로 + vcam 전환

인트로는 **경로를 직접 저작한다**는 요구가 있으므로 `CinemachineSplineDolly`를 쓴다.

- `com.unity.splines` **2.0.0**이 이미 들어와 있다(`Packages/packages-lock.json:96`, Cinemachine 의존성). 패키지 추가 불필요.
- `CinemachineSplineDolly`(Body)가 vcam을 `SplineContainer` 위에 올린다. 진행도는 `CameraPosition` 필드 하나이고, 단위는 `PositionUnits`(`Distance` / `Normalized` / `Knot`)가 정한다.
  → **`Normalized`면 스플라인 길이·노트 수와 무관하게 0→1이 항상 전체 경로**다. 경로를 다시 그려도 코드가 안 바뀐다.
- 스플라인 자체(`SplineContainer`)는 **씬 오브젝트**다. 씬 뷰에서 노트를 찍어 편집한다 — 코드에 좌표가 들어가지 않는다.

경로 끝에서 게임플레이 구도로 **고정**되는 부분은 `CinemachineBrain`이 맡는다. 활성 vcam이 바뀌면 자동으로 보간하므로, 인트로 vcam의 우선순위를 내리는 것만으로 마무리 전환이 된다.

- 블렌드 곡선·시간은 `CinemachineBrain.DefaultBlend`(`CinemachineBlendDefinition`). 씬에 vcam이 둘뿐이면 이 기본값이 곧 이 전환의 설정이다.
- 씬의 Brain은 `Main Camera`에 있다(`:774`).

즉 인트로는 두 도막이다 — **경로 주행(스플라인, 저작)** + **마무리 고정(Brain 블렌드, 자동)**.

프레이밍(§5)과는 도구가 다르다. 프레이밍은 한 구도 안에서 담을 대상이 바뀌는 문제(→ TargetGroup), 인트로는 구도 자체가 이동하는 문제(→ 스플라인 + vcam 전환)다.
