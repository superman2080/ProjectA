# Plan — 튜토리얼 재설계 (호출 → 달려감 → 도착 → 허물)

근거: `Research_TutorialCall.md`. 서사는 `docs/Story/Story_Events.md` §0이 이긴다.

**신규 클래스 셋 · 기존 클래스 수정 한 줄.** 나머지는 전부 씬 배선과 시퀀스 저작이다.

| 새로 만드는 것 | 무엇을 하나 |
|---|---|
| `TutorialDirector` (씬 MonoBehaviour) | 튜토리얼 씬 전용 글루 셋 — 무대 준비 · 제자리 달리기 · 첫 노드 슬로우 |
| `TutorialCueStep` (시퀀스 스텝) | 위 셋 중 하나를 켠다. `HighlightStep`처럼 **기다리지 않는다**(`IsFinished` 언제나 true) |
| `CameraShotStep` (시퀀스 스텝) | 샷 vcam 하나를 올리고 그 길이만큼 기다린다. **Timeline을 쓰지 않는다**(아래 Step 6) |

> 셋을 한 클래스에 두는 이유: 전부 **이 씬에서만 참인 반칙**이다(`Time.timeScale`은 §7-3이 곡이 도는 씬에서 금지하고,
> 제자리 달리기는 위치를 안 옮기는 씬에서만 성립한다). 흩어 두면 전투 씬에 실려 갈 자리가 생긴다.
> 나누고 싶어지면 그때 셋으로 쪼갠다.

---

## Step 1 — `TutorialDirector` (신규)

- [x] `Assets/02. Scripts/UI/TutorialDirector.cs` (전역 네임스페이스, `TutorialHighlightView`와 같은 자리)

세 가지 public 진입점만 갖는다.

```
public void PrepareStage(int clusterSize)   // SetClusterSize → PrepareStage 순서 고정
public void SetRunning(bool on)             // Update에서 SetExploreLocomotion을 매 프레임 호출
public void ArmSlowMo()                     // 다음 판정 대상의 첫 노드에서 한 번만 감속
```

### 1-1. 달리기 — 제자리와 실제 이동 둘
- `Update`에서 `on`이면 `actionPlayer.SetExploreLocomotion(runSpeed, sprinting: true)`.
- ⚠ **매 프레임이다** — 래치(`convergeUntil`)가 만료되면 Idle로 덮인다.
- ⚠ **위치를 한 밀리미터도 안 옮긴다.** 달려가는 느낌은 카메라 구도가 만든다(Step 5).
- `runSpeed`는 인스펙터 값 하나. `exploreRunReferenceSpeed`(4.5)와 맞춰 두면 발이 안 미끄러진다.
- **`RunTo()`가 실제 이동이다** — `runDestination`(씬의 `Stage`)까지 `runSpeed`로 밀고, 도착하면 `RunArrived`가 true가 된다.
- **카메라는 고삐로만 끈다**(`runCamera` · `runCameraMaxDistance`) — 그 거리까지는 제자리에서 미오가 멀어지는 것을 보여 주고, 넘으면 딱 그 거리를 유지한다.
  ⚠ `CinemachineFollow`를 붙이면 **처음부터** 따라붙어 "제자리에 선 카메라를 두고 달려 나간다"가 아예 안 나온다. 조준은 vcam의 `RotationComposer`(LookAt = 플레이어)가 이미 한다.
  ⚠ **높이는 안 따라간다** — y를 같이 끌면 지면 기복이 그대로 카메라에 실린다.
  ⚠ 여기서 `transform`을 옮겨도 되는 이유: 이 구간에는 패턴이 큐에 하나도 없어 `OnDuelScheduled`가 안 나고,
  `PlayerCombatMover`도 거리 커브도 한 프레임도 위치를 안 쓴다(§11-2 — `PatternDrillStep`이 다다미로 걸어갈 때와 같은 근거).

### 1-2. 무대 준비
- `enemyDirector.SetClusterSize(n)` → `enemyDirector.PrepareStage()`. **순서가 계약이다**(뒤면 인원과 배치가 어긋난다).
- ⚠ 여기서 `Instantiate`(프리웜)가 일어난다. **전투가 아니라 대사 구간에서 부른다.**

### 1-3. 첫 노드 슬로우
- `handler.OnJudgeTargetBegan`을 구독한다. **`armed`일 때만** `info.FirstNodeTime`을 집고 무장을 내린다
  (사슬 드릴은 판정 대상이 넷이라, 무장이 없으면 넷 다 느려진다).
- `Update`: `Time.time >= FirstNodeTime - slowLead`면 `Time.timeScale = slowScale`(기본 `0.25`).
- **복구 경로 셋** — ⚠ 하나라도 빠지면 게임이 0.25배속으로 굳는다.
  1. 그 노드가 판정됨 (`OnJudged` / `OnNodeConnected`)
  2. `Time.time > FirstNodeTime + goodWindow` (안 눌렀다 — 슬로우는 보험이지 감옥이 아니다)
  3. `OnDisable`
- ⚠ **`Time.timeScale`을 건드리는 유일한 클래스가 이 씬에서는 이것이다.** §7-3의 금지는
  *두 시계(오디오·게임)가 어긋날 수 있는 동안* 금지이고, 이 씬은 `ChartPlayer`가 꺼져 있어 시계가 하나뿐이다.
  헤더 주석에 그 경계를 못박는다.
- ⚠ **노드가 하나뿐인 패턴**(`PatternChain(4)`)에서는 첫 노드 = 마지막 노드라 **메인 히트스톱과 겹친다** —
  실시간 정지가 `1/slowScale`배로 늘어난다. 그때는 그 드릴에 무장을 걸지 않는다(저작으로 해결, 코드 분기 없음).

## Step 2 — `TutorialCueStep` (신규)

- [x] `Assets/02. Scripts/SequenceSystem/Steps/TutorialCueStep.cs` (`SequenceSpace`)

```
enum Action { PrepareStage, RunOn, RunOff, ArmSlowMo }
[SequenceSlot] string tutorialDirectorSlot;
int value;                      // PrepareStage의 인원
IsFinished => true              // HighlightStep과 같다 - 상태만 바꾸고 기다림은 다음 스텝이 한다
```

- ⚠ **클래스 이름과 네임스페이스를 나중에 바꾸지 않는다**(`[SerializeReference]`가 이름으로 저장한다).

## Step 3 — `PatternDrillStep` 수정 (한 줄)

- [x] 슬롯 필드 하나 추가: `[SequenceSlot] string tutorialDirectorSlot`
- [x] `Feed()` 한 곳에서 무장 — 첫 투입(`BeginDrill`)과 실패 후 재투입(`TickDrill`)이 둘 다 지난다(`TickDrill`의 재투입)에서 `director?.ArmSlowMo()` 호출

무장을 드릴이 거는 이유: **"드릴의 첫 노드"를 아는 것은 드릴뿐**이다(§Research 2-4).
재시도에서도 걸리므로 실패한 플레이어가 다시 시도할 때도 보험이 살아 있다.

## Step 4 — 시퀀스 재저작 (`Seq_Tutorial`)

⚠ **설명을 줄인다.** 지금의 여섯 국면 캡션 뭉치를 걷어내고, 캡션은 **"링이 줄어들면 N번"** 수준으로만 남긴다.
대사는 태도, 캡션은 조작 — 겹치면 캡션이 진다.

`D`=Dialog · `Cue`=TutorialCueStep · `Shot`=CameraShotStep · `H`=Highlight · `Drill`=PatternDrillStep

### A. 호출 (제자리 달리기 → 실제 이동)
| # | 스텝 | 내용 |
|---|---|---|
| 0 | Shot | `CamCall` prio 15 · dur 0 — **미오를 비스듬히 내려다본다**(높이 3.2m · 피치 27°). 제자리에 선 샷이라 미오가 달려 나가면 멀어지고, **`runCameraMaxDistance`(9m)를 넘으면 그 거리를 유지하며 뒤따른다**(고삐) |
| 1 | D | 카시마 — *"미오. 자는데 미안해."* / *"북쪽 상가야. 균열 셋. 크진 않아."* |
| 2 | Cue | `PrepareStage` (인원 4) — **허물이 도착 지점에 미리 선다.** 프리웜 히치도 대사에 숨는다 |
| 3 | D | 카시마 — *"천천히 가. 서두르면 박을 놓쳐."* / *"파수꾼 미오. 균열을 봉인해라."* <br> 미오 — *"파수꾼 미오, 균열을 봉인하겠습니다."* |
| 4 | Cue | `RunTo` — **여기서부터 실제로 이동한다.** 도착할 때까지 기다리는 유일한 Cue |
| 5 | Cue | `RunOff` |

⚠ **대사 구간에 제자리 달리기를 두지 않는다.** 미오는 **서서 전화를 받는다** — 다리만 움직이고 세상이 서 있으면
달리는 것으로 안 읽히기 때문이고, 실제 이동(`RunTo`)이 그 자리를 대신한다.
그래서 배경 스크롤(`scrollRoot`)과 `Map/RunBackdrop`은 **통째로 제거했다**(`RunOn` Action은 enum 서수 때문에 남겨 둔다).

⚠ **무리는 무대 중심이 아니라 `PrepareStage`를 부르는 순간의 <b>플레이어 위치</b>를 기준으로 선다**(`EnemyRing.PickClusterCenter` — `arenaCenter`는 범위만 제한한다). 그대로 부르면 아직 출발 지점에 있는 미오 근처, 즉 **가는 길목에 허물이 선다**(실측: 도착점이 원점인데 z −13~−20).
⚠ **세워 두는 것만으로는 부족하다 — 재워야 한다.** `TickWander`는 배치가 아니라 **매 프레임** 플레이어를 중심으로 궤도 슬롯을 나눠 주므로(`standoffDistance`), 그냥 두면 허물이 **25m 밖의 미오에게 걸어온다**. `PrepareStage` 끝에서 `EnemyDirector.enabled = false`로 재우고 도착(`RunOff`)에서 깨운다 — 슬롯 배정이 멎어 **배회가 시작조차 안 되고**(`wandering`이 false로 남는다), `EnemyView`는 자기 `Update`로 계속 도므로 **등장 이동은 그대로 마친 뒤 그 자리에 선다**.

→ **도착 지점에 세우려면 그 자리에서 부르면 된다.** `TutorialDirector.PrepareStage`가 플레이어를 목적지로 옮겼다가 **같은 프레임에 되돌린다** — 아무도 못 보고, `EnemyDirector`에 배치 좌표를 넘기는 새 API도 필요 없다. 덤으로 `EnemyView.Setup`과 `DesignateStagedCenter`가 같은 위치를 봐서 허물이 도착 지점을 향해 선다.
⚠ **배경 스크롤은 제자리 구간에만 돈다.** 실제 이동 중에도 흘리면 세상이 두 번 움직여 속도가 배로 보인다.

⚠ 급한 것은 **상황**이지 그의 말투가 아니다(`Story_Events.md` — 사건 0의 격앙은 0단).

### B. 도착
| # | 스텝 | 내용 |
|---|---|---|
| 7 | Shot | `CamArrival` prio 16 · dur 3.5초 · 끝나며 복귀 — **미오의 로우앵글에서 무대 중심(허물 쪽)을 본다** |
| 8 | Shot | `CamCall` prio 0 · dur 0 — 되돌리기(게임플레이 구도로 넘긴다) |

⚠ 샷은 **카메라만** 몬다 — 허물은 풀에서 나오므로 어차피 저작 대상이 아니다.
⚠ `CamArrival`의 `LookAt`은 **무대 중심(`Stage`)**이다. 허물 자리가 런타임이라 각도를 박아 둘 수 없고,
   "전체를 한 화면에" 대신 **그 지점을 바라보는 것**으로 정했다(사용자 결정).

### C. 드릴 넷 (기존 순서·패턴 유지, 표적만 허물)
| # | 스텝 | 내용 |
|---|---|---|
| 7 | H | 캡션 *"링이 점 크기가 될 때 누른다"* |
| 8 | Drill | `Pattern(3, 4, 5)` — **`targetDirectorSlot`·`targetAnchorSlot` 비움** |
| 9 | H | 캡션 *"반투명한 길이 그을 순서다"* |
| 10 | Drill | `Pattern(6, 7, 3, 4)` |
| 11 | D | 카시마 — *"짧은 획이 이어질 때가 있어. 하나씩 끊지 말고, 한 호흡으로."* |
| 12 | Drill | `PatternChain(3, 4)` → `(4, 6)` → `(6, 4)` → `(4)` — ⚠ 마지막이 1노드라 슬로우 무장을 뺀다(Step 1-3) |
| 13 | D | 카시마 — *"박을 잡을 수 없는 것도 있어. 무너질 때까지 두드려."* |
| 14 | H | `Point5` · 캡션 *"순서가 없다. 아무 점이나"* |
| 15 | Drill | `PatternMash(4)` |
| 16 | H | 끄기 |

### D. 마무리
| # | 스텝 | 내용 |
|---|---|---|
| 17 | D | 허물 — *"파수꾼 미오, 균열을 봉인하겠습니다."* (⚠ 미오가 방금 한 말) <br> 미오 — *"……방금, 제가 한 말인데요."* <br> 카시마 — *"괜찮아. 저것들은 방금 들은 걸 흉내 내. 저희 말이 없어서 그래."* |
| 18 | Unlock | `tutorial_done` |

⚠ 17번이 사건 0의 **질문**이다(`Story_Events.md`). 튜토리얼이 끝나기 전에 이미 이상해야 한다.

- [x] `requiredBindings`에서 `SliceTargetDirector` · `Tatami1~4` 제거, `TutorialDirector` · `CamCall` · `CamArrival` 추가
- [x] 드릴 넷의 `targetDirectorSlot` · `targetAnchorSlot` · `target` 비우기
- [x] 위 표대로 스텝 재배치(20스텝)

## Step 5 — 씬 배선 (`Tutorial.unity`)

- [x] `EnemyDirector` 활성화 · 로스터(`EnemyDefinition`)와 `DeathSliceSet` 확인
- [x] `TutorialDirector` 오브젝트 추가(`Manager/TutorialDirector`) → `PatternHandler` · `EnemyDirector` · `CharacterActionPlayer` 배선
- [x] vcam 둘 — `Cam_Call`(달리는 구간: 미오를 크게 잡고 **외곽 배경을 적게 노출**해 배경이 안 흐르는 것을 감춘다)
      · `Cam_Arrival`(로우 앵글 전신 + `CinemachineSplineDolly`로 허물 쪽 팬)
- [x] ⚠ 두 vcam의 휴지 우선순위는 **0**, 샷 동안만 **15** — 게임플레이(`activePriority` 10)를 이기고 인트로(20)에 진다
- [x] `Tatami` 넷과 `SliceTargetDirector` 배선 제거(오브젝트는 프롭 작업 전까지 남겨 둬도 무해하다)
- [x] `SequenceRunner`의 슬롯 갱신
- [ ] ⚠ 프롭·라이트(밤거리)는 **이 플랜의 범위 밖**이다. 지금은 도장 배경 위에서 로직만 성립시킨다.

## Step 6 — `CameraShotStep` (신규) · 샷 vcam 저작

- [x] `Assets/02. Scripts/SequenceSystem/Steps/CameraShotStep.cs` (`SequenceSpace`)

```
[SequenceSlot] string cameraSlot;   // CinemachineCamera
int   priority = 15;                // 게임플레이(10) 위, 인트로(20) 아래
float duration;                     // 0이면 즉시 끝난다(컷만 하고 다음 스텝으로)
AnimationCurve ease;                // vcam에 SplineDolly가 있으면 CameraPosition을 0→1로 민다
bool  restoreOnExit;                // 다음 샷이 이길 거면 false
```

`Enter` 원래 우선순위 캐시 → `priority` 대입 → 돌리가 있으면 `CameraPosition = 0`
`Tick` 진행도를 민다 · `IsFinished` `ElapsedInStep >= duration` · `Exit` `restoreOnExit`면 되돌린다

**Timeline을 쓰지 않는 이유**: 이 컷이 요구하는 트랙이 카메라 하나뿐이다. 적은 풀에서 나와 트랙에 못 묶이고
플레이어는 제자리에 서 있으므로, `PlayableDirector` · 타임라인 에셋 · 트랙 바인딩은 **배선 비용만 남고 이득이 없다.**
카메라 이동을 만드는 것은 이미 Cinemachine 블렌드와 `CinemachineSplineDolly`이고,
후자를 미는 코드는 `CameraDirector.IntroRoutine`(1122~1127행)에 이미 있는 열 줄이다.
⚠ **`TimelineStep`은 지우지 않는다** — 컷 안에서 카메라 말고 다른 것이 시각에 맞춰 움직여야 할 때 그때 쓴다.

- [x] ⚠ **Cinemachine 타입이 네 번째 이음매로 등장한다**(§7-2는 `ApplyShake`·`ApplyFraming`·`IntroRoutine` 셋으로 못박았다).
      스텝은 저작 글루라 상위층 규율의 대상이 아니라고 보고 직접 만진다 — 헤더 주석에 그 판단을 남긴다.
- [x] ⚠ **블렌드 시간을 스텝이 바꾸지 않는다.** 그 값의 주인은 `Brain.DefaultBlend`이고
      `CameraDirector.angleBlendDuration`이 `Awake`에서 덮는다(§7-2). 느린 전환이 필요하면 **vcam 자신의 돌리**로 만든다.
- [ ] `Cam_Arrival`에 스플라인 배치 — 로우 앵글 전신 → 허물. ⚠ `PositionUnits = Normalized`(§7-2의 전제)
      **지금은 돌리 없이 고정 로우 앵글이다**(`CameraShotStep`은 돌리가 없으면 구도만 유지한다).
      허물의 자리가 런타임 랜덤이라 팬 경로를 미리 저작할 수 없다 — **프롭 배치 때 무대가 정해지면 그때 붙인다.**

## Step 7 — 검증

- [x] 대사를 넘기는 동안 미오가 계속 달린다 · 배경이 112m 흘렀다(실측)
- [x] 도착 컷 전에 허물이 서 있다(`PrepareStage`에서 32 인스턴스 프리웜 · `active` 4)
- [x] 첫 드릴 첫 노드에서 `timeScale` 0.25 → 창을 지나자 1로 복귀(실측)
- [x] 드릴 성공 = 허물이 갈라진다(오토퍼펙트로 드릴 둘 연속 통과 · `SlicePiece` 활성 확인 · `tutorial_done` 기록)
- [x] 플레이 종료 후 `Time.timeScale` = 1 · 콘솔 에러/경고 0

---

---

## 구현 중에 드러난 것 둘 (플랜에 없던 수정)

### `PatternHandler.ClearAllPatterns(bool notify = true)`
**드릴이 실패해 재시도할 때 무대의 허물이 통째로 사라졌다.** 재시도가 큐를 비우려고 `ClearAllPatterns()`를 부르는데,
`EnemyDirector.HandleAllCleared`가 그 이벤트를 *"곡이 끝났다"*로 읽어 `DissolveAll()`을 돌린다.
→ 인자 하나로 **"치우는 것"과 "끝난 것"**을 가른다. 재시도만 `notify: false`. 중단(`Exit`)은 그대로 `true`다.
⚠ 튜토리얼에 `EnemyDirector`가 없던 동안에는 듣는 쪽이 없어 드러나지 않았다. 오토퍼펙트 검증으로는 **절대 안 잡힌다**(실패가 없다).

### ⚠ 드릴의 `patternGap`은 링 노출(0.5초)보다 커야 한다
사슬 드릴만 `patternGap`이 0.4라 **다음 패턴의 첫 링이 앞 패턴의 마지막 노드를 치기도 전에 태어났다**(링은 입력 0.5초 전에 생긴다 — `PatternHandler.DefaultExposureDuration`). 사슬은 *앞 패턴의 끝점 = 다음 패턴의 시작점*으로 저작돼 있어(`4→5 · 5→7 · 7→5 · 5`) 그 겹침이 **매번 같은 Point 위에서** 일어났다 — 화면에는 노브 하나에 원이 둘로 보인다. → **0.55로 올렸다**(노출 + 0.05 여유). 다른 드릴은 패턴이 하나뿐이고 `patternGap`도 0.6이라 원래 안 났다.

⚠ 곡에서는 이 겹침이 정상이다(§4 — 채보당 7~28쌍). 드릴은 **패턴을 한 번에 다 큐에 밀어 넣고** 간격을 스스로 정하므로 여기서만 저작 규칙이 된다.

### 스폰 예산 — `EnemyDirector.SetSpawnBudget(int)`
곡은 끝날 때까지 **사망 1 : 스폰 1**로 보충하는 것이 정상이라 무한이다. 튜토리얼처럼 **처치 횟수가 저작으로 정해진** 구간에서는 그것이 *"적이 끝없이 걸어 들어오는"* 그림이 된다.
→ 무대가 평생 스폰할 총수를 예산으로 준다(음수 = 무제한, 기존 동작). **⚠ 새 분기를 만들지 않았다** — 예산이 떨어지면 `SpawnAt`/`SpawnIntoStage`가 `null`을 돌려주고, 그 경로는 **풀이 마른 경우로 이미 전부 처리돼 있다**.
**`TutorialDirector.spawnBudget` = 드릴 그룹 수와 같다**(현재 **4** = 단일 2 + 사슬 1 + 연타 1).
그래야 **마지막 베기가 마지막 허물이 되고 잔여가 0**이 된다. 드릴은 성공할 때까지 반복하므로 **실패해도 처치 횟수는 언제나 4**다.
실측: 첫 무리가 예산 4를 다 쓰고(`ring=4 · 예산 0`) 보충이 없다 → 처치 4 · 잔여 0.
⚠ 코드 기본값을 바꿔도 **씬에 직렬화된 값이 이긴다** — 이 값의 진실의 원천은 씬의 `TutorialDirector`다.

### 드릴 그룹 하나가 허물 하나를 벤다 — `PatternDrillStep`이 cue를 넘긴다
드릴이 `EnqueueCue`를 안 해서 **모든 패턴이 `EnemyDirector.FallbackCue()`의 `killOnSuccess = true`를 탔다** — 사슬(패턴 넷)이 **넷을 벴다**. 이제 드릴이 `SetPattern` 직전에 cue를 넣고 **마지막 패턴에만** `killOnSuccess`를 준다(§11-5의 사슬 규칙 그대로). 이 클래스가 이미 *"단위는 패턴이 아니라 그룹"*이라고 말해 온 것과 같은 규칙이라 새 개념이 없다.
실측(예약 큐): `PatternChain(3,4) False · (4,6) False · (6,4) False · (4) True`.

### `EnemyDirector.CancelPending()`
위 cue가 FIFO라 **재시도에서 걷어낸 패턴의 예약이 남으면 `killOnSuccess`가 한 칸씩 밀린다**. 실패 재시도는 아직 판정되지 않은 패턴을 `PatternHandler`에서 걷어내는데 그것들의 **완료 이벤트는 영영 안 온다**. `HandleAllCleared`에서 소멸(`DissolveAll`)만 뺀 것이 `CancelPending()`이고, 드릴의 재시도가 그것을 부른다.

### `PrepareStage`는 멱등이 아니다
`EnemyDirector.PrepareStage`는 무리 리스트만 비우고 **이전 적을 풀에 돌려주지 않으며 `ring`은 계속 늘어난다** — 두 번 부르면 **무대의 적이 그대로 두 배**가 되고 앞 무리는 아무 목록에도 없는 채 서 있는다. `TutorialDirector`가 `staged` 플래그로 **한 번만** 세운다.

### `TutorialCueStep.Action`의 서수
`RunTo`를 enum **중간**에 끼웠다가 저작해 둔 `RunOff`(서수 2)가 `RunTo`로 읽혔다 — 명시 정수가 없어 **서수가 곧 직렬화 키**다(§7-4가 `EffectTiming`에 대해 못박은 그 규칙). **새 값은 언제나 끝에 붙인다.**

---

## 하지 않는 것
- **기습(`DodgeDirector`)** — `EnemyDirector`와 **같은 오브젝트에 붙어 있어** 그것을 켠 순간 같이 깨어났다. 컴포넌트만 끈다. 회피는 1스테이지에서 처음 만난다.
- **밤거리 프롭·라이팅** — 로직이 선 뒤에 한다(사용자 지시).
- **`EnemyCue` 배선** — `FallbackCue()`의 `killOnSuccess` 기본값이 `true`라 필요 없다.
- **`SliceTargetDirector` 삭제** — 배선만 끊는다. 나중에 투사체가 필요해지면 그대로 쓴다.
- **스킵 버튼·진행도 표시** — `Story_Overview.md` §0.
- **회피(Space) 교육** — 이 씬엔 `DodgeDirector`가 없다. 1스테이지에서 처음 만난다.
- **Timeline** — 트랙이 카메라 하나뿐이라 `CameraShotStep`으로 덮는다(Step 6). `TimelineStep`은 남겨 둔다.
