# Plan — 탐색 씬과 전투 씬의 분리 (SceneSplit)

근거: `docs/SceneSplit/Research_SceneSplit.md` / `CLAUDE.md` §9 · §10 · §11-2 · §13 / `Story_Overview.md` §1-1 · §2-4

---

## 0. 설계 결정 (먼저 합의할 것)

### 0-1. 무엇을 나누고 무엇을 안 나누는가

```
Explore_Stage1 ─┐                                     ┌─ Explore_Stage1 (같은 씬, 떠난 자리로 복귀)
Explore_Stage2 ─┤                                     │
Explore_Stage3 ─┼→ [BattleScene] 한 벌 ───────────────┤
Explore_Stage4 ─┤   + StageBackground_Stage{N} 가산   │
Explore_Stage5 ─┘                                     └─ …
```

- **전투 씬은 하나다.** 스테이지마다 복제하면 `CameraDirector` 노브 하나가 다섯 씬 수정이 된다 — 분리의 유일하게 큰 이득이 그것인데 복제하면 그 이득이 통째로 사라진다.

> **용어**: 아래에서 **"전투 오브젝트 일습"**은 지금 `BattleScene`에 있는 `Canvas`(패턴인풋·HUD) · `PatternHandler` · `ChartPlayer` · `EnemyDirector` · `CameraDirector`와 vcam들 · `Stage` · `Pool` · `EventSystem`을 한꺼번에 가리킨다(`Research_SceneSplit.md` §1-1의 표). 씬을 나눌 때 **함께 복제되는 단위**가 정확히 이 묶음이다.
- **무대 배경은 `StageBackground_Stage{N}.unity`를 가산 로드**한다. 프리팹이 아닌 이유는 하나뿐이다 — **베이크 라이트맵은 씬에 딸린다**(`DojoLighting.lighting`이 이미 그렇다). 이 씬에는 **프롭과 라이트만** 들어가고 전투 오브젝트 일습·플레이어·카메라·Canvas는 한 개도 안 들어간다.
- **탐색 씬은 스테이지당 하나**이며 §13 배치 툴(`Tools/Story Props/Build Stage Layout`)의 산출물이 그대로 그 씬이 된다.

**⚠ 프리팹 + 실시간 라이트는 대안이 아니다 — 성능 이전에 그림이 틀린다.** 실측:

| 사실 | 값 |
|---|---|
| `BattleScene`의 라이트 | **13개** — Point 12 + Directional 1(`intensity 0.1`) |
| 활성 URP 에셋 | `PC_RPAsset`(`ProjectSettings/GraphicsSettings.asset`이 이 guid를 가리킨다) |
| `m_AdditionalLightsPerObjectLimit` | **4** |
| `DojoLighting.lighting` | `EnableBakedLightmaps: 1` · `MixedBakeMode: 2`(Shadowmask) · 2048 / 해상도 6 |

지금 도장의 룩은 **Point 12개가 만든 것**이고 Directional은 0.1이라 사실상 채움광이다. 이것을 실시간으로 돌리면 URP가 **오브젝트당 4개만 골라 쓰고 나머지를 버려서**, 바닥과 벽이 서로 다른 라이트로 켜지는 그림이 된다 — 프레임 얘기 이전에 렌더가 안 맞는다. Point 그림자까지 켜면 섀도우 큐브맵이 12장 붙는다.

**그래서 이 항목은 배경 씬으로 확정한다.** 씬이 5개 느는 것은 부담처럼 보이지만 그 씬들은 **프롭과 라이트만** 들고(§13 배치 툴 산출물이 그대로 그 씬이다) 로직이 0줄이라 유지비가 사실상 없다 — 반면 전투 오브젝트 일습을 복제하는 쪽은 노브 하나가 다섯 씬 수정이 된다.

⚠ 라이트를 줄이고 싶다면 그것은 **이 분리안과 무관한 별개 작업**이다(지금 룩을 바꾸는 일이므로 여기서 곁다리로 하지 않는다).

### 0-2. ⚠ 이 분리의 성립 조건은 "이음매가 안 보인다" 하나다

`Story_Overview.md` §1-1이 폐기한 것은 **곡 선택 UI**와 **전투가 딴 세계에서 벌어진다는 감각**이지 씬 파일 개수가 아니다. 그래서 이 플랜은 로딩 경계에 **아무 UI도 두지 않는다**:

- 로딩 화면·진행바·"Press any key" 없음. **검은 페이드 하나**뿐이다.
- 확인 창 없음 — 무대에 들어서면 시작된다(§2-4의 재도전 규칙이 처음과 재도전에서 똑같이 작동해야 한다).
- 씬 이름·스테이지 번호를 화면에 안 띄운다.

**이 규칙을 깨는 순간 분리안은 §0 위반이 되고, 그러면 단일 씬 안이 옳다.** 판단 기준을 코드가 아니라 여기 적어 둔다.

### 0-3. 복귀 정보는 `GameSession` 필드 몇 개다 (새 클래스 없음)

`SongSelectManager`가 이미 `SelectedChart`를 꽂고 씬을 로드한다 — **분리안은 새 파이프라인이 아니라 그 경로의 재사용이다.** 여기에 "어디로 돌아갈 것인가"만 얹는다:

```
GameSession.SelectedChart   // 이미 있다
GameSession.ReturnScene     // 떠난 탐색 씬 이름
GameSession.ReturnPosition  // 떠난 자리(월드 좌표)
GameSession.ReturnYaw       // 떠난 방향
GameSession.EncounterId          // 어느 자리였나 — 복귀 후 등급·일렁임 갱신에 쓴다
GameSession.StageIndex           // StageBackground_Stage{N} 가산 로드에 쓴다(Step 6)
GameSession.ClusterSizeOverride  // 그 무대의 적 수(0이면 무시) — 4-b·튜토리얼이 쓴다
```

**별도의 `SceneTransitionPayload` 클래스를 만들지 않는다.** 여섯 개고 소유자가 이미 "씬을 넘는 상태의 유일 관리 지점"이다. ⚠ **여덟을 넘으면 그때 클래스로 묶는다**(아래 "안 하는 것" 표).

⚠ **`ReturnScene`이 비어 있으면 FreePlay다** — `SongSelectScene`으로 돌아가고 `GameProgress`에 아무것도 기록하지 않는다(§9). 진입 경로가 둘인데 복귀 경로가 하나면 자유 연주가 심상세계에 떨어진다.

### 0-4. 로드는 단일(Single)이다, 가산이 아니다

탐색 씬을 켜 둔 채 전투를 얹으면 라이트·오디오 리스너·`EventSystem`·플레이어가 **둘씩** 생기고, 탐색 씬의 프롭이 전투 내내 메모리에 남는다. 단일 로드는 그 부류를 통째로 없앤다. 대가는 복귀 시 탐색 씬 재로드 비용 하나이고, 그것은 페이드 뒤에 있다.

⚠ **`Pool`·`EventSystem`은 `Managers`에 넣지 않는다**(§10). 왕복이 잦아지면 그 규율을 깼을 때 즉시 터진다.

### 0-5. `PlayerModeDirector`는 씬 상수가 된다 (잡몹 전투가 있어도)

`BattleScene`은 언제나 `Combat`, 탐색 씬은 언제나 `Explore`. **두 무버가 같은 씬에 존재하지 않으므로 §11-2가 "유일한 위험 지점"이라 부른 위치 소유권 충돌이 구조적으로 소멸한다.** `PlayerModeDirector`는 지우지 않는다 — `Cutscene`/`Overlay`가 각 씬 안에서 여전히 필요하다.

### 0-6. 무대의 상태는 넷이고, 그중 하나만 상호작용을 요구한다

| 상태 | 조건 | 일렁임 | 무대에 들어서면 |
|---|---|---|---|
| **Locked** | `unlockFlag`가 안 섰다 | 없음 | 아무 일도 안 일어난다 |
| **Fresh** | 해금됐고 아직 **완곡한 적이 없다** | 있음 | **곧바로 시작된다** |
| **Retry** | 완곡했지만 `SSS`가 아니다 | 있음 | 상호작용하면 텍스트 → 재도전 |
| **Done** | `SSS` | 없음 | 아무 일도 안 일어난다 |

- **해금은 플래그 하나다.** `Encounter.unlockFlag`(문자열)를 시퀀스가 세운다(§15). **해금 순서표·의존 그래프·`stageIndex` 비교를 만들지 않는다** — "하나씩"은 시퀀스가 재생되는 순서가 이미 표현하고 있고, 순서를 두 곳에 적으면 어긋난 조합이 조용히 만들어진다(§5의 `onsetTimes` 규율과 같은 근거).
- **⚠ 플래그는 씬을 넘어 살아야 한다.** `SequenceRunner.SetFlag`/`HasFlag`는 **런타임·씬 한정**이라(§15) 전투 씬을 다녀오면 사라진다. 그래서 해금 플래그의 소유자는 러너가 아니라 **Step 3의 `GameProgress`**다. 러너의 플래그는 지금처럼 "이 시퀀스 안에서만 쓰는 조건"으로 남는다 — **둘은 다른 물건이고 합치지 않는다.**
- **⚠ Locked과 Done은 화면에서 똑같이 조용하다.** 일렁임의 뜻이 "지금 여기 볼일이 있다"로 통일되기 때문이고, 그래서 자물쇠 아이콘·"아직 갈 수 없습니다" 문구가 **하나도 필요 없다**(§13: 목표 마커·프롬프트 금지).

**⚠ 이 표는 `Story_Overview.md` §2-4의 한 문장을 폐기한다** — *"재도전 메뉴·확인 창이 없다. 들어서면 시작된다는 규칙이 처음과 재도전에서 똑같이 작동할 뿐이라 새로 배울 규칙이 0이다."* 재도전만 상호작용을 요구하므로 규칙이 하나 는다. 그 대가로 얻는 것:

- **다 지나온 구역을 걸어 지나갈 때 곡이 실수로 다시 시작되지 않는다.** 탐색이 생기면 클리어한 무대를 가로지르는 일이 계속 생기고, 그때마다 곡이 시작되는 쪽이 훨씬 나쁘다.
- **텍스트가 재도전을 권할 수 있다** — 일렁임만으로는 "여기 아직 뭔가 남았다"까지만 말한다.
- **⚠ 처음 진입(Fresh)은 손대지 않는다.** 거기까지 상호작용을 요구하면 §0("게임이 스테이지를 선언하지 않는다")이 깨진다 — 확인 창이 곧 선언이다. **새로 배우는 규칙은 "끝낸 자리는 말을 걸어야 다시 열린다" 하나뿐**이다.

### 0-7. 재도전 프롬프트는 기능 안내가 아니라 대사다

`Retry` 상태의 무대에 다가서면 **찜찜한 한 줄이 뜬다** — 예: *"아직 전부 처리하지 않았어"*(임시 텍스트). 그 상태에서 `Interact`를 누르면 곡이 다시 시작된다.

- **대사가 프롬프트를 겸한다.** `"E — 다시 도전"` 같은 기능 안내를 따로 띄우지 않는다 — 그건 게임이 자기 구조를 설명하는 것이고 §0이 금지하는 그 부류다. **찜찜함이 곧 유도**이며, 일렁임(보인다)과 대사(말한다)가 같은 사실을 두 겹으로 말한다.
- **⚠ §13의 "프롬프트를 두지 않는다"는 여전히 유효하다** — 그 규칙의 대상은 **배경 도상과 「닫힌 문」**이고(다가가도 아무 일이 없어야 한다), 무대는 원래 들어서면 시작되던 자리다. **예외는 `Retry` 상태의 무대 하나뿐**이라고 못박는다. 도상 쪽에 프롬프트가 하나라도 생기면 이 예외가 규칙을 잠식한 것이다.
- **표시는 `DialogUI`를 그대로 쓴다**(§15). 새 패널·새 프리팹 없음. **⚠ 대사 창의 주인이 둘이 되면 안 된다** — 브리핑 시퀀스가 재생 중이면 프롬프트는 물러난다(`SequenceRunner.IsPlaying`).
- **⚠ 멈춰 세우지 않는다.** 모드는 `Explore` 그대로다 — 창을 띄우고 확인을 받는 구조가 아니라 **걸어 다니는 채로 한 줄이 떠 있고 키 하나가 살아 있는 것**뿐이다. 그래서 브리핑(§15 `holdMode = Keep`)과 같은 결이고, `Overlay` 복구 경로가 아예 필요 없다.
- **확인 창이 없다.** 프롬프트가 떠 있는 것 자체가 확인이고, `Interact` 한 번이 곧 시작이다.
- 대사는 `Encounter.retryText`(문자열 필드)에 적는다 — 자리마다 다른 말을 쓸 수 있어야 하고, 그 말이 **그 무대가 무엇이었는지**를 기억하게 한다.

### 0-8. 스토리 씬에 무대와 같은 구조를 세운다 — 이음매가 안 보이게

§0-2의 성립 조건("이음매가 안 보인다")을 페이드 혼자 지게 하지 않는다. **전환 앞뒤에 같은 장소가 있으면 페이드가 훨씬 적게 일한다.**

- 탐색 씬에도 그 스테이지의 무대 구조를 **같은 배치표로** 세운다 — §13의 `Tools/Story Props/Build Stage Layout`이 **현재 씬에 세우는** 툴이라 탐색 씬을 열고 한 번 더 돌리면 끝이다. **새 툴도 새 데이터도 없다**(배치표의 진실의 원천은 `StageLayoutBuilder.cs`의 좌표 테이블 하나 그대로).
- **⚠ 탐색 씬의 무대 복제본도 월드 원점에 둔다.** `BattleScene`의 무대 중심은 월드 원점 고정이고(§11-2) 복귀 정보(§0-3)도 같은 축에서 읽으므로, 원점을 맞춰야 전환 전후로 플레이어가 **같은 자리에 서 있다.**
- 라이트맵은 두 씬이 각자 굽는다(탐색 씬은 주변 영역까지 함께, `StageBackground_Stage{N}`은 무대만). **프롭 프리팹은 공유하고 베이크만 갈린다.**
- **⚠ 4스테이지는 무대가 둘이다**(「면을 쓴 자」의 4-b — `Story_Overview.md` §4-3, `Story_Narrative.md` §343의 *"같은 실내의 안쪽 방"*). 원점에 놓을 수 있는 것은 하나뿐이므로 **탐색 씬을 `Explore_Stage4a` / `Explore_Stage4b` 둘로 나눈다.**
  - 그러면 **모든 탐색 씬이 "무대 하나, 그 무대는 원점"**이라는 한 규칙으로 통일된다 — 4스테이지만 예외인 상태가 사라진다.
  - **씬 경계 자리도 이미 정해져 있다** — §9가 씬 경계를 *"좁고 시야가 막힌 통로"*에만 두라고 했고, 4-b로 들어가는 **안쪽 방의 문**이 정확히 그 조건이다. 경계를 새로 지어내지 않는다.
  - ⚠ 그래서 **스테이지 수와 탐색 씬 수가 다르다**(스테이지 5 · 탐색 씬 6). 씬 이름을 `stageIndex`에서 파생시키지 말고 `Encounter`/`GameSession.ReturnScene`이 **이름을 그대로 들게** 한다.

### 0-9. 진행의 조건은 완곡이지 성적이 아니다

- **곡을 끝까지 치면 그 무대는 완료다** — 등급이 `D`여도 다음으로 간다. **⚠ 이것은 엔딩이 갈리기 위한 필수 조건이다**: 성적으로 잠그면 못 깬 플레이어는 끝에 도달할 수 없어 **배드 엔딩이 표현 불가능**해진다(`Story_Overview.md` §6 "서사를 성적으로 잠그지 않는다"). 엔딩(굿/배드/트루)은 **도달 여부가 아니라 쌓인 등급**이 가른다.
- **완곡은 플래그 하나를 세운다** — `Encounter.completeFlag`. 완곡하면 `GameProgress.SetFlag(completeFlag)`이고, **해금 플래그와 같은 이름 공간**이라 시퀀스의 `WaitFlagStep`도 다음 무대의 `unlockFlag`도 그대로 읽는다. **새 개념이 0개**다(§0-6의 `UnlockStep`이 세우는 것과 구분이 없다 — 세우는 주체만 다르다).
- **⚠ 도중에 끊기면 아무것도 기록하지 않는다.** 목숨이 0이면(§7-6) 곡을 끊고 탐색으로 되돌아가며 등급도 완곡 플래그도 안 남는다 — 그 무대는 `Fresh` 그대로라 **다시 들어서면 곧바로 시작된다**(상호작용을 안 거친다). 패배 화면을 만들지 않는다.
- ⚠ 그래서 **`Fresh`와 `Retry`의 실질적 차이는 "한 번이라도 끝까지 쳤는가"** 하나다. 아직 못 끝낸 무대에 재도전 프롬프트를 붙이면 *"아직 전부 처리하지 않았어"*가 **처음 도전에도 뜨게 되어** 그 말이 뜻을 잃는다.

### 0-9. 잡몹·튜토리얼 전투도 `Encounter`다 — 탐색 씬에 전투를 심지 않는다

**검토했고 기각한 안**: 탐색 씬에 "연습용 적 한 마리"를 오브젝트로 놓는 것. 이 게임에서 **벤다는 것은 `PatternHandler` 입력 → 판정 → `CharacterActionPlayer` 재생**이라, 연습용이라도 `Canvas`·`PatternHandler`·`CharacterActionPlayer`가 전부 있어야 한다 — 즉 전투 오브젝트 일습을 탐색 씬에 복제하는 것이고, 그러면 **분리의 유일한 이득이 사라진다**(§0-1).

**그래서 잡몹 전투와 튜토리얼은 "가장 짧은 `Encounter`"로 만든다** — 적 1~2마리(`clusterSizeOverride`), 패턴 2~3개, 10초 안팎의 `SongChart`. **새 코드가 0줄이고 전투가 시작되는 방식이 게임 전체에서 하나로 유지된다.**

- **튜토리얼은 자동으로 연습장이 된다** — §0-6의 `Retry` 상태 덕에 `SSS`를 낼 때까지 몇 번이고 다시 들어갈 수 있고, `retryText`가 그 유도를 맡는다. **연습 모드를 따로 만들지 않는다.**
- **⚠ 짧은 곡일수록 전환 비용이 도드라진다.** 15초 전투에 페이드·언로드·재로드가 붙으므로, 잡몹은 **길목마다 뿌리지 말고 몇 군데에만** 둔다. 이것이 §0-1을 택한 대가이며, 잡몹이 촘촘해져 리듬이 끊기는 것이 실제로 관찰되면 그때 §0-1을 다시 연다(그때의 경로는 리그를 `CombatRig.unity`로 추출해 Additive로 올리는 것이다).
- **못 베는 허수아비 프롭은 탐색 씬에 자유롭게 놓는다** — 그건 §13의 배경 도상이라 리그가 필요 없다. ⚠ 단 **다가가도 아무 일이 없어야 한다**(프롬프트 금지 — §0-7의 예외는 `Retry` 무대 하나뿐이다).
- ⚠ 튜토리얼 `Encounter`는 `unlockFlag`를 비운다(§0-6) — 처음부터 열려 있어야 한다.

>>> 여기까지 이견 있으면 적어 주세요. Step 1부터는 위 결정을 전제로 씁니다.

---

## Step 1 — 페이드 오버레이 (`ScreenFader`)

- [x] `Assets/02. Scripts/UI/ScreenFader.cs` — `Singleton<ScreenFader>`(`DontDestroy = true`), 자식 `CanvasGroup` 알파만 민다. `IEnumerator FadeOut()` / `FadeIn()` 둘.
- [x] `03. Prefabs/Managers.prefab`에 자식으로 추가. `Canvas`는 `ScreenSpaceOverlay` + `sortingOrder` 최대(패턴인풋 위).
- [x] ⚠ 로드 중에도 살아 있어야 하므로 반드시 `Managers` 안이다. 씬에 두면 자기가 언로드된다.
- [x] ⚠ `raycastBlocker`를 페이드 중에만 켠다 — 검은 화면에서 입력이 먹으면 안 된다.

검증: `ContextMenu`로 FadeOut→FadeIn 왕복 1회.

## Step 2 — `Encounter` (탐색 씬 쪽)

- [x] `Assets/02. Scripts/Encounter/Encounter.cs` — 무대 진입 자리 하나. 필드: `SongChart chart` · `string encounterId` · `string unlockFlag` · `string completeFlag` · `string retryText`(프롬프트 겸 대사) · `int clusterSizeOverride`(§13 「면을 쓴 자」가 요구하는 그 필드) · `GameObject shimmerVfx`.
- [x] 트리거 콜라이더(`isTrigger`)로 플레이어 진입 감지 → `EncounterDirector.Enter(this)`.
- [x] ⚠ **프롬프트를 띄우지 않는다**(§13). 들어서면 시작된다.
- [x] `EncounterState State` 게터 — `GameProgress`의 플래그(`unlockFlag`·`completeFlag`)와 등급에서 §0-6의 넷(`Locked`/`Fresh`/`Retry`/`Done`)을 파생한다. **저장하지 않는다**(저장하면 등급과 어긋난 상태가 표현 가능해진다).
- [x] `Start()`에서 `shimmerVfx.SetActive(State == Fresh || State == Retry)`.
- [x] `OnTriggerEnter`: `Fresh`면 `EncounterDirector.Enter(this)`, `Retry`면 상호작용 대기, 나머지는 아무것도 안 한다.
- [x] ⚠ `unlockFlag`가 **비어 있으면 해금된 것으로 본다** — 1스테이지 첫 무대와 FreePlay가 해금을 안 거친다.

## Step 3 — `GameProgress` (자리별 최고 등급 · 해금 플래그)

- [x] `Assets/02. Scripts/GameProgress.cs` — `PlayerPrefs` 한 겹. **자리별 최고 등급과 해금 플래그를 같이 든다**(둘 다 "씬을 넘어 남는 진행도"라 소유자가 하나여야 한다).
  - `GetGrade(id)` / `ReportGrade(id, grade)` — 최고 기록으로만 갱신.
  - `HasFlag(name)` / `SetFlag(name)` — 해금·시퀀스 1회 재생 판정(§0-6).
- [x] ⚠ **`SequenceRunner.SetFlag`와 이름이 같지만 다른 물건이다**(§0-6). 러너 쪽은 씬 한정 런타임 조건이고 이쪽은 영구 진행도다. 헤더 주석에 이 구분을 못박는다.
- [x] ⚠ **`PlayerPrefs`는 임시 백엔드다 — 저장 시스템은 나중에 따로 만든다.** 그래서 **읽고 쓰는 곳은 이 클래스 하나로 모은다**: 다른 클래스가 `PlayerPrefs`를 직접 부르지 않으면 교체가 이 파일 안에서 끝난다. 헤더에 `ponytail:` 주석으로 그 교체 경로를 적어 둔다.
- [x] ⚠ **`PlayerPrefs`가 감당 못 하는 것을 지금 여기 얹지 않는다**(파편 수집 목록·엔딩 분기 이력 같은 구조체). 그런 것이 필요해지는 순간이 곧 저장 시스템을 만들 때다.
- [x] ⚠ **FreePlay 결과는 절대 여기 안 들어간다**(§9) — `EncounterId`가 비면 기록하지 않는다.

## Step 4 — 해금 배선 (시퀀스 → 진행도)

- [x] `Assets/02. Scripts/SequenceSystem/Steps/UnlockStep.cs` — `SequenceStep` 하나. `GameProgress.SetFlag(flagName)`을 부르고 즉시 끝난다.
  - ⚠ **클래스 이름·네임스페이스(`SequenceSpace`)를 나중에 바꾸지 않는다** — `[SerializeReference]`가 그 이름으로 참조를 저장해서, 바꾸면 저작해 둔 시퀀스가 `Managed Reference missing`으로 끊긴다(§15).
- [x] `Assets/02. Scripts/SequenceSystem/SequenceZoneTrigger.cs` — 트리거 콜라이더에 플레이어가 들어오면 `SequenceRunner.Play()`.
  - `onceFlag`(문자열)를 들고, 서 있으면 재생하지 않는다. **그 플래그를 세우는 것은 시퀀스 자신의 `UnlockStep`**이라 "그 장소를 처음 열 때만 재생"(§13)이 **추가 상태 없이** 성립한다.
- [x] ⚠ **카시마의 브리핑은 이동을 멈추지 않는다**(§13) — 이 트리거가 재생하는 시퀀스는 `holdMode = Keep`이다. `Overlay`로 두면 걸어가다 멈춰 선다.
- [x] `Assets/IngameInputs.inputactions`의 **`Explore` 맵에 `Interact` 액션 추가**(지금은 `Move`·`Look` 둘뿐이다). `Player` 맵은 안 건드린다.
- [x] `Encounter`가 `Retry`이고 플레이어가 범위 안이면 `DialogUI.Instance`에 `retryText`를 띄운다. `Interact` 한 번 → `EncounterDirector.Enter(this)`. 범위를 벗어나면 내린다(§0-7).
- [x] ⚠ **모드는 `Explore` 그대로다** — 멈춰 세우지 않으므로 복구할 것이 없다.
- [x] ⚠ **`SequenceRunner.IsPlaying`이면 프롬프트를 띄우지 않는다** — 브리핑과 대사 창을 두고 다투면 한쪽이 다른 쪽을 덮어쓴다.
- [x] ⚠ 내리는 경로가 둘이다 — 범위 이탈 · `OnDisable`. 빠지면 *"대사가 화면에 굳는다"*.
- [x] 검증: 지점 통과 → 대사 한 줄 → 무대가 일렁이기 시작 → 전투 씬 왕복 → **되돌아와도 여전히 일렁이고 브리핑은 다시 안 나온다**.

## Step 5 — `EncounterDirector` (탐색 → 전투)

- [x] `Assets/02. Scripts/Encounter/EncounterDirector.cs` — 탐색 씬에 하나.
- [x] `Enter(Encounter e)` — ⚠ 맨 앞에서 `e.State`가 `Locked`/`Done`이면 그대로 return:
  1. `PlayerModeDirector.SetMode(Overlay)` — 페이드 동안 아무도 위치를 안 건드린다.
  2. `GameSession`에 `SelectedChart` · `ReturnScene`(`SceneManager.GetActiveScene().name`) · `ReturnPosition`/`ReturnYaw`(플레이어의 **지금** transform) · `EncounterId` · `StageIndex` 기입.
  3. `ScreenFader.FadeOut()` → `SceneManager.LoadScene("BattleScene")`.
- [x] ⚠ 복귀 좌표는 **트리거 위치가 아니라 플레이어의 현재 위치**다. 트리거를 쓰면 매번 같은 자리에서 되살아나 "그 자리에 선 채 이어진다"가 깨진다.

## Step 6 — 전투 씬의 진입/복귀 (`BattleSceneBootstrap`)

- [x] `Assets/02. Scripts/Encounter/BattleSceneBootstrap.cs` — `BattleScene`에 하나.
- [x] `Start()`:
  1. `GameSession.StageIndex`로 `StageBackground_Stage{N}` 가산 로드(`LoadSceneAsync(..., Additive)`) — 로드 완료까지 대기.
  2. `EnemyDirector.clusterSize`에 `GameSession.ClusterSizeOverride` 적용(0이면 무시).
  3. `ScreenFader.FadeIn()` → `ChartPlayer.Play()`.
- [x] ⚠ **배경 씬 로드가 끝나기 전에 `Play()`를 부르면 안 된다** — `PrepareStage` 프리웜(§5)이 도는 동안 씬 로드 히치가 겹치면 그대로 판정 손실이다. `countdownDuration` 3초가 프리웜 창인데 그 안에 씬 로드를 넣지 않는다.
- [x] **완곡** — `ChartPlayer.OnSongEnded` 또는 `FinaleSilhouetteDirector.OnFinaleEnded` 구독 →
  1. `GameProgress.ReportGrade(EncounterId, LastResult.Grade)`
  2. `GameProgress.SetFlag(CompleteFlag)` — **이 한 줄이 "완곡하면 다음으로 간다"의 구현 전부다**(§0-9).
  3. `ScreenFader.FadeOut()` → `SceneManager.LoadScene(GameSession.ReturnScene)`.
- [x] ⚠ **`OnSongEnded`는 아웃트로 길이만큼 늦다**(§14가 이미 짚었다). 마무리 실루엣이 있는 곡은 `OnFinaleEnded`가, 없으면 `OnSongEnded`가 트리거다 — 둘 중 먼저 오는 것 하나만 받고 래치로 막는다.
- [x] **중단** — `PlayerHealth.OnDepleted`(§7-6) 구독 → 곡을 멈추고 **아무것도 기록하지 않은 채** 같은 복귀 경로를 탄다(등급도 완곡 플래그도 안 남는다, §0-9).
  - ⚠ **패배 화면을 만들지 않는다.** 그 무대는 `Fresh` 그대로라 다시 들어서면 곧바로 시작된다.
  - ⚠ **기록 분기가 여기 둘뿐이어야 한다.** `ScoreDirector`가 `GameSession.LastResult`를 채우는 것과 `GameProgress`에 남기는 것은 다른 일이다 — 중단에서 `ReportGrade`가 새어 나가면 **죽은 곡의 `D`가 최고 기록으로 박힌다**(최고 기록으로만 갱신하므로 지워지진 않지만 `Fresh`가 `Retry`로 바뀐다).

## Step 7 — 탐색 씬의 복귀 처리 (`ExploreSceneBootstrap`)

- [x] `Assets/02. Scripts/Encounter/ExploreSceneBootstrap.cs` — 탐색 씬에 하나.
- [x] `Start()`: `GameSession.ReturnScene`이 이 씬이면 플레이어를 `ReturnPosition`/`ReturnYaw`로 옮기고 그 필드들을 비운다. 아니면(첫 진입) 씬 저장 위치 그대로.
- [x] `PlayerModeDirector.SetMode(Explore)` → `ScreenFader.FadeIn()`.
- [x] ⚠ **방금 나온 `Encounter` 트리거 안에 되살아난다** — 복귀 즉시 다시 전투로 들어가는 무한 루프다. 복귀 지점이 트리거 안이면 **플레이어가 그 트리거를 한 번 벗어날 때까지 `Encounter`를 잠가 둔다**(`OnTriggerExit`에서 다시 연다). 이 플랜에서 유일하게 미묘한 지점이라 여기 못박는다.

## Step 8 — 씬 만들기 · 배선

- [ ] `StageBackground_Stage1` 추출 — 현재 `BattleScene`의 도장 배경·베이크 라이트를 새 씬으로 옮긴다. 전투 오브젝트 일습은 `BattleScene`에 남긴다.
- [ ] `Explore_Stage1.unity`에도 **같은 배치표로 무대 구조를 세운다**(§0-8) — 그 씬을 열고 `Tools/Story Props/Build Stage Layout/Stage 1`을 한 번 더 돌린다. **무대 중심은 월드 원점.**
- [ ] `SequenceRunner` + `SequenceZoneTrigger` + 그 시퀀스 에셋(`04. Datas/Sequences/Stage1/`) 배선 — 마지막 스텝이 `UnlockStep(1스테이지 무대 플래그)`.
- [ ] `Explore_Stage1.unity` 생성 — 플레이어 프리팹 + `PlayerModeDirector`(Explore) + 탐색 vcam + `EventSystem` + `Managers` + `ExploreSceneBootstrap` + `EncounterDirector` + `Encounter` 하나. **전투 오브젝트 일습(Canvas·`PatternHandler`·`EnemyDirector`·`Pool` 등)은 넣지 않는다.**
- [ ] Build Settings에 세 종류 씬 등록. **인덱스 0 = `Explore_Stage1`** — 게임은 튜토리얼부터 시작한다(별도 타이틀·시작 화면을 두지 않는다).
- [ ] 1스테이지 초입에 **튜토리얼 `Encounter`**를 놓는다(§0-9: 적 1마리, 패턴 2~3, 10초 안팎, `unlockFlag` 비움).
- [ ] ⚠ 스테이지 2~5는 Step 1~8이 1스테이지에서 왕복으로 검증된 **뒤에** 만든다. 먼저 다 만들면 배선 실수가 그만큼 복제된다. **4스테이지는 탐색 씬이 둘**(`Explore_Stage4a`/`4b`)이다(§0-8).

## Step 9 — 문서 갱신

- [x] `CLAUDE.md` §9를 이 구조로 다시 쓴다(현재 "무대 하나 = 씬 하나 = 곡 하나"는 **탐색+전투 한 씬**을 뜻한다 — 정확히 이 플랜이 바꾸는 문장이다).
- [x] `Story_Overview.md` §1-1에 "씬은 나뉘지만 화면에는 페이드뿐"이라는 단서를 한 줄 넣는다. **폐기된 것은 곡 선택 UI이지 씬 파일이 아니다**를 명시.
- [x] ⚠ `Story_Overview.md` **§2-4의 "재도전 메뉴·확인 창이 없다" 문장을 고친다**(§0-6이 폐기했다) — 처음은 들어서면 시작, 재도전만 상호작용.
- [x] ⚠ `CLAUDE.md` §13의 **프롬프트 금지 규칙에 예외 하나를 명시**한다(§0-7) — `Retry` 상태의 무대만이고, 그것도 기능 안내가 아니라 대사다.
- [x] `docs/!Guides/Guide_SceneSplit.md` — 새 스테이지 추가 절차(씬 3장 만들고 `Encounter`에 채보 꽂기).

---

## 이 플랜이 일부러 안 하는 것

| 안 한 것 | 언제 추가하나 |
|---|---|
| 비동기 로딩 진행바 | 로드가 페이드(0.4초)보다 길어지는 것이 실측되면 |
| `SceneTransitionPayload` 전용 클래스 | `GameSession`의 복귀 정보 필드가 8개를 넘으면 |
| 세이브 파일(JSON·암호화) | 저장할 것이 `id → grade`를 넘으면 |
| 저장 시스템(파일·슬롯·클라우드) | `PlayerPrefs`로 표현 못 하는 진행도(파편 목록·엔딩 이력)가 생기면. 창구가 `GameProgress` 하나라 교체는 그 파일 안에서 끝난다 |
| 예/아니오 확인 창 | 실수로 재도전이 시작되는 일이 실제로 관찰되면. 지금은 `Interact` 한 번이 곧 확인이다 |
| 해금 순서표·의존 그래프 | 한 무대가 **여러 조건**을 요구하게 되면. 지금은 플래그 하나 = 무대 하나 |
| 전환 시 카메라 포즈 일치 | 페이드만으로 이음매가 보이는 것이 실제로 확인되면(§0-8이 위치는 이미 맞춰 둔다) |
| 탐색 씬 간 이동(골목·계단참 경계) | 스테이지 2가 생길 때. Step 5의 복귀 정보 구조를 그대로 쓴다 |
| 전투 중 탐색 씬 프리로드 | 복귀 페이드가 눈에 띄게 길면 |


---

## 구현 기록 (2026-08-29)

**Step 1~7 · 9 완료. Step 8(씬 저작)만 남았다** — 코드는 전부 컴파일되고 배선만 없는 상태다.

새 파일:
`GameProgress.cs` · `UI/ScreenFader.cs` · `Encounter/{Encounter,EncounterDirector,BattleSceneBootstrap,ExploreSceneBootstrap}.cs` · `SequenceSystem/Steps/UnlockStep.cs` · `SequenceSystem/SequenceZoneTrigger.cs`

기존 파일 수정(전부 추가만, 기존 동작 변경 없음):
`GameSession.cs`(복귀 정보 6필드) · `InputHandler.cs`(`OnInteractPressed`) · `IngameInputs.inputactions`(Explore/Interact = `<Keyboard>/e`, 래퍼 자동 재생성됨) · `EnemyDirector.cs`(`SetClusterSize`) · `UI/DialogUI.cs`(프롬프트 모드)

### 플랜과 달라진 것 하나

**§0-7은 "`DialogUI`를 그대로 쓴다"였는데 그대로로는 안 됐다** — `DialogUI`는 큐가 비면 **스스로 창을 닫는다**(`Close`). 프롬프트는 내려 달라고 할 때까지 떠 있어야 하므로 `ShowPrompt`/`HidePrompt`를 얹었다.

그런데 그 김에 **§0-7이 `Encounter`에게 지웠던 짐이 없어졌다.** 원래는 *"브리핑이 재생 중이면 프롬프트가 물러난다(`SequenceRunner.IsPlaying`)"*였는데, 그러려면 `EncounterDirector`가 씬의 러너들을 알아야 했다(배선 하나 빠뜨리면 그 씬만 조용히 깨진다). **창의 주인이 둘인 문제를 창을 가진 클래스가 풀게** 했다 — `SetDialog`가 언제나 이기고 그 대사가 끝나면 `Close`가 프롬프트를 되살린다. 그래서 `Encounter`도 `EncounterDirector`도 시퀀스를 모른다.

### Step 8 진행분 (MCP)

**왕복이 실제로 도는 것까지 확인했다.** 플레이 모드에서 트리거 진입 → `BattleScene` 로드 → 곡 재생 → 완곡 처리 → `Explore_Stage1` 복귀 → **떠난 자리 (0,0,0) 복원** → 등급 `D` 기록 + `Stage1.Cleared` 플래그 → 재진입해도 **자동 시작 안 됨(잠금)** → 한 번 벗어났다 들어오니 **프롬프트 표시**(`아직 전부 처리하지 않았어`) → `Interact` → 다시 전투. 콘솔 에러·경고 0.

- [x] `Managers.prefab`에 `ScreenFader` 추가(Canvas `sortingOrder` 32000 + 전체화면 검은 `Cover` + `CanvasGroup`)
- [x] `BattleScene`에 `BattleSceneBootstrap` 추가(`ChartPlayer` 오브젝트에) + 배선
- [x] ⚠ `ChartPlayer.playOnStart`를 **끔** — 부트스트랩이 페이드를 걷은 뒤 `Play()`를 부르므로 켜져 있으면 곡이 두 번 시작된다
- [x] `Explore_Stage1.unity` 생성 — 바닥 · 라이트 · 플레이어 프리팹(+`PlayerExploreMover`/`PlayerModeDirector`/캡슐 콜라이더/kinematic Rigidbody) · `Cam_Explore`(CinemachineCamera, priority 30) · `Main Camera`(Brain) · `Canvas`+`DialogUI` · `EventSystem` · `InputHandler` · `Managers` · `EncounterDirector`+`ExploreSceneBootstrap` · `Encounter_Stage1`
- [x] Build Settings — **인덱스 0 = `Explore_Stage1`**, 1 = `BattleScene`, 2 = `SongSelectScene`
- [ ] `StageBackground_Stage1` 분리 — **보류**(아래 참조)
- [ ] 탐색 씬 드레싱(§13 배치표 · 무대 복제 구조) — 지금은 회색 평면 하나뿐이다
- [ ] 튜토리얼 `Encounter`(짧은 채보) · 브리핑 `SequenceZoneTrigger` — 채보와 시퀀스 에셋이 저작돼야 붙는다

**⚠ `StageBackground_Stage1` 분리를 보류한 이유**: 도장 배경(`Map/Stage/Dojo_Stage`, 649개 · static)이 `DojoLighting`으로 **베이크**돼 있어 다른 씬으로 옮기면 라이트맵 배정이 끊겨 재베이크가 필요하다. 그 분리의 이득(배경 교체)은 **스테이지 2가 생겨야** 발생하고, `Encounter.stageIndex = 0`이면 배경 가산 로드를 건너뛰도록 코드가 이미 돼 있어 1스테이지는 지금 구조로 정상 동작한다. 스테이지 2를 만들 때 같이 한다.

### Step 8에서 주의할 것

- `BattleScene`의 도장 배경·라이트를 `StageBackground_Stage1`로 떼어낼 때 **`Stage` 오브젝트는 `BattleScene`에 남긴다**(`EnemyDirector.arenaCenter`가 가리킨다).
- `Managers` 프리팹에 `ScreenFader` 자식(Canvas + `CanvasGroup`, `sortingOrder` 최대)을 추가한다.
- `EnemyDirector.clusterSize`는 인스펙터 하한이 `[Min(3)]`이지만 `SetClusterSize`는 1까지 받는다(튜토리얼용 — 근거는 그 메서드 주석).
