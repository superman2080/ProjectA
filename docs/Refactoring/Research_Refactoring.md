# Research — 미사용 코드 · 리팩토링 대상 전수 조사

대상: `Assets/02. Scripts` 전체(84파일 / 22,914줄, `IngameInputs.cs` 자동생성 1,564줄 포함).
방법: ① 심볼 선언 대비 참조 수 스캔(주석·문자열 제거 후), ② private 필드 read/write 분리 카운트,
③ `.meta` GUID를 씬·프리팹·에셋 전체와 대조해 **에셋에서 한 번도 참조되지 않는 스크립트** 추출,
④ 중복 구현 육안 대조.

---

## A. 완전 사망 — 참조 0

### A-1. `Statemachine/` 폴더 전체 (359줄)
| 파일 | 줄 |
|---|---|
| `Statemachine/StateMachine.cs` | 267 |
| `Statemachine/CompositeStateBase.cs` | 50 |
| `Statemachine/StateBase.cs` | 22 |
| `Statemachine/IState.cs` | 20 |

서로만 참조한다. 외부 참조 0, 씬 참조 0. CLAUDE.md §10도 *"현재 게임플레이 루프에 직접 배선돼 있진 않은 범용 유틸"*이라 인정한다.
`StateMachine.CurState`(:14), `OnStateChanged`(:17)는 그 안에서도 참조 0.

### A-2. `Character/WeaponTrailController.cs` (64줄) — **대체된 구현** (확인 완료)
GUID `d92ab2f6d76d8e14c936670a556ae2ed`를 `Assets/**/*.{unity,prefab,asset}` 전체에서 검색 → **자기 `.meta` 외 0건**.
어떤 게임오브젝트에도 붙어 있지 않다.

**이유 확인됨: 트레일 on/off는 이제 애니메이션 클립 이벤트가 한다.** 이 컴포넌트는 그 방식으로 갈아탄 뒤 남은 구현이다 → 삭제.
CLAUDE.md §6이 아직 이 컴포넌트를 "칼날 트레일의 유일한 관리 지점"으로 서술한다 → **문서-구현 불일치, 정정 필요**.

⚠ 삭제해도 `CharacterActionPlayer`의 `RaiseSwingBegan/Ended`는 남겨야 한다 —
`swingActive`가 `ApplyHitStop:737`의 가드로 쓰여, 걷어내면 **히트스톱이 조용히 안 걸린다.**

### A-3. 미사용 public API (선언만 있고 호출자 0)
| 위치 | 심볼 |
|---|---|
| `Enemy/EnemyDirector.cs:1223` | `DuelAnchorPosition()` |
| `Enemy/EnemyView.cs:348` | `ReadyBy(float)` — `IsFreeBy`로 위임만 하는 별칭 |
| `Enemy/EnemyView.cs:87` | `MoveSpeed` (주석은 "디렉터가 같은 값을 본다"지만 디렉터는 자기 `cruiseSpeed`를 쓴다) |
| `Enemy/EnemyDefinition.cs:40` | `DisplayName` |
| `Slice/Core/MeshSliceBaker.cs:70` | `Validate(Mesh)` — 주석에 "기존 호출자 호환용", 그 호출자가 없다 |
| `Slice/CorpseView.cs:36,39` | `Age`, `RootPiece` |
| `Slice/SliceSet.cs:75,76` | `BakedPoseClip`, `BakedPoseTime` (쓰기만: `SliceSet.cs:120-121`) |
| `Slice/SliceTargetView.cs:161` | `ImpactPosition` |
| `UI/DodgePointView.cs:90` | `IsShowing` |
| `Camera/CameraAngleSwitcher.cs:49` | `CurrentIndex` |
| `ChartGen/Editor/PatternTemplateLibrary.cs:43` | `MaxNodeCount` |
| `Slice/Editor/MeshSliceBakerWindow.cs:29` | `const PieceMeshPrefix` |

### A-4. 미사용 필드
| 위치 | 필드 | 비고 |
|---|---|---|
| `Character/CharacterActionPlayer.cs:90` | `[SerializeField] sprintClip` | 툴팁은 "길이를 읽어 배속 역산"이지만 읽는 코드 없음(`quickshiftClip`만 읽는다) |
| `Character/CharacterActionPlayer.cs:203,205` | `playStartTime`, `playDur` | 쓰기만. 주석이 근거로 든 "히트스톱 캐치업"은 '밀기'로 바뀌며 사라짐(:717 주석이 스스로 인정) |
| `Enemy/EnemyView.cs:82` | `[SerializeField] wanderArriveDistance` | 툴팁 3줄짜리 노브인데 읽는 곳 없음 |
| `SongSelectManager.cs:8` | `[SerializeField] availableCharts` | 선택은 버튼이 `SelectChart(chart)`로 직접 넘긴다 |
| `Pattern/Pattern.cs:64,138` | `sliceTarget` / `SliceTarget` | 아래 A-4-1 참조 |
| `Pattern/Pattern.cs:67,141` | `sliceTargetOffset` / `SliceTargetOffset` | 아래 A-4-1 참조 |

#### A-4-1. 패턴 소유 표적은 **구 연출의 잔해다** (추적 완료)

CLAUDE.md §11은 *"`Pattern.sliceTarget`이 이 에셋을 직접 참조한다 / 표적은 패턴당 하나 / 배치는 `sliceTargetOffset`이 정한다"*로 서술하지만, **둘 다 런타임에서 읽히지 않는다.** 표적 시스템이 패턴 소유에서 채보 소유로 이관됐다:

- `SliceTargetDirector.cs:112-115` 주석이 자백한다 — *"이 클래스는 더 이상 PatternHandler 이벤트를 직접 구독하지 않는다 / 공개 API (EnemyDirector가 유일한 호출자)"*
- `EnemyDirector`의 필드명이 `sliceTargetDirector`가 아니라 **`projectileDirector`**(`:35`)
- 유일 호출부 `EnemyDirector.cs:1181` — 넘기는 세트는 `Pattern.sliceTarget`이 아니라 **`EnemyCue.projectile`**(채보 엔트리 소유), offset은 **항상 `Vector2.zero`**, 발사 지점은 `spawnOverride = 쏘는 적의 링 위치`
- 패턴 에셋 전수 확인: `sliceTarget` GUID는 아직 박혀 있으나 **`sliceTargetOffset`은 전부 `{x:0, y:0}`** — 한 번도 저작된 적이 없다

**⚠ 결투 거리와 혼동 금지.** 결투 거리는 별개 필드 `Pattern.duelDistanceOffset`(`:76`, `DuelDistanceOffset:155`)이고 **살아 있다** — `EnemyDirector.DuelDistanceOf:965-974` · `MeshSliceBakerWindow.cs:75` · `PatternChartWindow.cs:848`이 읽는다.

→ 삭제 확정. **인프라(`SliceTargetDirector` 등 481줄)는 그대로 살고 지워지는 것은 배선 한 곳뿐**이라, 나중에 날아오는 표적을 다시 넣을 때는 호출부 3줄이면 된다(`Plan_Refactoring.md` 부록 A).

### A-5. 미사용 enum 값 — `Effect/EffectCatalog.cs`
`EffectTrigger.Parried`(6), `EnemyEvaded`(7), `ProjectileSliced`(9) — `Play(...)` 호출 0.
§7-4 `PatternEffect`(패턴이 큐를 소유)로 역할이 이관되며 남은 껍데기다.

**데이터 위치 확인**: `EffectCatalog.cs`는 enum + struct 정의일 뿐 ScriptableObject가 **아니다.**
매핑은 `EffectManager.catalog`(`EffectManager.cs:24`)에 인라인 직렬화되고, 인스턴스는 **`BattleScene.unity` 한 곳뿐**이다
(GUID `eed5329e7bd7bf942afed144331c57ca` 전체 검색 결과 1건).

씬의 실제 값(`:1099-1120`)은 **0·1·2·3·5뿐 — 6 이상이 하나도 없다.**
따라서 중간 값을 지워 `EnemyKilled`가 8→6으로 밀려도 **가리킬 직렬화 데이터가 없어** 실질 위험이 0이다.
그래도 남는 값에는 명시 정수를 박아 둔다(앞으로의 추가·삭제에 대한 방어). 절차는 `Plan_Refactoring.md` Step 3.

부수 확인: `EnemyKilled`(8)와 `PatternComplete`(4)는 **살아 있는 트리거인데 카탈로그에 프리팹이 없어 오늘 무연출**이다.
특히 `EnemyKilled`는 `EffectManager.HandleEnemyKilled:80`이 구독·호출까지 한다 — 의도인지 잊힌 것인지 확인 필요(Step 3 범위 밖).

### A-6. 구독자 없는 이벤트 (발행만)
| 위치 | 이벤트 |
|---|---|
| `UI/PatternHandler.cs:195` | `OnJudged` — CLAUDE.md는 EffectManager가 구독한다고 적었지만 실제로는 `OnFocusRingResolved`를 쓴다 |
| `UI/PatternHandler.cs:211,215` | `OnFocusRingSpawned`, `OnFocusRingMissedArrival` |
| `UI/PatternLineRenderer.cs:36,38,39` | `OnSegmentPointAdded`, `OnFadeStarted`, `OnFadeCompleted` |
| `PlayerHealth.cs:21,24` | `OnDamaged`, `OnDepleted` |

`PlayerHealth`는 플레이어 프리팹(`03. Prefabs/Char_School_Katana_FullBody-Magica cloth2.prefab`)에 **붙어 있다**.
즉 목숨은 실제로 깎이는데 **0이 돼도 아무 일도 일어나지 않는다**(`OnDepleted` 구독자 0).
→ **삭제 대상 아님.** `OnDepleted`는 향후 **플레이어 사망 연출의 진입점**이다. 미완성이지 사망이 아니다.

`WeaponTrailController` 삭제(A-2) 후에는 `CharacterActionPlayer.OnSwingBegan/OnSwingEnded`(`:140,146`)도 이 목록에 합류한다 — 마찬가지로 공표된 확장 포인트라 유지하고 표기만 한다.

---

## B. 중복 구현

### B-1. 프리팹 풀이 넷 (가장 큰 건)
| 위치 | 형태 |
|---|---|
| `Pool/Pool.cs` | `PoolKey` enum 기반 싱글톤. **실사용 키가 `FocusRing` 하나뿐** |
| `Enemy/EnemyDirector.cs:1700-1770` | `pools`/`maxSizes` + `CreateInstance`/`Rent`/`Release` + 마커 `EnemyPooledInstance`(:1906) |
| `Slice/SliceTargetDirector.cs:350-395` | 위와 **거의 문자 단위로 동일** + 마커 `SlicePooledInstance`(:477) |
| `Effect/PatternEffectDirector.cs:369-430` | 같은 구조의 `CanvasEffectView` 전용 판(`SourcePrefab` 필드를 뷰가 직접 든다) |

세 디렉터 판은 `Rent`/`Release`/`maxSizes` 캡 처리 로직이 같고, 차이는 ① 부모 재지정 여부 ② 마커 컴포넌트 타입 ③ 반환 타입뿐이다.
합계 약 200줄이 중복이며, 마커 컴포넌트 2개(`EnemyPooledInstance`/`SlicePooledInstance`)는 필드 하나짜리 쌍둥이다.

### B-2. 임팩트 시각 식이 6곳에 흩어짐
`Deadline + Pattern.ImpactOffset`(= `LastNodeTime + GoodWindow + ImpactOffset`):

| 위치 | 형태 |
|---|---|
| `Camera/CameraDirector.cs:385-386` | `LastNodeTime + handler.GoodWindow + offset` |
| `Camera/CameraDirector.cs:429` | `info.Deadline + info.Template.ImpactOffset` |
| `HitStop/HitStopDirector.cs:94-95` | `LastNodeTime + handler.GoodWindow + offset` |
| `Character/CharacterActionPlayer.cs:661` | `info.Deadline + info.Template.ImpactOffset` |
| `Enemy/EnemyDirector.cs:1096` | `info.Deadline + (Template?.ImpactOffset ?? 0)` |
| `ChartGen/Editor/PatternChartWindow.cs:883,953` | `GoodWindowApprox = 0.1f` **상수 복제** |

원인은 하나다 — **`PatternCompletionInfo`에만 `Deadline`이 없다**(`JudgeTargetInfo`·`PatternQueuedInfo`에는 있다).
그래서 완료 이벤트 구독자만 `handler.GoodWindow`를 따로 참조하고 식을 손으로 다시 조립한다.
CLAUDE.md도 *"`Deadline`은 페이로드에 없다 — 직접 만든다"*로 이 부채를 명시하고 있다.

### B-3. 페이로드 struct 3종의 명명 불일치
`PatternCompletionInfo.Pattern` vs `JudgeTargetInfo.Template` vs `PatternQueuedInfo.Template` — **같은 것을 두 이름으로 부른다.**

### B-4. `EnforcePieceBudget`
`EnemyDirector.cs:1677`, `SliceTargetDirector.cs:243` — "총 조각 수가 상한을 넘으면 오래된 것부터 회수"라는 같은 알고리즘.
순회 대상 자료구조가 달라 무리한 공통화는 오히려 손해다(우선순위 낮음).

---

## C. 구조 — 비대한 클래스

| 파일 | 줄 | 담고 있는 관심사 |
|---|---|---|
| `Enemy/EnemyDirector.cs` | 1,910 | 무대/무리 배치 · 로스터 · 결투 계획 · 예약&사슬 판정 · 처치 · **시체/파편 회수** · **자체 풀** · **기즈모** |
| `Enemy/EnemyView.cs` | 1,170 | 이동 · 배회 · 시선 · 클립 4종 · 리액션 · 디졸브 · 히트스톱 |
| `Camera/CameraDirector.cs` | 1,042 | 쉐이크 · 펀치 · 줌 · 프레이밍 · 인트로 · 앵글 교체 위임 · 히트스톱 잠금 |
| `Enemy/DodgeDirector.cs` | 667 | 사전 접근 · 텔레그래프 · 원호 회피 · 로그 |

`EnemyDirector`가 유일한 실질적 문제다. 나머지는 **자기 문서가 "층이 겹치지 않는다"는 근거를 명시**하고 있어(§7-2·7-3) 쪼갤 실익이 적다.
`EnemyDirector`에서 떼어낼 수 있는 자족적 덩어리는 셋이다:
- 풀(`CreateInstance`/`Rent`/`Release`/`ReleaseEnemy`, ~90줄) → B-1과 같은 작업
- 잔해 회수(`RecycleCorpses`/`ReleaseCorpse`/`RecycleDissolved`/`RecycleDebris`/`AllSettled`/`RecycleDebrisEntry`/`EnforcePieceBudget`, `:1585-1690` ~105줄)
- 기즈모(`:1775-1900` ~125줄, 이미 `#if UNITY_EDITOR`)

## D. 죽은 분기 — `clusterEnabled == false` 폴백
`ringCount`(`EnemyDirector.cs:41`)는 `RingCapacity`(:404)의 `clusterEnabled ? clusterSize : ringCount` 삼항 한 곳에서만 쓰인다.
§11-6은 무리 배치를 확정 설계로 서술하고 `PickStagePosition`을 "폴백"으로 격하했다.
다만 `EnemyRingTests` 4건이 `PickStagePosition`을 검증하므로 **테스트를 함께 지우지 않으면 제거할 수 없다**.

## E. 잡동사니
- 불필요한 `using`: `Slice/SliceTargetView.cs`(`System`), `Slice/Core/SliceGeometry.cs`(`System.Collections.Generic`), `ChartGen/Tests/*` 4파일(`ChartGen`), `Pattern/Tests/DuetTimelineTests.cs`(`PatternSpace`).
- `CharacterActionPlayer.cs:400-404`: `<summary>` 블록이 **두 개 연속**으로 `HandleDuelScheduled` 위에 쌓여 있다(위쪽은 `SwitchBaseState`용 설명이 남은 것). XML 문서 생성 시 깨진다.
- `UI/PatternHandler.cs:409` `ComputeFallDuration(int pointIndex, float exposureDuration)` — 본문이 `return exposureDuration;` 한 줄. 인자 `pointIndex`는 미사용. 유일 호출자는 굽기 툴.
- `UI/PatternHandler.cs:458` `OnPointReleased(int index)` — 인자 미사용.
- 작업 트리 오염: `Assets/_Recovery/0 (1).unity`(+meta) untracked, 외부 에셋 `.unitypackage.meta` 2건 untracked.

---

## 규모 요약
| 분류 | 삭제 가능(줄) |
|---|---|
| A-1 Statemachine | 359 |
| A-2 WeaponTrailController(배선 복구 or 삭제) | 64 |
| A-3~A-6 미사용 멤버·이벤트·enum | ~60 |
| B-1 풀 중복 | ~150 (공통 클래스 60줄 신설 후 순감) |
| C EnemyDirector 분리 | 순감 0, 1,910 → ~1,600 |
| **합계** | **약 600줄 순삭** |
