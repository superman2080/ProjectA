# Plan — 균열에서 늘어나 나오는 적 등장 연출 (EnemySpawnWarp)

근거: `docs/EnemySpawnWarp/Research_EnemySpawnWarp.md`

## 확정된 결정

| 항목 | 결정 |
|---|---|
| 등장 궤적 | 균열에서 태어나 **늘어난 채 자기 자리까지 이동**, 도착하며 복원 |
| 화면 밖(절두체) 판정 | **폐기** — 화면 안이어도 그대로 보여준다(연출이 팝인 숨기기를 대신한다) |
| 균열 목록 | **`EnemyDirector`가 인스펙터 배열로 든다.** `RiftController`는 순수 연출이라 한 줄도 안 고친다 |
| 균열 선택 | 그 자리에서 **가장 가까운** 균열 |
| 연출 중 자격 | 상대 배정·기습 후보·배회·이격에서 **제외** |
| 길이 노브 | **하나다** — warp 진행이 이동 구간에 정규화된다(진실의 원천이 하나) |

## Step

- [x] **Step 1 — `EnemySpawnWarp.shader`**
  `Assets/Shaders/Dissolve/EnemyDissolve.shader`를 복제해 `Assets/Shaders/Dissolve/EnemySpawnWarp.shader`로 만든다.
  - 프로퍼티: `_BaseMap` · `_BaseColor` · `_Ambient`(그대로) + `_Warp`(0~1, 1 = 최대 늘어남) ·
    `_WarpAxisOS`(오브젝트 공간, 균열 -> 자리 방향의 **반대** = 늘어나는 방향) · `_WarpStretch` ·
    `_EdgeWidth` · `[HDR] _EdgeColor`.
  - vert: 스캔 평면 기준 `s = dot(positionOS, -_WarpAxisOS)`를 0..1로 정규화하고,
    `positionOS += _WarpAxisOS * (_WarpStretch * _Warp * smoothstep(...))` 으로 **뒤쪽 정점만** 당겨 늘인다.
    `_Warp = 0`이면 변형항이 정확히 0 -> **복원이 식으로 보장된다**(분기 없음).
  - frag: 디졸브의 `clip`은 **빼고**, 스캔 평면 근처 발광 띠만 남긴다(`_EdgeColor` 관용구 재사용).
  - 노이즈 텍스처·`_Dissolve` 관련 코드는 삭제한다(늘어남은 노이즈를 안 쓴다).
  - `Assets/Shaders/Materials/EnemySpawnWarpMaterial.mat` 생성.

- [x] **Step 2 — `EnemyView`에 warp 진행**
  디졸브의 쌍둥이로 붙인다. **신규 필드는 넷**(`warpStart`·`warpEnd`·`warpMaterial`·`warpAxisId`).
  - `public void BeginSpawnWarp(Vector3 fromRift, Vector3 slot, float endTime, Material warpMaterial)` —
    `dissolveSwap.Begin(renderers, warpMaterial, propertyBlock)`(같은 인스턴스를 쓴다:
    죽는 것과 태어나는 것은 동시에 일어날 수 없고, `Active` 가드가 이미 있다) +
    축을 **오브젝트 로컬로 변환해** 프로퍼티 블록에 넣는다.
  - `Update`의 `dissolving` 블록 **아래**에 warp tick: `_Warp = 1 - Clamp01((now - warpStart)/(warpEnd - warpStart))`.
    끝나면 `dissolveSwap.Restore()` 한 번.
    ⚠ `dissolving`처럼 `return`하지 않는다 — **이동·회전이 계속 돌아야 자리까지 간다**.
  - `public bool SpawnWarping => warpEnd > Time.time` (아래 Step 4가 쓴다).
  - `ResetState`가 이미 `dissolveSwap.Restore()`를 부르므로 **풀 반납 원복은 공짜다**.
    warp 시각만 0으로 되돌린다(안 하면 다음 대여가 늘어난 채 나온다).

- [x] **Step 3 — `EnemyDirector.SpawnAt`을 균열 경로로**
  - 신규 인스펙터: `Transform[] riftPoints` · `float spawnWarpStretch` · `bool warpOnPrepareStage` ·
    `Material spawnWarpMaterial`.
  - `Vector3 NearestRift(Vector3 slot, out bool found)` — 배열이 비었거나 전부 비활성이면 `found = false`.
  - `SpawnAt`: `found`면 `from = 균열`, 이동 시간 = `거리 / clusterMoveSpeed`(기존 노브),
    `Setup` 뒤에 `view.BeginSpawnWarp(...)`.
    `found`가 아니면 **기존 `PickSpawnNearCluster` + 절두체 경로 그대로**(회귀 0).
  - `PrepareStage`에서 온 호출은 `warpOnPrepareStage`가 꺼져 있으면 폴백 경로를 쓴다
    -> `SpawnAt`에 `bool allowWarp` 인자 하나. (`SpawnCluster`가 그 값을 흘려 준다.)
  - `SpawnIntoStage`(무리 미사용 폴백 경로)는 **안 건드린다** — `clusterEnabled = false`인 씬 전용이다.

- [x] **Step 4 — 연출 중 자격 제외**
  - `EnemyView.BusyReasonBy`에 **한 줄**: `if (SpawnWarping) return "등장 연출 중";`
    -> 배회·기습 후보·매 프레임 이격이 **한 술어로 동시에** 제외된다(새 판정을 만들지 않는다).
  - `EnemyDirector.TakeTargetForWindow`의 1차 후보 필터에 `if (e.SpawnWarping) continue;`
    한 줄. 거긴 일부러 `HasPendingAction`(좁은 술어)을 써서 이동 중을 안 막기 때문이다.
    ⚠ **그 아래 폴백(후보 0일 때 전원 허용)은 그대로 둔다** — 상대가 없으면 그 패턴에 벨 대상이 아예 없다.

- [x] **Step 5 — 프리웜**
  `PrepareStage`의 프리웜에 `spawnWarpMaterial`을 얹는다(§5: 곡 도중 셰이더 컴파일 히치 = 판정 손실).
  머티리얼 하나뿐이라 `Shader.WarmupAllShaders`가 아니라 **그 머티리얼만** 데운다.

- [x] **Step 6 — 씬 배선과 확인**
  `Tutorial.unity`의 `EnemyDirector`에 `riftPoints`(씬의 `Rift`)와 머티리얼을 꽂고 플레이로 확인:
  ① 균열에서 늘어나 나와 자리에서 복원되는가 ② 도착 직후 툰 머티리얼로 정확히 돌아오는가
  ③ 연속 사망/스폰에서 **다음 대여가 늘어난 채 나오지 않는가**(풀 원복)
  ④ 연출 중인 적이 상대로 배정되지 않는가.

  **확인 결과**(플레이 모드, `Tutorial.unity`):
  - `PrepareStage` 스폰 전원이 균열 좌표 `(0.0, 7.5, 12.0)`에서 시작하고 `SpawnWarping = true`.
  - 씬 뷰 캡처로 늘어남과 스캔 띠 확인.
  - 진행이 끝난 뒤 머티리얼이 `Stage1Enemy`(툰)로 원복됨 — 스왑/복원 왕복 성립.
  - ③④는 곡을 끝까지 돌려야 나오는 사건이라 **실제 플레이에서 확인이 남아 있다.**

  **⚠ 발견**: 씬의 `Rift`가 `y = 7.5`라 적이 7.5m 상공에서 내려온다. 의도면 그대로 두고,
  지면에서 나오게 하려면 `SpawnAt`의 `from`에서 y만 `slot.y`로 눌러 쓰면 된다(한 줄).

## 안 하는 것

- 진짜 슬릿스캔(포즈 히스토리 N장) — §5의 프리웜 규율과 정면 충돌(Research §3-C).
- 이동과 별개인 warp 길이 노브 — 둘이 어긋날 여지를 만들지 않는다.
- `RiftController`·`Encounter`·`EnemyRing` 수정 — 이번 변경은 `SpawnAt` 한 메서드와 뷰 한 곳에 닫힌다.
- 아웃라인(§11-8)·실루엣(§14) 레이어와의 조합 손보기 — 스왑이 그 구간에만 걸리므로 겹칠 일이 없다.
  실제로 기습 강조와 겹치면 그때 본다(연출 중인 적은 Step 4가 기습 후보에서 이미 뺀다).

## 정정 1 — 늘어남 축의 공간 (플레이 확인에서 드러남)

**증상**: 적이 균열에서 나오긴 하는데 **늘어나지 않고 그냥 뛰어온다.**

**원인**: 축을 `EnemyView` **루트 공간**으로 변환해 넘겼는데, 정점은 **렌더러 자식의 오브젝트 공간**에 있다.
현재 적 모델의 자식은 FBX 보정으로 `localRotation = (270, 0, 0)`이라 두 공간이 다르다.
그래서 축이 몸의 **두께 방향**(바운즈 extent 0.19)에 얹혀 스캔 좌표가 사실상 상수가 되고,
늘어나는 대신 **몸이 통째로 조금 밀렸다**(화면에서는 아무 일도 안 일어난다).

⚠ 처음 씬 뷰 확인이 이걸 못 잡은 이유: 그때는 축이 **우연히** 긴 쪽에 얹혀 늘어남이 보였다.
같은 코드가 위치에 따라 되기도 안 되기도 한다 — 그게 공간이 어긋났다는 신호였다.

**고친 것**
- `EnemyView.SetWarp`가 **렌더러마다** `r.transform.InverseTransformDirection`으로 축을 변환한다(월드 방향을 받는다).
- 몸 길이(`_WarpSpan`)와 중심(`_WarpCenterOS`)도 **그 렌더러의 `localBounds`에서 뽑는다** —
  축 방향 실제 두께라 스캔 좌표가 정확히 0..1에 펴지고, **모델이 바뀌어도 값을 다시 잡을 필요가 없다**
  (인스펙터의 `_WarpSpan`은 이제 툴 미리보기용 기본값이다).
- 첫 프레임에는 적이 균열 위에 있어 방향이 0이므로, `BeginSpawnWarp`가 **자리에서 균열을 보는 방향**을 폴백으로 든다.

**재확인**: 스폰 시 `axis = (0.08, 0.72, 0.69)` · `span = 1.86`(추측값 1.8이 아니라 실측) ·
씬 뷰에서 상체가 균열 쪽으로 길게 늘어나고 스캔 띠가 선단에 보임 · 진행 종료 후 `Stage1Enemy`로 원복.

## 정정 2 — 줄이 균열에 붙어 있어야 한다

**요구 재확인**: 늘어남은 "적이 조금 찌그러지는 것"이 아니라 **균열과 스폰 지점을 잇는 한 줄**이다.
도착한 뒤 그 줄이 몸으로 빨려 들어가며 원형이 된다.

**틀렸던 것 셋** — 전부 "닿지 않는다"의 다른 얼굴이다.

1. **길이가 상수였다**(`_WarpStretch = 2`). 균열이 9~16m 떨어져 있으니 줄이 애초에 닿을 수 없었다.
   -> 길이를 **균열까지의 실제 거리**로 매 프레임 채운다(`InverseTransformVector`라 모델 스케일도 탄다).
   `_Warp`는 이제 길이에 안 곱해진다 — 그것은 스캔 띠의 자리일 뿐이다.
2. **몸 길이를 바운즈(AABB)로 쟀다.** 사람 메쉬는 박스를 안 채워서 꼬리 정점의 스캔 좌표가
   **0.69**에 그쳤다(= 거리의 69%에서 끊김). -> 정점 투영의 최소·최대로 잰다.
3. **그 측정을 쉬는 포즈로 했다.** A포즈가 재생 포즈보다 넓어 여전히 **0.84**였다.
   -> `BakeMesh(useScale: false)`로 **지금 포즈**를 굽는다(작업용 `Mesh`·`List`를 정적으로 재사용해 GC 0).
   최종 스캔 범위 **0.02~0.98**, 꼬리는 균열을 살짝 지나간다(`WarpReachMargin` 1.05 —
   모자라면 끊겨 보이고 남으면 균열 안으로 들어갈 뿐이라 넘치는 쪽으로 둔다).

**구간이 둘이 됐다**: 이동(줄이 균열에 앉은 채 길어진다) -> 수축(`spawnRetractDuration`, 기본 0.35초 —
길이가 0으로 줄고 스캔 띠가 꼬리에서 머리로 쓸려 오며 원형이 된다). 도착과 동시에 놓으면 줄이 끊겨 보인다.

**재확인**(플레이 모드): 머리는 적 위치, 꼬리는 균열 — 씬 뷰에서 지면의 적부터 균열까지 한 줄로 이어지고
선단에 스캔 띠. 수축이 끝난 뒤 `Stage1Enemy`(툰)로 원복.

## 추가 — 튜토리얼 등장 타이밍

**요구**: 허물을 미리 세우지 말고 **미오가 도착하는 순간에 3마리**, 나머지는 **플레이 도중에**.

`Seq_Tutorial` 스텝 순서만 바꿨다(코드가 아니라 저작 데이터다):

```
... RunOn -> 대사 -> [PrewarmStage] -> 대사 -> RunTo(도착까지 대기) -> PrepareStage(3) -> RunOff ...
```

- `PrepareStage`를 **`RunTo` 뒤로** 옮기고 인원을 5 -> **3**으로 줄였다.
  ⚠ 반드시 `RunOff` **앞**이어야 한다 — `TutorialDirector.PrepareStage`가 무대를 재우고(`enemyDirector.enabled = false`)
  `RunOff`(`SetRunning(false)`)가 그걸 깨우기 때문이다. 뒤로 가면 무대가 잠든 채 남는다.
- 나머지 5마리(`TutorialDirector.spawnBudget` 8 − 3)는 **사망 1 : 스폰 1**로 플레이 도중에 나온다(§11-6). 새 코드가 0줄이다.

**⚠ 프리웜만 떼어 냈다**(`TutorialCueStep.Action.PrewarmStage` · `EnemyDirector.PrewarmStage`).
`PrepareStage`는 프리웜을 겸하므로 그대로 늦추면 **적 8마리와 절단 세트의 `Instantiate`가 드릴 직전에** 온다(§5).
데우는 일만 예전 자리(대사 구간)에 남겼다.

**⚠ `PrefabPool.Prewarm`은 멱등이 아니라서** `EnemyDirector.Prewarm`에 `prewarmed` 가드를 넣었다 —
없으면 두 번째 호출이 인스턴스를 다시 만들고 상한을 넘은 만큼 파기해서 **피하려던 히치를 그대로 낸다**.

**확인**(플레이 모드): 프리웜만 부르면 활성 적 0, 이어서 `PrepareStage(3)`을 부르면 활성 3이고
셋 다 균열 좌표 `(0, 7.5, 12)`에서 시작한다.
