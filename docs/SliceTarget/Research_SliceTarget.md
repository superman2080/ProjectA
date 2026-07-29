# Research: 베이는 표적(SliceTarget)

칼 모션에 맞춰 표적 오브젝트를 등장시키고, 패턴 성공 시 베는 타이밍에 맞춰 미리 구운 조각으로 갈라 흩뿌리는 시스템을 붙이기 위한 기존 코드 조사.

---

## 1. 요구사항 요약 (브레인스토밍 확정 사항)

| 항목 | 결정 |
|---|---|
| 표적의 성격 | **순수 연출용**. 판정/점수에 영향 없음 |
| 분할 방식 | **미리 버텍스 단위로 절단해 구운 조각 프리팹**. 런타임 메쉬 컷 아님 |
| 절단면 개수 | 절단 결과가 **2조각이라는 보장이 없다**. 캡(단면) 폐루프도 여러 개일 수 있고, 조각(연결 요소)도 N개일 수 있다 |
| Pattern 지정 방식 | **enum(오브젝트 종류, 절단 형태)로 카탈로그 조회**. 실물 프리팹은 카탈로그가 소유 |
| SliceShape | `Horizontal, Vertical, Diagonal, Cross, Dice` 5종 |
| 표적 개수 | **패턴당 여러 개 가능**(배열). 조각도 배열(N개) |
| 절단 시각 | **마지막 노드 판정 시각**(`ActivePattern.LastNodeTime`) 기준 |
| 등장 방식 | **고정 접근시간 역산** — 스폰 시각 = 임팩트 − approachDuration |
| 이동 | 표적은 **−Z 방향 등속** 이동 |
| 흩뿌림 | **Z 속도는 유지**하고 **X·Y만** 플레이어 반대 방향으로 임펄스 |
| 실패(미스) | 관통시키지 않는다. **플레이어와 충돌 → 이펙트와 함께 소멸** |

---

## 2. 기존 코드 조사

### 2.1 `PatternHandler` (`Assets/02. Scripts/UI/PatternHandler.cs`)

패턴 큐·판정·낙하 노드 스폰의 중심 허브. 외부 시스템은 **이벤트 구독만으로** 붙는 것이 이 프로젝트의 확장 규칙이다(`EffectManager`, `CharacterActionPlayer`가 선례).

현재 공개 이벤트:

| 이벤트 | 발화 시점 |
|---|---|
| `OnJudged(JudgementResult, int)` | 판정 발생 |
| `OnPatternComplete(PatternCompletionInfo)` | 패턴 완료(완주/만료) |
| `OnJudgeTargetBegan(JudgeTargetInfo)` | 패턴이 **판정 대상(선두)이 된 순간** |
| `OnJudgeTargetFirstMiss()` | 판정 대상의 첫 미스 |
| `OnNodeConnected(int, Vector3)` | 노드가 라인에 연결 |
| `OnFallingNodeSpawned / Resolved / MissedArrival` | 낙하 노드 생명주기 |

**핵심 제약 — 표적 스폰 훅이 없다.**
`SetPattern()`(L251)은 패턴을 **큐에 추가**하는 시점이고, 이때 이미 `ActivePattern`이 만들어져 `FirstNodeTime`/`LastNodeTime`이 확정된다(L261). 그러나 이 시점에 발화하는 이벤트는 **큐가 비어 있어 곧바로 판정 대상이 될 때만**(`becomesJudgeTarget`, L290~297) `OnJudgeTargetBegan`이 나간다.

즉 `OnJudgeTargetBegan`은 **직전 패턴이 끝난 뒤**에 발화하므로, 그 시점부터 마지막 노드까지 남은 시간은 패턴 입력 길이(대략 0.4~1.5초)뿐이다. 표적 접근시간(1.5초 안팎)을 확보할 수 없다.

→ **`SetPattern` 시점에 발화하는 이벤트가 필요하다.** (CLAUDE.md의 "패턴 겹침 규칙"상 채보 95%가 겹치므로, 큐 투입은 판정 시작보다 확실히 앞선다.)

### 2.2 `ActivePattern` (`Assets/02. Scripts/Pattern/ActivePattern.cs`)

- `StartTime`은 **큐 투입 시각(SetPattern 호출 시각)으로 고정**된다.
- `FirstNodeTime = StartTime + inputTimes[0]`, `LastNodeTime = StartTime + inputTimes[마지막]`.
- 따라서 `SetPattern` 시점에 이미 **임팩트 절대시각을 알 수 있다** → 스폰 시각 역산이 가능하다.
- `AllCorrect`는 오답 입력 시 `MarkIncorrect()`로 내려간다.

### 2.3 `PatternCompletionInfo` / `JudgeTargetInfo`

- `JudgeTargetInfo`: `Template`, `FirstNodeTime`, `LastNodeTime`.
- `PatternCompletionInfo`: `AllCorrect`, `Template`, `LastNodeTime`, `NextLastNodeTime` — **성공 여부와 어느 패턴인지를 함께 준다.** 완료(만료 포함) 순간에 발화하므로 절단/실패 확정 신호로 그대로 쓸 수 있다.

### 2.4 `CharacterActionPlayer` (`Assets/02. Scripts/Character/CharacterActionPlayer.cs`)

- `OnJudgeTargetBegan`에서 성공 베기 클립 재생을 **예약**하고(`HandleJudgeTargetBegan`), `scheduleStart = max(LastNodeTime − 재생시간, FirstNodeTime)`에 재생을 시작한다(`TryStartPendingSuccess`).
- 즉 **클립의 트림 끝(임팩트 정렬 지점)이 `LastNodeTime`에 오도록** 배속까지 재계산해 정렬한다.
- → 표적 절단 시각을 `LastNodeTime`으로 잡으면 **별도 임팩트 데이터 없이 칼과 자동으로 일치**한다. 이것이 "마지막 노드 기준" 선택의 근거다.
- `OnJudgeTargetFirstMiss`에서 예약을 취소하고 Hit(피격) 클립을 재생한다. **실패 시 플레이어 리액션은 이미 여기서 처리된다** → 표적 시스템은 애니메이션을 건드릴 필요가 없고, 같은 이벤트를 독립적으로 구독하면 된다.

### 2.5 `Pattern` (`Assets/02. Scripts/Pattern/Pattern.cs`)

- `PatternData[]`, `SuccessAnimationClip`, `AnimationStartOffset/Duration/Speed`를 들고 있다.
- 주석에 명시된 원칙: **"모양에 종속된 정적 데이터"는 이 에셋에 두어도 되지만, 진행 상태는 두지 않는다.**
- 표적 지정(enum + 오프셋)은 정적 데이터이므로 이 에셋에 두는 것이 원칙과 충돌하지 않는다.

### 2.6 `Pool` / `IPoolable` (`Assets/02. Scripts/Pool/`)

- `PoolKey` **enum 하나당 프리팹 하나** 매핑(`SerializedDictionary<PoolKey, GameObject>`).
- 표적은 (오브젝트 종류 × 절단 형태 × 조각 N) 조합으로 프리팹 수가 빠르게 늘어난다 → **`PoolKey` 방식에 맞지 않는다.**
- 대안 선례: `EffectManager`가 `Dictionary<GameObject, Queue<CanvasEffectView>>`로 **프리팹별 자체 풀 큐**를 운영한다(L33~35). 같은 패턴을 따르는 것이 적절하다.

### 2.7 `EffectManager` / `EffectCatalog` (`Assets/02. Scripts/Effect/`)

- 카탈로그 = `List<EffectEntry>`(트리거 enum → 프리팹 + 풀 크기), `Awake`에서 `Dictionary`로 인덱싱(`BuildPools`).
- **프리팹이 비면 무연출로 조용히 넘어간다.** 표적 카탈로그도 같은 규칙을 따르면 일관된다.
- 표적 절단/충돌 이펙트도 장기적으로 `EffectTrigger`에 얹을 수 있으나, 이펙트 시스템은 Canvas(2D) 전용이라 **월드 공간 표적 이펙트는 별도 훅**이 필요하다.

### 2.8 에디터 툴 선례 (`Assets/02. Scripts/Character/Editor/`)

- `AnimationClipTrimmerWindow`, `WeaponBoneBakeWindow`/`WeaponBoneBaker` — **EditorWindow + 굽기 로직 분리** 구조가 이미 정착돼 있다.
- 메쉬 절단 툴도 `MeshSliceBakerWindow`(UI) + `MeshSliceBaker`(순수 로직)로 나누면 기존 관례와 맞고, 로직 단독 테스트가 가능하다.

### 2.9 테스트 인프라

- `Assets/02. Scripts/ChartGen/Tests/`에 asmdef 기반 유닛테스트가 존재한다(`ChartGen.Core` 대상).
- 메쉬 절단 로직은 순수 기하 연산이라 **동일한 방식으로 유닛테스트가 가능하다**(정육면체 절단 → 조각 수/버텍스 수/부피 보존 검증).

---

## 3. 미확정 사항 (구현 시 씬에서 확인 필요)

1. **임팩트 지점의 월드 좌표** — 플레이어(캐릭터)와 칼이 지나가는 지점. 씬 구조에 대한 가정을 코드에 박지 않고 **인스펙터에 `impactAnchor: Transform`으로 주입**한다.
2. **표적의 진행 축** — "오브젝트가 −Z로 이동"은 월드 기준으로 확정된 사양. 카메라가 이 축을 바라보는지는 씬 배치 문제이므로 코드는 월드 −Z만 가정한다.
3. **표적 스케일/시야** — approachDuration과 이동 속도의 곱이 스폰 거리이므로, 화면 밖에서 시작하도록 두 값을 인스펙터에서 튜닝한다.

---

## 4. 결론 — 설계에 반영할 제약

- `PatternHandler`에 **`SetPattern` 시점 이벤트 하나를 추가**해야 한다. 그 외 판정 파이프라인은 손대지 않는다.
- 절단 타이밍은 `LastNodeTime`으로 잡으면 `CharacterActionPlayer`의 기존 정렬과 자동으로 맞는다.
- 성공/실패 확정은 `OnPatternComplete`(성공)와 `OnJudgeTargetFirstMiss`(조기 실패)를 함께 구독해 판별한다.
- 풀링은 `Pool`이 아닌 **프리팹별 자체 큐**(EffectManager 방식)로 간다.
- 굽기 툴은 **N조각 · 캡 루프 다수**를 1급 전제로 설계한다.
