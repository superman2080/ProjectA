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
- 포인트 간 중심 간격: **200px**
- 배치 좌표 (AnchoredPosition, 중심 기준):
  - Point_1: (-200, -200) / Point_2: (0, -200) / Point_3: (200, -200)
  - Point_4: (-200, 0)    / Point_5: (0, 0)     / Point_6: (200, 0)
  - Point_7: (-200, 200)  / Point_8: (0, 200)   / Point_9: (200, 200)
- 인덱스는 0~8 (Point_1 = index 0)

### 주요 클래스

**`InputHandler`** (`Assets/02. Scripts/Input/InputHandler.cs`)
- 키보드 1~9 입력을 `bool[] inputs` (length 9)로 관리
- `IngameInputs.Player` 액션맵을 루프로 바인딩 (Input1~Input9)
- `Inputs` 프로퍼티로 외부 접근

**`Pattern`** (`Assets/02. Scripts/Pattern/Pattern.cs`) - ScriptableObject
- `PatternData[]` : 인덱스(0~8)와 입력 타이밍(inputTime)의 배열
- `Input()` 호출 시 `OnInput` 이벤트 발행 후 다음 패턴으로 진행
- 마지막 패턴 처리 후 `OnExit` 이벤트 발행

**`Point`** (`Assets/02. Scripts/Pattern/Handler/Point.cs`)
- `IPointerDownHandler`, `IPointerUpHandler` 구현
- Down 시: `OnPointDown(index)` 이벤트 발행, `PatternHandler.AddPattern(index)` 호출, `refIndex++`
- Up 시: `OnPointUp(index)` 이벤트 발행, `refIndex--`
- `isBusy` 플래그로 현재 눌림 상태 관리

**`PatternHandler`** (`Assets/02. Scripts/UI/PatternHandler.cs`)
- `Point[9]` 배열 보유
- `refIndex`: 현재 눌린 포인트 수 추적
- `AddPattern(int index)`: 미구현 - 패턴 입력 처리 로직 작성 예정

## 네임스페이스
- `PatternSpace`: `Pattern`, `PatternData`, `Point` 클래스가 속함

## ⚠️ 개발 파이프라인 (가장 중요 — 반드시 준수)

새로운 기능/설계 작업을 시작할 때는 아래 절차를 예외 없이 따른다. 사용자가 명시적으로 절차를 생략하라고 요청하지 않는 한 절대 건너뛰지 않는다.

### 0단계 — 문서 위치 및 네이밍 규칙
- 모든 Research/Plan 문서는 프로젝트 루트가 아닌 **`/docs`** 폴더에 작성한다.
- 파일명 규칙: `docs/Research_{리서치명}.md`, `docs/Plan_{플랜명}.md` (플랜명/리서치명은 작업 주제를 축약한 케밥 또는 파스칼 케이스, 예: `Plan_PatternLine.md`, `Research_PatternLine.md`)
- Research와 Plan은 동일한 `{이름}`을 공유하여 한 쌍임을 알아볼 수 있도록 한다.

### 1단계 — Research 문서 작성
- 설계를 시작하기 전, 해당 설계와 관련 있는 기존 파일들(스크립트, 씬, 에셋 등)을 분석한다.
- 분석 결과를 `docs/Research_{리서치명}.md` 파일로 정리한다.
- 관련 클래스/메서드/데이터 흐름, 현재 구현 상태, 제약사항 등을 포함한다.

### 2단계 — Plan 문서 작성
- Research 문서를 근거로 `docs/Plan_{플랜명}.md` 파일을 작성한다.
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
1. 설계 요청 → `docs/Research_{이름}.md` 생성
2. Research 기반 → `docs/Plan_{이름}.md` 생성 (단계별로 분리)
3. 사용자가 Plan에 `>>>` 피드백 남김 → Plan 재작성 → 반복
4. "구현해줘" 요청 → 확정된 Plan 기준으로 전 단계 끝까지 구현, 각 단계 완료 여부(`[x]`/`[ ]`)를 Plan 문서에 계속 갱신, 새로운 문제 유발 금지
