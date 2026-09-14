# Plan — 씬 전환 (SceneTransition)

## 목표

1. 모든 씬 전환이 **비동기 + 검은 화면 최소 0.5초**를 지난다.
2. 검은 화면 우하단에 **`FocusRing` 스프라이트를 재사용한 로딩 링**이 돈다(퍼센트 없음).
3. 튜토리얼은 마지막 드릴 성공 → 마무리 실루엣이 걷히면 **대사 없이 바로** 다음 씬으로 넘어간다.

## 설계 요약

- 새 클래스 **둘**뿐이다: `SceneTransition`(전환의 유일 관리 지점) · `LoadSceneStep`(시퀀스 스텝).
- `ScreenFader`는 **한 줄도 안 고친다** — 헤더의 규율("여기는 알파만 민다")이 그대로 남는다.
  `SceneTransition`이 그 위에 서서 *"무엇을 덮을지"*를 정한다.
- 로딩 링은 **`Image` 하나**다. `FocusRingView`를 재사용하지 않는다(그 클래스는 판정 타이밍 전용).
- 받는 쪽(`*SceneBootstrap`)은 안 건드린다 — `CoverInstantly` → `FadeIn` 계약이 그대로다.

## Step 1 — `SceneTransition` (`02. Scripts/SceneTransition.cs`)

- [x] `Singleton<SceneTransition>`, `DontDestroy = true`. `Managers` 프리팹 자식으로 들어간다.
- [x] 직렬화 필드
  - `Image loadingRing` — 우하단 링. **비어도 동작한다**(아이콘만 안 뜬다).
  - `float minBlackDuration = 0.5f` — 화면이 완전히 검어진 뒤 씬을 켤 때까지의 **하한**.
  - `float ringCycleDuration = 1.2f` — 채움 한 바퀴 + 비움 한 바퀴.
- [x] `public void Load(string sceneName)` — 중복 호출은 무시한다(`loading` 플래그).
      전환 중 두 번 부르면 `LoadSceneAsync`가 둘 뜬다.
- [x] 코루틴 `LoadRoutine`:
  1. `ScreenFader.Instance?.FadeOut()` 대기. **이미 덮여 있으면 즉시 끝난다**(Research §2) —
     그래서 `ScreenFadeStep`을 먼저 둔 튜토리얼 경로와 겹쳐도 화면이 두 번 깜빡이지 않는다.
  2. 링 켜기 → `AsyncOperation op = SceneManager.LoadSceneAsync(sceneName)`, `allowSceneActivation = false`.
  3. `while (op.progress < 0.9f || Time.unscaledTime - blackAt < minBlackDuration)` 동안 매 프레임 `TickRing()`.
  4. `op.allowSceneActivation = true` → `while (!op.isDone) yield`.
  5. 링 끄기, `loading = false`. **페이드 인은 안 한다** — 받는 씬의 부트스트랩이 든다.
- [x] `TickRing()`: `t = (Time.unscaledTime % cycle) / cycle` 기준으로
      `t < 0.5` → `fillClockwise = true`, `fillAmount = t * 2`
      아니면 → `fillClockwise = false`, `fillAmount = 2 - t * 2`.
      `fillOrigin`은 **건드리지 않는다**(프리팹이 Bottom으로 저작돼 있고 두 단계가 같은 origin을 쓴다).
- [x] ⚠ **전부 `Time.unscaledTime`이다** — 마무리 실루엣이 `timeScale = 0.1`을 걸고 있을 수 있다(§14).
- [x] `sceneName`이 비었거나 `Application.CanStreamedLevelBeLoaded`가 false면 **에러를 찍고 아무것도 안 한다**
      (검은 화면에 굳는 것보다 낫다).

## Step 2 — 씬 오브젝트 (`Managers.prefab`)

- [x] `ScreenFader` 오브젝트에 `SceneTransition` 컴포넌트를 붙인다(같은 오브젝트 = `Managers` 루트 아래 그대로).
- [x] `ScreenFader/Cover` **다음 형제**로 `LoadingRing`(`Image`) 추가 — Cover 위에 그려져야 한다.
  - 스프라이트: `FocusRing.prefab`이 쓰는 것과 같은 것(guid `fbfb1169…`)
  - `Type = Filled`, `FillMethod = Radial360`, `FillOrigin = Bottom`, `FillClockwise = true`, `FillAmount = 0`
  - 앵커 우하단(1,0) · 피벗 (0.5,0.5), `anchoredPosition (-80, 80)`, `sizeDelta (64, 64)`
    ⚠ 이 Canvas는 `ScreenFader`의 것이고 `CanvasScaler`가 **ConstantPixelSize · scaleFactor 1**이라
    **값이 곧 화면 픽셀이다**(게임플레이 Canvas의 3840x2160 기준이 아니다).
  - `raycastTarget = false`, 시작 시 `SetActive(false)`
- [x] `SceneTransition.loadingRing`에 배선.

## Step 3 — 호출부 셋을 갈아끼운다

- [x] `EncounterDirector.EnterRoutine` — `fader.FadeOut()` + `LoadScene` 두 줄을 `SceneTransition.Instance.Load(battleSceneName)` 하나로.
- [x] `BattleSceneBootstrap.ReturnRoutine` — 같은 방식.
- [x] `SongSelectManager.SelectChart` — `LoadScene` → `Load(...)`. **여기서 페이드가 처음 생긴다**(지금은 즉시 전환).
- [x] ⚠ `BattleSceneBootstrap.LoadStageBackground`(가산 배경)는 **안 건드린다** — 씬 전환이 아니다.
- [x] 두 부트스트랩의 `ReturnRoutine`/`EnterRoutine`은 코루틴 유지(`Load`는 void라 호출만 남는다).

## Step 4 — `LoadSceneStep` (`SequenceSystem/Steps/LoadSceneStep.cs`)

- [x] `[Serializable] class LoadSceneStep : SequenceStep`, 네임스페이스 `SequenceSpace`.
- [x] 필드 `string sceneName` 하나. `Enter`에서 `SceneTransition.Instance?.Load(sceneName)`.
- [x] `IsFinished => false` — **일부러 안 끝난다.** 씬이 바뀌면 러너째 사라지므로 다음 스텝이 있을 수 없고,
      끝났다고 말하면 남은 스텝이 로드가 도는 동안 실행된다.
- [x] `Label => $"LoadScene {sceneName}"`.
- [x] ⚠ 클래스 이름·네임스페이스를 나중에 바꾸지 않는다(`[SerializeReference]` 규율).

## Step 5 — 튜토리얼 시퀀스 끝부분 (`Seq_Tutorial.asset`)

바꿀 모양:
```
486408  HighlightStep    (하이라이트 해제)      그대로
926102  ScreenFadeStep   Out, waitForFinale     그대로  ← 실루엣이 걷힐 때까지 기다렸다 덮는다
486410  UnlockStep       tutorial_done          그대로 (순서만 앞으로)
신규    LoadSceneStep    Explore_Stage1
```

- [x] `DialogStep`(486409) 제거.
- [x] `SetActiveStep` 둘(926101·926100) · `MoveToStep`(926099) · `ScreenFadeStep In`(926098) 제거 —
      **그 넷은 오직 그 대사를 보여 주려고 있었다**(Research §4). 남기면 걷어냈다가 곧바로 다시 덮는다.
- [x] `requiredBindings`에서 `Rift`·`FinaleDebris` **두 슬롯을 지운다**(마지막 사용처가 위 네 스텝이었다).
      `SequenceRunner`의 씬 배선 항목도 같이 사라진다.
- [x] ⚠ **씬 오브젝트는 안 지운다** — `Rift`(균열)는 튜토리얼 내내 화면에 있고,
      `Debris_EnemyHead`는 꺼진 채로 배치돼 있어 아무 일도 안 한다. 지우는 것은 **슬롯**이지 물건이 아니다.
- [x] ⚠ 순서가 요구 그 자체다 — `UnlockStep`이 `LoadSceneStep`보다 **앞**이어야 한다. 뒤면 씬이 넘어가 영영 안 돈다.

확정: 넘어갈 씬은 **`Explore_Stage1`**(프로젝트에 있는 유일한 탐색 씬).
⚠ `Build Settings`에 등록돼 있어야 한다 — 아니면 Step 1의 가드가 에러를 찍고 전환이 안 일어난다.

## Step 6 — 검증

- [x] 컴파일 에러 0.
- [ ] 튜토리얼 마지막 드릴 성공 → 실루엣 → 검게 덮임 → 우하단 링이 하단에서 시계방향으로 차고, 다시 하단부터 시계방향으로 빔 → 다음 씬.
- [ ] 곡 선택 → 전투 · 전투 → 탐색 · 탐색 → 전투 세 경로 모두 검은 화면이 **최소 0.5초** 유지된다.
- [ ] 전환 도중 클릭이 사라지는 씬 UI로 안 샌다(`ScreenFader`의 `blocksRaycasts`가 이미 든다).
- [ ] `Managers` 프리팹 없는 씬에서 진입해도 전환이 성립한다(링만 안 뜬다).
- [ ] 링이 도는 동안 `timeScale`이 0.1이어도 속도가 안 느려진다.
