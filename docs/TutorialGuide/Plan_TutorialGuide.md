# Plan — 튜토리얼 설명 장치 (강조 · 키 입력)

근거: `Research_TutorialGuide.md`. 앞선 작업: `docs/TutorialScene/Plan_TutorialScene.md`(씬·드릴·시퀀스는 완료).

**신규 코드는 파일 셋(뷰 1 · 스텝 2)이고, 기존 코드 수정은 `SequenceAsset.WarnUnknownSlots`의 `case` 둘과 `SequenceRunner`의 정리 한 줄뿐이다.**
`PatternHandler` · `InputHandler` · `DialogUI` · `PatternDrillStep`은 **한 줄도 안 고친다.**

## 이 장치가 하는 일 / 안 하는 일
- **하는 일**: ① Canvas의 한 자리를 지목하고 한 줄로 설명한다 ② 지정한 키가 실제로 눌릴 때까지 기다린다.
- **안 하는 일**: 대사(그건 `DialogStep`), 판정(그건 `PatternHandler`), 입력 모드 전환(그건 `PlayerModeDirector`).
- **⚠ 조작은 설명해도 세계는 설명하지 않는다**(`Story_Overview.md` §0 · `Plan_TutorialScene` 4-4b). 캡션은 기능 문구(*"3번 키"*)를 써도 되지만, **박·균열·그것들에 대해서는 한 글자도 쓰지 않는다** — 그건 카시마의 대사 몫이다.
- **화면 정체성을 대사와 분리한다**: 캡션은 **이름표가 없고 화면 상단**에 뜬다. 대사창(하단·이름표·타자기)과 섞이면 카시마가 UI를 설명하는 인물이 된다.

---

## Step 1: `TutorialHighlightView` (신규 · 표시 계층)
`Assets/02. Scripts/UI/TutorialHighlightView.cs`

- [x] 1-1. **`DialogUI`와 같은 관용구** — `public static TutorialHighlightView Instance`를 `Awake`에서 등록, `OnDestroy`에서 해제. `Singleton<T>`를 쓰지 않는다(없을 때 빈 껍데기를 만들어 내는 것보다 `null`이 낫고, 스텝이 에러를 찍고 건너뛰는 것이 기존 규율이다).
- [x] 1-2. 인스펙터 참조 셋 — `RectTransform ring`(강조 테두리 Image) · `TextMeshProUGUI caption` · `CanvasGroup group`. **비면 `Awake`가 에러를 찍고 멈춘다**(`DialogUI` 규율 — 이름으로 자동 배선하지 않는다).
- [x] 1-3. API 둘.
  - `Show(RectTransform target, string caption, float padding)` — 링을 `target` 자리에 맞추고 캡션을 띄운다.
  - `Hide()` — 링·캡션을 내린다. **여러 번 불러도 안전하다.**
- [x] 1-4. **위치·크기는 복사한다(부모로 붙이지 않는다).** ⚠ Point의 `Visual`에는 `CanvasGroup`이 걸려 있어 자식으로 붙이면 **노브가 꺼질 때 링도 같이 사라진다**(Research §4-2).
  - `ring.position = target.position` · `ring.sizeDelta = target.rect.size * (target.lossyScale.x / ring.lossyScale.x) + padding*2`.
  - 루트 Canvas가 `ScreenSpaceOverlay`(§7-5)라 **월드 투영이 필요 없다** — 카메라·앵글 교체와 무관하다.
- [x] 1-5. 맥동은 한 줄이다 — `Update`에서 `group.alpha = Mathf.Lerp(minAlpha, 1f, Mathf.PingPong(Time.unscaledTime * pulseSpeed, 1f))`. **`unscaledTime`인 이유**: 마무리 실루엣(§14)이 `timeScale`을 건드리는 유일한 곳이고, 안내가 그 영향을 받을 이유가 없다.
- [x] 1-6. `Show(null, ...)`이면 링만 끄고 캡션은 띄운다 — **"화면 어디도 아닌 설명"**이 자연스럽게 성립한다. 분기 하나로 두 쓰임을 덮는다.
- [x] 1-7. 프리팹으로 만들지 않는다 — 씬 하나에서만 쓴다. 두 번째 씬이 필요해지면 그때 뽑는다.

## Step 2: `HighlightStep` (신규 스텝)
`Assets/02. Scripts/SequenceSystem/Steps/HighlightStep.cs`

- [x] 2-1. **기다리지 않는다 — `IsFinished`가 언제나 `true`다.** 강조는 상태 변경이고 대기는 다음 스텝(`WaitInputStep`·`DialogStep`·`PatternDrillStep`)이 한다. 이 분리 덕에 *"강조를 켠 채로 드릴을 시킨다"*가 새 필드 없이 성립한다.
- [x] 2-2. 저작 필드
  - `[SequenceSlot] targetSlot` — **비우면 끄기**다(`Hide()`). 별도의 `HideHighlightStep`을 만들지 않는다.
  - `[TextArea] caption` — 한 줄 설명. 비우면 캡션 없이 링만.
  - `float padding = 24f`
  - `bool showKnob = true` — ⚠ **이 필드가 이 스텝의 핵심 함정을 덮는다**(아래 2-4).
- [x] 2-3. `Enter`에서 `TutorialHighlightView.Instance`를 잡는다. `null`이면 **에러를 찍고 그 스텝만 건너뛴다**(`DialogStep`과 같은 실패 양상).
- [x] 2-4. ⚠ **노브를 같이 켠다.** `PatternHandler.ApplyKnobVisibility`는 **살아 있는 패턴이 쓰는 Point만** 보이게 하므로(§1), 드릴 전에 Point를 강조하면 **빈 자리에 링만 뜬다.**
  - `showKnob`이면 슬롯이 푼 대상에서 `GetComponentInParent<Point>()`를 찾아 `SetKnobVisible(true, 0f)`. **`Point`가 아니면 아무 일도 안 한다** — 대상 종류를 묻는 분기가 아니라 있으면 켜는 한 줄이다.
  - 되돌릴 필요가 없다: 다음 `SetPattern`이 합집합을 다시 계산해 **자동으로 원래 규칙으로 복귀**한다.
- [x] 2-5. `Exit`에서 **끄지 않는다**(2-1의 귀결 — 끄면 다음 스텝에서 강조가 유지되지 않는다). 끄는 것은 언제나 `targetSlot`이 빈 `HighlightStep`이다.
- [x] 2-6. `Label` — `Highlight 'Point4'` / `Highlight (off)`.

## Step 3: `WaitInputStep` (신규 스텝)
`Assets/02. Scripts/SequenceSystem/Steps/WaitInputStep.cs`

- [x] 3-1. 저작 필드
  - `enum InputKind { Node, Dodge, Move, Interact }` + `InputKind kind`
  - `int nodeIndex`(0~8, `Node`일 때만) · `bool anyNode`
  - `[Min(1)] int requiredCount = 1` — 연타·반복 확인용
  - `[SequenceSlot] inputHandlerSlot`
- [x] 3-2. **`InputHandler`의 기존 이벤트만 구독한다** — `OnKeyPressed(int)` · `OnDodgePressed` · `OnInteractPressed`. `Move`만 폴링이다(`MoveInput.sqrMagnitude > 0.04f`인 시간이 0.3초를 넘으면 통과 — 한 프레임 튀는 값으로 통과하면 "움직여 봐"를 안 배운다).
  - ⚠ **`UnityEngine.InputSystem`을 참조하지 않는다.** 키 바인딩은 `IngameInputs.inputactions` 한 곳이 소유한다(`InputHandler` 주석의 계약).
- [x] 3-3. ⚠ **모드를 바꾸지 않는다.** `Node`·`Dodge`는 `Combat`, `Move`·`Interact`는 `Explore` 맵에서만 발행되는데 모드의 주인은 `PlayerModeDirector`다(§11-2). 진입 시 `inputHandler.Mode`가 안 맞으면 **경고를 찍고 그대로 기다린다** — 조용히 영원히 안 끝나는 것보다 낫다.
- [x] 3-4. `Exit`에서 반드시 구독 해제(`Stop()`·`OnDisable`에서도 러너가 `Exit`을 부른다 — `PatternDrillStep`과 같은 규율).
- [x] 3-5. `Label` — `WaitInput Node 4 x1` / `WaitInput Dodge`.
- [x] 3-6. **타임아웃을 두지 않는다.** 튜토리얼은 "될 때까지"가 규칙이고(드릴이 이미 그렇다), 시간으로 넘겨 주면 못 배운 채 다음으로 간다.

## Step 4: 기존 코드 최소 수정
- [x] 4-1. `SequenceAsset.WarnUnknownSlots`의 `switch`에 `case HighlightStep` · `case WaitInputStep` 추가(각 1~2줄). 두 스텝은 `TargetSlot` / `InputHandlerSlot` 게터를 노출한다(`PatternDrillStep`과 같은 모양).
- [x] 4-2. **`SequenceRunner`의 종료·중단 경로에 `TutorialHighlightView.Instance?.Hide()` 한 줄.** ⚠ 강조는 스텝이 안 끄면 화면에 남는데, `Stop()`·`OnDisable`은 저작이 닿지 않는 경로다(§15의 "복구 경로가 셋"과 같은 근거). `PlayerModeDirector` 복구가 이미 있는 그 자리다.
- [x] 4-3. 그 외 수정 없음 — `PatternHandler` · `InputHandler` · `DialogUI` · `Point` · `PatternDrillStep` 전부 그대로.

## Step 5: 씬 배선 (`Tutorial.unity`)
- [x] 5-1. `Canvas` **직속**으로 `TutorialGuide` 오브젝트(형제 인덱스 **마지막** — 패턴인풋·링 위에 그려야 한다).
  - 자식 `Ring`(테두리 스프라이트 Image, `raycastTarget = false`) · `Caption`(TMP, 화면 **상단** 중앙, 한글 폰트는 `NotoSansKR SDF`).
  - ⚠ **`raycastTarget`을 전부 끈다.** 켜져 있으면 마우스로 노드를 그을 때 강조 판이 레이캐스트를 가로채 **입력이 통째로 죽는다.**
  - ⚠ 캡션은 **패턴인풋 영역과 겹치지 않는 자리**에 둔다(Point_8이 (0, 700), Point_9가 (700, 700)).
- [x] 5-2. `TutorialHighlightView` 컴포넌트 + 참조 셋 배선. 시작 상태는 **꺼짐**.
- [x] 5-3. `SequenceRunner`의 `bindings`에 슬롯 셋 추가 — `PatternInput`(= 씬의 `PatternHandler` RectTransform, 1400x1400) · `Point3`(= `Point_3/Visual`, 90x90) · `InputHandler`(= 같은 오브젝트의 `InputHandler`).
  ⚠ 이 씬에서 패턴인풋 컨테이너의 이름은 `PointBackground`가 아니라 **`PatternHandler`**다(CLAUDE.md §1의 이름은 다른 씬 기준).
  `Point4`·`Point5`는 **배선하지 않았다** — 쓰는 스텝이 없다(안 쓰는 슬롯은 빈 칸으로 남아 경고를 만든다).
  - ⚠ **Point 본체(150x150)가 아니라 자식 `Visual`(90x90)을 배선한다** — 링이 노브 크기에 맞아야 "이것"이 무엇인지 읽힌다.
- [x] 5-4. `Seq_Tutorial.asset`의 `requiredBindings`에 위 슬롯 이름을 더한다(안 하면 `WarnUnknownSlots`가 경고).

## Step 6: `Seq_Tutorial` 저작 — 설명 스텝 끼워 넣기
기존 커리큘럼(대사 5 · 드릴 4 · 해금 1)은 **그대로 두고 사이에만 넣는다.** 드릴의 패턴·다다미는 손대지 않는다.

- [x] 6-1. 첫 드릴 앞
  1. `Highlight` — 대상 `PatternInput`, 캡션 *"화면 가운데 아홉 개의 점 — 순서대로 이어 긋는다"*
  2. `Highlight` — 대상 `Point3`, 캡션 *"이 점은 숫자키 3"*
  3. `WaitInput` — `Node` / `nodeIndex = 2` (Point_3 = 인덱스 2)
  4. `Highlight` — **대상 없음**(끄기)
  ⚠ **Point_4가 아니라 Point_3이다** — 첫 드릴이 `Pattern(3, 4, 5)`라 그 패턴의 **첫 노드**를 강조해야 손이 이어진다. 저작 초안의 Point_4는 실제 커리큘럼과 어긋나 있었다.
- [x] 6-2. 타이밍 대사 뒤 — `Highlight`(**대상 없이 캡션만**, *"원이 점 크기로 줄어드는 순간에 누른다"*) → 드릴 → 끄기.
  ⚠ 두 번째 드릴은 `Pattern(6, 7, 3, 4)`라 첫 노드가 Point_6이다. 특정 점을 지목하면 슬롯이 하나 더 필요해지는데 **이 캡션의 내용은 어느 점에도 매이지 않으므로** 1-6의 "캡션만" 경로를 쓴다.
  ⚠ 이 캡션은 **"어떻게 누르는가"까지만** 말한다. 왜 박이 필요한지는 카시마 대사(§4-4)의 몫이다.
- [x] 6-3. 연타 드릴 앞 — `Highlight`(대상 `PatternInput`, 캡션 *"아무 점이나 빠르게"*) → `WaitInput`(`anyNode = true`, `requiredCount = 5`) → 끄기 → 드릴.
- [x] 6-4. **회피는 이번 범위 밖이다.** 튜토리얼 씬에는 `DodgeDirector`가 비활성이고 기습이 없어 **회피 키를 눌러 볼 상황 자체가 없다.** `WaitInput.Dodge`는 만들어 두되 시퀀스에는 안 넣는다(1스테이지에서 처음 만나게 한다).
- [x] 6-5. 마지막 스텝 앞에 **끄기 `Highlight` 하나**를 반드시 둔다(4-2의 안전망은 안전망일 뿐 저작이 먼저다).

## Step 7: 검증
- [x] 7-1. 강조가 대상 노브에 정확히 겹치고 맥동한다.
- [x] 7-2. **강조 중에도 마우스로 노드를 그을 수 있다**(`raycastTarget` 확인 — 5-1의 함정).
- [x] 7-3. 패턴이 없는 구간에서 Point를 강조해도 **노브가 보인다**(2-4).
- [x] 7-4. `WaitInput`이 **지정한 키에만** 통과한다(다른 숫자키로는 안 넘어간다).
- [x] 7-5. 드릴이 시작되면 노브 표시가 `PatternHandler`의 원래 규칙(합집합)으로 돌아온다.
- [x] 7-6. 시퀀스를 중간에 끊어도(플레이 정지 · `Stop()`) 강조가 화면에 남지 않는다.
- [x] 7-7. 콘솔에 `선언되지 않은 슬롯` 경고가 없다.
- [x] 7-8. 기존 드릴 4개의 동작이 그대로다(실패 시 다다미가 서 있고 재시도된다).

---

## 하지 않는 것 (필요해지면 붙이는 자리)
- **화면 어둡게 하기(딤 + 구멍).** 마스크·스텐실이 필요하고 패턴인풋 위에서 레이캐스트를 가로챌 위험이 있다. 링 + 맥동으로 지목이 충분히 읽힌다. → 안 읽히면 `TutorialHighlightView`에 딤 Image 하나를 더한다.
- **키캡 스프라이트·손 모양 아이콘.** 캡션 텍스트가 그 일을 한다.
- **월드 오브젝트 강조**(다다미에 화살표). 대상이 Canvas RectTransform으로 고정이라 지금 범위 밖이다. → `Show`에 월드 좌표 오버로드 하나.
- **스킵 UI.** `Plan_TutorialScene` 미결정 E의 결정을 유지한다(§0이 막는 부류).
- **실패 시 안내 캡션.** 반복되면 환경음이 된다(`Plan_TutorialScene` 4-4b와 같은 근거).
- **강조 대상 여러 개 동시.** 링이 하나면 "지금 여기"가 흐려지지 않는다.

---

## 미결정 (구현 전에 정할 것)

| # | 정할 것 | 권장 | 근거 |
|---|---|---|---|
| A | 강조 링의 모양 | **결정: 흰색 테두리**(내장 `UI/Skin/UISprite.psd` + `Type = Sliced` + `fillCenter = false`) | 새 스프라이트 에셋이 0개다. 둥근 사각형이라 원형은 아니지만 노브를 덮지 않고 테두리만 남는다 — 원형이 꼭 필요하면 스프라이트만 갈아 끼운다. 색은 판정 색(Perfect/Good/Miss)과 겹치지 않는 흰색 |
| B | 캡션 자리 | **화면 상단 중앙** | 하단은 대사창, 우하단은 닷지 포인트(§11-8), 가운데는 패턴인풋 |
| C | `WaitInput`을 대사 중에도 받나 | **안 받는다**(스텝이 선형이라 자동) | 새 규칙이 아니라 시퀀스가 선형 큐인 결과다 |

---

## 구현 기록 (2026-09-03)

- **신규 코드 3개** — `UI/TutorialHighlightView.cs` · `SequenceSystem/Steps/HighlightStep.cs` · `SequenceSystem/Steps/WaitInputStep.cs`.
- **기존 코드 수정 2곳** — `SequenceAsset.WarnUnknownSlots`에 `case` 둘, `SequenceRunner.RestoreMode` → **`RestorePresentation`**으로 이름을 바꾸고 맨 앞에 `TutorialHighlightView.Instance?.Hide()` 한 줄.
  ⚠ **이름을 바꾼 이유**: 그 메서드가 이제 모드만 되돌리는 것이 아니다. 그리고 `Hide()`는 `modeApplied` 가드 **위**에 있어야 한다 — 튜토리얼은 `holdMode = Keep`이라 `modeApplied`가 **언제나 false**이고, 가드 아래에 두면 안전망이 통째로 안 돈다.
- **⚠ 계층 구조를 한 번 고쳤다** — 처음에 `CanvasGroup`을 `TutorialGuide` 루트에 두고 루트를 꺼서 시작했는데, **꺼진 오브젝트는 `Awake`가 돌지 않아 `Instance`가 영영 null**이었다. 지금은 루트가 항상 켜져 있고 껐다 켜는 대상은 자식 `Content`다(`Ring`·`Caption`이 그 아래).
- **씬** — `Canvas/TutorialGuide`(마지막 형제) → `Content`(`CanvasGroup`, 시작 시 꺼짐) → `Ring` · `Caption`(TMP, 상단 중앙 y −160, `NotoSansKR SDF`, 60pt). 전부 `raycastTarget = false` + `blocksRaycasts = false`.
- **에셋** — `Seq_Tutorial`이 10스텝 → **19스텝**. 기존 스텝은 순서·내용 그대로이고 사이에만 들어갔다.

### 플레이 검증 (실측)
- 시퀀스가 step 3(`WaitInput Node 2`)에서 **멈춘다**. 그 시점에 링이 `Point_3/Visual` 위에 정확히(중심 거리 **0.00**) 138x138로 떠 있고 캡션이 `이 점은 숫자키 3`이다.
- **노브가 보인다** — `Point_3/Visual`의 `CanvasGroup.alpha = 1.00`. 패턴이 하나도 살아 있지 않은 구간이라 2-4가 없으면 0이었다.
- **틀린 키(index 0)로는 안 넘어간다**(`count = 0` 유지). **맞는 키(index 2)에 통과** → step 5(`Drill 'Pattern(3, 4, 5)'`)로 진행하며 강조가 꺼지고 노브 알파가 0으로 복귀(= `PatternHandler`가 표시 규칙을 되찾음).
- **`Stop()` 안전망** — 강조를 켠 채 `runner.Stop()`을 부르면 강조가 내려간다.
- 링 배치 계산(에디터): 90x90 대상 → 138x138, 1400x1400 대상 → 1448x1448, 중심 거리 둘 다 0.00. `Hide()` 두 번 호출 안전.
- 콘솔 에러·경고 **0건**.

### ⚠ 검증하지 못한 것
- **7-1의 맥동**과 **7-2의 마우스 입력**은 눈으로 확인하지 못했다(원격 실행이라 화면을 못 본다). 구조상으로는 `Ring`/`Caption`의 `raycastTarget`과 `Content`의 `blocksRaycasts`가 전부 꺼져 있어 레이캐스트를 가로채지 않는다.
- **7-8의 드릴 4개**는 이번에 다시 완주시키지 않았다. 스텝을 사이에 끼웠을 뿐 드릴 스텝·패턴·다다미 배선을 손대지 않았다.

### 알려진 잔여물 (작고 의도적)
- `WaitInput`으로 연습 삼아 누른 키는 `PatternHandler.OnPointPressed`를 그대로 타므로 **`connectedIndices`에 남는다**(판정 대상이 없어 `AddPattern`은 즉시 반환한다 — 색도 소리도 안 난다). 그 기록은 **첫 패턴이 완료될 때 지워진다.**
  드릴 1의 첫 노드가 곧 그 연습 노드(index 2)인데, 중복 가드가 **'지금 기다리는 노드'를 예외로 두므로**(§3) 막히지 않는다. 연타 구간의 `anyNode`는 연타 경로가 그 가드를 통째로 건너뛴다(§2-1).
  → 실제로 거슬리면 `WaitInputStep.Exit`에서 라인을 걷어내는 한 줄을 붙인다(지금은 안 붙였다 — 화면에 남는 것이 없다).
