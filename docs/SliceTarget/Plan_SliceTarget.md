# Plan: 베이는 표적(SliceTarget)

근거 문서: `docs/SliceTarget/Research_SliceTarget.md`

## 목표

칼 모션에 맞춰 표적 오브젝트가 −Z로 다가오고,
- **패턴 성공** → 마지막 노드 판정 시각(= 칼이 지나가는 순간)에 미리 구운 조각으로 갈라지며 흩뿌려진다.
- **패턴 실패** → 플레이어와 충돌하고 이펙트와 함께 소멸한다.

## 설계 원칙

- 표적은 **연출 전용**. 판정 파이프라인에 개입하지 않는다.
- `PatternHandler`는 **이벤트 하나를 추가**하는 것 외에 수정하지 않는다(관심사 분리, `EffectManager` 선례).
- 절단은 **런타임 메쉬 컷이 아니라 에디터에서 미리 구운 조각 프리팹 교체**다.
- 굽기 툴은 **조각 N개 · 단면 폐루프 다수**를 1급 전제로 한다.
- **패턴이 `SliceSet`을 직접 참조한다.** enum 키 카탈로그를 거치지 않는다 — `Pattern.SuccessAnimationClip`이 클립을 직접 들고 있는 것과 같은 성격이다(모양에 종속된 정적 데이터).
- **물리 엔진을 쓰지 않는다.** 조각의 흩어짐은 `Rigidbody`가 아니라 **트랜스폼 계산**으로 만든다. 표적은 연출 전용이라 충돌 상대(조각끼리·바닥·플레이어)가 없고, 결정론적인 편이 튜닝·재현에 유리하다.

---

## 아키텍처 개요

```
[에디터]  MeshSliceBakerWindow ──> MeshSliceBaker (순수 기하 로직)
                                        │  조각 Mesh 에셋 + 조각 프리팹
                                        ▼
                                   SliceSet (SO: 원본 프리팹 + 조각 프리팹[] + 절단 평면)
                                        ▲ 직접 참조
[런타임]  Pattern.sliceTargets(SliceSpec[]) ────┘
                │
   PatternHandler.OnPatternQueued(신규) ──> SliceTargetDirector ──> SliceTargetView(−Z 등속 이동)
   PatternHandler.OnPatternComplete    ──>        │                        │ 성공: 조각 교체 + XY 임펄스
   PatternHandler.OnJudgeTargetFirstMiss ──>      │                        │ 실패: 충돌 이펙트 + 소멸
```

---

## 단계

### - [x] Step 1: 데이터 계층 정의

새 폴더 `Assets/02. Scripts/Slice/`, 네임스페이스 `SliceSpace`.

**`SliceEnums.cs`**
```csharp
public enum SliceShape { Horizontal, Vertical, Diagonal, Cross, Dice, Custom }
```
`SliceShape`는 **굽기 툴의 평면 프리셋 이름이자 결과물의 라벨**일 뿐이다. 런타임 조회에는 쓰이지 않으므로 같은 모양을 몇 벌이든 구울 수 있고, `Custom`도 개수 제한이 없다. 실제 절단 정보는 `SliceSet.bakedPlanes`에 평면으로 들어 있다.

> **`SliceObjectType`은 두지 않는다.** 표적 종류를 enum으로 열거하는 것은 카탈로그 키를 만들기 위한 장치였는데, 패턴이 `SliceSet`을 직접 참조하면 키 자체가 필요 없다. 표적을 추가할 때 enum을 고치고 재컴파일하는 절차도 함께 사라진다.

**`SliceSet.cs`** (ScriptableObject) — 굽기 툴의 산출물 1건.
- `GameObject originalPrefab` — 온전한 표적.
- `GameObject[] piecePrefabs` — 조각 N개. **2개라고 가정하지 않는다.**
- `Vector3[] pieceLocalOffsets` — 각 조각의 원본 기준 로컬 위치(피벗을 무게중심으로 옮겼기 때문에 필요).
- `Vector3[] pieceScatterDirs` — 굽기 때 계산한 조각별 바깥 방향(무게중심 − 원본 중심). 런타임 흩뿌림의 1차 방향원.
- `SlicePlane[] bakedPlanes` — **이 세트를 구울 때 실제로 사용한 절단 평면**(메쉬 로컬 좌표계, `{Vector3 normal; float distance;}`). 툴이 기존 세트를 열 때 프리셋 대신 이 값을 로드해 **재굽기가 재현 가능**하게 한다. 손으로 그은 획도 여기에 평면으로 환원되어 남는다.
- `SliceShape shape` — 라벨(어떤 프리셋에서 출발했는지). 조회에 쓰지 않는다.
- `int initialPoolSize`, `int maxPoolSize` — 풀 크기. **세트 자신의 속성**이므로 세트가 들고 있는다(조각 수가 곧 인스턴스 수라 세트마다 다르다).

**`SliceSpec.cs`** (`[Serializable]` struct/class) — 패턴이 표적 하나를 지정하는 단위.
- `SliceSet set` — **직접 참조.** 비면 그 표적은 무연출로 건너뛴다.
- `Vector2 spawnOffset` — 임팩트 지점 기준 XY 배치(표적 여러 개를 좌우/상하로 벌린다). **칼 궤적 밖으로 벌리지 않는다** — 아래 제약 참조.
- `float impactOffset` — 마지막 노드 시각 대비 ±초(여러 표적에 시간차를 준다. 기본 0).

> **카탈로그를 두지 않는 이유.** `EffectCatalog`가 성립하는 건 트리거가 `Perfect/Good/Miss`처럼 **본질적으로 enum인 이벤트**여서 매핑 테이블이 필요하기 때문이다. 여기서 절단을 부르는 주체는 이벤트가 아니라 **패턴 에셋**이고, 패턴은 이미 `SuccessAnimationClip`을 직접 들고 있다. 중간에 enum 키를 끼우면 (1) 표적 추가마다 enum 수정·재컴파일, (2) 키 중복을 피하려는 인위적 제약(커스텀 절단 개수 제한), (3) 굽기 → 카탈로그 등록 → 패턴에서 enum 선택이라는 3단 배선이 생긴다. 직접 참조는 이 셋을 모두 없앤다. `SliceSet`이 에셋이므로 다시 굽기만 하면 그것을 참조하는 모든 패턴에 반영되는 이점도 그대로 남는다.

**`Pattern.cs` 수정** — 필드 하나 추가.
```csharp
[Tooltip("이 패턴에서 등장할 베이는 표적들. 비우면 표적 없음.")]
[SerializeField] private SliceSpec[] sliceTargets;
public IReadOnlyList<SliceSpec> SliceTargets => sliceTargets;
```
`SuccessAnimationClip`과 같은 성격의 **모양에 종속된 정적 데이터**이므로 Pattern 에셋에 두는 원칙과 충돌하지 않는다.

---

### - [x] Step 2: 메쉬 절단 코어 (`MeshSliceBaker`)

`Assets/02. Scripts/Slice/Editor/MeshSliceBaker.cs` — **순수 로직, 에디터 API 비의존**(테스트 가능하도록).

입력: `Mesh source`, `Plane[] planes`(로컬 좌표계) → 출력: `List<SlicedPiece>`(`Mesh mesh`, `Vector3 centroid`).

**평면 1장 처리**
1. 삼각형을 세 정점의 평면 부호로 분류.
   - 전부 앞/뒤 → 해당 쪽에 그대로 편입.
   - 걸침 → 엣지 교점 2개를 구해 **1개 삼각형을 3개로 재분할**. position/normal/uv/tangent를 t로 선형 보간(normal은 보간 후 정규화).
2. **단면 캡 생성** — 절단으로 생긴 교차 엣지를 모아 폐루프를 구성한다.
   - **루프는 하나가 아닐 수 있다**(도넛 → 2개, 다리 4개 형태 → 4개). 엣지 인접 그래프를 순회해 **모든 루프를 추출**한다.
   - 루프마다 중심점을 추가해 팬(fan) 삼각형으로 캡을 만든다(볼록 가정이 아니어도 시각적으로 충분).
   - 캡 삼각형의 노멀은 평면 노멀(뒤쪽 조각은 반전), UV는 평면 접선 기저에 투영해 부여.
   - 캡은 **별도 서브메쉬**로 분리해 단면 전용 머티리얼을 물릴 수 있게 한다. **원본 서브메쉬 M개는 그대로 보존하고 캡을 인덱스 M에 덧붙인다**(→ 조각 메쉬의 `subMeshCount = M + 1`). 단면 머티리얼을 지정하지 않는 경우에도 이 구조는 동일하다 — 머티리얼만 원본 것을 물린다(Step 4).
   - 정점 용접 허용오차(`weldEpsilon`, 기본 1e-4)로 루프 연결이 끊기지 않게 한다.
3. **연결 요소 분해** — 앞/뒤 결과를 각각 정점 용접 기준 그래프로 보고 연결 요소별로 쪼갠다. → 결과 조각 수는 **2개가 아니라 N개**.
4. 조각별 무게중심을 구해 정점을 `−centroid` 만큼 평행이동(피벗 정렬)하고, `centroid`를 함께 반환한다.

**평면 여러 장 (획 여러 개) — 교차 처리**

1~3을 **직전 단계의 결과 조각 전체에** 순차 적용한다. 평면은 무한 평면이므로 두 획이 만나면 교차점에서 자연히 갈라진다 — 가로 1획 + 세로 1획 = `┼` → **4조각**, Dice(3장) → 8조각. 별도의 교차 판정 코드는 필요 없고, "이미 잘린 조각을 다시 자른다"는 순차 적용만으로 성립한다.

교차를 제대로 내려면 아래 네 가지를 지킨다.

- **캡도 일반 지오메트리로 취급한다.** 2번째 평면은 1번째가 만든 캡 삼각형을 **그대로 잘라야** 한다. 캡을 절단 대상에서 제외하면 `┼`의 중심부가 뚫린다. → 캡 생성 직후 그 삼각형들을 조각 메쉬에 편입해, 다음 평면 입장에서는 원본 면과 구분되지 않게 한다.
- **캡 서브메쉬는 계속 1개로 병합한다.** 2번째 이후 절단이 만든 캡을 새 서브메쉬로 추가하면 평면 수만큼 서브메쉬가 늘어 **머티리얼 슬롯 규칙(`M+1`)이 깨진다**. 몇 번을 자르든 캡은 전부 **인덱스 M 하나**에 모은다.
- **교차선 용접.** 1번째 캡의 가장자리와 2번째 절단면이 만나는 선에서 T-정션이 생기기 쉽다. `weldEpsilon`(1e-4)을 캡 정점에도 동일하게 적용해 폐루프 추출이 끊기지 않게 한다.
- **적용 순서는 결과에 영향을 주지 않는다.** 평면 절단의 결과는 반평면 교집합이라 순서와 무관하다. → 툴에서 획 순서를 관리하거나 사용자에게 노출할 필요가 없다.

**퇴화 조각 처리** — 두 획이 거의 겹치거나 평행하면 두께 0에 가까운 파편이 생긴다. 조각 부피가 `minPieceVolume`(원본 부피 대비 비율, 기본 0.1%) 미만이면 **폐기하고 Bake 로그에 알린다**. 잔해가 Z-파이팅을 일으키거나 `MeshCollider(convex)` 생성에 실패하는 것을 막는다.

**Shape → 평면 프리셋** (`SliceShapeExtensions.ToPlanes(bounds)`)
| Shape | 평면 |
|---|---|
| Horizontal | normal `+Y`, 중심 통과 |
| Vertical | normal `+X`, 중심 통과 |
| Diagonal | normal `normalize(X+Y)`, 중심 통과 |
| Cross | `+X`, `+Y` 2장 |
| Dice | `+X`, `+Y`, `+Z` 3장 |
| Custom | 프리셋 없음 — 툴에서 그은 획으로 생성 |

프리셋은 **시작점**일 뿐이다. 굽기 툴에서 획을 긋거나 지워 평면을 자유롭게 추가·삭제·조정할 수 있고(Step 4), 프리셋에서 벗어나면 Shape를 `Custom`으로 두면 된다. **평면 장수는 Shape에 묶이지 않는다.**

**거부 조건** — 스킨드 메쉬, 인덱스 포맷 초과, 서브메쉬 토폴로지가 삼각형이 아닌 경우는 명확한 에러 메시지로 굽기를 중단한다.

---

### - [~] Step 3: 절단 코어 유닛테스트

> **상태 — 테스트는 작성 완료, 실행은 미확인.** `Mesh`가 네이티브 타입이라 Unity 밖에서는 실행할 수 없고,
> 구현 시점에 Unity 에디터가 프로젝트를 연 상태(락)라 배치 모드도 불가능했다.
> **사용자가 Test Runner(EditMode)에서 `Slice.Tests`를 돌려 확인해야 한다.**
> 컴파일 자체는 프로젝트의 Unity 6000.3.11f1 어셈블리로 검증했다(런타임+에디터 전체 에러 0).
>
> 아래 목록 중 **에셋 무결성 검증(GUID 보존)은 구현하지 않았다** — 에디터 에셋 조작이라 `Slice.Tests`(순수 코어)와
> 다른 asmdef가 필요하고, 굽기 툴 전체를 태워야 한다. 수동 확인 절차는 `docs/!Guides/Guide_MeshSliceBaker.md`의 재굽기 절 참조.

`Assets/02. Scripts/Slice/Tests/`(asmdef, `ChartGen/Tests` 선례를 따름).
- 정육면체 + 수평 평면 → 조각 2개, 각 조각 부피 ≈ 절반, 캡 루프 1개.
- 정육면체 + Dice(3장) → 조각 8개.
- 도넛(토러스) 형태 메쉬 + 중앙 평면 → **캡 루프 2개**가 나오는지.
- 서로 떨어진 두 덩어리를 한 메쉬에 담고 절단 → **연결 요소 분해로 조각 4개**가 나오는지.
- 절단 결과 전체 정점의 평면 부호가 한쪽으로만 몰리는지(오분류 없음).
- 서브메쉬 M개짜리 원본 절단 → 각 조각의 `subMeshCount == M + 1`이고 **캡이 마지막 인덱스**에 오는지(머티리얼 슬롯 규칙의 전제).

**교차(다중 획) 검증**
- 정육면체 + 가로/세로 평면 2장(`┼`) → **조각 4개**, 각 부피 ≈ 1/4, 조각마다 **캡 루프 2개**(두 절단면).
- 같은 케이스에서 **평면 적용 순서를 바꿔도 결과가 동일**한지(부피 집합·조각 수 비교).
- 같은 케이스에서 각 조각의 `subMeshCount`가 여전히 `M + 1`인지 — **절단할 때마다 캡 서브메쉬가 늘어나지 않는지**(머티리얼 슬롯 규칙 회귀 방지).
- 교차 지점에 **뚫린 구멍이 없는지** — 조각별 폐다면체 검사(모든 엣지가 정확히 2개 삼각형에 공유되는지).
- 거의 겹치는 평면 2장 → 퇴화 조각이 `minPieceVolume`으로 폐기되고, 남은 조각 수가 2개인지.

**에셋 무결성 검증**(에디터 테스트)
- 굽기 → 각 에셋 GUID 기록 → **같은 이름으로 재굽기 → GUID가 전부 동일**한지(참조 끊김 방지의 유일한 안전장치라 테스트로 못박는다).
- 재굽기로 조각 수가 줄어든 경우, 남는 조각 에셋이 정리되는지.

---

### - [x] Step 4: 굽기 툴 윈도우 (`MeshSliceBakerWindow`)

`Assets/02. Scripts/Slice/Editor/MeshSliceBakerWindow.cs` — 메뉴 `Tools/Mesh Slice Baker`.

- 입력 필드: 원본 프리팹/메쉬, **세트 이름**(에셋 파일명), `SliceShape`(프리셋 선택 겸 라벨), **절단 평면 목록**(아래 획 긋기), **단면 머티리얼(선택 — 비우면 원본 머티리얼을 그대로 쓴다)**, 출력 경로.
- **Preview** 버튼: 절단 결과 조각 수/삼각형 수/캡 루프 수와 함께, **최종 머티리얼 슬롯 구성**(캡 서브메쉬 인덱스 / 그 슬롯에 들어갈 머티리얼 이름 + 폴백 여부)을 미리 표시(에셋 생성 없이).
- **Bake** 버튼:
  1. 조각 Mesh를 `.asset`으로 저장 (`{출력경로}/{세트이름}/Piece_{i}.asset`).
  2. 조각 프리팹 생성 — `MeshFilter`, `MeshRenderer`(머티리얼 배열은 아래 규칙), `SlicePiece` 컴포넌트. **콜라이더·Rigidbody는 붙이지 않는다**(물리 미사용).
  3. `SliceSet` 에셋 생성/갱신(원본·조각·오프셋·흩뿌림 방향·`bakedPlanes` 기록). **여기서 끝이다** — 등록할 카탈로그가 없다. 이 세트를 쓰려면 패턴 에셋의 `sliceTargets`에 끌어다 놓는다.
- **조각 정점/삼각형 수 표시** — Preview/Bake 로그에 조각별로 남긴다(렌더 비용 참고용). 물리를 쓰지 않으므로 `MeshCollider(convex)`의 255 정점 상한은 해당 없다.
- 같은 이름으로 다시 구우면 기존 에셋을 **덮어쓰되 GUID를 유지**해 이미 그 세트를 참조하는 **패턴 에셋들의 참조가 끊기지 않게** 한다. (카탈로그가 없으므로 GUID 유지가 유일한 안전장치다 — 반드시 지킨다.)

**GUID 유지는 저절로 되지 않는다.** 같은 경로에 `AssetDatabase.CreateAsset`을 다시 호출하면 기존 에셋이 **삭제 후 재생성**되어 GUID가 바뀌고, 그 세트를 참조하던 모든 패턴의 배선이 끊긴다. 반드시 **제자리 수정**한다.
- `Mesh` — 기존 에셋을 `LoadAssetAtPath`로 불러와 `Clear()` 후 새 데이터를 기입, `EditorUtility.SetDirty` → `SaveAssets`.
- `SliceSet`(SO) — 기존 인스턴스에 `EditorUtility.CopySerialized`로 새 값을 덮어쓴다.
- 프리팹 — 기존 프리팹 에셋을 열어 컴포넌트 값만 갱신(`PrefabUtility.SaveAsPrefabAsset`), 새로 `SaveAsPrefabAsset`으로 경로를 갈아엎지 않는다.
- **조각 수가 줄어드는 재굽기**(획을 지운 경우)는 남는 에셋을 삭제해야 한다. 이때 삭제되는 조각을 참조하던 곳은 없으므로(조각은 `SliceSet`만 참조) 안전하다.
> 조용히 깨지는 유형이라 눈으로는 안 잡힌다. **Step 3 테스트에 GUID 보존 케이스를 포함**한다 — 굽기 → GUID 기록 → 재굽기 → GUID 동일 확인.
- 기존 `SliceSet`을 열면 프리셋이 아니라 **`bakedPlanes`를 로드**한다(재굽기 재현).

#### 절단 획 긋기 (창 내부 프리뷰)

**창 안에서 `PreviewRenderUtility`로 모델을 렌더하고, 그 위에 직선 획을 그어** 절단 평면을 만든다(`AnimationClipTrimmerWindow` 선례를 따름). 획 하나 = 평면 한 장이고, 여러 획이 교차하면 Step 2의 순차 적용 규칙에 따라 `┼` → 4조각으로 갈라진다.

> **씬 뷰가 아니라 창 안에서 하는 이유.** 씬에 임시 프리팹을 꽂는 방식은 ① 사용자 씬을 오염시키고, ② 씬 뷰의 선택·기즈모 조작과 충돌하며(기본 조작을 `AddDefaultControl`로 눌러야 한다), ③ 표적에 맞춰 카메라를 움직이면 사용자가 보던 시점이 틀어진다. 창 내부 프리뷰는 셋 다 없고, **프리뷰 월드가 곧 메쉬 로컬**이라 좌표 변환도 사라진다.

**획 → 평면 변환**
```
드래그 시작점 p0, 끝점 p1 (창 좌표) → 프리뷰 카메라 광선 2개
평면 normal = normalize(cross(p1_world − p0_world, 카메라 forward))
평면 통과점 = p0 광선을 메쉬 bounds 중심 깊이에 투영한 지점
→ 그대로 SlicePlane 1장 (프리뷰는 메쉬를 원점 identity로 그리므로 변환 불필요)
```
프리뷰 카메라는 `Repaint` 중에만 유효하므로, 카메라 포즈(`camPos`/`camRot`/`fov`)를 캐시해 두고 창 좌표 ↔ 월드 변환을 직접 계산한다.

**중요 — 획의 길이와 위치(선분의 양 끝)는 절단 범위를 제한하지 않는다.** 생성되는 것은 무한 평면이므로, 짧게 그어도 메쉬 전체가 갈린다. 획에서 실제로 쓰이는 정보는 **각도와 (시선 방향 기준) 위치**뿐이다. `T`자 모양의 부분 절단은 이 방식으로 만들 수 없다.

**조작**

| 입력 | 동작 |
|---|---|
| **좌드래그** | 획 추가(그리는 동안 노란 선으로 미리보기) |
| **우/중간 드래그** | 카메라 회전 — 다른 각도에서 그으면 **임의 방향 평면**을 만들 수 있다 |
| **휠** | 줌 |
| **Shift + 좌드래그** | 화면 축 스냅(수평/수직/45°) — 정확한 `┼`, `X`를 손떨림 없이 |

- 평면은 메쉬 bounds와 만나는 사각형 단면으로 프리뷰에 겹쳐 표시된다. 선택한 획은 노란색.
- 획 목록에서 선택·삭제하고 **법선/거리를 수치로 미세 조정**한다.
- 프리셋 버튼: Shape를 고르면 해당 프리셋 평면들이 획 목록에 채워진다. 거기서부터 손보면 된다.
- 획을 하나라도 수정하면 Shape가 **`Custom`으로 자동 전환**된다(프리셋과 어긋난 채 `Cross`로 저장되는 혼동 방지).

**Preview 연동** — `Preview`를 누르면 조각 수·퇴화 폐기 수·머티리얼 슬롯 구성을 텍스트로 보여주는 동시에, **프리뷰가 실제 조각들로 바뀌고 분해 슬라이더가 활성화**된다. 조각을 바깥으로 밀어 보며 의도한 분할이 나왔는지 **굽기 전에** 눈으로 확인한다. 획을 수정하면 분해 프리뷰는 초기화된다.
> 조각은 `previewUtil.DrawMesh`로 서브메쉬별로 직접 그린다 — 임시 `GameObject`를 만들지 않으므로 정리할 것도 없다(메쉬만 파기).

#### 머티리얼 슬롯 구성 규칙

조각 메쉬의 서브메쉬는 `원본 M개 + 캡 1개`(인덱스 M)이므로, `MeshRenderer.sharedMaterials`도 **항상 길이 M+1**로 만든다.

| 슬롯 | 내용 |
|---|---|
| `0 … M-1` | 원본 프리팹의 `sharedMaterials[i]`를 순서 그대로 복사 |
| `M` (캡) | **단면 머티리얼이 지정되어 있으면 그것**, **비어 있으면 원본 `sharedMaterials[0]`** |

**단면 머티리얼 미지정 = 정상 경로다.** 굽기를 막지 않고, 캡 슬롯에 원본의 첫 머티리얼을 그대로 물려 굽는다. 표적 대부분이 단색/패턴 재질이라 겉면 재질로도 단면이 충분히 그럴듯하고, 단면 재질을 따로 만들지 않고도 바로 구울 수 있게 하기 위함이다. 배열에 `null`이 들어가는 경로는 이 규칙상 존재하지 않는다(마젠타 에러 셰이더 방지).

- 폴백을 탄 경우 Bake 로그에 **정보 수준**으로 남긴다: `[MeshSliceBaker] 단면 머티리얼 미지정 — 원본 머티리얼 '{이름}'을 캡에 적용했습니다.` 경고가 아니다(의도된 기본 동작).
- 다만 원본이 **멀티 머티리얼**이면 어느 것을 캡에 쓸지는 자명하지 않다. 규칙은 `sharedMaterials[0]` 고정으로 하되, 이 경우에만 **경고**로 알려 사용자가 단면 머티리얼을 명시할지 판단하게 한다: `[MeshSliceBaker] 원본 머티리얼이 {M}개입니다 — 캡에 [0] '{이름}'을 적용했습니다. 의도와 다르면 단면 머티리얼을 지정하세요.`
- 원본 `sharedMaterials`가 **비었거나 [0]이 `null`**인 경우(에셋 파손)는 폴백할 대상이 없으므로 **에러로 굽기를 중단**한다(Step 2의 "거부 조건"과 같은 취급).
- **캡 UV 스케일** — 캡 UV는 평면 투영값이라 오브젝트 크기에 따라 텍셀 밀도가 달라진다. 폴백으로 원본 머티리얼을 쓰면 겉면과 단면의 밀도 차가 눈에 띌 수 있으므로, 굽기 툴에 `capUvScale`(기본 1.0) 필드를 두어 투영값에 곱한다. 겉면 UV 밀도에 맞추는 자동 계산은 하지 않는다(범위 밖).

---

### - [x] Step 5: `PatternHandler`에 큐 투입 이벤트 추가

표적은 접근시간(≈1.5초)이 필요한데 `OnJudgeTargetBegan`은 직전 패턴 종료 후에야 발화해 너무 늦다(Research 2.1).

- `PatternQueuedInfo` (readonly struct): `Template`, `StartTime`, `FirstNodeTime`, `LastNodeTime`, `Deadline`.
  - `Deadline`은 `ActivePattern.Deadline`(= `LastNodeTime + goodWindow`). **이 값을 실어 보내면 `goodWindow`를 따로 공개하지 않아도 된다** — `PatternHandler`의 `goodWindow`는 `private SerializeField`이고(`PatternHandler.cs:15`), 접근자를 새로 뚫는 것보다 이미 계산된 값을 넘기는 쪽이 "이벤트 하나만 추가한다"는 원칙에 맞다.
  - `StartTime`은 `ActivePattern.StartTime`(= 큐 투입 시각 = **첫 노드가 낙하를 시작하는 시각**). `ChartPlayer`가 `spawnTimes[0]`을 기준으로 상대시간을 만들어 넘기므로 둘은 같은 시각이다(`ChartPlayer.cs:98~100`). 표적 접근시간의 상한을 여기서 구한다(Step 7).
- `public event Action<PatternQueuedInfo> OnPatternQueued;`
- `SetPattern()` 끝에서 `activePatterns.Add(active)` **직후** 발화. 기존 `becomesJudgeTarget` 분기와 순서가 얽히지 않도록 그 앞에 둔다.
- 그 외 판정/스폰 로직은 일절 수정하지 않는다.
- `ClearAllPatterns()`는 이미 존재하므로, Director가 곡 중단 시 표적을 정리할 수 있도록 **`OnAllPatternsCleared` 이벤트도 함께 추가**한다(표적 잔존 방지).

---

### - [x] Step 6: 표적 뷰 (`SliceTargetView`, `SlicePiece`)

`Assets/02. Scripts/Slice/SliceTargetView.cs`
- 스폰 시 `Setup(SliceSet set, Vector3 spawnPos, float speed)`.
- `Update`에서 월드 **−Z로 등속 이동**(물리 아닌 트랜스폼 이동 — 임팩트 시각에 정확히 도달해야 하므로).
- `Slice()`
  1. 원본 렌더러 비활성.
  2. 조각 프리팹을 풀에서 꺼내 **`SliceTargetView`의 자식으로** `pieceLocalOffsets` 위치에 배치(= 절단 직전 실루엣과 동일).
  3. 조각마다 흩어짐 파라미터를 정해 `SlicePiece.Scatter(dir, speed, spin)`으로 넘긴다. **부모(`SliceTargetView`)는 계속 −Z로 등속 이동**하므로 조각은 **로컬 XY로만** 흩어지면 된다 → "Z 속도는 유지하고 XY만 밀어낸다"는 규칙이 구조로 표현되고, 별도 승계 코드가 필요 없다.
     - 방향 = `pieceScatterDirs[i]`의 XY 성분을 정규화(표적 중심에서 바깥으로).
     - **XY가 0에 가까운 조각의 폴백** — 다중 획에서는 "절단 평면"이 여럿이라 어느 노멀을 쓸지 자명하지 않다(`┼`의 4조각은 모두 이 경우에 걸릴 수 있다). 평면에 의존하지 않는 결정론적 규칙을 쓴다: **조각 인덱스 `i`로 원을 균등 분할**해 `angle = 360° × i / N`, `dir = (cos, sin, 0)`. 조각이 겹치지 않게 흩어지고, 굽기 결과가 같으면 런타임 결과도 같다.
     - 크기는 `scatterSpeed ± scatterJitter`, 회전은 `scatterSpin`(도/초) 범위의 결정론적 랜덤 축.
- `Crush()` — 실패 시. 이동을 멈추고 충돌 이펙트 훅을 호출한 뒤 **소멸(풀 반납)**한다. 관통시키지 않는다.
- `debrisLifetime` 경과 또는 카메라 뒤 통과 시 조각·본체를 풀에 반납.

`SlicePiece.cs` — 조각 하나의 운동을 **닫힌 식으로** 계산한다(적분 누적 없음 → 프레임률과 무관하게 동일한 궤적).

```csharp
// t = 절단 이후 경과 시간, 전부 부모 로컬 좌표
localPos = spawnLocalPos + dir * speed * t + 0.5f * gravity * t * t;   // gravity=(0,-g,0), 0이면 직선
localRot = spawnLocalRot * Quaternion.AngleAxis(spin * t, spinAxis);
```

- `gravity`는 튜너블(기본 0). 0이면 직선으로 흩어지고, 값을 주면 아래로 처지며 날아간다.
- 감쇠가 필요하면 `speed`에 `exp(-damping * t)`를 곱하는 식으로 확장한다(1차 구현에서는 넣지 않는다).
- 스폰/반납 시 상태 리셋(`t`, 로컬 위치·회전)을 한 곳에 모아 풀 재사용 시 이전 궤적이 새지 않게 한다.

---

### - [x] Step 7: 표적 오케스트레이터 (`SliceTargetDirector`)

`Assets/02. Scripts/Slice/SliceTargetDirector.cs` — 씬에 하나. **표적의 유일한 관리 지점.**

**인스펙터**
- `PatternHandler handler`
- `Transform impactAnchor` — 칼이 지나가는 월드 지점(씬 구조를 코드에 가정하지 않기 위해 주입).
- `float approachDuration = 1.5f` — 표적이 캐릭터 앞까지 오는 데 쓰는 **희망 시간**. 패턴이 짧으면 확보되지 않으므로 아래 규칙으로 클램프된다.
- `float approachSpeed` — 접근 속도. **스폰 거리 = `approachSpeed × 실제 접근시간`.**
- `float scatterSpeed`, `float scatterJitter`, `float scatterSpin`, `Vector3 scatterGravity = (0,0,0)`, `float debrisLifetime`
- `int maxActivePieces = 64` — **동시 활성 조각 상한.** 넘으면 가장 오래된 조각부터 회수한다. 물리를 쓰지 않아 부하는 트랜스폼·렌더링뿐이지만, Dice(8조각) 표적이 겹치면 드로우콜이 몰리므로 상한은 둔다.
- `SliceSet[] prewarmSets` — 씬 시작 시 미리 채울 세트(아래 프리워밍).
- 성공 절단 이펙트 / 실패 충돌 이펙트 프리팹(월드 공간, 비우면 무연출).

**구독**
| 이벤트 | 처리 |
|---|---|
| `OnPatternQueued` | `Template.SliceTargets`를 순회하며 표적 예약 등록.<br>`impactTime = Deadline + spec.impactOffset` ← **판정 종료 시점에 도착**,<br>`actualApproach = min(approachDuration, impactTime − StartTime)` ← **아래 클램프 규칙**,<br>`spawnTime = impactTime − actualApproach`,<br>`spawnPos = impactAnchor.position + spawnOffset(XY) + Z×(approachSpeed × actualApproach)` |
| `OnJudgeTargetFirstMiss` | 현재 판정 대상 패턴의 표적들을 **실패 확정**으로 표시 |
| `OnPatternComplete` | `AllCorrect`면 해당 패턴 표적을 **성공 확정**, 아니면 실패 확정 |
| `OnAllPatternsCleared` | 예약·활성 표적 전부 정리 |

#### 접근시간 클램프 (`actualApproach`)

표적은 패턴이 큐에 들어와야 존재를 알 수 있으므로 **첫 노드보다 먼저 나타날 수 없다.** 따라서 쓸 수 있는 시간의 상한은 **첫 노드 낙하 시작(`StartTime`) ~ 판정 종료(`impactTime` = `Deadline`)** 구간이다. 이 구간은 곧 **그 패턴이 화면에 살아 있는 시간 전체**다.

```
actualApproach = min(approachDuration, impactTime − StartTime)
```

- 패턴 구간이 `approachDuration`보다 **길면** → `approachDuration`을 그대로 쓴다(표적은 패턴 도중에 나타난다).
- 패턴 구간이 **짧으면** → 그 구간 전체를 쓴다(표적이 첫 노드와 동시에 나타나 마지막 노드까지 이동한다).

`spawnTime`이 과거가 되는 경우가 원천적으로 사라지므로, 표적이 화면 중앙에 순간이동하는 일이 없다.

**실측 — `Dreamer_Lv10`, 엔트리 102개의 `impactTime − StartTime`** (`goodWindow` 0.10 포함)

| 노드 수 | 엔트리 | 구간 | `approachDuration=1.5` |
|---|---|---|---|
| 1개 | 25 | 1.141 | 클램프 → 1.141 |
| 2개 | 23 | 1.541 | 1.5 그대로 |
| 3개 | 21 | 1.897 ~ 1.993 | 1.5 그대로 |
| 4개 | 17 | 2.393 | 1.5 그대로 |
| 5개 | 16 | 2.741 | 1.5 그대로 |

**클램프에 걸리는 것은 1노드 패턴 25개(25%)뿐**이고, 그마저 1.141초로 `approachDuration`에 근접한다. 임팩트를 `Deadline`으로 옮기면서 구간이 `goodWindow`만큼 늘어 2노드(1.541초)가 클램프를 벗어났다 — 기본값 1.5를 그대로 두어도 무리가 없다.

**튜닝 주의 — `approachSpeed`는 최소 구간(≈1.0초)을 기준으로 잡는다.** 스폰 거리가 `approachSpeed × actualApproach`이므로 클램프된 표적은 **더 가까이에서 시작한다.** 속도를 `approachDuration`(1.5초) 기준으로 맞추면 1노드 패턴의 표적이 화면 안에서 튀어나온다. 최소 구간에서도 화면 밖에서 시작하도록 속도를 정하면, 긴 패턴은 자연히 더 멀리서 여유 있게 다가온다.
> 이 상한은 `PatternHandler`의 노드 낙하 노출시간(`exposureDuration`)에 딸려 움직인다 — 노드 낙하를 짧게 바꾸면 표적 접근시간의 상한도 같이 줄어든다.

**Update 루프** (`PatternHandler.scheduledSpawns`와 같은 형태)
1. `Time.time >= spawnTime`인 예약을 스폰하고 활성 목록으로 옮긴다.
2. `Time.time >= impactTime`인 활성 표적을 처리 — 성공 확정이면 `Slice()`, 실패 확정이면 `Crush()`.
   - `impactTime`은 `Deadline` 기준이므로 이 시점엔 **성패가 이미 확정되어 있다**(아래). 확정이 없으면 그 프레임만 정지해 기다린다. 표적이 플레이어를 지나쳐 살아남는 상태를 만들지 않는다.
3. 수명 만료 표적을 회수한다.

#### 임팩트 시각은 `Deadline`이다 (판정 종료 시점)

**표적이 판정 위치에 닿는 순간 = 그 패턴의 판정이 끝나는 순간**으로 맞춘다.

```
impactTime = Deadline + spec.impactOffset       // Deadline = LastNodeTime + goodWindow
```

`LastNodeTime`이 아니라 `Deadline`을 쓰는 이유: 마지막 노드를 `goodWindow`(0.10초) 안에 늦게 눌러도 **Good 성공**이고(`PatternHandler.cs:584`), 그 경우 `OnPatternComplete`는 `LastNodeTime` **이후에** 발화한다. 만료(실패) 경로도 `Deadline`에 확정된다(`ActivePattern.cs:51`). 즉 **`Deadline` 이전에는 성패가 아직 열려 있다.**

도착을 `Deadline`에 맞추면 이 문제가 통째로 사라진다.

- 표적이 다가오는 **내내 판정 구간**이고, 도착하는 순간 성패는 **이미 확정되어 있다.**
- 확정을 기다리는 유예 로직이 필요 없다. 도착 시점에 결과를 그대로 읽어 `Slice()` / `Crush()`.
- 늦은 Good으로 성공해도 표적이 판정 지점을 지나쳐 갈라지는 일이 없다.

> **한 프레임 가드만 남긴다.** `Deadline`이 걸린 그 프레임에서 Director의 `Update`가 `PatternHandler`보다 먼저 돌면 확정이 아직 없을 수 있다. 이때는 실패로 넘기지 말고 **확정이 올 때까지 도착 상태로 정지**시킨다(이미 도착해 있으므로 눈에 띄지 않는다). 실무상 한 프레임이다.

**소유자 추적** — 예약/활성 표적은 `Pattern` 템플릿이 아니라 **패턴 인스턴스 단위 토큰**으로 묶는다. 같은 템플릿이 겹쳐 재생될 수 있기 때문이다(CLAUDE.md 패턴 겹침 규칙). `OnPatternQueued`에서 발급한 토큰을 완료 이벤트와 매칭한다.
> `PatternCompletionInfo`는 `Template`만 주므로, 매칭은 **큐 순서(FIFO)** 로 한다 — 판정 대상은 언제나 선두 하나이고 완료도 선두부터 순서대로 일어나므로 안전하다.

**풀링** — `Pool`(PoolKey 단일 매핑)은 표적 프리팹 수 증가에 맞지 않는다. `EffectManager`처럼 `Dictionary<GameObject, Queue<...>>`로 **프리팹별 자체 큐**를 Director가 운영한다.

**프리워밍은 인스펙터 `prewarmSets`로 지정한다.** `Start`에서 이 목록의 세트를 각자의 `initialPoolSize`만큼 미리 만든다.
> 채보(`GameSession.SelectedChart` → `entries[].template.SliceTargets`)를 훑어 자동 수집하는 안도 검토했으나 **채택하지 않는다.** Director가 채보 시스템(`GameSession`/`ChartPlayer`/`SongChart`)에 의존하게 되어, "표적은 `PatternHandler` 이벤트만 구독한다"는 설계 원칙이 깨진다. 프리워밍은 첫 히치를 줄이는 최적화일 뿐이고(풀은 부족하면 늘어난다), 그 이득이 의존성을 새로 만들 만큼 크지 않다.

---

### - [ ] Step 8: 씬 배선 및 튜닝

> **상태 — 미착수(사용자 작업 필요).** 이 단계는 Unity 에디터 안에서만 할 수 있다:
> 신규 스크립트의 `.meta`(GUID)가 아직 생성되지 않아 씬 파일을 직접 편집할 수도 없고,
> 표적으로 쓸 원본 프리팹(Barrel 등)도 아직 없다. Unity가 스크립트를 임포트한 뒤
> `docs/!Guides/Guide_MeshSliceBaker.md`의 6·7절 순서대로 진행하면 된다.

- `DefaultScene`에 `SliceTargetDirector` 배치, `PatternHandler`·`impactAnchor` 연결.
- 표적 1종(예: Barrel)으로 `SliceSet` 2벌(Horizontal, Cross)을 구워 **패턴 에셋 2개의 `sliceTargets`에 각각 배선**한 뒤 실제 곡에서 확인. **`Cross`(교차 4분할)를 초기 검증에 포함**해 다중 획 경로를 씬에서 일찍 확인한다.
- **표적 1개 기준으로 먼저 맞춘다**(`spawnOffset = 0`). 다중 표적은 그 뒤에 폭을 조금씩 늘려가며 확인.
- 튜닝 순서: ① `approachSpeed`를 **최소 구간(≈1.14초)에서도 화면 밖에서 시작**하도록 잡기 → ② 임팩트 시각과 칼 궤적의 시각적 일치 확인 → ③ `scatterSpeed`/`scatterSpin`/`scatterGravity`로 흩뿌림 감 조정 → ④ `debrisLifetime`·`maxActivePieces`로 잔해 정리.

---

### - [x] Step 9: 문서화

- `docs/!Guides/Guide_MeshSliceBaker.md` — 굽기 툴 사용법(원본 준비 → **획 긋기** → Bake → **패턴 에셋에 `SliceSet` 배선**), 조각이 N개로 나오는 경우, **단면 머티리얼 설정(미지정 시 원본 머티리얼 폴백 · 멀티 머티리얼 원본에서의 주의 · `capUvScale`)**.
- `CLAUDE.md`에 "11. 베이는 표적(Slice)" 절과 `Assets/02. Scripts/Slice/` 폴더 구조, `OnPatternQueued`/`OnAllPatternsCleared` 확장 포인트를 추가.

---

## 알려진 제약 (인지하고 받아들이는 것)

### 표적은 패턴 템플릿에 묶인다 — 곡·구간별로 다르게 못 준다

`sliceTargets`가 `Pattern`에 있으므로, 같은 템플릿을 쓰는 모든 자리에서 **같은 표적이 나온다.** `Dreamer_Lv10` 실측:

```
엔트리 102개 / 고유 템플릿 10개 / 최다 사용 템플릿 25회
```

즉 한 곡에서 같은 표적이 **25번 반복**되고, 그 템플릿을 쓰는 **다른 곡에서도 동일**하다. 표적 연출의 다양성이 사실상 **템플릿 수(10개)에 묶인다.**

받아들이는 이유: `SuccessAnimationClip`이 이미 같은 구조이고(패턴별 베기 클립도 동일하게 반복된다), 표적을 곡별로 다르게 하려면 채보 데이터 모델에 손을 대야 해서 이번 범위를 크게 넘는다. **필요해지면 `SongChartEntry`에 오버라이드 필드를 더하는 경로가 열려 있다** — 그때 `SliceSpec[]`을 엔트리에서 읽고 없으면 템플릿 값을 쓰는 식으로 확장하면 되고, 지금 구조를 버릴 필요가 없다.

### 표적을 벌릴 수 있는 범위는 칼 궤적 안이다

절단은 `impactAnchor`(칼이 지나가는 지점)에서 일어나는데, `spawnOffset`으로 표적을 멀리 벌리면 **칼이 지나가지 않은 위치의 표적이 저절로 갈라진다.**

- `spawnOffset`은 **칼 궤적 폭 안의 미세 배치**로만 쓴다(겹침을 피해 살짝 어긋놓는 용도).
- 화면 좌우로 크게 벌리는 다중 표적 연출은 **1차 범위에서 제외**한다. 하려면 표적별로 칼 모션을 매칭해야 하는데, 그건 표적 시스템이 아니라 캐릭터 액션 쪽 설계다.
- Step 8 튜닝에서 **표적 1개 기준으로 먼저 맞추고**, 다중 표적은 그 뒤에 폭을 조금씩 늘려가며 확인한다.

---

## 범위 밖 (하지 않는 것)

- 런타임 메쉬 절단.
- **곡선·꺾인 획으로 자르기.** 절단 기준이 평면이 아니라 곡면(ruled surface)이 되면 삼각형 평면 부호 분류, 캡의 평면 투영 UV, 평면 노멀, 폐루프 팬 삼각형이 모두 성립하지 않아 사실상 메쉬 부울에 가까워진다. **획은 직선만** 지원하고, 꺾인 모양은 직선 획 여러 개로 표현한다.
- 획의 길이로 절단 범위를 제한하는 **부분 절단(`T`자)**. 획은 무한 평면으로 환원된다.
- **조각의 물리 상호작용** — 조각끼리 충돌, 바닥에 떨어져 구르기, 플레이어와의 물리 충돌. 조각은 콜라이더 없이 정해진 궤적으로 날아가다 회수된다.
- 표적을 판정·점수에 연결하는 것(연출 전용).
- 스킨드 메쉬(캐릭터) 절단.
- 표적별 개별 접근 속도(고정 `approachDuration` 역산으로 통일).
