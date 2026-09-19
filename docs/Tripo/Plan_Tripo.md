# Plan — Tripo 연동

근거: `docs/Tripo/Research_Tripo.md`. 용도는 **3-A 배경 프롭**으로 잡는다(§3-B는 조사량이 몇 배라 형태를 눈으로 본 뒤에 판단한다 — Research §3 결론).

구현 형태는 **안 A**(Unity 에디터 창). 이유 세 가지는 Research §4에 있다.

---

## 설계 요약

**폴더를 나눈다.** Tripo 산출물은 `Assets/06. Models/TripoProps`에 떨어진다 — `Props`에 섞으면 `StoryPropMaterialSetup.ConfigureImporter`가 모든 `t:Model`에 무조건 걸려 `isReadable = false`·팔레트 리맵 시도가 따라붙는다(Research §2-1).

**임포터 설정은 이 툴이 직접 건다.** 다운로드 직후 `ModelImporter`를 잡아 값을 박으므로 `Setup Materials`를 돌릴 이유가 아예 없다. 팔레트 머티리얼도 안 만든다(Tripo는 텍스처를 들고 온다).

**기존 툴 2·3번은 그대로 재사용한다.** `Extract Prefabs`는 `ModelFolder` 상수가 `Props`로 박혀 있으니 **거기에 폴더 하나를 더 보게 한다**(상수 → 배열, 2줄). `Build Stage Layout`은 프리팹만 보므로 한 줄도 안 바뀐다.

**파일 하나.** `Assets/02. Scripts/Props/Editor/TripoGenerateWindow.cs`, 네임스페이스 `StoryProps.EditorTools`(기존 관례). 에디터 전용이라 빌드에 안 들어간다.

**키는 리포에 안 쓴다.** `Environment.GetEnvironmentVariable("TRIPO_API_KEY")`로만 읽고, 없으면 창에 안내만 띄운다.

---

## 단계

- [ ] **Step 1 — 폴더와 상수**
  `Assets/06. Models/TripoProps` 생성(툴이 없으면 만든다). `StoryPropPrefabExtractor.ModelFolder`를 `ModelFolders` 배열(`Props`, `TripoProps`)로 바꾸고 `FindAssets`에 그대로 넘긴다. **`StoryPropMaterialSetup`은 한 줄도 안 건드린다** — 그쪽 폴더가 안 늘어나는 것이 이 개편의 요점이다.

- [ ] **Step 2 — API 클라이언트(창 안 private 메서드)**
  `UnityWebRequest` 3개. 별도 클래스를 안 만든다 — 호출자가 이 창 하나뿐이다.
  - `GET /v2/openapi/user/balance` — 창 열 때 1회, 잔액 표시.
  - `POST /v2/openapi/task` — `{"type":"text_to_model","prompt":...}` → `task_id`.
  - `GET /v2/openapi/task/{id}` — 폴링.
  `Authorization: Bearer <key>`. JSON은 `JsonUtility`로 파싱(`code`·`data.status`·`data.progress`·`data.output`).

- [ ] **Step 3 — 폴링 루프**
  `EditorApplication.update`에 물려 **2초 간격**, 타임아웃 **5분**. 진행률을 창에 바로 찍는다(`EditorUtility.DisplayProgressBar`를 안 쓴다 — 에디터를 막으면 취소가 안 된다). `failed`/`banned`/`expired`면 상태 문자열을 그대로 찍고 멈춘다.

- [ ] **Step 4 — 다운로드와 임포트**
  성공 응답의 `output`에서 **`pbr_model` → `model` → `base_model` 순으로 처음 있는 URL**을 쓴다(v2 실제 키 조합이 아직 미확인 — Research §1-3. 이 폴백이 그 불확실성을 흡수한다). 서명 URL은 만료가 짧으니 **응답 즉시** 받는다. 파일명은 `{프롬프트 슬러그}_{task_id 앞 8자}.glb`. 쓴 뒤 `AssetDatabase.ImportAsset`.

- [ ] **Step 5 — 임포터 설정**
  임포트 직후 그 에셋의 `ModelImporter`에 건다:
  `useFileScale = true` / `importAnimation = false` / `animationType = None` / `importCameras·importLights = false` / `meshCompression = Off` / `addCollider = false` / `generateSecondaryUV = true` / **`isReadable = false`** / `materialImportMode = ImportStandard` / `materialLocation = External`.
  ⚠ `isReadable`은 3-A 기준이다 — 베는 표적으로 쓸 거면 `true`여야 하고(§11 `MeshSliceBaker`), 그때는 창에 토글을 하나 단다. 지금은 안 단다.

- [ ] **Step 6 — 창 UI**
  `Tools/Tripo/Generate`. 위젯은 넷뿐이다 — 프롬프트 텍스트필드 / `생성` 버튼 / 상태·진행률 라벨 / 잔액 라벨. 배치 생성·히스토리·프리셋은 안 만든다.

- [ ] **Step 7 — 크레딧 이후 검증**
  실제로 한 개 생성해서 확인할 것 넷:
  1. `output`의 실제 키 이름(Step 4의 폴백 중 무엇이 왔나) → Research §1-3 갱신.
  2. glb가 Unity에서 머티리얼·텍스처를 들고 제대로 임포트되나.
  3. 메쉬 삼각형 수·머티리얼 슬롯 수.
  4. **§12-1 필름 룩(채도 −25 + 포스터라이즈 8단계)을 통과한 결과가 팔레트 프롭 옆에 놓였을 때 결이 맞나**(Research §2-2). 안 맞으면 Step 8.

- [ ] **Step 8 — (조건부) 형태만 쓰기**
  Step 7-4가 실패하면 텍스처를 버리고 팔레트 머티리얼을 손으로 씌운다. 그러면 Tripo는 **형태 생성기**로만 쓰는 셈이고 툴에 단계가 하나 붙지 않는다(수동).

---

## 안 하는 것

- **MCP 서버** — 레지스트리에 Tripo가 없다(Research §4-C). 직접 만들 일이 아니다.
- **런타임 생성** — 쓸 곳이 없다.
- **PowerShell 스크립트** — §1-5의 따옴표 함정을 자초할 이유가 없다.
- **`image_to_model`·리깅·리토폴로지** — 3-A에 필요 없다. 3-B로 갈 때 연다.
- **에셋 형태의 생성 히스토리** — 서명 URL은 만료되므로 저장해도 죽은 데이터다(§1-5).
