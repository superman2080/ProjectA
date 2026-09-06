# Research — 튜토리얼 재설계 (호출 → 달려감 → 도착 → 허물)

지금 튜토리얼은 **도장에서 짚단을 베는 연습**이다. 바꾸려는 것은 **사건 0 그 자체** — 카시마가 급히 부르고,
미오가 밤거리를 달려가고, 도착한 자리에서 풀려난 허물을 벤다.

앞선 문서: `docs/TutorialScene/`(씬·드릴) · `docs/TutorialGuide/`(강조·키 입력) · `docs/TutorialGuide/Flow_Tutorial.md`(현재 저작의 진실의 원천).
서사 근거: `docs/Story/Story_Events.md` §0(이 문서가 서사에서 나머지를 이긴다).

---

## 1. 지금 있는 것 (실측)

| 것 | 상태 |
|---|---|
| `Tutorial.unity` | 도장. `ArenaFloor` · `Tatami` 넷 · `Pendant_*` · `Prop_Noren` · vcam 다섯(`Cam_RightLow` · `Cam_ShoulderBack` · `Cam_NearLowRight` · `Cam_FarShoulderRight` …) |
| `EnemyDirector` | **씬에 있고 `m_IsActive: 0`(꺼져 있다)** |
| `ChartPlayer` | **꺼져 있다.** `playOnStart: 0`, `AudioSource`에 클립 없음 |
| `SequenceRunner` | `Seq_Tutorial` · `playOnStart: 1` · 슬롯 9개(`PatternHandler` · `SliceTargetDirector` · `Tatami1~4` · `PatternInput` · `Point4` · `Point5`) |
| `Seq_Tutorial` | 35스텝. 드릴 넷이 각각 `Tatami{N}`을 앵커로 잡고 `SliceSet`으로 가른다 |

## 2. 바뀌는 것 넷과, 그것이 기존 코드에 요구하는 것

### 2-1. 표적이 짚단에서 허물로 — **`PatternDrillStep` 수정 0줄**

- `PatternDrillStep`은 `targetDirectorSlot` · `targetAnchorSlot`이 **비면 조용히 물러난다** —
  앵커가 없으면 걷기 단계를 건너뛰고(`Enter`의 `anchor == null` 분기) 그 자리에서 바로 친다.
  표적 예약도 `director != null && target != null`일 때만이라 **둘을 비우는 것만으로 짚단이 사라진다.**
- 적 쪽은 `EnemyDirector`가 **`PatternHandler`의 이벤트만 구독**한다(`OnPatternQueued` · `OnPatternComplete` ·
  `OnMashHit` · `OnJudgeTargetFirstMiss` · `OnAllPatternsCleared`). 드릴이 `SetPattern`을 부르면 그대로 흐른다.
- **cue 배선이 필요 없다.** `ChartPlayer`가 없으면 `FallbackCue()`가 `new EnemyCue()`를 주는데
  **`killOnSuccess`의 기본값이 `true`**다 → 드릴 성공 = 처치. 사슬 드릴(패턴 넷)은 **넷을 연달아 벤다**(의도에 맞는다).
- **⚠ 없는 것은 `PrepareStage()`의 호출자 하나뿐이다.** 지금 그것을 부르는 곳은 `ChartPlayer`의 카운트다운과
  에디터 컨텍스트 메뉴(`Debug: Prepare Stage`)뿐이다. 프리웜(`Instantiate`)이 여기서 일어나므로
  **전투가 시작되기 한참 전에** 불러야 한다(§5의 히치 = 판정 손실 규율).
- 인원은 `SetClusterSize(int)`가 씬 값을 이긴다 — `[Min(3)]` 하한을 우회하는 경로가 이미 있고,
  주석이 **"튜토리얼처럼 처치가 한두 번에 그치는 짧은 자리"**를 그 용도로 못박아 두었다.

### 2-2. 도입이 대사 창 + 제자리 달리기 — **호출자만 없다**

- **대사를 플레이어가 넘기는 것은 이미 된다.** `DialogUI`는 `nextDialog` **버튼 클릭**으로 다음 줄로 간다.
  `DialogStep`이 완료 콜백을 받아 그때 넘어가므로 **시퀀스 진행이 플레이어 페이스**다.
- **달리는 그림은 `CharacterActionPlayer.SetExploreLocomotion(speed, sprinting)`이 이미 만든다**(public).
  ⚠ **매 프레임 불러야 한다** — 그 메서드가 `convergeUntil` 래치를 갱신해 base 레이어의 주인을 쥐고,
  호출이 멎으면 0.1초 뒤 복귀 로직이 Idle로 덮는다(`PatternDrillStep.TickWalk`의 주석이 같은 것을 말한다).
- **⚠ 그런데 스텝은 끝나면 Tick을 안 받는다.** 대사 스텝이 도는 동안 계속 부르려면
  **호출자가 씬 컴포넌트여야 한다** — 스텝은 그것을 켜고 끄기만 한다(`HighlightStep`과 같은 관용구: `IsFinished`가 언제나 true).
- **위치는 한 밀리미터도 안 옮긴다.** 달리는 것처럼 보이게 하는 것은 카메라 구도(밤거리 · 외곽을 적게 노출)이고,
  위치를 안 옮기므로 §11-2의 "위치의 주인" 문제가 애초에 생기지 않는다.
- ⚠ 이 씬의 `CharacterActionPlayer`는 `sprintClip` · `exploreWalkClip` · `exploreRunClip`이 **전부 비어 있다**
  (`Research_TutorialPolish.md` 문제 1). 비면 `Sprint` 스테이트에 저작된 `Sprint_HS`가 그대로 나온다 —
  **달리기로는 이미 맞고**, 그래서 제자리 달리기에 새 클립 배선이 필요 없다.

### 2-3. 도착 컷은 Timeline — **⚠ 적을 트랙에 못 묶는다**

- `TimelineStep`이 이미 있다. `PlayableDirector` 슬롯 하나만 배선하고 `extrapolationMode`를 코드가 `None`으로 못박는다.
- **⚠ 허물은 런타임에 풀에서 나온다** — Timeline 트랙 바인딩은 씬 오브젝트를 요구하므로 **적을 직접 몰 수 없다.**
  그래서 타임라인이 하는 일은 **카메라뿐**이고, 그 앞 스텝에서 이미 `PrepareStage()`로 무대가 서 있어야 한다.
  즉 **순서가 곧 설계다: 프리웜(달리는 동안) → 도착 컷(카메라만) → 드릴.**
- 컷신 동안 조작·위치의 주인을 비우는 것은 `SequenceAsset.HoldMode`가 아니라 **스텝 단위로는 못 한다** —
  `holdMode`는 에셋 전체에 걸린다. 지금 `Seq_Tutorial`은 `holdMode: 0`(=`Keep`)이고,
  이 씬에는 `PlayerExploreMover`가 위치를 쓰지 않으므로(전투 씬 구성) 그대로 둔다.

### 2-4. 첫 노드 슬로우 — **여기서만 `Time.timeScale`이 허용된다**

- §7-3의 금지 근거는 *"판정·클립 정렬은 `Time.time`인데 채보는 `audioSource.time`으로 돌고 오디오는 timeScale 밖이라
  차이가 **영구 누적**된다"*이다. **튜토리얼 씬에는 그 오디오 시계가 없다** — `ChartPlayer`가 꺼져 있고
  드릴은 `Time.time`으로만 시각을 잡는다(`PatternDrillStep.Feed`). 두 시계가 하나뿐이라 어긋날 곳이 없다.
- 즉 §14가 이미 쓰고 있는 것과 **같은 경계**다: *"`Time.timeScale`은 두 시계가 어긋날 수 있는 동안 못 쓴다."*
  ⚠ 그래서 **이 슬로우는 튜토리얼 씬 전용**이고, 곡이 도는 씬으로 옮기면 그 순간 규칙 위반이 된다.
- 느려지면 `Time.time`이 느리게 흘러 **판정 창이 실시간으로 그만큼 넓어진다** — 이것이 "무조건 성공"의 구현 전부다.
  포커스 링 수축도 `Time.deltaTime`이라(`FocusRingView` 104행) 같은 비율로 느려져 **화면과 판정이 안 어긋난다.**
- **필요한 정보는 이미 이벤트에 실려 있다** — `OnJudgeTargetBegan(JudgeTargetInfo)`의 **`FirstNodeTime`**이
  그 패턴 첫 노드의 절대 입력 시각이다. 새 시계도 새 필드도 없다.
- **⚠ "드릴의 첫 노드"는 이벤트만 봐서는 알 수 없다.** 사슬 드릴은 패턴 넷이 각각 판정 대상이 되므로
  `OnJudgeTargetBegan`이 넷 다 난다. **드릴이 시작될 때 한 번 무장(arm)해 주는 신호가 필요하다.**
- 복구 경로가 셋이다(§14와 같은 규율): 노드 판정 · 창 초과 · `OnDisable`.

---

## 3. 함정 목록

- **⚠ `PrepareStage`를 드릴 직전에 부르면 히치가 그대로 판정 손실이다.** 달리는 구간(대사 중)에 부른다.
- **⚠ `SetClusterSize`는 반드시 `PrepareStage` 앞이다**(주석에 명시 — 뒤면 인원과 배치가 어긋난다).
- **⚠ 슬로우 중에는 애니메이터도 함께 느려진다.** 베기 클립이 슬로우모션으로 보이는데, 이것은 손해가 아니라 연출이다.
  다만 **히트스톱(`AttackSpeed = 0`)과 겹치면 정지가 실시간으로 길어진다** — §14가 `SuppressNextMainImpact`로 겪은 것과 같은 부류다.
  첫 노드 슬로우는 **임팩트(마지막 노드)보다 앞**이라 메인 히트스톱과 겹치지 않지만, 노드가 하나뿐인 패턴(`PatternChain(4)`)에서는 겹친다.
- **⚠ `Tatami` 넷과 `SliceTargetDirector` 슬롯을 지우면 `Seq_Tutorial`의 `requiredBindings`도 같이 정리해야 한다** —
  안 그러면 러너 인스펙터에 죽은 칸이 남는다.
- **⚠ 카시마는 사건 0에서 다정하다**(`Story_Events.md`). **급한 것은 상황이지 그의 말투가 아니다** —
  전화로 재촉하더라도 문장은 길고 설명해 주며, 격앙은 0단이다. 여기서 짧고 차갑게 쓰면 뒤의 낙차가 통째로 죽는다.
- **⚠ 조작 안내와 세계 설명을 섞지 않는다**(`Flow_Tutorial.md` §0). 캡션은 조작·수치, 대사는 태도·검술.
