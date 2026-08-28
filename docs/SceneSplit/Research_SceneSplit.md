# Research — 탐색 씬과 전투 씬의 분리 (SceneSplit)

대상: `CLAUDE.md` §9 · §11-2 · §13 / `docs/Story/Story_Overview.md` §1-1 / `docs/PlayerExplore/` / `docs/GameFramework/`

---

## 1. 지금 무엇이 있는가

### 1-1. 씬은 둘뿐이다

| 씬 | 내용 |
|---|---|
| `BattleScene.unity` | 게임플레이 전부 — Canvas(패턴인풋·HUD) · `PatternHandler` · `ChartPlayer` · `EnemyDirector` · `CameraDirector` + vcam 다수 · `Stage`(월드 원점) · `Pool` · `EventSystem` · `Managers` · 도장 배경 · `DojoLighting.lighting`(베이크) · `DojoPostProcess.asset` |
| `SongSelectScene.unity` | `SongSelectManager` + 버튼. `GameSession.SelectedChart`에 채보를 꽂고 `SceneManager.LoadScene("BattleScene")` |

즉 **위 표의 `BattleScene` 칸에 적힌 것이 통째로 그 씬 하나에만 존재한다.** 이것이 이 조사의 가장 중요한 사실이다.

> **용어**: 이 문서에서 **"전투 오브젝트 일습"**은 위 표의 `BattleScene` 칸에 열거된 것들 — `Canvas`(패턴인풋·HUD) · `PatternHandler` · `ChartPlayer` · `EnemyDirector` · `CameraDirector`와 vcam들 · `Stage` · `Pool` · `EventSystem` — 을 한꺼번에 가리킨다. 씬을 나눌 때 **함께 움직이거나 함께 복제되는 단위**가 정확히 이 묶음이라 이름이 하나 필요하다.

### 1-2. 씬을 넘는 상태는 `GameSession` 하나다

`GameSession`(`Singleton`, `DontDestroyOnLoad`)이 드는 것은 둘:

- `SelectedChart` — `ChartPlayer.ActiveChart`가 읽는다(`SelectedChart ?? debugChart`).
- `LastResult` — ⚠ **읽는 쪽이 아직 없다**(결과 화면 미구현).

`Managers` 프리팹이 `GameSession` · `SoundManager` · `SfxManager`를 들고 루트째 씬을 넘어간다.
⚠ **`Pool`과 `EventSystem`은 의도적으로 여기 없다**(§10) — `Pool`은 대여한 `FocusRingView`를 씬 Canvas 밑으로 옮기고 반납 시 부모를 안 되돌려서, 씬을 넘기면 큐에 파괴된 오브젝트가 남는다. **씬을 반복해서 오갈 계획이라면 이 규율이 그대로 유효하다**(오히려 더 중요해진다).

### 1-3. 탐색은 이미 절반 구현돼 있다

- `PlayerExploreMover` — 무대 밖 3인칭 이동. **존재한다.**
- `PlayerModeDirector` — `PlayerMode{Explore, Combat, Cutscene, Overlay}` + `ApplyMode`의 전수 대입. **존재한다.** 기본값이 `Combat`이라 모드를 아무도 안 바꾸는 씬은 예전과 똑같이 돈다.
- `InputHandler.SetMode(PlayerInputMode)` — 액션 맵 교체. **존재한다.**
- `EncounterDirector` / `Encounter` — **코드베이스에 0건.** 탐색과 전투를 잇는 그 지점이 비어 있다.

즉 **분기점은 지금 정확히 비어 있는 자리 하나**이고, 이 조사가 묻는 것은 "그 자리에 씬 로드를 넣을 것인가, 모드 전환을 넣을 것인가"다.

### 1-4. 전투 오브젝트 일습이 씬에 요구하는 것

무대 안의 계약을 훑으면 (§11-2 · §13):

- **무대 중심은 월드 원점**이며 `EnemyDirector.arenaCenter`가 씬의 `Stage` 오브젝트를 가리킨다.
- 무대 원(반경 8m, 5스테이지 3.5m) 안은 **반드시 평면**이다 — 플레이어·적 이동이 `transform.position` 대입이라 경사를 못 탄다.
- 배치·이격·프레이밍·§13 배치표 좌표가 전부 그 원점 위에 서 있다.

**이 계약들은 씬을 나눠도 하나도 안 바뀐다.** 원점이 원점인 것은 씬 내부 사정이기 때문이다.

---

## 2. 현재 확정 설계가 씬 분리를 배제한 근거

`Story_Overview.md` §1-1은 `곡 선택 씬 → 전투 씬 → 종료 → 곡 선택 씬`을 **명시적으로 폐기**했다. 근거는 셋이다:

1. **§0 위반** — "지금부터 스테이지에 들어갑니다"라는 게임의 선언이 세계가 스테이지들의 묶음이라고 자백한 셈이 된다.
2. **재도전이 장소여야 한다**(§2-4) — 못 깬 자리는 아직 일렁이고, 다시 들어서면 다시 시작된다. 재도전 메뉴가 없다.
3. **다가갈 수 있어야 하는 것들**(§2-2의 문 · §4-2의 그녀)이 탐색 이동을 요구한다.

⚠ 여기서 폐기된 것은 **"곡 선택 UI"**와 **"전투가 딴 세계에서 벌어진다"** 두 가지이지, "씬 파일이 둘"이라는 기술적 사실 자체가 아니다. **이음매가 안 보이면 셋 다 그대로 성립한다** — 이것이 이번 분리안의 성립 조건 전체다.

---

## 3. 분리했을 때 실제로 얻는 것 / 잃는 것

### 얻는 것

- **전투 오브젝트 일습을 한 벌만 유지한다.** 단일 씬 안이라면 스테이지 5개 = `BattleScene`의 그 묶음을 5벌 복제하는 것이고, `CameraDirector` 노브 하나 고치면 다섯 씬을 손으로 열어 고쳐야 한다. **이것이 분리의 진짜 이유이고 유일하게 큰 것이다.**
- **탐색 씬은 가벼워진다.** 프롭·라이트맵만 들면 되므로 §13 배치 툴의 산출물이 그대로 씬이 된다.
- 씬 로드가 전투 상태를 통째로 리셋한다 — 재도전 시 적 풀·`Pool` 큐·링 잔존물 정리가 **공짜**다(단일 씬이면 손으로 비워야 한다).

### 잃는 것 / 새로 생기는 것

- **로딩 이음매.** 위 §2의 성립 조건. 페이드로 덮어야 하고, 페이드는 `DontDestroyOnLoad`여야 한다(로드 중에 살아 있어야 하므로).
- **복귀 좌표.** 탐색 씬이 다시 로드되면 플레이어는 씬 저장 위치에 선다. "그 자리에 선 채 세계가 이어진다"를 지키려면 **떠난 자리를 들고 갔다 돌아와야 한다.**
- **무대 배경이 스테이지마다 다르다.** 일습을 한 벌로 유지하면 배경만 갈아끼워야 하는데, `DojoLighting.lighting` 처럼 **베이크 라이트맵은 프리팹이 못 든다**(씬에 딸린다).
- **진행도가 씬 밖으로 나가야 한다** — 자리별 최고 등급(§9 "재도전은 장소다")을 탐색 씬이 로드될 때 읽어 일렁임 VFX를 켜고 꺼야 한다. 지금은 저장 계층이 아예 없다.

---

## 4. 이 조사가 확인한 제약

1. **`Pool`은 절대 `DontDestroyOnLoad`가 되면 안 된다**(§10). 씬 왕복이 잦아질수록 이 규율을 깨면 즉시 터진다.
2. **`EventSystem`도 마찬가지다** — 입력 모듈이 씬마다 다르다.
3. **`Managers` 루트에 `ManagerRoot`가 붙어 있어야 한다**(§10). 씬을 오갈 때마다 빈 껍데기가 쌓이는 그 함정이 왕복 구조에서 훨씬 자주 드러난다.
4. **`PlayerModeDirector`는 씬 분리 후 전이기가 아니라 씬 상수가 된다** — `BattleScene`은 언제나 `Combat`, 탐색 씬은 언제나 `Explore`. §11-2가 "유일한 위험 지점"이라 부른 **위치 소유권 충돌이 구조적으로 소멸한다**(두 무버가 같은 씬에 없다). 이것은 분리안의 부수 이득이다.
5. **`SongSelectScene`은 그대로 산다** — FreePlay 전용(§9). 분리안은 그 경로를 한 줄도 안 바꾼다(오히려 같은 진입 관용구를 쓴다).
6. **`ChartPlayer.ActiveChart`가 진실의 원천이다** — `GameSession.SelectedChart`를 꽂고 씬을 로드하는 기존 경로(`SongSelectManager`)가 이미 정확히 이 구조다. **분리안은 새 파이프라인이 아니라 그 경로의 재사용이다.**
