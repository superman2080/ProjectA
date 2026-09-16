# Research — 구 형태 등장 (SpawnWarpSphere)

`docs/EnemySpawnWarp/`의 후속 연구다. 기존 Research/Plan(늘어남 자체)은 그대로 유효하고,
여기서는 **"구가 균열에서 나와 늘어나고, 끊긴 뒤 구로 뭉쳤다가 적이 된다"**는 요구와
현재 구현의 차이만 다룬다.

## 0. 요구한 그림 (4단계)

| # | 단계 | 화면 |
|---|---|---|
| 1 | 생성 | 균열 자리에 **구** 하나가 생긴다 |
| 2 | 늘어남 | 그 구가 자기 자리까지 **쭉 늘어난다**(꼬리는 균열에 붙어 있다) |
| 3 | 끊김·뭉침 | 자리에 닿으면 균열 쪽이 **끊기고** 줄이 자리로 빨려 들어가 **다시 구**가 된다 |
| 4 | 원복 | 그 구가 **적의 형태로** 돌아온다 |

**3과 4가 순차**라는 것이 요구의 핵심이다 — 뭉치는 동안에는 아직 구이고,
적이 되는 것은 줄이 다 회수된 뒤다.

## 1. 지금 코드가 하는 일

- `EnemyDirector.SpawnAt`(`Assets/02. Scripts/Enemy/EnemyDirector.cs:970`)
  - `NearestRift(slot)`이 균열을 찾으면 `from = rift.position`, `duration = spawnWarpDuration`.
  - `view.Setup(...)`이 **적 트랜스폼을 균열에 놓고 자리까지 이동 예약**(`EnemyView.cs:308`).
  - `view.BeginSpawnWarp(rift.position, slot, Time.time + enterDuration, spawnRetractDuration, mat)`(`:1013`).
- `EnemyView.TickSpawnWarp`(`Assets/02. Scripts/Enemy/EnemyView.cs:1111`)
  - **이동 구간**: `stretch = 균열까지 거리`, `band = 1`.
  - **수축 구간**(`spawnRetractDuration`): `stretch → 0`, `band → 0`. **둘이 같은 구간에서 동시에 흐른다.**
  - 끝나면 `SetWarp(0, ...)` + `dissolveSwap.Restore()`.
- `EnemySpawnWarp.shader`
  - `scan` = 축 방향 정규화 좌표(0 = 앞머리, 1 = 균열 쪽 꼬리).
  - `posOS += axis * (_WarpStretch * scan)` — 꼬리가 균열에 앉는다.
  - (직전 작업에서 추가) `_BlobCenterOS`/`_BlobRadius`로 정점을 구 위로 `lerp`,
    비율은 `saturate((_Warp - scan)/_EdgeWidth)`.

## 2. 확인된 문제

### P1. 전투 씬에 이 기능이 아예 배선돼 있지 않다 (가장 큰 원인)
`Assets/01. Scenes/BattleScene.unity`의 `EnemyDirector` 블록(`:11251` 부근)에는
`riftPoints` · `spawnWarpMaterial` · `spawnWarpDuration` · `spawnRetractDuration` **키 자체가 없다**.
씬에 `Rift` 오브젝트도 0개다(`grep m_Name: Rift` → 0건).
→ `NearestRift`가 null → **예전 경로(화면 밖 스폰)로 조용히 돌아간다.**
배선된 곳은 `Tutorial.unity:18250` 하나뿐이다.
**"균열에서 나오지도 않는다"의 직접 원인이며, 코드로는 고칠 수 없다(씬 배선).**

### P2. 구가 렌더러 수만큼 생긴다
구 중심(`warpBlobCenters`)과 반지름(`_WarpSpan × _BlobRadius`)을 **렌더러마다** 잡는다
(`EnemyView.cs:1458` `MeasureWarpExtents`). `renderers`는 `GetComponentsInChildren<Renderer>`(`:281`)라
**몸 + 무기**가 각각 들어온다 → 구가 두 개 뜬다. 요구는 구 **하나**다.

### P3. 구의 크기가 균열 방향에 따라 달라진다
`_WarpSpan`은 **축(= 균열 방향) 투영 길이**다. 균열이 옆에 있으면 몸 두께(≈0.4m),
앞에 있으면 몸 길이가 되어 같은 적이 스폰마다 다른 크기의 구가 된다.
게다가 0.22 비율이면 실제 반지름이 0.1m 안팎이라 **구로 안 보이고 사라진 것처럼 보인다.**

### P4. `_Warp = 1`에서도 몸 전체가 구가 아니다
`blob = saturate((_Warp - scan)/_EdgeWidth)`는 `_Warp = 1`일 때 `scan > 1 - _EdgeWidth`인
꼬리 구간이 0이다 — **늘어나는 내내 꼬리는 사람 형태**다. 스윕 범위가 `[0, 1]`이라
양 끝에서 포화되지 않는 것이 원인이다.

### P5. 끊김·뭉침과 원복이 같은 구간에서 동시에 흐른다
`spawnRetractDuration` 하나가 `stretch`와 `band`를 **함께** 민다(`:1125`).
즉 줄이 회수되는 동안 이미 사람 형태로 돌아오고 있어, 요구한 3단계(구로 뭉침)가 화면에 존재하지 않는다.
`0.35`초 안에 두 사건이 겹쳐 지나가므로 어느 쪽도 읽히지 않는다.

### P6. 끊김의 순간이 없다
`stretch`가 거리에서 0으로 **선형**으로 줄어든다 — "끊긴다"가 아니라 "천천히 짧아진다"로 읽힌다.
요구는 자리에 닿는 **그 프레임**에 균열 쪽이 놓이는 것이다.

## 3. 안 건드려도 되는 것

- 줄 길이가 **균열까지의 실제 거리**라는 규칙(`TickSpawnWarp`) — 요구와 일치한다.
- `scan` 좌표를 **원본 정점**에서 재는 규칙 — 구로 옮긴 좌표로 재면 범위가 구 지름으로 무너진다.
- 공간 변환을 **렌더러마다** 하는 규칙(`SetWarp`) — 모델 자식의 FBX 보정 때문.
- `_Warp = 0`이면 변형항이 정확히 0이라는 규칙 — 원형 복원이 분기가 아니라 식이다.
- `dissolveSwap` 공유와 `ResetState`의 원복(`:1609`).
