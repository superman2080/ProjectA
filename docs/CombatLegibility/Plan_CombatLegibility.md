# Plan — CombatLegibility

근거: `docs/CombatLegibility/Research_CombatLegibility.md`

## 방침 (변경됨)

**적 무리 시스템(§11-6)을 걷어낸다.** Research 2-3은 "무리 자체는 범인이 아니다"로 결론냈지만 그 판단을 뒤집는다 —
무리가 남아 있는 한 **화면 위 인원이 줄지 않고**(사망 1 : 스폰 1), **후보 넷이 2m 안에 뭉쳐** 거리 선택이 임의로 보이며,
**집결·승격·보충이라는 세 종류의 이동**이 상시 돌아간다. 셋 다 무리의 존재 이유에 직결돼 있어 노브로는 못 뺀다.

새 모델은 하나다.

> **적은 곡 시작 전에 전원 태어나 플레이어를 중심으로 배회한다.
> 그중 지금 패턴이 요구하는 하나만 표적이 되고, 나머지는 예비 적이다.**
>
> **그리고 적 수는 채보가 정한다 — 엔트리마다 죽는 적 수의 <b>합</b>이고, 지금은 `killOnSuccess`면 1 · 아니면 0이라
> 결과적으로 `killOnSuccess` 엔트리 수와 같다.
> 마지막 엔트리에서 마지막 적이 죽고(= 마무리 실루엣, §14), 잔여 적은 남지 않는다.**

**⚠ 한 패턴이 여럿을 상대하는 것은 나중에 구현한다.** 이 Plan은 그것을 **앞당기지도, 막지도 않는다** —
지금 "하나"라고 못박을 자리마다 **"이 패턴이 상대하는 적들"이라는 말로 적어 두고**, 실제 코드는 1인 채로 둔다.
어디를 고치게 되는지는 **Step 6**에 적어 뒀다.

바뀌는 것:

| | 지금 | 바뀐 뒤 |
|---|---|---|
| 생성 | 첫 무리만 · 사망마다 1명 보충 | **곡 시작 전 전원**(`PrepareStage`) · 보충 없음 |
| 목록 | `activeCluster` / `stagedCluster` | **`ring` 하나** |
| 비표적의 일 | 궤도 배회 **+ 집결 이동 + 승격** | **궤도 배회만** |
| 인원 | 항상 `clusterSize × 2` | **벨 때마다 하나씩 줄어 0이 된다** |
| 인원의 출처 | 씬 노브(`ringCount`) | **채보**(엔트리별 처치 수의 합) |

**배회는 남긴다.** 마네킹 문제(`docs/EnemyIdleWander`)가 그대로 살아 있고, 궤도는 **플레이어를 중심으로** 도는 것이라
무리 모델과 애초에 무관하게 성립한다. 지우는 것은 **집결·승격·보충**, 즉 무리가 무리이기 위해 하던 이동들뿐이다.

이것이 Research 4의 **#3·#4를 해결한다** — 후보가 2m 안에 뭉쳐 있지 않아 거리 선택이 임의로 안 보이고,
인원이 0까지 줄어 "정리했다"가 화면에 남는다. **무대가 실제로 비는 것이 곡의 끝이다.**

**⚠ 대가를 알고 받는다 — #1·#2는 무리 제거만으로는 안 풀린다.** 배경(비표적)이 계속 도는 한 표적의 "멈춤"은
여전히 신호가 못 된다(Research 1-(a)·(c)). **그래서 Step 4(가드 자세)까지가 이번 작업의 범위다.**
배회가 남으므로 Step 0의 노브(`orbitSpeed`·`wanderSpeed`·`standoffDistance`)도 계속 유효한 손잡이다.

---

## Step 0 — 코드 0줄, 씬 노브만 (측정)

무리를 걷어내는 일이 큰 작업이므로, **그 전에 노브만으로 어디까지 되는지 먼저 본다.**

- [ ] `EnemyDirector` 인스펙터에서 아래를 바꿔 한 곡 플레이한다.

| 필드 | 현재 | 시험값 | 이유 |
|---|---|---|---|
| `orbitSpeed` | 15 | **6** | 궤도 이동이 시선을 덜 끈다 |
| `wanderSpeed` | 1.3 | **0.6** | 걷는 티만 남기고 이동량을 줄인다 |
| `standoffDistance` | 3.5 | **4.5** | 비표적을 결투 통로 밖으로 민다 |

- [ ] 결과를 여기 한 줄로 적는다. **이 값들은 Step 1~4 뒤에도 그대로 유효하다**(배회가 남으므로).

---

## Step 1 — 무리 경로를 삭제하고 비무리 경로를 주 경로로 승격

`clusterEnabled = false` 경로는 **이미 "전원 미리 세운다" 그 자체다**(`PrepareStage`의 else 분기 →
`SpawnIntoStage()` × `RingCapacity`). 새로 만드는 게 아니라 **남길 것을 고르는 작업**이고, diff의 대부분이 삭제다.

- [x] `EnemyDirector` 필드 삭제: `clusterEnabled` · `clusterSize` · `clusterRadius` · `clusterMoveSpeed` ·
      `stagedReturnDuration` · `stagedMaxDistance` · `spawnStagger` ·
      `activeCluster` · `stagedCluster` · `stagedCenter` · `stagedDirection` · `reinforcedThisCluster` · `lastDesiredDistance`.
- [x] 메서드 삭제: `SpawnCluster` · `SpawnOneIntoStaged` · `DesignateStagedCenter` · `PromoteClusterIfEmpty` ·
      `ReinforceActiveIfThin` · `ForgetFromClusters` · `ClusterCenterOf`.
- [x] `EnemyRing`에서 호출자가 사라지는 것 삭제: `PlaceInCluster` · `PickClusterCenter` · `PickClusterDirection` ·
      `PickSpawnNearCluster`(+ 해당 테스트). **`PickStagePosition` · `PickOrbitSlot` · `PickTargetByDistance` ·
      `SeparationPush`는 남는다.**
- [x] **적 수를 채보에서 받는다** — `PrepareStage(int rosterCount)`. `ChartPlayer`가 이미 카운트다운에서 부르고
      거기서 채보를 들고 있으므로(`ActiveChart`), 셈은 그쪽에서 한 줄이다:
      `entries.Sum(e => e.enemyCue.killOnSuccess ? 1 : 0)`. **`ringCount` 필드는 지운다**(`clusterSize × 2` 파생도 같이).
      ⚠ **`Count(…)`가 아니라 `Sum(…)`으로 적는다.** 값은 똑같지만, 다수 상대(Step 6)에서 엔트리 하나가
      N명을 죽이게 되면 **그 한 항만 바뀐다** — `Count`로 적어 두면 셈의 뜻 자체를 다시 정해야 한다.
      ⚠ 인자가 0 이하면(디버그 투입·채보 없음) **4명**을 세운다 — `Debug: Prepare Stage`가 그 경로다.
      상수 하나면 충분하다(인스펙터 필드로 만들지 않는다 — 정상 경로에서는 아무도 안 읽는 값이다).
- [x] `PrepareStage`: 분기를 없애고 `for (i = ring.Count; i < rosterCount; i++) SpawnIntoStage();` 하나만 남긴다.
- [x] `TakeTargetForWindow`: `restrict`(무리로 후보 묶기) 블록과 그 폴백을 지운다 — 후보는 `ring` 전체다.
      **`HasPendingAction` · `SpawnWarping` 필터는 남긴다**(기습자·등장 중인 적을 벨 수 없는 것은 무리와 무관한 규칙이다).
- [x] `PickIdleAmbusher`(§11-8): `includeStaged` 인자와 `stagedCluster` 2차 수집을 지우고 `ring` 하나만 본다.
      `ReleaseAmbusher`는 집결지 대신 **그 적의 배회 슬롯**으로 돌려보낸다(Step 2가 매 프레임 다시 잡아 주므로
      실제로는 `StopWander` 해제만으로 충분하다 — 확인하고 불필요하면 메서드째 지운다).
- [x] **`Encounter.clusterSizeOverride`와 `SetClusterSize`를 통째로 지운다.** 그 필드의 존재 이유가
      "한 씬에 `Encounter`가 둘이라 적 수를 씬 단위로 못 둔다"(§13의 4-b 「면을 쓴 자」)였는데,
      **적 수가 채보에서 나오면 그 문제 자체가 사라진다** — 4-b는 자기 `SongChart`를 들고 있다.
      CLAUDE.md §13의 "그가 요구하는 신규 코드는 이 필드 하나뿐"은 **0개가 된다(확정)**.

**⚠ `SpawnIntoStage`는 한 줄도 안 고친다.** 절두체 밖 스폰·균열 등장(§11-11)·`SetGazeTarget`·`ApplyBackgroundBudget`이
전부 그 안에 이미 있다.

---

## Step 2 — 배회를 `ring` 전체로 되돌린다

배회는 **삭제가 아니라 대상 변경**이다. 지금은 `activeCluster`만 돌고 `staged`는 집결지에 서 있어야 해서 제외돼 있는데,
그 구분이 사라지므로 **교전 중이 아닌 적 전원**이 돈다.

- [x] `TickWander`: `clusterEnabled` 가드와 `stagedCluster` 정리 루프를 지우고, 순회 대상을 `activeCluster` → `ring`으로 바꾼다.
      `currentOpponent` 제외와 **슬롯 인덱스를 리스트 순서로 고정**하는 규칙은 그대로다(매 프레임 재배정하면 서로를 가로지른다).
- [x] `wanderEnabled` · `wanderSpeed` · `orbitSpeed` · `orbitReverseInterval` · `orbitJitter` · `standoffDistance` ·
      `JitterOf` · `EnemyRing.PickOrbitSlot` · `EnemyView.SetWander`/`StopWander` — **전부 남긴다.**
- [x] §11-7의 상체 마스크는 조건(`walkingNow`)이 그대로라 **한 줄도 안 고친다.**
- [x] **⚠ 궤도 슬롯 수가 `ring.Count`라 인원이 줄면 간격이 벌어진다**(`PickOrbitSlot`이 `count`로 원주를 나눈다).
      Step 3에서 인원이 **0까지** 줄므로 후반에는 `ring`이 1~2명이다 — **마지막 한 명이 반대편에 혼자 서는 그림**이
      나올 수 있고, 이제 그것이 **곡의 마지막 몇 패턴 = 가장 잘 보이는 구간**에 온다.
      **먼저 실측하고**, 문제가 되면 `standoffDistance`를 인원에 따라 줄인다(코드 0줄로는 안 된다).

**⚠ 이격(§11-2 `TickSeparation`)은 그대로다.** `separationRadius`(1m)가 `standoffDistance`(3.5m)보다 한참 작아야
배회 슬롯과 상시로 싸우지 않는다는 기존 규칙이 계속 유효하다.

---

## Step 3 — 인원이 줄게 한다 (사망 보충 삭제 + 마지막 처치 보장)

- [x] `KillOpponent`의 보충 블록(`ForgetFromClusters` → `SpawnOneIntoStaged` → `PromoteClusterIfEmpty` →
      `ReinforceActiveIfThin`, 그리고 `else if (ring.Count < RingCapacity) SpawnIntoStage()`)을 전부 지운다.
      **죽으면 그냥 준다. 다시 태우지 않는다.**
- [x] **마지막 엔트리는 `killOnSuccess`여야 한다** — `ValidateForSave`에 **저장 차단** 한 항목을 더한다
      (`PatternChartWindow.Loop.cs`, 기존 "마지막 엔트리가 통과 패턴" 검사 바로 아래. **두 모드 공유**).
      이것이 **"마지막 하이라이트에 마지막 적이 죽는다"를 데이터로 못박는 유일한 장치**다 —
      §14의 실루엣 트리거가 `마지막 엔트리 + AllCorrect`이므로 그 엔트리가 처치를 들고 있으면
      **런타임에 맞추는 코드가 0줄**이다(기존 검사 ②와 같은 관용구: 자동 보정이 아니라 차단 + 안내).
- [x] 요약 줄에 **`적 N명`**을 찍는다. 저작자가 그 무대에 몇 명이 서는지를 채보 화면에서 본다.
      ⚠ 라벨을 `killOnSuccess N`이 아니라 **`적 N명`**으로 쓴다 — 다수 상대에서 둘이 갈라진다.

**⚠ 잔여가 생기는 경우는 하나뿐이고, 그것도 없앤다.**

| 모드 | 실패한 처치 엔트리 | 결과 |
|---|---|---|
| `Loop`(§16, 스토리 기본) | **재시도된다** — 결국 전부 성공한다 | 잔여 0. **구조적으로** 보장된다 |
| `Linear`(커스텀) | 소비되고 넘어간다 | 적이 남는다 → **아래 한 줄로 없앤다** |

- [x] **`DissolveAll` 호출을 `outroHold` <b>앞</b>으로 옮긴다**(`ChartPlayer`의 종료 상태기, §16).
      지금은 `outroHold` → 실루엣 대기 → 오디오 페이드 **뒤**에 부르는데, 그 순서면 **마무리 실루엣이 터지는
      프레임에 남은 적이 서 있다.** 앞으로 옮기면 마지막 일격의 임팩트 직후에 스러지기 시작해
      **실루엣 노출 구간 안에서 사라진다** — 화면에는 *"마지막 일격에 나머지가 같이 스러진다"*로 읽힌다.
      **새 연출이 0개다**(§11-3의 소멸을 부르는 시점만 바뀐다). `Loop`에서는 이미 아무도 안 남아 **no-op**이다.
      ⚠ 소멸 중인 적이 배회를 계속하면 안 된다 — `TickWander`가 `IsIdle`이 아닌 적을 이미 건너뛰는지 확인한다.

- [x] **마름 방지 가드는 그대로 남긴다**: `ring.Count == 0 && currentOpponent == null`인데 처치 엔트리가 남아 있으면
      한 명 세우고 `Debug.LogWarning`. 위 표대로면 일어나지 않지만(적이 <b>모자라는</b> 경우는 셈이 어긋났을 때뿐),
      채보와 런타임이 어긋난 순간
      **전투가 통째로 무연출이 되는 것**만은 막는다.

> `ponytail:` 곡 도중 `Instantiate` 0회라는 §5 규율은 가드 경로에서만 깨진다 — 프리웜된 풀에서 빌리므로
> 실제 `Instantiate`는 아니고, 그것도 저작이 어긋난 경우에만 일어난다.

---

## Step 4 — 표적에게 가드 자세를 준다 (이번 범위에 포함 · 새 에셋 0개)

배회가 남으므로 **Research 1-(a)가 그대로 살아 있다** — 표적이 얻는 시각적 사실이 "멈춤" 하나인데 배경이 움직인다.
무리를 걷어내도 이 증상은 안 없어지므로, **이 Step까지가 이번 작업의 범위다**(측정 뒤로 미루지 않는다).

- [x] 클립은 이미 있다 — §11-7이 배회 상체용으로 쓰는 `Samurai_BlockIdle`.
      `AssignAttack`/`AssignFeint`의 **무연출 경로**(클립이 없거나 창이 `minFeintWindow`보다 짧을 때)에서
      Idle 대신 그것으로 크로스페이드한다.
- [x] 같은 순간 `LookAtInstant(플레이어)`로 **즉시** 정면을 잡는다. `gazeTurnSpeed`로 돌면 표적이 된 순간이 흐려진다.
- [x] §11-7의 배회 상체 클립 뽑기에서 **`Samurai_BlockIdle`을 뺀다.** 비표적이 같은 자세를 쓰면 뜻이 둘이 된다 —
      **가드 자세 = 지금 표적**이 유일한 규칙이어야 한다.
- [x] 해제: `Resolve` · `MarkDying` · `ResetState`(풀 반납). 안 풀면 다음 대여가 가드 자세로 나온다
      (§11-8 아웃라인과 같은 함정).
- [x] `EnemyFeint`(§11-4)는 손대지 않는다 — 배선돼 있으면 그쪽이 이긴다.
      **폴백을 Idle에서 가드로 바꾸는 것**뿐이라 기존 패턴 회귀가 0이다.

---

## Step 6 — 다수 상대를 막지 않는다 (지금 구현하지 않음 · 기록)

한 패턴이 적 여럿을 동시에 상대하는 것(광역 베기 · 연타로 주변을 쓸기 · 사슬 중 둘이 덤빔)은 **나중 작업**이다.
여기서는 **이번 개편이 그 길을 좁히지 않는지만 확인**하고, 실제로 고칠 자리를 적어 둔다.

**이번 개편이 오히려 유리하게 만드는 것**

- **`ring` 하나로 합쳐진다** — 다수 상대의 후보는 "무대에 서 있는 적 전부"인데, 지금은 `active`/`staged`로 갈려 있어
  *"어느 목록에서 고르는가"*를 매번 정해야 했다(§11-8의 `includeStaged`가 그 흔적이다). 목록이 하나면 그 질문이 사라진다.
- **적 수가 채보에서 나온다** — 한 엔트리가 N명을 죽이게 되면 `Sum` 한 항만 바뀐다(Step 1·3에 그렇게 적어 뒀다).
- **집결·승격이 사라진다** — 여럿을 상대하는 도중 무리가 승격되면 표적 중 일부가 이동 중이 된다. 그 경우가 없어진다.

**그때 실제로 고칠 자리 (지금은 손대지 않는다)**

| 자리 | 지금 | 다수 상대에서 |
|---|---|---|
| `EnemyDirector.currentOpponent` | 필드 하나 | **목록**. 여기가 시작점이고, 아래 넷이 전부 이 필드를 읽는다 |
| `TickWander`의 `view == currentOpponent` 제외 | 하나 제외 | 교전 중인 **전원** 제외 |
| `TickSeparation`의 불가침 캡슐(§11-2) | 플레이어–상대 통로 하나 | 상대마다 통로 하나 (`SeparationPush`는 이미 선분 단위라 **호출을 N번 하면 된다**) |
| `CameraDirector` 프레이밍(§7-2) | `TargetGroup` **고정 2칸** | 칸이 늘어난다. ⚠ 그 "고정 2칸"에는 *"넣었다 뺐다 하면 바운즈가 계단식으로 튄다"*는 근거가 있다 — **최대 인원만큼 칸을 미리 잡고 가중치로 여닫는** 것이 그 근거를 지키는 방법이다 |
| `BuildDuelPlan` · `Pattern.duelDistanceCurve`(§11-9) | 적 하나와의 간격 | **여기가 가장 깊다.** 간격이 `enemy − player(t)`라는 *정의*라서 적이 둘이면 정의가 성립하지 않는다 — 대표 한 명(예: 가장 가까운 적)을 정해 곡선을 구동하고 나머지는 자기 자리에 서는 것이 가장 얕은 답이다 |

**⚠ 이번 개편에서 "하나"를 더 깊이 박지 않는다.** 위 목록이 늘어나는 변경은 이 Plan 범위 밖이다.
반대로 **지금 추상화를 미리 만들지도 않는다**(`IsEngaged(view)` 같은 술어를 예비로 두지 않는다) —
다수 상대의 요구가 확정되기 전에 만든 인터페이스는 그때 다시 지우게 된다.

---

## Step 7 — 열지 않는 문 (기록만)

- **아웃라인(`SetHighlight`) · 표적 위 월드 UI 마커**: 안 한다. 아웃라인은 기습자 강조 전용이고(§11-8) 겹치면
  "공격해 온다"는 뜻이 흐려진다. 월드 추종 마커는 루트 Canvas가 `ScreenSpaceOverlay`라는 규율(§7-5) 밖으로 나가는 첫 물건이 된다.
- **적 외형으로 역할 가르기**(Dead as Disco의 Baton Guard). 우리 역할(`Attacker`·`CountersOnFail`)은 **패턴이 소유**하므로(§5)
  같은 적이 엔트리마다 역할이 바뀐다. 외형에 묶으면 채보 저작이 적 배치에 종속된다.
- **넉백·군중 산란**: 결투는 `transform.position` 대입이고 도착 시각 계약(§6)이 있다. 물리 산란은 그 계약을 깬다(§11-2).

---

## 문서 갱신

- [x] `CLAUDE.md` §11-6(EnemyCluster)을 새 모델로 다시 쓰고, §11-2·§11-8의 `active`/`staged` 언급을 `ring`으로 정리한다.
- [x] `CLAUDE.md` §5의 저장 검사 목록에 **"마지막 엔트리가 `killOnSuccess`가 아니다"(중단)**를 더하고,
      §13에서 **`clusterSizeOverride`가 사라졌다**는 것을 반영한다(「면을 쓴 자」의 신규 코드가 0개가 된다).
- [x] `docs/EnemyCluster/`에 **폐기 표시**를 남긴다(지우지 않는다 — 왜 만들었고 왜 되돌렸는지가 근거다).
      `docs/EnemyIdleWander/`는 **살아 있다** — 배회는 남으므로 건드리지 않는다.

## 검증

- [ ] 한 곡을 끝까지 플레이해 **"내가 벨 적이 누구인지 칼이 닿기 전에 알 수 있는가"**. 이 질문이 성공 기준이다.
- [ ] **마지막 엔트리에서 마지막 적이 죽고 무대가 정확히 비는가.** 실루엣(§14)이 터지는 그 프레임에
      살아 있는 적이 0이어야 한다.
- [ ] **`StageMode.Linear`에서 처치 패턴을 일부러 실패**시켜, 남은 적이 실루엣 구간 안에서 스러지는지 본다
      (Step 3의 `DissolveAll` 시점 이동).
- [ ] **인원이 1~2명일 때 궤도가 어색하지 않은가**(Step 2의 경고). 곡 후반이라 가장 잘 보인다.
- [ ] `StageMode.Loop`에서 **일부러 처치 패턴을 실패**해 재시도 뒤에도 인원 셈이 맞는지 본다
      (취소된 패턴이 처치로 세어지면 적이 먼저 마른다).
- [ ] Step 6의 표가 **그대로인지** 확인한다 — 구현 중에 `currentOpponent`를 읽는 자리가 늘었다면 여기 적는다.
- [ ] 기습(§11-8)이 여전히 발동하는가 — 후보가 `ring` 전체가 되어 `ReinforceActiveIfThin`이 풀던 문제 자체가 사라져야 한다.
- [ ] 짧은 창(0.5초)과 긴 창(2.1초)이 섞인 곡에서 **대시 거리가 여전히 갈리는가**(무리를 지웠을 때의 거리 선택 실측).
- [ ] `EnemyFeint`가 배선된 엔트리와 안 된 엔트리를 섞은 곡에서 **저작 상태와 무관하게** 표적이 읽히는가(Research 4의 #5).
- [ ] 오토플레이(§8)로는 실패 경로가 안 나온다. 실패 시 적이 패링하는 그림도 눈으로 확인한다.
