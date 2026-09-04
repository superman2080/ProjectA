# Plan — 튜토리얼 완성도 (이동 애니메이션 · 가독성 · 설명 · 연타)

근거: `Research_TutorialPolish.md`. 앞선 작업: `Plan_TutorialScene.md` · `Plan_TutorialGuide.md`.

**새 클래스가 0개다.** 코드 수정은 세 파일에 각각 한 덩어리씩이고, 나머지는 씬 배선과 저작이다.

| # | 문제 | 해결 |
|---|---|---|
| 1 | 이동 애니메이션 없음 | 기존 `CharacterActionPlayer.SetExploreLocomotion` 호출 + 클립 배선 |
| 2 | 캡션 가독성 | 배경 판(uGUI 기본 컴포넌트, 코드 0줄) |
| 3 | 설명 부족 · 마우스 미언급 | `WaitInputStep` 구독 대상 교체 + 대사/캡션 확충 |
| 4 | 연타 설명 부족 · 순서 | `PatternDrillStep.walkOnly` 필드 하나 + 저작 순서 |

---

## Step 1: 걸어갈 때 로코모션 (문제 1)

### 1-A. 코드 — `PatternDrillStep`
- [x] 1-1. `Enter`에서 `context.Player.GetComponentInChildren<CharacterActionPlayer>()`를 잡아 둔다(없으면 `null`인 채로 진행 — **배선이 비면 조용히 비활성**되는 기존 규율).
      ⚠ 슬롯을 새로 만들지 않는다. 플레이어는 이미 `context.Player`로 들어오고 그 오브젝트 자신이 `CharacterActionPlayer`다.
- [x] 1-2. `TickWalk`의 이동 분기에서 `actionPlayer?.SetExploreLocomotion(walkSpeed)`.
      ⚠ **매 프레임 불러야 한다** — 그 메서드는 `convergeUntil` 래치를 매 프레임 갱신해 base 레이어의 주인을 쥔다. 한 번만 부르면 0.1초 뒤 복귀 로직이 Idle로 덮는다.
- [x] 1-3. **도착 처리에 코드를 쓰지 않는다.** 호출이 멎으면 래치가 만료되며 `Update`의 복귀 로직이 저절로 Idle로 되돌린다. `SetExploreLocomotion(0)`을 부르는 것도 같은 결과지만, **안 부르는 쪽이 더 짧고 이미 그렇게 설계돼 있다.**
- [x] 1-4. ⚠ **드릴이 시작된 뒤에는 부르지 않는다.** `Phase.Drill`에서도 계속 부르면 래치가 base를 계속 쥐고 있어 **베기 뒤 복귀(`Release`·Idle) 경로와 싸운다**. 호출은 `TickWalk`의 이동 분기 안에만 둔다.

### 1-B. 씬 배선 — 클립
- [x] 1-5. `Tutorial.unity`의 `CharacterActionPlayer`에서 **`sprintClip`을 `Sprint_HS`로 채운다.**
      ⚠ **이것이 오버라이드의 '키'다** — `ApplySprintClip`은 `sprintClip`이 `null`이면 조용히 물러나므로, 비어 있으면 걷기 클립을 꽂아도 반영되지 않는다.
- [x] 1-6. `exploreWalkClip = Walk`(`05. Animations/Clip/Move/Walk.anim`), `exploreRunClip = Run`.
- [x] 1-7. **달리기로 간다**(미결정 A 확정). `PatternDrillStep`에 `[SerializeField] bool runToTarget = true` 하나를 두고 `SetExploreLocomotion(walkSpeed, runToTarget)`으로 넘긴다.
      필드를 두는 이유: 이동 수단은 **드릴마다 다를 수 있는 저작 선택**이고(짧은 이동은 걷는 편이 낫다), 값 하나가 `exploreRunClip`/`exploreWalkClip` 중 어느 쪽을 쓸지를 통째로 가른다.
- [x] 1-8. ⚠ **배속에 하한이 있다 — 이것이 실제 미끄러짐의 원인이었다.** `sprintSpeedRange = (1.2, 2.0)`이라 `walkSpeed / exploreRunReferenceSpeed`가 1.2 아래로 못 내려간다.
      `walkSpeed = 4.0`이면 파생 배속이 0.89여야 하는데 **1.2로 클램프되어 발은 5.4 m/s로 도는데 몸은 4.0으로 간다**(35% 미끄러짐).
      **`walkSpeed = 5.4`(= 1.2 × 4.5)로 맞췄다** — 배속을 건드리지 않고 이동 쪽을 맞추는 쪽이, 다른 씬과 공유하는 튜닝 값(`sprintSpeedRange`)을 이 씬만 다르게 만드는 것보다 낫다.
      ⚠ 필드 이름이 `walkSpeed`인 채로 달리기에 쓰인다. **이름을 안 바꾼다** — 저작해 둔 드릴 넷의 직렬화 값이 그 이름에 묶여 있고, 툴팁 한 줄이면 오해가 없다.
- [x] 1-9. 클립을 못 꽂으면 `Sprint_HS`가 그대로 나온다 — **회귀가 아니라 지금보다 나은 상태**다(지금은 미끄러진다).

## Step 1-C: 다다미 간격 넓히기 (미결정 A의 뒷면)
달리기는 **거리가 있어야 달리기로 보인다.** 지금 배치는 한 다리가 3.6~4.6m라 달리는 구간이 1초 남짓이다.

- [x] 1-10. 좌표를 아래로 옮긴다(`Map/Tatami/*`). ⚠ **전부 무대 원(반경 8m) 안이고 y = 0 평면이다**(§13 — 이동이 `transform.position` 대입이라 경사를 못 탄다).

| | 지금 | 바꿀 값 | 직전 지점에서의 거리 |
|---|---|---|---|
| 시작 | (0, 0) | 그대로 | — |
| Tatami1 | (0, 4.0) | **(0, 5.5)** | 5.5 |
| Tatami2 | (3.0, 6.0) | **(6.0, 3.0)** | 6.5 |
| Tatami3 | (-1.0, 7.5) | **(1.0, -5.0)** | 9.4 |
| Tatami4 | (-4.0, 4.5) | **(-6.0, 1.5)** | 9.6 |

(표는 `(x, z)`. 원점에서의 거리는 각각 5.5 · 6.7 · 5.1 · 6.2 — 전부 8 미만이고 서로 6m 이상 떨어져 있다.)
- [x] 1-11. 다다미가 서로 가리지 않게 **플레이어가 오는 방향을 보도록** 회전을 맞춘다(표적이 옆모습이면 절단면이 안 보인다).
- [x] 1-12. ⚠ **무대 원 안에 프롭이 끼지 않았는지 본다.** 새 자리는 예전 배치가 비워 두던 곳이라, 도장 기둥·소품과 겹치면 달려가다 뚫고 지나간다(이동이 물리를 안 본다).
- [x] 1-13. 4.0 m/s × 9.6m ≈ **2.4초**가 가장 긴 다리다. 그 시간이 곧 다음 설명을 읽는 시간이 된다(5-5의 순서 변경과 짝이다).

## Step 2: 캡션 배경 (문제 2)
- [x] 2-1. `TutorialGuide/Content` 아래 `CaptionPanel`(Image, 반투명 검정 `alpha ≈ 0.72`, `raycastTarget = false`)을 넣고 **`Caption`을 그 자식으로** 옮긴다.
- [x] 2-2. 폭을 글자에 맞춘다 — 패널에 `HorizontalLayoutGroup`(padding 48/48/16/16, `childControlWidth/Height = true`, `childForceExpand = false`) + `ContentSizeFitter`(Horizontal·Vertical = `PreferredSize`). **코드 0줄.**
- [x] 2-3. `Caption`의 `enableWordWrapping`을 켜고 `maxWidth`는 패널의 `LayoutElement.preferredWidth`(2400)로 잡는다 — 긴 문장이 화면 밖으로 나가지 않게.
- [x] 2-4. 앵커는 지금 그대로 **상단 중앙**(`anchoredPosition = (0, -160)`, pivot `(0.5, 1)`).
- [x] 2-5. ⚠ `TutorialHighlightView.caption` 참조는 **TMP 그대로** 둔다. 켜고 끄는 대상은 `Content`이고 패널은 그 아래라 자동으로 따라간다 — **뷰 코드에 손대지 않는다.**
- [x] 2-6. ⚠ 캡션이 비면(`Show`의 caption 빈 문자열) 지금은 `caption.gameObject`만 끈다 → **패널만 덩그러니 남는다.** `TutorialHighlightView`에서 끄는 대상을 **캡션의 부모가 있으면 부모**로 바꾼다(1줄). 새 필드를 만들지 않는다.

## Step 3: 키보드·마우스 둘 다 받기 (문제 3의 결함)
- [x] 3-1. **`WaitInputStep`의 `Node` 구독 대상을 `PatternHandler.OnNodeConnected`로 바꾼다.**
      근거: 그 이벤트가 나는 `AppendPointToLine`은 **키보드와 마우스가 모두 지나가는 유일한 지점**이다(키보드도 `ForceDown` → `OnPointPressed`를 탄다). `InputHandler.OnKeyPressed`는 키보드 전용이라 *"마우스로도 됩니다"*를 써 두면 **마우스로는 영영 안 넘어간다.**
- [x] 3-2. 필드 교체 — `Node`는 `[SequenceSlot] patternHandlerSlot`을 쓰고, `Dodge`/`Move`/`Interact`는 `inputHandlerSlot`을 그대로 쓴다. **스텝 하나가 슬롯을 둘 가리키는 것은 기존 관용구다**(`PatternDrillStep`은 셋).
- [x] 3-3. `SequenceAsset.WarnUnknownSlots`의 `case WaitInputStep`에 새 게터를 한 줄 더한다.
- [x] 3-4. 모드 경고(3-3 of `Plan_TutorialGuide`)는 `Node`에서 **더 이상 안 찍는다** — `PatternHandler`는 모드를 모른다. `Dodge`/`Move`/`Interact`에서만 유지한다.
- [x] 3-5. ⚠ 회귀 확인: 연타 판정 대상이 살아 있으면 `OnPointPressed`가 앞에서 반환해 이 이벤트가 안 난다. **`WaitInput` 구간에는 살아 있는 패턴이 없으므로 무관하다** — 다만 앞으로 "연타 중에 기다리는" 스텝을 만들면 이 가정이 깨진다.

## Step 4: 연타를 도착한 뒤에 설명한다 (문제 4)
- [x] 4-1. `PatternDrillStep`에 **`bool walkOnly` 하나**를 더한다. 켜면 걷기만 하고 `Phase.Done`으로 끝난다(패턴 투입·표적 예약·`OnPatternComplete` 구독 전부 건너뛴다).
- [x] 4-2. `Enter`의 "패턴이 없으면 에러" 가드를 `walkOnly`일 때는 통과시킨다. `patterns`는 비워 둘 수 있어야 한다.
- [x] 4-3. ⚠ **`targetAnchorSlot`은 그대로 필요하다** — 목적지가 거기서 파생된다. `walkOnly` 스텝과 뒤따르는 드릴은 **같은 앵커**를 가리켜야 한다(다르면 두 번 걷는다).
- [x] 4-4. **뒤따르는 드릴의 걷기는 저절로 no-op이 된다** — `TickWalk`가 목적지까지 `arriveRadius`(0.15) 이내면 그 프레임에 `BeginDrill`로 넘어간다. 새 분기가 0개다.
- [x] 4-5. 이 순서를 **연타 드릴에만** 적용한다. 나머지 셋은 지금 순서를 유지한다 — 걷는 동안 대사를 읽는 것이 첫 세 번에서는 오히려 낫고, 바꾸면 검증이 끝난 구간을 흔든다. (원하면 같은 방식으로 하나씩 옮길 수 있다.)

## Step 5: 저작 — 설명 확충 (문제 3·4)
캡션은 **조작만** 말한다. 박·균열·그것들은 카시마 대사 몫이다(`Story_Overview.md` §0 · `Plan_TutorialScene` 4-4b).

- [x] 5-1. **첫 드릴 앞 — 조작 둘을 명시한다.**
  - `Highlight`(`PatternInput`) — *"화면 가운데 아홉 개의 점"*
  - `Highlight`(`PatternInput`) — *"숫자키 1~9로 누르거나, 마우스로 누른 채 이어 그으면 된다"*
  - `Highlight`(`Point3`) — *"이 점은 숫자키 3. 눌러 보자"*
  - `WaitInput`(`Node` 2) — **이제 마우스로도 통과한다**(Step 3)
- [x] 5-2. **첫 드릴 앞에 가이드라인·판정색 설명을 얹는다.** (가이드라인은 배치 완료, 판정 색 문구는 Step 7 뒤로 미룸)
  - *"반투명한 길이 다음에 그을 모양이다"*(가이드라인) — 드릴 시작 직전에 띄우고 드릴 동안 유지
  - 판정 색 — **문구는 Step 7에서 색을 고친 뒤에 확정한다.**
- [x] 5-3. **두 번째 드릴(`Pattern(6, 7, 3, 4)`) 앞** — 타이밍 캡션(기존)에 더해 *"실패하면 짚단이 남는다. 다시 뜰 때까지 기다리면 된다"*.
      ⚠ **실패했을 때 띄우는 것이 아니라 미리 한 번 말한다** — 반복되면 환경음이 된다는 기존 결정(`Plan_TutorialScene` 4-4b)을 지킨다.
- [x] 5-4. **세 번째(사슬) 드릴 앞** — *"짧은 획 넷이 이어진다. 하나라도 놓치면 처음부터"* + 통과 노드 설명 *"1에서 3으로 그으면 가운데 2도 함께 지나간다"*.
- [x] 5-5. **연타 — 순서를 바꾼다.**
  1. `Dialog`(기존, *"박을 잡을 수 없는 것도 있어…"*)
  2. **`PatternDrillStep(walkOnly, Tatami4)`** ← 먼저 걸어간다
  3. `Highlight`(`PatternInput`) — *"이건 순서가 없다. 아무 점이나 두드리면 된다"*
  4. `WaitInput`(`anyNode`, x5)
  5. `Highlight`(`PatternInput`) — *"창이 열리면 4초 안에 20번. 링 안의 숫자가 남은 횟수다"*
  6. `PatternDrillStep`(연타, `Tatami4`) — 걷기는 no-op
  7. `Highlight`(끄기)
  - ⚠ **"20번을 다 채워도 창이 끝날 때까지 이어진다"는 쓰지 않는다.** 사실이지만(§2-1) 플레이어가 알 필요가 없고, 알면 **일찍 채우고 손을 떼는** 잘못된 습관이 든다.
- [x] 5-6. **연타는 대사도 늘린다**(미결정 C 확정). 카시마의 몫은 **태도와 판단**이고, 수치(20타·4초·남은 횟수)는 캡션이 든다 — 둘이 같은 말을 하면 한쪽이 환경음이 된다.
  - 기존 (1) *"박을 잡을 수 없는 것도 있어. 그럴 땐 무너질 때까지 두드리면 돼."*
  - 추가 (2) *"순서를 찾지 마. 이건 세는 게 아니라 미는 거야."*
  - 추가 (3) *"손에 힘을 빼. 세게 치는 것보다 끊기지 않는 게 나아."*
  - ⚠ **왜 연타가 존재하는지는 말하지 않는다** — 박을 못 잡는 상대가 무엇인지는 §0이 막는다. 카시마는 *"그런 것도 있다"*까지만 안다는 투로 말한다.
- [x] 5-7. 새 슬롯은 필요하면 `requiredBindings`에 더한다. 안 쓰는 슬롯은 만들지 않는다.

## Step 7: 판정 색 정리 (미결정 B의 실체)
판정 색은 코드 상수가 아니라 **`Point` 컴포넌트의 인스펙터 값 셋**(`perfectColor`/`goodColor`/`missColor`)이고, `SetJudgementColor`가 그 값을 자식 `Visual`에 통째로 대입한다. 캡션에 색 이름을 쓰려면 **씬이 실제로 그 색인지**를 먼저 맞춰야 한다. 실측 결과 둘이 걸렸다.

- [x] 7-1. ⚠ **`Point_1`의 `goodColor`만 다르다** — `#00FF80`(민트)인데 나머지 여덟은 `#00FF00`(초록)이다. 아홉 개를 같은 값으로 통일한다.
      첫 드릴이 `Pattern(3, 4, 5)`라 Point_1이 잘 안 쓰여 오래 숨어 있었다. **판정 색이 점마다 다르면 "색 = 판정"이라는 규칙 자체가 거짓이 된다.**
- [x] 7-2. **`perfectColor`가 `#0000FF`(순색 파랑)다** — 어두운 배경에서 가장 안 읽히는 색이고, 최고 판정이 가장 안 보이는 상태다. 밝은 하늘색(예: `#4DA6FF`)으로 올린다.
      ⚠ 콤보 포스트FX가 화면을 붉게 물들이므로(§12) **Miss의 빨강과 구별되는 채널**이어야 한다 — 파랑 계열을 유지하는 이유다.
- [x] 7-3. 9개를 손으로 고치지 않는다. 값 셋을 한 번에 대입하는 에디터 일회성 스크립트로 돌린다(툴을 만들지 않는다).
- [x] 7-4. 색을 확정한 **뒤에** 5-2의 캡션 문구를 쓴다. 예: *"누른 점의 색이 판정이다 — 하늘색이 가장 정확하고, 빨강은 놓친 것"*.
      ⚠ Good을 굳이 이름 붙이지 않는다. 셋을 다 외우게 하면 안내가 표가 된다.

## Step 6: 검증
- [x] 6-1. 다다미로 이동하는 동안 **달리기 클립이 재생되고**, 도착하면 Idle로 돌아온다.
- [x] 6-2. 발이 지면을 긁지 않는다(1-8의 배속).
- [x] 6-2b. 새 좌표에서 **네 다다미가 전부 무대 원 안**이고 달려가는 경로에 프롭이 안 낀다(1-12).
- [x] 6-2c. 판정 색이 **아홉 점에서 같다**(7-1).
- [x] 6-3. 드릴 중(베기·연타)에는 로코모션이 base를 안 쥔다 — 베기 뒤 복귀가 예전 그대로다.
- [x] 6-4. 캡션에 배경 판이 있고 **폭이 글자에 맞는다**. 캡션이 없는 강조(링만)에서는 **패널이 안 보인다**(2-6).
- [x] 6-5. `WaitInput`이 **키보드로도 마우스로도** 통과한다.
- [x] 6-6. `walkOnly` 스텝 뒤의 연타 드릴이 **다시 걷지 않는다**(그 프레임에 바로 시작).
- [x] 6-7. 연타 실패 시 재시도에서 걷기가 다시 일어나지 않는다(기존 동작 유지).
- [x] 6-8. 콘솔 에러·경고 0건. 시퀀스를 끊어도 강조·로코모션이 남지 않는다.

---

## 하지 않는 것
- **걷기 전용 애니메이터 스테이트.** `Sprint` 스테이트 + 클립 교체가 이미 그 일을 한다(`SetExploreLocomotion`의 설계).
- **드릴 넷 전부의 순서 변경.** 연타 하나만 바꾼다(4-5).
- **실패 시 안내 캡션.** 미리 한 번 말하는 것으로 대신한다(5-3).
- **캡션 타자기 효과.** 대사창과 구분되는 정체성이 흐려지고, 조작 안내는 즉시 읽혀야 한다.
- **키캡 스프라이트.** 텍스트로 충분하다(`Plan_TutorialGuide`의 결정 유지).

## 미결정
| # | 정할 것 | 권장 |
|---|---|---|
| A | 이동을 걷기로 할까 달리기로 할까 | **결정: 달리기.** `runToTarget = true` · `walkSpeed = 4.0` · `exploreRunClip = Run`. 다다미 간격도 함께 넓힌다(Step 1-C) — 거리가 없으면 달리기로 안 보인다 |
| B | 판정 색 문구 | **결정: 문구보다 색이 먼저다.** 실측에서 `Point_1`만 Good이 민트고 Perfect가 순색 파랑이었다 → Step 7에서 색을 고치고 그 뒤에 문구를 쓴다 |
| C | 연타 설명을 대사로도 늘릴까 | **결정: 늘린다**(5-6). 대사는 태도, 캡션은 수치로 나눈다 |

---

## 배치 기록 (2026-09-03) — Step 3 · 4 · 5

`Flow_Tutorial.md`를 만들고 그대로 옮겼다. **Step 1(로코모션) · 1-C(다다미 좌표) · 2(캡션 배경) · 7(판정 색)은 아직 손대지 않았다.**

### 저작 중에 드러난 오류 둘
1. **⚠ 패턴 에셋 이름은 인덱스 기준이다** — `Pattern(3, 4, 5)`의 실제 점은 **4·5·6**이다. 직전 배치가 이름을 점 번호로 읽어 `Point_3` / `nodeIndex = 2`로 배선했고, **강조한 점과 눌러야 할 점이 달랐다.** `Point_4` / `nodeIndex = 3`으로 고쳤다.
   (덤: `PatternChain(4, 6)`은 실제로 `5 → 7`이라 `Plan_TutorialScene` 4-5가 경고한 중복이 **이미 해소돼 있었다.**)
2. **⚠ 캡션은 스스로 기다리지 않는다** — `HighlightStep.IsFinished`가 언제나 true라 캡션을 나란히 두면 한 프레임에 전부 지나간다. **직전 배치의 첫 두 캡션은 한 번도 화면에 나온 적이 없었다.** 기존 `WaitStep`을 사이에 넣어 노출 시간을 준다(새 코드 0줄).

### 코드
- `WaitInputStep` — `Node`의 구독 대상을 `InputHandler.OnKeyPressed` → **`PatternHandler.OnNodeConnected`**로 교체. 슬롯이 둘이 됐다(`patternHandlerSlot` / `inputHandlerSlot`). `SequenceAsset.WarnUnknownSlots`에 한 줄 추가.
- `PatternDrillStep` — `walkOnly` 필드 하나. 켜면 걷기만 하고 `Phase.Done`(패턴 투입·표적 예약·구독을 전부 건너뛴다). `Label`이 `WalkTo 'Tatami4'`로 뜬다.

### 에셋 · 씬
- `Seq_Tutorial` **19 → 35스텝**. 슬롯: `Point3` 제거, **`PatternInput` · `Point4` · `Point5`** 배선. `InputHandler` 슬롯은 이제 아무도 안 써서 뺐다.
- 연타 대사 두 줄 추가(카시마 x1 → x3).

### 플레이 검증 (실측)
- 캡션 셋이 **차례로** 뜬다(`Wait 2.5s` / `3.5s` 사이). 이전엔 마지막 하나만 보였다.
- `Point_4` 강조 · 노브 알파 1.00 · 링 중심 거리 0.00. **틀린 점(Point_1)으로는 안 넘어가고**(`count = 0`) `Point_4`에 통과.
- 드릴 1 동안 가이드라인 캡션이 **떠 있는 채로 유지**된다.
- **`WalkTo 'Tatami4'`가 먼저 걷는다** — 도착 후 `Tatami4`까지 2.64m(목표 2.5 + `arriveRadius` 0.15). 그 **뒤에** 연타 캡션이 뜬다.
- **뒤따르는 연타 드릴이 다시 걷지 않았다** — 드릴 전후로 플레이어 좌표가 `(-1.40, 0.00, 4.98)`로 동일.
- 마지막 `Highlight (off)` 뒤 강조가 꺼지고, Tatami4 몸통이 비활성(= 표적으로 교체됨).
- 콘솔 에러·경고 0건. 오토플레이 토글은 런타임에만 켰다 껐으므로 씬에 저장되지 않았다(확인 완료).

### 알게 된 것 (문서에 반영 안 함, 무해)
- `anyNode` 카운트에는 **통과 노드 자동 인식으로 들어온 입력도 포함된다**(5회 요구에 7이 찍혔다). 연타 예행 확인이 목적이라 문제가 아니다.


---

## 구현 기록 (2026-09-03) — Step 1 · 1-C · 2 · 7

### 코드 (2파일)
- `PatternDrillStep` — `actionPlayer`(= `context.Player.GetComponentInChildren<CharacterActionPlayer>()`) + `runToTarget` 필드. `TickWalk`의 **이동 분기 안에서만** `SetExploreLocomotion(walkSpeed, runToTarget)`.
- `TutorialHighlightView` — 캡션을 끌 때 **부모가 있으면 부모**(배경 판)를 끈다. 안 그러면 캡션 없는 강조에서 **빈 판만 덩그러니 남는다.**

### 씬
- 클립 배선 — `sprintClip = Sprint_HS`(오버라이드의 **키**) · `exploreWalkClip = Walk` · `exploreRunClip = Run`. 확인: 런타임 오버라이드가 `Sprint_HS -> Run`으로 잡혔다.
- 다다미 재배치 — `(0, 5.5)` · `(6, 3)` · `(1, -5)` · `(-6, 1.5)`. 원점거리 5.10~6.71(무대 원 8m 안), 다리 길이 **5.5 / 6.5 / 9.4 / 9.6m**. 각 다다미는 플레이어가 오는 쪽을 보게 회전.
- **경로 검사 통과** — 새 경로 위 0.9m 높이를 0.4m 반경으로 훑어 걸린 것은 `CameraBounds`(카메라 컨파이너 트리거)뿐이다. `Girder+4.3`은 `min.y = 8.84`로 천장보다.
- 캡션 배경 판 — `CaptionPanel`(검정 alpha 0.72) + `HorizontalLayoutGroup`(padding 48/16) + `ContentSizeFitter`. **폭이 글자에 맞는다**(34자 캡션에서 1524x104, 캔버스 폭 3840). 캡션은 줄바꿈을 끄고 한 줄로 유지한다.
- 판정 색 — 아홉 점 전부 `#4DA6FF` / `#00FF00` / `#FF0000`. **`Point_1`만 Good이 `#00FF80`이던 것을 통일했고**, Perfect를 순색 파랑 `#0000FF` → 밝은 하늘색으로 올렸다.
- 드릴 5개 `walkSpeed = 5.4` · `runToTarget = true`.

### 검증 (실측)
- 캡션 판이 **글자 폭에 맞고**, 캡션 없는 강조에서는 **판이 꺼진다**(링만 남음) · 캡션만 있는 강조에서는 링이 꺼진다 · `Hide()`로 둘 다 내려간다.
- 오버라이드 컨트롤러가 `Sprint_HS -> Run`으로 갈렸다(= 클립 배선이 실제로 먹었다).
- 파생 배속 1.20 × `exploreRunReferenceSpeed` 4.5 = **5.40 m/s = `walkSpeed`** → 미끄러짐 0.
- 판정 색 아홉 점 일치.
- 콘솔 에러·경고 0건.

### ⚠ 검증하지 못한 것
- **달리는 프레임을 눈으로 못 잡았다.** 원격 실행이라 호출 간격이 게임 시간 수 초라 1.8초짜리 이동 구간을 매번 지나쳐 버렸다. 재생 중 `SprintSpeed = 1.20`이 실제로 설정돼 있는 것과 오버라이드가 갈린 것은 확인했으므로 **동작하는 것은 확실하나, 클립이 보기에 자연스러운지는 사람이 한 번 봐야 한다.**
- 새 다다미 자리의 **카메라 구도**(무대 가장자리 쪽 두 자리)도 눈으로 확인이 필요하다.
