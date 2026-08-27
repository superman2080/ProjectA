# Plan — 탐색 이동과 전투 조작의 분리 (PlayerExplore)

근거: `docs/PlayerExplore/Research_PlayerExplore.md` / `CLAUDE.md` §9 · §11-2 / `Story_Overview.md` §9 A행

---

## 0. 설계 결정 (먼저 합의할 것)

### 0-1. 상태 모델 — FSM이 아니라 enum + 전수 대입

다이얼로그를 포함해 이 게임이 실제로 요구하는 플레이어 모드는 **넷**이다
(`Story_Script.md` §2 · §10 근거):

| 모드 | 근거 | 자기 매 프레임 로직 |
|---|---|---|
| `Explore` | 심상세계 3인칭 이동 | `PlayerExploreMover` |
| `Combat` | 무대 위 결투 | `PlayerCombatMover` |
| `Cutscene` | 엔딩 샷 28장 · B9 몽타주 | **없음**(카메라·타임라인이 몬다) |
| `Overlay` | 결과·파편 화면(6자리 중 5곳) | **없음**(UI) |

**⚠ 대사는 모드가 아니다.** 카시마의 브리핑은 통로 옆에 서서 한 줄 하고 물러나는 것이 전부이고
정지 컷·페이드·대사 상자가 **설계상 금지**다(`Story_Script.md` §2). 선택지도 분기도 없다
(「마지막 선택」은 대사 분기가 아니라 기습 회피 입력 한 번이다). 즉 자막은 **탐색 위에 얹히는
논블로킹 오버레이**이며 `ScoreHudView`·`SilentWitnessDirector`와 같은 부류다 — 모드를 늘리지 않는다.

**`IState`/`StateMachine` 클래스 계층을 두지 않는다.** 넷 중 자기 로직을 가진 것이 둘뿐이고
그 둘은 이미 별개 컴포넌트라 상태 객체 둘이 빈 껍데기가 된다. 불법 전이도 없다(전이를 부르는 쪽이
`EncounterDirector` 하나다). 프로젝트 관용구도 같은 쪽이다 — `EnemyView`가 6-Phase를 enum + 가드로 굴리고
`IState`/`StateMachine`은 코드베이스에 0건이다.

**⚠ 그리고 FSM의 `OnEnter`/`OnExit` 쌍이 바로 이 기능의 버그가 사는 자리다.** 노브를 하나 늘리면
`OnEnter` 넷에는 넣고 `OnExit` 하나를 빠뜨린다 — 증상은 "컷신이 끝났는데 HUD가 안 돌아온다"처럼
**특정 전이 경로에서만** 난다. 그래서 이 플랜은 반대 규율을 쓴다:

> **`ApplyMode(mode)` 하나가 모든 전이에서 모든 노브를 무조건 대입한다.**
> 노브는 예외 없이 `knob = (mode == …)` 형태로만 쓴다 — `if (mode == X) 켜기`는 금지다.

그러면 최종 상태가 **출발지와 무관하게 목적지 하나로만** 결정되어 비대칭 버그가 표현 불가능해진다.
§11-2가 "유일한 위험 지점"이라 부른 배타성에 대해 `OnEnter`/`OnExit` 쌍보다 강한 보장이다.

**승격 조건**: `Cutscene`이나 `Overlay`가 **자기 매 프레임 로직**을 갖게 되는 순간
(예: 컷신이 샷 리스트를 스스로 진행), `ApplyMode`의 `switch`를 상태 객체 디스패치로 바꾼다.
전이 지점이 한 곳뿐이라 그 교체는 국소적이다.

### 0-2. 배타성은 `enabled`로 표현한다

위치를 대입하는 컴포넌트 **자체를 켜고 끈다**:

```
Explore : PlayerExploreMover.enabled = true  · PlayerCombatMover.enabled = false
Combat  : PlayerExploreMover.enabled = false · PlayerCombatMover.enabled = true
```

`Update`가 안 도는 컴포넌트는 `transform.position`에 손댈 수가 없다 — 배타성이 **구조적으로** 보장된다.
`isExploring` 같은 불을 만들고 두 컨트롤러가 각자 그것을 해석하게 하면 진실의 원천이 셋이 된다.
(`PlayerCombatMover`는 `OnEnable`/`OnDisable`에서 `EnemyDirector` 구독을 정확히 붙였다 뗀다 — 이미 안전하다.)

### 0-2-1. `CharacterActionPlayer`는 끄지 않는다

애니메이터 레이어 인덱스·스테이트 해시·`AnimatorOverrideController`가 전부 이 클래스에 있고,
끄면 그 지식을 탐색 쪽이 복제해야 한다. 대신 **기존 래치 `convergeUntil`을 재사용**한다 —
그 필드의 의미가 이미 "지금 base 레이어는 내 것이 아니다"이고, 탐색 로코모션이 요구하는 의미와 정확히 같다.

### 0-3. 입력은 액션 맵 교체로 가른다 (가드 플래그 없음)

`PatternHandler`·`DodgeDirector`에 "탐색 중이면 무시" 가드를 넣으면 그런 가드가 구독자 수만큼 늘어난다.
Input System의 **액션 맵**이 이 일을 하라고 있는 기능이다 — 맵을 하나만 켜면
탐색 중에 숫자키 이벤트가 **애초에 발행되지 않는다.**

### 0-4. 충돌 처리는 지금 하지 않는다

무대 원 안은 평면이고 물리를 안 본다(§11-2 · §13). 탐색 영역의 벽·프롭 콜라이더는
그런 씬이 생길 때 처음 필요해진다. 그때의 업그레이드 경로는 `CharacterController.SimpleMove`이며,
코드에 `ponytail:` 주석으로 남긴다.

>>> 여기까지 이견 있으면 적어 주세요.

---

## Step 1 — `Explore` 액션 맵 추가

- [x] `Assets/IngameInputs.inputactions`에 맵 `Explore` 추가
  - `Move` (Value / Vector2) — `<Keyboard>/w,a,s,d` 2DVector 컴포짓 + `<Gamepad>/leftStick`
  - `Look` (Value / Vector2) — `<Mouse>/delta` + `<Gamepad>/rightStick`
- [x] Unity가 `IngameInputs.cs`를 재생성하는지 확인 (**직접 수정 금지**)

`Player` 맵은 **한 액션도 안 건드린다** — 전투 조작은 지금 그대로가 정답이다.

## Step 2 — `InputHandler`에 모드 교체

- [x] `public enum PlayerInputMode { Explore, Combat }` (`InputHandler.cs`)
- [x] `Start()`의 무조건 `inputActions.Player.Enable()`을 `SetMode(PlayerInputMode.Combat)`로 교체 (기본값 = 지금 동작)
- [x] `public void SetMode(PlayerInputMode mode)` — 정확히 한 맵만 `Enable()`, 나머지는 `Disable()`
- [x] `public Vector2 MoveInput` / `public Vector2 LookInput` — `Explore` 맵의 `ReadValue<Vector2>()`
  - 맵이 꺼져 있으면 Input System이 `Vector2.zero`를 돌려준다(별도 가드 불필요)

`OnKeyPressed`·`OnDodgePressed`의 시그니처와 구독자는 **그대로**다.

## Step 3 — `CharacterActionPlayer.SetExploreLocomotion`

- [x] `public void SetExploreLocomotion(float travelSpeed)` 추가
  - `travelSpeed`가 임계 이하 → `SwitchBaseState(idleStateHash)`
  - 이상 → `SprintSpeed` 갱신 + `Sprint` 스테이트 (수렴 경로의 배속 식 `travelSpeed / sprintReferenceSpeed` 재사용)
  - 두 경우 모두 `convergeUntil = Time.time + exploreLocomotionLatch` (0.1초 정도)로 **복귀 로직의 되찾기를 막는다**
- [x] 새 스테이트·새 파라미터·새 상태 필드를 **만들지 않는다**

⚠ 걷기 클립이 없어 지금은 `Sprint` 배속으로만 표현된다. 걷기 블렌드 트리는 별건이고 이 플랜 밖이다.

## Step 4 — `PlayerExploreMover`

`Assets/02. Scripts/Character/PlayerExploreMover.cs`

- [x] 배선: `InputHandler` · `CharacterActionPlayer` · `Transform cameraTransform`
- [x] `Update`:
  1. `InputHandler.MoveInput`을 **카메라 yaw 기준**으로 회전시켜 월드 방향으로 바꾼다
  2. `transform.position += dir * moveSpeed * Time.deltaTime` (물리 없음 — §11-2와 같은 규율)
  3. 이동 중이면 그 방향으로 `Quaternion.Slerp` 회전 (`turnDuration`은 전투와 별개 필드)
  4. `actionPlayer.SetExploreLocomotion(현재 속도)`
- [x] `ponytail:` 주석 — 직접 대입 이동, 프롭 충돌이 필요해지면 `CharacterController.SimpleMove`로 올린다
- [x] 컴포넌트 기본 상태는 **`enabled = false`** (씬 기본이 전투 = 지금 동작)

## Step 5 — `PlayerModeDirector` (모드의 유일 관리 지점)

`Assets/02. Scripts/Character/PlayerModeDirector.cs`

- [x] `public enum PlayerMode { Explore, Combat, Cutscene, Overlay }` — **넷을 지금 전부 선언한다**
  (`Cutscene`/`Overlay`는 당장은 `Explore`와 같은 배선으로 두고 나중에 채운다. 나중에 enum을 늘리면
  그때 `ApplyMode`의 모든 노브를 다시 훑어야 하는데, 지금 선언해 두면 그 훑기가 이미 끝나 있다)
- [x] 배선: `PlayerCombatMover` · `PlayerExploreMover` · `InputHandler` · 탐색 vcam
- [x] `public void SetMode(PlayerMode mode)` — `previous` 기록 후 `ApplyMode(mode)`
- [x] `public void ReturnToPrevious()` — `Cutscene`/`Overlay`가 끝나고 되돌아갈 곳 (필드 1개)
- [x] `private void ApplyMode(PlayerMode mode)` — **모든 노브를 무조건 대입한다**
  ```csharp
  combatMover.enabled  = mode == PlayerMode.Combat;
  exploreMover.enabled = mode == PlayerMode.Explore;
  input.SetMode(mode == PlayerMode.Combat ? PlayerInputMode.Combat : PlayerInputMode.Explore);
  exploreCamera.Priority = mode == PlayerMode.Explore ? explorePriority : 0;
  ```
  ⚠ `if (mode == X) 켜기` 형태를 하나라도 쓰면 §0-1의 보장이 깨진다.
- [x] `Awake`에서 `ApplyMode(PlayerMode.Combat)` (**회귀 0** — 지금 동작과 같다)
- [x] 배타성 자기 검사: 두 mover가 동시에 `enabled`면 `Debug.LogError` 한 줄

**왜 `EncounterDirector`에 합치지 않는가**: 저쪽은 무대(씬) 쪽 관심사이고 여기는 플레이어 쪽 관심사다.
합치면 무대 컴포넌트가 플레이어 내부 컴포넌트 넷을 알게 되고, 무대가 없는 경로(디버그·자유 연주)에서 모드를 못 바꾼다.

## Step 6 — 씬 배선 (`BattleScene`)

- [x] 플레이어 루트에 `PlayerExploreMover`(비활성) + `PlayerModeDirector` 추가
- [x] 탐색 vcam 하나 추가 — `TrackingTarget`을 **`CameraTargetGroup`이 아니라 플레이어 Transform으로** 직접 지정
  - `CinemachineOrbitalFollow` + `CinemachineRotationComposer`, 휴지 우선순위 0
  - ⚠ `CameraTargetGroup`을 공유하면 안 된다 — 그룹 회전은 `CameraDirector`가 몰고 있고(§7-2) 마우스 회전과 싸운다
- [x] `PlayerExploreMover.cameraTransform`에 그 vcam을 배선

실제 배선 결과(`BattleScene`):

| 무엇 | 어디 |
|---|---|
| `PlayerExploreMover`(비활성) · `PlayerModeDirector` | `Character/Char_School_Katana_FullBody-Magica cloth2` (플레이어 루트) |
| 탐색 vcam | `Camera/Cam_Explore` — `CinemachineCamera` + `OrbitalFollow` + `RotationComposer` + `InputAxisController`, 휴지 우선순위 0 |
| vcam `TrackingTarget` | 플레이어 루트 **직접**(`CameraTargetGroup` 아님 — §7-2와 안 싸우게) |
| `TargetOffset.y` | 궤도·조준 **둘 다 1.5**(루트가 발바닥이라 §7-2의 두 노브 규율과 같은 값) |

## Step 7 — 검증

- [ ] 에디터에서 `PlayerModeDirector.SetMode(Explore)` (컨텍스트 메뉴) → WASD로 걷고, 카메라가 따라온다
- [ ] 탐색 중 숫자키 1~9 · Space가 **아무 일도 하지 않는다** (`PatternHandler`에 이벤트가 안 들어온다)
- [ ] `SetMode(Combat)`로 되돌린 뒤 `ChartPlayer.Play()` — **곡 재생·판정·결투 이동·거리 커브가 전부 이전과 동일**
- [ ] 곡 도중 `SetMode(Explore)`를 눌러도 콘솔에 배타성 위반 에러가 안 뜬다
- [ ] `CharacterActionPlayer`의 `logConvergeDecision`을 켜고, 탐색 → 전투 전환 직후 첫 수렴 로코모션이 정상적으로 찍히는지 확인
      (`convergeUntil` 래치를 탐색이 잡은 채로 남기지 않았는가)

---

## 이 플랜이 하지 않는 것

| 항목 | 언제 |
|---|---|
| `EncounterDirector` · `Encounter`(무대 진입 → 곡 시작) | 다음 단계 |
| `GameMode`(Story/FreePlay) · `SongSelectScene` 소속 변경 | 다음 단계 |
| 탐색 중 HUD(점수·패턴인풋) 감추기 | 다음 단계 (`ScoreHudView.SetHidden`이 선례) |
| 프롭 충돌 · 벽 · 상호작용 프롬프트 | 탐색 영역이 있는 씬이 생길 때 |
| 대사 자막 오버레이(카시마 25줄) | 별건 — **모드가 아니다**(§0-1). 문자열 테이블 + 작은 오버레이 컴포넌트 |
| 걷기 블렌드 트리 | 별건 |
