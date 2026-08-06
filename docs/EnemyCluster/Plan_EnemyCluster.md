# Plan — EnemyCluster (적을 무리 지어 스폰하기) · v3.1 이동 배치

근거: `docs/EnemyCluster/Research_EnemyCluster.md`
v1(씬 고정 앵커) 폐기 → v2(유동 순간이동) → **v3(유동 + 이동)** → v3.1(목적지 요동 대책 추가)

---

## 결정된 것

| 항목 | 결정 |
|---|---|
| 무리 위치 | **런타임 유동** — 창에서 나온 `desired` 거리에 놓는다 |
| 자리 잡는 방법 | **적이 그 자리로 이동한다** (순간이동 아님) |
| 무리 크기 | **`clusterSize` 기본 4, 인스펙터 조정** |
| 무리 개수 | `active` + `staged` **둘** (`ringCount` = `clusterSize` × 2 = 8) |
| 재배치 시점 | **`active`가 `restageLookahead`명 이하로 줄었을 때만** |
| 방향 | **한 번 정하면 유지**, 거리만 조정 |
| 스폰 위치 | **무리 중심에 최대한 가까운 화면 밖 지점** (무대 가장자리 아님) |
| 스폰 연출 | 스폰 후 자기 자리로 짧게 이동. **파티클 등장은 후속 검토**(이음매만 남긴다) |

---

## 왜 v2에서 바뀌었나

v2는 **화면 밖일 때만 순간이동**이었다. 약점 셋:

1. `staged`가 화면 안이면 아무것도 못 한다 — 재배치가 카메라에 인질로 잡힌다.
2. 절두체 판정이 유일한 방어선이라, 앵글 교체(§7-5) 중 live vcam이 바뀌면 조용히 뚫린다.
3. 순간이동은 한 번이라도 보이면 치명적이다.

**이동으로 바꾸면 셋 다 사라진다.** 보여도 되는 동작이라 카메라를 볼 필요가 없다.

> **카메라 의존은 스폰에만 남는다.** 재배치(연속 이동)는 보여도 무해하지만, **스폰은 팝인**이라 여전히 화면 밖이어야 한다. 실패 양상이 다르다 — 보이는 이동은 아무 일도 아니고, 보이는 팝인은 즉시 티가 난다. 그래서 절두체 판정은 **`SpawnIntoCluster` 한 곳**에만 남기고, 실패해도 "가장 덜 보이는 곳"으로 폴백해 진행한다(기존 `PickStagePosition`과 같은 규율).

> ⚠ **§11-2를 되돌리는 결정이다.** 거기서 등장 이동(`entryDuration`)을 없앤 근거는 *"스폰은 화면 밖에서 즉시 일어난다. 아무도 못 보므로 걸어 들어올 이유가 없다"*였다. v3는 **보이는 것이 목적**이라 근거가 반전된다. 구현 후 `CLAUDE.md` §11-2와 `docs/StageTraversal/` 갱신 필요.

---

## 목적지 요동 (v3에서 새로 생긴 문제)

> 용어 주의: **카메라 쉐이크(§7-1)와 무관하다.** 흔들리는 것은 화면이 아니라 **적의 목적지**다.

```
desired = cruiseSpeed(4.5) × 창 / playerShare(1) + duelDistance
```

창은 음악이 정하고 0.5~2.1초로 **4배** 흔들린다. 연속 패턴에서:

| 패턴 | 창 | desired |
|---|---|---|
| N | 1.3초 | 5.9m |
| N+1 | 0.6초 | 2.7m |
| N+2 | 1.8초 | 8.1m |
| N+3 | 0.9초 | 4.1m |

적 이동 속도 3m/s면 1.3초에 4m — **한 번도 도착하기 전에 다음 명령이 온다.** 걷다 홱 돌기를 반복해 목적 없이 서성이는 그림이 된다.

**v2에는 없던 문제다.** 순간이동은 중간 결과가 화면에 안 남았지만, 이동은 **갱신 한 번이 곧 보이는 동작 하나**다. 그래서 대책은 "부드럽게"가 아니라 **갱신 횟수 자체를 줄이는 것**이다.

### 대책 넷

1. **`restageLookahead`(기본 2)** — `active`에 적이 이 수 이하로 남았을 때만 갱신한다. 그 전에는 `staged`가 가만히 있는다. **갱신 횟수를 근본적으로 줄이는 주 방어선.**
2. **방향 고정** — 한 번 정한 방향은 유지하고 그 직선 위에서 **거리만** 조정한다. 방향 전환이 요동의 가장 추한 부분이고, 거리만 움직이면 "다가왔다 물러났다"로 자연스럽게 읽힌다. 무대를 벗어나거나 `active`와 겹칠 때만 방향을 다시 고른다.
3. **`restageThreshold`(1.5m)** — 새 목적지가 현재 목적지와 이만큼 이상 달라야 재명령.
4. **`restageMinInterval`(0.8초)** — 아무리 달라도 이 간격에 한 번만.

1이 없으면 3·4만으로는 부족하다 — 위 표의 5.9→2.7→8.1은 문턱과 간격을 전부 넘긴다.

---

## 핵심 메커니즘

```
매 BindReservation (창을 아는 유일한 시점):
    activeCluster.Count > restageLookahead      → 아무것도 안 함
    마지막 재배치로부터 restageMinInterval 미만 → 아무것도 안 함

    desired = cruiseSpeed × 창 / playerShare + duelDistance
    direction = 기존 방향 (없거나 무효면 새로 고름)
    target = 플레이어 + direction × clamp(desired, 무대 안)

    |target - staged 현재 목적지| <= restageThreshold → 아무것도 안 함
    → staged 전원에게 각자 자리로 ScheduleMove
```

### 도착이 늦으면?

`BuildDuelPlan`은 이미 `opponent.Destination`(갈 곳)을 쓴다(§11-1). `staged`가 걸어오는 중에 `active`로 승격돼도 **계획이 도착 지점 기준으로 서고** 플레이어가 그리로 간다 — 늦게 오는 것이 구조적으로 깨지지 않는다.

---

## Step 1 — `EnemyRing`에 순수 계산 추가

- [x] `PickClusterDirection(playerPosition, previousDirection, stageCenter, stageRadius, desiredDistance, avoidCenter, avoidRadius, random)`
  → **기존 방향을 우선 반환**한다. 그 방향으로 `desiredDistance`를 갔을 때 무대를 벗어나거나 `active` 무리와 겹칠 때만 새 방향을 고른다. `previousDirection`이 0이면(첫 호출) 새로 고른다.
- [x] `PickClusterCenter(playerPosition, direction, desiredDistance, stageCenter, stageRadius, minPlayerDistance)`
  → 방향은 받고 **거리만 클램프**한다. 무대를 벗어나면 **거리를 줄이는 쪽으로** — 늘리면 창을 초과한다. `minPlayerDistance`가 하한.
  - ⚠ v2의 `isVisible` 인자는 **없다.** 이동이면 화면 판정이 필요 없다.
  - 기존 규율 유지: **"될 때까지 재시도"가 아니라 "가장 나은 후보"**. 실패 경로 없음.
- [x] `PlaceInCluster(center, index, count, clusterRadius, minSpacing)` → 무리 안 개별 자리. **결정적**이어야 한다(랜덤이면 재배치마다 대형이 바뀌어 같은 무리로 안 읽힌다).
- [x] `PickSpawnNearCluster(slot, clusterCenter, stageCenter, stageRadius, playerPosition, minPlayerDistance, viewPosition, viewForward, isVisible, maxOffset, candidateCount)`
  → **자기 자리(`slot`)에서 가장 가까운 화면 밖 지점.** 반경을 0부터 `maxOffset`까지 키워 가며 훑고 **처음 발견한 화면 밖 후보를 즉시 반환**한다(가장 가까운 것이 곧 최선이므로 더 볼 이유가 없다).
  - 자리 자체가 이미 화면 밖이면 **오프셋 0** — 그 자리에 그대로 스폰하고 이동이 없다.
  - 전부 화면 안이면 **카메라 전방 내적이 가장 낮은 후보**로 폴백(기존 규율). 실패 경로 없음.
  - 무대 안 · `minPlayerDistance` 밖 제약은 그대로.
- [x] 기존 `PickStagePosition`은 **지우지 않는다** — 폴백 경로이자 기존 테스트 4건의 대상.

## Step 2 — `EnemyDirector` 필드·상태

- [x] `[SerializeField] private bool clusterEnabled = true;` — false면 전부 기존 경로. **회귀 없음.**
- [x] `[SerializeField] private int clusterSize = 4;` — 툴팁: "한 무리의 적 수. `ringCount`는 이 값 × 2로 파생된다."
- [x] `[SerializeField] private float clusterRadius = 2f;`
- [x] `[SerializeField] private int restageLookahead = 2;` — 툴팁: "`active`에 이만큼 이하로 남으면 다음 무리 자리를 잡기 시작한다. 키우면 일찍 움직이지만 목적지가 자주 바뀐다."
- [x] `[SerializeField] private float restageThreshold = 1.5f;` (m)
- [x] `[SerializeField] private float restageMinInterval = 0.8f;` (초)
- [x] `[SerializeField] private float clusterMoveSpeed = 3f;` — 결투 접근(`cruiseSpeed` 4.5)과 **다른 값**이어야 한다. 이건 배회이지 돌진이 아니다.
- [x] `[SerializeField] private float spawnStagger = 0.15f;` (초)
- [x] **`ringCount`를 `clusterSize × 2`로 파생**시킨다. 독립 필드면 둘이 어긋난 채 조용히 굴러간다.
- [x] `private readonly List<EnemyView> activeCluster, stagedCluster;` · `private Vector3 stagedDirection;` · `private float lastRestageTime;`
  - `ring`(전체 목록)은 그대로 두고 소속만 따로 든다.

## Step 3 — 재배치 실행

- [x] `RestageStaged(window, duelDistance)`를 `BindReservation`의 `TakeTargetForWindow` **직후**에 호출.
  (직전이면 방금 옮긴 무리에서 상대를 고를 수 있다.)
- [x] 게이트 순서: `restageLookahead` → `restageMinInterval` → 방향 유지 판정 → `restageThreshold`. **싼 판정부터.**
- [x] 통과 시 `stagedCluster` 각 적에게 `EnemyView.ScheduleMove`로 자기 자리 지정, `lastRestageTime` 갱신.
- [x] ⚠ **결투 중이거나 예약이 걸린 적은 건너뛴다.** `activeCluster`는 애초에 대상이 아니고, `staged`라도 `hasPendingAttack` 등이 있으면 제외.
- [ ] ⚠ **`EarliestArrival` 적용 여부는 실측으로 정한다.** 지금은 결투 접근에만 걸려 있다(§11-2). 무리 이동은 "빨리 가서 서는" 편이 나을 수 있다. **현재 구현은 안 걸었다** — `clusterMoveSpeed`로 지속시간을 역산한다. Step 8에서 판단.
- [x] 로코모션: 이동 중 `EnemyView.moving`이 true라 기존 `ApplyLocomotion`이 그대로 돈다. **새 스테이트를 만들지 않는다.**

## Step 4 — 순차 스폰 + 짧은 이동

- [x] 새 `staged`를 채울 때 **`spawnStagger` 간격으로 한 명씩** 풀에서 꺼낸다. 넷이 동시에 나타나면 팝인이 티 난다.
- [x] 스폰 위치는 `PickSpawnNearCluster` — **자기 자리에서 가장 가까운 화면 밖 지점**. 거기서 자리까지 짧게 걸어온다.
  - **무대 가장자리에서 걸어오게 하지 않는다.** 그러면 첫 이동만 최대 16m가 되어 `restageLookahead`가 감당할 수 없다(아래 위험 참조).
  - 자리가 이미 화면 밖이면 오프셋 0 → **이동 없이 그 자리에 선다.** 정상 경로다.
- [x] `[SerializeField] private float spawnMaxOffset = 6f;` — 자리에서 이만큼까지만 밀어낸다. 넘으면 폴백(가장 덜 보이는 곳).
- [x] 곡 도중 `Instantiate` 금지 유지 — 전부 풀 대여(`Rent`).
- [x] **등장 연출은 메서드 하나로 격리한다** (`PresentSpawn(EnemyView, Vector3 slot)`). 후속 파티클 등장은 이 메서드만 갈아끼우면 되고, 그때는 오프셋도 절두체 판정도 통째로 필요 없어진다(자리에 바로 나타나면 되므로). 지금 만들지 않는다(YAGNI).
- [x] ⚠ `PrepareStage()` 시점의 절두체는 **인트로 vcam**의 것이다(§7-2 — 카운트다운 동안 인트로가 live). `Camera.main`은 Brain 카메라라 자동으로 그 구도를 본다 — 별도 처리 불필요하지만, 인트로가 무대를 훑는 구간이라 **가려질 곳이 적다.** 폴백이 자주 타는지 Step 8에서 확인한다.

## Step 5 — 핸드오프

- [x] `KillOpponent`에서 `activeCluster`가 비면 `staged` → `active` 승격, 새 `staged`를 Step 4로 채우고 `stagedDirection`을 초기화(다음 무리는 방향을 새로 고른다).
- [x] `PrepareStage()`에서 두 무리를 다 세운다. **카운트다운(하한 3초) 안에 8체 프리웜 + 배치가 들어가는지 확인** — 넘치면 `InitialPoolSize`를 키운다(`countdownDuration`을 늘리지 않는다).

## Step 6 — 기즈모

- [x] `active`·`staged` 무리를 다른 색 원(`clusterRadius`)으로.
- [x] 플레이어→`staged` 목표 지점 선 + `desired` 값 표시 — **지금 거리가 창에 맞는지 눈으로** 본다.
- [x] `stagedDirection` 화살표(방향이 유지되는지 보이게).
- [x] 각 적의 현재 위치→목적지 선(이동 중인지).
- [x] **마지막 재배치가 건너뛰어진 이유** 라벨 — lookahead 미달 / 간격 미달 / 문턱 미달 / 예약 있음. 안 옮겨지는 이유가 안 보이면 디버깅이 불가능하다.

## Step 7 — 테스트

- [x] `PickClusterDirection` — 유효하면 기존 방향 그대로, 무대를 벗어나거나 `active`와 겹치면 새 방향, 첫 호출이면 새 방향.
- [x] `PickClusterCenter` — 결과가 주어진 방향 위, 플레이어에서 `desiredDistance`(클램프 시 그 이하), 무대 안, `minPlayerDistance` 밖. 실패하지 않음.
- [x] `PickSpawnNearCluster` — 자리가 화면 밖이면 **오프셋 0**, 가려야 하면 `maxOffset` 안에서 **가장 가까운** 화면 밖 지점, 전부 화면 안이면 폴백. 결과는 항상 무대 안 · `minPlayerDistance` 밖.
- [x] `PlaceInCluster` — `clusterRadius` 안, 서로 `minSpacing` 이상, **같은 입력에 같은 출력**.
- [x] 기존 `EnemyRingTests` 7종 전부 통과(폴백 회귀).

## Step 8 — 실측 튜닝

- [ ] `Dreamer_Lv10`(86엔트리, 창 min 0.50 / p50 1.30 / max 2.10초)로:
  - **재배치 횟수** (lookahead가 실제로 줄이는지)
  - 무리 이동이 **핸드오프 전에 끝나는 비율**
  - 핸드오프 시점 도착 오차(창 대비)
  - 방향이 다시 뽑히는 빈도
  - **스폰 오프셋 분포** — 0(가릴 필요 없음)이 몇 %인지, 폴백(전부 화면 안)이 몇 %인지. 폴백이 잦으면 팝인이 보인다는 뜻이다.
- [ ] `restageLookahead` · `clusterMoveSpeed` · `restageThreshold` · `restageMinInterval`을 이 숫자로 정한다.

## Step 9 — 문서 갱신

- [x] `CLAUDE.md` §11-2의 "등장 이동을 통째로 없앴다" 문단 갱신.
- [x] `docs/StageTraversal/`에 반영.

---

## 정지 구간 — 그대로 둔다 (확정)

무리 안에서는 거리 선택지가 무리 지름(4m)뿐이라, 창이 길어도 플레이어가 일찍 도착해 서 있는다.
중앙값 창(1.30초) 기준 이동 0.4초 + **정지 0.9초**, `clusterSize` 4면 그 구간이 **3패턴 연속**이다.
CLAUDE.md §11-5가 사슬에서 같은 현상을 경고한다("플레이어 이동이 0이다").

**결론: 노브를 남기고 그대로 간다.** 근거리 난타 3연타 → 무대 횡단 대시라는 완급이 의도이고,
실제로 어떻게 보이는지는 숫자보다 화면이 정확하다. 조일 필요가 생기면 `clusterSize`를 줄이거나
`clusterRadius`를 키운다(후자는 이산화 완화와 같은 노브다). Step 6 기즈모로 확인한다.

## 후속 검토 (이번 범위 아님)

- **파티클 등장 연출.** Step 4의 `PresentSpawn` 하나만 갈아끼우면 된다. 그러면 "화면 밖에서 걸어옴"이 필요 없어져 스폰 위치 제약이 풀린다.
- 무리 대형(뭉친 삼각형 vs 벌린 호) — 기즈모 보고 정한다.

## 열린 위험

- **`restageLookahead` 트레이드오프는 크게 완화됐다.** 스폰이 자리 근처라 **초기 이동이 0에 가깝고**, 남은 이동은 재배치뿐이다. 재배치 거리는 방향이 고정돼 있어 `|Δdesired|`(보통 1~4m)로 묶이지, 무대 횡단 거리가 아니다. 2패턴(≈2.6초, 3m/s로 8m)이면 대부분 덮는다. 그래도 창이 극단으로 튀는 구간(0.5초 ↔ 2.1초 인접)은 남으므로 Step 8에서 실측한다.
- **적이 걸어오다 베인다.** 도착 전 승격이 깨지진 않지만(`Destination` 기반 계획), 느리면 "달려오는 적을 마중 나가 벤다"가 아니라 "걸어오는 적을 쫓아가 벤다"로 읽힌다.
- **`ringCount` 파생화가 기존 씬 값을 덮는다.** 인스펙터에 저장된 6이 조용히 8이 된다 — 의도된 변경이지만 프리웜 비용이 함께 는다.

---

## 피드백

`>>>`로 의견 남겨주면 반영해서 다시 쓴다. 확정 전에는 구현하지 않는다.

---

# v4 — 무리 이동 폐기, 사망 1 : 스폰 1 (플레이 피드백 반영, 구현 완료)

## 무엇이 틀렸나

v3.1은 **무리 전체를 통째로 옮겼다**(`RestageStaged`). 실제로 플레이해 보니 **적들이 우르르 몰려다니는 그림**이라 폐기.

목적지 요동 대책 넷(`restageLookahead`·방향 고정·`restageThreshold`·`restageMinInterval`)은 **요동을 줄였을 뿐 이동 자체를 없애지 못했고**, 문제는 요동이 아니라 **집단 이동이라는 동작 자체**였다. 노브로 못 고치는 종류였다.

## 새 모델

```
[곡 시작]
  무리 하나(active, clusterSize명)만 세운다
  집결지(stagedCenter)를 지정한다 — 여기에 다음 무리가 모인다

[적 사망]
  active에서 걷어낸다
  적 하나를 태운다 → 집결지의 다음 빈 자리로 걸어간다   ← 사망 1 : 스폰 1
  active가 비었으면:
      staged → active 승격
      새 집결지를 지정한다

[반복]
```

- **집결지는 무리가 찰 때까지 안 움직인다.** 거리를 음악에 맞추는 일은 지정 한 번이 하고, 그 뒤로는 아무도 안 옮겨진다.
- **총원은 `clusterSize`로 불변**이다(`active` 잔여 + `staged` 집결). 그래서 무리가 차는 순간과 `active`가 비는 순간이 구조적으로 일치한다 — 세 번째 무리가 생길 여지가 없다.
- 자리는 **이미 모인 인원 수**가 정한다. 무리가 채워지는 순서대로 대형이 완성된다.

## 삭제된 것

`RestageStaged` · `restageLookahead` · `restageThreshold` · `restageMinInterval` · `lastRestageTime` · `lastRestageSkip`,
그리고 v3.1 문서의 "목적지 요동" 절 전체(이제 발생 여지가 없다).

`PickClusterDirection`의 **방향 유지 기능은 남아 있지만 안 쓴다** — 집결지 지정 때마다 `previousDirection`에 0을 넘겨 매번 새로 뽑는다. 무리가 통째로 안 움직이므로 유지할 이유가 없다. 함수와 테스트는 그대로 두었다(폴백·회귀 가치).

## 검증

- EditMode 테스트 **115/115 통과**, 컴파일 에러 0.
- ⚠ **플레이 검증은 아직 안 했다.** v3.1도 테스트는 통과했지만 화면에서 틀렸다 — 이 모델도 눈으로 봐야 확정된다.

## 남은 노브

`clusterSize`(4) · `clusterRadius`(2m) · `clusterMoveSpeed`(3m/s) · `spawnMaxOffset`(6m) · `spawnStagger`(0.15초, 곡 시작 첫 무리에만 쓰인다).
