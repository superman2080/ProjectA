# Plan — 구 형태 등장 (SpawnWarpSphere)

근거: `docs/EnemySpawnWarp/Research_SpawnWarpSphere.md`

## 설계 결정

1. **구간을 셋으로 나눈다.** 지금은 `spawnRetractDuration` 하나가 줄 회수와 원복을 동시에 민다(P5).
   `줄 회수(구 유지)`와 `구 → 적`을 **다른 시간 노브**로 가른다. 요구의 3·4단계가 곧 그 둘이다.

   | 구간 | stretch | warp(구 비율·띠) | 길이 노브 |
   |---|---|---|---|
   | 늘어남 | 균열까지 거리 | 1 (몸 전체가 구) | `spawnWarpDuration` |
   | 끊김·뭉침 | 거리 → 0 | 1 **유지** | `spawnSnapDuration` (신규, 기본 0.18) |
   | 원복 | 0 | 1 → 0 | `spawnRestoreDuration` (= 기존 `spawnRetractDuration` 개명) |

2. **셰이더 파라미터를 안 늘린다.** 위 표는 전부 `_Warp` 하나를 어떻게 미느냐의 문제다.
   단 `_Warp = 1`에서 몸 전체가 구가 되도록 스윕 범위를 `[0, 1 + _EdgeWidth]`로 넓힌다(P4):
   `front = _Warp * (1 + _EdgeWidth)`, `sphere = saturate((front - scan) / _EdgeWidth)`.
   빛나는 띠도 같은 `front`에 앉힌다 — **띠가 곧 형태가 정해지는 자리**가 된다.

3. **구는 월드 기준 하나다.** 중심·반지름을 월드에서 한 번 정하고 렌더러마다 변환한다(P2·P3).
   반지름은 비율이 아니라 **미터**(`EnemyDirector.spawnSphereRadius`, 기본 0.35)로 둔다 —
   균열 방향과 무관하게 같은 크기여야 하고, 미터라야 무대 규모와 같은 단위로 읽힌다.

4. **끊김은 회수 곡선이 만든다**(P6). `spawnSnapDuration`을 짧게 두고 `stretch`를
   `1 - (1-p)^3`(ease-out)으로 회수한다 — 첫 프레임에 대부분이 딸려 들어와 "끊겼다"로 읽힌다.
   새 상태도 새 파라미터도 없다.

5. **이름은 `_Sphere*`로 통일한다.** 직전 작업에서 넣은 `_Blob*`은 코드베이스에 없던 말이다
   (CLAUDE.md 0단계 — 새 용어 금지). 문서·코드·머티리얼이 전부 `구`를 가리키게 한다.

## 단계

- [x] **Step 1 — 셰이더**: `_Blob*` → `_Sphere*` 개명, 구 중심/반지름을 오브젝트 공간 값으로 직접 받는다
      (`_SphereCenterOS` + `_SphereRadiusOS`, 비율 계산을 셰이더에서 뺀다).
      `front = _Warp * (1 + _EdgeWidth)`로 구 비율과 빛나는 띠를 같은 자리에 앉힌다.
      `_Warp = 0`에서 항이 정확히 0인 규칙 유지.
- [x] **Step 2 — 구 기하 측정**: `MeasureWarpExtents`가 **월드 구 중심 하나**(주 렌더러 정점 무게중심,
      비면 바운즈 중심)를 잡고, `SetWarp`가 렌더러마다 `InverseTransformPoint`/`InverseTransformVector`로
      중심·반지름을 옮겨 넣는다. 렌더러별 배열(`warpBlobCenters`)은 제거.
- [x] **Step 3 — 3구간 타임라인**: `EnemyView.BeginSpawnWarp` 인자에 `snapDuration` 추가,
      `TickSpawnWarp`을 `늘어남 → 끊김(band 1 고정, stretch ease-out 0) → 원복(stretch 0, band 1→0)`으로 나눈다.
      끝 처리(`SetWarp(0) + dissolveSwap.Restore()`)는 그대로 원복 구간 끝에만 둔다.
- [x] **Step 4 — 디렉터 노브**: `spawnRetractDuration` → `spawnRestoreDuration` 개명,
      `spawnSnapDuration`(기본 0.18) · `spawnSphereRadius`(기본 0.35) 추가, `BeginSpawnWarp` 호출부 갱신.
      ⚠ 개명은 `Tutorial.unity`의 직렬화 키를 끊는다 — `[FormerlySerializedAs]`를 붙인다.
- [x] **Step 5 — 머티리얼**: `EnemySpawnWarpMaterial`의 `_Blob*` 항목을 `_Sphere*`로 교체,
      `_EdgeWidth`는 이제 **구 전환 폭**을 겸하므로 값 재확인(0.25 유지, 필요하면 여기서만 조정).
- [x] **Step 6 — 씬 배선**(P1, 코드 아님): 작업 당시 에디터에 열려 있던 씬
      (`Assets/_Recovery/0 (1).unity`)에 **Rift는 이미 있었고 `EnemyDirector` 쪽 배선만 비어 있었다** —
      `riftPoints[0] = Rift.transform`, `spawnWarpMaterial = EnemySpawnWarpMaterial`로 채웠다(Undo 가능, 미저장).
      ⚠ `BattleScene.unity`는 아직 그대로다 — `Rift` 오브젝트 자체가 없어 놓을 자리가 레벨 디자인 판단이다.
- [x] **Step 7 — 확인**: 컴파일 에러 0건(`recompile` → `errors: []`), 배선 값 확인
      (`riftPoints=1`, `mat=EnemySpawnWarpMaterial`, `snap=0.18`, `restore=0.35`, `radius=0.35`).
      ⚠ **눈으로 보는 확인은 남았다** — 재생해서 네 단계가 순서대로 보이는지는 직접 봐야 한다.

## 안 하는 것

- 파티클·발광 추가 — 지금 문제는 형태지 화려함이 아니다.
- 이동 중 로코모션 억제 — 몸이 구라 다리는 어차피 안 보인다. 보이면 그때 한다.
- `SlicePiece`처럼 조각으로 뭉치는 물리 — 닫힌 식 하나로 충분하다.
