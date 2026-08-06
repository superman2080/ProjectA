# Research — EnemyCluster (적을 무리 지어 스폰하기)

## 문제

적이 무대 전체에 **균등하게 흩어져** 서 있어 화면이 정신없다. 위치마다 **무리(群)**를 지어 나오게 하고 싶다.

---

## 현재 구현

### 배치를 정하는 유일한 지점

`EnemyRing.PickStagePosition` (`Assets/02. Scripts/Enemy/Core/EnemyRing.cs:121`) — 순수 계산. `EnemyDirector.SpawnIntoStage`(`EnemyDirector.cs:475`)가 유일한 호출자다.

```
후보를 candidateCount(24)번 뽑는다:
    angle  = random() * 360
    radius = sqrt(random()) * stageRadius     ← sqrt 때문에 원 안 "균등 분포"
    후보 = AngleToPosition(center, angle, radius)

    플레이어와 minPlayerDistance(4m) 미만 → 버림
    기존 적과 minSpacing(2.5m) 미만       → 버림
    화면 안(절두체)                        → 폴백 후보로만 보관
    화면 밖                                → clearance(기존 적과의 최소거리)가 클수록 승리
```

**흩어짐의 원인이 두 겹이다.**
1. `sqrt(random())`가 원 안 **면적 균등**을 만든다 — 설계상 "고르게 퍼뜨리기"다.
2. 화면 밖 후보 중 **`clearance`가 최대인 것**을 고른다 — 명시적으로 **서로 멀어지는** 선택이다.

즉 지금 코드는 "무리 짓지 않기"를 두 번 강제한다. 무리를 만들려면 이 두 규칙을 모두 뒤집어야 한다.

### 관련 파라미터 (`EnemyDirector` 인스펙터)

| 필드 | 기본값 | 역할 |
|---|---|---|
| `stageRadius` | 8 | 무대 원 반경 |
| `ringCount` | 6 | 무대에 유지할 적 수 |
| `minSpacing` | 2.5 | 적끼리 최소 거리 |
| `minPlayerDistance` | 4 | 플레이어 코앞 스폰 금지 반경 |
| `spawnCandidateCount` | 24 | 후보 샘플 수 |

`arenaCenter`는 씬의 `Stage` 오브젝트(월드 고정, §11-2). **플레이어를 가리키면 안 된다.**

### 스폰이 일어나는 시점 (2곳뿐)

- `PrepareStage()` (`:389`) — 곡 시작 전 카운트다운. `ringCount`까지 채운다. **곡 도중 `Instantiate` 금지**라 여기서 프리웜까지 끝낸다.
- `KillOpponent` 끝 (`:977`) — 처치로 하나 빠지면 `if (ring.Count < ringCount) SpawnIntoStage();`로 한 명 보충.

스폰은 **화면 밖에서 즉시** 일어난다(등장 이동 없음, §11-2).

### 배치를 소비하는 곳 — 여기가 진짜 제약이다

`TakeTargetForWindow` (`:552`):

```csharp
float desired = cruiseSpeed * window / playerShare + duelDistance;
desired = Mathf.Clamp(desired, minTargetDistance, stageRadius * 2f);
// → EnemyRing.PickTargetByDistance(ring 위치들, 플레이어, desired)
```

**다음 상대는 "목표 거리에 가장 가까운 적"이다.** 목표 거리는 음악이 정한 창(0.5~2.1초)에서 나오고, 이것이 §11-2의 **체감 이동 속도를 일정하게 유지하는 장치**다.

> ⚠ **무리 짓기가 이 장치와 정면으로 충돌한다.** 적이 2~3개 덩어리에만 있으면 플레이어~적 거리가 **이산적인 몇 개 값**으로 수렴한다. 그러면 `desired`가 아무리 매끄럽게 변해도 실제 이동 거리는 계단식이 되고, 짧은 창에서 먼 무리밖에 없으면 `PickTargetByDistance`가 큰 오차를 감수하고 고른다 → **창을 초과하는 이동 = 늦게 도착 = 연출 붕괴.**

### 시야 판정

`BuildVisibilityTest(cam)` — 실제 절두체(`GeometryUtility`). 무대가 월드 고정이라 각도 근사로는 화면 안인지 알 수 없다(§11-2).

### 테스트

`Assets/02. Scripts/Enemy/Tests/EnemyRingTests.cs` — `PickStagePosition` 4건 포함. `EnemyRing`은 순수 계산이라 무리 로직도 같은 자리에 넣으면 그대로 테스트된다.

---

## 무리 짓기의 설계 축

"무리"를 정의하는 방법이 여럿이고, **`TakeTargetForWindow`와의 충돌 정도가 서로 다르다.**

### A. 무리 중심을 먼저 뽑고, 그 주변에 흩뿌린다
- 무대 안에 `clusterCount`개 중심을 잡고(서로 `clusterSpacing` 이상), 각 적을 어느 중심에 배정한 뒤 `clusterRadius` 안에 배치.
- 거리 스펙트럼: **중심 개수만큼의 이산 대역.** 충돌 큼. `clusterRadius`를 키우면 완화되지만 그만큼 무리로 안 보인다.

### B. 기존 적에 붙여 자라게 한다 (응집 점수)
- `clearance`가 최대인 후보를 고르던 것을 뒤집어, **가장 가까운 적과 `cohesionDistance`에 가장 근접한** 후보를 고른다. `minSpacing`은 하한으로 유지(겹침 방지).
- 무리 개수·크기를 직접 정하지 않고 **자연 발생**시킨다. 코드 변경이 가장 작다(점수 함수 한 줄).
- 거리 스펙트럼: 연속에 가깝게 남는다. 충돌 작음. 다만 "정확히 3무리" 같은 통제가 안 된다.

### C. 씬에 무리 지점을 authoring한다
- `Stage` 밑에 `ClusterAnchor` Transform 몇 개를 두고 거기 주변에 스폰.
- 카메라 구도·인트로 스플라인과 함께 손으로 맞출 수 있다(§7-2와 같은 결 — 좌표는 씬이 안다).
- 무대가 월드 고정이라 성립한다. 충돌은 A와 같다.

---

## 미해결 질문 (Plan 확정 전 필요)

1. **무리를 몇 개, 몇 명씩?** `ringCount`가 6이라 "3무리 × 2명"과 "2무리 × 3명"은 그림이 꽤 다르다.
2. **무리 위치를 코드가 뽑나, 씬이 authoring하나?** (A/B vs C)
3. **`TakeTargetForWindow`의 속도 일정성을 얼마나 포기할 수 있나?** 무리가 촘촘할수록 충돌이 크다. 완화책 후보:
   - `desired` 클램프를 무리 반경에 맞춰 넓히기
   - 무리 안에서는 상대를 거리 대신 **순서**로 고르기(같은 무리를 연속으로 치면 이동이 0에 가까워 §11-5 사슬과 결이 같다)
   - 무리 간 이동만 대시로 취급하고 무리 내는 근거리 난타로
4. **처치 보충 스폰이 원래 무리에 합류해야 하나, 새 무리를 만드나?**
5. **`minPlayerDistance`(4m)와 무리 반경의 관계** — 플레이어가 무리 한복판에 서 있을 때 보충 스폰이 갈 자리가 있나?
