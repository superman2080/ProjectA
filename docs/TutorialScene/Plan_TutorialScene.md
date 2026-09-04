# Plan — 튜토리얼 씬

근거: `Research_TutorialScene.md`.
**신규 코드는 시퀀스 스텝 하나(`PatternDrillStep`)이고, 기존 코드 수정은 `SliceTargetDirector.Reserve`의 인자 하나뿐이다.**

## 이 튜토리얼이 가르치는 것
**조작 넷이 아니라 규칙 하나다** — *박에 겹쳐 베지 않으면 완전히 소멸하지 않는다*(`Story_Overview.md` §2-1 · §2-5).
드릴 넷은 그 규칙을 손에 붙이는 순서이고, 박의 출처(병실의 심박·심박수 측정기)는 **여기서 한 글자도 나오지 않는다.**

## 이 씬이 다른 점 (설계의 뼈대)
- 표적이 **날아오지 않는다.** 다다미는 도장(Dojo Stage)에 **미리 서 있고**, 드릴마다 **플레이어가 그 앞으로 걸어간다.**
- 그래서 실패 처리가 `CrushTarget`(소멸)이 아니다 — **아무것도 안 부른다.** 다다미는 그대로 서 있고 패턴만 다시 뜬다.
  `SliceTargetDirector`는 `outcome == Pending`이면 임팩트 시각이 지나도 표적을 건드리지 않고 세워 둔다(기존 분기, 새 코드 0줄).
- 무대 중심·적·채보가 없으므로 `PlayerCombatMover`는 한 프레임도 위치를 안 쓴다(`moving`/`curveTemplate`이 전부 `OnDuelScheduled`에서 온다).
  **그래서 드릴이 플레이어를 직접 옮겨도 위치의 주인이 둘이 되지 않는다**(§11-2).

---

## Step 1: 표적 `SliceSet` 굽기
- [x] 1-1. `Target_TatamiRoll.prefab`에서 **몸통만 든 프리팹**을 뽑는다 (`03. Prefabs/Target_TatamiRoll_Body.prefab`, 자식 `TatamiRoll`만).
      근거: `MeshSliceBakerWindow`가 `GetComponentInChildren<MeshFilter>()` **첫 번째**를 자르는데 지금 첫 번째는 받침대(`StandBase`, 기본 Cube)다.
- [x] 1-2. `TatamiRoll_Diagonal` 세트를 구웠다(`Diagonal` 프리셋 평면 1장, 조각 2개). 굽기는 `Tools/Mesh Slice Baker`의 `BakeStatic` 경로를 그대로 탔다 — 툴 코드는 안 고쳤다.
- [x] 1-3. ⚠ **몸통 프리팹의 피벗이 씬에 놓인 다다미 몸통의 로컬 원점과 같아야 한다.** 드릴이 씬의 몸통을 감추고 같은 자리에 이 세트의 원본을 세우므로, 어긋나면 교체 순간 다다미가 튄다.
- [x] 1-4. 풀 크기 2 이상.

---

## Step 2: `PatternDrillStep` (신규 코드)
`Assets/02. Scripts/SequenceSystem/Steps/PatternDrillStep.cs`

**단위는 패턴 하나가 아니라 '패턴 그룹'이다** — 연속 공격이 "4개를 이어 치고 하나라도 놓치면 4개를 처음부터"를 요구한다. 그룹이 1개짜리면 단일 패턴과 같아서 분기가 늘지 않는다.

- [x] 2-1. 저작 필드
  - `Pattern[] patterns` — 이어 낼 패턴들(1개면 단일 드릴)
  - `[SequenceSlot] patternHandlerSlot` / `targetDirectorSlot` / **`targetAnchorSlot`**
    - `targetAnchorSlot` = 씬에 미리 놓인 **다다미 몸통 Transform**. **이 하나가 표적 위치와 플레이어 목적지를 동시에 준다** — 드릴마다 좌표를 두 번 적지 않는다.
  - `SliceSet target` — 비우면 표적 없이 입력만 익히는 드릴
  - `float standOffDistance = 2.5f` — 다다미에서 이만큼 떨어진 곳에 선다(다다미 → 플레이어 방향은 진입 시점의 현재 위치로 정한다)
  - `float walkSpeed = 3f`, `float turnDuration = 0.15f`
  - `float leadTime = 1.5f`, `float nodeInterval = 0.6f`, `float patternGap = 0.6f`, `float mashWindow = 3f`
  - `float retryDelay = 1.0f`
- [x] 2-2. 진행은 두 국면이다 — **Walk → Drill.**
  - Walk: `targetAnchor` 기준 스탠드오프 지점까지 `transform.position`을 직접 민다(무버가 없으므로 안전). 도착하면 다다미 쪽을 보게 회전.
    ⚠ `MoveToStep`을 따로 나열하지 않는 이유: 목적지가 **앵커에서 파생**되므로 좌표를 저작할 것이 없고, `SequenceAsset.OnValidate`의 "Keep인데 플레이어를 옮긴다" 경고에도 안 걸린다.
  - Drill 진입 시 1회: 씬의 다다미 몸통을 감추고(`SetActive(false)`) 표적을 예약한다.
- [x] 2-3. 패턴 투입(그룹 전체를 한 프레임에)
  - 패턴 k의 상대 시각을 누적해 `handler.SetPattern(patterns[k], times)`를 순서대로 부른다. 큐 겹침은 정상이고 판정 대상은 선두 하나다(§3) — **연속 공격의 겹침이 실제 채보와 같은 경로로 성립한다.**
  - `pattern.IsMash`면 `times = [t, t + mashWindow]`(시각이 시작·끝 둘뿐, §2-1), 아니면 `t + i*nodeInterval`.
- [x] 2-4. 표적 예약 — **드릴당 한 번뿐이다**(재시도해도 다시 예약하지 않는다)
  - `director.Reserve(nextToken--, target, now, now + 0.02f, Vector2.zero, spawnOverride: anchorPos, impactOverride: anchorPos)`
  - 스폰 = 임팩트라 표적은 **이동하지 않고 그 자리에 선다**.
  - ⚠ 토큰은 **음수 static 카운터** — `EnemyDirector`가 같은 씬에 살아 있어도 안 겹친다.
- [x] 2-5. 판정 — `OnPatternComplete`
  - **그룹 전체 성공** → `director.Resolve(token, true)` → 다다미가 갈라진다 → 스텝 종료.
  - **하나라도 실패** → **`Resolve`를 부르지 않는다.** 다다미는 서 있는 채로 남는다.
    `handler.ClearAllPatterns()`로 남은 큐를 걷고 `retryDelay` 뒤 **그룹 전체 재투입**(걷기는 다시 안 한다).
- [x] 2-6. `IsFinished`: 그룹을 통째로 성공했는가.
- [x] 2-7. `Exit`: 구독 해제(`Stop()`·`OnDisable`에서도 러너가 `Exit`을 부른다).
- [x] 2-8. `SequenceAsset.WarnUnknownSlots`의 `switch`에 이 스텝의 세 슬롯을 추가.
- [x] 2-9. `PatternHandler`·`CharacterActionPlayer`는 **한 줄도 안 고친다.**

---

## Step 2b: `SliceTargetDirector.Reserve`에 임팩트 지점 인자 추가 (유일한 기존 코드 수정)
- [x] 2b-1. `Vector3? impactOverride = null` 인자를 더하고 `impactPos` 계산에서 앵커 대신 쓴다(3줄).
      `spawnOverride`가 이미 같은 모양으로 있어 **새 개념이 0개**다.
      근거: 씬의 `impactAnchor`는 무대 정중앙 한 점인데, 튜토리얼은 다다미가 여러 자리에 서 있다.
- [x] 2b-2. 기존 호출자(`EnemyDirector` 한 곳)는 인자를 안 넘기므로 **회귀 0.**

---

## Step 3: `Tutorial.unity` 씬
- [x] 3-1. `BattleScene`(도장 배경 포함)을 복제해 `01. Scenes/Tutorial.unity`로 저장.
- [x] 3-2. `ChartPlayer` 제거/비활성 — 채보가 아니라 시퀀스가 몬다.
- [x] 3-3. `EnemyDirector` 비활성(적 없음). `SliceTargetDirector`는 **남긴다**.
- [x] 3-4. 다다미 넷 배치 완료 — `Map/Tatami/Tatami1~4` = (0,0,4) · (3,0,6) · (-1,0,7.5) · (-4,0,4.5). 전부 무대 원(8m) 안이고 서로 3m 이상 떨어져 있다. 원문: 시작 지점에서 하나씩 옮겨 다닐 수 있게 완만한 동선으로 배치하고, 전부 **무대 원 안 평면**에 둔다(§13).
- [x] 3-5. `Explore_Stage1`의 `Canvas/Dialog` 서브트리를 이 씬 Canvas로 복사하고 `DialogUI` 참조 배선.
      ⚠ 패턴인풋(`PointBackground`, Point_1이 (-700,-700))과 겹치지 않는 자리에 둔다.
- [x] 3-6. `SequenceRunner` 하나 추가 — `asset` = Step 4, `playOnStart = true`, `player`·`playerModeDirector` 배선.
      슬롯 배선: `PatternHandler`, `SliceTargetDirector`, **다다미 몸통 4개**(`Tatami1`~`Tatami4`).
- [x] 3-7. **곡 0 재생용 `AudioSource` 하나**(loop, playOnAwake). 채보가 없으므로 `ChartPlayer`와 무관하고, 드릴 성공/실패에 반응하지 않는다.
- [x] 3-8. `Managers` 프리팹 인스턴스 확인(없으면 무음).

---

## Step 4: `Seq_Tutorial` 에셋 저작
`04. Datas/Sequences/Tutorial/Seq_Tutorial.asset`

- [x] 4-1. `holdMode = Keep`. ⚠ `Overlay`면 `PlayerModeDirector`가 입력을 탐색 모드로 갈아 **패턴을 칠 수 없다**.
- [x] 4-2. `requiredBindings = ["PatternHandler", "SliceTargetDirector", "Tatami1", "Tatami2", "Tatami3", "Tatami4"]`.
- [x] 4-3. 커리큘럼 — 네 드릴, 전부 `Attacker.Player`라 실패해도 플레이어가 맞지 않는다(§6):

| # | 스텝 | 내용 | 다다미 |
|---|---|---|---|
| 1 | Dialog | 첫 인사 · 이어 긋는 법 | |
| 2 | Drill | `Pattern(3, 4, 5)` — 3노드 가로 직선 | Tatami1 |
| 3 | Dialog | 링이 노브에 겹치는 순간이 박이다 · 짚단과 그것들의 차이 | |
| 4 | Drill | `Pattern(6, 7, 3, 4)` — 4노드 꺾임 | Tatami2 |
| 5 | Dialog | 짧은 획이 이어질 때 | |
| 6 | Drill | `PatternChain(3, 4)` → `PatternChain(4)` → `PatternChain(6, 4)` → `PatternChain(4, 6)` **한 그룹** | Tatami3 |
| 7 | Dialog | 박을 잡을 수 없을 땐 두드려 무너뜨린다 | |
| 8 | Drill | `PatternMash(4)` — 20타, `mashWindow` 3초 | Tatami4 |
| 9 | Dialog | 마무리 | |
| 10 | Unlock | `tutorial_done` | |

- [x] 4-4. **튜토리얼이 가르치는 것은 조작이 아니라 규칙 하나다** — *박(拍)에 겹쳐 베지 않으면 완전히 소멸하지 않는다*(`Story_Overview.md` §2-1).
      이것이 이 게임에서 리듬이 **장르가 아니라 필요**인 이유이고, 튜토리얼은 그 사실을 **말이 아니라 순서로** 가르친다:
  - **다다미는 균열에서 태어난 것이 아니라 연습용 짚단이다** — 박을 안 타도 잘린다. 그래서 드릴 1·2는 **손만** 가르치고,
    카시마가 박을 꺼내는 것은 **타이밍 대사(3번)에서 처음**이다. *"여기선 박을 안 타도 잘려. 그것들은 아니야."*
  - ⚠ **박이 어디서 오는지는 말하지 않는다**(§2-5 — 그 정체는 병실의 심박·심박수 측정기이며 트루 엔딩에서 소리로만 회수된다).
    카시마가 쓰는 말은 **박 · 호흡 · 숨** 셋뿐이고, "BPM" · "리듬" · "박자를 맞춰라"는 세계 안에서 쓰지 않는다.
  - ⚠ 튜토리얼에 심박 관련 시각 단서(파형·수치·붉은 맥동)를 **하나도 두지 않는다.** 1스테이지의 −18dB 전자음이 유일한 채널이다.
- [x] 4-4b. **대사 톤 — 카시마는 다정하고 어른스럽다.** 다그치지 않고, 실패를 나무라지 않으며, 조작을 "기능"이 아니라 **검술 지도**로 말한다(§0: 조작은 설명해도 **세계는 설명하지 않는다**).
  초안:
  - (1) "천천히 해도 괜찮아. 손이 먼저 기억할 테니까."
  - (1) "점을 순서대로 이어 그으면 돼. 그게 전부야."
  - (3) "원이 점 크기로 줄어드는 그 순간 — 거기에 박이 있어. 기다렸다가 겹치면 돼."
  - (3) "여기선 박을 놓쳐도 짚단은 잘려. …그것들은 아니야. 박이 안 맞으면 흩어졌다가 다시 일어나."
  - (5) "짧은 획이 이어질 때가 있어. 하나씩 끊지 말고, 한 호흡으로."
  - (7) "박을 잡을 수 없는 것도 있어. 그럴 땐 무너질 때까지 두드리면 돼."
  - (9) "잘했어. 이제 손이 알고 있으니까 — 나머지는 숨만 맞추면 돼."
  - 실패해도 대사가 끼어들지 않는다(재시도는 조용히 다시 뜬다). 격려 대사를 넣으면 실패가 반복될 때 그 말이 환경음이 된다.
- [x] 4-5. 확인해 둘 것
  - ⚠ **`PatternChain(4, 6).asset`의 내용이 이름과 다르다** — `patternDatas`가 `3, 4`라 `PatternChain(3, 4)`와 **같은 모양**이다. 그대로 쓰면 6번 드릴에서 같은 패턴을 두 번 친다.
  - `PatternMash(4)`는 `playerAttack`이 비어 있어 **마무리 일격이 없고 입력 마감 = `Deadline`**이다(§2-1의 정상 경로). `mashHitClips` 8개는 배선돼 있다. 20타/3초 = 6.7타/초라 튜토리얼로 빡세면 `mashWindow`를 늘린다.

---

## Step 5: 종료 (행선지는 보류)
- [x] 5-1. `UnlockStep`으로 `tutorial_done` 플래그만 세우고 끝낸다.
- [x] 5-2. 다음 씬 로드는 **붙이지 않는다** — 행선지가 정해지면 `SequenceRunner.OnFinished`에 한 줄로 붙는다.

---

## Step 6: 검증
- [x] 6-1. (플레이 검증) 드릴마다 플레이어가 다음 다다미 앞으로 걸어가 그쪽을 보고 선다.
- [x] 6-2. 성공하면 그 다다미가 갈라진다.
- [x] 6-3. **일부러 틀리면 다다미가 그대로 서 있고**, `retryDelay` 뒤 같은 패턴이 다시 뜬다(걷기는 다시 안 한다).
- [x] 6-4. 연속 공격 그룹에서 **두 번째 패턴을 틀리면 남은 3·4번째가 큐에서 걷히고** 네 개가 처음부터 다시 뜬다.
- [x] 6-5. 재시도를 반복해도 링·가이드라인·노브가 누적되지 않는다.
- [x] 6-6. 씬을 벗어나도(`OnDisable`) 구독이 남지 않는다.
- [x] 6-7. `[SequenceBindings] 슬롯 ... 배선되지 않았습니다` 경고가 없다.

---

## 하지 않는 것
- 튜토리얼 전용 `SongChart`·오디오 재생 — 성공할 때까지 기다려야 하므로 오디오 시각과 맞지 않는다.
- 실패 시 격려 대사·전용 UI 화살표·판정 완화 — 요청 밖이고, 필요해지면 드릴 스텝에 필드 하나로 붙는다.
- `PatternHandler` 수정 — 확장 포인트(이벤트) 밖으로 나갈 이유가 없다.

---

## 미결정 (구현 전에 정할 것)

| # | 정할 것 | 권장 | 근거 |
|---|---|---|---|
| A | **튜토리얼에서 박을 무엇으로 들려주나** | **들려주지 않는다.** 박은 포커스 링과 판정음으로만. 대신 **튜토리얼 전용 트랙(곡 0)**을 깐다 — 차분한 동양풍 실내악, **무타악 · 맥 없음 · 90초 루프**(`Story_Music.md` §3 곡 0) | 소리가 박을 세어 주면 1스테이지의 곡 시작이 사건이 아니라 이어짐이 된다. 전자음도 한 겹 없어야 그 첫 등장이 1스테이지로 남는다 |
| B | **드릴의 노드 간격** | `nodeInterval = 0.571s`(= 105 BPM 4분음표) · `patternGap`도 그 배수 | 1곡이 105 BPM(§Music 1-2)이라 **첫 곡이 시작되는 순간 손이 이미 그 간격을 알고 있다**. 0.6초 같은 임의값이면 그 이월이 없다 |
| C | **카시마가 화면에 보이는가** | **안 보인다 — 대사창만.** 모델·프리팹 0 | 지금 카시마 프리팹이 없다. 서사적으로도 그는 "칼을 건네고 길을 알려 주는" 존재라 목소리만으로 성립하며, 모델을 세우면 리깅·로코모션·시선 처리가 통째로 따라온다. 필요해지면 `MoveToStep` + 슬롯 하나로 나중에 붙는다 |
| D | **튜토리얼 진입 경로** | 게임 시작 씬을 `Tutorial.unity`로 둔다(행선지는 §Step 5대로 보류) | 행선지가 안 정해졌어도 진입은 정해야 씬을 열어 볼 수 있다 |
| E | **다시 볼 수 있나 / 스킵되나** | **둘 다 없다.** `tutorial_done`이 서면 끝 | 스킵 UI는 §0의 "게임이 자기 구조를 설명하는" 부류다. 재연습이 필요하면 §2-4의 `Retry` 자리가 이미 그 역할이다 |
| F | **연타 난이도** | `mashWindow`를 3초 → **4초**(20타 = 5타/초) | 튜토리얼에서 처음 만나는 조작이다. 곡에서는 그대로 3초를 쓴다 |
| G | **다다미 4개의 자리와 시작 위치** | 씬 작업에서 확정(Step 3-4) | 좌표는 앵커에서 파생되므로 에셋 저작에 영향이 없다 — 씬만 고치면 된다 |

⚠ A·B는 **같은 결정의 앞뒤다** — 박을 소리로 안 주기로 하면, 등간격을 정확히 첫 곡의 격자에 맞추는 것이 그 대신이 된다.

---

## 구현 기록 (2026-09-01)

- **신규 코드 1개** — `SequenceSystem/Steps/PatternDrillStep.cs`.
- **기존 코드 수정 2곳** — `SliceTargetDirector.Reserve`에 `impactOverride` 인자(3줄), `SequenceAsset.WarnUnknownSlots`가 슬롯을 여럿 검사하도록(스텝당 슬롯이 셋이라).
  `PatternHandler`·`CharacterActionPlayer`·`EnemyDirector`는 한 줄도 안 고쳤다.
- **에셋** — `03. Prefabs/Target_TatamiRoll_Body.prefab` · `04. Datas/Slice/TatamiRoll_Diagonal/` · `04. Datas/Sequences/Tutorial/Seq_Tutorial.asset`.
- **씬** — `01. Scenes/Tutorial.unity`(BattleScene 복제). `ChartPlayer`(+`BattleSceneBootstrap`)와 `EnemyDirector`(+`DodgeDirector`) 비활성, 다다미 4개, `Canvas/Dialog` + `DialogUI`, `Manager/SequenceRunner`, `Manager/TutorialMusic`(곡 0 자리).
- **⚠ 복제본이 오토플레이를 물려받고 있었다** — `BattleScene`의 `debugInputEnabled`/`debugAutoPerfect`가 켜져 있어 첫 플레이에서 드릴이 **입력 없이 저절로 성공**했다. 튜토리얼 씬에서는 껐다.
  (그 사고가 성공 경로 전체를 대신 검증해 줬다: 걷기 → 다다미 감추기 → 예약 → 투입 → 성공 → `Resolve(true)` → 절단.)
- **플레이 검증** — 대사 → 걷기(도착 z=1.36, 목표 1.5) → 다다미 몸통 비활성 · 표적 예약(token −1) → 실패해도 `resolved=False`로 **(0, 0.9, 4)에 그대로 서 있음** → `retryDelay` 뒤 재투입. 콘솔 에러·경고 0건.

### 한글 폰트 (TMP)
- `Assets/09. Fonts/NotoSansKR.ttf`(윈도우 동봉 Noto Sans KR, **SIL OFL이라 빌드에 넣어도 된다** — Malgun Gothic은 재배포 제한이 있어 피했다) + `NotoSansKR SDF.asset`.
- **Dynamic 아틀라스다**(2048x2048, SDFAA, 샘플링 90). 한글은 조합 음절이 1만 자를 넘어 **전부 굽는 것이 실무적이지 않다** — 쓰이는 글자만 런타임에 채우므로 **대사를 고쳐도 다시 구울 필요가 없다.**
- **`TMP_Settings`의 폴백 목록에 넣었다** — 기존 텍스트(LiberationSans)가 한글을 만나면 여기서 글리프를 얻는다. 다른 씬도 같이 해결된다.
- 튜토리얼 씬의 TMP 텍스트 4개(`ScoreLabel` · `ComboLabel` · `Dialog/Name` · `Dialog/Script`)는 폴백을 거치지 않도록 직접 이 폰트로 바꿨다.
- 검증: 시퀀스 대사 전문(278자)을 한 번에 렌더해 **빠진 글리프 0개**.

### 조작감 — 마지막 노드 유예 (2026-09-01 추가)
- 증상: `Pattern(6, 7, 3, 4)`의 마지막 노트만 유난히 빡빡하다.
- 원인 둘이 겹쳐 있었다.
  1. **버그** — `OnPointPressed`가 오답 입력까지 `connectedIndices`에 넣고 그 다음 중복 가드가 막아, **순서 전에 한 번 스친 노드는 그 패턴에서 영영 죽었다.** 지금 기다리는 노드를 가드에서 뺐다(`CLAUDE.md` §3).
  2. **구조** — 중간 노드는 늦어도 `Deadline` 전이면 입력이 먹지만 마지막 노드의 지각 허용치는 `goodWindow`(0.1초)뿐이다. `PatternHandler.judgeGraceDuration`이 **회수만** 미뤄 그 창을 넓힌다(튜토리얼 씬 = **1.5초**, 다른 씬 = 0). `Deadline`(임팩트 앵커)과 판정 결과는 그대로다.
- 검증: 마지막 노드를 `Deadline + 0.8초`에 눌러도 입력이 등록되고 패턴이 완료된다(판정은 Miss).
- **판정 창도 씬에서 넓혔다** — `Tutorial` 씬은 `perfectWindow` 0.10 / `goodWindow` 0.25(전투 씬은 0.05 / 0.10). 코드 변경 0줄이다.
  근거: 판정 자체는 노드마다 대칭인데 **복구 가능성이 비대칭**이다 — 중간 노드는 늦어도 다음 노드로 진행되지만 마지막 노드는 거기서 패턴이 끝나 만회할 자리가 없다.
  0.571초 간격에서 앞 노드에 쌓인 지각이 정확히 마지막에서 터지므로, 손잡이는 유예가 아니라 **창 자체**여야 했다.
  검증: 앞 세 노드 +0.085초 · 마지막 +0.206초 지각으로 눌렀을 때 마지막이 **Good**으로 잡히고 `AllCorrect=True`(예전 값이면 Miss → 재시도).
- ⚠ **그래도 창 밖이면 Miss라 드릴은 재시도된다.** 늦은 마지막 타를 '통과'로도 쳐 주려면 드릴의 통과 기준을 `AllCorrect` → '완주'로 바꿔야 하는데, 그건 "박에 겹쳐야 완전히 소멸한다"는 이 튜토리얼의 교육 목적(§4-4)과 정면으로 부딪히므로 **하지 않았다.**

## 남은 것 (에셋 제작 · 코드 아님)

1. **곡 0(튜토리얼 트랙)** — `Story_Music.md` §3의 프롬프트로 생성해 `Manager/TutorialMusic`의 `AudioSource.clip`에 꽂는다. 지금은 무음.
2. **대사 창 레이아웃** — `Canvas/Dialog`를 화면 하단 중앙(2400x460, y+120)에 임시 배치했다. 탐색 씬은 월드 스페이스 캔버스였고 여기는 `ScreenSpaceOverlay`라 **눈으로 한 번 맞춰야 한다.**
3. **다다미 동선** — 좌표는 앵커에서 파생되므로 씬에서 옮기기만 하면 된다(에셋 수정 불필요).
4. **행선지** — `tutorial_done` 플래그만 세우고 끝난다. 정해지면 `SequenceRunner.OnFinished`에 한 줄.
