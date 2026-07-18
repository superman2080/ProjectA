# ProjectA - 리듬액션게임

## 게임 개요
리듬액션게임. 핸드폰 잠금 패턴처럼 생긴 **패턴인풋**(3x3 = 9개 노드 배치) 위에 노드가 떨어지고, 플레이어가 핸드폰 잠금 패턴처럼 노드를 이어 그어서 처리하는 방식.

전체 흐름: **곡 선택 → 채보(SongChart) 재생 → 패턴이 순차적으로 투입되며 노드 낙하 → 플레이어가 타이밍에 맞춰 이어 긋기 → 판정(Perfect/Good/Miss) → 이펙트·캐릭터 액션 연출.**

데이터 흐름 한 눈에:
```
SongSelectManager ─(GameSession.SelectedChart)→ ChartPlayer ─(SetPattern)→ PatternHandler
                                                                              │
                          ┌───────────────────────────────────────────────────┤ (이벤트 발행)
                          ▼                       ▼                            ▼
                   EffectManager          CharacterActionPlayer          FallingNodeView(낙하)
                   (판정/완성 이펙트)        (베기/피격 애니메이션)         (Pool로 재사용)
```

## 폴더 구조

```
Assets/
├── 01. Scenes/
│   └── DefaultScene.unity          # 메인 게임플레이 씬 (곡 선택 씬은 별도)
├── 02. Scripts/
│   ├── Input/
│   │   ├── InputHandler.cs          # 키보드 1~9 입력 → 인덱스(0~8) 이벤트 발행
│   │   └── IngameInputs.cs          # Input System 자동생성 래퍼 (수정 금지)
│   ├── Pattern/
│   │   ├── Pattern.cs               # ScriptableObject - 패턴 '모양 원본'(+NodeType, PatternData)
│   │   ├── ActivePattern.cs         # 재생 중 패턴 하나의 런타임 상태
│   │   ├── JudgementResult.cs       # enum: Perfect/Good/Miss
│   │   ├── PatternCompletionInfo.cs # 패턴 완료 이벤트 페이로드(struct)
│   │   └── Handler/
│   │       └── Point.cs             # 개별 노드(포인트) - 입력 이벤트 발행 전용
│   ├── UI/
│   │   ├── PatternHandler.cs        # 패턴인풋 전체 관리(판정·큐·노드 스폰의 중심 허브)
│   │   ├── PatternLineRenderer.cs   # 입력 라인 / 가이드 캡슐 렌더러
│   │   ├── FallingNodeView.cs       # 낙하 노드 뷰(IPoolable)
│   │   └── Editor/
│   │       └── PatternHandlerEditor.cs # 디버그 입력 커스텀 인스펙터(에디터 전용)
│   ├── ChartGen/                    # 채보 데이터·재생·굽기(온셋 분석) 시스템
│   │   ├── SongChart.cs             # ScriptableObject - 곡+채보 데이터
│   │   ├── ChartPlayer.cs           # 오디오 시각에 맞춰 SetPattern 흘려보내는 재생 글루
│   │   ├── Core/                    # OnsetDetector, BeatGrid, OnsetGrouper 등 분석 코어(asmdef)
│   │   ├── Editor/                  # PatternChartWindow(굽기 툴), PatternTemplateLibrary
│   │   └── Tests/                   # 코어 유닛테스트(asmdef)
│   ├── Character/
│   │   └── CharacterActionPlayer.cs # 패턴 완료 시 베기/피격 애니메이션 재생
│   ├── Effect/                      # Canvas 이펙트 시스템
│   │   ├── EffectManager.cs         # 이펙트 유일 관리 지점(PatternHandler 이벤트 구독)
│   │   ├── EffectCatalog.cs         # EffectTrigger enum + EffectEntry(트리거→프리팹 매핑)
│   │   ├── CanvasEffectView.cs      # 개별 이펙트 뷰
│   │   └── AmbientEffectController.cs # 배경 앰비언트 강도 조절
│   ├── Statemachine/                # 범용 FSM/HFSM (StateMachine<T>, StateBase, Composite)
│   ├── Pool/
│   │   ├── Pool.cs                  # PoolKey 기반 오브젝트 풀(Singleton)
│   │   └── IPoolable.cs             # OnSpawn/OnDespawn 인터페이스
│   ├── Util/
│   │   └── Singleton.cs             # MonoBehaviour 싱글톤 베이스
│   ├── GameSession.cs               # 씬 간 SelectedChart 전달(DontDestroyOnLoad 싱글톤)
│   └── SongSelectManager.cs         # 곡 선택 → GameSession 등록 → 씬 전환
├── 04. Datas/
│   ├── Patterns/Templates/          # Pattern 에셋(모양 원본)
│   └── Song/                        # SongChart 에셋
└── docs/                            # Research/Plan 설계 문서(주제별) + !Guides(사용 가이드)
```

---

## 핵심 시스템

### 1. 패턴인풋
- 3x3 격자로 배치된 9개의 Point (Point_1 ~ Point_9)
- 포인트 간 중심 간격: **150px**
- 배치 좌표 (AnchoredPosition, 중심 기준):
  - Point_1: (-150, -150) / Point_2: (0, -150) / Point_3: (150, -150)
  - Point_4: (-150, 0)    / Point_5: (0, 0)     / Point_6: (150, 0)
  - Point_7: (-150, 150)  / Point_8: (0, 150)   / Point_9: (150, 150)
- 인덱스는 0~8 (Point_1 = index 0). 인덱스는 `PatternHandler.Initialize(i, this)`로 `patternPoints` **배열 순서에서 주입**된다(게임오브젝트 이름을 파싱하지 않는다).

#### 판정 영역과 시각 표현의 분리
- **Point 본체**(100x100): Image가 투명(알파 0) + `raycastTarget = true` → **판정(레이캐스트) 영역 전용**
- **자식 `Visual`**(60x60): Knob 스프라이트, `raycastTarget = false` → **시각 표현 전용**. `Point.image` 필드가 이것을 가리키며 판정 색상(Perfect/Good/Miss)이 여기에 표시된다.
- 판정 영역용 본체 Image는 `Point.hitGraphic` 필드가 따로 참조한다. **`image`와 `hitGraphic`은 서로 다른 Image이므로 혼동하지 말 것.**

#### 패턴 가이드라인
- 진행 중인 패턴이 지나갈 Point들을 순서대로 잇는 **반투명 캡슐 경로**를 표시해, 노드가 낙하하는 동안 패턴 모양을 미리 볼 수 있게 한다.
- 구현: `PatternLineRenderer`의 `capsuleMode` 옵션 (별도 클래스 없음). 씬 오브젝트는 `PointBackground/PatternGuideLine`.
- 인스펙터 조정 항목: `lineWidth`(캡슐 두께=원 지름), `normalColor`(채움), `outlineWidth`, `outlineColor`, `capSegments`.
- 표시/소멸: `PatternHandler.SetPattern()`에서 생성(노드 스폰보다 먼저), 패턴 완료 시 페이드아웃.
- **렌더 순서 규칙**: 가이드는 `PointBackground`의 **첫 자식**(Point/노드/입력 라인 아래), 실제 입력 라인 `PatternLine`은 **마지막 자식**(맨 위).
- 상세: `docs/PatternGuideLine/`

#### 동적 판정 영역 축소 (조작감)
- 진행 중인 패턴에 **포함되지 않은** Point는 판정 영역이 축소된다 (`PatternHandler.inactiveHitAreaRatio`, 기본 0.5 → 100x100이 실질 50x50).
- 구현: `Point.SetHitAreaRatio(ratio)`가 `hitGraphic.raycastPadding`을 조정 (RectTransform과 자식 Visual은 불변).
- 적용/복구 시점: `SetPattern()`에서 축소, 패턴 종료 및 패턴 없는 대기 구간에서 9개 모두 복구.
- `raycastPadding`은 마우스/터치 경로에만 영향을 준다. 키보드 입력(`ForceDown`)과 통과 노드 자동 인식은 레이캐스트를 거치지 않아 영향받지 않는다.
- 상세: `docs/PointHitArea/`

#### 통과 노드 자동 인식
- 3x3 격자에서 a→b로 직선을 그을 때 정확히 가운데를 지나는 노드가 있으면(예: 1→3은 2를 통과) 자동으로 입력 처리한다.
- 구현: `PatternHandler.GetPassThroughIndex(a, b)` → 통과 노드에 `ForceDown()`. 상세: `docs/PatternPassThrough/`

### 2. 판정 시스템
- `PatternHandler`가 판정을 담당한다. 윈도우(초): `perfectWindow`=0.05, `goodWindow`=0.10.
- `AddPattern(int index)`가 입력을 판정한다:
  - **오답 인덱스**: `AllCorrect`만 취소하고 Miss 색 표시, 진행하지 않음(패턴 정체).
  - **정답 인덱스**: `delta = |Time.time - ExpectedTime|` → `Judge(delta)`로 Perfect/Good/Miss 결정. 정답 노드 위 "타이밍 Miss"는 진행(`Advance`)된다.
- 판정 결과 `JudgementResult`(Perfect/Good/Miss)는 색상·이펙트·캐릭터 액션의 분기 키로 쓰인다.

### 3. 패턴 데이터 모델
**`Pattern`** (ScriptableObject, `PatternSpace`) — 패턴의 **'모양 원본'**. 진행 상태를 갖지 않는다.
- `PatternData[]`(각 `index` 0~8의 나열), `GetNodeType(position)`(0=Start, 마지막=End, 그 외=Progress), `SuccessAnimationClip`(완주 성공 시 캐릭터가 재생할 베기 클립. '모양에 종속된 정적 데이터'라 에셋에 두어도 원칙과 충돌하지 않는다).
- 진행 상태를 에셋에 두면 같은 템플릿을 쓰는 두 패턴이 동시에 살아 있을 때 서로의 상태를 덮어쓴다 → 그래서 상태는 `ActivePattern`으로 분리.

**`ActivePattern`** (일반 C# 클래스) — 재생 중인 패턴 하나의 **런타임 상태**.
- `Template`, `StartTime`, 노드별 입력 시각, `CurrentPosition`, `AllCorrect`, `Deadline`, `ExpectedPointIndex`, `ExpectedTime`, `LastNodeTime`.
- `StartTime`은 **큐 투입 시각(SetPattern 호출 시각)으로 고정**한다. 입력 시각이 이 시점 기준 상대시간이라, 판정 대상으로 승계될 때 다시 잡으면 타이밍이 통째로 밀린다.
- `Deadline` = 마지막 입력 시각 + `goodWindow`. 넘기면 만료 처리(미완료면 `AllCorrect=false`).

#### 패턴 겹침 규칙 (중요)
- 채보는 **다음 패턴의 노드를 이전 패턴이 끝나기 전에 스폰**해야 한다 (스폰 리드타임 ≈1.0초 > 엔트리 간 입력 간격 최소 0.4초). 실제 채보의 95%가 겹친다.
- 반면 **입력(판정) 시각은 겹치지 않는다.** 따라서 `PatternHandler`는 여러 패턴을 **큐(`activePatterns`)**로 들고 있되(연출/스폰 대상), **판정 대상(`JudgeTarget`)은 언제나 선두 하나**다.
- `SetPattern()`은 진행 중인 패턴을 **파기하지 않고 큐에 추가**만 한다. 판정 대상은 선두 패턴이 **완료/만료될 때 같은 프레임에 즉시 승계**된다.
- 낙하 노드는 패턴별 소유자(`owner`)를 추적하며, 패턴이 완료/만료되면 그 패턴의 노드만 즉시 회수한다.
- 상세: `docs/PatternOverlap/`

#### 입력 계층 규칙 (중요)
- **중복 입력 방지의 진실의 원천은 `PatternHandler.connectedIndices` 하나다.** 이미 입력된 Point는 `OnPointPressed`의 가드에서 걸러진다. 이 기록은 **패턴이 끝날 때마다 클리어**되어 다음 패턴에서 같은 Point를 다시 쓸 수 있다. (예전 `Point.isBusy`는 스트로크가 끝나야 풀려 패턴 경계에서 입력을 삼켜 제거됨.)
- **`IsDragging`과 `IsMouseDragging`을 구분한다.** `IsDragging`은 키보드 스트로크 중에도 true다. `Point.OnPointerEnter`(지나가며 입력)는 반드시 **`IsMouseDragging`**을 봐야 한다(안 그러면 키보드 입력 중 마우스 호버만으로 입력됨).
- 키보드 스트로크는 패턴 완료/만료 시 종료된다. **마우스 드래그는 패턴 경계에서 끊지 않는다**(다음 패턴으로 이어 그을 수 있어야 함).
- 상세: `docs/KeyboardInputStuck/`

### 4. 낙하 노드 시스템
- `FallingNodeView`(IPoolable): `Pool`(PoolKey.FallingNode)로 재사용. 스폰 시각에 나타나 목표 Point로 낙하, 도착 시 `OnArrived` 발행.
- `PatternHandler`가 스폰을 스케줄링(`scheduledSpawns`)하고 활성 노드(`activeFallingNodes`)를 소유자별로 추적한다.
- **낙하 속도(행별 상이)**: 생성 Y(`fallSpawnPositionY`)는 모든 행 공통이지만, 화면 경계 밖에서 시작하는 상단 행은 노출시간이 짧아지므로 **화면 안쪽 구간만 `exposureDuration` 동안** 이동하도록 전체 낙하시간을 역산(`ComputeFallDuration`).
- 판정된 노드는 즉시 회수, 미입력으로 자연 도착한 노드는 `OnFallingNodeMissedArrival` 발행 후 회수. 상세: `docs/FallingNode/`

### 5. 채보 시스템 (ChartGen)
- **`SongChart`**(ScriptableObject): `song`(AudioClip), `level`, `bpm`, `beatOffset`, `entries[]`. 각 `SongChartEntry`는 `template`(Pattern), `onsetTimes`(판정 절대시각), `exposureDurations`(노출시간), `spawnTimes`(스폰 절대시각 스냅샷).
- **`ChartPlayer`**: `GameSession.SelectedChart`(없으면 `debugChart`)를 오디오 재생 시각에 맞춰 순차적으로 `PatternHandler.SetPattern`에 흘려보낸다. `audioSource.time >= spawnTimes[0]`이 되면 해당 엔트리를 투입. 재생 가이드: `docs/!Guides/Guide_ChartPlayback.md`
- **굽기 툴**: `Tools/Pattern Chart Tool`(`PatternChartWindow`) — 음원을 온셋 분석(`ChartGen.Core`)해 채보를 굽거나 기존 SongChart를 편집/저장. 가이드: `docs/!Guides/Guide_PatternChartTool.md`. `Core`/`Tests`는 각각 asmdef 보유.

### 6. 캐릭터 액션 (CharacterActionPlayer)
- `PatternHandler.OnPatternComplete` 구독. **완주 성공(AllCorrect)이면 패턴별 베기 클립(`Pattern.SuccessAnimationClip`), 실패면 공용 피격(Hit) 클립**을 번갈아 재생.
- `AnimatorOverrideController`로 단일 슬롯(`Attack`) 스테이트의 placeholder 클립을 런타임에 덮어쓴 뒤 그 스테이트를 `CrossFadeInFixedTime`으로 재생. Attack Layer는 휴지 시 웨이트 0, 재생 중 1, 종료 후 0으로 페이드.
- **겹침 방지 배속**: 다음 패턴까지의 여유(`NextLastNodeTime`)보다 클립이 길면 `AttackSpeed`로 압축하되 `maxAttackSpeed`(기본 2.5) 상한. 상한으로도 안 담기면 다음 액션 CrossFade가 현재 액션을 끊는다(의도된 동작). 상세: `docs/CharacterAction/`

### 7. 이펙트 시스템 (Effect)
- **`EffectManager`**: Canvas 이펙트의 유일 관리 지점. `PatternHandler`의 확장 이벤트(판정/라인연결/패턴완성)만 구독해 카탈로그에서 프리팹을 골라 재생. **PatternHandler는 이펙트를 위해 수정하지 않는다(관심사 분리).**
- **`EffectCatalog`**: `EffectTrigger` enum(Perfect/Good/Miss/PatternCompleteFull/PatternComplete/NodeConnected) + `EffectEntry`(트리거→프리팹+풀 크기). **이펙트 추가 = 카탈로그에 한 줄 추가**(코드 수정 없음). 프리팹 비면 무연출.
- 프리팹별 자체 풀 큐로 관리. 배경 앰비언트는 상시 루프 인스턴스로 배치하고 `SetIntensity`로 강도 조절. 상세: `docs/CanvasEffect/`

### 8. 디버그 입력 (에디터 전용)
- `PatternHandler`의 `#if UNITY_EDITOR` 블록 + `PatternHandlerEditor` 커스텀 인스펙터. **빌드에는 포함되지 않는다.**
- **수동 강제 입력**: F1/F2/F3(인스펙터에서 변경 가능) 또는 Force 버튼으로 판정 대상의 다음 노드를 Perfect/Good/Miss로 강제 입력. `DebugForceInput(result)`가 `ExpectedPointIndex`에 `ForceDown()` → 기존 입력 파이프라인 재사용, `AddPattern`은 `result = debugForcedResult ?? Judge(delta)`로만 분기.
- **자동 Perfect(오토플레이) 토글**: 켜면 각 노드의 도달 타이밍(`ExpectedTime`)마다 자동 Perfect 처리되어 패턴이 저절로 진행.
- 인스펙터: On/Off 마스터 토글, 키 매핑, 마지막 사용 모드 색상 하이라이트, Force 버튼. 마스터가 꺼지면 전부 무반응. 상세: `docs/DebugInput/`

### 9. 씬 전환 / 곡 선택
- **`SongSelectManager`**: 곡 선택 씬에서 버튼으로 `SelectChart(chart)` → `GameSession.SelectedChart`에 등록 후 `DefaultScene` 로드.
- **`GameSession`**(Singleton, DontDestroyOnLoad): 씬을 넘어 `SelectedChart`를 전달. `ChartPlayer`가 읽어 사용.

### 10. 인프라
- **`Singleton<T>`**: `Instance` 게터가 최초 1회 인스턴스를 캐시/생성. `DontDestroy` 플래그로 씬 유지 여부 결정.
- **`Pool`**(Singleton): `PoolKey`(현재 `FallingNode`) → 프리팹 매핑(SerializedDictionary). `Get<T>(key, initializer)`로 대여, `Return(key, obj)`로 반납. 대여 대상은 `IPoolable`(OnSpawn/OnDespawn).
- **`StateMachine<T>`**(범용 FSM/HFSM): Enum 키/인스턴스로 전환, 조건 기반 자동 전환(`RegisterCondition`), AnyState 전환, HFSM용 `CompositeStateBase`. *현재 게임플레이 루프에 직접 배선돼 있진 않은 범용 유틸.*

---

## 이벤트 확장 포인트 (`PatternHandler`)
새 연출/시스템은 아래 이벤트만 구독해 붙인다(PatternHandler 본체 수정 없이 확장).
- `OnJudged(JudgementResult, int index)` — 판정 발생.
- `OnPatternComplete(PatternCompletionInfo)` — 패턴 완료(완주/만료). 성공/실패·타이밍·다음 패턴 정보 포함. (CharacterActionPlayer, EffectManager가 구독)
- `OnNodeConnected(int index, Vector3 world)` — 노드가 라인에 연결.
- `OnFallingNodeSpawned / OnFallingNodeResolved / OnFallingNodeMissedArrival` — 낙하 노드의 스폰/판정/미입력 도착.

## 네임스페이스
- `PatternSpace`: `Pattern`, `PatternData`, `NodeType`, `Point`, `ActivePattern`, `JudgementResult`, `PatternCompletionInfo`
- `ChartGen`: `SongChart`, `SongChartEntry`, `ChartPlayer`, 분석 코어/에디터
- 그 외(`PatternHandler`, `EffectManager`, `CharacterActionPlayer`, `GameSession`, `Singleton`, `Pool` 등)는 전역 네임스페이스

---

## ⚠️ 개발 파이프라인 (가장 중요 — 반드시 준수)

새로운 기능/설계 작업을 시작할 때는 아래 절차를 예외 없이 따른다. 사용자가 명시적으로 절차를 생략하라고 요청하지 않는 한 절대 건너뛰지 않는다.

### 0단계 — 문서 위치 및 네이밍 규칙
- 모든 Research/Plan 문서는 **`docs/{주제폴더}/`** 하위에 작성한다. 주제폴더명은 작업 주제를 파스칼 케이스로 축약한다 (예: `PatternLine`, `ChartGen`).
- 파일명 규칙: `docs/{주제폴더}/Research_{주제}.md`, `docs/{주제폴더}/Plan_{주제}.md` (예: `docs/PatternLine/Plan_PatternLine.md`)
- Research와 Plan은 동일한 `{주제}` 이름과 동일한 폴더를 공유하여 한 쌍임을 알아볼 수 있도록 한다.
- 사용 방법/가이드 문서는 **`docs/!Guides/`** 하위에 작성한다. 파일명: `Guide_{주제}.md`

### 1단계 — Research 문서 작성
- 설계를 시작하기 전, 해당 설계와 관련 있는 기존 파일들(스크립트, 씬, 에셋 등)을 분석한다.
- 분석 결과를 `docs/{주제폴더}/Research_{주제}.md` 파일로 정리한다.
- 관련 클래스/메서드/데이터 흐름, 현재 구현 상태, 제약사항 등을 포함한다.

### 2단계 — Plan 문서 작성
- Research 문서를 근거로 `docs/{주제폴더}/Plan_{주제}.md` 파일을 작성한다.
- Plan은 구현을 여러 **단계(Step)**로 나누어 문서화한다. 각 단계는 이후 구현 완료 여부를 표시할 수 있는 형태로 작성한다 (예: `- [ ] Step 1: ...`).

### 3단계 — 피드백 루프
- 사용자가 Plan 문서 내에 `>>>` (꺽쇠 괄호 3개)로 주석을 남겨 피드백한다.
- 해당 피드백을 반영하여 Plan 문서를 다시 작성하고, 다시 검토받는다.
- 사용자가 만족할 때까지 이 피드백 루프를 반복한다. **Plan이 확정되기 전까지는 절대 코드를 구현하지 않는다.**

### 4단계 — 구현
- 사용자가 "Plan대로 구현해줘"라고 요청하면, 확정된 Plan 문서를 기준으로 전체 구현을 진행한다.
- 구현 진행 시 Plan 문서의 각 단계 항목에 완료 표시(`- [x]`)를 반드시 갱신하여, 어떤 단계가 구현되었고 어떤 단계가 아직 구현되지 않았는지 항상 명확히 알 수 있도록 한다.
- **모든 단계가 완료될 때까지 중간에 멈추지 않고 끝까지 구현을 진행한다.** 확인을 위해 임의로 작업을 중단하지 않는다.
- 구현 도중 새로운 문제(버그, 사이드이펙트, 불필요한 리팩토링 등)를 만들지 않도록 주의하며, Plan에 명시된 범위를 벗어나는 변경을 하지 않는다.

### 요약 규칙
1. 설계 요청 → `docs/{주제폴더}/Research_{주제}.md` 생성
2. Research 기반 → `docs/{주제폴더}/Plan_{주제}.md` 생성 (단계별로 분리)
3. 사용자가 Plan에 `>>>` 피드백 남김 → Plan 재작성 → 반복
4. "구현해줘" 요청 → 확정된 Plan 기준으로 전 단계 끝까지 구현, 각 단계 완료 여부(`[x]`/`[ ]`)를 Plan 문서에 계속 갱신, 새로운 문제 유발 금지
