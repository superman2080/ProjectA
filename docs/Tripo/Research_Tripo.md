# Research — Tripo 연동

Tripo AI(텍스트·이미지 → 3D 메쉬 생성)를 이 프로젝트의 에셋 파이프라인에 붙이기 위한 사전 조사.
실측은 2026-09-19에 실제 API를 호출해 확인했다.

---

## 1. API 실측 결과

### 1-1. 어느 API인가
호스트가 둘이고 문서도 둘로 갈라져 있다.

| 세대 | 베이스 | 상태 |
|---|---|---|
| v2 openapi | `https://api.tripo3d.ai/v2/openapi` | **우리 키가 이쪽에서 인증된다** |
| v3 | `https://openapi.tripo3d.ai/v3` | 같은 키로 401 |

→ **v2를 쓴다.** v3 문서(`developers.tripo3d.ai`)의 필드명(`output.model_url`)은 v2와 다르므로 섞어 읽으면 안 된다.

### 1-2. 인증
```
Authorization: Bearer <TRIPO_API_KEY>
```
키는 `tsk_` 접두, 길이 47. **`tcli_` 접두 토큰은 CLI 전용이라 REST에서 401(`code 1002`)이 난다** — 처음에 이걸로 막혔다.

키는 사용자 환경변수 `TRIPO_API_KEY`에 있다. **⚠ 리포에 절대 쓰지 않는다** — 에디터 툴도 `Environment.GetEnvironmentVariable`로만 읽는다.

### 1-3. 확인된 엔드포인트

**잔액**
```
GET /v2/openapi/user/balance
→ {"code":0,"data":{"balance":0,"frozen":0}}
```

**태스크 생성**
```
POST /v2/openapi/task
Content-Type: application/json
{"type":"text_to_model","prompt":"a simple wooden crate"}
→ {"code":0,"data":{"task_id":"..."}}
```

**태스크 조회(폴링)**
```
GET /v2/openapi/task/{task_id}
→ data.status ∈ queued | running | success | failed | banned | expired
   data.progress (0~100)
   data.output.{model, base_model, pbr_model, rendered_image}
```
⚠ `output`의 정확한 키 조합은 **실제 성공 응답으로 확정해야 한다**(크레딧 0이라 아직 못 봤다). 문서상 v2는 `output.pbr_model`(glb)이 주 산출물이다.

### 1-4. ⚠ 현재 차단 지점 — 크레딧 0
```
POST /v2/openapi/task
→ 403 {"code":2010,"message":"You don't have enough credit to create this task"}
```
**인증·엔드포인트·요청 스키마는 전부 통과했고 잔액에서만 막힌다.** 충전 전에는 실제 메쉬를 한 개도 못 받으므로, 구현은 해도 **검증은 충전 이후**다.

### 1-5. 알려진 함정
- **모델 다운로드 URL은 서명 URL이고 만료가 짧다**(v3 문서 기준 5분). 폴링 성공 즉시 받아야 한다 — URL을 에셋이나 문서에 저장하는 설계는 성립하지 않는다.
- 생성은 비동기다. 폴링 간격·타임아웃이 필요하다.
- PowerShell 5.1에서 `curl.exe`에 JSON을 인라인으로 넘기면 따옴표가 먹혀 `code 1003`(malformed)이 난다. 파일(`-d "@file"`)로 넘겨야 한다. → 툴을 C#(`UnityWebRequest`)으로 짜면 애초에 없는 문제.

---

## 2. 기존 프롭 파이프라인 (§13)

`Assets/02. Scripts/Props/Editor/`, 네임스페이스 `StoryProps.EditorTools`. 전부 에디터 전용.

| 순서 | 메뉴 | 파일 | 하는 일 |
|---|---|---|---|
| 1 | `Tools/Story Props/Setup Materials` | `StoryPropMaterialSetup.cs` | `06. Models/Props`의 모든 `t:Model` 임포터 설정 + 팔레트 머티리얼 24종 생성·리맵 |
| 2 | `Tools/Story Props/Extract Prefabs` | `StoryPropPrefabExtractor.cs` | FBX 안 오브젝트들을 낱개 프리팹(`03. Prefabs/StoryProps`)으로 분해 |
| 3 | `Tools/Story Props/Build Stage Layout/…` | `StageLayoutBuilder.cs` | 스테이지별 좌표 테이블대로 현재 씬에 배치 |

### 2-1. ⚠ 가장 중요한 발견 — 머티리얼 모델이 서로 안 맞는다

`StoryPropMaterialSetup`은 **Blender 산출물 전용 전제** 위에 서 있다:

- 프롭 FBX는 **텍스처 없이 베이스 컬러만** 들고 나온다(파일 주석 §2-0).
- 코드가 `Stone`·`WoodLight`·`Metal` 등 **고정 팔레트 24종**을 만들고,
- `importer.AddRemap(SourceAssetIdentifier(typeof(Material), "Stone"), …)` 으로 **머티리얼 이름이 일치할 때만** 리맵한다.

**Tripo 산출물은 정반대다** — 단일 메쉬 + 베이크된 PBR 텍스처 세트이고, 머티리얼 이름이 팔레트와 일치할 리가 없다. 즉:

- `Setup Materials`를 돌려도 **Tripo 모델에는 리맵이 하나도 안 걸린다**(추가만 되고 매칭 실패 = 무해한 no-op).
- 하지만 `ConfigureImporter`는 **폴더 안 모든 `t:Model`에 무조건 걸린다**. glb도 `t:Model`이므로 `materialLocation = External`·`generateSecondaryUV = true`·`isReadable = false`가 Tripo 모델에도 적용된다.
- ⚠ `isReadable = false`가 특히 중요하다 — **절단 굽기(§11 `MeshSliceBaker`)는 메쉬를 읽어야 한다.** Tripo 모델을 베는 표적으로 쓸 계획이면 이 폴더에 두면 안 된다.

→ **Tripo 산출물은 별도 폴더가 필요하다.** `06. Models/Props` 안에 섞으면 두 파이프라인이 한 폴더를 두고 싸운다.

### 2-2. 룩 정합성
프로젝트 룩은 §12-1(채도 −25 + 화면 전체 포스터라이즈 8단계)이다. 팔레트 프롭은 원래 단색이라 잘 맞지만, **Tripo의 사실적 베이크 텍스처는 포스터라이즈를 통과해도 결이 다르게 보인다.** 배경 프롭으로 쓸 경우 실제로 비교해 봐야 하는 지점이며, 최악의 경우 텍스처를 버리고 팔레트 머티리얼을 손으로 씌우는 편이 나을 수 있다(그러면 Tripo는 **형태 생성기**로만 쓰는 셈).

---

## 3. 용도별 제약

### 3-A. 배경 프롭 (§13)
- 가장 가벼운 경로. glb를 받아 폴더에 떨구면 Unity가 임포트한다.
- 기존 3종 툴 중 **`Extract Prefabs`·`Build Stage Layout`은 그대로 재사용 가능**하고, `Setup Materials`만 안 맞는다(§2-1).
- ⚠ 무대 원(반경 8m) 안은 평면이어야 하고 콜라이더를 두지 않는다(§11-2·§13).

### 3-B. 적·무기 등 베는 표적 (§11)
제약이 훨씬 세다.
- **`isReadable = true`가 필수**(`MeshSliceBaker`가 정점을 읽는다).
- 절단 결과는 **연결 요소별로 분해**되므로, 속이 빈 셸 메쉬·비다양체(non-manifold)면 조각이 이상하게 나온다. Tripo 메쉬 품질을 실측해야 한다.
- 캡(잘린 면)은 **서브메쉬 하나로 병합**되고 머티리얼 슬롯 `M+1` 규칙을 쓴다 → Tripo 머티리얼 슬롯 수를 알아야 한다.
- 적으로 쓰려면 **리깅·Humanoid 아바타**가 필요하다. v2 `text_to_model`은 리깅을 안 준다 — 별도 rig 태스크(v3의 `output.model_urls` 게임레디 변형)가 있는지 확인이 필요하고, 우리 키는 v3에서 401이다.
- ⚠ §11-3의 사망 절단은 **시체 프리팹에 그 적의 스켈레톤 사본**이 들어간다. 적 모델 교체는 `SliceSet` 전체 재굽기를 요구한다.

→ **3-B는 3-A보다 조사·검증 비용이 훨씬 크다.** 먼저 3-A로 파이프라인을 세우고, 메쉬 품질을 눈으로 본 뒤 3-B를 판단하는 것이 순서상 맞다.

---

## 4. 구현 형태 선택지

| 안 | 내용 | 비용 | 평가 |
|---|---|---|---|
| A | **Unity 에디터 창** `Tools/Tripo/Generate` — 프롬프트 입력 → 생성 → 폴링 → glb 다운로드 → 임포트 | C# 1파일, `UnityWebRequest` | 프로젝트 관례(`Props/Editor`)와 일치, 배선 불필요 |
| B | PowerShell 스크립트 + 수동 임포트 | 스크립트 1개 | 더 가볍지만 Unity 밖이라 왕복이 생김 |
| C | MCP 서버 | 신규 서버 작성 | **레지스트리에 Tripo MCP가 없다**(검색 확인). 직접 만들 일은 아니다 |
| D | 런타임 생성 | — | 이 게임에 쓸 곳이 없다 |

**A가 유력하다.** 이유: ①`StoryProps.EditorTools`와 같은 자리·같은 관례 ②PowerShell 따옴표 함정(§1-5)이 원천 소멸 ③임포터 설정을 다운로드 직후에 코드로 걸 수 있어 §2-1의 폴더 충돌을 설계로 피할 수 있다.

---

## 5. 열린 질문 (Plan 전에 답이 필요)

1. **용도가 3-A인가 3-B인가.** 이것이 폴더·임포터 설정·메쉬 요구사항을 전부 가른다.
2. **크레딧 충전 계획.** 없으면 구현해도 검증이 안 된다(§1-4).
3. **텍스처를 쓸 것인가 형태만 쓸 것인가**(§2-2). 후자면 다운로드 후 팔레트 머티리얼을 씌우는 단계가 하나 붙는다.
4. `output`의 실제 키 이름(§1-3) — 첫 성공 응답을 받아야 확정된다.
