# Plan — 에디터 전용 디버그 입력 시스템 (DebugInput)

근거: `docs/DebugInput/Research_DebugInput.md`

## 개요
`PatternHandler`에 에디터 전용 강제 판정 입력을 추가한다.
1. **수동 강제 입력**: F1/F2/F3 키(및 인스펙터 버튼)로 현재 판정 대상의 다음 노드를 Perfect/Good/Miss로 한 번씩 강제 입력.
2. **자동 Perfect 토글(오토플레이)**: 인스펙터의 토글 버튼이 켜진 상태면, 키 입력 없이도 판정 대상의 각 노드가 도달 타이밍(`ExpectedTime`)에 맞춰 자동으로 Perfect 처리되어 패턴이 저절로 진행.

커스텀 인스펙터로 On/Off 토글, 키 매핑, 마지막 사용 모드 하이라이트, 자동 Perfect 토글 버튼, 수동 Force 버튼 3개를 표시한다. 모든 코드는 `#if UNITY_EDITOR`로 감싼다.

---

## Step 1 — `PatternHandler`에 강제 판정 주입 지점 추가
- [x] `AddPattern(int index)`의 결과 계산 라인을 `#if UNITY_EDITOR` 분기로 변경:
  - 에디터: `JudgementResult result = debugForcedResult ?? Judge(delta);`
  - 그 외(빌드): `JudgementResult result = Judge(delta);`
- [x] `#if UNITY_EDITOR` 필드 추가: `private JudgementResult? debugForcedResult;`
- 주의: 강제 입력은 항상 `ExpectedPointIndex`로만 들어오므로 오답 분기를 타지 않는다. `delta` 계산 코드는 그대로 둔다. 빌드 영향 0.

## Step 2 — `PatternHandler`에 디버그 입력 필드/설정 추가 (`#if UNITY_EDITOR`)
- [x] 기존 Debug 블록(32~45행)에 이어서 필드 추가:
  - `[SerializeField] private bool debugInputEnabled = false;` — 마스터 On/Off (수동·자동 모두 이 스위치에 종속)
  - `[SerializeField] private Key debugPerfectKey = Key.F1;`
  - `[SerializeField] private Key debugGoodKey = Key.F2;`
  - `[SerializeField] private Key debugMissKey = Key.F3;`
  - `[SerializeField] private bool debugAutoPerfect = false;` — **자동 Perfect(오토플레이) 토글 상태**
  - `private JudgementResult? debugLastUsedMode;` — 인스펙터 하이라이트용(마지막 사용 모드)
- [x] `using UnityEngine.InputSystem;` 존재 확인.

## Step 3 — 강제 입력 실행 메서드 (`#if UNITY_EDITOR`)
- [x] `public void DebugForceInput(JudgementResult forced)` 추가:
  1. `if (!debugInputEnabled) return;`
  2. `var target = JudgeTarget; if (target == null) return;`
  3. `int index = target.ExpectedPointIndex;`
  4. `debugForcedResult = forced;`
  5. `debugLastUsedMode = forced;`
  6. `patternPoints[index].ForceDown();` — 기존 파이프라인 전체 재사용
  7. `debugForcedResult = null;` — 즉시 클리어(실제 입력 잔류 영향 방지)
- 커스텀 에디터 버튼 및 자동 Perfect 로직이 공통으로 호출한다. `public`.

## Step 4 — 키 폴링 + 자동 Perfect 처리 (`#if UNITY_EDITOR`)
- [x] `Update()` 맨 앞(기존 로직 앞)에 `#if UNITY_EDITOR` 블록 추가. 나머지 기존 로직은 그대로 흐르게 둔다(`return` 없음).
- [x] `debugInputEnabled && Keyboard.current != null`일 때 **수동 키 폴링**:
  - `Keyboard.current[debugPerfectKey].wasPressedThisFrame` → `DebugForceInput(JudgementResult.Perfect)`
  - Good/Miss도 동일.
- [x] `debugInputEnabled && debugAutoPerfect`일 때 **자동 Perfect 처리**:
  - `var target = JudgeTarget;`
  - `if (target != null && Time.time >= target.ExpectedTime)` → `DebugForceInput(JudgementResult.Perfect)`
  - 도달 타이밍(`ExpectedTime`)에 맞춰 노드를 소비하므로 오토플레이가 자연스럽다. `DebugForceInput`이 `Advance()`까지 처리하므로 다음 프레임엔 다음 노드가 대상이 된다.
  - 한 프레임에 여러 노드가 밀려 있어도(예: 진입 직후) `if`가 한 프레임 한 노드씩 처리 → 자연스러운 진행. (원하면 `while`로 즉시 몰아치기 가능하나 기본은 프레임당 1노드.)

## Step 5 — 커스텀 인스펙터 `PatternHandlerEditor` 신규 작성
- [x] `Assets/02. Scripts/UI/Editor/PatternHandlerEditor.cs` 생성, `[CustomEditor(typeof(PatternHandler))]`.
- [x] `OnInspectorGUI()`:
  1. `DrawDefaultInspector()` — 기존 필드 유지.
  2. 구분선 + "Debug Input (Editor Only)" 헤더.
  3. `debugInputEnabled` SerializedProperty 토글 그리기.
  4. 키 매핑 3개(Perfect/Good/Miss ↔ 키) 표시.
  5. **자동 Perfect 토글 버튼**: `debugAutoPerfect`를 **눌린 상태가 유지되는 토글 버튼**(`GUILayout.Toggle(value, "Auto Perfect", "Button")`)으로 그린다. 켜져 있으면 버튼이 눌린(강조) 상태로 보인다. 값 변경 시 SerializedProperty에 반영.
  6. **마지막 사용 모드 하이라이트**: `debugLastUsedMode`를 읽어 Perfect/Good/Miss 라벨 박스 중 해당 항목을 색상 배경으로 강조(Perfect=파랑, Good=초록, Miss=빨강). `Application.isPlaying` 중 실시간 갱신 위해 `Repaint()`.
  7. **수동 Force 버튼 3개**: "Force Perfect/Good/Miss". `Application.isPlaying && debugInputEnabled`일 때만 활성(`EditorGUI.BeginDisabledGroup`). 클릭 시 `((PatternHandler)target).DebugForceInput(...)`.
- [x] 자동 Perfect 토글 버튼과 Force 버튼은 `debugInputEnabled`가 꺼져 있으면 비활성(또는 무반응)하게 하여 마스터 스위치 일관성 유지.

## Step 6 — 에디터 접근을 위한 최소 노출 (`#if UNITY_EDITOR`)
- [x] `debugLastUsedMode`는 non-serialized 런타임 값이라 SerializedObject로 안 잡힘 → `#if UNITY_EDITOR public JudgementResult? DebugLastUsedMode => debugLastUsedMode;` 프로퍼티 노출.
- [x] `debugInputEnabled`, `debugAutoPerfect`, 키 필드는 `[SerializeField] private`이라 `serializedObject.FindProperty`로 접근 가능(추가 노출 불필요).

## Step 7 — 컴파일 검증
- [x] Unity 콘솔에서 컴파일 에러/경고 확인(`read_console`).
- [x] `#if UNITY_EDITOR` 분기(특히 Step 1 result 라인)가 빌드/에디터 양쪽에서 깨지지 않는지 확인.

---

## 검증 시나리오 (구현 후 수동 확인)
1. Play 모드 진입, 디버그 테스트 패턴 세팅.
2. 인스펙터에서 `debugInputEnabled` 체크.
3. F1 연타 → 노드가 순서대로 Perfect(파랑) 판정되며 패턴 진행, 인스펙터 Perfect 박스 하이라이트.
4. F2/F3도 각각 Good(초록)/Miss(빨강)로 판정되는지 확인.
5. 수동 Force 버튼 클릭으로도 동일 동작 확인.
6. **"Auto Perfect" 토글 버튼 ON → 키 입력 없이도 노드가 도달 타이밍마다 자동으로 Perfect 처리되며 패턴이 저절로 진행. 토글 OFF 시 자동 진행 멈춤.**
7. `debugInputEnabled` 해제 시 키/버튼/자동 Perfect 모두 무반응.

## 범위 밖 (하지 않음)
- 실제 유저 입력(마우스/키보드 1~9) 경로 변경 없음.
- 빌드에 디버그 코드 포함 없음.
- 새 판정 종류/윈도우 추가 없음.
