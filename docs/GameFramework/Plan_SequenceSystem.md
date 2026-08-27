# Plan — SequenceSystem 데이터 중심 재설계 (+ Timeline 연동)

근거: `docs/GameFramework/Research_GameFramework.md` §3 · §4 · §6
작성일: 2026-08-28 / 상태: **구현 완료 (Step 1~8)**

## 목표

시퀀스(대사·브리핑·컷신·튜토리얼)의 **정의를 씬에서 에셋으로 옮긴다.** 지금은 씬에 `SequenceStateBase` 게임오브젝트를 여러 개 배치하는 방식이라, 대사 한 줄을 고치려 해도 씬을 열어야 하고 씬 diff가 오염된다. 서사 분량(`docs/Story/` 5문서 규모)이 계속 늘어날 것이므로 지금 바꾼다.

**동시에 Timeline을 배제하지 않는다.** 조건부 대기·분기는 시퀀스가, 시간축 연출(컷신·카메라 워크)은 Timeline이 맡고, **시퀀스가 Timeline을 한 스텝으로 부른다.**

---

## 1. 설계 개요

```
SequenceAsset (ScriptableObject)          ← 정의. 씬 참조 0개. 러너와 1:1
 ├ PlayerMode holdMode                     이 시퀀스가 도는 동안의 플레이어 모드
 ├ string[] requiredBindings                이 시퀀스가 필요로 하는 씬 오브젝트 슬롯 이름
 └ [SerializeReference] List<SequenceStep>
      ├ WaitStep      { duration }
      ├ MoveToStep    { actorSlot, destination(Vector3), speed }
      ├ DialogStep    { lines }
      ├ WaitFlagStep  { flagName }
      └ TimelineStep  { timeline(TimelineAsset), directorSlot }

SequenceRunner (MonoBehaviour, 씬)        ← 실행 + 씬 참조 배선
 ├ SequenceAsset asset
 ├ SequenceBindings bindings                슬롯 이름 -> 씬 오브젝트 (인스펙터 드래그)
 └ bool playOnStart / Play() / OnFinished

SequenceContext                            ← 스텝이 씬 세계에 닿는 유일한 통로
 ├ MonoBehaviour Owner                      코루틴이 필요한 스텝용
 ├ SequenceBindings Bindings
 └ PlayerModeDirector PlayerMode
```

### 1-1. 왜 `IState` / `StateMachine`을 안 쓰는가

시퀀스는 **선형 큐**다. 조건 전이도 AnyState도 없고, 그래서 `StateMachine`의 기능이 전부 죽는다. 인터페이스만 공유하면 `FixedExecute` 같은 빈 구현이 스텝마다 하나씩 붙는다(Research §D-10). **`SequenceStep`은 자기 인터페이스를 갖는다.**

덤으로 Research §4-2 [1]의 치명 결함 — `SequenceStateBase`가 `MonoBehaviour`인데 파라미터 생성자만 있어 **씬 배치 자체가 불가능**한 문제 — 가 MonoBehaviour를 벗는 순간 사라진다.

### 1-2. 위치는 좌표다 (바인딩을 쓰지 않는다)

> **[2026-08-28 결정] 시퀀스는 씬 전용 일회성이다.** 에셋 하나가 특정 씬의 특정 자리에서 한 번 재생되며, 여러 씬이 공유하는 템플릿은 만들지 않는다. → **에셋과 러너는 1:1**이다.

그래서 위치는 이름표가 필요 없다. 무대 중심이 월드 원점으로 고정이고(CLAUDE.md §11-2) `StageLayoutBuilder`가 이미 배치를 좌표 테이블로 들고 있으므로(§13), **그 씬의 그 자리는 영원히 같은 값이다.**

```csharp
public Vector3 destination;   // 이게 곧 데이터다
```

(초안에 있던 `SequenceLocation`의 Fixed/Binding 이중 모드는 **삭제한다** — 재사용을 대비한 구조였고, 재사용을 안 하기로 했으므로 한쪽이 언제나 죽은 코드가 된다.)

### 1-3. ⚠ 그래도 바인딩은 남는다 — 대상이 위치가 아니라 오브젝트일 때

**재사용 여부와 무관하게 ScriptableObject는 씬 오브젝트를 참조할 수 없다.** 일회성이어도 마찬가지다. 그래서 아래는 여전히 러너를 거쳐야 한다:

- `MoveToStep`이 옮길 대상(어느 NPC인가)
- `TimelineStep`이 재생시킬 `PlayableDirector`
- `WaitFlagStep`의 플래그를 세우는 쪽

**키는 전역 enum이 아니라 에셋이 선언하는 슬롯으로 한다.**

```
SequenceAsset:  requiredBindings = [ "Kashima", "GateDirector" ]   ← 저작자가 이름을 적는다
SequenceRunner: 인스펙터가 그 이름마다 Object 칸을 하나씩 그린다     ← 드래그로 배선
SequenceStep:   드롭다운으로 슬롯을 고른다                          ← 오타 불가능
```

**전역 `enum`을 쓰지 않는 이유**: 스테이지 5개 × 시퀀스 여러 개의 항목이 전부 한 enum에 쌓이고, **대사 하나 추가하는 콘텐츠 작업이 코드 수정을 요구하게 된다.** 에셋이 자기 슬롯을 선언하면 코드가 안 늘고, 러너와 1:1이라 슬롯 수도 몇 개 안 된다. 드롭다운이 오타를 막으므로 문자열 키의 통상적 단점도 없다.

> ⚠ 슬롯은 **이름**으로 참조한다(인덱스가 아니라). 인덱스는 저작 중 순서를 바꾸면 조용히 다른 것을 가리킨다.

### 1-4. ⚠ `Time.timeScale`을 쓰지 않는다 — `PlayerMode`로 표현한다

현재 `DialogState`는 진입 시 `Time.timeScale = 0`을 건다. CLAUDE.md §7-3이 명시적으로 금지하는 것이다 — 판정·클립 정렬은 `Time.time`인데 채보는 `audioSource.time`으로 돌고 **오디오는 timeScale 밖**이라 차이가 영구 누적된다.

대신 **`SequenceAsset.holdMode`가 시퀀스가 도는 동안의 `PlayerMode`를 선언한다.** 러너가 `Play()`에서 걸고 종료 시 `ReturnToPrevious()`로 되돌린다.

| holdMode | 뜻 | 쓰는 곳 |
|---|---|---|
| (기본) 유지 | 모드를 안 건드린다 | 걸으면서 듣는 대사 — 카시마 브리핑(§13) |
| `Overlay` | 이동·입력 정지, 위치의 주인 없음 | 멈춰 서서 보는 대사 |
| `Cutscene` | 위치·카메라를 Timeline이 몬다 | Timeline 컷신 |

**이 필드 하나가 두 문제를 동시에 푼다** — timeScale 금지(§7-3)와 위치 소유권(§11-2). `MoveToStep`이 플레이어를 옮기려면 어차피 두 무버가 다 꺼져 있어야 하는데, `holdMode`가 그걸 이미 보장한다.

### 1-5. Timeline과의 소유권 규칙

**⚠ 계층은 한 방향이다 — 시퀀스가 위, Timeline이 아래.** 시퀀스가 `TimelineStep`으로 Timeline을 부르고, Timeline은 시퀀스를 부르지 않는다. 양방향이면 누가 주인인지 모호해져 `PlayerModeDirector`가 경고하는 그 부류(주인이 둘)가 다시 생긴다.

| 일 | 담당 |
|---|---|
| 조건부 대기("도착할 때까지"), 분기, 순서 | **시퀀스** — Timeline은 시간축 모델이라 이게 어색하다 |
| 정해진 시간축 연출, 카메라 워크, 컷신 | **Timeline** |
| 대사 | **시퀀스**(`DialogStep`) — Timeline의 대사 저작은 빈약하다 |

**Timeline의 씬 참조는 Timeline이 알아서 한다.** `PlayableDirector`가 트랙별 바인딩을 씬에 들고 있으므로(우리 `SequenceBindings`와 정확히 같은 해법), 우리는 **"어느 director인가"만** 바인딩하면 된다. 중복해서 배선하지 않는다.

**⚠ 카메라 소유권**: Timeline의 Cinemachine 트랙은 vcam 우선순위를 몬다. `CameraDirector`/`CameraAngleSwitcher`도 같은 값을 몬다(§7-2 · §7-5). 컷신은 곡 밖에서만 일어나므로 실제 충돌 가능성은 낮지만, `holdMode = Cutscene`이 그 배타성을 표현하는 자리다.

---

## 2. 단계

### - [x] Step 1: 코어 타입

`Assets/02. Scripts/SequenceSystem/` — 네임스페이스 **`SequenceSpace`**.

> **⚠ 네임스페이스를 지금 확정하는 이유**: `[SerializeReference]`는 클래스의 어셈블리·네임스페이스·이름으로 참조를 저장한다. 나중에 옮기면 **저작해 둔 시퀀스의 참조가 전부 끊긴다**(`Managed Reference missing`). 뒤늦게 옮기려면 `[MovedFrom]`을 붙여야 한다.

- `SequenceStep.cs` — `abstract class SequenceStep` (`Enter` / `Tick` / `Exit` / `abstract IsFinished`), `[Serializable]`
- `SequenceBindings.cs` — `[Serializable]` 배열 `(string slot, UnityEngine.Object target)` + `Resolve<T>(slot)`. 미배선·타입 불일치는 `LogError`(프로젝트 관용구 — `WarnIfBothMoversLive`)
- `SequenceContext.cs` — §1의 필드 셋
- `SequenceAsset.cs` — `[CreateAssetMenu(menuName = "Sequence/Sequence Asset")]`, `holdMode` + `requiredBindings` + `[SerializeReference] List<SequenceStep> steps`

**하지 않는 것**: `SequenceLocation`(Fixed/Binding 이중 모드)과 전역 `SequenceBindingKey` enum. 둘 다 초안에 있었으나 §1-2·§1-3의 결정으로 필요 없어졌다 — 위치는 `Vector3`, 슬롯은 에셋이 선언하는 문자열이다.

**하지 않는 것**: asmdef를 두지 않는다. 스텝이 `UnityEngine`에 깊게 의존해 순수 코어로 분리할 것이 없다(`Pattern/Core`·`Score/Core`와 성격이 다르다).

### - [x] Step 2: `SequenceRunner`

- `asset` · `bindings` · `playOnStart` · `Play()` · `Stop()` · `event Action OnFinished`
- 진행: `Update`에서 `Tick` → `IsFinished`면 `Exit` → 다음 스텝 `Enter`. **한 프레임에 여러 스텝이 넘어갈 수 있다**(대기 시간 0인 스텝 연속). 무한 루프 방지 가드를 둔다
- `Play()`에서 `holdMode` 적용, 종료·`Stop()`·`OnDisable` **셋 다**에서 복구
  > ⚠ 복구 경로를 하나라도 빠뜨리면 "시퀀스가 끝났는데 조작이 안 돌아온다"가 된다. `FinaleSilhouetteDirector`가 timeScale 복구에 세 경로를 두는 것과 같은 규율(§14)
- 빈 `steps`에 안전(현재 `SequenceController.Start`는 빈 큐에 `InvalidOperationException` — Research §4-2 [3])

### - [x] Step 3: 기본 스텝 4종

`SequenceSystem/Steps/`

- `WaitStep` — `duration` 경과
- `MoveToStep` — `actorSlot`(비면 플레이어) · `destination`(**`Vector3`**) · `speed` · `arriveRadius`. 물리를 안 쓰고 `transform.position` 대입(프로젝트 규율, §11-2)
  > ⚠ 대상이 플레이어면 `holdMode`가 `Overlay`/`Cutscene`이어야 한다. 아니면 두 무버 중 하나가 같은 프레임에 위치를 덮어쓴다. **`OnValidate`에서 검사해 경고한다**
- `DialogStep` — `List<DialogData>` (Step 4에서 UI 연결)
- `WaitFlagStep` — 씬 쪽(트리거 볼륨 등)이 `SequenceRunner.SetFlag(name)`으로 세우는 플래그를 폴링. 트리거 콜백을 스텝이 직접 못 받는 것에 대한 답이며, **현재도 이미 폴링 방식**(`FinishSequenceCondition`)이라 새 개념이 아니다

### - [x] Step 4: UI 계층 결함 수정 + `DialogStep` 연결

Research §3-2 · §6에서 잡힌 것들. `DialogStep`이 `DialogUI`를 쓰려면 선행돼야 한다.

- ~~`SubUIManager`: 등록을 `Start` → `Awake`로 이동~~ → **[2026-08-28] `SubUIManager`·`UIManager`를 통째로 삭제했다**(Research §3 상단). 클라이언트가 `DialogUI` 하나뿐인 추상화였고, 씬의 UI 계층 중 편입 대상이 하나도 없었다. `DialogUI`가 정적 `Instance`를 들고 `Awake`에서 자기를 등록한다 - 등록 순서 레이스 수정은 그대로 유지된다
- `DialogUI`: `Awake`에서 `Reset()` 직접 호출 제거(§3-2 [4] — 인스펙터 배선을 매 실행 덮어쓴다), 파일 인코딩 UTF-8 복구(§3-2 [5]), `SetDialog`에서 `dialogQueue` 클리어(§3-2 [8])
- `DialogUI.OnExit` 구독 누수(§3-2 [6]): `SetDialog(lines, onFinished)`로 **완료 콜백을 인자로 받게** 바꾼다. `Action` 필드에 `+=` 하는 구조 자체를 없앤다
- `DialogStep`이 `DialogUI.Instance`로 접근. **`timeScale`을 건드리지 않는다**(§1-4)

### - [x] Step 5: `TimelineStep` (Timeline 연동)

- `timeline`(`TimelineAsset`, 에셋 참조) + `directorKey`(binding → 씬의 `PlayableDirector`)
- `Enter`: director에 asset 대입 후 `Play()`. `Exit`: 아직 재생 중이면 `Stop()`
- `IsFinished`: `director.stopped` 이벤트로 플래그를 세우고 그걸 본다. **폴링(`director.state`)을 폴백으로 함께 둔다** — `extrapolationMode`가 `None`이 아니면 `stopped`가 안 나온다
- `OnValidate`: `extrapolationMode != None`이면 경고(그 조합은 스텝이 영원히 안 끝난다)
- 컷신이면 `SequenceAsset.holdMode = Cutscene`을 쓴다 — `PlayerModeDirector.PlayerMode.Cutscene`의 주석(*"엔딩 샷·몽타주. 아무도 위치를 안 건드린다(카메라·타임라인이 몬다)"*)이 **이 용도로 미리 적혀 있던 자리다**
- **하지 않는 것**: Timeline Signal → 시퀀스 제어. 소유권이 양방향이 된다(§1-5). 필요해지면 Signal이 `WaitFlagStep`의 플래그를 세우는 단방향으로만 붙인다

### - [x] Step 6: 저작 에디터

`SequenceSystem/Editor/SequenceRunnerEditor.cs`

- `OnSceneGUI`에서 `MoveToStep.destination`에 `Handles.PositionHandle` — **씬 뷰에서 드래그로 위치를 잡는다.** MonoBehaviour를 배치할 때의 조작감을 그대로 되찾는 부분이며, 이 리팩토링의 체감 비용 대부분이 여기서 없어진다
- `Handles.DrawPolyLine`으로 시퀀스 이동 경로 전체를 그린다(MonoBehaviour 방식으로는 못 보던 것)
- 인스펙터가 `asset.requiredBindings`를 읽어 **슬롯마다 Object 칸을 하나씩** 그리고, 미배선 슬롯을 경고로 표시
- 스텝의 슬롯 선택은 **드롭다운**(`asset.requiredBindings`에서 고름) — 문자열 오타가 불가능해진다. 이게 전역 enum 없이도 안전한 이유다(§1-3)
- 프로젝트에 이미 유사한 에디터 툴이 여럿 있다(`Pattern Chart Tool` · `Animation Clip Trimmer` · `Mesh Slice Baker`) — 결이 맞는다

### - [x] Step 7: 구 코드 제거

- 삭제: `SequenceController.cs` · `SequenceStateBase.cs` · `DialogState.cs` (+ `.meta`)
- 참조 0곳임을 확인(현재도 씬 배선 0 — Research §0)
- ⚠ 파일 삭제 후 Unity 에셋 DB를 `scope: all`로 강제 갱신한다. 안 하면 `CS2001: Source file could not be found`가 난다(FSM 리팩토링에서 실제로 겪음)

### - [x] Step 8: 검증

- Unity 컴파일 에러·경고 0 (MCP `read_console`)
- 샘플 `SequenceAsset` 하나로 스모크 테스트: `Wait → Dialog → MoveTo → Wait` 4스텝이 순서대로 돌고 `OnFinished`가 한 번 난다
- `holdMode = Overlay` 시퀀스 종료 후 **조작이 돌아오는지** 확인(복구 3경로)
- 시퀀스 도중 `Stop()` 호출 시 모드가 복구되는지 확인

---

## 3. 범위 밖 (이번에 하지 않는 것)

- **`EncounterDirector` / `GameMode`** (CLAUDE.md §9) — 시퀀스가 그 위에 얹히지만 별개 작업이다. `SequenceRunner.Play()`가 그때 붙을 진입점이다
- **분기(조건에 따라 다른 스텝으로)** — 지금 필요한 시퀀스는 전부 선형이다. 필요해지면 `BranchStep`을 추가하는 것으로 국소적으로 해결된다
- **FSM D 계열 결함**(Research §2-4) — 첫 실사용처가 정해져야 답이 갈린다
- **`ScoreHudView`·`DodgePointView`의 `SubUIManager` 편입**(Research §5-5) — 루트 Canvas가 `ScreenSpaceOverlay` 하나라는 전제가 §7-5·§11-8에 걸려 있어 따로 검토해야 한다

## 4. 열린 질문

- **바인딩이 실제로 몇 개나 필요한가.** 씬 전용 1:1이라 예상보다 훨씬 적을 수 있다 — 극단적으로 슬롯이 `PlayableDirector` 하나뿐이라면 `SequenceBindings`를 통째로 빼고 러너에 타입 지정 필드를 직접 두는 게 낫다. **첫 시퀀스를 하나 저작해 본 뒤 판단한다**(Step 3 이후)
- `MoveToStep`의 회전 처리 — 진행 방향을 볼지, 별도 `LookAtStep`을 둘지. 첫 사용 사례에서 정한다
- 에셋 배치 위치 — `04. Datas/Sequences/{Stage}/` 를 제안한다(`Patterns`·`Song`과 같은 결). 씬 전용이라 스테이지별로 갈리는 것이 자연스럽다
