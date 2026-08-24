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
                   EffectManager          CharacterActionPlayer          FocusRingView(수축)
                   (판정/완성 이펙트)        (베기/피격 애니메이션)         (Pool로 재사용)
```

## 폴더 구조

```
Assets/
├── 01. Scenes/
│   ├── BattleScene.unity           # 메인 게임플레이 씬 (구 DefaultScene — 오래된 문서는 옛 이름으로 부른다)
│   └── SongSelectScene.unity       # 곡 선택 씬
├── 02. Scripts/
│   ├── Input/
│   │   ├── InputHandler.cs          # 키보드 1~9 입력 → 인덱스(0~8) 이벤트 발행
│   │   └── IngameInputs.cs          # Input System 자동생성 래퍼 (수정 금지)
│   ├── Pattern/
│   │   ├── Core/ClipAlignment.cs    # 임팩트 프레임 정렬(플레이어·적 공용, asmdef)
│   │   ├── Pattern.cs               # ScriptableObject - 패턴 '모양 원본'(+NodeType, PatternData)
│   │   ├── ActivePattern.cs         # 재생 중 패턴 하나의 런타임 상태
│   │   ├── JudgementResult.cs       # enum: Perfect/Good/Miss
│   │   ├── PatternCompletionInfo.cs # 패턴 완료 이벤트 페이로드(struct)
│   │   └── Handler/
│   │       └── Point.cs             # 개별 노드(포인트) - 입력 이벤트 발행 전용
│   ├── UI/
│   │   ├── PatternHandler.cs        # 패턴인풋 전체 관리(판정·큐·노드 스폰의 중심 허브)
│   │   ├── PatternLineRenderer.cs   # 입력 라인 / 가이드 캡슐 렌더러
│   │   ├── FocusRingView.cs         # 포커스 링 뷰 — Point 자리에서 줄어든다(IPoolable)
│   │   ├── DodgePointView.cs        # 기습 회피 입력 지점 — 화면 우하단 고정(§11-8)
│   │   ├── ScoreHudView.cs          # 점수·콤보 HUD(순수 표시, §12)
│   │   ├── ComboPostFxView.cs       # 콤보 단계 포스트FX 볼륨 웨이트(§12)
│   │   └── Editor/
│   │       └── PatternHandlerEditor.cs # 디버그 입력 커스텀 인스펙터(에디터 전용)
│   ├── ChartGen/                    # 채보 데이터·재생·굽기(온셋 분석) 시스템
│   │   ├── SongChart.cs             # ScriptableObject - 곡+채보 데이터
│   │   ├── ChartPlayer.cs           # 오디오 시각에 맞춰 SetPattern 흘려보내는 재생 글루
│   │   ├── Core/                    # OnsetDetector, BeatGrid, OnsetGrouper 등 분석 코어(asmdef)
│   │   ├── Editor/                  # PatternChartWindow(굽기 툴), PatternTemplateLibrary
│   │   └── Tests/                   # 코어 유닛테스트(asmdef)
│   ├── Character/
│   │   ├── CharacterActionPlayer.cs # 베기/피격 애니메이션 + 수렴 로코모션
│   │   ├── PlayerCombatMover.cs     # 결투 이동·회전(OnDuelScheduled 구독)
│   │   └── Editor/                  # AnimationClipTrimmerWindow(짝 저작 툴)
│   ├── Slice/                       # 베이는 표적(연출 전용)
│   │   ├── SliceSet.cs              # ScriptableObject - 굽기 산출물(원본+조각 프리팹 N개+절단 평면)
│   │   ├── SliceTargetDirector.cs   # 표적 유일 관리 지점(예약·확정·풀링)
│   │   ├── SliceTargetView.cs       # 표적 하나의 −Z 이동 / 절단 / 소멸
│   │   ├── SlicePiece.cs            # 조각 하나의 운동(물리 없이 닫힌 식)
│   │   ├── Core/                    # MeshSliceBaker 등 순수 기하 로직(asmdef)
│   │   ├── Editor/                  # MeshSliceBakerWindow(획 긋기 굽기 툴)
│   │   └── Tests/                   # 절단 코어 유닛테스트(asmdef)
│   ├── Camera/                      # 카메라 연출
│   │   ├── CameraDirector.cs        # 유일 관리 지점 — 쉐이크 · 프레이밍(TargetGroup) · 인트로(스플라인)
│   │   └── CameraCueCatalog.cs      # CameraTrigger enum + CameraCueEntry(트리거→쉐이크 설정)
│   ├── Enemy/                       # 무쌍 전투(무대 · 상대 배정 · 처치)
│   │   ├── EnemyDirector.cs         # 유일 관리 지점(무대 배치·표적 선택·결투 계획·처치)
│   │   ├── EnemyView.cs             # 적 하나의 이동/클립/사망
│   │   ├── EnemyDefinition.cs       # 적 종류(프리팹 + DeathSliceSet)
│   │   ├── EnemyCue.cs              # Attacker enum + 채보 엔트리의 전투 지시
│   │   ├── DodgeDirector.cs         # 기습·회피의 유일 관리 지점(§11-8)
│   │   ├── Core/EnemyRing.cs        # 무대 배치·표적 선택의 순수 계산(asmdef)
│   │   └── Tests/                   # 배치·선택 유닛테스트(asmdef)
│   ├── Effect/                      # Canvas 이펙트 시스템
│   │   ├── EffectManager.cs         # 이펙트 유일 관리 지점(PatternHandler 이벤트 구독)
│   │   ├── EffectCatalog.cs         # EffectTrigger enum + EffectEntry(트리거→프리팹 매핑)
│   │   ├── CanvasEffectView.cs      # 개별 이펙트 뷰
│   │   └── AmbientEffectController.cs # 배경 앰비언트 강도 조절
│   ├── HitStop/
│   │   └── HitStopDirector.cs       # 히트스톱 유일 관리 지점(§7-3)
│   ├── Score/
│   │   ├── ScoreDirector.cs         # 채점 유일 관리 지점(§12)
│   │   ├── Core/                    # ScoreMath, ScoreResult(asmdef)
│   │   └── Tests/                   # 채점 코어 유닛테스트(asmdef)
│   ├── Audio/
│   │   └── SoundManager.cs          # SfxTrigger 요청만 받는 싱글톤(게임플레이를 모른다)
│   ├── Props/Editor/                # 배경 프롭·스테이지 배치 에디터 툴(§13, 에디터 전용)
│   ├── Pool/
│   │   ├── Pool.cs                  # PoolKey 기반 오브젝트 풀(Singleton)
│   │   └── IPoolable.cs             # OnSpawn/OnDespawn 인터페이스
│   ├── Util/
│   │   └── Singleton.cs             # MonoBehaviour 싱글톤 베이스
│   ├── GameSession.cs               # 씬 간 SelectedChart 전달(DontDestroyOnLoad 싱글톤)
│   ├── PlayerHealth.cs              # 목숨(§7-6)
│   └── SongSelectManager.cs         # 곡 선택 → GameSession 등록 → 씬 전환
├── 03. Prefabs/StoryProps/          # 배경 프롭 낱개 프리팹(툴 산출물, §13)
├── 04. Datas/
│   ├── Patterns/Templates/          # Pattern 에셋(모양 원본)
│   └── Song/                        # SongChart 에셋
├── 06. Models/Props/                # 배경 프롭 FBX(Blender 산출물, §13)
└── docs/                            # Research/Plan 설계 문서(주제별) + !Guides(사용 가이드) + Story(서사 설계)
```

---

## 핵심 시스템

### 1. 패턴인풋
- 3x3 격자로 배치된 9개의 Point (Point_1 ~ Point_9)
- 포인트 간 중심 간격: **700px**
- 배치 좌표 (AnchoredPosition, 중심 기준):
  - Point_1: (-700, -700) / Point_2: (0, -700) / Point_3: (700, -700)
  - Point_4: (-700, 0)    / Point_5: (0, 0)     / Point_6: (700, 0)
  - Point_7: (-700, 700)  / Point_8: (0, 700)   / Point_9: (700, 700)
- **입력 영역과 플레이어 시점을 분리하지 않는다(몰입감).** 예전에는 `Canvas/TileArea`(우측 1280px 반투명 검정 스트립)가 입력 영역을 화면 오른쪽에 따로 떼어 놓았지만, 이 컨테이너와 `Boarder`는 제거됐다. 지금은 `PointBackground`가 **Canvas 직속**(형제 인덱스 1 — `AmbientEffectLayer` 위, `EffectOverlayLayer` 아래)이며 앵커·anchoredPosition 모두 화면 중앙(0.5, 0.5)/(0,0)이라 **Point_5가 화면 정중앙**에 온다(Canvas 3840x2160 기준 world (1920,1080)).
- `PointBackground`는 좌표계 기준점 + `PatternHandler`/`InputHandler`의 부착 지점일 뿐 **더 이상 배경 판이 아니다**(`Image`/`CanvasRenderer` 제거). 캐릭터 위에 반투명 판이 덮이지 않는다.
- 인덱스는 0~8 (Point_1 = index 0). 인덱스는 `PatternHandler.Initialize(i, this)`로 `patternPoints` **배열 순서에서 주입**된다(게임오브젝트 이름을 파싱하지 않는다).

#### 판정 영역과 시각 표현의 분리
- **Point 본체**(150x150): Image가 투명(알파 0) + `raycastTarget = true` → **판정(레이캐스트) 영역 전용**
- **자식 `Visual`**(90x90): Knob 스프라이트, `raycastTarget = false` → **시각 표현 전용**. `Point.image` 필드가 이것을 가리키며 판정 색상(Perfect/Good/Miss)이 여기에 표시된다.
- 판정 영역용 본체 Image는 `Point.hitGraphic` 필드가 따로 참조한다. **`image`와 `hitGraphic`은 서로 다른 Image이므로 혼동하지 말 것.**

#### 패턴 가이드라인
- 진행 중인 패턴이 지나갈 Point들을 순서대로 잇는 **반투명 캡슐 경로**를 표시해, 노드가 낙하하는 동안 패턴 모양을 미리 볼 수 있게 한다.
- 구현: `PatternLineRenderer`의 `capsuleMode` 옵션 (별도 클래스 없음). 씬 오브젝트는 `PointBackground/PatternGuideLine`.
- 인스펙터 조정 항목: `lineWidth`(캡슐 두께=원 지름), `normalColor`(채움), `outlineWidth`, `outlineColor`, `capSegments`.
- 표시/소멸: `PatternHandler.SetPattern()`에서 생성(노드 스폰보다 먼저), 패턴 완료 시 페이드아웃.
- **렌더 순서 규칙**: 가이드는 `PointBackground`의 **첫 자식**(Point/노드/입력 라인 아래), 실제 입력 라인 `PatternLine`은 **마지막 자식**(맨 위).
- 상세: `docs/PatternGuideLine/`

#### 동적 판정 영역 축소 (조작감)
- 진행 중인 패턴에 **포함되지 않은** Point는 판정 영역이 축소된다 (`PatternHandler.inactiveHitAreaRatio`, 기본 0.5 → 150x150이 실질 75x75). 비율 기반이라 Point 크기를 바꿔도 따라온다(`SetHitAreaRatio`가 RectTransform의 현재 rect에서 패딩을 계산).
- 구현: `Point.SetHitAreaRatio(ratio)`가 `hitGraphic.raycastPadding`을 조정 (RectTransform과 자식 Visual은 불변).
- 적용/복구 시점: `SetPattern()`에서 축소, 패턴 종료 및 패턴 없는 대기 구간에서 9개 모두 복구.
- `raycastPadding`은 마우스/터치 경로에만 영향을 준다. 키보드 입력(`ForceDown`)과 통과 노드 자동 인식은 레이캐스트를 거치지 않아 영향받지 않는다.
- 상세: `docs/PointHitArea/`

#### 노브 표시/숨김 (⚠ 판정 영역 축소와 기준이 다르다)
- 지금 쓰이는 Point의 **노브(자식 `Visual`)만 보이고 나머지는 감춰진다**. `PatternHandler.knobFadeDuration`(기본 0.12초)로 빠르게 페이드.
- **표시 기준은 살아 있는 모든 패턴(`activePatterns`)이 쓰는 Point의 합집합**이다(`ApplyKnobVisibility`). 판정 대상 하나만 보면 안 된다 — 링은 **큐에 얹히기만 한 패턴에도 스폰**되므로, 그렇게 하면 아직 감춰진 노브 위에서 다음 패턴의 링이 약 0.2초간 수축해 타이밍 단서가 깨진다. 합집합이라 "이전 패턴의 마지막 노드 = 다음 패턴의 첫 노드"인 인수인계 구간에서도 깜빡임이 없다.
- 반면 **판정 영역 축소(`ApplyHitAreas`)는 `JudgeTarget` 기준을 유지한다.** 목적이 다르다 — 축소는 입력 오인 방지, 노브 표시는 시선 유도.
- 페이드는 `Visual`의 **`CanvasGroup`** 알파로 한다. `Point.image`의 알파를 쓰면 안 된다 — `SetJudgementColor`/`ResetColor`가 알파 포함 색을 통째로 대입해 페이드를 덮어쓴다.
- `CanvasGroup`은 `Visual` 서브트리에만 걸리므로 **부모 본체의 레이캐스트(`hitGraphic`)에는 영향이 없다. 노브를 감춰도 판정 영역은 살아 있다**(영역 제어는 위의 `inactiveHitAreaRatio`가 담당).
- 상세: `docs/FocusRing/`

#### 통과 노드 자동 인식
- 3x3 격자에서 a→b로 직선을 그을 때 정확히 가운데를 지나는 노드가 있으면(예: 1→3은 2를 통과) 자동으로 입력 처리한다.
- 구현: `PatternHandler.GetPassThroughIndex(a, b)` → 통과 노드에 `ForceDown()`. 상세: `docs/PatternPassThrough/`

### 2. 판정 시스템
- `PatternHandler`가 판정을 담당한다. 윈도우(초): `perfectWindow`=0.05, `goodWindow`=0.10.
- `AddPattern(int index)`가 입력을 판정한다:
  - **오답 인덱스**: `AllCorrect`만 취소하고 Miss 색 표시, 진행하지 않음(패턴 정체).
  - **정답 인덱스**: `delta = |Time.time - ExpectedTime|` → `Judge(delta)`로 Perfect/Good/Miss 결정. 정답 노드 위 "타이밍 Miss"는 진행(`Advance`)된다.
- 판정 결과 `JudgementResult`(Perfect/Good/Miss)는 색상·이펙트·캐릭터 액션의 분기 키로 쓰인다.

### 2-1. 연타 노트 (MashNote)
- **아무 노드나 눌러 타수를 채우는 구간.** 뮤즈대쉬 샌드백 · 태고의 달인 연타. `Pattern.isMash` + `mashTargetHits` + `mashHitClips`. **`Attacker.Player` 전용**이고 지정 타수를 채우면 성공, 못 채우면 실패다.
- **⚠ 목표 타수를 `patternDatas`로 표현할 수 없다** — `OnValidate`가 중복 인덱스를 막고 노드는 9개뿐이라 20타를 20칸으로 못 적는다. 연타의 `patternDatas`는 **정확히 1칸**이고 그것은 **게이지(포커스 링)가 앉을 자리**를 뜻한다(`OnValidate`가 에러로 강제).
- **타수는 패턴이, 창은 채보가 든다** — `SongChartEntry.onsetTimes = [시작, 끝]` **두 값뿐**이다. 타수만큼 채우면 진실의 원천이 둘이 되어 어긋난 조합이 조용히 만들어진다. `Deadline = 끝 + goodWindow`가 그대로 성립해 **채보 스키마는 한 필드도 안 바뀌었다**.
- **⚠ 완료 시각은 언제나 `Deadline` 하나다**(성공이든 실패든). 타수를 채운 자리에서 완료하면 다음 패턴이 **같은 프레임에 승계**돼(§3) **초과 타격이 그 패턴의 입력으로 흘러 들어가 `AllCorrect`를 깬다**. `AddPattern`의 완료 가드가 연타를 빼는 것이 그 방어의 전부이고, 완료는 `ExpireOverduePatterns`만 한다.
- **타이밍 판정이 없다** — 창 안의 모든 타격이 `Perfect`다. 연타는 "언제"가 아니라 "몇 번"을 묻는다. ⚠ 그래서 **연타 구간은 점수 효율이 일반 패턴보다 좋다**(Good이 안 나온다) — 밸런스는 타수와 창 길이로 잡는다(판정으로 잡으면 "빨리 치면 손해"가 되어 연타가 아니게 된다).
- **⚠ 입력 계층의 두 가드를 건너뛴다**(§3): `connectedIndices` 중복 가드는 "같은 Point는 패턴당 한 번"이라 **2타부터 전부 삼키고**, 통과 노드 자동 인식은 대각선 입력 하나를 **두 타로 세게** 한다. 입력 라인도 안 그린다(연타는 획이 아니다).
- **시각 계층 셋이 갈린다**: 판정 영역은 **9개 전부 온전**(`ApplyHitAreas`), 노브도 **9개 전부**(`ApplyKnobVisibility`가 합집합에 9개를 통째로 넣는다), 가이드라인은 **끈다**(이을 순서가 없다). 게이지는 링 **하나**이고 `FocusRingView.SetLabel`이 남은 타수를 쓴다 — ⚠ 일반 패턴의 `IndexLabel`은 여전히 비활성이다(링이 둘 겹치면 숫자가 뭉개진다). 연타는 링이 하나라 그 근거가 성립하지 않는다.
- **점수는 이벤트 하나로 갈린다** — 목표 이내 타격만 `OnJudged`를 쏜다. 그 한 줄이 "**그 이상은 애니메이션만 나오고 점수는 안 오른다**"의 구현 전부다. `ScoreDirector`는 두 줄만 바뀌었다(총량·뺄셈 좌변이 `MashTargetHits`를 본다) — **⚠ 둘이 짝이라 하나만 고치면 달성도가 1을 못 넘거나 넘어 버린다.** `ScoreMath`는 한 줄도 안 바뀌었다.
- **타격 재생은 정렬 대상이 아니다**(§6의 전제가 없다 — 타격 시각을 플레이어가 정하므로 역산할 시각이 없다). `ImpactTime`을 정렬 앵커가 아니라 **재생 시작점**으로 써서 와인드업을 건너뛰고 **써는 구간만** 나온다. 클립은 리스트를 **번갈아** 돈다(`hitClips` 관용구, 커서는 패턴이 바뀌면 리셋). ⚠ `isSwing: true`여야 `ApplyHitStop`의 `swingActive` 가드를 통과한다. ⚠ 그 결과 `impactAuthored = 0` → **`DuelCurveTime`이 `NaN`**이라 결투 거리 커브(§11-9)가 연타 구간에 구동되지 않는다 — 연타 중 플레이어가 미끄러지면 안 되므로 **자동으로 맞는 지점**이다. ⚠ 연타는 **리드인 클립(§6-1)을 못 쓴다**(타격이 시퀀스를 끊는다, `OnValidate`가 막는다).
- **적은 타격마다 젖혀진다** — `EnemyDirector`가 `OnMashHit`을 구독해 `currentOpponent.PlayMashReaction(Pattern.EnemyHit)`. **새 슬롯을 안 만든다**(그 슬롯의 정의가 이미 "맞았는데 안 죽은 적의 리액션"이다). **⚠ 예약하지 않는다** — `Resolve`의 리액션은 확정이 칼보다 이르러서 미루지만, 연타는 맞는 순간이 곧 지금이다. **⚠ `Phase`를 안 건드린다**(`Recover`로 넘기면 견제·이동이 끊긴다). **⚠ 클립은 완주하지 못한다** — 초당 8타면 앞 ~0.12초만 보이고 다시 시작하므로 **젖혀지는 동작이 클립 맨 앞에 와야 한다**.
- **연타 실패는 물러나지 않고 제자리 패링**이다 — `ResolveRetreatDistance`가 창 판단을 건너뛰고 0을 돌려준다. 거리 0이 곧 패링이라는 규칙이 `EnemyView.Resolve`에 이미 있어 **`Pattern.EnemyParry`가 자동으로 골라지고 그림에 새 코드가 0줄**이다. 덤으로 `docs/FailConverge/`의 함정(재접근이 창에 안 맞춰져 기어가는 것)을 통째로 피한다.
- **히트스톱은 `minHitStopGap`이 대부분을 버린다 — 그게 의도다**(전부 멈추면 슬라이드쇼가 된다). 새 노브를 안 만들었고, `isMainImpact: false`라 절단을 밀지도 적을 얼리지도 않는다.
- **월드 이펙트도 타격마다 뜬다**(`EffectTiming.MashHit`). **⚠ 예약할 수 없는 것과 발사할 수 없는 것은 다르다** — `PatternEffectDirector.Fire`는 큐 하나만 받는 독립 메서드(앵커·풀·포즈·배속·소리·정지 동결이 전부 그 안)라 사건에서 바로 부르면 된다. `Opponent` 앵커가 원래 발사 순간에 조회되고 `bladeT`가 칼날을 따라가며 `Sfx`도 같이 난다. **⚠ 이 값은 enum 끝에 있어야 한다**(명시 정수가 없어 서수가 곧 직렬화 키 — 중간에 끼우면 기존 패턴의 모든 큐가 밀린다). **⚠ 조건은 `Always`만 유효**(타격 순간에는 성패 미확정). **⚠ 풀 크기가 유일한 실무 함정**이다(초당 8타 × 수명 = 동시 인스턴스).
- **⚠ 절단은 자동으로 따라오지만 마무리 일격은 창을 요구한다.** `EnemyDirector`는 성패 bool 하나만 보므로 `killOnSuccess` → `KillOpponent` → 임팩트 프레임 절단이 **한 줄도 안 고치고** 성립한다. 그런데 `playerAttack`은 패턴 종류와 무관하게 임팩트에 정렬 예약되므로 **창 끝에 마무리 베기가 들어오는데**, 초과 타격이 그 클립을 매번 처음부터 끊어 **칼이 안 지나갔는데 몸이 갈라진다**. 그래서 **연타 입력은 `Deadline − Pattern.MashInputDeadlineLead`에 닫힌다**(= `playerAttack`의 와인드업). **저작 필드가 0개다** — 와인드업은 이미 그 슬롯의 트림·임팩트·배속에 있고 `ClipSequence.AuthoredImpactSpan`이 뽑는다. **슬롯을 비우면 마감 = `Deadline`이고 마무리 일격이 없다**(분기가 아니라 데이터로 갈린다). ⚠ **링 수축도 이 마감에 맞춘다** — 창 전체로 그리면 아직 줄고 있는데 입력이 안 먹어 **링이 거짓말을 한다**.
- **`MashHitInfo`가 페이로드다**(`Template`·`PointIndex`·`Scored`·`Hits`·`Target`). 구독자가 다섯이고(캐릭터·히트스톱·게이지·적·이펙트) **`Template`이 실려 있어야** 적·이펙트가 `pendingTokens.Peek()`으로 "지금 판정 대상"을 다시 유도하지 않는다(§11-1의 부류). **연출 구독자는 `Scored`를 보지 않는다** — 초과 타격에도 모션·이펙트·적 반응이 전부 나온다.
- 저작: `Tools/Pattern Chart Tool`이 `연타 N타` 배지와 **`N타 / M초(마감까지) = X타/초`**를 찍는다(⚠ 창 전체가 아니라 마감까지다). `Tools/Pattern Effect Tool`은 `MashHit` 큐를 타임라인에서 빼고 `타격마다` 배지로 표시한다.
- 상세: `docs/MashNote/`

### 3. 패턴 데이터 모델
**`Pattern`** (ScriptableObject, `PatternSpace`) — 패턴의 **'모양 원본'**. 진행 상태를 갖지 않는다.
- `PatternData[]`(각 `index` 0~8의 나열), `GetNodeType(position)`(0=Start, 마지막=End, 그 외=Progress), 그리고 **클립 슬롯들**(`PlayerAttack`·`PlayerParry`·`EnemyAttack`·`EnemyFeint`·`EnemyHit`·`EnemyParry`·`EnemyDeath` — 전부 `ClipAlignment`). 클립은 '모양에 종속된 정적 데이터'라 에셋에 두어도 원칙과 충돌하지 않는다.
- 진행 상태를 에셋에 두면 같은 템플릿을 쓰는 두 패턴이 동시에 살아 있을 때 서로의 상태를 덮어쓴다 → 그래서 상태는 `ActivePattern`으로 분리.

**`ActivePattern`** (일반 C# 클래스) — 재생 중인 패턴 하나의 **런타임 상태**.
- `Template`, `StartTime`, 노드별 입력 시각, `CurrentPosition`, `AllCorrect`, `Deadline`, `ExpectedPointIndex`, `ExpectedTime`, `LastNodeTime`.
- `StartTime`은 **큐 투입 시각(SetPattern 호출 시각)으로 고정**한다. 입력 시각이 이 시점 기준 상대시간이라, 판정 대상으로 승계될 때 다시 잡으면 타이밍이 통째로 밀린다.
- `Deadline` = 마지막 입력 시각 + `goodWindow`. 넘기면 만료 처리(미완료면 `AllCorrect=false`).

#### 패턴 겹침 규칙 (중요)
- 채보는 **다음 패턴의 노드를 이전 패턴이 끝나기 전에 스폰**해야 한다 (스폰 리드타임 = `exposureDuration`, 기본 0.5초 > 엔트리 간 입력 간격 최소 0.4초). 0.5 > 0.4 이므로 겹침은 계속 발생한다. (포커스 링 전환 전에는 리드타임이 ≈1.0초여서 겹침이 훨씬 잦았다 — 줄었을 뿐 없어지지 않았으므로 아래 큐 구조는 그대로 필요하다.)
- 반면 **입력(판정) 시각은 겹치지 않는다.** 따라서 `PatternHandler`는 여러 패턴을 **큐(`activePatterns`)**로 들고 있되(연출/스폰 대상), **판정 대상(`JudgeTarget`)은 언제나 선두 하나**다.
- `SetPattern()`은 진행 중인 패턴을 **파기하지 않고 큐에 추가**만 한다. 판정 대상은 선두 패턴이 **완료/만료될 때 같은 프레임에 즉시 승계**된다.
- 포커스 링은 패턴별 소유자(`owner`)를 추적하며, 패턴이 완료/만료되면 그 패턴의 링만 즉시 회수한다.
- 상세: `docs/PatternOverlap/`

#### 입력 계층 규칙 (중요)
- **중복 입력 방지의 진실의 원천은 `PatternHandler.connectedIndices` 하나다.** 이미 입력된 Point는 `OnPointPressed`의 가드에서 걸러진다. 이 기록은 **패턴이 끝날 때마다 클리어**되어 다음 패턴에서 같은 Point를 다시 쓸 수 있다. (예전 `Point.isBusy`는 스트로크가 끝나야 풀려 패턴 경계에서 입력을 삼켜 제거됨.)
- **`IsDragging`과 `IsMouseDragging`을 구분한다.** `IsDragging`은 키보드 스트로크 중에도 true다. `Point.OnPointerEnter`(지나가며 입력)는 반드시 **`IsMouseDragging`**을 봐야 한다(안 그러면 키보드 입력 중 마우스 호버만으로 입력됨).
- 키보드 스트로크는 패턴 완료/만료 시 종료된다. **마우스 드래그는 패턴 경계에서 끊지 않는다**(다음 패턴으로 이어 그을 수 있어야 함).
- 상세: `docs/KeyboardInputStuck/`

### 4. 포커스 링 시스템
- `FocusRingView`(IPoolable): `Pool`(PoolKey.FocusRing)로 재사용. **입력해야 할 Point 자리에 고정된 채 크기만 줄어든다** — 링이 노브와 정확히 같은 크기가 되는 순간이 입력 타이밍이고, 그때 `OnArrived`를 발행한다.
- 크기: `focusRingStartScale`배에서 시작해 `endSize`(90x90, **노브 `Visual`과 같은 값이어야 한다** — 이게 어긋나면 "딱 맞았다"는 단서 자체가 거짓이 된다)로 수축. **보간은 선형이다** — 등속이어야 남은 시간이 크기로 정직하게 읽힌다. 이징을 넣으면 타이밍 판단이 왜곡된다.
- `PatternHandler`가 스폰을 스케줄링(`scheduledSpawns`)하고 활성 링(`activeFocusRings`)을 소유자별로 추적한다.
- **링은 이동하지 않으므로 화면 기하가 개입하지 않는다.** 스폰 리드타임 = `exposureDuration` 그대로이고, 행마다 속도가 갈리지 않는다. (예전 낙하 노드는 생성 Y·화면 경계로 행별 낙하시간을 역산했고, 포인트 간격을 700으로 넓히자 행 간 배수가 2.39~7.53으로 벌어져 깨졌다. 그 역산을 감싸던 `PatternHandler.ComputeFallDuration`은 본문이 인자를 그대로 돌려주는 한 줄만 남아 제거했다 — **그래서 굽기 툴이 씬 `PatternHandler`를 요구하지 않는다**.)
- 그 결과 `exposureDuration`이 **정확히 링이 보이는 시간**이 되고, 스폰 리드타임도 정확히 그 값이다.
- 판정된 링은 즉시 회수, 미입력으로 수축을 끝낸 링은 `OnFocusRingMissedArrival` 발행 후 회수.
- **같은 Point에 링이 둘 겹칠 수 있다(정상).** 한 패턴 안에서는 불가능하지만(`Pattern.OnValidate`가 중복 인덱스를 막는다), **이전 패턴의 마지막 노드와 다음 패턴의 첫 노드가 같은 Point**면 겹친다. 겹침 길이는 `exposureDuration`(0.5) − 엔트리 간 최소 입력 간격(0.4) = **최대 0.1초**이며, 실측상 채보당 7~28쌍. 크기(72 vs 120)와 색이 달라 구분되지만 **인덱스 숫자는 완전히 겹쳐 두꺼워 보이므로 `IndexLabel`은 비활성**이다.
- 상세: `docs/FocusRing/` (구 낙하 노드 설계: `docs/FallingNode/`)

### 5. 채보 시스템 (ChartGen)
- **`SongChart`**(ScriptableObject): `song`(AudioClip), `level`, `bpm`, `beatOffset`, `entries[]`. 각 `SongChartEntry`는 `template`(Pattern), `onsetTimes`(판정 절대시각), `exposureDurations`(노출시간), `spawnTimes`(스폰 절대시각 스냅샷), `enemyCue`(전투 지시).
- **`EnemyCue`에는 `killOnSuccess`와 `projectile`만 있다.** 누가 휘두르는가(`Attacker`)와 임팩트 보정(`impactOffset`)은 **패턴이 소유한다** — 둘 다 모션의 성질이라(획 모양 → 스윙 → 역할·타이밍) 채보에 두면 같은 패턴이 엔트리마다 다른 값을 가져 어긋난 조합이 조용히 만들어진다. 여기 남은 둘은 반대로 진짜 채보 순간의 성질이다(같은 패턴이라도 이 엔트리에서만 죽이고, 이 엔트리에서만 투사체가 날아온다). `Pattern Chart Tool`은 패턴 소유 값을 **회색 읽기 전용**으로만 보여준다.
- **`SongChart.patternPool`**: 이 곡이 쓸 패턴 목록. **비우면 템플릿 폴더 전체**를 쓴다(기존 동작). 곡마다 어울리는 패턴이 다르므로 **선택은 채보의 성질**이고, 그래서 엔트리와 같은 에셋에 산다 — 다시 열어 재분석해도 같은 풀로 구워진다. 굽기 툴만 읽고 런타임은 안 읽는다.
- **`ChartPlayer`**: `GameSession.SelectedChart`(없으면 `debugChart`)를 오디오 재생 시각에 맞춰 순차적으로 `PatternHandler.SetPattern`에 흘려보낸다. `audioSource.time >= spawnTimes[0]`이 되면 해당 엔트리를 투입. 재생 가이드: `docs/!Guides/Guide_ChartPlayback.md`
- **`countdownDuration`(`[Min(3f)]`, 기본 3초)은 곡 시작 전 대기 구간**이다. 두 가지를 겸한다 — ① **프리웜**(`EnemyDirector.PrepareStage`; 곡 도중 `Instantiate`가 한 번이라도 일어나면 히치 = 판정 손실) ② **카메라 인트로 창**(§7-2). 어느 쪽으로도 짧아져 좋을 게 없어 하한을 타입으로 못박았다. 시작 순간 `OnCountdownStarted(duration)`을 발행한다(프리웜 **뒤**라 구독자는 적이 배치된 무대를 본다).
- **굽기 툴**: `Tools/Pattern Chart Tool`(`PatternChartWindow`) — 음원을 온셋 분석(`ChartGen.Core`)해 채보를 굽거나 기존 SongChart를 편집/저장. 가이드: `docs/!Guides/Guide_PatternChartTool.md`. `Core`/`Tests`는 각각 asmdef 보유.
  - **`spawnTimes`는 저장 직전에 전부 재계산한다.** 굽는 시점의 스냅샷이라 낡을 수 있고, 씬 `PatternHandler`가 없으면 null이 되어 저장이 터졌다. 재계산 후에도 null이면 **몇 번 그룹인지 찍고 저장을 중단**한다 — 조용히 잘못된 채보를 쓰는 것보다 낫다.
  - 일괄 도구: `killOnSuccess` 전부 켜기/끄기 / 매 N번째만. `killOnSuccess` 기본값은 **켜짐**.

### 6. 캐릭터 액션 (CharacterActionPlayer)
- `PatternHandler.OnPatternComplete` 구독. **완주 성공(AllCorrect)이면 패턴별 베기 클립(`Pattern.PlayerAttack`, 적이 공격자면 `PlayerParry`)** 재생. 슬롯이 비면 무연출이다 — 구 `SuccessAnimationClip` + 트림 4필드 폴백은 제거됐다(전 템플릿이 `ClipAlignment`로 이관 완료).
- **⚠ 실패는 역할로 갈린다.** 피격(Hit) 클립은 **`Attacker.Enemy`일 때만**, 그것도 첫 미스 순간이 아니라 **`impactTime`에 예약해서** 재생한다(그때 적 칼이 닿는다). **`Attacker.Player`면 피격 자체가 없다** — 적이 애초에 휘두르지 않았으므로 헛스윙으로 끝나고, `OnPlayerHit`이 안 나므로 **카메라 피격 큐(`PatternMiss`)도 체력 감소도 없다**. 그 실패의 화면상 사실은 "적이 막았다" 하나뿐이다(§11-2의 패링).
- **⚠ 두 배우를 맞추는 규칙 — 공유하는 것은 임팩트 순간 하나뿐이다.** 플레이어 공격과 적 클립(공격/패링/사망)은 길이도, 저작 배속도, 압축을 유발하는 제약도 다르다(플레이어는 다음 패턴까지의 여유, 적은 처치 확정~임팩트 간격). **배속이 같아질 이유가 없으므로 시작이나 끝을 맞추는 정렬은 원리적으로 성립하지 않는다.** 각 배우는 `Deadline + Pattern.ImpactOffset`이라는 **같은 절대 시각**에 **자기 임팩트 프레임**이 오도록 **자기 시작 시점과 자기 배속을 역산**한다(`ClipAlignment.ResolveScheduleStart` / `ResolvePlaySpeed`가 전부 `impactAlignTime`을 받는 이유). 시작·끝·배속이 서로 달라도 칼이 닿는 순간은 구조적으로 일치한다.
- **⚠ 배속은 클립 전체에 걸린다**(Animator Speed Multiplier). `ResolvePlaySpeed`는 **임팩트 이전** 구간만 보고 배속을 정하지만 그 값이 이후에도 적용된다 → **`ImpactTime`을 뒤에 찍을수록 마무리 동작까지 빨라진다.** 사망 클립에서 특히 직접적이다(§11-3).
- **정렬 앵커는 '임팩트 프레임'이다.** 칼날이 표적을 지나가는 프레임(`Pattern.AnimationImpactTime`, 클립 절대 초)이 **표적이 갈라지는 시각과 같은 식**(`Deadline + Pattern.ImpactOffset`)에 오도록 시작 시점과 배속을 역산한다 — 트림 끝을 `LastNodeTime`에 맞추던 예전 방식은 "칼은 지나갔는데 뒤늦게 갈라지는" 어긋남을 낳았다. 임팩트 **이후** 잔여 트림 구간은 같은 배속으로 이어 재생되어 마무리 동작이 뒤에 남는다. 미오서링(0 이하/범위 밖)이면 트림 끝으로 폴백. 오서링은 `Tools/Animation Clip Trimmer`(Start/**Impact**/End 세 마크). 상세: `docs/SliceImpactFrame/`
- `AnimatorOverrideController`로 단일 슬롯(`Attack`) 스테이트의 placeholder 클립을 런타임에 덮어쓴 뒤 그 스테이트를 `CrossFadeInFixedTime`으로 재생. Attack Layer는 휴지 시 웨이트 0, 재생 중 1, 종료 후 0으로 페이드.
- **겹침 방지 배속**: 다음 패턴까지의 여유(`NextLastNodeTime`)보다 클립이 길면 `AttackSpeed`로 압축하되 `maxAttackSpeed`(기본 2.5) 상한. 상한으로도 안 담기면 다음 액션 CrossFade가 현재 액션을 끊는다(의도된 동작). 상세: `docs/CharacterAction/`
- **공격 종료 후 복귀**: 트림 끝(`actionEndTime`)은 재생 끝이 아니라 **복귀 시작점**이다(클립은 계속 재생되며 마무리 동작이 이어진다). 여기서 `AttackSpeed`를 1로 되돌리고, `recoveryHoldDuration` 동안 마무리 동작을 노출한 뒤(공통) **세 경로**로 갈린다 — ① 다음 공격이 `comboLinkWindow`(1.0초) 안이면서 `minRunExposure`(0.35초)보다 촘촘히 붙으면 **웨이트 1을 유지**한 채 바로 잇고(Run·Release 생략, 깜빡임 방지), ② `comboLinkWindow` 안이되 간격에 여유가 있으면 웨이트를 0으로 내려 **그 사이 Sprint(`Sprint_HS`)를 노출**한 뒤 다음 공격에서 다시 올리며(Release 생략, 실측상 주 경로 ≈95%), ③ `comboLinkWindow` 밖(곡 공백)이면 **Release를 압축 완주**시킨 뒤 Run으로 페이드한다. **Release 진입은 코드가 유일하게 통제한다** — 애니메이터의 Attack→Release ExitTime 전이는 제거했다(과거 이 전이가 연계 중에도 Release를 새어나오게 한 버그의 원인). **base 로코모션 클립은 경로에 따라 갈린다** — 경로 ②는 Sprint, 경로 ③은 Run으로 코드가 base `Running Layer`를 CrossFade(`SwitchBaseState`). ⚠ **경로 ②의 Sprint는 '수렴 중일 때만' 참이다** — `SwitchBaseStateUnlessConverging`는 수렴 중에는 물러나므로, 이미 도착해 서 있으면(사슬처럼 이동이 0인 구간) Idle로 떨어진다. 안 그러면 제자리에서 다리만 젓는다. 상세: `docs/ReleaseRecovery/`
- **확장 포인트**: `OnSwingBegan` / `OnSwingEnded` — 스윙(베기) 트림 구간의 시작·끝. **성공 베기에서만** 발행되고(피격 클립은 제외), 트림 끝뿐 아니라 **인터럽트(연계·미스)에서도 종료가 나온다**. '칼을 휘두르는 동안'에만 붙는 연출은 이 이벤트만 구독한다.
- **칼날 트레일은 애니메이션 클립 이벤트가 켜고 끈다** — 코드가 관여하지 않는다. 이벤트를 구독해 제어하던 `WeaponTrailController`는 그 방식으로 갈아탄 뒤 어디에도 배선돼 있지 않아 삭제했다. ⚠ 그래서 지금 `OnSwingBegan`/`OnSwingEnded`는 **구독자가 없는 확장 포인트**다(발행은 계속된다 — `swingActive` 플래그가 히트스톱 가드로 쓰이므로 발행 경로 자체를 지우면 안 된다). 옛 설계: `docs/WeaponTrail/`

### 6-1. 한 패턴 안의 클립 순차 재생 (PatternClipSequence)
- **한 패턴이 클립 여러 개를 A→B→C로 이어 재생한다.** 데이터는 `Pattern.playerLeadInClips`(리스트) — **활성 슬롯(`PlayerAttack`/`PlayerParry`) '앞에' 붙는다.** 슬롯을 리스트로 갈아엎지 않은 이유는 **마지막 클립이 곧 정렬 앵커**여서다: 앵커를 제자리에 두면 기존 17개 템플릿의 **마이그레이션이 0**이고, `Tools/Mesh Slice Baker`의 칼 평면 유도(§11-3)·이펙트 프리뷰가 **한 줄도 안 바뀐다**. 비우면 예전 단일 클립 경로 그대로다.
- **리스트는 하나뿐이다** — "한 패턴은 한 역할만 갖는다"(§3)가 이미 참이라 역할별로 나누면 언제나 하나는 죽은 데이터다.
- **⚠ 정렬 앵커는 여전히 마지막 클립 하나뿐이다.** 리드인 원소는 **트림 전체**가 재생 길이이고 `ImpactTime`은 아무 일도 안 한다(찍혀 있으면 `OnValidate`가 경고한다 — 저작자가 "여기서 칼이 닿는다"로 오해할 유일한 진입점이라). 리드인 구간에서 멈추고 싶으면 그 원소의 `extraImpactTimes`에 찍는다(§7-3-1 그대로, **새 필드 0개**).
- **⚠ 배속은 원소마다 정하지 않는다 — 시퀀스 전체에 걸리는 비율 `k` 하나다.** 창이 모자라면 원소를 자르는 게 아니라 **전부 같은 비율로 빨라진다**(`ClipSequence.ResolveRatio`, `Pattern/Core`·테스트됨). 원소 i의 애니메이터 배속 = `speed_i × k`이고, 그 실시간 합이 정확히 창과 같아 **마지막 임팩트가 제시각에 도착한다**. `k`는 1 미만으로 안 내려간다(저작 배속보다 느리게 재생하지 않는 기존 규칙).
  - **⚠ 상한이 원소 수로 갈린다.** 원소가 1개면 `maxAttackSpeed` 상한을 **유지**한다(기존 템플릿의 실패 양상을 안 바꾼다). 2개 이상이면 **상한을 안 건다** — "전부 재생된다"가 이 기능의 요구라 상한과 양립하지 않는다. 대신 넘으면 경고를 찍는다.
- **⚠ 재생 헤드는 '저작 초' 하나로 일원화돼 있다**(`CharacterActionPlayer.headAuthored`/`impactAuthored`). 원래 `clipConsumed`(클립 초)에서 파생하던 `DuelCurveTime`(§11-9)·`IsBeforeImpact`(히트스톱 밀기/따라잡기)가 **원소를 가로지르는 시계**를 요구하기 때문. 환산율은 `playSpeed / playingBaseSpeed`이며 **리드인이 비면 예전 식과 대수적으로 동일**하다 — 그래서 `PlayerCombatMover`는 한 줄도 안 고쳤다.
  - **단위가 둘인 것은 소비자가 둘이라서다**: 추가 히트스톱 마크는 **그 원소의 클립 초**로 저작되고(`clipConsumed`), 거리 커브와 임팩트 판정은 **시퀀스 전체의 저작 초**를 본다. 합칠 수 없어서 나눈 것이지 시계를 늘린 게 아니다.
  - **따라잡기는 원소의 배속이 아니라 `k`를 다시 잡는다** — 남은 스팬이 원소 경계를 넘을 수 있어서, 비율을 고쳐야 뒤따르는 원소까지 함께 벌충된다.
- **⚠ `PlaySlot(continuesSequence: true)`이 건너뛰는 여섯을 하나라도 빠뜨리면 증상이 다 다르다**: 복귀 스케줄(**시퀀스가 1타 만에 끝난다**) · 레이어 웨이트 블렌드(매 타 깜빡임) · Release 래치(중간에 Release 누출 — 과거 그 버그가 있었다) · `hitStopped` 리셋(**전환이 진행 중인 정지를 삼킨다**) · 스윙 재발행(`swingActive`가 잠깐 false → **그 순간 `ApplyHitStop`이 통째로 무시된다**) · 헤드 리셋(거리 커브 원점이 원소마다 다시 잡힘). **`useSlotA` 핑퐁만은 그대로 둔다** — A→B→A가 정확히 원하는 동작이다.
- **`inLeadIn` 게이트가 `Update`의 복귀 로직을 통째로 막는다.** 마지막 클립이 시작되는 순간 게이트가 내려가고 **예전 경로가 그대로 주인**이 된다 — 즉 이 기능은 "리드인 모드"이고 정렬·복귀·히트스톱은 손대지 않았다.
- **⚠ 시퀀스를 끊는 곳은 전부 `PlaySlot(continuesSequence: false)` 하나로 모인다**(다음 패턴·피격·일회성). 첫 미스만 예외로 따로 처리한다 — 지금 원소는 끝까지 재생되고 거기서 복귀한다(단일 클립에서 진행 중이던 베기가 취소되지 않는 것과 같은 결).
- **정지 예산은 리드인 원소의 칼질까지 합산한다**(§7-3-1) — 정지가 시퀀스 어디서 나든 그만큼 재생이 안 흐른다.
- **⚠ 창 예산이 실질 상한이다.** `Dreamer_Lv10` 실측 가용 창(`impact − FirstNodeTime`)이 p50 **0.90초** · max **1.70초** · **2.0초 이상이 0%**다. 3클립이면 클립당 평균 0.30초 — **긴 모션 여러 개는 성립하지 않는다.** 해결은 배속이 아니라 **채보의 노드 간격을 늘리는 것**이며, 그래서 툴은 총 저작 시간을 **숫자로만** 보여 주고 금지하지 않는다.
- 저작: `Tools/Animation Clip Trimmer`의 **플레이어 슬롯 팝업**(마지막/리드인 [i] — 적 보조 슬롯과 같은 관용구) + 추가·삭제·순서 이동 + `시퀀스 N개 · 저작 N.NN s` 표시. 리드인 원소는 **Impact 필드가 없고** 트림 끝이 타임라인의 `t = AuthoredEndOffset` 지점에 붙어 원소들이 이어져 보인다.
- 상세: `docs/PatternClipSequence/`

### 7-1. 카메라 연출 (Camera)
- **`CameraDirector`**: 카메라 연출의 유일 관리 지점. `PatternHandler`의 기존 이벤트만 구독하는 **순수 연출**(판정에 개입하지 않음). `EffectManager`와 같은 위치·같은 카탈로그 관례 — **연출 추가 = 카탈로그에 한 줄**.
- **`CameraCueCatalog`**: `CameraTrigger` enum(PatternSuccess/PatternMiss/PatternFailure) + `CameraCueEntry`(진폭·지속). 감쇠 곡선은 공식 `(1-t)²`, 주파수는 director 공용 값 하나 — 큐마다 나눌 만한 차이가 안 난다.
- **큐 시각은 화면에서 사건이 일어나는 순간에 맞춘다**: 성공/실패(표적 파괴)는 **`info.ImpactTime()`**(= `Deadline + Pattern.ImpactOffset`, §6·§11과 **같은 확장 메서드**), 피격은 `CharacterActionPlayer.OnPlayerHit`(= **적 칼이 닿는 `impactTime`**). **실패는 사건이 둘이라 큐도 둘이다.** ⚠ 단 `Attacker.Player` 실패는 안 맞으므로 `PatternFailure` 하나만 난다.
- **예약은 최대 하나**다 — 패턴 완료가 순차적이고 A의 Deadline(A 마지막노드 +0.1초)보다 B의 완료가 최소 0.4초 뒤라, 리스트가 필요 없다.
- **Perlin은 채널이 하나라 겹침이 합성되지 않는다.** 마지막 노드 미스면 두 큐가 0.1초 간격으로 확실히 붙으므로, 새 쉐이크는 타이머를 재시작하되 **진폭은 큰 쪽을 취한다**(덮어쓰면 세기가 뚝 떨어짐). **휴지값은 0이 아니라 씬의 현재 값**(`AmplitudeGain` 0.1)이라 `Awake`에서 캐시해 그리로 복귀한다.
- 상세: `docs/CameraDirection/`

### 7-2. 카메라 프레이밍 · 인트로 (CameraDirector)
`CameraDirector`가 하는 일은 **셋**이고 서로 다른 층에 산다 — **쉐이크**(노이즈 채널) / **프레이밍**(무엇을 담을지) / **인트로**(어느 vcam을 쓸지). 채널이 겹치지 않아 동시에 돌아도 간섭하지 않는다. **Cinemachine 타입은 `ApplyShake`·`ApplyFraming`·`IntroRoutine` 세 이음매에만 등장한다.** **세 기능은 각자 독립적으로 꺼진다** — 배선이 빠진 기능만 조용히 비활성된다.

**프레이밍** — 교전 상대가 있으면 플레이어와 함께, 없으면 플레이어만. **그리고 카메라는 언제나 플레이어 등 뒤에 선다.**
- 씬 구성: `CameraTargetGroup`(`CinemachineTargetGroup`)이 게임플레이 vcam의 `TrackingTarget`, vcam에 `CinemachineGroupFraming`(`SizeAdjustment = DollyOnly` — Zoom이면 원근이 왜곡된다).
- **그룹은 `RotationMode = Manual`, vcam은 `BindingMode = LockToTarget`.** 이 조합이라 `FollowOffset`이 **그룹 오브젝트의 로컬 축**으로 해석되고, `CameraDirector`가 그룹 회전을 **플레이어 yaw로 몰아** 카메라 궤도가 등 뒤로 따라 돈다. ⚠ `GroupAverage`로 바꾸면 회전이 **멤버 배치에서 파생**돼 구도가 적을 따라 돌고, 상대 가중치 0 구간에서는 정의되지 않아 튄다. (예전 문서의 "반드시 `WorldSpace`" 규칙은 `GroupAverage`를 전제한 오진이라 폐기됐다 — 씬은 처음부터 `Manual`이었고 그래서 증상이 없었다.)
- **⚠ 카메라는 플레이어의 회전 속도를 복사하지 않는다.** `PlayerCombatMover.turnDuration`은 0.15초 — 캐릭터에는 옳지만 카메라가 따라 하면 상대가 무대 반대편으로 바뀔 때 **화면이 0.15초에 반 바퀴 돈다.** `CameraDirector.cameraTurnDamping`(0.45초)으로 `SmoothDampAngle`(각도 랩어라운드 + 속도 승계) 뒤따르고, **그 지연 자체가 "플레이어가 먼저 돌고 카메라가 따라붙는" 연출**이다.
- 멤버는 **고정 2칸**(0=플레이어 w=1, 1=상대 w=0~1). 넣었다 뺐다 하면 바운드가 계단식으로 튄다.
- 그룹 위치는 `GroupCenter`(가중 중심)라 카메라는 엄밀히는 "플레이어 뒤"가 아니라 **"교전 중심의 뒤"**다. 결투 간격이 1m 남짓이라 차이는 0.5m 미만이고, 오히려 상대가 화면 중앙에 잘 남는다.
- **⚠ 높이 노브가 둘이고 값이 같아야 한다.** 캐릭터 root가 **발바닥**(y=0, 머리끝 1.69)이라 그룹 바운드 중심도 발이다. `RotationComposer.TargetOffset.y`(어디를 **조준**하나)만 올리면 `GroupFraming`이 여전히 **바운드**(=발)를 화면 중앙에 놓아 둘이 싸우고 화면이 발쪽으로 끌려간다 — `GroupFraming`은 조준점을 모른다. **`GroupFraming.CenterOffset.y`를 같은 값으로 맞춘다**(현재 둘 다 1.5 = 머리 높이). 발이 화면 아래로 잘리면 둘을 같이 낮춘다.
- **"적이 있다/없다"는 이진값이 아니라 거리의 함수다** — `fullFrameDistance`(3m) 이하면 w=1, `dropoffDistance`(6m) 이상이면 w=0. 처치 즉시 승격된 적은 아직 링(6m)에 있어서, 이진값이면 카메라가 확 물러났다 다시 붙는다.
- **⚠ 대상을 갈아끼울 때 가중치를 0으로 리셋한다.** 가중치는 연속이지만 **대상 위치는 순간이동**한다(1m의 A → 6m의 B). 이월하면 그 감쇠 시간 동안 그룹이 6m 밖 한 점을 무겁게 껴안아 **카메라가 바깥으로 튄다.**

**인트로** — 곡 시작 전 로우앵글부터 훑고 올라가 게임플레이 구도로 고정.
- **두 도막**: ① `CinemachineSplineDolly` 주행(씬의 `IntroSpline`) ② `CinemachineBrain` 블렌드. 시각은 `ChartPlayer.OnCountdownStarted`에서 온다.
- **경로는 코드가 모른다.** 좌표·높이·곡률 전부 씬의 `SplineContainer`에 있고 코드는 `CameraPosition` 0→1만 민다. **`PositionUnits = Normalized`가 그 전제**라 어긋나면 경고를 찍는다(경로가 조용히 일부만 재생됨). 코드가 주는 건 속도 배분(`introEase`)뿐.
- **블렌드 시간은 `Brain.DefaultBlend.BlendTime`에서 읽는다**(`Time`이 아니라 — `Cut`이면 0을 돌려주는 실효값). 인스펙터에 두 번 적으면 *곡은 시작됐는데 카메라가 아직 움직이는* 상태가 된다.
- **끝점을 게임플레이 구도에 정확히 맞출 의무가 없다** — 차이는 블렌드가 흡수한다.
- 인트로 vcam은 TargetGroup을 안 쓰고(그래서 등 뒤 추적과도 무관하다) 플레이어를 `LookAt`으로 직접 본다(주인공이 플레이어고, 그룹을 공유하면 두 기능이 한 값으로 얽힌다). 노이즈도 안 붙인다. **휴지 우선순위는 −10** — 0이면 게임플레이 vcam과 동점이라 끝난 뒤 승자가 활성화 순서에 달린다.
- 상세: `docs/CameraFraming/` · 등 뒤 추적: `docs/CameraOverShoulder/`

### 7-3. 히트스톱 (HitStop)
- **`HitStopDirector`**: 히트스톱의 유일 관리 지점. `OnPatternComplete`만 구독하는 순수 연출. **성공(`AllCorrect`)에서만** `Deadline + ImpactOffset`(§6·§11과 같은 식)에 예약하고, 그 순간 두 배우에게 "멈춰라"를 지시한다. 예약은 최대 하나(`CameraDirector`와 같은 근거).
- **⚠ `Time.timeScale`은 이 게임에서 절대 못 쓴다.** 판정·클립 정렬·표적 운동은 전부 `Time.time`인데 채보는 `audioSource.time`으로 돈다 — **오디오는 timeScale의 지배를 받지 않는다.** 시계를 내리면 게임 시계만 느려져 차이가 **영구 누적**되고, `perfectWindow`가 0.05초인데 통상 히트스톱이 0.05~0.10초라 **한 번으로 판정이 무너진다.** 그래서 멈추는 것은 **Animator의 Speed Multiplier**(`AttackSpeed`/`DeathSpeed`) 둘뿐이다. 판정·오디오·포커스 링·이동은 계속 흐른다.
- **두 배우 모두 '밀기'다**(캐치업 아님). 임팩트 시점에 정지 창 안으로 흡수할 잔여가 양쪽 다 없기 때문.
  - **플레이어**(`actionEndTime`·`recoveryEndTime += D`): 공격 클립은 임팩트가 트림 **끝** 근처라 임팩트 시점 잔여가 실측 **0.036~0.109초(10/10 패턴)** — 정지가 그보다 길어 압축할 시간이 음수다. 클립은 멈춘 자리에서 원래 배속으로 이어진다. **임팩트는 이미 지나갔으므로 §6 정렬은 안 깨진다** — 미는 것은 마무리 동작과 복귀뿐이고, 다음 공격은 자기 Deadline에서 독립 예약이라 제시각에 시작한다(줄어드는 건 Sprint 노출 시간뿐).
  - **적**(`burstTime += D`): 절단이 **임팩트 바로 그 순간**이라(§11-3) 잔여가 **0**이다 — 안 밀면 멈춘 프레임에 몸이 갈라져 정지가 안 보인다. 사망 클립은 `DeathSpeed = 0`으로 얼렸다가 해제 시 원래 배속으로 이어진다.
  - **⚠ `EnemyDirector.TickPendingKills`는 `LateUpdate`에 있다.** 절단 시각과 히트스톱 발사 시각이 **같은 임팩트 프레임**이라, 둘 다 `Update`에 있으면 스크립트 실행 순서에 따라 정지가 걸리기 전에 몸이 갈라진다. `HitStopDirector.Fire`(Update)가 `burstTime`을 민 뒤에 보게 만드는 순서 보장.
  - **한쪽만 멈춰도 타격감은 성립한다.** 사망 클립이 없는 패턴은 적이 빠지고 플레이어만 멈춘다.
- **연출 토글**: `HitStopDirector.hitStopEnabled`, `CameraDirector`의 `shakeEnabled`/`punchEnabled`/`framingEnabledOption`/`introEnabled`. 전부 인스펙터 bool이며 끄면 해당 층만 죽고 나머지는 그대로 돈다(배선 누락 시 조용히 비활성되는 기존 규율과 같은 결).
- **`EnemyView.ApplyHitStop`은 새 `burstTime`을 반환하고 `EnemyDirector`가 그걸로 `PendingKill.burstTime`을 갱신한다.** 배속은 뷰가, 시각은 디렉터가 드는 구조라 **갱신을 빠뜨리면 클립이 도는 중에 먼저 갈라진다.**
- **⚠ 카메라는 정지 창 동안 완전히 언다**(`CameraDirector.HoldForHitStop`). 순서가 요구 그 자체다 — ① 진행 중인 쉐이크를 즉시 끄고 ② 잠그고 ③ **해제된 다음에** 큐가 나간다. 멈추는 순간 화면이 흔들리면 "멈췄다"가 아니라 "끊겼다"로 읽힌다.
  - **잠금 = `CinemachineBrain.enabled = false`.** 그래야 카메라 Transform이 마지막 값에 굳는다 — 프레이밍 갱신만 멈추면 감쇠(`PositionDamping` 1.0)가 남은 오차를 계속 따라가 여전히 흐른다. Brain이 없으면 프레이밍·쉐이크 정지만으로 **부분 잠금**이 된다.
  - **큐는 버리지 않고 미룬다.** `CameraDirector`와 `HitStopDirector`의 `Update` 실행 순서는 보장되지 않아 큐가 먼저 시작됐을 수 있다 — 잠금 진입 시 진행 중인 큐가 있으면 그 트리거를 지연 큐로 옮기므로 **어느 순서로 돌든 결과가 같다**.
  - ⚠ `OnDisable`에서 반드시 Brain을 되살린다. 잠금 도중 꺼지면 **카메라가 영구히 굳는다**.
  - `HitStopDirector`는 창을 **알려 줄** 뿐 카메라를 직접 안 만진다 — Cinemachine 호출은 전부 `CameraDirector` 안에 남는다.
- **카메라 펀치는 `CameraDirector`가 낸다**(`CameraCueEntry.punchFovDelta`/`punchDuration`, "연출 추가 = 카탈로그 한 줄"). `HitStopDirector`는 카메라를 안 만지고, `CameraDirector`는 애니메이터를 안 만진다 — 각자 자기 층. 펀치는 **`Lens.FieldOfView`에만** 건다(돌리에 걸면 매 프레임 도는 `GroupFraming`과 싸운다). `DollyOnly`의 "Zoom 금지"는 상시 프레이밍 경고지 0.1초 전환 얘기가 아니다.
- **월드 이펙트도 같은 창만큼 언다**(§7-4). `PatternEffectDirector.ApplyHitStop`이 활성 뷰의 `simulationSpeed`를 0으로 내렸다가 각 큐의 `speed`로 되돌린다 — **복귀값을 뷰가 드는 이유**는 큐마다 배속이 다르기 때문. 캐릭터가 멈췄는데 스파크만 흐르면 "멈췄다"가 아니라 "캐릭터만 렉 걸렸다"로 읽힌다. 여기서도 `HitStopDirector`는 파티클을 직접 안 만진다.
- **⚠ 남은 이음매**: `SlicePiece`(표적 조각)는 닫힌 식이라 안 멈춘다. 표적 절단이 임팩트 바로 그 순간이라 캐릭터가 멈춘 동안 조각만 날아간다. 0.08초라 안 보인다고 보고 뺐다 — 보이면 그때 붙인다. 적 사망 폭발(`burstTime`)은 정지 창 **끝**으로 밀리므로 조각이 정지 중에 날아갈 일은 없다.
- 상세: `docs/HitStop/`

### 7-3-1. 한 클립 안의 다중 히트스톱 (MultiHitStop)
- **여러 번 베는 클립은 칼질마다 멈춘다.** 저작 모델은 하나다 — **`ClipAlignment.ImpactTime`은 언제나 '마지막 베기'**에 찍고(그게 절단·사망·카메라의 정렬 앵커), 그 **이전**의 칼질들을 `extraImpactTimes`(클립 절대 초)에 찍는다. 마지막 베기 이후의 마크는 런타임·툴 양쪽에서 버린다 — 남기면 **몸이 갈라진 뒤에 화면이 멈춘다**.
- **⚠ 정지 시간은 어디선가 나와야 한다.** 정지 N회 × `hitStopDuration` = **F** 동안 클립이 안 흐르는데 마지막 베기는 여전히 `Deadline + ImpactOffset`에 도착해야 한다. 방법 둘을 **같이** 쓴다:
  - **예산(주 경로)** — 재생을 **F만큼 일찍 시작**한다(`pendingScheduleStart`·시작 배속 양쪽에서 뺀다). 배속을 안 건드려 모션이 안 뭉개진다.
  - **따라잡기(보정)** — 정지 해제마다 `남은 임팩트 스팬 ÷ 남은 실시간`으로 배속 재계산. 스톱이 버려지거나 창이 모자라도 **자기 수정**된다.
- **⚠ 임팩트 이전 정지는 복귀 스케줄을 밀지 않는다.** §7-3의 '밀기'는 임팩트가 이미 지나갔을 때만 옳다 — 이전에 밀면 마지막 베기가 절단보다 늦는다. `CharacterActionPlayer.IsBeforeImpact()`가 두 경로를 가른다.
- **⚠ 추가 스톱은 적을 얼리지 않는다.** 적의 공격·사망 클립도 **같은 `impactAlignTime`에 정렬**돼 있는데(§6·§11-3) 적에게는 예산도 따라잡기도 없다 — 함께 얼리면 적 쪽 정렬만 밀린다. 마지막 베기(메인)에서는 예전처럼 전원 정지한다. **카메라·파티클은 추가 스톱에서도 얼린다**(정렬을 안 들고 있고, 캐릭터만 멈추면 "렉"으로 읽힌다).
- **⚠ 절단을 미는 것은 메인 하나뿐이다** — `EnemyView.ApplyHitStop(..., pushBurst)`. 스톱마다 밀면 3회에 0.3초, **몸이 갈라지는 순간이 마지막 칼질보다 한참 뒤**가 된다.
- **칼질 시각을 아는 것은 배우뿐이다.** 클립 초를 월드 시각으로 바꾸려면 그때의 실제 배속이 필요하다 → `CharacterActionPlayer.OnExtraImpact(float)`가 알리고 `HitStopDirector`가 예약한다. **한 번에 하나씩** 발행한다 — 미리 다 발행하면 첫 정지 시간만큼 나머지가 이르게 터진다. "얼마나 · 누구를 멈출까"는 여전히 디렉터 하나가 정한다.
- **`HitStopDirector.HitStopDuration`은 꺼져 있으면 0을 돌려준다** — 그래야 "예산만큼 일찍 시작했는데 안 멈추는" 어긋남이 원천 소멸한다. 예약은 이제 **리스트**다(한 패턴이 스톱을 N개 낸다 — "최대 하나"의 전제가 바뀌었다). 너무 촘촘한 예약은 `minHitStopGap`으로 **버린다**(연장하면 "여러 번 끊김"이 아니라 "한 번 길게 멈춤"이 되어 목적과 반대다).
- 저작: `Tools/Animation Clip Trimmer`의 `+ Mark Extra` · 타임라인 `◆ N타` 배지 · **정지 예산 표시**(F > 0.3초면 경고 — 엔트리 간 최소 간격이 0.4초라 그 위로는 대부분의 채보에서 창을 넘긴다).
- 상세: `docs/MultiHitStop/`

### 7-4. 패턴별 월드 이펙트 (PatternEffect)
- **`Pattern.effectCues`(리스트)가 소유한다.** 큐 하나가 "**언제 · 어디에 · 어떤 조건에서** 무엇을 재생할지"를 스스로 든다(`PatternEffectCue`, `Pattern/Core`). **슬롯이 아니라 리스트인 이유**: 개수와 시점이 코드가 아니라 저장 단계에서 정해진다 — `ClipAlignment` 슬롯들과 성질이 다르다(클립은 배우당 하나씩 재생되지만 이펙트는 동시에 여럿 뜬다).
- **조건은 판정 결과가 아니라 적의 반응 클립을 따라간다**(`Always`/`Success`/`Parry`/`Evade`). 막는 모션이면 스파크가 튀고 뒷구르기면 아무것도 안 튄다 — **칼이 만났느냐**가 화면에 남는 사실이기 때문. **네 값이 다섯 경우를 덮는다**: `attacker`가 패턴의 성질이라 같은 값이 역할에 따라 다른 의미를 가져도 한 에셋 안에서 섞이지 않는다(`Attacker.Player`의 `Success`는 베는 이펙트, `Attacker.Enemy`의 그것은 받아친 스파크, `Evade`는 피격).
- **"뒷구르기는 무연출"은 큐를 안 만드는 것**이지 코드 분기가 아니다.
- **⚠ 패링/회피는 기존 이벤트로 알 수 없다** — `EnemyDirector.OnEnemyReacted(Pattern, EnemyReaction, impactTime)`가 그것만을 위해 있다. 반응 판정은 `EnemyView.Resolve`의 `parried` 식과 **같은 `retreat` 값**을 보고, 확정이 아니라 **임팩트 시각**을 실어 보낸다(`OnEnemyKilled`/`OnEnemyBurst`가 갈린 것과 같은 이유). 실패를 한 덩어리로 보면 **적이 구르는 동안 허공에서 스파크가 튄다**.
- **시각 기준점 다섯**(`PatternStart`/`FirstNode`/`Node[i]`/`LastNode`/`Impact`) ± `timeOffset`. 전부 `PatternQueuedInfo`에서 나오며(`NodeTimes` 포함) **새 시계를 만들지 않는다**. 그래서 `PatternEffectDirector`는 **`OnPatternQueued` 하나만 구독해 예약을 다 만들고**, 조건만 나중에 채운다(성패는 마지막 노드에서, 반응은 그 직후에 정해지므로 **예약 시점과 조건 확정 시점이 구조적으로 다르다**).
- **⚠ `HitStopDirector`의 "예약 최대 하나"를 쓸 수 없다** — 그 근거는 시각이 임팩트 고정이라는 것인데, 큐는 `PatternStart`까지 앞당겨져 **앞 패턴의 임팩트 큐와 다음 패턴의 시작 큐가 겹친다**(리드타임 0.5 > 간격 0.4).
- **⚠ 결과 조건 큐는 `LastNode`보다 이른 시각에 걸 수 없다**(미래를 앞당겨 보여 주는 셈). 런타임은 조용히 폐기하고 `OnValidate`·툴 타임라인이 잡는다.
- **앵커 넷**(`ImpactAnchor`/`Player`/`PlayerWeapon`/`Opponent`) + `follow`(자식으로 붙어 따라감). `Opponent`는 **발사 순간에** 조회한다(§11-1 — 큐 시점엔 미배정). **`PlayerWeapon`은 안쪽 칼날 노드**를 배선한다(동명 2단 — 트레일 클립 이벤트가 붙은 그 노드다).
- **칼날은 점이 아니라 선분이다** — `bladeT`(0=손잡이, 1=칼끝)로 비율로 집는다. **축은 추측하지 않고 유도한다**: `BladePath`가 칼 렌더러 `localBounds`의 **최장 축 = 날 길이** 규칙을 쓰며(`MeshSliceBakerWindow.SampleWeapon`과 동일), 런타임과 저장 툴이 **같은 클래스**를 공유한다. 계산은 스폰 때 한 번이고 이후 추종은 부모 관계가 공짜로 한다.
- **⚠ 패턴인풋 노드(Point) 자리는 앵커가 아니다** — 루트 Canvas가 `ScreenSpaceOverlay`라 그 좌표는 월드가 아니다(§7-5). 노드 자리 이펙트는 기존 `EffectManager` 관할이며 이 시스템은 침범하지 않는다.
- **⚠ `NodeTimes`는 '예정'이지 '실제'가 아니다.** 늦게 눌러도 안 밀린다. 입력 순간에 정확히 붙는 연출은 판정 이벤트를 쓴다.
- 뷰는 `CanvasEffectView`를 **그대로 재사용**한다(월드 경로 `SetWorldPose`/`SetSpeed` 추가). 이름이 Canvas인 채 남은 것은 **프리팹 스크립트 참조가 클래스명에 묶여** rename이 기존 이펙트를 통째로 깨기 때문. ⚠ `MainModule`은 구조체 사본이라 **되대입해야** 배속이 반영되고, 풀 재사용이라 **반납 시 부모·크기·배속을 원복**해야 한다(안 하면 따라가기 이펙트가 앵커와 함께 파괴되고 다음 큐가 이전 배속을 물려받는다).
- **큐는 그림뿐 아니라 소리도 든다**(`PatternEffectCue.Sfx`). 패턴마다 다른 임팩트음이 필요한데 `SfxTrigger` enum은 **키를 코드가 정해** 패턴 수만큼 못 늘린다(`SliceSet`이 enum 카탈로그를 안 두는 것과 같은 지점). 큐에 얹으면 **시각·조건·개수·툴 타임라인이 전부 공짜로 따라온다** — 새 시계도 새 저작 화면도 없다. 조건이 소리에 더 직접적이다: `Success`(Player) 촥 / `Success`(Enemy)·`Parry` 챙 / `Evade`는 **큐를 안 만들어** 무음.
  - **`IsUsable`이 `prefab != null || sfx != null`이다.** 이 값을 보는 곳이 예약·프리웜·툴 목록·툴 경고 **전부**라 여기만 넓히면 나머지가 따라온다. ⚠ 그래서 **`cue.Prefab`을 역참조하는 모든 지점에 null 가드가 필요하다**(프리웜·`OnValidate` 경고·툴 프리뷰 — 이번 확장의 유일한 회귀 지점).
  - **⚠ `Fire`는 소리가 먼저다.** 소리는 2D라 앵커가 필요 없는데 앵커 가드가 앞에 있으면 소리 전용 큐가 통째로 죽는다.
  - **⚠ 히트스톱은 소리를 안 얼린다.** 파티클은 `simulationSpeed = 0`으로 얼지만 오디오는 원래 `timeScale` 밖이고(§7-3), 타격감으로도 **멈추는 그 순간**에 울리는 것이 맞다 — 재생 중 끊으면 "정지"가 아니라 "소리가 끊겼다"로 읽힌다.
  - **⚠ 소리 큐가 하나라도 있으면 §7의 공용 `SfxTrigger.PatternImpact` 폴백이 물러난다**(`Pattern.HasSfxCue`). 안 그러면 둘 다 임팩트 시각이라 한 소리로 뭉쳐 들리고 저작자가 원인을 못 짚는다. 타이밍이 `Impact`인지까지는 안 본다 — **"소리를 직접 설계한 패턴"이라는 사실 하나**로 가르는 편이 규칙으로 단순하다.
  - **툴 프리뷰는 소리를 내지 않는다** — `ParticleSystem.Simulate`는 되감기가 되지만 오디오는 안 된다. 스크럽마다 소리가 튀면 저작을 방해한다(타임라인 막대로 시각만 보여 준다).
- **저장**: `Tools/Pattern Effect Tool` — 패턴 진행 전체 타임라인 + 애니메이션·파티클 동시 프리뷰(`ParticleSystem.Simulate`라 **되감기가 된다**) + 칼날 경로 폴리라인(우클릭으로 그 프레임 시각을 `timeOffset`에 집기). 가이드: `docs/!Guides/Guide_PatternEffectTool.md` / 상세: `docs/PatternEffect/`

### 7-5. 앵글 교체 (CameraAngleSwitcher)
- **`CameraAngleSwitcher`**(`CameraDirector`가 소유하는 `[Serializable]` 헬퍼): 앵글 vcam 여러 대를 랜덤 교체한다. 우선순위만 갈아끼우고 실제 이동은 Cinemachine 블렌드가 한다.
- **이 게임이 카메라를 마음껏 바꿔도 되는 구조적 이유**: 루트 Canvas가 **`ScreenSpaceOverlay`**라 패턴인풋·포커스 링·가이드라인이 카메라와 완전히 독립이다. **앵글이 바뀌어도 플레이어가 봐야 할 것은 1픽셀도 안 움직인다.**
- **⚠ 교체는 세 단계다 — 쿨다운(자격) → 패턴 경계(**예약**) → 카메라 큐 종료(**발사**).** 경계에서 바로 교체하면 안 된다: **패턴 승계(`OnJudgeTargetBegan`)는 임팩트보다 먼저 온다**(완료 = 마지막 노드 입력, 임팩트 = 거기서 `goodWindow` 0.1초 뒤). 그래서 경계에서 쏘면 0.4초 블렌드가 임팩트·히트스톱·쉐이크를 **매번** 덮는다 — 확률 문제가 아니라 구조적으로 고정된 거리다. 큐가 끝난 뒤(마지막 노드 +0.38초쯤) 발사하면 블렌드가 조용한 구간에서 돌고, 예약이 그 패턴에 묶여 있어 음악적 착지점은 유지된다.
- **앵글 = `FollowOffset` 벡터 하나.** vcam이 `LockToTarget`이고 그룹이 `RotationMode = Manual`이라 오프셋이 그룹 로컬 축으로 해석되고, `CameraDirector`가 그 그룹을 플레이어 yaw로 몬다(§7-2) → **오프셋만 바꾸면 자동으로 플레이어 기준 앵글**이다. 기존 vcam을 복제하고 벡터만 바꾸면 `TargetGroup`·`RotationComposer`·`GroupFraming`을 물려받아 "적과 플레이어를 따라다닌다"가 공짜로 성립한다. **스위처는 앵글이 무엇인지 모른다.**
- **시작 카메라는 `cameras[0]` 고정**(랜덤 아님). 곡 시작 구도가 매번 같아야 인트로 스플라인 끝점이 어느 구도로 흡수될지 정해지고(§7-2), 리스트 순서가 곧 저작 의도가 된다. `Setup()`이 씬 저장값을 덮고 쿨다운도 여기서 시작한다(시작 직후 즉시 교체 방지).
- **블렌드는 `BlendHint = SphericalPosition`** — 위치가 LookAt(= `CameraTargetGroup`) 중심의 **구면 위**를 지나 **교전을 축으로 돌아간다**. 기본 직선 보간이면 반대편 앵글로 갈 때 카메라가 무대를 가로질러 대상을 뚫고 지나간다. ⚠ **LookAt이 없으면 조용히 직선으로 떨어진다.** 높이 차가 큰 쌍은 `CylindricalPosition`이 나을 수 있다(씬 값).
- **⚠ 쉐이크·펀치는 `brain.ActiveVirtualCamera`(live vcam)에 건다.** 특정 vcam을 하드와이어로 잡으면 **다른 앵글이 올라온 순간 타격감 연출이 통째로 사라진다**(화면에 없는 카메라를 흔들게 된다). 휴지값(Perlin 진폭·주파수, 렌즈 FOV)은 **vcam마다 다를 수 있어 대상별로 캐시**하고, 대상이 바뀌면 **이전 vcam을 반드시 휴지값으로 되돌린다**(안 그러면 흔들린 상태로 굳어 다음 등장 때 그 값으로 나온다). Brain/live 조회 실패 시 `gameplayCamera`로 폴백 — 교체를 안 쓰는 씬은 예전과 동일.
- **활성 우선순위(10)는 인트로(20)보다 낮아야 한다.** 아니면 인트로를 이겨 곡 시작 연출이 안 나온다.
- `Brain.DefaultBlend`는 **EaseOut 0.4초**. 패턴 주기가 1.38초라 1.0초면 73%를 이동에 쓴다. 인트로가 이 값을 읽지만(§7-2) 줄이면 주행 시간이 늘어 무해하다.
- 상세: `docs/CameraSwitch/`

### 7. 이펙트 시스템 (Effect)
- **`EffectManager`**: Canvas 이펙트의 유일 관리 지점. `PatternHandler`의 확장 이벤트(판정/라인연결/패턴완성)만 구독해 카탈로그에서 프리팹을 골라 재생. **PatternHandler는 이펙트를 위해 수정하지 않는다(관심사 분리).**
- **`EffectCatalog`**: `EffectTrigger` enum(Perfect/Good/Miss/PatternCompleteFull/PatternComplete/NodeConnected) + `EffectEntry`(트리거→프리팹+풀 크기). **이펙트 추가 = 카탈로그에 한 줄 추가**(코드 수정 없음). 프리팹 비면 무연출.
- **효과음도 여기서 낸다** — `SoundManager`는 게임플레이를 모르고(어느 씬에도 놓이는 싱글톤) `Play(SfxTrigger)` 요청만 받는다. 판정음(`Perfect`/`Good`/`Miss`)은 `OnFocusRingResolved`에서 즉시, **`PatternImpact`는 `Deadline + ImpactOffset`에 예약해서** 낸다(§6·§11과 같은 `info.ImpactTime()`).
  - **⚠ 패턴 완료 순간에 울리면 안 된다.** 완료 = 마지막 노드 입력이고 칼이 닿는 것은 거기서 `goodWindow`(0.1초) + 보정만큼 뒤다. **예약은 최대 하나**(§7-1과 같은 근거), `OnDisable`에서 회수한다.
  - **⚠ 히트스톱과 시계가 안 어긋난다.** 예약이 `Time.time`이고 히트스톱은 `timeScale`을 안 쓰므로(§7-3) 정지 창 안에서도 제때 울린다 — 타격감 관점에서도 **멈추는 그 순간**에 나는 것이 맞다.
  - **⚠ 소리 층은 소리 층이 든다.** `HitStopDirector`가 이미 같은 시각에 예약하고 있어 거기 한 줄이 더 짧지만, §7-3의 층 분리("`HitStopDirector`는 카메라를 안 만지고 `CameraDirector`는 애니메이터를 안 만진다")를 깬다.
  - **⚠ `PatternImpact`는 폴백이다.** 패턴이 자기 소리를 들고 있으면(§7-4의 `Pattern.HasSfxCue`) 이 공용음은 예약조차 안 한다 — 겹치면 한 소리로 뭉쳐 들려 원인을 못 짚는다. **패턴별 소리는 `SfxTrigger`가 아니라 이펙트 큐로 낸다**(enum 키는 코드가 정해 패턴 수만큼 못 늘린다).
- 프리팹별 자체 풀 큐로 관리. 배경 앰비언트는 상시 루프 인스턴스로 배치하고 `SetIntensity`로 강도 조절. 상세: `docs/CanvasEffect/`

### 7-6. 목숨 (PlayerHealth)
- **`PlayerHealth`**(플레이어 프리팹): `CharacterActionPlayer.OnPlayerHit`만 구독한다. 그 이벤트가 **적 칼이 실제로 닿는 시각**에만, 그것도 `Attacker.Enemy` 패턴에서만 나오므로 "적 공격을 못 막았을 때만 깎인다"는 규칙이 이벤트 하나로 이미 표현돼 있다(§6).
- **⚠ `OnDepleted`는 아직 구독자가 없다** — 목숨이 0이 돼도 화면에서 아무 일도 일어나지 않는다. 버그가 아니라 **사망 연출이 미구현**인 것이며, 그 연출이 붙을 진입점이 이 이벤트다. `OnDamaged`(남은 수치)도 UI가 붙기 전까지 같은 상태다. (점수·콤보 쪽 HUD는 `ScoreHudView`로 이미 붙어 있다 — §12.)

### 8. 디버그 입력 (에디터 전용)
- `PatternHandler`의 `#if UNITY_EDITOR` 블록 + `PatternHandlerEditor` 커스텀 인스펙터. **빌드에는 포함되지 않는다.**
- **수동 강제 입력**: F1/F2/F3(인스펙터에서 변경 가능) 또는 Force 버튼으로 판정 대상의 다음 노드를 Perfect/Good/Miss로 강제 입력. `DebugForceInput(result)`가 `ExpectedPointIndex`에 `ForceDown()` → 기존 입력 파이프라인 재사용, `AddPattern`은 `result = debugForcedResult ?? Judge(delta)`로만 분기.
- **자동 Perfect(오토플레이) 토글**: 켜면 각 노드의 도달 타이밍(`ExpectedTime`)마다 자동 Perfect 처리되어 패턴이 저절로 진행.
- 인스펙터: On/Off 마스터 토글, 키 매핑, 마지막 사용 모드 색상 하이라이트, Force 버튼. 마스터가 꺼지면 전부 무반응. 상세: `docs/DebugInput/`

### 9. 씬 전환 / 곡 선택
- **`SongSelectManager`**: 곡 선택 씬에서 버튼으로 `SelectChart(chart)` → `GameSession.SelectedChart`에 등록 후 `BattleScene` 로드. 씬 이름은 `gameplaySceneName` 인스펙터 값이 진실의 원천이다(코드 기본값은 새 인스턴스용 폴백).
- **`GameSession`**(Singleton, DontDestroyOnLoad): 씬을 넘어 `SelectedChart`를 전달. `ChartPlayer`가 읽어 사용.

#### ⚠ 이 구조는 스토리 모드에서 폐기된다 (설계 확정 · 코드 미구현)

**`곡 선택 씬 → 전투 씬 → 종료 → 다시 곡 선택 씬`은 스토리 모드에 존재하지 않는다.** 플레이어는 **심상세계를 3인칭으로 직접 걸어 다니고**, 괴물이 고여 있는 무대에 들어서면 **그 자리에서** 곡이 시작되며, 끝나면 그 자리에 선 채 세계가 이어진다. 근거와 서사 규율은 `docs/Story/Story_Overview.md` §1-1·§2-4에 있다.

- **모드가 둘로 갈린다**(`GameMode`) — **Story**(심상세계, 곡 선택 없음) / **FreePlay**(해금된 곡만 다시 치기). **`SongSelectScene`·`SongSelectManager`는 삭제하지 않는다** — FreePlay 전용으로 소속만 바뀐다. ⚠ **FreePlay 결과는 파편·등급 기록·엔딩 판정 어디에도 쌓이지 않는다**(쌓이면 서사가 성적표의 부산물이 된다).
- **곡은 고르는 것이 아니라 자리에 딸린다** — 무대 하나가 `Encounter` 컴포넌트로 자기 `SongChart`를 들고, `EncounterDirector`가 진입 감지 → 곡 시작 → 종료 후 탐색 복귀를 맡는다. **`ChartPlayer`·`PatternHandler`·`EnemyDirector`는 한 줄도 안 고친다** — `GameSession.SelectedChart` 경로가 그대로 살아 있고 FreePlay가 계속 쓴다.
- **⚠ 재도전은 UI가 아니라 장소다.** 그 곡을 `SSS`로 못 낸 자리는 **아직 얕게 일렁이고**, 다시 들어서면 같은 곡이 다시 시작된다. 재도전 메뉴·확인 창이 없다 — "들어서면 시작된다"는 규칙이 처음과 재도전에서 똑같이 작동할 뿐이라 **새로 배울 규칙이 0이다**. 등급은 **최고 기록으로만 갱신**되고, 트루 엔딩(전 곡 `SSS`)으로 가는 세계는 **일렁임이 하나도 없는 세계**라 진행도 UI가 필요 없다.
- **무대 하나 = 씬 하나 = 곡 하나**이며, 씬 안에 전투 원(`stageRadius` 8m)과 그 주위 탐색 영역이 함께 있다. **무대 중심은 여전히 월드 원점**이라 §11-2의 배치·이격·프레이밍, §13의 배치표 좌표, 라이트맵이 전부 그대로다 — **이 개편은 무대 안을 건드리지 않는다.** 씬 경계는 골목·계단참 같은 **좁고 시야가 막힌 통로**에만 둔다.
- **⚠ 이 개편의 유일한 위험 지점은 플레이어 위치의 소유권이다** — §11-2 참조.

### 10. 인프라
- **`Singleton<T>`**: `Instance` 게터가 최초 1회 인스턴스를 캐시/생성. `DontDestroy` 플래그로 씬 유지 여부 결정.
- **`Pool`**(Singleton): `PoolKey`(현재 `FallingNode`) → 프리팹 매핑(SerializedDictionary). `Get<T>(key, initializer)`로 대여, `Return(key, obj)`로 반납. 대여 대상은 `IPoolable`(OnSpawn/OnDespawn).
- **`PrefabPool`**(`Util/`, 순수 C#): 프리팹별 인스턴스 풀. `EnemyDirector`·`SliceTargetDirector`가 필드로 하나씩 든다. 반납 시 원본을 되짚는 표식은 런타임에 붙는 `PooledInstance`다. **`Pool`(PoolKey)과 역할이 다르다** — 저쪽은 키가 고정된 소수의 `IPoolable`, 이쪽은 에셋이 데이터로 지정하는 임의 개수의 프리팹.
- **`CanvasEffectPool`**(`Effect/`, 순수 C#): `CanvasEffectView` 전용 풀. `EffectManager`(Canvas)와 `PatternEffectDirector`(월드)가 공유한다. 반납 훅(`OnDespawn`)과 `SourcePrefab` 규약 때문에 `PrefabPool`과 나뉜다.

### 11. 베이는 표적 (Slice)
- 패턴 성공 시 표적이 **미리 구운 조각으로 갈라지고**, 실패하면 충돌·소멸하는 **연출 전용** 시스템. 판정/점수에 개입하지 않는다.
- **`MeshSliceBaker`**(`Slice/Core`, asmdef): 메쉬를 평면 여러 장으로 절단하는 순수 기하 로직. 다중 평면 교차는 "이미 잘린 조각을 다시 자른다"는 **순차 적용**만으로 성립한다(가로+세로 = `┼` → 4조각). 앞 평면이 만든 **캡도 일반 지오메트리로 취급**해 다음 평면이 자르고(안 그러면 교차부가 뚫림), 캡은 몇 번을 잘라도 **서브메쉬 하나(인덱스 M)에 병합**한다(머티리얼 슬롯 `M+1` 규칙). 결과는 **연결 요소별로 분해**되므로 조각 수는 2개가 아니라 N개다.
- **`MeshSliceBakerWindow`**(`Tools/Mesh Slice Baker`): 씬 뷰에서 **직선 획을 그어** 평면을 만든다(획 길이는 무시 — 무한 평면). 재굽기는 에셋을 **제자리 수정해 GUID를 유지**한다. 카탈로그가 없어 이것이 배선을 지키는 유일한 장치다.
- **`SliceSet`**(SO): 굽기 산출물(원본·조각 프리팹 N개·오프셋·흩뿌림 방향·`bakedPlanes`·풀 크기). **참조 주체는 채보의 `EnemyCue.projectile`이다** — enum 키 카탈로그를 두지 않는다(`PlayerAttack`과 같은 성격). ⚠ 예전에는 `Pattern.sliceTarget`이 직접 참조했지만 그 경로는 죽어 있어 필드째 제거했다(부활 레시피: `docs/SliceTarget/Guide_RevivePatternTarget.md`). 발사 지점은 `Reserve`의 `spawnOverride`(쏘는 적의 위치)가, 도착 시각 보정은 패턴의 `impactOffset`(Deadline 대비 ±초 — 플레이어 칼·적 칼·시체 교체·투사체·카메라 큐가 전부 읽는 공통 앵커 보정)이 정한다.
- **`SliceTargetDirector`**: 표적의 유일한 관리 지점. **임팩트 시각 = `Deadline`**(판정 종료 시점)이라 표적이 닿는 순간 성패가 이미 확정돼 있다(`LastNodeTime`에 맞추면 정상적인 늦은 Good이 실패로 연출됨). **캐릭터 베기 애니메이션이 이 시각에 자신의 임팩트 프레임을 맞춘다**(§6) — 정렬 식이 양쪽에서 동일하므로 칼날이 지나가는 순간과 절단 순간이 구조적으로 일치한다. 접근시간은 `min(approachDuration, impactTime − StartTime)`으로 **클램프**된다 — 표적은 첫 노드보다 먼저 나타날 수 없기 때문. **등장 위치를 authoring하고 속도는 파생시킨다** — 도착 시각이 Deadline으로 고정이라 '거리 = 속도 × 시간'에서 하나만 정할 수 있고, `spawnAnchor`(씬 Transform, 비면 임팩트에서 +Z로 `fallbackSpawnDistance`)로 거리를 잡아 화면 구도를 일정하게 유지한다. 그 결과 **패턴이 짧을수록 표적이 빨리 날아온다**(의도된 결과). 씬 뷰 기즈모(`drawGizmos`)가 스폰·임팩트 지점과 파생 속도를 표시하고, 플레이 중에는 활성 표적의 실제 경로와 성패 확정 상태(노랑/초록/빨강)까지 그린다.
- **물리를 쓰지 않는다.** 조각은 콜라이더·Rigidbody 없이 `SlicePiece`가 경과 시간 t로 위치·회전을 **닫힌 식**으로 계산한다. 조각이 표적의 자식이라 −Z 진행 속도는 구조적으로 승계된다.
- 가이드: `docs/!Guides/Guide_MeshSliceBaker.md` / 상세: `docs/SliceTarget/`

### 11-1. 상대 배정 시점 (⚠ 어기면 조용히 어긋난다)
- **`EnemyDirector`는 큐 접수와 상대 배정을 분리한다.** `OnPatternQueued`에서는 토큰·cue·예약만 만들고(`opponent = null`), **상대는 그 패턴이 판정 대상이 되는 순간에 배정한다**(`BindReservation`).
- 이유: **큐 시점에는 상대를 알 수 없다.** 다음 패턴의 상대는 지금 패턴의 성패가 정하는데(성공+처치면 다음 적, **실패하거나 성공해도 사슬이 안 끝났으면 같은 적이 이어진다** — §11-5), 그 답은 지금 패턴이 완료돼야 나온다. 겹침 리드타임(`exposureDuration` 0.5초) 때문에 다음 패턴은 그보다 **먼저** 큐에 오르므로, 큐에서 `currentOpponent`를 잡으면 **한 명씩 밀린 상대**가 잡힌다(실측 채보 92%가 어긋났고, 링에 서 있던 엉뚱한 적이 갈라졌다).
- 배정 시각은 **새 이벤트가 아니다** — 선행 예약이 확정되는 순간(`ResolveReservation` 끝)이 곧 다음 패턴이 판정 대상이 되는 순간이다(§3 "같은 프레임에 즉시 승계"). 선두 예약(앞에 미확정 예약이 없음)만 접수 즉시 배정한다.
- **승격(`PromoteOpponent`)은 `KillOpponent` 안에 그대로 둔다.** 배정이 완료 시점으로 내려오면 승격과 배정이 한 호출 흐름에 들어와 순서가 뒤집힐 수 없다.
- **실패 후 후퇴는 짧다**(`failRetreatDistance`). 링 복귀(`ReturnToRing`)는 **상대 자격을 잃을 때만** 한다 — 교전이 이어지는데 링(6m)까지 가면 다음 패턴에서 다시 달려와야 한다. 후퇴가 곧바로 이어지는 `AssignAttack`에 덮이지 않도록 `EnemyView`가 **이동 구간을 둘** 든다(`ScheduleMoveAfter`).
- **결투 계획은 `EnemyView.Destination`(갈 곳)으로 세운다**, `transform.position`이 아니라. 배정 순간 적이 이동 중이면(후퇴·대기석 진입·링 등장) 현재 위치는 곧 떠날 위치라, 후퇴에서는 "둘 다 제자리"로 계산되어 플레이어가 안 붙는다.
- 상세: `docs/OpponentBinding/`

### 11-2. 무대와 결투 배치 (⚠ "플레이어 중앙 고정 · 전진 이동 없음"은 폐기됐다)
- **무대는 월드에 고정된 원형이다.** `EnemyDirector.arenaCenter`는 **씬의 `Stage` 오브젝트**(원점)를 가리킨다 — **절대 플레이어를 가리키면 안 된다.** 예전엔 플레이어였고, 그래서 배치·스폰·계획이 전부 플레이어를 따라다녔다. 거기에 월드 고정점(리시) 개념을 하나만 덧댄 탓에 **모델이 둘**이었고 표류 버그가 그 증상이었다. `Center`가 상수가 되면 그 부류가 원천 소멸한다.
- **적은 무대 원 '안'에 흩어져 선다**(`stageRadius` 8, `minSpacing` 2.5m). 링 궤도가 아니다. 배치는 `EnemyRing.PickStagePosition` — 원 안 2D 후보 샘플링이고, **시야 판정은 각도 근사가 아니라 실제 절두체**(`GeometryUtility`)다. 무대가 고정되면 적도 플레이어도 원 안 어디에나 있어 각도로는 화면 안인지 알 수 없다.
- **스폰은 화면 밖에서 일어난다.** ⚠ 플레이어가 홱 돌면 방금 나타난 적이 보일 수 있다(감수). ~~등장 이동을 통째로 없앴다~~ — **무리 배치(§11-6)가 되돌렸다.** 근거가 반전됐기 때문이다: 예전에는 "아무도 못 보므로 걸어 들어올 이유가 없다"였는데, 무리는 **보이는 것이 목적**이라 짧은 등장 이동이 다시 필요하다.
- **⚠ 적이 흩어져 서는 것은 `clusterEnabled`가 꺼졌을 때의 폴백이다.** 기본 경로는 §11-6의 무리 배치이며, `PickStagePosition`은 그 폴백으로만 남아 있다(기존 테스트 4건의 대상이기도 하다).
- **다음 표적은 창이 고른다** — 여기가 속도감의 심장이다(`TakeTargetForWindow`). 창은 음악이 정해 0.5~2.1초로 4배 흔들리므로, 거리를 고정하면 속도가 그만큼 흔들린다. 거꾸로 `목표거리 = cruiseSpeed × 창 / playerShare + duelDistance`로 잡으면 **체감 속도가 일정**해지고 짧은 구간은 근거리 난타, 긴 구간은 무대 횡단 대시로 갈린다. **`playerShare`로 나누는 항을 빼면 비율을 올릴수록 오히려 느려진다.**
- **만나는 지점의 비율은 `playerShare`(현재 1.0 — 적은 제자리에 선다)**. 예전엔 0.5 고정 + 리시 1.5m라 플레이어가 **초속 1m, 걷는 것보다 느렸고**, 그 뒤 0.85로 올렸다. 지금 1인 이유: **적이 정지 표적으로 보이는 것을 막는 일을 '마중 한 걸음'이 아니라 견제 클립(`Pattern.EnemyFeint`, §11-4)이 한다.** 1보다 낮추면 적 이동이 창에 **비례**해 커져(`목표거리 = cruiseSpeed × 창 / playerShare`) 견제 구간이 `0.735 × 창`으로 잘린다 — 실측상 2초짜리 클립이 들어갈 확률이 0%가 된다. **플레이어 속도는 share와 무관하다**(거리를 `cruiseSpeed × 창`으로 잡으므로 share가 오르면 목표 거리가 오히려 줄어든다). `EnemyView.EarliestArrival`은 그대로 남아 있다 — 이동이 남는 경로(후퇴 후 복귀·링 등장)에서 여전히 필요하다.
- **상대 선택은 `BindReservation`이 한다** — 창을 알 수 있는 유일한 시점이기 때문(§11-1). `KillOpponent`는 죽은 상대를 놓아주기만 한다. 둘은 `ResolveReservation` 한 호출 안이라 같은 프레임이다.
- **⚠ 실패한 적의 반응은 창이 고른다**(`EnemyDirector.ResolveRetreatDistance` → `EnemyView.Resolve`). **다음 패턴의 창이 후퇴+재접근을 감당하면 회피**(`evadeStateName`, `failRetreatDistance`), **못 감당하면 제자리 패링**(`parryStateName`, 거리 0). 조건: `창 >= retreatDuration + failRetreatDistance×playerShare/cruiseSpeed + retreatWindowMargin`. 다음 예약이 없으면(곡 공백) 물러나지 않는다 — 돌아올 사람이 없다. **`Attacker.Enemy` 실패는 언제나 물러난다**(벤 뒤의 여파이지 회피가 아니다).
- **왜 조건부인가**: 재접근은 `TakeTargetForWindow`를 안 거친다(같은 적이 유지되므로) → **거리를 창에 맞추는 §11-2 규율이 빠져 있어** `failRetreatDistance`가 거리를 통째로 정한다. 창이 짧으면 그대로 늘어져 **0.9 m/s로 기어가고**, 그 사이 Attack 레이어 웨이트가 1이라(`recoveryHold` + `Release` ≈1.15초) **로코모션조차 안 보인다**. 제자리 패링이면 이동 거리가 `convergeMinDistance`(0.15m) 아래라 수렴 로코모션을 **아예 안 건다**. ⚠ 판정에 쓰는 `arriveTime`은 **낙관적**이다 — 진짜 마감은 플레이어 자기 스윙 시작인데 그 값은 `OnJudgeTargetBegan` 뒤에야 알 수 있어 못 본다. `retreatWindowMargin`이 그 간격을 흡수한다. 상세: `docs/FailConverge/`
- **⚠ 적은 도착 시각까지 끌지 않고 `moveSpeed`(3 m/s)로 빨리 가서 선다**(`EnemyView.EarliestArrival`). 안 그러면 **도착하는 순간이 곧 베이는 순간**이라 서 있는 구간이 아예 없다 — `ScheduleMove`가 선형 보간이라 1m를 1.3초에 펴면 초속 0.77m로 기어가고, 화면에는 *"제자리에 선 것 같은데 Run이 계속 도는"* 그림이 된다(`moving`이 true인 동안 로코모션이 유지되므로). **앞당기는 건 언제나 안전하다** — "클립 시작 전에 도착"이라는 제약과 방향이 같다.
  - 다만 이건 **결투 접근(`ApproachDuel`)에만** 건다. 후퇴·등장은 "이만큼 걸리는 동작"이라 저작된 지속시간을 그대로 쓴다(후퇴를 속도로 자르면 회피의 날카로움이 죽는다).
- **플레이어 로코모션은 창으로 갈린다**(거리가 아니다): `창 > 대시클립 길이 → Sprint`(루프, 이동속도에 맞춰 배속) / `이하 → Quickshift`(단발, 창 안에 완주하도록 배속, **상한 없음**). 거리로 가르면 평균 창(1.48초) > Quickshift 클립(1초)이라 **클립이 먼저 끝나고 나머지는 미끄러진다.**
- **뒤로 빠질 때는 클립만 갈아 끼운다**(`Quickshift_B`). 예전에는 부호를 아무도 안 봐서 전진 클립으로 뒷걸음질했다. 판정은 `dot(목표−현재, 적−목표) < 0` 하나이고, 교체는 이미 있는 `AnimatorOverrideController`가 한다 — **새 스테이트가 없다**. ⚠ **뒤로 갈 때 Sprint 분기는 막는다**(달리기 루프로 뒤로 미끄러지면 그림이 통째로 깨진다). 배선(`quickshiftBackClip`)이 비면 예전 동작.
- **⚠ 결투 간격은 상수가 아니라 패턴이 든 시간 함수다**(`Pattern.duelDistanceCurve`, §11-9).
- **⚠ `EnemyDirector.PlayerPosition`은 결투 앵커가 아니라 그 부모(플레이어 루트)다.** 앵커의 역할은 **기준 간격을 정하는 것**이지 플레이어의 자리가 아니다 — 앵커는 플레이어보다 그 간격만큼 **앞**(적 쪽)에 있어서, **현재 간격이 그보다 좁아지면 `BuildDuelPlan`의 `toEnemy`가 부호를 뒤집어 목표가 적 반대편에 잡힌다**(물러나야 할 때 **적을 관통해 건너간다**). 예전에는 결투 간격 = 앵커 거리라 경계값에 딱 붙어 증상이 안 났고, **거리 커브(§11-9)가 그 전제를 깼다**(임팩트 간격 0.4m로 저작하면 매 패턴 그 구간에 들어간다).
- **⚠ 플레이어 회전은 이동과 별개 스케줄이다**(`PlayerCombatMover.turnDuration` 0.15초). 한 벌로 묶으면 회전이 이동 시간(평균 1.5초)에 끌려가 **무대를 가로지르는 내내 목을 천천히 돌린다.** 예전엔 `OnOpponentChanged`의 회전이 같은 프레임 `OnDuelScheduled`에 통째로 덮여 `turnDuration`이 한 번도 안 쓰였다. 지금은 **먼저 상대를 보고 그 다음에 달린다.**
- **⚠ 탐색 이동(`PlayerExploreMover`, §9 · 미구현)과 전투 이동은 배타적으로만 켜진다.** 전투 중 플레이어 위치의 주인은 `PlayerCombatMover`(수렴)와 `Pattern.duelDistanceCurve`(§11-9)이고 둘 다 `transform.position`을 **대입**한다 — 탐색 컨트롤러가 같은 프레임에 살아 있으면 대입이 서로를 덮어 **간격 커브·도착 시각 계약(§6 임팩트 정렬)이 통째로 깨진다.** 배타성의 경계는 곡 시작/종료가 아니라 **`EncounterDirector`가 무대 진입을 확정하는 순간**이며(카운트다운 3초 동안 이미 결투 계획이 선다), 반대로 곡 종료 후에는 **마지막 액션의 복귀가 끝난 뒤에** 탐색으로 넘긴다.
- **⚠ 몸이 겹치는 것은 콜라이더로 못 막는다.** 플레이어·적 둘 다 `transform.position`을 **대입**한다(닫힌 보간·커브 구동) — 물리가 밀어낸 값은 다음 프레임에 소멸하고, 이동을 물리로 옮기면 도착 시각 계약(§6 임팩트 정렬)이 깨진다. 이격은 위치 계산 층에서 한다.
- **불가침 영역은 원이 아니라 캡슐이다** — 플레이어 원 ∪ 현재 상대 원 ∪ **둘을 잇는 통로**(`EnemyRing.SeparationPush`, `separationRadius` 1m). 통로가 곧 대시 경로이자 칼이 지나가는 선이라, 비우면 겹친 뒤 밀어내는 게 아니라 **닿기 전에 비켜서 있다**. 상대가 없으면 두 끝점이 같아져 **그대로 원**이 된다(분기 없음).
- **⚠ 미는 것은 적뿐이고, 그것도 `IsIdle`인 적뿐이다.** 플레이어는 도착 시각과 거리 커브가 위치를 소유하고, 이동 중인 적은 `Destination`이 결투 계획의 입력이다(§11-1) — 밀면 플레이어가 밀린 자리로 달려간다. **현재 상대는 캡슐의 끝점이라 애초에 위반자가 아니다**(거리 커브의 음수 = 관통은 의도다, §11-9).
- **⚠ 밀어내는 방향은 선분에 수직이다.** 방사로 밀면 통로 한가운데 선 적이 통로를 **따라** 미끄러질 뿐이다. 수직이라야 옆으로 비켜서고, 그래야 화면에서 "군중이 갈라진다"가 된다.
- **⚠ `LateUpdate`에서 돈다**(`TickSeparation`). `Update`면 `EnemyView.TickMove`·`PlayerCombatMover`의 보간과 실행 순서가 안 정해져 밀어낸 값이 **같은 프레임 lerp에 덮인다**(§7-3 `TickPendingKills`와 같은 근거). 밀린 값이 살아남는 이유는 대상이 idle이라 이동 보간이 꺼져 있고 배회가 `+=` 증분 경로이기 때문 — **새 상태 필드가 없다.**
- 상세: `docs/ActorSeparation/` · 무대 배치: `docs/StageTraversal/` (폐기: `docs/DuelConverge/`의 리시·대기석 결정)
### 11-6. 적 무리 배치 (EnemyCluster)
- **적은 무대 전체에 흩어지지 않고 `active`(지금 싸우는 무리) + `staged`(다음 무리) 두 덩어리로만 존재한다.** `ringCount`는 더 이상 독립 필드가 아니라 **`clusterSize × 2`로 파생**된다 — 둘을 따로 두면 어긋난 채 조용히 굴러간다.
- **무리 위치는 씬이 아니라 런타임이 정한다.** 앵커를 씬에 박으면 공급(적 위치)이 고정이고 수요(`desired = cruiseSpeed × 창 / playerShare + duelDistance`)가 연속이라 **거리가 이산화**된다 — 창이 5.5m를 요구해도 고를 수 있는 게 3m나 8m뿐인 상태. **찾는 대신 놓으면** 그 부류가 원천 소멸한다.
- **⚠ 무리를 통째로 옮기지 않는다.** 창이 바뀔 때마다 `staged` 전원을 재배치하던 방식(`RestageStaged`)은 **플레이 결과 적들이 우르르 몰려다니는 그림**이 되어 폐기했다. 지금은 **집결지가 고정**이고 적이 하나씩 걸어와 합류한다.
- **사망 1 : 스폰 1.** 적이 죽으면 그 자리에서 한 명이 태어나 집결지로 걸어간다. `RingCapacity`는 `clusterSize × 2`가 아니라 **`clusterSize`**다 — `active` 잔여 + `staged` 집결 인원의 합이 언제나 그 값으로 불변이라, 무리가 차는 순간과 `active`가 비는 순간이 구조적으로 일치한다.
- **집결지는 무리가 빌 때 한 번만 지정한다**(`DesignateStagedCenter`, 승격 직후). 거리를 음악에 맞추는 일은 그 한 번이 하고, 이후로는 아무도 안 움직인다. 사망 시점에는 창을 모르므로 `BindReservation`이 지나가며 남긴 `lastDesiredDistance`를 쓴다.
  - **⚠ 그 거리에는 상한이 있다**(`stagedMaxDistance`, 8m). 창에 비례시키면 5.5~8m가 나오는데, 적 `moveSpeed`가 3m/s라 **2.0~2.7초**가 걸린다 — 기습 리드가 1.1초라 그 무리는 후보로 쓸 수 없다. **상한은 여기에만 걸고 `TakeTargetForWindow`는 안 건드린다** — 무리 '안'의 교전 거리와 속도감은 그대로 두고 무리와 무리 '사이'만 좁힌다. ⚠ `minPlayerDistance`(5)가 실질 하한이라 그보다 줄이려면 그 값도 같이 내려야 한다.
- **⚠ `KillOpponent`의 순서가 걷어내기 → 보충 → 승격 → 재보충이다.** 승격을 먼저 하면 방금 태운 적이 승격에 휩쓸려 `active`로 넘어간다.
- **`active`가 현재 상대 하나만 남으면 `staged`에서 한 명을 부른다**(`ReinforceActiveIfThin`). 근거는 실측이다 — 기습 후보는 `active − 현재 상대`인데 `active`는 4→3→2→1로 마르고 **1에 도달하면 그 1명이 상대**라 후보가 0이 된다(한 곡에서 "후보 없음" 19건 중 17건이 `active 1 · staged 3`이었다. **인원은 있는데 다른 목록에 있었다**). 목록만 옮기므로 **위의 합 불변식은 안 깨진다.**
  - **⚠ 무리 세대당 한 번만 부른다.** 계속 채우면 `active`가 0에 도달하지 못해 **승격이 영영 안 일어나고**, 다음 무리로 대시하는 이 절의 리듬이 통째로 사라진다. 한 번이면 교전이 하나 늘 뿐이다.
  - 부르는 대상은 **현재 무리에서 가장 가까운** 적이다 — 이동이 짧을수록 빨리 후보가 되고 다음 무리 대형도 덜 흐트러진다.
- **곡 시작에는 무리 하나만 세운다.** 둘 다 미리 세우면 시작부터 두 덩어리가 보여 원래 문제(정신없음)로 되돌아간다.
- **재배치는 카메라를 안 본다.** 이동은 보여도 되는 동작이다. **절두체 판정은 스폰에만 남는다**(`PickSpawnNearCluster`) — 실패 양상이 다르기 때문이다. 보이는 이동은 아무 일도 아니고, **보이는 팝인은 즉시 티가 난다.**
- **스폰은 자기 자리에서 가장 가까운 화면 밖 지점**이다. 무대 가장자리에서 걸어오게 하면 첫 이동만 무대 횡단(최대 16m)이 되어 재배치 예산으로 감당할 수 없다. 자리가 이미 화면 밖이면 **오프셋 0 = 이동 없음**이 정상 경로다. 등장 연출은 `SpawnAt` 하나로 격리돼 있어 **후속 파티클 등장은 그 메서드만 갈아끼우면 된다.**
- **⚠ `TakeTargetForWindow`의 후보를 `active` 무리로 묶는다.** 링 전체에서 거리로 고르면 `desired`가 클 때 `staged`를 집어 **플레이어가 두 무리 사이를 왔다 갔다 한다.**
- **무리 안에서는 플레이어 이동이 0에 가깝다**(반경 2m). 즉 `clusterSize`가 4면 **3패턴 연속 제자리 난타 뒤 한 번 대시**다 — §11-5 사슬과 같은 현상이고, 그 리듬 자체가 의도다. 조이려면 `clusterSize`를 줄이거나 `clusterRadius`를 키운다(후자는 이산화 완화와 같은 노브).
- `clusterEnabled`를 끄면 전부 예전 경로(`PickStagePosition`) — 회귀 없음.
- 상세: `docs/EnemyCluster/`

### 11-7. 배회 상하체 분리 (EnemyUpperBody)
- **배회 중에는 상체를 클립 하나가 덮는다.** `Walk` 블렌드 트리의 `WalkForward`는 칼을 **한 손**으로, 나머지 `_HS` 셋은 **두 손**으로 잡는데, 블렌드는 본 회전을 가중 평균하므로 대각선 이동에서 **두 파지 사이의 어중간한 포즈**가 나왔다. 배회는 궤도 슬롯 추적이라 대각선 성분이 상시 존재해 그 구간 대부분이 그 상태였다.
- **해결은 마스크 레이어 하나다** — `EnemyAnimator`의 `[1] Upper Body Layer`(Override, weight 0, `EnemyUpperBodyMask`) + `UpperIdle` 스테이트. 하체는 블렌드 트리가, 상체는 Idle 클립 하나가 소유하므로 **파지가 섞일 수가 없다**. 방향 클립이 몇 개로 늘어도 안 깨진다.
- **마스크는 body-part 토글만 쓴다**(현재 `LeftArm`·`RightArm`·`LeftFingers`·`RightFingers`). ⚠ 적(`Spine`/`Bip001…`)과 플레이어(`root/pelvis/spine_01…`)는 **본 이름 체계가 완전히 달라** transform path 마스크는 리그에 묶인다 — 둘 다 Humanoid라 body-part 마스크만이 양쪽에 붙는다(`PlayerAvatarMask`는 path 기반이라 재사용 불가하며 현재 미사용).
- **켜는 조건은 `walkingNow` 하나**다. 이미 히스테리시스를 통과한 "지금 배회 걷는 중인가"의 진실의 원천이라 새 판정을 만들면 그것과 어긋난다. **`Run`(결투 접근)은 단일 클립이라 켜지 않는다** — 켜면 오히려 달리기 상체가 죽는다.
- **⚠ 공격·사망·리액션에서는 반드시 내린다.** 그 클립들은 상체가 전부라 마스크가 덮으면 **칼을 휘두르지 않는 그림**이 된다. 조건은 `ApplyLocomotion`과 같은 식(`Windup || Dying || reactionUntil`)이며, 해제는 **두 곳**이다 — 진입 요청을 꺾는 `SetUpperBody`와, 이미 켜진 뒤 공격이 시작된 경우를 잡는 `TickUpperBody`.
- **⚠ 웨이트 보간은 `Update`에 있다(`TickWander` 안이 아니다).** 배회가 끝나면 `TickWander`가 즉시 return하므로 거기 두면 **웨이트가 1에 굳는다.**
- 상체 클립은 `Samurai_Idle`/`Samurai_BlockIdle` 중 **배회 진입마다** 하나를 뽑는다(직전 것 회피). 매 프레임 뽑으면 떨리고, 스폰 때 한 번만 뽑으면 그 적은 곡 내내 같은 자세다. 교체는 기존 `AnimatorOverrideController` placeholder 방식 그대로다.
- 풀 반납(`ResetState`)에서 웨이트·목표·직전 인덱스를 되돌린다 — 안 하면 **다음 대여가 상체가 굳은 채 나온다**.
- 상세: `docs/EnemyUpperBody/`

### 11-8. 기습과 회피 (EnemyAmbushDodge · AmbushVisibility)
- **`DodgeDirector`**: 플레이어 애니메이션 **공백**에 적 하나가 끼어들어 찌르고, 플레이어가 회피 키(Space)로 피하는 사건의 유일 관리 지점. 자리는 `CharacterActionPlayer.OnIdleWindow`가 준다(도착 뒤 ~ 다음 액션 클립 시작 전). **판정 파이프라인에 개입하지 않는다** — `PatternHandler`는 패턴 판정의 단일 소유자로 남고 회피는 자기 시계로 판정한다.
- **⚠ 경고 시간은 상수다**(`minRingExposure` = `maxRingExposure` = **1.0초**). "언제나 같은 시간"이 이 기능의 요구다.
  - **자격 검사가 안전장치다** — `RequiredLead = max(와인드업, minRingExposure)`이고 `PickClipWithin`이 그보다 리드가 짧은 클립을 거르므로, **발동한 기습은 반드시 리드 ≥ 노출**이다. 약속보다 짧게 보이는 경우가 구조적으로 안 생긴다.
  - **⚠ `lead`는 창 길이가 아니다** — `start`가 아니라 `now`부터 재므로 **대시 구간을 포함한다.** 링·적 클립은 플레이어가 이동 중이어도 성립한다(링이 `ScreenSpaceOverlay`라 이동과 무관). 공백에 요구되는 것은 **구르기뿐**이다(`minIdleWindow` = `dodgeWindow + rollMoveDuration + margin`).
  - 그래서 **노출을 늘려도 빈도가 안 깎이는 게 아니라 깎인다** — 그 대가는 후보 선정(§11-6의 `ReinforceActiveIfThin` 등)이 되찾는다. 지금 빈도를 붙잡는 유일한 장치는 `cooldown`(3초)이다.
- **후보 선정은 두 번 시도한다** — 사전 접근(`OnDuelScheduled`, 한 패턴 앞)과 늦은 선정(`HandleIdleWindow`). 한 번만 하면 그 순간 조건을 만족하는 적이 없을 때 창이 통째로 날아간다.
  - **`staged` 무리도 후보다**(`PickIdleAmbusher(includeStaged)`). `active`만 보면 후보가 구조적으로 자주 0이다(§11-6). **`active`가 빈손일 때만** `staged`를 본다 — 가까운 적이 있으면 그쪽이 언제나 낫다.
  - **⚠ 묻는 것은 "먼가"가 아니라 "제때 닿는가"다**(`canReach` → `EnemyView.TravelTime`). 집결지 거리는 창에 비례해 3~8m로 변하므로 거리로 뭉뚱그려 막으면 **가까울 때까지 버린다.** 마감은 임팩트가 아니라 **클립 시작**(`impact − lateStageWindup`)이다 — 와인드업 동안에는 이미 서 있어야 칼이 어긋나지 않는다.
  - **`staged`에서 빌려온 기습자는 집결지로 돌려보낸다**(`ReleaseAmbusher`). `active`에서 온 기습자는 제자리에 남는다 — 무리에서 하나가 나와 찌르고 다시 섞이는 것이 그 사건의 전부다.
- **닷지 포인트는 화면 우하단에 고정된다**(`DodgePointView`). 예전에는 적을 따라다녔고 그래서 **적이 화면 밖이면 단서가 사라지고 중앙에 오면 패턴 링과 겹쳤다.** 자리를 고정하면 둘 다 존재하지 않는 문제가 되고, 그 클래스는 카메라를 아예 모르게 된다(월드→화면 투영·`LateUpdate` 규율이 통째로 사라졌다).
  - ⚠ **패턴인풋 밖이어야 한다** — Point_3이 (700,−700)이다. 씬에서 `Canvas` **직속**이어야 하며, `PatternHandler`(1400x1400)의 자식으로 두면 그 rect 기준 앵커가 되어 캔버스 좌표로는 패턴 영역 한가운데에 온다.
- **역할이 갈린다 — 아웃라인 = 누가·어디서 / 닷지 포인트 = 지금.** 링이 자리를 못 말하게 된 대신 기습자에게 아웃라인을 켠다(`EnemyView.SetHighlight`). 배선은 `Fire()`(= 텔레그래프 시작 = 카메라·소리와 같은 순간)이고 끄는 곳은 `Finish()`·`Abort()` **양쪽**이며 `ResetState`도 끈다(안 끄면 **다음 대여가 빛나는 적으로 나온다**).
  - 구현은 **레이어 스왑 하나**다 — URP `RenderObjects` 피처(`AmbushOutline` 레이어 → `AmbushOutlineMaterial`)가 그 레이어만 한 번 더 그린다. 프리팹·머티리얼·셰이더를 안 건드리고 튜닝은 머티리얼 한 곳(`_OutlineColor`·`_OutlineWidth`)에 모인다.
  - **⚠ 깊이 상태를 건드리면 안 된다.** `CelOutline`은 메쉬를 부풀려 **뒷면만**(`Cull Front`) 그리는 셸 방식이라 **깊이 테스트가 겹치는 부분을 잘라내야 테두리만 남는다.** `depthCompareFunction = Always`로 열면 셸이 앞면 위에 그려져 **적 표면 전체가 칠해진다.**
- **소리는 `SoundManager.Play(SfxTrigger)`만 부른다**(`AmbushTelegraph`/`DodgeSuccess`/`DodgeFail` — 각각 `Fire()`·`Succeed()`·`Fail()`). `DodgeDirector`가 오디오를 직접 만지지 않는다(카탈로그 조회·풀은 매니저 관할). ⚠ **클립이 아직 카탈로그에 미배선이라 지금은 무음**이다 — 버그가 아니라 에셋이 안 꽂힌 상태이며, 넣으면 코드 변경 없이 난다.
- **회피 착지점은 구르기 <b>시작</b> 시각에 비운다**(`EnemyDirector.ClearSpot` — 캡슐이 점으로 무너진 특수해). 원호 중심이 현재 상대라 착지점은 통로 **밖**이고, 그래서 §11-2의 매 프레임 이격에 안 걸린다. `ScoreSpot`이 좌/우 중 빈 쪽을 고르지만 그건 점수일 뿐 보장이 아니다. **도착이 아니라 출발 시각인 것이 핵심** — 도착 후에 밀면 겹친 프레임이 이미 나온 뒤다. **기습자에게 예외 처리가 없다**: `Fire()`의 `AssignAttack` 이후 `hasPendingAttack`이라 `IsIdle`이 false로 자동 제외되고, 사전 접근 중에는 `"이동 중"`으로 걸린다.
- 상세: `docs/EnemyAmbushDodge/` · `docs/AmbushVisibility/` · 측정 기록과 폐기된 대안(예보·슬롯 저작): `docs/AmbushSlot/` · `docs/AmbushLookahead/`

### 11-9. 결투 거리 커브 (DuelDistanceCurve)
- **간격은 패턴이 든 시간 함수다** — `Pattern.duelDistanceCurve`(키 시간 = **임팩트 기준 상대초**, 값 = **절대 간격 m**). 계산은 `Pattern/Core/DuelGap`(asmdef·테스트됨) 하나이고 런타임·짝 에디터·슬라이서가 **같은 함수**를 부른다. **커브가 비면 예전 상수 경로**(`앵커 + duelDistanceOffset`) — 기존 패턴 회귀 0.
- **적은 고정, 플레이어만 움직인다.** 그래서 간격은 파생값이 아니라 `enemy − player(t)`라는 **정의**다. 음수면 플레이어가 적을 **지나쳐 뒤로** 나가고 **회전은 안 건드린다**(등 돌린 채 지나침 = 참격 후 잔심).
- **⚠ 시각의 출처는 `CharacterActionPlayer.DuelCurveTime` 하나다** — `Time.time − impactTime`을 쓰면 셋이 어긋난다: ① 히트스톱은 `AttackSpeed = 0`으로만 걸려 **시계는 계속 흐른다**(캐릭터는 얼었는데 몸만 미끄러짐 = "렉") ② `AttackSpeed` 압축이 걸리면 칼 리치와 간격의 대응이 깨진다 ③ 다중 히트스톱 예산은 재생을 F만큼 일찍 시작한다. **재생 헤드에서 파생하면 셋 다 공짜로 성립한다**(`clipConsumed`·`playSpeed`·`playingBaseSpeed`). 액션이 없으면 `NaN` → 구동 정지 = 홀드.
- **구동 구간 = 플레이어 클립 재생 구간**(도착 이후). 도착 목표 간격이 `curve(arriveTime − impactTime)`이라 이음매가 연속이고, 구간 밖은 `AnimationCurve`의 기본 Clamp가 **끝 키 값으로 홀드**한다(홀드 코드 0줄). 채보의 창 길이와는 무관하다.
- **위치의 주인 순서**: `rolling`(구르기) > **커브 구동** > 수렴 이동. 구르기는 진입 시 커브 구동을 놓는다(구른 뒤 자리는 구르기가 정한다).
- **⚠ `Attacker.Enemy`에서는 저작자 책임이다** — 적 칼은 예약 시점의 자리를 겨냥하므로 임팩트 시점 간격이 저작값과 다르면 빗나간다. 막지 않는 이유는 툴 프리뷰가 그 어긋남을 그대로 보여 주기 때문.
- 저작: `Tools/Animation Clip Trimmer`(타임라인 `t`와 같은 축) — 기준 거리는 씬 `EnemyDirector.DuelBaseDistance()`에서 자동으로 읽고, **프리뷰는 런타임과 같이 플레이어를 움직인다**(적은 임팩트 자리 고정). 가이드: `docs/!Guides/Guide_CharacterActionTrim.md` / 상세: `docs/DuelDistanceCurve/`

### 11-10. 상호 공격 — 실패하면 플레이어가 맞는 `Attacker.Player` 패턴 (PatternCounter)
- **`Attacker.Player` 패턴에 `enemyAttack` 클립이 배선돼 있으면 적이 견제 대신 진짜 공격을 같이 휘두른다.** 두 칼이 같은 시각(`Deadline + ImpactOffset`)을 겨누고, **실패하면 적의 칼이 닿아 `OnPlayerHit`이 난다** — 즉 §6의 "`Attacker.Player`면 피격 자체가 없다"의 **유일한 예외**다.
- **저작 필드가 0개다**(`Pattern.CountersOnFail`은 게터). `Attacker.Player` + `enemyAttack`은 원래 `WarnUnusedSlots`가 "재생되지 않는 슬롯"으로 경고만 하던 **무의미한 조합**이라, 거기에 의미를 준다. bool을 따로 두면 "켰는데 클립이 없다"/"클립은 있는데 껐다"가 새로 생기지만 **클립 유무가 곧 의도면 그 부류가 원천 소멸한다**(§2-1의 "분기가 아니라 데이터로 갈린다"와 같은 관용구).
- **⚠ 반격(실패를 보고 그때 휘두르기)이 아니라 상호 공격이다.** 첫 미스에 배정하면 임팩트까지 남는 실시간이 `goodWindow`(0.1초)뿐이라 사망 클립과 같은 함정에 빠진다(§11-3). **견제와 같은 구간**(`BindReservation` ~ 임팩트)에 놓으면 창이 `Attacker.Enemy` 패턴과 완전히 같아져 그 문제가 통째로 사라진다.
- **성공 경로에 새 코드가 0줄이다** — `ResolveReservation`이 이미 성공+`Attacker.Player`에 `Pattern.EnemyHit`을 `impactTime − ReactionLead`에 예약하고, 그것이 적의 공격 클립을 **자기 임팩트 직전에 끊는다**(= "플레이어가 더 빨랐다"). `killOnSuccess`면 `EnemyDeath`가 끊는다.
- **⚠ 손댈 곳은 실패 경로 하나다 — 적은 아무 반응도 하면 안 된다.** `EnemyView.Resolve`는 실패에서 **반드시** parry/evade로 크로스페이드하는데, `Deadline`과 임팩트가 `ImpactOffset`밖에 안 떨어져 있어 **적 칼이 닿기 직전 프레임에 회피 모션으로 튄다.** `keepAttackClip`이 그 크로스페이드만 건너뛴다.
  - **⚠ 그래도 `Resolve`를 불러야 한다.** `ApplyLocomotion`이 `Phase.Windup`에서 스스로 물러나므로, 건너뛰면 `Windup`을 빠져나오는 경로가 (`Resolve`·`MarkDying`뿐이라) 사라져 **적이 영구히 갇힌다**. 대신 `reactionUntil`을 걸어 그 사이 로코모션·상체 마스크가 따라베기 잔여를 못 덮게 한다 — 기존 노브라 **새 상태 필드가 0개다**.
  - **⚠ 아직 시작 안 한 공격 예약은 살려 둔다**(`attackStillPending`). `ImpactOffset`이 와인드업보다 크면 Deadline 시점에 클립이 대기 중일 수 있고, 그때 `hasPendingAttack`을 지우면 **적이 안 휘두른 채 플레이어만 맞는다**.
  - **⚠ 넉백은 0이다** — 물러나면 자기 칼이 안 닿는다. 성공(사슬 중간)과 값은 같지만 근거가 반대라 분기를 따로 둔다.
  - **⚠ `OnEnemyReacted`는 `Evade`로 보낸다.** 넉백 0이라 그냥 두면 `Parry`로 읽히는데 적은 막은 게 아니라 **벴다**. `Attacker.Player`에서 `Evade`의 뜻이 이미 "플레이어가 맞았다"라 저작 규칙이 안 늘어난다(§7-4).
- **⚠ 견제의 `minFeintWindow` 가드를 물려받지 않는다.** 견제는 창이 짧으면 깜빡임이라 안 거는 편이 나았지만, 상호 공격은 **안 걸면 실패해도 안 맞는다**(규칙 자체가 사라진다). 창이 짧으면 `AssignAttack`이 시작 시점에 남은 시간으로 배속을 재계산해 스스로 벌충한다.
- **`PlayerHealth`·`CameraDirector`·`PatternHandler`는 한 줄도 안 고쳤다** — `OnPlayerHit` 하나에 체력 감소와 `PatternMiss` 카메라 큐가 이미 매달려 있다(§7-6).
- 저작: `Tools/Animation Clip Trimmer`의 **적 보조 슬롯 → `Attack`**(ImpactTime을 플레이어와 같은 `t = 0`에 찍는다) · `Tools/Pattern Chart Tool`의 **`상호 공격 · 실패 시 피격`** 배지.
- 상세: `docs/PatternCounter/`

### 11-4. 견제 — 표적이 된 순간부터 임팩트까지 (EnemyFeint)
- **`Attacker.Player` 패턴에서 적은 휘두르지 않는다** → 그 구간에 클립이 없어 **표적이 된 순간부터 베이는 순간까지 가만히 서 있었다.** `Pattern.EnemyFeint`(`ClipAlignment` 슬롯)가 그 구간을 채운다.
- **임팩트가 없는 슬롯이다.** 닿지 않는 동작이라 `ImpactTime`을 찍지 않고, 그러면 `ClipAlignment`가 트림 끝을 임팩트로 폴백해 **클립 끝이 임팩트 시각에 붙는다**(§6과 같은 정렬 규칙, 새 수학 없음).
- **새 애니메이터 스테이트를 만들지 않는다** — `Attacker.Player`에서는 `Attack` 슬롯이 비어 있으므로 그것을 쓴다. 예약 필드도 공격과 공유하며(`hasPendingAttack`), 그래서 `Resolve`·`MarkDying`의 정리 경로가 공짜로 따라온다.
- **시작은 표적이 되는 순간이다**(`BindReservation`) — 도착을 기다리지 않는다. `playerShare = 1`이라 적 이동이 0이라서 기다릴 도착이 없고, 그 차이가 창 전체(`W`)를 쓰느냐 `0.735 × W`만 쓰느냐를 가른다.
- **트림 0.8초 이하가 기준이다.** 실측(`Dreamer_Lv10`, 86엔트리): `W`는 min 0.50 / p50 1.30 / max 2.10초이고, 0.8초 클립이 배속 없이 들어가는 비율이 **97%**다(2초짜리는 23%). 원본이 긴 공격 모션이면 가장 읽히는 0.8초만 잘라 쓴다.
- **창이 `minFeintWindow`(0.35초)보다 짧으면 아예 안 건다** — 너무 짧은 재생은 동작이 아니라 깜빡임으로 보인다. 배속 클램프는 견제에서 **경고하지 않는다**(정렬이 깨질 것이 없고, 넘친 구간은 성패 확정의 리액션 크로스페이드가 끊는다).
- **클립이 없으면 기본 Idle** — 예약을 걸지 않고 자리만 잡으면 `ApplyLocomotion`이 이동 없는 상태에서 Idle을 유지한다(예전 동작 그대로).
- 저작: `Tools/Pattern Chart Tool`이 엔트리마다 **클립 트림 길이와 그 엔트리의 실제 창을 나란히** 보여 주고(길면 경고), `Tools/Animation Clip Trimmer`의 적 슬롯 선택기에서 `enemyFeint`를 골라 정밀 저작한다.
- 상세: `docs/EnemyFeint/`

### 11-5. 연계 공격 — 한 적에 짧은 패턴 여러 개 (PatternChain)
- **사슬 = `EnemyCue.killOnSuccess = false`의 연속.** 새 데이터 모델이 없다 — 상대가 비는 유일한 경로가 `KillOpponent`라, 안 죽이면 `BindReservation`의 `currentOpponent == null` 분기를 안 타고 **같은 적이 그대로 이어진다**(§11-1). 마무리는 `killOnSuccess`가 켜진 마지막 엔트리다.
- **긴 패턴을 쪼개는 게 아니라 짧은 패턴을 잇는다.** 사슬 전체가 1~2초라, 그 구간은 무대를 가로지르는 대시가 아니라 **근거리 난타**로 읽힌다. ⚠ 사슬 동안 `TakeTargetForWindow`를 안 거치므로 **플레이어 이동이 0이다**(§11-2의 속도감이 그 구간만 멈춘다). 길게 저작하면 그대로 정지로 보인다.
- **처치 = 마지막 타 성공 && 성공 수 >= `ceil(사슬 길이 × chainKillRatio)`.** 마지막 타 성공이 **필수**인 이유는 빗나간 임팩트에는 절단을 붙일 자리가 없기 때문이다(그 순간 적은 패링·회피 모션 중이다). 비율은 노브 하나로 "마지막 타만 본다"(0에 가까움)와 "전부 성공해야 한다"(1) 사이를 잇고, **비사슬 엔트리는 길이 1이라 어느 값에서도 1타 필요** — 기존 채보에 회귀가 없다.
- **임계 미달로 살아남으면 카운터를 리셋하지 않는다.** 계속 맞아 온 적이라 다음 사슬의 성공이 누적돼 결국 죽는다 — 이게 "안 죽은 적이 곡 후반까지 쌓인다"를 막는 장치다.
- **사슬 중간 타격에는 넉백이 없다**(`ResolveRetreatDistance`가 성공+`Attacker.Player`에 0을 돌려준다). 물러나면 재접근이 `TakeTargetForWindow`를 안 거쳐 매 타격 기어가는 구간이 생긴다(`docs/FailConverge/`). ⚠ `Attacker.Enemy`는 예외다 — 패링당해 밀려나는 것은 그 연출의 핵심이라 넉백이 살아 있고, 그래서 **사슬 안에 역할이 섞이면 그 타만 늘어진다**(굽기 툴이 경고한다).
- **리액션은 패턴이 소유한다** — `Pattern.EnemyHit`(맞았는데 안 죽음) / `Pattern.EnemyParry`(막아냄). 고정 스테이트 이름이면 가로베기든 내려베기든 같은 모션이 나온다. 비우면 `EnemyView`의 `knockBack`/`parry` 스테이트로 폴백한다.
  - **⚠ `ImpactTime` 폴백 방향이 견제와 반대다.** 리액션은 **맞는 순간이 곧 시작**이라 미지정이면 임팩트에 시작한다(`ReactionLead`가 0). `ClipAlignment`의 기본 폴백(트림 끝 = 임팩트)을 그대로 쓰면 맞기도 전에 다 젖혀져 있다.
  - **재생은 `Attack` 슬롯 + `AttackSpeed`다**(견제와 같은 규율, 새 스테이트 없음). 그 덕에 히트스톱이 `AttackSpeed = 0` 하나로 리액션까지 얼린다.
  - **트림 0.5초 이하 권장** — 임팩트에 시작하므로 길면 다음 패턴의 견제 클립이 끊는다.
- **중간 타격도 히트스톱이 걸린다**(§7-3). `EnemyDirector.ApplyHitStop`이 `pendingKills` 뒤에 `currentOpponent`도 얼린다. 살아 있는 적은 **절단 시각을 밀지 않는다** — 밀 절단이 없다.
- **회피(`Evade`)는 승격하지 않았다** — 클립·후퇴 이동·무대 경계 클램프가 한 덩어리라 `ClipAlignment` 하나로 안 끝난다.
- 저작: `Tools/Pattern Chart Tool`의 **`사슬로 굽기`**(마지막 타가 마무리, `i % N == N-1`). ⚠ 옆의 `매 N번째만 처치`는 **위상이 반대다**(`i % N == 0` — 첫 타가 마무리라 사슬을 만들려고 쓰면 첫 타에 죽는다). 엔트리 행에 `사슬 2/3 · 2타 이상 필요` 뱃지와 미마감·역할 혼재 경고가 뜬다. 리액션 클립은 `Tools/Animation Clip Trimmer`의 **적 보조 슬롯** 선택기(Feint/Hit/Parry)에서 저작한다.
- 상세: `docs/PatternChain/`

### 11-3. 적 사망 클립과 절단 시점
- **⚠ 어느 각도로 갈라지는가는 패턴의 스윙이 정한다.** `EnemyDefinition.deathSliceSets[]`가 각도별 세트를 들고, 패턴은 자기 `playerAttack` 임팩트 프레임에서 유도된 평면(`Pattern.BladePlane`) 하나만 든다 — `ResolveDeathSet`이 `SliceMatch`로 **가장 비슷한 각도로 구워진 세트**를 고른다.
  - **세트를 키가 아니라 기하로 고른다.** 인덱스·enum으로 고르면 패턴마다 키를 정해야 하고 새 각도마다 enum이 늘지만, 평면으로 고르면 **저작 필드가 0개**고 근처 각도가 이미 구워져 있으면 **굽기 0회로 재사용**된다. 굽는 횟수가 패턴 수(계속 증가)가 아니라 **서로 다른 각도 수**(포화)를 따라간다.
  - **⚠ 좌표계가 둘이고 섞으면 안 된다.** `SliceSet.BakedPlanes`는 **메쉬 로컬**(실제로 메쉬를 자르는 값), `Pattern.BladePlane`·`SliceSet.BakedBladePlane`은 **적 루트 로컬**(canonical — 비교 전용). 메쉬 로컬은 리그 구조에 종속이라 적 종류를 넘나들며 비교하면 조용히 헛것을 비교한다. 적 루트는 발바닥이라 휴머노이드끼리 의미가 같다.
  - **⚠ 절단 평면에는 부호가 없다** — `n`과 `−n`은 같은 절단면이다. 유도 법선은 칼의 진행 방향이 정하므로 같은 각도라도 좌우가 반대면 뒤집혀 나온다. `SliceMatch`가 두 부호를 함께 보지 않으면 그 둘이 180° 차이로 읽혀 **재사용이 전부 실패한다**(유닛테스트가 지키는 지점).
  - **⚠ 프리웜은 배열 전체를 돈다.** 하나만 채우면 나머지 각도가 뽑힐 때 곡 도중 `Instantiate`가 나고 그 히치가 그대로 판정 손실이다(§5).
  - **패턴은 여전히 `SliceSet`을 참조하지 않는다** — 시체 프리팹에 그 적의 스켈레톤 사본이 들어 있어 세트는 적 모델을 넘나들 수 없다. 패턴이 세트를 들면 "세트는 패턴이 고르고 죽는 적은 링에서 고른다"가 되어 **엉뚱한 몸이 갈라지는 상태가 표현 가능**해진다. 소유자는 `EnemyDefinition` 그대로고 **개수만 늘었다**.
  - 패턴에 평면이 없거나 후보가 없으면 **기존 단일 필드(`deathSliceSet`) 그대로** — 어느 한쪽만 이관해도 안전하다.
  - 저작: `Tools/Mesh Slice Baker`의 **`패턴 감사` 탭** — `스캔` 한 번이 전 패턴의 평면을 유도·기입하고 기존 세트와 매칭해 `재사용 / 굽기 필요`를 판정한다. 굽기가 필요한 것만 체크해 일괄 굽기.
- **`Pattern.EnemyDeath`는 런타임에 재생된다.** 임팩트 프레임이 플레이어 공격과 **같은 절대 시각**(`Deadline + ImpactOffset`)에 오도록 배속을 역산한다(§6의 두 배우 규칙).
- **절단(시체 교체·폭발)은 임팩트 프레임**이다 — 칼이 지나가는 그 순간 갈라진다. 사망 클립 유무와 무관하게 `burstTime = impactTime`이며(`EnemyView.AssignDeath`), 사망 클립은 처치 확정~임팩트 구간에만 보인다. (예전엔 트림 끝이라 쓰러지는 걸 다 본 뒤 갈라졌다.)
- **재생은 처치 확정 즉시 시작한다.** 그보다 이른 시각은 알 수 없다(성패가 마지막 노드에서 정해진다). 그래서 임팩트까지 남는 실시간은 `goodWindow`(0.1초) + `impactOffset`뿐이고, **사망 클립의 `ImpactTime`은 트림 시작 근처에 찍어야 한다.** 죽는 모션은 원래 '맞는 순간'이 시작점이라 자연스럽게 맞는다. 뒤에 찍으면 정렬이 깨지기 전에 **쓰러지는 속도부터 빨라진다**(§6의 배속 경고).
- **슬롯은 `Attack`과 나눈다**(`Death` 스테이트 + `DeathSlot_Placeholder` + `DeathSpeed`). 적이 공격 도중 죽을 때 같은 슬롯을 덮으면 진행 중인 클립이 튄다.
- **죽는 적은 결투 위치를 비켜 준다**(`deathClearOffset` 0.6m). 승격은 확정 즉시 일어나 다음 상대가 같은 자리로 들어오기 때문 — 예전엔 임팩트에 사라져 문제가 없었다. 루트 모션이 있는 사망 클립이면 0으로 끈다.
- **이벤트가 둘로 갈린다.** `OnEnemyKilled`는 **확정**(승격·링 보충과 같은 시점, 화면에는 아직 아무 일도 없다), `OnEnemyBurst`는 **절단 = 임팩트**(화면에서 사건이 일어나는 순간). **카메라 쉐이크는 `OnEnemyBurst`를 듣는다** — 확정에 걸면 칼이 닿기도 전에 화면이 흔들린다.
- **굽기 포즈는 사망 클립의 임팩트 프레임**이다(`MeshSliceBakerWindow.BakePoseTime`). 터지는 순간의 포즈로 구울수록 관절 뒤틀림이 준다 — 절단 시각이 트림 끝 → 임팩트로 옮겨졌으므로 굽기 포즈도 같이 옮겨야 한다.
  - **⚠ 평면을 유도하는 포즈와 메쉬를 자르는 포즈가 같아야 한다.** `TryDerivePlanes`는 적을 굽기와 **같은 포즈로 샘플링한 뒤** 좌표를 옮긴다 — 포즈를 안 잡으면 FBX 바인드 포즈(서 있지도 않다, 몸통 중심 y ≈ −0.09)를 기준으로 평면이 만들어져 **몸을 통째로 빗나간다**.
  - **⚠ 빗나간 평면은 굽기를 실패시키지 않는다** — `MeshSliceBaker`가 '원래 떨어져 있던 메쉬 섬들'을 그대로 돌려주므로 조각 수가 0이 아니고, 툴은 성공으로 끝나며 런타임은 멀쩡한 시체를 세운다(**몸통이 안 갈라진다**). 그래서 `TryDerivePlanes`가 유도 직후 **평면이 포즈 메쉬를 실제로 가르는지** 검사하고 아니면 그 행을 실패로 찍는다.
- **절단면에서 피가 계속 난다** — `CorpseView.Bleed`가 흩어진 조각마다 루프 파티클(`VFX_CorpseBleed`)을 **자식으로** 붙인다. 부모가 조각이라 조각이 구르면 피도 같이 돈다(추종 코드 0줄).
  - **발생원은 점이 아니라 절단면 자체다** — 캡(잘린 면)은 굽기가 **마지막 서브메쉬 하나**로 병합해 두므로(머티리얼 슬롯 M+1 규칙), 파티클 shape을 `MeshRenderer` + `useMeshMaterialIndex = subMeshCount − 1`로 주면 **면 전체에서 고르게 솟는다**. 방향은 캡의 면 법선이 그대로 준다 — **절단 평면도, 좌표계 변환도, 저작값도 필요 없다**(`BakedBladePlane`을 안 본다). 서브메쉬가 하나뿐인 조각은 **잘린 면이 없다**는 뜻이라 그냥 건너뛴다.
  - **⚠ 이펙트의 로컬 포즈는 항등이어야 한다.** `MeshRenderer` shape은 **그 렌더러의 트랜스폼으로** 메쉬를 샘플링하므로, 이펙트를 따로 옮기면 발생면과 그림이 어긋난다.
  - **⚠ 임팩트의 `VFX_BloodSplash`와 다른 물건이다.** 저쪽은 패턴의 이펙트 큐(§7-4)라 임팩트에 **고정 좌표에서 한 번** 터지고 아무것도 안 따라간다 — 큐를 손봐도 "절단면에서 계속"은 안 된다. 큐가 붙을 앵커(산 적)는 절단 순간 풀로 반납되고 화면에 남는 것은 **다른 오브젝트**(`CorpseView`)이기 때문.
  - **⚠ 소멸이 피를 삼키면 안 된다** — `Dissolve`의 렌더러 수집에서 `ParticleSystemRenderer`를 뺀다(안 빼면 머티리얼이 갈려 피가 통째로 사라진다). 대신 **방출만 멈추고**(`StopEmitting`) 떠 있는 입자는 제 수명대로 사라진다 — 즉시 파기하면 "탄다"가 아니라 "끊겼다"로 읽힌다.
  - **⚠ 풀 반납은 조각을 되돌리기 전이다**(`ResetState` 맨 앞). 안 떼면 이펙트가 시체와 함께 반납돼 **다음 대여가 피를 흘리며 나온다**(§11-8 아웃라인과 같은 함정). 프리팹 참조는 `EnemyDirector`가 필드 하나로 들고 프리웜에 얹는다 — 시체 프리팹 16개에 손으로 꽂으면 재굽기가 날린다.
- 상세: `docs/EnemyDeathClip/` · 각도 매칭: `docs/PatternSliceAngle/`


---

### 12. 점수 · 콤보 (ScoreCombo)
- **`ScoreDirector`**: 채점의 유일 관리 지점. `PatternHandler`·`ChartPlayer`의 기존 이벤트만 구독하는 순수 소비자 — **`PatternHandler`는 채점을 위해 한 줄도 고치지 않는다**(§7의 관심사 분리와 같은 규율). 순수 계산은 `Score/Core/ScoreMath`(asmdef·테스트됨)에 있고, `JudgementResult`를 **모른다**(그 타입은 Assembly-CSharp라 asmdef가 참조할 수 없고, 들어오는 것은 이미 세어진 개수뿐이다).
- **⚠ 놓친 노트는 이벤트로 오지 않는다.** `OnJudged`는 **친 노트**에서만 나고 — 오답 인덱스는 발행조차 안 하며(`AddPattern`의 조기 return) 무입력은 아예 판정되지 않는다. 그래서 셈법은 **패턴이 끝날 때의 뺄셈** 하나다: `Template.AllData.Count − 그 패턴에서 난 OnJudged 수`(`ScoreMath.MissedNotes`).
- **⚠ `OnFocusRingMissedArrival`을 채점에 쓰면 안 된다.** 이름은 "놓쳤다"지만 아니다 — 링의 수축 완료는 **`ExpectedTime` 정각**에 발행되는데 판정 창은 거기서 `goodWindow`만큼 더 열려 있다. **늦은 쪽 Perfect/Good이 전부 이 이벤트를 먼저 발행**하므로, 이걸로 콤보를 끊으면 정확히 친 노트의 절반이 콤보를 끊는다.
- **⚠ 오답 인덱스 자체는 콤보를 안 끊는다.** 끊는 것은 그 결과로 놓치게 된 노트다. 즉시 끊으려면 `OnJudgeTargetFirstMiss`가 이미 있지만 안 쓴다 — 쓰면 패턴이 정체하는 동안 콤보가 두 번 끊긴 것으로 읽힌다.
- **점수는 정규화형이다** — 만점을 세 풀(노트 70% · 패턴 20% · 콤보 10%)로 나눈다. **⚠ 콤보를 노트 점수에 곱하지 않는다**: 곱하면 같은 Perfect라도 곡 후반이 몇 배 비싸져 초반 실수가 싸게 먹히고 "끝에서만 집중"이 유리해진다. 콤보 풀의 분모가 `N(N+1)/2`(= 풀콤보일 때의 값)라 **튜닝 테이블 없이 만점이 정확히 풀콤보에서 나온다.**
- **⚠ 달성도는 비중 합으로 나눈다.** `0.7f + 0.2f + 0.1f`는 부동소수점에서 `0.99999994f`다 — 그대로 쓰면 **퍼펙트인데 만점이 1점 모자라고 최고 등급이 영영 안 나온다.** 분자·분모가 같은 식이라 셋이 전부 1일 때 결과가 정확히 `1f`가 된다(덤으로 비중을 상대값으로 써도 된다).
- **만점은 곡이 소유한다**(`SongChart.maxScore`, 0 이하면 디렉터 기본값). 만점이 "이 곡을 완주하면 몇 점인가"라 채보의 성질이기 때문(`patternPool`과 같은 근거, §5).
- **⚠ 그래서 등급은 절대 점수가 아니라 달성 비율로 가른다** — `D/C/B/A/S/SS/SSS`, 문턱 `0.60/0.77/0.85/0.90/0.95`(이상). 점수로 가르면 만점 2M 곡의 60만 점과 500k 곡의 60만 점이 같은 등급을 받는다. **만점 값을 바꿔도 등급은 안 바뀐다** — 곡별 만점은 화면에 뜨는 숫자만 정한다.
- **⚠ `SSS`는 비율이 아니라 '사실'이다.** `ratio >= 1`로 판정하면 반올림 오차 하나로 영영 안 나올 수 있어, **정수 비교 셋의 논리곱**으로 정한다(Miss 0 · Good 0 · 실패 패턴 0 · 최대콤보 == 총 노트). 최고 등급의 유일성이 구조적으로 보장된다.
- **콤보 단계는 3단계**(문턱 10/30/60)이고 **점수와 무관하다** — 점수는 연속 함수, 단계는 순수 연출이라 섞으면 문턱 근처에서 점수가 계단식으로 튄다. **문턱 배열 길이가 곧 단계 수**라 코드는 3을 모른다. `OnComboTierChanged`는 **단계가 바뀔 때만** 발행한다.
- **화면 연출은 URP Volume 하나다**(`ComboPostFxView`) — 씬의 `ComboPostFxVolume`(priority 10 > `Global Volume` 0)에 붉은 `Vignette` + 약한 `ChromaticAberration`을 얹고 **`weight`만 민다**. **연출 추가 = 프로파일에 오버라이드 한 줄**(카탈로그 규율과 같은 결)이라 클래스 이름이 `ComboVignette`가 아니다.
  - **⚠ 프로파일을 코드가 수정하지 않는다.** `sharedProfile`을 건드리면 에디터에서 에셋에 그대로 저장된다. `weight`만 밀면 그 부류가 원천 소멸한다.
  - **⚠ 기존 `Global Volume`을 안 건드린다.** `weight = 0`이면 평소 화면이 1픽셀도 안 바뀌므로 콤보 시스템을 꺼도 원래 룩이 그대로 돌아온다. `OnDisable`에서 반드시 0으로 되돌린다(안 그러면 화면이 붉게 굳는다).
  - **⚠ 올라갈 때는 감쇠, 끊길 때는 즉시 0.** 감쇠로 사라지면 "서서히 식는다"로 읽혀 **끊겼다는 사실 자체가 안 보인다.**
  - **⚠ 색수차는 약해야 한다.** 타이밍 단서가 포커스 링의 **크기**(§4)인데 색수차는 화면 가장자리일수록 강하게 갈라진다 — 세게 걸면 링 가장자리가 번져 "딱 맞았다"가 흐려진다. **판정을 방해하는 순간 연출이 아니라 손해다.**
- **콤보가 앰비언트 강도를 민다**(`EffectManager.SetIntensity`) — 오래 비어 있던 그 호출자가 여기다.
- **⚠ `GameSession.LastResult`는 아직 읽는 쪽이 없다** — 결과 화면이 미구현이며 그 화면이 붙을 진입점이다(`PlayerHealth.OnDepleted`와 같은 상태, §7-6). 오토플레이(§8)가 같은 파이프라인을 타므로 **디버그로 만점이 나온다** — 기록 저장이 붙을 때 막아야 한다.
- 상세: `docs/ScoreCombo/`

---

## 이벤트 확장 포인트 (`PatternHandler`)
새 연출/시스템은 아래 이벤트만 구독해 붙인다(PatternHandler 본체 수정 없이 확장).
- `OnJudged(JudgementResult, int index)` — 판정 발생.
- `OnPatternComplete(PatternCompletionInfo)` — 패턴 완료(완주/만료). 성공/실패·타이밍·다음 패턴 정보 포함. (EffectManager, CameraDirector가 구독) **`Deadline`을 페이로드가 직접 든다**. 임팩트 시각은 세 페이로드 공통 확장 메서드 `info.ImpactTime()`(= `Deadline + Pattern.ImpactOffset`)으로 얻는다 — 식을 손으로 다시 조립하지 않는다.
- `OnPatternQueued(PatternQueuedInfo)` — 패턴이 **큐에 투입되는 순간**(판정 대상이 되기 훨씬 전). 등장에 시간이 걸리는 연출이 구독한다. `StartTime`(첫 노드 낙하 시작)과 `Deadline`(판정 종료)을 함께 준다. (SliceTargetDirector가 구독) ⚠ **이 시점에 "이 패턴의 적"을 정하면 안 된다** — §11-1 참조.
- `OnAllPatternsCleared` — `ClearAllPatterns()`로 전부 정리된 순간(곡 중단 등). 외부 연출의 잔존물 회수용.
- `OnNodeConnected(int index, Vector3 world)` — 노드가 라인에 연결.
- `OnFocusRingSpawned / OnFocusRingResolved / OnFocusRingMissedArrival` — 포커스 링의 스폰/판정/미입력 수축완료.

## 네임스페이스
- `PatternSpace`: `Pattern`, `PatternData`, `NodeType`, `Point`, `ActivePattern`, `JudgementResult`, `PatternCompletionInfo`, `PatternQueuedInfo`, `ClipAlignment`
- `EnemySpace`: `Attacker`, `EnemyCue`, `EnemyDefinition`, `EnemyDirector`, `EnemyView`
- `SliceSpace`: `SliceSet`, `SlicePlane`, `SliceShape`, `MeshSliceBaker`, `SliceTargetDirector`, `SliceTargetView`, `SlicePiece`
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

---

### 14. 마무리 실루엣 (FinaleSilhouette)
- **곡의 마지막 패턴을 성공으로 끝내면 칼이 닿는 그 프레임에 화면이 뒤집힌다** — 배경은 빨갛게, 배우들만 검은 실루엣으로, 그리고 그 순간이 슬로우모션으로 늘어난다. 레퍼런스는 킬 빌.
- **`FinaleSilhouetteDirector`**(`Effect/`): `CameraDirector`·`HitStopDirector`와 같은 관례 — **기존 이벤트만 구독하는 순수 소비자**이고 판정에 개입하지 않으며 배선이 비면 조용히 비활성된다. `PatternHandler`·`ChartPlayer`·`ScoreDirector`를 한 줄도 안 고친다.
- **"마지막"은 채보를 직접 읽어 센다** — `ChartPlayer.ActiveChart.entries.Length`와 완료 횟수를 비교한다(`ScoreDirector`가 총량을 잡는 것과 같은 방식). ⚠ **`ChartPlayer.OnSongEnded`는 트리거로 못 쓴다** — 그것은 `audioSource.isPlaying`이 false가 돼야 나오므로 **아웃트로 길이만큼 늦다**. 카운터 리셋 지점은 `OnCountdownStarted`다.
- **터지는 시각은 `info.ImpactTime()`** — §6·§7-1·§7-3과 **같은 확장 메서드**다. 그래야 히트스톱·쉐이크·절단·임팩트음과 한 프레임에 붙는다.
- **⚠ 이 클래스만이 `Time.timeScale`을 건드린다.** §7-3의 금지 근거는 "판정·클립 정렬은 `Time.time`인데 채보는 `audioSource.time`으로 돌고 오디오는 timeScale 밖이라 차이가 **영구 누적**된다"인데, **여기서만 그 근거가 성립하지 않는다** — 마지막 노드가 이미 입력됐고 **판정할 패턴이 남아 있지 않아** 누적될 곳이 없다. 예외가 아니라 규칙의 경계다:
  - **`Time.timeScale`은 판정이 남아 있는 동안 못 쓴다. 곡의 마지막 판정이 끝난 뒤에는 쓸 수 있고, 되돌리는 책임만 남는다.**
  - 그래서 **트리거 조건(마지막 엔트리 + `AllCorrect`)이 곧 안전 조건**이다. 실패로 끝나면 시계도 안 건드린다.
  - ⚠ **복구 경로가 셋이다** — 노출 종료 · `OnAllPatternsCleared`(곡 중단) · `OnDisable`. 하나라도 빠지면 **게임이 0.1배속으로 굳는다.**
- **⚠ 마지막 일격의 히트스톱을 억제한다**(`HitStopDirector.SuppressNextMainImpact`). 해제 시각이 `Time.time + hitStopDuration`인데 **`Time.time`은 스케일된 시계**라 0.1초 정지가 **실시간 1초**가 되어 노출 창 전체를 먹는다 — 그러면 슬로우모션이 아니라 **정지 컷**이 된다. 슬로우가 타격감 강조를 대신하므로 여기서는 물러난다. 추가 스톱(`OnExtraImpact`)은 마지막 베기 *이전*이라 안 막는다.
- **⚠ 노출 타이머는 `Time.unscaledDeltaTime`이다.** `deltaTime`으로 재면 0.1배속에서 10배로 늘어난다. **프로젝트 안에서 unscaled 시간을 쓰는 유일한 곳**이다.
- **그림은 `RenderObjects` 피처 2장**(`FinaleBackground` 빨강 / `FinaleActors` 검정, 둘 다 `AfterRenderingOpaques`, 배경 → 배우 순서). §11-8의 레이어 스왑 관용구와 같다. ⚠ **깊이 상태를 건드리지 않는다**(`overrideDepthState = false`) — §11-8이 정확히 그걸로 데였다.
  - **레이어는 `Silhouette`(10)** 이고 배우 서브트리를 통째로 올린다. ⚠ **원래 레이어를 오브젝트별로 기록해서 그 기록으로만 되돌린다** — 일괄로 0을 대입하면 `AmbushOutline`(9)에 있던 기습자가 강조를 잃는다. 적은 풀에서 나오므로 원복이 어긋나면 **다음 대여가 검은 채로 나온다**(§11-8과 같은 함정).
  - **⚠ 절단 조각은 스왑 대상이 아니다.** `SlicePiece.Launch`가 부모에서 떼면서 `pieceLayer`(8)로 올리므로 **이미 전용 레이어에 있다** — `FinaleActors`의 LayerMask가 `Silhouette | SlicePiece`인 이유이고, 그래서 흩어진 조각을 되짚을 필요가 없다(그 목록은 아무도 안 든다).
  - 실루엣 대상은 `EnemyDirector.CollectActorRoots`가 준다 — **적이 어디에 몇이나 있는지는 그 클래스만 안다**는 규율 유지.
  - ⚠ **`SetActive`는 렌더러 <b>에셋</b>의 상태를 바꾼다.** 플레이 종료 시 반드시 false로 되돌린다(안 그러면 에디터 세션에 빨간 화면이 남는다).
- **HUD는 감춘다** — 루트 Canvas가 `ScreenSpaceOverlay`라(§7-5) 그냥 두면 점수판이 실루엣 위에 그대로 남는다. `ScoreHudView.SetHidden(bool)`이 `CanvasGroup` 알파만 민다. ⚠ **오브젝트를 끄지 않는다** — `OnDisable`이 구독을 풀어 감춘 사이의 점수 변화를 놓친다. 감출지 말지는 여전히 부르는 쪽이 정한다(표시 계층은 게임플레이를 모른다).
- **오디오는 안 느려진다**(timeScale 밖). `audioPitchScale` 노브가 있으나 기본 1 — ⚠ 내리면 `ChartPlayer`가 `audioSource.isPlaying`을 보므로 **곡 종료가 그만큼 늦어진다**.
- **`OnFinaleBegan`/`OnFinaleEnded`** — 결과 화면(§12의 `GameSession.LastResult`)·서사 연출이 붙을 진입점. 지금은 구독자가 없다.
- 상세: `docs/FinaleSilhouette/`

### 13. 서사와 배경 프롭 (Story · StoryProps)
- **서사 설계의 진실의 원천은 `docs/Story/`의 다섯 문서다** — `Story_Overview.md`(구조: 층·엔딩·시스템 연결) / `Story_Narrative.md`(인물·장소·사건) / `Story_Script.md`(대사·이미지·샷·자산) / `Story_Music.md`(곡) / `Story_Playthrough.md`(**처음부터 끝까지의 시간 순서 — 전체 흐름을 볼 때 여기부터 읽는다**). **CLAUDE.md는 "어떻게 보이는가", Story 문서는 "왜 그렇게 보이는가"**를 다룬다 — 둘이 겹치면 성질에 맞는 쪽으로 보낸다. (설계 과정의 Research/Plan 문서들은 확정 후 폐기했다.)
- ⚠ **서사의 제1원칙은 `Story_Overview.md` §0이다** — 플레이어는 이곳이 심상세계라는 것을 모르고, **어떤 NPC도 세계를 설명하지 않는다.** 적은 기억이 아니라 세계를 무너뜨리는 괴물이고, 최종 보스의 얼굴은 **참수 순간 한 프레임**에만 드러난다. 연출을 붙일 때 이 셋을 먼저 확인한다.
- **가짜 흑막이 있다** — 「면을 쓴 자」(`Story_Overview.md` §4-3). 4스테이지 씬 **안의 두 번째 무대(4-b)**에서 그를 베지만 아무것도 끝나지 않는다. 그가 요구하는 신규 코드는 **`Encounter.clusterSizeOverride` 필드 하나뿐**이며(한 씬에 `Encounter`가 둘이라 `EnemyDirector.clusterSize`를 씬 단위로 못 둔다), 나머지는 전부 기존 파라미터(`killOnSuccess` 사슬 · `DeathSliceSet` 조각 교체 · 이격 규칙)다. ⚠ **그에게 목소리·발광·아웃라인·전용 스팅어를 주지 않는다.**
- **⚠ 서사 진행과 전투는 같은 공간·같은 흐름 안에 있다**(`Story_Overview.md` §1-1) — 곡 선택 씬과 전투 씬을 오가던 구조는 스토리 모드에서 폐기됐다. **§9가 그 구조를, §11-2가 그 유일한 위험 지점(위치 소유권)을 다룬다.** 여기서는 그것이 서사에 요구하는 것만 적는다:
  - 튜토리얼·결과·수집·엔딩 **씬 4개가 폐기됐다** — 각각 1스테이지 시작 구간 / 무대 위 오버레이 / 오버레이 / 5스테이지 씬 안에서의 연속으로 흡수. **신규 씬은 심상세계 5 + 병실 1**뿐이다.
  - **카시마의 브리핑은 화면이 아니라 자리다** — 무대로 가는 통로 옆에 서서 한 줄 하고 물러난다. ⚠ **그 장소를 처음 열 때만 재생된다**(되돌아가면 없다 — 반복되면 대사가 환경음이 되고 태도 곡선이 죽는다).
  - **⚠ 「등을 보인 사람」의 "다가갈 수 없다"는 근거가 바뀌었다.** 예전 근거(무대 원 8m 밖 = 구조적 도달 불가)는 탐색이 생기며 무효다. 지금은 **다가가면 페이드 아웃하고 다시 안 나타난다** — ⚠ 벽·프롬프트로 막지 않는다(막으면 "게임이 아껴 둔 캐릭터"가 된다).
  - **⚠ 「닫힌 문」과 모든 배경 도상 앞에 상호작용 프롬프트를 두지 않는다.** 이제 걸어가서 들여다볼 수 있지만 **가까이 가도 아무 일이 없어야** 한다. 콜라이더는 이때 처음 필요해지지만 **무대 원 안에는 여전히 두지 않는다**(전투 이동이 물리를 안 본다).
  - **⚠ 탐색 구간에 목표 마커·미니맵·퀘스트 로그가 없다.** 길은 "괴물이 있는 쪽만 무너져 있다"가 정한다.
- **서사가 요구하는 신규 코드는 이것이 전부다**(전부 미구현, 근거는 `Story_Overview.md` §9):
  0. **`PlayerExploreMover` · `EncounterDirector` + `Encounter` · 자리별 최고 등급 저장 · 일렁임 VFX on/off · `GameMode`(Story/FreePlay)** — §9의 개편분. 전부 작은 소비자이고 기존 전투 코드를 수정하지 않는다.
  1. `SongChart.stageIndex` — 적·무대·파편·힌트·배경이 전부 이 한 필드에서 파생된다.
  2. **`DodgeDirector.OnDodgeSucceeded`** — 지금 `DodgeDirector`는 이벤트를 하나도 안 내보낸다. `Succeed()`에서 한 줄 발행.
  3. **`SilentWitnessDirector`** — 위 이벤트만 구독해 「등을 보인 사람」(§Overview 4-2)을 무대 밖에 세웠다 지우는 순수 소비자. **`CameraDirector`·`HitStopDirector`와 같은 관례**(판정에 개입 없음, 배선이 비면 조용히 비활성). ⚠ 그녀는 **적이 아니다** — `EnemyDirector`의 타겟·이격·판정 어디에도 들어가면 안 되고, **소리·발광·아웃라인이 금지**다(아웃라인은 이미 기습자 강조가 쓰고 있어 겹치면 "공격해 오는 것"으로 읽힌다, §11-8).
  4. 보스 참수 후 **머리 조각 교체** — 기존 `SliceSet` 파이프라인(§11)에 인간 머리 프리팹 1개.
  5. 기습 보이스 1줄(`Fire()`에서 `SoundManager.Play` + 쿨다운) · 「마지막 선택」 결과 bool 1개 · 닫힌 문 프롭.
- ⚠ **새 연출은 판정을 방해하면 안 된다** — 그녀의 페이드 인은 포커스 링 수축 중에는 시작하지 않는다(§Overview 4-2). 콤보 포스트FX 색수차 규율(§12)과 같은 근거.
- **아직 런타임 코드가 없다.** 지금 있는 것은 **에디터 전용 파이프라인 셋**(`Assets/02. Scripts/Props/Editor/`, 네임스페이스 `StoryProps.EditorTools`)이고 빌드에 들어가지 않는다. 순서대로 쓴다:
  1. `Tools/Story Props/Setup Materials` — `06. Models/Props`의 FBX 임포트 설정 + URP/Lit 머티리얼(`07. Materials/Props`) 생성·리맵. **멱등**(재실행해도 값만 갱신).
  2. `Tools/Story Props/Extract Prefabs` — FBX 하나에 여러 프롭이 든 경우를 낱개 프리팹(`03. Prefabs/StoryProps`)으로 분해. **이미 있는 프리팹은 안 건드린다**(씬 배치의 참조가 끊기면 안 되므로).
  3. `Tools/Story Props/Build Stage Layout/…` — 스테이지별 배치표를 현재 씬에 세운다(스테이지 1~5 + 트루 엔딩 병실). ⚠ **배치표의 진실의 원천은 `StageLayoutBuilder.cs`의 좌표 테이블**이다(설계 문서는 폐기됨) — 무대가 무엇을 뜻하는지는 `Story_Overview.md` §5, 놓이는 물건의 근거는 `Story_Narrative.md` §3-3에 있다.
- **⚠ 무대 원 안은 반드시 평면이다**(반경 8m, 5스테이지는 3.5m). 플레이어·적 이동이 전부 `transform.position` **대입**이라 경사면을 못 탄다(§11-2) — 경사·계단·단차는 **무대 원 밖 배경으로만** 놓는다.
- 무대 중심은 월드 원점이고 씬의 `Stage` 오브젝트가 거기 있다(§11-2) — 배치표도 그 전제 위에서 좌표를 적는다.
- 상세: `docs/Story/`
