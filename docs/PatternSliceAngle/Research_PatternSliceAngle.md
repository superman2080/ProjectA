# Research_PatternSliceAngle.md — 베는 모양과 갈라지는 모양의 불일치

> 조사 시점: 2026-08-20 / 대상: `Pattern` · `SliceSet` · `EnemyDefinition` · `MeshSliceBakerWindow` · `EnemyDirector`

## 0. 한 줄 요약

**패턴의 `playerAttack` 클립이 어떤 궤적으로 베든, 적은 그것과 무관한 각도로 갈라진다.** 그것도 고정된 오답이 아니라 **매번 다른 오답**이다 — 지금 절단 각도를 정하는 것은 `PickDefinition()`의 `Random.Range`다.

---

## 1. 현재 절단 각도가 정해지는 경로

```
EnemyDirector.PickDefinition()          ← Random.Range(0, rosterPool.Length)
        ↓
EnemyDefinition (스폰된 적이 든다)
        ↓
ResolveDeathSet(r) = r.opponent.Definition.DeathSliceSet      // EnemyDirector.cs:1606
        ↓
KillOpponent(opponent, set, impactTime, death) → SwapToCorpse(opponent, set)
```

`ResolveDeathSet`은 **인자로 `Reservation r`을 통째로 받으면서 `r.template`을 보지 않는다.** 패턴이 이 경로에 닿는 지점이 한 곳도 없다.

### 1-1. 이미 존재하는 워크어라운드 — 그리고 그것이 만든 버그

`Assets/04. Datas/EnemyDefinitions/`에 정의가 4개 있다:

| 에셋 | prefab guid | ambushAttacks | deathSliceSet |
|---|---|---|---|
| `Samurai_Male_Diagonal` | `0b6c0dc9…` | 동일 | `d601a521…` |
| `Samurai_Male_Dice` | `0b6c0dc9…` | 동일 | `8af6377b…` |
| `Samurai_Male_DoubleSlice` | `0b6c0dc9…` | 동일 | `779d4b97…` |
| `Samurai_Male_Vertical` | `0b6c0dc9…` | 동일 | `f552809c…` |

**프리팹도 기습 클립도 전부 같다. 다른 것은 `deathSliceSet` 하나뿐이다.** 즉 이것은 적 4종이 아니라 **한 적의 절단 각도 4개**이며, `EnemyDefinition`이 각도 운반자로 전용(轉用)되고 있다.

그리고 그 4개를 고르는 것이 `PickDefinition()`의 랜덤 순회이므로 — **의도한 "각도 다양성"이 실제로는 "스윙과 무관한 랜덤 각도"로 구현돼 있다.** 가로로 그은 패턴이 세로로 갈라질 확률이 구조적으로 3/4다.

---

## 2. 왜 `Pattern`이 `SliceSet`을 직접 들면 안 되는가

`EnemyDirector.cs:1594`의 기존 주석이 이미 근거를 든다:

> 시체 프리팹 안에는 그 적의 스켈레톤 사본이 들어 있어 세트는 원리적으로 적 모델을 넘나들 수 없다. 패턴에도 두면 "세트는 패턴이 고르고 죽는 적은 링에서 고른다"가 되어 모델이 어긋난다 — 리그가 같고 메쉬만 다르면 경고도 안 뜨고 엉뚱한 몸이 갈라진다.

`SliceSet.EditorAssignSkinned`가 `corpsePrefab`(그 적의 스켈레톤 사본 + 조각 `SkinnedMeshRenderer` N개)을 산출물로 든다는 사실이 이를 뒷받침한다. **세트는 (적 모델 × 각도)의 곱집합에 산다.** 그래서 소유자는 적이어야 하고, 패턴은 "어느 각도"만 말할 수 있다.

---

## 3. 결정적 발견 — 굽기 툴은 이미 패턴에서 평면을 유도한다

`MeshSliceBakerWindow`(휴머노이드 모드)는 **이미 `targetPattern`과 `enemyDefinition`을 둘 다 받는다**(`:60`, `:62`).

### 3-1. `DeriveBladePlane()` (`:1070~1131`)

1. 플레이어를 원점 +Z, 적을 `Vector3.forward * DuelDistance`에 180° 회전으로 세운다 — **표준 결투 배치**.
2. `DuelDistance = targetPattern.DuelGapAt(0f, duelBaseDistance)` — 임팩트 순간(t=0)의 간격. 런타임·합주 프리뷰와 같은 함수.
3. `attack.ImpactTime` 및 그 ±1프레임에서 `SampleWeapon`으로 칼의 월드 위치와 **장축**(렌더러 `localBounds` 최장 축)을 뜬다.
4. `normal = Cross(장축, 진행방향)` → 평면.
5. 적 `SkinnedMeshRenderer[enemyRendererIndex]`의 transform 로컬로 변환해 `bladePlane`에 담는다.

**즉 "이 패턴의 스윙이 만드는 절단 평면"을 계산하는 코드가 이미 완성돼 있다.** 지금은 사람이 `칼 평면 유도` → `이 평면 사용` 버튼을 눌러 수동으로 목록에 넣을 때만 쓰인다.

### 3-2. 좌표계 주의

유도 결과는 **`SkinnedMeshRenderer` transform 로컬**이다. 이 공간은 적 프리팹의 리그 구조에 종속이라 **적 종류 간 비교에 못 쓴다.** 반면 표준 배치(플레이어 원점 +Z, 적이 마주 봄)에서 나온 **월드 평면을 적 루트 로컬로 옮긴 값**은 발바닥 기준(§11-2, 캐릭터 root = 발바닥, 머리끝 1.69)이라 휴머노이드 간에 비교 가능하다. 매칭용 canonical 공간은 그쪽이다.

---

## 4. 포즈는 제약이 아니다 (중요)

`SliceSet`은 `bakedPoseClip` / `bakedPoseTime`을 든다. 출처는 `BakePoseTime(death) = death.StartOffset + death.ResolvedDuration`, 즉 **`Pattern.EnemyDeath`의 트림 끝**이다.

"패턴마다 죽는 포즈가 다르니 패턴마다 구워야 하나?"의 답은 **아니오**다. 툴 자신의 주석(`:339~345`)과 필드 툴팁이 명시한다:

> 조각이 스키닝을 유지하므로 **어떤 포즈든 따라가지만**, 터지는 순간의 포즈로 구울수록 관절 뒤틀림이 준다.
> 어떤 포즈로 구웠는지 기록(재굽기 재현용). **런타임 정합성 요구는 아니다.**

**따라서 굽기 횟수를 지배하는 축은 포즈가 아니라 각도 하나다.** 포즈는 품질 노브일 뿐이다.

---

## 5. 규모 — 순진하게 가면 얼마나 드는가

`Assets/04. Datas/Patterns/Templates/` 17개 중 `playerAttack` 클립이 배선된 것이 16개이고, **16개 전부 서로 다른 클립 guid**다(`PatternBlock(4)`만 `fileID: 0`).

| 방식 | 굽기 횟수 (적 1종) | 패턴 1개 추가 시 |
|---|---|---|
| 패턴마다 한 벌 | **16** | **+1 (매번)** |
| 각도 군집마다 한 벌 | 군집 수 (추정 4~6) | **대개 0** |

적 종류가 N종이면 양쪽 다 ×N이다. 휴머노이드 굽기는 조각 프리팹 19장(`Enemy_Samurai_Male_Diagonal/Piece_0~18`)을 만드는 무거운 작업이라 이 차이가 그대로 저작 비용이다.

**16개 클립이 16개의 서로 다른 각도를 뜻하지는 않는다.** 가로베기 계열 여러 개가 몇 도 차이로 몰려 있을 가능성이 높고, 그 경우 한 벌을 공유해도 화면상 차이가 없다. 이것이 절차 최소화의 유일한 지렛대다.

---

## 6. 손대야 하는 지점 (전수)

| # | 파일 | 현재 | 필요 |
|---|---|---|---|
| 1 | `EnemyDirector.cs:1606` `ResolveDeathSet` | 정의의 단일 세트 | 패턴 평면으로 후보 중 선택 |
| 2 | `EnemyDefinition.cs:16` `deathSliceSet` | 단일 | 배열(단일은 폴백으로 잔존) |
| 3 | `Pattern.cs` | 칼 평면 없음 | canonical 평면 1개 (툴이 기입) |
| 4 | `SliceSet.cs` | `bakedPlanes`(메쉬 로컬, 에디터 전용) | canonical 평면 1개 추가 |
| 5 | `MeshSliceBakerWindow.cs` | 수동 1건 굽기 | 유도값 자동 기입 + 일괄 굽기 |
| 6 | `EnemyDirector.PrewarmSet` (`:745`) | `definition.DeathSliceSet` 1개 | 배열 전체 순회 |
| 7 | 데이터 | 정의 4개(가짜) | 정의 1개 + 세트 4벌 |

`SliceTargetDirector`(투사체 경로)는 **건드리지 않는다** — `EnemyCue.projectile`은 채보 순간의 성질이고 스윙 각도와 무관하다.

---

## 7. 제약 정리

- **런타임 클립 샘플링 금지.** `AnimationClip.SampleAnimation`은 에디터 저작용이다. 매칭에 쓸 평면은 **에셋에 구워 둔 값**이어야 한다.
- **`SliceSet` GUID를 잃으면 안 된다**(`SliceSet.cs:8` 주석). 재굽기는 제자리 수정.
- **절단 평면은 부호가 없다.** `n`과 `−n`은 같은 절단이다 — 매칭 시 반드시 두 부호를 함께 본다. 안 그러면 같은 각도가 180° 차이로 읽혀 재사용이 통째로 실패한다.
- **`rosterPool`은 씬(`EnemyDirector`)이 든다.** 정의를 4→1로 줄이면 `BattleScene`의 배선도 같이 갱신해야 한다.
- 기존 채보 회귀 0이어야 한다 — 패턴에 평면이 없거나 정의에 배열이 비면 **지금 동작 그대로**.
