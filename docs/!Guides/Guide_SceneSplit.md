# Guide — 스테이지 하나 추가하기 (SceneSplit)

설계 근거: `docs/SceneSplit/Plan_SceneSplit.md` / `CLAUDE.md` §9

---

## 씬 셋이 하는 일

| 씬 | 무엇이 들어가나 | 무엇이 들어가면 안 되나 |
|---|---|---|
| `Explore_Stage{N}` | 플레이어 · 탐색 vcam · `Managers` · `EventSystem` · `DialogUI` · `EncounterDirector` · `Encounter` · `SequenceRunner` · **그 무대의 복제 구조**(월드 원점) · 프롭 | 전투 오브젝트 일습(Canvas·`PatternHandler`·`EnemyDirector`·`Pool`) |
| `BattleScene` | 전투 오브젝트 일습 **한 벌** · `BattleSceneBootstrap` | 스테이지별 배경(가산 로드로 온다) |
| `StageBackground_Stage{N}` | 프롭과 라이트만 | 플레이어 · 카메라 · Canvas · 로직 |

---

## 새 스테이지 만들기

1. **`StageBackground_Stage{N}.unity`** — 새 씬에 `Tools/Story Props/Build Stage Layout/Stage {N}`을 돌리고 라이트를 굽는다. 무대 중심은 월드 원점.
2. **`Explore_Stage{N}.unity`** — 탐색 영역을 만들고, **같은 배치표를 여기서도 한 번 더 돌린다**(무대 구조가 두 씬에 같은 좌표로 존재해야 전환이 안 보인다). 라이트는 이 씬 것대로 따로 굽는다.
3. 두 씬을 **Build Settings에 등록**한다. 등록을 빠뜨리면 `BattleSceneBootstrap`이 경고를 찍고 **배경 없이** 진행한다.
4. `Explore_Stage{N}`에 배치:
   - `EncounterDirector` — `player` · `modeDirector` · `inputHandler` 배선
   - `ExploreSceneBootstrap` — `player` · `modeDirector` · `encounterDirector` · **이 씬의 `Encounter` 전부**
   - `Encounter`(무대마다 하나) — 아래 표대로 채운다
   - `SequenceRunner` + `SequenceZoneTrigger`(브리핑·해금 지점마다)

### `Encounter` 채우기

| 필드 | 무엇 |
|---|---|
| `chart` | 이 자리에서 시작되는 곡 |
| `encounterId` | 등급 기록의 키. **비우면 아무것도 기록되지 않는다** |
| `stageIndex` | `StageBackground_Stage{N}`의 N |
| `clusterSizeOverride` | 적 수. 0이면 씬의 `EnemyDirector` 값 |
| `unlockFlag` | 이 플래그가 서야 열린다. **비우면 처음부터 열려 있다** |
| `completeFlag` | 완곡하면 서는 플래그. 다음 무대의 `unlockFlag`가 이것을 읽는다 |
| `retryText` | `Retry`에서 뜨는 한 줄. **기능 안내가 아니라 대사** |
| `shimmerVfx` | 일렁임. `Fresh`·`Retry`에서만 켜진다 |

⚠ 콜라이더는 `isTrigger`여야 한다(`Reset`이 자동으로 켠다).

---

## 해금 잇기

`Stage1` 무대를 끝내면 `Stage2` 무대가 열리게 하려면:

1. `Stage1`의 `Encounter.completeFlag` = `Stage1.Cleared`
2. `Stage2` 통로에 `SequenceZoneTrigger` — `onceFlag` = `Stage2.Briefed`
3. 그 시퀀스 에셋(`04. Datas/Sequences/Stage2/`)의 스텝:
   - `WaitFlagStep`이 필요하면 **러너 플래그**를 쓴다(씬 안의 조건)
   - 마지막에 `UnlockStep(Stage2.Briefed)` — 이 하나가 **브리핑 1회 제한과 다음 무대 해금을 같이** 한다
4. `Stage2`의 `Encounter.unlockFlag` = `Stage2.Briefed`

⚠ 그 시퀀스의 `holdMode`는 **`Keep`**이다. 브리핑은 이동을 멈추지 않는다.

---

## 자주 걸리는 곳

| 증상 | 원인 |
|---|---|
| 복귀하자마자 곡이 다시 시작된다 | `ExploreSceneBootstrap.encounters`에 그 `Encounter`가 없다(잠글 대상을 못 찾는다) |
| 무대가 안 열린다 | `unlockFlag`가 안 섰다. `GameProgress.ResetAll()`로 초기화하고 다시 진행해 본다 |
| 배경이 안 뜬다 | `StageBackground_Stage{N}`이 Build Settings에 없다(콘솔에 경고가 있다) |
| 프롬프트가 안 뜬다 | 그 무대가 `Fresh`다(아직 완곡한 적이 없다). 또는 `DialogUI`가 그 씬에 없다 |
| 화면이 검은 채로 굳는다 | `Managers`에 `ScreenFader`가 없거나 `CanvasGroup`이 미배선 |
| 상호작용이 무반응 | `EncounterDirector.inputHandler` 미배선. 콘솔에 `Interact 액션이 없습니다` 경고가 있으면 입력 에셋 쪽 |
| 곡 선택 씬으로 떨어진다 | `GameSession.ReturnScene`이 비었다 = 자유 연주로 읽혔다. `EncounterDirector`를 거치지 않고 전투 씬을 직접 연 경우다 |

## 진행도 초기화

`GameProgress.ResetAll()` — `PlayerPrefs`를 통째로 지운다. ⚠ 임시 백엔드라 저장 시스템이 붙으면 이 메서드 본문이 바뀐다.
