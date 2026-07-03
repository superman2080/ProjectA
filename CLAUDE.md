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
