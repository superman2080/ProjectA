# Plan — 애니메이션 클립 트리밍 툴 (ClipTrimTool)

근거: `docs/ClipTrimTool/Research_ClipTrimTool.md`

## 개요
`Tools/Animation Clip Trimmer` 에디터 윈도우를 만든다. 클립+미리보기 모델을 지정하면 **창 안에서 프레임 단위로 포즈를 보며 스크럽**하고, 잡은 시각을 **초 단위로 표시**하며, start/duration을 **`Pattern` 에셋에 바로 저장**한다. 구현은 `PreviewRenderUtility` 기반 창 내부 렌더링.

파일: `Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs` (신규, Editor 폴더)

---

## Step 1 — 윈도우 골격 + 입력 필드
- [x] `AnimationClipTrimmerWindow : EditorWindow`, `[MenuItem("Tools/Animation Clip Trimmer")]`.
- [x] 필드: `AnimationClip clip`, `GameObject previewModel`, `Pattern targetPattern`.
- [x] `targetPattern` 지정 시 "Load Clip from Pattern" 버튼으로 `SuccessAnimationClip` 자동 채움.
- [x] 클립/모델 변경 감지 시 프리뷰 인스턴스 재생성(아래 Step 3).

## Step 2 — 타임라인/스크럽 UI
- [x] 슬라이더(0 ~ `clip.length`)로 현재 시각(`currentTime`) 조정.
- [x] 프레임 이동 버튼(◀ 이전 / ▶ 다음): `frame = round(currentTime * clip.frameRate)`, 스텝 = `1/frameRate`.
- [x] Play/Pause 토글: `EditorApplication.update`로 `currentTime` 진행(루프), `Repaint()`.
- [x] 현재 상태 표시: **`time: X.XXX s (frame N / M)`** (M = `round(length*frameRate)`).

## Step 3 — 창 내부 3D 프리뷰 (PreviewRenderUtility)
- [x] `PreviewRenderUtility` 생성/보관. `previewModel` 인스턴스화(`HideFlags.HideAndDontSave`) 후 `AddSingleGO`.
- [x] 매 그리기: `clip.SampleAnimation(previewInstance, currentTime)`로 포즈 적용.
- [x] 렌더러 바운즈로 카메라 프레이밍(타깃=바운즈 중심, 거리=바운즈 크기 기반). 조명 2개 세팅.
- [x] `BeginPreview → Render(true) → EndAndDrawPreview(rect)`. URP 대응(`Render(true)`).
- [x] 프리뷰 rect에서 마우스 드래그=회전(yaw/pitch), 스크롤=줌.
- [x] 모델 미지정 시 안내 HelpBox(“미리보기 모델을 지정하세요”).

## Step 4 — start/duration 마킹 + 초 표시
- [x] "Mark Start" → `startTime = currentTime`, "Mark End" → `endTime = currentTime`.
- [x] 표시: `Start Offset = startTime (s)`, `Duration = endTime - startTime (s)`. `endTime <= startTime`이면 경고 표시.
- [x] 마킹 지점을 타임라인 위에 시각적 마커(초록=start, 빨강=end)로 그린다.
- [x] 현재 시각이 `[start, end]` 구간 안인지 라벨로 표시(“IN SWING” 등) — “프레임마다 어떤 구간인지” 요구 충족.

## Step 5 — Pattern 에셋에 저장
- [x] "Apply to Pattern" 버튼(`targetPattern != null`일 때 활성).
- [x] `SerializedObject(targetPattern)` → `FindProperty("animationStartOffset") = startTime`, `FindProperty("animationDuration") = endTime - startTime`.
- [x] `Undo.RecordObject` + `ApplyModifiedProperties` + `EditorUtility.SetDirty` + `AssetDatabase.SaveAssetIfDirty`.
- [x] 저장 후 현재 값 확인 표시(에셋의 현재 offset/duration 읽어 표기).

## Step 6 — 정리(리소스 누수 방지)
- [x] `OnDisable`에서 프리뷰 인스턴스 파기 + `PreviewRenderUtility.Cleanup()` + `EditorApplication.update` 구독 해제.
- [x] 창 재열기/도메인 리로드 안전(널 가드, 필요한 것 lazy 재생성).

## Step 7 — 컴파일 검증
- [x] `read_console`로 에러/경고 확인.
- [x] 메뉴 `Tools/Animation Clip Trimmer`로 창이 열리는지 확인(스모크).

---

## 검증 시나리오 (구현 후, 사용자 육안)
1. 창 열기 → 캐릭터 프리팹을 previewModel, 베기 클립을 clip에 지정.
2. 슬라이더/프레임 버튼으로 스크럽하며 창 안에서 포즈가 프레임마다 바뀌는지 확인.
3. 휘두름 시작에서 Mark Start, 끝에서 Mark End → Start Offset/Duration 초 표시 확인.
4. targetPattern 지정 후 Apply → Pattern 인스펙터에서 값 반영 확인.
5. `CharacterActionPlayer`로 재생 시 해당 구간만 나오는지 확인.

## 범위 밖
- 클립 자체(.anim) 편집/자르기 없음 — 트림은 재생 시 구간 한정 방식(기존 메커니즘)만 사용.
- 여러 클립 일괄 처리 없음(단일 클립 대상).
