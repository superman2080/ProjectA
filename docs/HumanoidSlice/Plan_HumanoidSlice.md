# Plan: 휴머노이드 절단 — 산 적 / 시체 두 오브젝트 교체

> 근거 Research: [`docs/EnemyRagdoll/Research_EnemyRagdoll.md`](../EnemyRagdoll/Research_EnemyRagdoll.md) §10
> 관련: `docs/SliceTarget/`, `docs/EnemyCombat/`, `docs/SliceImpactFrame/`, `docs/ClipTrimTool/`, `docs/!Guides/Guide_MeshSliceBaker.md`
>
> **개정 이력**
> - v1: 바인드 포즈 절단 + 포즈 독립 프록시. → 폐기(칼 평면과 메쉬의 좌표계 불일치).
> - v2: 사망 포즈를 저작해 확정하고 그 공간에서 절단. → 부분 폐기(포즈 정합성 요구가 과했음).
> - **v3 (현재): 산 적과 시체는 별개 오브젝트다.** 시체 프리팹이 자기 스켈레톤을 갖고,
>   교체 시 포즈만 전사한 뒤 **산 적은 풀로 반납**한다. 굽기 포즈는 정합성 요구가 아니라 저작 보조로 격하.

---

## 0. 핵심 구조

```
[저작]   Pattern
           playerAttack : 플레이어가 베는 클립 + ImpactTime   (칼 궤적 = 절단 평면의 근거)
           enemyDeath   : 적이 베어지는 클립 + ImpactTime      (굽기 포즈 = 저작 보조)  ← 신설
           enemyDeathSliceSet : 굽기 산출물                                            ← 신설

[굽기]   슬라이서(휴머노이드 모드)
           enemyDeath 임팩트 프레임 포즈로 프리뷰 → 같은 프레임의 칼 궤적에서 평면 유도
           → 스키닝 보존 절단 → **시체 프리팹 1개**로 저장 (자기 스켈레톤 + 조각 N개)

[런타임]  임팩트 시각(= Deadline + SliceTargetImpactOffset)에
           ① 시체 프리팹 대여 → 산 적의 본 로컬 포즈를 **그대로 전사**
           ② 산 적 인스턴스는 **풀로 반납**
           ③ 시체의 루트 조각은 스킨드로 남아 래그돌, 나머지는 굳혀 흩뿌림

[실패]   미스가 나면(마지막 노드든 그 전이든) 절단 없이 **회피(Evade) 애니메이션**만 재생
```

### 목표
1. `Pattern`에 `enemyDeath`(`ClipAlignment`) + `enemyDeathSliceSet` 신설.
2. `Tools/Animation Clip Trimmer`에 **슬롯 선택** 추가 — 적 클립도 Start/End/Impact를 찍어 저장.
3. 슬라이서에 **휴머노이드 모드** 신설 — 저작값을 읽어 굽고, 칼 궤적에서 평면 유도, 결과를 `Pattern`에 자동 배선.
4. `Tools/Mesh Slice Baker`를 `일반 메쉬` / `휴머노이드` **모드 2개로 분리**. 일반 메쉬 모드는 기존 동작 보존.
5. 런타임을 **오브젝트 교체 + 산 적 풀 반납** 구조로 전환.
6. **단면(캡)이 막혀 있어야 한다** — 이 플랜의 수용 기준.

### 비목표
- **래그돌 물리 자체** → `docs/EnemyRagdoll/Plan_EnemyRagdoll.md`(후속).
  이 플랜은 "시체 프리팹의 루트 조각이 스킨드로 살아 있어 래그돌을 붙일 수 있는 상태"까지.
- 본 계층을 쪼개는 MGR식 Dynamic Skeleton (Research §1, 안 D).
- 런타임 실시간 절단 (Research §4 — 리듬게임에서 프레임 히치 = 판정 손실).

---

## 1. 설계 결정

### 1-1. 굽기 포즈는 **정합성 요구가 아니라 저작 보조**다
v2에서 "굽기 포즈 = 런타임 포즈여야 한다"고 못박았으나, **결정 1-2(스키닝 보존)를 채택하면 그 요구가 사라진다.**

- **루트 조각**: bindpose를 굽기 포즈 P에서 재계산했으므로 "P를 rest로 갖는 정상 스킨드 메쉬"다.
  스켈레톤이 포즈 Q에 있으면 **Q로 변형돼 렌더된다.** 굽기 포즈가 뭐든 런타임 포즈를 따라간다.
- **날아가는 조각**: 교체 순간 `BakeMesh`로 그때의 포즈를 떠서 굳힌다(결정 1-4). 굽기 포즈와 무관.

→ **선딜 제약, `Animator.Play` 0블렌드 강제, 포즈 불일치 경고 전부 불필요.** (v2의 결정 1-6 폐기)

남는 잔여 오차: LBS를 두 번 거치는 셈이라 P와 Q가 크게 벌어지면 관절부에 뒤틀림(캔디랩)이 생긴다.
단일 본 가중치 정점은 정확하고 여러 본에 걸친 정점만 근사다.
→ **P를 죽는 자세 근처로 잡으면 무시할 수준.** 그래서 `enemyDeath`를 굽기 기준으로 쓰는 건
정합성 요구가 아니라 **품질 최적화**로서 여전히 유효하다.

>>> ✅ 확정 (2026-08-01)

### 1-2. 스키닝을 보존한다 (bindpose를 굽기 포즈에서 재계산)
정적 메쉬로만 구우면 딱딱한 조각으로 되돌아가 "축 늘어짐"이 불가능하다.

- **boneWeights** — `BakeMesh`는 정점 수·순서를 보존하므로 `sharedMesh.boneWeights`를 인덱스 그대로 승계.
  절단으로 생긴 새 정점은 보간(Step 3), 캡 정점은 루프에서 승계(Step 4).
- **bindposes** — 굽기 포즈에서 재계산: `bindposes[i] = bones[i].worldToLocalMatrix * meshTransform.localToWorldMatrix`

>>> ✅ 확정 (2026-08-01)

### 1-3. 산출물은 **시체 프리팹 1개**다 (조각 N개가 아니라)
"산 적"과 "시체"는 근본적으로 다른 오브젝트이므로, 시체가 **자기 스켈레톤 사본**을 갖는다.

- 굽기 산출물 = 프리팹 1개: `적 스켈레톤 사본 + 조각 SkinnedMeshRenderer N개`.
- 교체 시: 산 적의 본 **로컬 회전/위치를 그대로 전사**(같은 계층이므로 루프 한 번) → 겉모습 일치.
- 그 다음 **산 적 인스턴스를 통째로 풀에 반납**한다.

**이 결정이 "풀 반납"을 가능하게 하는 유일한 이유다.** 조각을 산 적의 스켈레톤에 물리면
그 스켈레톤이 살아 있어야 해서 반납할 수 없다.

부수 이득: 런타임 배선이 대여 1회로 끝난다(조각별 `bones` 배선 불필요), 풀 복구도 프리팹 단위로 단순해진다.

>>> ✅ 확정 (2026-08-01)

### 1-4. 날아가는 조각은 교체 순간 `BakeMesh`로 굳힌다
모든 조각이 같은 스켈레톤에 물려 있으면 몸이 갈라진 채 **붙어서** 움직인다.
**루트 본을 포함한 조각 1개만 스킨드로 남기고**(래그돌 대상), 나머지는 굳혀 부모에서 떼고 Rigidbody로 넘긴다.

>>> ✅ 확정 (2026-08-01)

### 1-5. ~~`SliceSet` 소유권을 `Pattern`으로~~ → **철회. `EnemyDefinition`이 유일 소유자다** (2026-08-01 개정)

원안은 "굽기 평면이 패턴마다 다르니 세트도 패턴이 갖는다"였다. **틀렸다.**

**세트가 결정하는 것은 평면 하나가 아니라 메쉬·스켈레톤이 9할이다.** 시체 프리팹 안에는
그 적의 스켈레톤 사본이 들어 있어 세트는 원리적으로 적 모델을 넘나들 수 없다.

패턴에 두면 축이 어긋난다:
```
세트는 패턴이 고른다   ┐
                      ├→ 서로를 모른다
죽는 적은 링에서 정해진다 ┘

rosterPool = [적A, 적B]  ·  Pattern 세트 = 적A로 구움
   → 적B가 죽는 순간 적A의 시체가 나온다
```
리그가 같고 메쉬만 다르면 본 수가 맞아 **경고도 없이** 엉뚱한 몸이 갈라진다.

**런타임 검증(모델 비교 + 폴백)으로 막을 수도 있었지만, 필드를 없애는 쪽을 택했다** —
검증 코드를 추가하는 것보다 **그 상태를 표현 불가능하게** 만드는 편이 낫다.

- `Pattern.enemyDeathSliceSet` **삭제.** `EnemyDefinition.deathSliceSet` 하나만 남는다.
- 굽기 툴은 프리팹 대신 **`EnemyDefinition`을 입력으로 받는다** — 세트가 그 정의의 프리팹에서
  구워졌다는 것이 굽는 시점에 보장되고, 배선도 자동으로 그리로 간다.
- `ChartPlayer.CollectDeathSets` **삭제** — `EnemyDirector.Prewarm`이 `rosterPool`을 훑는 것으로 충분해졌다.

**대가**: 절단 각도가 적 종류당 하나로 고정된다. 지금은 배선된 패턴이 0개라 잃는 것이 없다.
`ponytail:` 각도 다양성이 필요해지면 **정의에 세트 배열**을 두고 패턴이 인덱스로 고르게 올린다 —
전부 같은 프리팹에서 구워지므로 모델 정합성은 유지된다.

>>> ✅ 확정 (2026-08-01, 개정)

### 1-6. 실패는 전부 **회피(Evade)** 로 통일한다
미스가 나면 — 마지막 노드든 그 전이든 — 절단하지 않고 **뒤로 물러나는 회피 애니메이션**만 재생한다.

현재 `EnemyView.Resolve`(`:192`)는 공격자에 따라 갈린다:
```
attacker == Enemy  && 성공 → KnockBack
attacker == Player && 실패 → Evade
그 외                      → Idle
```
→ **실패면 공격자와 무관하게 Evade**로 단순화한다. 성공 분기(KnockBack / 처치)는 그대로 둔다.

>>> ✅ 확정 (2026-08-01)

### 1-7. 휴머노이드 모드에는 Shape 프리셋을 두지 않는다
프리셋은 `bounds.center`를 지나는 평면(`SliceShapeExtensions.ToPlanes`)이라 인체에서 의미가 없다.
획 긋기 + 칼 궤적 유도만 제공하고, `SliceSet.shape`에는 항상 `SliceShape.Custom`을 저장한다.

>>> ✅ 확정 (2026-08-01)

### 1-8. 교체 시각 = **임팩트 시각** (`Deadline + SliceTargetImpactOffset`) — ✅ 확정

플레이어 칼이 그 시각에 지나가도록 정렬되어 있고(`CharacterActionPlayer.cs:420`),
성패는 `Deadline`에서야 확정되므로 그보다 이르게 그릴 수 없다.

Perfect로 쳤을 때 입력 순간과 절단 사이에 최대 **0.1초 지연**이 생기지만
**의도된 지연 연출로 수용한다**(사용자 확정, 2026-08-01).
입력 순간의 즉각 피드백은 `OnJudged`(판정 색상·이펙트·SFX)가 이미 담당한다 — 지연되는 것은 결과 연출뿐이다.

---

## 2. 단계

### Step 0 — 선행: 교체 시각을 임팩트 시각에 예약한다 (**모든 작업보다 먼저**)

#### 현상 — 적 처치만 "임팩트 시각 = Deadline" 규율에서 빠져 있다

| 경로 | 시각 결정 | 상태 |
|---|---|---|
| 플레이어 칼 | `CharacterActionPlayer.cs:420` — `Deadline + SliceTargetImpactOffset`에 임팩트 정렬 | ✅ 예약 |
| 투사체 절단 | `SliceTargetDirector.cs:205` — `now >= r.impactTime`까지 대기. `Resolve()`는 **성패만** 기록 | ✅ 예약 |
| **적 처치** | `EnemyDirector.cs:396` → `KillOpponent` → `opponent.Kill()` **즉시 실행** | ❌ **예약 없음** |

`Reservation.impactTime`을 들고 있으면서 처치에는 쓰지 않는다.

#### 왜 잘 칠수록 더 어긋나는가
`CompletePattern`은 두 곳에서 불린다:
- `PatternHandler.cs:599` — 마지막 노드를 **입력한 순간**(성공 경로)
- `PatternHandler.cs:255` — `Time.time > Deadline`(만료 경로)

`Deadline = LastNodeTime + goodWindow`(0.1초). Perfect로 치면 완료 이벤트가 `LastNodeTime`에 떠서 **Deadline보다 0.1초 이르다.**
→ **Perfect일 때 적이 칼보다 최대 0.1초(60fps 6프레임) 먼저 갈라진다.** 늦은 Good일수록 오히려 잘 맞는 거꾸로 된 상태.
`killOnSuccess`는 성공할 때만 발동하므로 **항상 이 이른 경로를 탄다.**

#### 수정
- [x] `EnemyDirector`에 교체 예약 목록 추가(`SliceTargetDirector.Reservation` 선례).
      **성패 확정은 즉시, 오브젝트 교체는 `r.impactTime`에.**
- [x] `KillOpponent`을 둘로 나눈다:
      - **즉시**: 상대에서 제외, 링 보충, `PromoteOpponent()`, `OnEnemyKilled` 발행
        → "죽는 연출을 기다리지 않는다 — 베고 뒤도 안 돌아본다"(`EnemyDirector.cs:406`)가 무쌍 감각의 핵심이라 **반드시 즉시 유지**.
      - **예약**: 시체 교체는 `Update`에서 `now >= impactTime`일 때.
- [x] `HandleAllCleared`(곡 중단)에서 **대기 중인 교체 예약도 정리** — 안 그러면 곡이 끝난 뒤 적이 갈라진다.
- [ ] 예약 대기 구간(최대 0.1초)에 적이 어떤 포즈인지 확인. 눈에 띄면 조정. **(플레이 확인 필요)**

#### 검증
- [ ] Perfect / 늦은 Good 두 방식으로 쳐서 **갈라지는 순간이 동일**한지. **(플레이 확인 필요)**
- [ ] 칼날이 지나가는 프레임과 갈라지는 프레임이 겹치는지(저배속 녹화). **(플레이 확인 필요)**

---

### Step 1 — `Pattern`에 `enemyDeath` + `enemyDeathSliceSet`
- [x] `[SerializeField] ClipAlignment enemyDeath` + 게터 (`Combat Clips` 헤더, 기존 셋과 같은 자리).
- [x] `OnValidate`에 `enemyDeath?.ValidateImpactTime(this, "EnemyDeath")` 추가 (`Pattern.cs:123` 선례).
      **선딜 제약 검증은 넣지 않는다** (결정 1-1로 불필요).
- [x] `[SerializeField] SliceSet enemyDeathSliceSet` + 게터 (결정 1-5).
- [x] 런타임 조회를 **패턴 우선, `EnemyDefinition` 폴백**으로 (`EnemyView.cs:219`, `EnemyDirector.cs:411`
      — **두 곳 모두**. 어긋나면 조각 수와 배치가 갈린다).

### Step 2 — `Animation Clip Trimmer`에 슬롯 선택 추가
현재 툴은 `Pattern`의 **레거시 평면 필드**(`animationStartOffset` / `animationDuration` / `animationImpactTime`)에
`SerializedProperty` 이름으로 **하드코딩**한다(`AnimationClipTrimmerWindow.cs:449-451`). `ClipAlignment` 슬롯은 못 건드린다.

- [x] **슬롯 선택 팝업**: `레거시 (successAnimationClip)`(기본값, 기존 워크플로 보존) /
      `playerAttack` / `playerParry` / `enemyAttack` / **`enemyDeath`**
- [x] `LoadFromPattern`(`:111`) / `ApplyToPattern`(`:442`)이 선택 슬롯의
      `clip` / `startOffset` / `duration` / `impactTime`을 읽고 쓰도록 일반화.
      `ClipAlignment`는 `[Serializable]` 클래스이므로 `so.FindProperty("enemyDeath.impactTime")`처럼 경로 접근.
- [x] 슬롯은 클립도 들고 있으므로(레거시와 달리) **트리머에서 클립 자체를 지정/저장**할 수 있어야 한다.
- [x] **프리뷰 대상 프리팹 필드 노출** — `enemyDeath` / `enemyAttack`은 적 프리팹으로 봐야 한다
      (지금은 플레이어 프리팹 전제).
- [x] `Apply` 시 임팩트를 트림 구간으로 클램프하는 기존 규칙 유지(`:447`).

### Step 3 — 코어: `Vertex`에 BoneWeight 추가 + 보간
- [x] `MeshSliceBaker.Vertex`(`:415`)에 `BoneWeight boneWeight` 추가.
- [x] `Vertex.Lerp`(`:422`)에 가중치 보간. **단순 Lerp가 안 되는 유일한 필드다** —
      두 정점이 최대 8개 영향을 갖고 결과는 4개여야 한다:
      ① (본인덱스 → 가중치) 병합하며 각각 `(1-t)`/`t` 곱해 누적 ② 내림차순 상위 4개 ③ 합이 1이 되도록 정규화
- [x] 가중치 없는 정적 경로는 기본값(전부 0)으로 남아 무영향이어야 한다.
- [x] **테스트**(`Slice/Tests`) `BoneWeightLerpTests`: 같은 본 → 가중치 1 / t=0·1에서 원본 일치 /
      t=0.5에서 합 1 / 영향 8개 → 상위 4개 + 합 1 / 0가중치가 상위를 밀어내지 않음.

### Step 4 — 코어: 절단선·캡 경로에 가중치 관통
**캡 정점이 가중치를 잃는 지점을 막는 것**이 핵심.

- [x] `cutSegments` 타입 `List<(Vector3,Vector3)>` → `List<(Vertex,Vertex)>` (`:131`, `:239`).
- [x] `ClipTriangle` `:279` — `cutSegments.Add((onPlane[0].position, onPlane[1].position))`으로
      **Vertex를 버리고 position만 남긴다.** `onPlane`은 이미 `List<Vertex>`이므로 그대로 실어 보낸다.
- [x] `CollectOnPlaneEdge`(`:220`)도 position 대신 `src.GetVertex(i)`.
- [x] `BuildLoops`(`:295`) — 용접은 position 기준 유지, 용접 인덱스 → `Vertex` 매핑을 함께 들고
      루프를 `List<List<Vertex>>`로 반환.
- [x] `AppendCap`(`:367`) — 루프 정점의 가중치를 승계하고 UV/노멀/탄젠트만 캡 규칙으로 덮어쓴다.
      **캡 중심 정점**은 루프 가중치 평균(Step 3 병합 로직 재사용).
      평면과 메쉬가 같은 공간이므로 **UV 투영·노멀은 기존 코드 그대로**.
- [x] 다중 평면에서 "앞 평면의 캡을 다음 평면이 다시 자른다"는 규칙 유지 확인.
- [x] **테스트**: 캡 정점 가중치 합 = 1.

### Step 5 — 코어: `WorkMesh` 보관 + `ToMesh` 기록 + `Validate` 분리
- [x] `WorkMesh`에 `List<BoneWeight> boneWeights`, `Matrix4x4[] bindposes` 추가.
      `FromMesh` / `AppendVertex` / `CreateEmptyLike`가 유지. `bindposes`는 통째로 승계.
- [x] `ToMesh()`(`:702`)가 기록. 비어 있으면 기록하지 않는다(정적 경로 무영향).
- [x] `Validate`(`:43`) 분리: `ValidateStatic`(기존, 스킨드 거부 유지) /
      `ValidateSkinned`(`blendShapeCount > 0` 거부 — 절단 후 델타를 나눌 수 없다. 토폴로지·readable 공통).
- [x] 기존 `Slice/Tests` 전부 통과 — **정적 메쉬 결과가 달라지면 안 된다**(삼각형 수·조각 수·형태).
      단 정점 수는 아래 용접으로 줄어든다 — 이건 의도된 변경이다.

### Step 5-b — 코어: `ToMesh()`에 정점 용접 (§3 예산 참조)
현재 `WorkMesh.AppendTriangle`(`:564`)은 **삼각형마다 정점 3개를 새로 만든다.**
인덱스 공유가 전혀 없어 결과 정점 수 = 삼각형 수 × 3이다.
정적 프롭에서는 무시할 만했지만 **캐릭터(수천 폴리) + boneWeights(32B/정점)에서는 치명적**이다.

- [x] `ToMesh()`에서 **속성이 같은 정점을 합치는 용접 패스**를 돌린다.
      기준: 위치(weldEpsilon) + 노멀 + UV + 탄젠트 + boneWeight가 모두 같을 때만 병합
      (하나라도 다르면 별개 정점이어야 셰이딩·UV 이음매가 깨지지 않는다).
- [x] 기존 `VertexWelder`(`:433`)의 해시 격자를 재사용하되, **위치만이 아니라 속성 전체를 키로** 쓴다.
- [x] 절단 중간 표현(`WorkMesh`)은 건드리지 않는다 — 용접은 **마지막 출력 단계에서만**.
      중간에 합치면 평면별 캡 병합 로직이 꼬인다.
- [x] **테스트**: 용접 전후 삼각형 수 동일 / 바운즈 동일 / 정점 수 감소 / 스무딩 그룹 경계가 안 뭉개짐.
- [x] 정적 경로도 같이 이득을 본다(회귀가 아니라 개선).

### Step 6 — `SliceSet` 확장 (시체 프리팹 방식)
- [x] `bool skinned` — 런타임 경로 판단의 유일한 근거.
- [x] **`GameObject corpsePrefab`** — 결정 1-3의 시체 프리팹. `skinned`면 이것이 산출물이고,
      `piecePrefabs`는 **쓰지 않는다**(정적 경로와 필드를 공유하지 않도록 문서화).
- [x] `int rootPieceIndex` — 시체 프리팹 안에서 스킨드로 남을 조각. 없으면 `-1`.
- [x] `AnimationClip bakedPoseClip` + `float bakedPoseTime` — **재굽기 재현용 기록**.
      (v2에서는 런타임 불일치 경고용이었으나 결정 1-1로 격하)
- [x] `EditorAssignSkinned(...)` 추가. 기존 `EditorAssign` 시그니처는 유지(정적 경로 무영향).
- [x] `IsUsable`에 스킨드 검증: `skinned`면 `corpsePrefab != null` && `rootPieceIndex` 유효.

### Step 7 — 툴: 모드 전환 뼈대
- [x] `enum BakeMode { StaticMesh, Humanoid }` + `[SerializeField] BakeMode mode`.
- [x] `OnGUI` 최상단 `GUILayout.Toolbar` 탭 2개.
- [x] **모드 전환 시 프리뷰 캐시·조각 프리뷰·기존 세트 로드를 전부 초기화**
      (안 그러면 정적 프리뷰가 휴머노이드 탭에 남아 오인을 부른다).
- [x] 공통 유지: 세트 이름 · 출력 경로 · 단면 머티리얼 · Cap UV Scale · 고급 옵션 · 획 목록 UI · Preview/Bake.

### Step 8 — 툴: `일반 메쉬` 모드 (기존 보존)
- [x] 남기는 것 — **동작 변경 없음**: 표적 프리팹 / 세트 이름 / 출력 경로 /
      `Shape` 라벨·프리셋 + 확인 다이얼로그 + 획 목록 / 단면 머티리얼 · Cap UV Scale / Weld · Min Volume / Preview · Bake.
- [x] **삭제**: `deathPoseClip`(`:29`), `deathPoseTime`(`:32`), 스키닝 안내 HelpBox(`:102-113`).
      휴머노이드 모드가 이 역할을 저작값 기반으로 가져간다. 두 곳에 남으면 어느 쪽이 진실인지 모호해진다.
- [x] `GetBakedSkinnedMesh()`(`:578`)와 포즈 캐시(`:562-565`)는 **휴머노이드 모드로 이관**(삭제 아님).
- [x] 이 모드는 `ValidateStatic`을 쓴다.

### Step 9 — 툴: `휴머노이드` 모드 (신설)
- [x] **9-1 입력** — `Pattern` 에셋(핵심) / 적 프리팹(`SkinnedMeshRenderer` 필수, 여럿이면 선택 팝업) /
      플레이어 프리팹(칼 궤적용) / 공통 항목. **Shape 프리셋 없음**(결정 1-7).
- [x] **9-2 포즈 확정** — `enemyDeath.Clip`을 `enemyDeath.ImpactTime`에 `SampleAnimation` → `BakeMesh`.
      `OnGUI`가 반복 호출되므로 결과 캐시(기존 `:583` 로직 재사용).
      **동시에 본 월드 행렬을 떠서 bindposes 재계산 데이터를 만든다**(결정 1-2).
- [x] **9-3 프리뷰** — 그 포즈의 메쉬를 그린다. 기존 획 긋기·회전·줌·분해 슬라이더 그대로.
      **저작 화면 = 실제 죽는 순간의 모습**이라 눈으로 바로 판단된다.
- [x] **9-4 칼 평면 유도**
      - `playerAttack.Clip`을 `playerAttack.ImpactTime`에 플레이어 프리팹에 샘플링 →
        `root/add_weapon_r`(`WeaponBoneBake`가 굽는 그 본)의 트랜스폼.
      - **평면 법선 = 칼날 장축 × 진행 방향.** 진행 방향은 임팩트 전후 프레임 위치 차분
        (`WeaponBoneBake` 롤 보정과 같은 방식).
      - 적 배치는 **결투 거리 float 하나로 노출**. 씬 참조를 끌어오지 않는다
        (툴이 씬에 의존하면 프리팹만으로 못 굽는다).
      - 월드 평면 → 적 메쉬 로컬은 **강체 변환 하나**(같은 포즈 공간이므로 정확).
      - 프리뷰에 오버레이 + `이 평면 사용` 버튼으로 획 목록에 추가. 추가 후 손 조정 가능.
      - `Pattern` 미지정이면 이 블록을 숨긴다 — 자유 획만으로도 구울 수 있어야 한다.
- [x] **9-5 정합성 경고** — `playerAttack.ImpactTime` ≤ 0이면 `ClipAlignment`가 트림 끝으로 폴백하므로
      (`ClipAlignment.cs:57`) 유도 평면이 임팩트와 무관해진다 → 경고 + 유도 차단.
      (`enemyDeath` 관련 경고는 결정 1-1로 불필요)
- [x] **9-6 루트 조각 판정** — 조각의 정점 가중치가 참조하는 본에 `rootBone`(또는 하위)이 포함되는지.
      여러 조각이 걸치면 **루트 본 가중치 총합 최대**인 조각. 수동 오버라이드 허용.
- [x] **9-7 진단 표시** — 조각별 정점/삼각형/캡 루프 수(기존) + **영향 본 수** + **루트 조각 강조**.

### Step 10 — 툴: 휴머노이드 Bake (시체 프리팹 생성) + `Pattern` 자동 배선
- [x] 조각 메쉬 저장(`SaveMeshPreservingGuid` `:776`)을 확장해 **boneWeights/bindposes도 제자리 기입**.
      현재는 vertices/normals/uv/tangents/triangles만 옮긴다 —
      **여기 빠지면 재굽기에서 조용히 스키닝이 날아간다.**
- [x] **시체 프리팹 조립**(결정 1-3):
      - 적 프리팹의 **본 계층만 복제**(렌더러 제외).
      - 조각마다 `SkinnedMeshRenderer` + 머티리얼 슬롯(M+1) + `SlicePiece`를 붙이고
        `bones`/`rootBone`을 **복제된 스켈레톤**으로 배선한다(프리팹 내부 참조라 저장 가능).
      - `localBounds`를 조각 bounds로 설정 (안 하면 컬링이 어긋나 조각이 사라진다).
      - 루트 조각 표식을 프리팹에 남긴다(런타임이 인덱스로 찾지 않아도 되게).
- [x] `EditorAssignSkinned`로 `skinned` / `corpsePrefab` / `rootPieceIndex` / `bakedPoseClip` / `bakedPoseTime` 기록.
- [x] **`Pattern.enemyDeathSliceSet`에 자동 배선**(결정 1-5). 다른 세트가 이미 물려 있으면 확인 다이얼로그.
- [x] 재굽기 GUID 보존 규칙 유지.

### Step 11 — 런타임: 오브젝트 교체 + 산 적 풀 반납
- [x] `EnemyDirector`에 **시체 프리팹 풀** 추가(기존 프리팹별 풀 재사용).
      **곡 도중 `Instantiate` 0** 규율 유지(`EnemyDirector.cs:178`).
- [x] **프리웜을 카운트다운이 아니라 플레이 씬 진입 전 비동기 로드 구간으로 앞당긴다**(§3-5).
      `PrepareStage` 호출 지점을 옮기고, 대상은 **채보에서 실제 쓰는 세트의 중복 제거 집합**으로 한정(§3-6).
- [x] **`SwapToCorpse(EnemyView live, SliceSet set)`**:
      ① 시체 프리팹 대여 → 위치·회전을 산 적과 일치
      ② **본 로컬 포즈 전사** — 같은 계층이므로 이름 매칭 배열을 굽기 때 미리 만들어 두고 인덱스 루프로 복사
      ③ 루트 조각은 스킨드로 유지(래그돌 대상 — 후속 플랜)
      ④ 나머지 조각은 `BakeMesh`로 굳혀 부모에서 떼고 `Launch()` (결정 1-4).
         **대상 `Mesh`는 풀링한다** — 교체마다 `new Mesh()`는 GC 압박이다.
      ⑤ **산 적 인스턴스를 풀에 반납**(`ReleaseEnemy`)
- [x] `EnemyView.Kill()`(`:212`)은 `skinned == false`(정적 프록시)일 때만 기존 경로를 탄다.
      기존 `CrossFade(deathStateName)`(`:217`)는 **직후 렌더러를 꺼서 절대 보이지 않는 죽은 배선**이므로 정리한다.
- [x] **실패 경로**(결정 1-6): `EnemyView.Resolve`(`:192`)를 **실패면 공격자 무관 Evade**로 단순화.
      성공 분기(KnockBack / 처치)는 그대로.
- [x] `EnemyDirector`의 `debris` 수명 관리를 **시체 단위**로 바꾼다(조각 리스트가 아니라 시체 인스턴스).
      `maxActivePieces` 상한도 시체 단위로 재해석.
- [x] 시체 풀 반납 시 상태 복구: 조각의 스킨드↔정적 전환, `isKinematic`, 부모 관계, 본 로컬 포즈 원복.
      **안 되돌리면 다음 대여에서 뒤틀린 채 나온다**(Research §8 — 알려진 난점).

### Step 12 — 검증

**코드로 확인 완료**
- [x] `Slice/Tests` 전부 통과 — **23/23 passed** (기존 15 + 신규 `BoneWeightLerpTests` 8).
- [x] `read_console`로 컴파일 에러/경고 확인 — **0건**(테스트 러너 노이즈만).

**에디터/플레이에서 확인 필요 (실제 에셋을 구워야 판단 가능)**
- [ ] 정적 표적 회귀 — 기존 `SliceSet` 재굽기 시 조각 수·형태 동일.
      (용접이 들어가 **정점 수는 줄어드는 게 정상**이다 — 삼각형 수·형태만 비교할 것)
- [ ] **단면이 막혀 보이는지 카메라 정면 확인** (이 플랜의 수용 기준).
- [ ] 교체 순간이 안 보이는지 — 저배속 녹화로 프레임 확인.
- [ ] 칼이 지나간 자리와 갈라진 자리가 일치하는지.
- [ ] 미스(마지막 노드 / 그 전) 두 경우 모두 **Evade가 재생되고 절단이 일어나지 않는지**.
- [ ] 산 적이 실제로 풀에 반납되는지(프로파일러에서 인스턴스 수 확인).
- [ ] 루트 조각 자동 판정이 의도한 조각을 고르는지(툴 Preview의 `◀ 루트` 표시로 확인).

---

## 3. 예산 (성능·메모리)

### 3-1. 교체 순간은 비용 중립
| 항목 | 비용 |
|---|---|
| 시체 프리팹 대여 | 풀에서 1회 |
| 본 포즈 전사 | 트랜스폼 ~60개 복사. 무시 가능 |
| 산 적 반납 | 풀에 1회 |

기존(조각 N개 대여 + 조각별 `bones` 배선)보다 **오히려 싸다** — 대여가 1회로 줄어서.

### 3-2. 정상 상태도 기존과 거의 같다
교체 직후 **루트 조각만 스킨드로 남고 나머지는 굳어 `MeshRenderer`가 된다.**
시체당 `SkinnedMeshRenderer` 1 + `MeshRenderer` N−1 — 예전(전부 rigid) 대비 스킨드 하나 증가.
스키닝은 정점 수 비례이고 조각을 다 합쳐도 원래 몸 하나 분량이라 N배가 되지 않는다.

### 3-3. 교체 프레임의 `BakeMesh` 스파이크
날아가는 조각마다 1회. CPU 스키닝 패스 + 메쉬 업로드라 공짜가 아니고,
**리듬게임에서 프레임 히치 = 판정 손실**이다.

- **평면 1장(조각 2개)으로 시작한다** → `BakeMesh` 1회. 그 정도면 안전.
- 대상 `Mesh`는 풀링(Step 11).
- 조각을 8개·16개로 늘릴 때 재측정한다.

### 3-4. 정점 3배 폭증 (Step 5-b의 근거)
`AppendTriangle`(`:564`)이 삼각형마다 정점을 새로 만들어 **정점 수 = 삼각형 수 × 3**이다.

```
원본 5k 정점 / 10k 삼각형
  → 절단 결과 30k 정점 (6배)
  → boneWeights 32B/정점 추가로 정점당 ~80B
  → SliceSet 하나에 ~2.4MB
  → 세트 30개면 ~72MB
```

메모리만이 아니라 **스키닝 비용도 정점 수 비례**라 루트 조각 스키닝이 매 프레임 3배가 된다.
→ **Step 5-b 용접 필수.**

### 3-5. 프리웜 — 시간은 해결됐고 메모리는 아니다
**시간**: 플레이 씬 진입 전 비동기 로드 구간에서 프리웜하면 `Instantiate` 히치가 흡수된다.
지금 `PrepareStage`가 카운트다운에서 하는 것을 **로딩 구간으로 앞당긴다**(Step 11).

**메모리**: 언제 만들든 곡 내내 들고 있는 양은 같다. 게다가
**메쉬 메모리는 세트 수에 비례하지 인스턴스 수에는 비례하지 않는다**(메쉬는 공유 에셋)
— 풀 크기를 줄여도 안 줄어든다.

### 3-6. **`SliceSet` 재사용이 기본이다** (결정 1-5의 운용 지침)
결정 1-1로 **굽기 포즈가 정합성 요구에서 빠졌으므로**, 패턴마다 세트를 따로 가질 이유가 없다.
패턴마다 다른 것은 **칼이 지나간 각도**뿐이다.

- **가로베기 / 대각베기 / 세로베기 3~4종을 구워 여러 패턴이 공유한다.**
- 특별한 연출이 필요한 패턴만 전용 세트.
- `EnemyDefinition.DeathSliceSet` 폴백이 기본값 역할.
- 30종 → 4종이면 메쉬 메모리도 프리웜도 문제가 사라진다.
- [x] 프리웜 대상을 **채보에서 실제 쓰는 세트의 중복 제거 집합**으로 한정한다
      (`ChartPlayer`가 엔트리 목록을 안다).
- [x] 같은 세트의 시체가 동시에 필요해지면 **가장 오래된 시체를 즉시 회수해 재사용**한다.
      런타임 `Instantiate` 0을 지키기 위함.

---

## 4. 위험과 대비

| 위험 | 징후 | 대비 |
|---|---|---|
| **교체 시각 미수정** | 칼보다 최대 0.1초 먼저 갈라짐. **Perfect일수록 심함** | **Step 0 (전체 선행)** |
| 본 포즈 전사 누락/오매칭 | 교체 순간 시체가 T-포즈로 튐 | Step 10에서 이름 매칭 배열을 **굽기 때** 만들어 저장 |
| 캡 정점 가중치 누락 | 단면만 안 움직이거나 원점에 붙음 | Step 4가 정확히 그 지점. 테스트로 합=1 |
| 재굽기에서 스키닝 증발 | 두 번째 굽기부터 조각이 안 따라감 | Step 10 `SaveMeshPreservingGuid` 확장 |
| `localBounds` 미설정 | 조각이 특정 각도에서 사라짐 | Step 10 |
| 시체 풀 복구 누락 | 재사용 시 뒤틀린 채 등장 | Step 11 마지막 항목 (Research §8) |
| 루트 조각 오판 | 몸통이 날아가고 팔이 남음 | Step 9-6·9-7 진단 + 수동 오버라이드 |
| 굽기 포즈 P와 런타임 포즈 Q가 크게 벌어짐 | 관절부 캔디랩 뒤틀림 | `enemyDeath`를 죽는 자세로 저작(결정 1-1) |
| 정적 경로 회귀 | 기존 표적 절단이 달라짐 | Step 5·12 |
| 패턴/적정의 이중 배선 | 조각 수·배치가 갈림 | Step 1에서 **두 조회 지점 모두** 패턴 우선으로 |
| 시체 프리웜 누락 | 첫 처치에서 히치 | Step 11 + §3-5 (로딩 구간으로 앞당김) |
| **정점 3배 폭증** | 세트 수 늘수록 메모리 급증, 스키닝 3배 | **Step 5-b 용접** (§3-4) |
| 세트를 패턴마다 새로 굽기 | 메쉬 메모리 폭증 | §3-6 — 3~4종 공유가 기본, 전용은 예외 |
| 용접이 셰이딩 경계를 뭉갬 | 각진 면이 둥글어 보임 | 속성 전체를 키로 씀 (Step 5-b) |

---

## 5. 후속 (별도 플랜)

- **`docs/EnemyRagdoll/Plan_EnemyRagdoll.md`** — 시체 프리팹 스켈레톤에 래그돌 붙이기.
  Research §8 필수 항목(래그돌 간 충돌 끄기 / 풀 복구 / 시체 수 상한 / 절단면 위 본 콜라이더)이 여기.
- 단면 전용 살점 머티리얼 (Research §1 — MGR은 절단 방향별 단면 텍스처를 썼다).
- `docs/!Guides/Guide_MeshSliceBaker.md` 갱신(모드 2개), `docs/ClipTrimTool/` 갱신(슬롯 선택).

---

## 6. 피드백

>>> 여기에 `>>>`로 의견을 남겨 주세요. Plan 확정 전까지 구현하지 않습니다.
