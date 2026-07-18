# Research — 애니메이션 클립 트리밍 툴 (ClipTrimTool)

## 목표
캐릭터 액션(베기) 클립의 **실제 휘두르는 구간**을 시각적으로 찾아, 그 start/duration(초)을 잡아 `Pattern` 에셋의 트림 값에 바로 쓰는 에디터 툴.
- 애니메이션 클립을 선택하면 **프레임마다 포즈를 툴 창 안에서 직접 미리보기**.
- 스크럽 지점의 시간을 **유니티 기준 초(seconds)**로 표시.
- 잡은 start/duration을 `Pattern.AnimationStartOffset`/`AnimationDuration`에 저장.

## 트림 값 소비처 (연동 대상)
- `Assets/02. Scripts/Pattern/Pattern.cs`
  - `AnimationStartOffset`(private `animationStartOffset`), `AnimationDuration`(private `animationDuration`) — 둘 다 `[SerializeField] private float`.
  - 에디터에서 쓰려면 `SerializedObject.FindProperty("animationStartOffset")` / `"animationDuration"`로 접근(프로퍼티는 읽기 전용).
- `Assets/02. Scripts/Character/CharacterActionPlayer.cs` `PlayActionClip`
  - 재생 구간 = 클립의 `[startOffset, startOffset + duration]`. `duration <= 0`이면 클립 끝까지.
  - 이 툴이 잡는 값의 의미와 정확히 일치해야 한다: **startOffset = 휘두름 시작 시각, duration = 휘두름 길이**.

## 클립 미리보기 기술 조사
- 애니메이션 클립은 단독으로 그려지지 않는다 → **대상 모델(리그)에 씌워 샘플링**해야 포즈가 보인다. 따라서 툴은 `AnimationClip` + `미리보기 모델(prefab/GameObject)` 둘을 입력받는다.
- **포즈 샘플링**: `AnimationClip.SampleAnimation(GameObject go, float time)` — 에디터에서 특정 시각의 포즈를 GameObject에 적용(legacy/Mecanim 모두 동작, 휴머노이드는 아바타 매칭 시 적용). 프리뷰 인스턴스에 직접 적용 가능.
- **창 내부 렌더링**: `UnityEditor.PreviewRenderUtility`
  - `AddSingleGO(go)`로 프리뷰 전용 씬에 인스턴스를 넣고, `camera`를 배치, `Render(allowScriptableRenderPipeline: true)` 후 `EndAndDrawPreview(rect)`.
  - URP 프로젝트이므로 `Render(true)`로 SRP 허용, `lights[]`로 조명 세팅 필요.
  - 렌더러 바운즈로 카메라 프레이밍(거리/타깃) 계산, 마우스 드래그로 회전/줌 제공.
- **프레임 개념**: `clip.frameRate`(예: 30), `clip.length`(초). frame = round(time * frameRate), time = frame / frameRate. 표시: "time: X.XXXs (frame N / M)".

## 배치 / 어셈블리
- Pattern(PatternSpace)은 asmdef 없는 메인 어셈블리(Assembly-CSharp). 에디터 코드는 `Editor/` 폴더에 두면 Assembly-CSharp-Editor로 컴파일되어 참조 가능.
- 배치 예정: `Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs`
- 선례: `Assets/02. Scripts/ChartGen/Editor/PatternChartWindow.cs`(EditorWindow), `Assets/02. Scripts/UI/Editor/PatternHandlerEditor.cs`.

## 제약 / 주의
- 휴머노이드 클립은 미리보기 모델의 아바타가 맞아야 포즈가 정상 표시된다(안 맞으면 T포즈/이상). 모델 미지정 시 안내 메시지.
- 프리뷰 인스턴스는 `HideFlags.HideAndDontSave`로 만들고, 창이 닫히거나 모델 변경 시 반드시 파기(누수 방지). `PreviewRenderUtility.Cleanup()`도 `OnDisable`에서 호출.
- 재생(play) 스크럽은 `EditorApplication.update`로 시간을 진행시키고, 창 Repaint를 유도.
- Pattern에 값 쓰기는 `Undo.RecordObject` + `SerializedObject.ApplyModifiedProperties` + `EditorUtility.SetDirty`로 안전하게.
