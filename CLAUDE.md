# ProjectA - 리듬액션게임

## 게임 개요
리듬액션게임. 핸드폰 잠금 패턴처럼 생긴 **패턴인풋**(3x3 = 9개 노드 배치) 위에 노드가 떨어지고, 플레이어가 핸드폰 잠금 패턴처럼 노드를 이어 그어서 처리하는 방식.

## 폴더 구조

```
Assets/
├── 01. Scenes/
│   └── DefaultScene.unity       # 메인 개발 씬
├── 02. Scripts/
│   ├── Input/
│   │   ├── InputHandler.cs      # 키보드 1~9 입력 수신, bool[9] inputs 관리
│   │   └── IngameInputs.cs      # Unity Input System 자동생성 래퍼 (수정 금지)
│   ├── Pattern/
│   │   ├── Pattern.cs           # ScriptableObject - 패턴 데이터 및 진행 상태
│   │   └── Handler/
│   │       └── Point.cs         # 개별 노드(포인트) 동작 - 클릭 이벤트 처리
│   └── UI/
│       └── PatternHandler.cs    # 패턴인풋 전체 관리 (9개 Point 보유)
└── IngameInputs.inputactions    # Input System 액션 에셋
```

## 핵심 개념

### 패턴인풋
- 3x3 격자로 배치된 9개의 Point (Point_1 ~ Point_9)
- 포인트 간 중심 간격: **150px**
- 배치 좌표 (AnchoredPosition, 중심 기준):
  - Point_1: (-150, -150) / Point_2: (0, -150) / Point_3: (150, -150)
  - Point_4: (-150, 0)    / Point_5: (0, 0)     / Point_6: (150, 0)
  - Point_7: (-150, 150)  / Point_8: (0, 150)   / Point_9: (150, 150)
- 인덱스는 0~8 (Point_1 = index 0)

#### 판정 영역과 시각 표현의 분리
- **Point 본체**(100x100): Image가 투명(알파 0) + `raycastTarget = true` → **판정(레이캐스트) 영역 전용**
- **자식 `Visual`**(60x60): Knob 스프라이트, `raycastTarget = false` → **시각 표현 전용**. `Point.image` 필드가 이것을 가리키며 판정 색상(Perfect/Good/Miss)이 여기에 표시된다.
- 판정 영역용 본체 Image는 `Point.hitGraphic` 필드가 따로 참조한다. **`image`와 `hitGraphic`은 서로 다른 Image이므로 혼동하지 말 것.**

#### 패턴 가이드라인
- 진행 중인 패턴이 지나갈 Point들을 순서대로 잇는 **반투명 캡슐 경로**를 표시해, 노드가 낙하하는 동안 패턴 모양을 미리 볼 수 있게 한다.
- 구현: `PatternLineRenderer`의 `capsuleMode` 옵션 (별도 클래스 없음). 씬 오브젝트는 `PointBackground/PatternGuideLine`.
- 인스펙터 조정 항목: `lineWidth`(캡슐 두께=원 지름), `normalColor`(채움), `outlineWidth`, `outlineColor`, `capSegments`.
- 표시/소멸: `PatternHandler.SetPattern()`에서 생성(노드 스폰보다 먼저), `HandlePatternComplete()`에서 페이드아웃.
- **렌더 순서 규칙**: 가이드는 `PointBackground`의 **첫 자식**(Point/노드/입력 라인 아래), 실제 입력 라인 `PatternLine`은 **마지막 자식**(맨 위).
- 상세: `docs/PatternGuideLine/`

#### 동적 판정 영역 축소 (조작감)
- 진행 중인 패턴에 **포함되지 않은** Point는 판정 영역이 축소된다 (`PatternHandler.inactiveHitAreaRatio`, 기본 0.5 → 100x100이 실질 50x50).
- 구현: `Point.SetHitAreaRatio(ratio)`가 `hitGraphic.raycastPadding`을 조정 (RectTransform과 자식 Visual은 불변).
- 적용/복구 시점: `PatternHandler.SetPattern()`에서 축소, `HandlePatternComplete()`(패턴 종료) 및 패턴 없는 대기 구간에서 9개 모두 복구.
- `raycastPadding`은 마우스/터치 경로에만 영향을 준다. 키보드 입력(`ForceDown`)과 통과 노드 자동 인식은 레이캐스트를 거치지 않아 영향받지 않는다.
- 상세: `docs/PointHitArea/`

### 주요 클래스

**`InputHandler`** (`Assets/02. Scripts/Input/InputHandler.cs`)
- 키보드 1~9 입력을 `bool[] inputs` (length 9)로 관리
- `IngameInputs.Player` 액션맵을 루프로 바인딩 (Input1~Input9)
- `Inputs` 프로퍼티로 외부 접근

**`Pattern`** (`Assets/02. Scripts/Pattern/Pattern.cs`) - ScriptableObject
- **모양 원본 에셋일 뿐, 진행 상태를 갖지 않는다.** `PatternData[]`(인덱스 0~8의 나열)과 `GetNodeType()`만 제공.
- 진행 상태를 에셋에 두면, 같은 템플릿을 쓰는 두 패턴이 동시에 살아 있을 때 서로의 상태를 덮어쓴다.

**`ActivePattern`** (`Assets/02. Scripts/Pattern/ActivePattern.cs`) - 일반 C# 클래스
- 재생 중인 패턴 하나의 **런타임 상태**: `Template`, `StartTime`, 입력 시각, `CurrentPosition`, `AllCorrect`, `Deadline`
- `StartTime`은 **큐 투입 시각**으로 고정한다. 입력 시각이 이 시점 기준 상대시간이므로, 판정 대상으로 승계될 때 다시 잡으면 타이밍이 밀린다.
- `Deadline` = 마지막 입력 시각 + `goodWindow`. 이 시각을 넘기면 만료 처리(미완료면 `AllCorrect = false`).

#### 패턴 겹침 규칙 (중요)
- 채보는 **다음 패턴의 노드를 이전 패턴이 끝나기 전에 스폰**해야 한다 (스폰 리드타임 ≈1.0초 > 엔트리 간 입력 간격 최소 0.4초). 실제 채보의 95%가 겹친다.
- 반면 **입력(판정) 시각은 겹치지 않는다.** 따라서 `PatternHandler`는 여러 패턴을 **큐**로 들고 있되(연출/스폰 대상), **판정 대상(`JudgeTarget`)은 언제나 선두 하나**다.
- `SetPattern()`은 진행 중인 패턴을 **파기하지 않고 큐에 추가**만 한다. 판정 대상은 선두 패턴이 **완료되거나 만료될 때 같은 프레임에 즉시 승계**된다.
- 낙하 노드는 패턴별로 소유자를 추적하며, 패턴이 완료/만료되면 그 패턴의 노드만 즉시 회수한다.
- 상세: `docs/PatternOverlap/`

**`Point`** (`Assets/02. Scripts/Pattern/Handler/Point.cs`)
- `IPointerDownHandler`, `IPointerUpHandler`, `IPointerEnterHandler` 구현
- **입력을 알리기만 한다** — `OnPointDown(index)` / `OnPointUp(index)` 이벤트 발행. 중복 판정은 하지 않는다.
- 인덱스는 `PatternHandler.Initialize(i, this)`로 **`patternPoints` 배열 순서에서 주입**된다 (게임오브젝트 이름을 파싱하지 않는다).

#### 입력 계층 규칙 (중요)
- **중복 입력 방지의 진실의 원천은 `PatternHandler.connectedIndices` 하나다.** 이미 입력된 Point는 `OnPointPressed`의 가드에서 걸러진다.
  - 이 기록은 **패턴이 끝날 때마다 클리어**되므로, 다음 패턴에서 같은 Point를 다시 쓸 수 있다.
  - 예전엔 `Point.isBusy`가 같은 역할을 중복으로 했는데, 그건 스트로크가 끝나야만 풀려서 **패턴 경계에서 입력이 삼켜졌다**(키보드·마우스 모두). `isBusy`는 제거됐다.
- **`IsDragging`과 `IsMouseDragging`을 구분한다.** `IsDragging`은 키보드 스트로크 중에도 true다.
  - `Point.OnPointerEnter`(지나가며 입력)는 반드시 **`IsMouseDragging`**을 봐야 한다. `IsDragging`을 보면 키보드 입력 중 마우스를 올리기만 해도 입력으로 처리된다.
- 키보드 스트로크는 패턴이 완료/만료될 때 종료된다. **마우스 드래그는 패턴 경계에서 끊지 않는다** — 다음 패턴으로 이어 그을 수 있어야 한다.
- 상세: `docs/KeyboardInputStuck/`

**`PatternHandler`** (`Assets/02. Scripts/UI/PatternHandler.cs`)
- `Point[9]` 배열 보유
- `refIndex`: 현재 눌린 포인트 수 추적
- `AddPattern(int index)`: 미구현 - 패턴 입력 처리 로직 작성 예정

## 네임스페이스
- `PatternSpace`: `Pattern`, `PatternData`, `Point` 클래스가 속함

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
