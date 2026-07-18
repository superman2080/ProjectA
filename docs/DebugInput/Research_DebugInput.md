# Research — 에디터 전용 디버그 입력 시스템 (DebugInput)

## 목표
에디터에서만 동작하는 디버그 입력 시스템. **Perfect / Good / Miss** 세 모드를 각각 단축키(F1/F2/F3)로 발동해, 현재 판정 대상 패턴의 다음 노드를 **원하는 판정 결과로 강제 입력**한다. 인스펙터에서 On/Off와 마지막 사용 모드를 시각적으로 확인하고, 버튼으로도 발동할 수 있게 한다.

## 판정 흐름 분석 (`PatternHandler.cs`)

### 실제 판정이 결정되는 지점 — `AddPattern(int index)` (460~499행)
```
if (index != target.ExpectedPointIndex)   // 오답 인덱스
{
    target.MarkIncorrect();
    lineRenderer.SetCorrectState(false);
    patternPoints[index].SetJudgementColor(JudgementResult.Miss);
    return;                                // Advance 안 함 → 패턴 정체
}

float delta = Mathf.Abs(Time.time - target.ExpectedTime);
JudgementResult result = Judge(delta);     // ← 여기서 Perfect/Good/Miss 결정
...
ReleaseNodeOf(target, position);           // 낙하 노드 회수
OnJudged?.Invoke(result, index);
OnFallingNodeResolved?.Invoke(...);
target.Advance();                          // 다음 노드로 진행
if (target.IsComplete) CompletePattern(target);
```
- 결과는 오직 `delta` 기반 `Judge(delta)`로 나온다 (501~506행).
  - `delta <= perfectWindow(0.05)` → Perfect
  - `delta <= goodWindow(0.10)` → Good
  - 그 외 → Miss
- **정답 인덱스 위에서의 "타이밍 Miss"는 `Advance()`를 타서 패턴이 정상 진행**된다. (오답 인덱스 Miss만 정체)

### 입력이 `AddPattern`까지 오는 경로 — `OnPointPressed(int index)` (301~321행)
```
if (!IsDragging) BeginStroke();
if (connectedIndices.Contains(index)) return;   // 중복 입력 가드(진실의 원천)
// 통과 노드 자동 인식(GetPassThroughIndex) ...
AppendPointToLine(index);                        // 라인 갱신 + OnNodeConnected
AddPattern(index);                               // 판정
```
- Point 입력은 `Point.ForceDown()` → `OnPointDown?.Invoke(index)` → `OnPointPressed`로 들어온다 (Point.cs 63~68행).
- 즉 **`patternPoints[expectedIndex].ForceDown()` 한 번으로 라인/노드/이벤트/Advance/완료까지 기존 파이프라인 전체가 재사용된다.**

### 판정 대상 — `JudgeTarget` (61행)
- `activePatterns[0]` (선두 하나). 없으면 null.
- `target.ExpectedPointIndex` = 지금 눌러야 할 Point 인덱스 (ActivePattern.cs 28행).

## 강제 판정 주입 지점 결정
- `AddPattern`은 항상 **정답 인덱스**로만 디버그 입력이 들어오게 하면(= `target.ExpectedPointIndex` 사용) 오답 분기를 타지 않는다.
- 따라서 `result` 계산 한 줄만 바꾸면 된다:
  `JudgementResult result = debugForcedResult ?? Judge(delta);`
  - `debugForcedResult`(nullable)가 세팅돼 있으면 그 값을, 아니면 기존 `Judge(delta)`를 쓴다.
  - Miss도 정답 노드 위에서 처리되므로 `Advance()`를 타 패턴이 진행된다 (설계 의도와 일치).

## 기존 디버그 코드 관례
- `PatternHandler.cs` 32~45행에 이미 `#if UNITY_EDITOR` 디버그 블록(`debugTestPattern`, `DebugSetTestPattern`, `[ContextMenu]`)이 있다. 동일 관례로 확장한다.

## 입력 읽기 (Input System)
- 프로젝트는 새 Input System 사용 (`using UnityEngine.InputSystem`, `Mouse.current`, `Keyboard.current`).
- 임의 키 감지: `Keyboard.current[Key.F1].wasPressedThisFrame` 형태. `Update()`에서 `#if UNITY_EDITOR` 가드로 폴링.
- 게임 입력키는 1~9 (`InputHandler`), F1~F3과 충돌 없음.

## 인스펙터 커스텀 에디터
- 커스텀 에디터는 Editor 전용 어셈블리에 있어야 한다.
- `PatternHandler`는 asmdef 없는 메인 어셈블리(**Assembly-CSharp**)에 속함. `Assets/02. Scripts/**` 에 프로덕트용 asmdef 없음(`ChartGen`만 별도 asmdef 보유).
- 새 `Editor/` 폴더에 스크립트를 두면 자동으로 **Assembly-CSharp-Editor**로 컴파일되어 `PatternHandler`(Assembly-CSharp) 참조 가능. asmdef 불필요.
- 배치 예정: `Assets/02. Scripts/UI/Editor/PatternHandlerEditor.cs`
- 참고 선례: `Assets/02. Scripts/ChartGen/Editor/PatternChartWindow.cs` (EditorWindow), `Shaders/Editor/*` 등.

## 관련 타입
- `JudgementResult` enum: `Perfect, Good, Miss` (`Assets/02. Scripts/Pattern/JudgementResult.cs`, namespace `PatternSpace`).

## 제약 / 주의사항
- **비침습**: 기존 판정/스폰/드래그 로직을 바꾸지 않는다. `AddPattern`은 결과 계산 한 줄만 수정.
- 디버그 코드는 전부 `#if UNITY_EDITOR`로 감싸 빌드에서 완전히 제외.
- `debugInputEnabled`가 false면 키/버튼 모두 무시.
- 판정 대상(`JudgeTarget`)이 없으면 디버그 입력은 아무 것도 하지 않는다(널 가드).
- 강제 입력은 반드시 `debugForcedResult` 세팅 → `ForceDown()` → 즉시 클리어 순으로, 실제 유저 입력에 잔류 영향이 없게 한다.
