# Research — 튜토리얼 완성도 (이동 애니메이션 · 가독성 · 설명 · 연타)

앞선 작업: `Plan_TutorialScene.md`(씬·드릴) · `Plan_TutorialGuide.md`(강조·키 입력). 이 문서는 그 위에서 보고된 문제 넷의 원인을 짚는다.

---

## 문제 1 — 다다미로 걸어갈 때 애니메이션이 없다

**원인: 부르는 사람이 없다.** `PatternDrillStep.TickWalk`는 `player.position`만 밀고 애니메이터를 **한 번도 건드리지 않는다.**

- 해결 수단은 **이미 있다** — `CharacterActionPlayer.SetExploreLocomotion(float travelSpeed, bool sprinting = false)`(`public`). `PlayerExploreMover`가 매 프레임 부르는 바로 그것이고, 주석이 *"애니메이터는 `CharacterActionPlayer`만 안다 — 스테이트 이름을 여기서 복제하지 않는다"*라고 못박고 있다.
- **래치가 이미 맞는다** — 그 메서드는 `convergeUntil`을 매 프레임 갱신해 base 레이어의 주인을 가져간다. 호출이 멎으면 복귀 로직이 저절로 되찾는다. **그래서 "도착하면 멈춰라"를 따로 구현할 필요가 없다.**
- ⚠ **다만 이 씬은 클립이 안 꽂혀 있다.** `Tutorial.unity`의 `CharacterActionPlayer`에서 **`sprintClip` · `exploreWalkClip` · `exploreRunClip`이 전부 `NULL`**이다.
  - `ApplySprintClip`은 **`sprintClip`을 키로** 오버라이드하므로 키가 없으면 조용히 물러난다 → `Sprint` 스테이트가 컨트롤러에 저작된 `Sprint_HS`(달리기)를 그대로 재생한다.
  - 즉 코드만 고치면 **"달려간다"**가 되고, 걷게 하려면 클립 배선이 필요하다. `Assets/05. Animations/Clip/Move/Walk.anim`이 최근 추가돼 있다.
  - 참고 값: `exploreWalkReferenceSpeed = 1.6` · `exploreRunReferenceSpeed = 4.5` · `exploreMoveThreshold = 0.1` · 드릴의 `walkSpeed = 3`.
- 애니메이터(`PlayerAnimator`) `Running Layer` 스테이트 셋 — `Katana_Idle` · `Sprint`(`Sprint_HS`, `SprintSpeed`) · `Quickshift`. **새 스테이트를 만들 이유가 없다.**
- 접근 경로도 있다 — 러너의 `player`가 `Char_School_Katana_FullBody-Magica cloth2`이고 `CharacterActionPlayer`가 **그 오브젝트 자신**이다(`GetComponentInChildren`으로 잡힌다).

## 문제 2 — 안내 텍스트 가독성

`TutorialGuide/Content/Caption`은 TMP 하나뿐이고 **뒤에 아무것도 없다.** 배경 판 하나면 끝나고, 폭을 글자에 맞추는 것도 uGUI 기본 컴포넌트(`HorizontalLayoutGroup` + `ContentSizeFitter`)로 된다 — **새 코드 0줄.**

## 문제 3 — 설명이 불친절하다 / 키보드·마우스 둘 다 쓸 수 있음을 알리고 싶다

### ⚠ 여기 실제 결함이 하나 있다
`WaitInputStep`의 `Node`는 **`InputHandler.OnKeyPressed`를 구독한다 — 그건 키보드 전용이다.** 마우스 드래그는 `Point.OnPointerDown/Enter` → `PatternHandler.OnPointPressed` 경로라 그 이벤트가 **안 난다.**
→ 지금 상태로 *"마우스로도 됩니다"*라고 써 두면 **마우스로는 스텝이 안 넘어간다.**

**답이 이미 있다** — `PatternHandler.OnNodeConnected(int index, Vector3 world)`는 `AppendPointToLine`에서 나고, 그 함수는 **키보드·마우스 두 경로가 모두 통과하는 지점**이다(키보드도 `ForceDown` → `OnPointPressed`를 탄다). 여기로 갈아타면 두 조작이 **한 구독으로** 덮인다.
- ⚠ 연타(`IsMash`) 판정 대상이 있을 때는 `OnPointPressed`가 앞에서 반환해 이 이벤트가 안 난다. **`WaitInput` 구간에는 살아 있는 패턴이 없으므로 무관하다.**

### 설명거리는 이미 시스템에 다 있다 (전부 구현 완료)
| 가르칠 것 | 근거 |
|---|---|
| 숫자키 1~9 / 마우스 드래그 둘 다 | `InputHandler` · `Point.OnPointerEnter` |
| 포커스 링이 노브 크기가 되는 순간이 박 | §4 |
| 반투명 캡슐 가이드라인 = 앞으로 그을 모양 | §1 `PatternLineRenderer.capsuleMode` |
| 판정 색(Perfect/Good/Miss)이 노브에 뜬다 | `Point.SetJudgementColor` |
| 1→3처럼 그으면 **가운데 2가 자동으로 입력된다** | §1 통과 노드 자동 인식 |
| 실패하면 짚단이 안 잘리고 다시 뜬다 | `PatternDrillStep`(실패 경로에 `Resolve` 없음) |

## 문제 4 — 연타 설명 부족 / 도착한 뒤에 설명하고 싶다

- **지금 순서**: `Dialog` → `Highlight` → `WaitInput` → `Drill`(그 안에서 **걷기 → 연타**). 즉 설명이 전부 끝난 뒤에 걸어간다.
- **걷기의 주인은 드릴이다** — `PatternDrillStep`의 `Phase.Walk`가 `targetAnchorSlot`에서 목적지를 파생시킨다. 좌표 저작이 없으므로 `MoveToStep`으로 대체할 수 없다(그쪽은 `Vector3`를 저작한다. 게다가 `holdMode = Keep`이면 `OnValidate` 경고가 뜬다).
- **⚠ 걷기를 두 번 해도 공짜다** — `TickWalk`는 목적지까지 거리가 `arriveRadius`(0.15) 이내면 그 프레임에 곧바로 `BeginDrill`로 넘어간다. **먼저 걸어가 두면 드릴의 걷기는 저절로 no-op이 된다.**
- 연타 수치(실측): `PatternMash(4)` **20타**, 드릴 `mashWindow` **4초**, `MashInputDeadlineLead = 0`(마무리 일격 없음). 완료 시각은 언제나 `Deadline`(창 끝 + `goodWindow` 0.25) — **20타를 일찍 채워도 그 자리에서 안 끝난다**(§2-1).
- 링 하나가 게이지이고 `FocusRingView.SetLabel`이 **남은 타수를 숫자로** 쓴다(일반 패턴의 `IndexLabel`은 비활성) — 설명할 값이 화면에 이미 있다.

---

## 정리 — 신규 개념이 몇 개인가

| 문제 | 필요한 것 |
|---|---|
| 1 | 기존 `public` 메서드 호출 1줄 + 씬 클립 배선 |
| 2 | uGUI 기본 컴포넌트 조합(코드 0줄) |
| 3 | `WaitInputStep`의 구독 대상 교체(`InputHandler` → `PatternHandler`) + 대사·캡션 |
| 4 | `PatternDrillStep`에 `walkOnly` 필드 하나 + 저작 순서 |

**새 클래스가 0개다.**
