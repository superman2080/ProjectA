# Plan_PatternSliceAngle.md — 스윙이 절단 각도를 고르게 한다

> 기반 문서: `Research_PatternSliceAngle.md`

## 설계 요약

**패턴은 `SliceSet`을 참조하지 않는다. 평면 하나를 든다.**

`Pattern`이 세트를 직접 들면 모델 불일치가 표현 가능해진다(Research §2). 그래서 패턴이 드는 것은 **"이 스윙이 만드는 절단 평면"** 하나이고, 실제 세트는 여전히 `EnemyDefinition`이 소유한다. 런타임은 **가장 가까운 평면으로 구워진 세트**를 고른다.

```
Pattern.bladePlane  ─────┐
                         ├─► SliceMatch.Pick  ──► SliceSet
EnemyDefinition          │
  └ deathSliceSets[] ────┘     (각 세트가 BakedBladePlane을 든다)
```

### 이 선택이 절차를 줄이는 이유

세트를 **키(인덱스·enum)로 고르면** 패턴마다 키를 정해야 하고, 새 각도가 나올 때마다 enum이 늘고, 정의마다 그 키를 다 채워야 한다. **평면으로 고르면 매칭이 기하가 되어 저작 필드가 0개**다 — 패턴은 클립에서 유도된 값을 자동으로 받고, 근처에 구워진 세트가 있으면 **굽기 0회로 재사용**된다.

굽기 횟수를 지배하는 것이 **패턴 수(16, 계속 증가)에서 서로 다른 각도 수(추정 4~6, 포화)로 바뀐다.**

### 무엇을 어디에 두는가

| 무엇 | 어디 | 성질 |
|---|---|---|
| 스윙 → 평면 유도 | `MeshSliceBakerWindow.DeriveBladePlane` | **기존** — 코드 재사용 |
| 굽기 포즈 | `Pattern.EnemyDeath` 트림 끝 | **기존** — 품질 노브, 정합성 요구 아님 |
| 패턴의 canonical 평면 | `Pattern.bladePlane` | 신규 (툴이 기입, 손으로 안 적는다) |
| 세트의 canonical 평면 | `SliceSet.bakedBladePlane` | 신규 (굽기가 기입) |
| 각도별 세트 목록 | `EnemyDefinition.deathSliceSets[]` | 신규 (단일 필드는 폴백으로 잔존) |
| 매칭 | `Slice/Core/SliceMatch` | 신규 · asmdef · 테스트 |
| 감사·일괄 굽기 | `MeshSliceBakerWindow` 새 탭 | 툴만 |

### 이번 플랜에서 안 하는 것 (의도적)

- **런타임 각도 계산** — 클립 샘플링은 에디터 전용이다. 평면은 구워 둔다.
- **투사체(`EnemyCue.projectile`) 경로** — 채보 순간의 성질이라 스윙 각도와 무관하다.
- **군집 자동 병합(k-means 등)** — 감사 탭이 "가장 가까운 세트와 몇 도 차이"를 보여 주면 사람이 1초에 판단한다. 자동 군집은 굽는 사람의 의도를 덮는다.
- **적 종류 실제 확장** — 지금 정의 4개는 각도 운반자다(Research §1-1). 진짜 2종째 적은 별도 주제.

### 회귀 방어선

`Pattern.bladePlane` 미기입 **또는** `deathSliceSets` 비어 있음 → `ResolveDeathSet`이 기존 `DeathSliceSet` 단일 필드를 그대로 돌려준다. **어느 한쪽만 이관해도 안전하다.**

---

## 좌표계 — canonical 공간의 정의

> 이 절이 어긋나면 매칭 전체가 조용히 틀린다.

**적 루트 로컬**(플레이어 원점 +Z, 적이 `DuelGapAt(0)` 앞에서 180° 회전한 표준 배치에서, 적 루트 transform의 로컬 좌표계)로 통일한다.

- 적 루트는 **발바닥**이다(§11-2) — 휴머노이드 간에 의미가 같아 비교 가능하다.
- `SkinnedMeshRenderer` transform 로컬(현재 `bladePlane`이 사는 공간)은 리그에 종속이라 **비교에 못 쓴다.** 굽기용으로는 계속 쓰고, 매칭용 값만 따로 뽑는다.
- 두 값은 같은 월드 평면에서 나오므로 유도 함수를 한 번만 돌리고 변환만 둘로 나눈다.

---

## 단계별 구현 계획

### - [x] Step 1 — `SliceMatch` 순수 코어 + 테스트

`Assets/02. Scripts/Slice/Core/SliceMatch.cs` (기존 `SliceSpace` asmdef).

```
float Score(SlicePlane a, SlicePlane b, float positionWeight)
int   Pick(SlicePlane want, IReadOnlyList<SlicePlane> candidates, float positionWeight)
```

- **부호 없는 평면.** `n`과 `−n`은 같은 절단이다. 각도는 `min(θ, 180−θ)`, 그리고 뒤집힌 쪽을 골랐으면 `distance`도 부호를 뒤집어 비교한다. **이것이 이 단계의 존재 이유다** — 빠뜨리면 같은 각도가 180° 차이로 읽혀 재사용이 전부 실패한다.
- 점수 = `각도(도) + positionWeight × |거리차(m)|`. 노브 하나.
- `ponytail:` 각도와 높이를 한 스칼라로 더한다. 두 축을 따로 문턱 잡아야 할 만큼 갈리면 그때 나눈다.

테스트(`Slice/Tests`): 동일 평면 = 0점 / 뒤집힌 평면 = 0점(핵심) / 90° = 90점 / 같은 각도 높이차만 다른 둘 중 가까운 쪽 선택 / 후보 0개 = −1.

### - [x] Step 2 — `Pattern.bladePlane`

`Assets/02. Scripts/Pattern/Pattern.cs`.

- `[SerializeField] private SliceSpace.SlicePlane bladePlane;` + `bool hasBladePlane;`
- `public SlicePlane BladePlane` / `public bool HasBladePlane`
- 툴팁: **"굽기 툴이 `playerAttack` 임팩트 프레임에서 유도해 기입한다. 손으로 적지 않는다."**
- 절단 세트를 참조하지 않는 이유를 XML 주석으로 남긴다(Research §2 근거).

⚠ `Pattern`은 `PatternSpace`(Assembly-CSharp)이고 `SlicePlane`은 `SliceSpace` asmdef다 — Assembly-CSharp이 asmdef를 참조하는 방향이라 성립한다(`EnemyCue.projectile`이 이미 같은 방향).

### - [x] Step 3 — `SliceSet.bakedBladePlane`

`Assets/02. Scripts/Slice/SliceSet.cs`.

- 필드 + `public SlicePlane BakedBladePlane` / `bool HasBakedBladePlane`.
- `EditorAssignSkinned(...)`에 인자 추가. **기존 `bakedPlanes`(메쉬 로컬)와 혼동 금지** — 그쪽은 재굽기 로드용, 이쪽은 런타임 매칭용이며 사는 공간이 다르다. 툴팁에 그 차이를 적는다.
- 정적 경로(`EditorAssign`)는 건드리지 않는다(투사체·프롭은 매칭 대상이 아니다).

### - [x] Step 4 — `EnemyDefinition.deathSliceSets[]`

`Assets/02. Scripts/Enemy/EnemyDefinition.cs`.

- 배열 추가. **`deathSliceSet` 단일 필드는 남긴다** — 폴백이자 이관 중 안전망.
- `public IReadOnlyList<SliceSet> DeathSliceSets` / `SliceSet DefaultDeathSet`(배열 0번, 없으면 단일 필드).
- `OnValidate`: 배열 원소 중 `BakedBladePlane` 미기입이 있으면 경고(그 원소는 영영 안 뽑힌다).

### - [x] Step 5 — `ResolveDeathSet` 매칭 + 프리웜

`Assets/02. Scripts/Enemy/EnemyDirector.cs`.

- `ResolveDeathSet(r)`: `r.template.HasBladePlane && 배열 비어있지 않음` → `SliceMatch.Pick` → 그 세트. 아니면 **기존 단일 필드 그대로**.
- `[SerializeField] private float slicePositionWeight = 60f;` — 1cm ≈ 0.6도. 인스펙터 노브 하나.
- `PrewarmSet` 호출부(`:745`)를 배열 전체 순회로. ⚠ **빠뜨리면 곡 도중 `Instantiate` 히치 = 판정 손실**(§5 countdownDuration의 근거).
- `Reservation.template`은 이미 있다 — **새 배선이 없다.**
- `ponytail:` 매칭은 처치마다 후보 4~6개 순회다. 세트가 수십 개가 되면 정의별 캐시를 둔다.

### - [x] Step 6 — 굽기 툴이 canonical 평면을 기입

`Assets/02. Scripts/Slice/Editor/MeshSliceBakerWindow.cs`.

- `DeriveBladePlane()`이 월드 평면에서 **둘 다** 뽑게 한다: 메쉬 로컬(기존, 굽기용) + 적 루트 로컬(신규, 매칭용).
- `BakeHumanoid()`가 `EditorAssignSkinned`에 canonical 평면을 넘긴다.
- 같은 순간 `targetPattern.bladePlane`도 기입한다(`SerializedObject`, `EditorUtility.SetDirty`). **저작자가 두 곳을 맞출 일이 없어진다.**
- ⚠ 유도 없이 획만 그어 구운 세트는 canonical 평면이 없다 → 매칭 후보에서 빠지고 폴백만 된다. `OnValidate` 경고(Step 4)가 이를 잡는다.

### - [x] Step 7 — 감사 탭 (여기가 절차 최소화의 본체)

`MeshSliceBakerWindow`에 세 번째 탭 `패턴 감사`.

**입력:** 패턴 폴더(기본 `Assets/04. Datas/Patterns/Templates`) + `EnemyDefinition` 하나.

**`스캔` 버튼 한 번에:**
1. 폴더의 모든 `Pattern`을 읽어 `playerAttack.Clip`이 있는 것만 추린다.
2. 각각 `DeriveBladePlane` 로직으로 canonical 평면을 계산해 **`Pattern.bladePlane`에 기입**(멱등 — 재실행해도 값만 갱신).
3. 정의의 `deathSliceSets[]`와 매칭해 최근접 세트와 점수를 낸다.

**출력 표 (한 행 = 한 패턴):**

| 패턴 | 최근접 세트 | 각도차 | 높이차 | 판정 |
|---|---|---|---|---|
| `Pattern(0,4,8)` | `…_Diagonal` | 4.2° | 0.03m | O 재사용 |
| `Pattern(7,4)` | `…_Vertical` | 38.1° | 0.21m | ⚠ 굽기 필요 ☐ |

- 판정 문턱은 상단 노브 하나(`reuseThreshold`, 기본 15점).
- `ImpactTime` 미오서링 패턴은 별도 행으로 **"트리머로 임팩트 먼저"** 표시(기존 `DrawBladePlaneBlock`의 에러와 같은 문구).

**`선택 항목 일괄 굽기`:** 체크된 행마다 그 패턴을 `targetPattern`으로 세우고 유도 → 평면 1장 → 휴머노이드 굽기 → 정의 배열에 append. 진행률 바(`EditorUtility.DisplayProgressBar`), 실패 시 그 항목만 건너뛰고 **몇 번째가 왜 실패했는지 찍는다**(§5 굽기 툴의 "조용히 잘못된 데이터를 쓰지 않는다" 규율).

**결과 — 패턴 1개 추가 절차:**

| | 지금 | 이후 |
|---|---|---|
| 1 | 패턴 에셋 생성 | 패턴 에셋 생성 |
| 2 | 클립 트림(임팩트 찍기) | 클립 트림(임팩트 찍기) |
| 3 | 베이커 열고 6칸 채우기 | **감사 탭 `스캔`** |
| 4 | `칼 평면 유도` → `이 평면 사용` | (대개 여기서 끝 — O 재사용) |
| 5 | 프리뷰 확인 → `굽기`(조각 19장) | 새 각도일 때만 ☐ 체크 → 일괄 굽기 |
| 6 | 정의에 세트 수동 배선 | (툴이 append) |

### - [ ] Step 8 — 데이터 이관 (에디터에서 직접 실행 · 아래 절차대로)

1. 감사 탭으로 전 패턴 스캔 → `bladePlane` 일괄 기입.
2. 기존 세트 4벌에 canonical 평면이 없다 → 감사 탭의 **`기존 세트에 canonical 평면 기입`** 버튼 한 번.

   > **구현하면서 더 나은 답이 나왔다.** 플랜은 "재굽기 vs 평면만 기입" 중 후자를 골랐지만, 그때는 "어느 패턴으로 구웠는지"를 알아야 한다고 봤다. 실제로는 **알 필요가 없다** — 세트가 이미 `bakedPlanes`(메쉬 로컬)를 들고 있고, 메쉬 로컬 → 적 루트 로컬 변환은 프리팹 안 **정적 트랜스폼 사이의 강체 변환**이라(`SkinnedMeshRenderer`의 transform은 본이 아니다) 포즈·배치와 무관하게 정확하다. 패턴도, 재굽기도, 추측도 필요 없다.
3. `Samurai_Male_Diagonal` 하나를 대표로 남기고 `deathSliceSets[]`에 4벌을 모은다. 이름을 `Samurai_Male`로 정리.
4. `BattleScene`의 `EnemyDirector.rosterPool`을 정의 1개로 갱신.
5. 남은 정의 3개 삭제. ⚠ 삭제 전에 참조 검색 — 씬·프리팹 어디에도 안 남았는지 확인.
6. **검증:** 오토플레이(§8)로 한 곡 완주하며 패턴별 절단 각도가 스윙을 따라가는지 육안 확인. 랜덤성이 사라졌는지가 판별점 — 같은 패턴은 언제나 같은 각도로 갈라져야 한다.

### - [x] Step 9 — 문서

- `CLAUDE.md` §11-3에 "절단 각도는 패턴의 스윙이 고른다" 규율 추가. `EnemyDirector.cs:1601`의 `ponytail:` 주석은 **해소됐으므로 제거**하고 새 구조 설명으로 교체.
- `docs/!Guides/Guide_MeshSliceBaker.md`에 감사 탭 절차 추가.
- §11-3의 "`EnemyDefinition`이 유일한 소유자" 문장은 **유지**된다 — 소유자는 그대로고 개수만 늘었다.

---

## 검증 기준

| 항목 | 어떻게 |
|---|---|
| 부호 없는 매칭 | `SliceMatch` 유닛테스트 (뒤집힌 평면 = 0점) |
| 회귀 0 | `bladePlane`·배열 미이관 상태로 기존 곡 재생 → 지금과 동일 |
| 프리웜 누락 없음 | 곡 중 `Instantiate` 히치 없음(프로파일러) |
| 각도 일치 | 오토플레이 육안 — 같은 패턴 = 같은 각도, 스윙 방향과 절단면 일치 |
| 절차 단축 | 새 패턴 1개 추가에 굽기 0회로 끝나는 비율 (목표: 대부분) |
