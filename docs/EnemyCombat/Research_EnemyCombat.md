# Research — EnemyCombat (무쌍형 전투로의 전환)

## 0. 목표 (사용자 요청)

- 기존: 패턴 성공 → **오브젝트(표적)가 갈라진다**. 표적은 연출 전용 소품(통나무·푸프).
- 전환 후: **적이 여러 명 등장해 무쌍**. Dead as Disco 류.
- **적의 공격 하나 = 패턴 하나.** 플레이어는 그 패턴을 그어 **패링 또는 공격으로 상쇄**한다.

즉 "표적을 벤다"에서 "**들어오는 공격을 받아친다**"로 사건의 의미가 바뀐다. 판정 규칙 자체는 그대로 쓸 수 있다 — 바뀌는 건 **무엇이 화면에 있고, 성패가 무엇을 의미하는가**다.

---

## 1. 현재 아키텍처: 무엇이 판정이고 무엇이 연출인가

```
ChartPlayer ──SetPattern──▶ PatternHandler ──이벤트──▶ 연출 계층 (판정에 개입 안 함)
 (오디오 시각)               (판정 유일 지점)            ├─ EffectManager   (Canvas 이펙트)
                                                        ├─ CameraDirector  (쉐이크)
                                                        ├─ CharacterActionPlayer (플레이어 애니)
                                                        └─ SliceTargetDirector   (표적 = 이번 전환의 교체 대상)
```

**핵심 사실: 연출 계층은 전부 이벤트 구독자다.** `PatternHandler`는 표적도 캐릭터도 모른다. 따라서 **"표적을 적으로 바꾼다"는 작업은 원칙적으로 `SliceTargetDirector` 자리를 갈아끼우는 일이며, 판정 코드는 손대지 않아도 된다.**

### 1.1 확장 이벤트 (`PatternHandler`)

| 이벤트 | 발행 시점 | 현재 구독자 | 적 전투에서의 의미 |
|---|---|---|---|
| `OnPatternQueued(PatternQueuedInfo)` | 패턴이 큐에 **투입**되는 순간(판정 대상이 되기 훨씬 전) | SliceTargetDirector | **적이 공격을 예고(telegraph)하는 순간** |
| `OnJudgeTargetBegan(JudgeTargetInfo)` | 판정 대상이 선두가 되는 순간 | CharacterActionPlayer | 플레이어 대응 모션 예약 |
| `OnJudged(result, index)` | 노드 하나 판정 | EffectManager | 히트 스파크 |
| `OnJudgeTargetFirstMiss()` | 이번 패턴의 AllCorrect가 처음 깨진 순간(패턴당 1회) | CharacterActionPlayer, SliceTargetDirector, CameraDirector | **적 공격이 플레이어에게 꽂힌다(피격 확정)** |
| `OnPatternComplete(PatternCompletionInfo)` | 패턴 완료/만료 | EffectManager, CameraDirector, SliceTargetDirector | **적 공격의 성패 확정** |
| `OnAllPatternsCleared()` | `ClearAllPatterns()` (곡 중단) | SliceTargetDirector | 전 적 회수 |
| `OnFocusRing*` | 링 스폰/판정/미입력 | (선택) | 노드 단위 연출 |

`PatternQueuedInfo`: `Template / StartTime / FirstNodeTime / LastNodeTime / Deadline`
`PatternCompletionInfo`: `AllCorrect / Template / LastNodeTime / NextLastNodeTime`
`JudgeTargetInfo`: `Template / FirstNodeTime / LastNodeTime / Deadline`

**주의: 어느 페이로드에도 "패턴 인스턴스 ID"가 없다.** `SliceTargetDirector`는 이 때문에 `OnPatternQueued`에서 토큰을 발급하고 `OnPatternComplete`와 **FIFO 큐로 매칭**한다(`pendingTokens`). 적 시스템도 같은 문제를 그대로 물려받는다 — 적 개체와 패턴 인스턴스를 묶으려면 같은 토큰 매칭이 필요하다.

### 1.2 시각 계약 (세 시스템이 공유하는 하나의 식)

```
impactTime = Deadline + Pattern.SliceTargetImpactOffset
Deadline   = LastNodeTime + PatternHandler.GoodWindow(0.10)
```

- `SliceTargetDirector`: 표적이 이 시각에 도착 → 갈라진다.
- `CharacterActionPlayer`: 클립의 **임팩트 프레임**(칼날이 지나가는 프레임)이 이 시각에 오도록 시작 시각·배속을 역산.
- `CameraDirector`: 성공/실패 쉐이크를 이 시각에 예약.

**왜 `LastNodeTime`이 아니라 `Deadline`인가**: 마지막 노드를 goodWindow 안에 늦게 눌러도 Good 성공이다. `LastNodeTime`에 맞추면 정상적인 늦은 입력이 실패로 연출된다. → **적 공격의 "칼이 닿는 순간"도 반드시 이 식을 그대로 써야 한다.** 새 식을 만들면 플레이어 애니·카메라와 어긋난다.

### 1.3 동시성 사실

- 채보 스폰 리드타임 `exposureDuration` 기본 **0.5초**, 엔트리 간 최소 입력 간격 **0.4초** → **항상 최대 2개 패턴이 동시에 살아 있다**(`activePatterns` 큐).
- 그러나 **판정 대상은 언제나 선두 하나**(`JudgeTarget`).
- → **적 전투에서: 화면에 "공격을 걸어 온 적"이 동시에 2체까지 존재할 수 있고, 그중 플레이어가 지금 상대하는 건 언제나 1체다.** 이건 무쌍 연출과 잘 맞는다(대기 중인 적 = 다음 공격 예고).

---

## 2. 교체 대상: SliceTargetDirector는 이미 "적 디렉터"의 골격이다

`SliceTargetDirector`(501줄)의 구조를 적 시스템 요구와 나란히 놓으면:

| SliceTargetDirector가 하는 일 | 적 전투에서 필요한 일 | 재사용 가능? |
|---|---|---|
| `OnPatternQueued`에 예약 생성 + 토큰 발급 | 적 등장·공격 예고 예약 | **그대로** |
| `spawnTime = impactTime − approach`, 접근시간 클램프(`impactTime − StartTime`) | 적이 첫 노드보다 먼저 나타날 수 없다 | **그대로** |
| `spawnAnchor`로 거리 authoring → 속도 파생 | 적 등장 위치 고정, 접근 속도 파생 | **그대로** |
| `Outcome{Pending,Success,Failure}` 확정, Deadline 프레임 1프레임 유예 | 공격 성패 확정 | **그대로** |
| `SliceTarget()` / `Crush()` | **적 사망 / 플레이어 피격** | **의미 교체** |
| 프리팹별 자체 풀 + prewarm | 적 풀링 | **그대로** |
| 기즈모(경로·성패 색) | 동일 | 그대로 |

**결론: 새 시스템을 백지에서 짜지 않는다.** `SliceTargetDirector` → `EnemyDirector`가 자연스러운 진화 경로다. 바뀌는 건 (a) 예약이 참조하는 에셋(`SliceSet` → 적 정의), (b) 확정 시 하는 일(절단 → 적 리액션/사망, 소멸 → 플레이어 피격), (c) 적을 **패턴 하나보다 오래 살릴지** 여부(§4 미결정).

---

## 3. 제약사항 (설계를 실제로 구속하는 것들)

### 3.1 ⚠️ 적은 현재 절단 시스템으로 벨 수 없다

`MeshSliceBakerWindow.cs:536` — 굽기 대상은 `GetComponentInChildren<MeshFilter>()`. **스태틱 메쉬 전용이다.** 적 캐릭터는 `SkinnedMeshRenderer`(본 스키닝)라 이 툴로 조각을 구울 수 없고, 구운다 해도 포즈가 T-Pose로 고정된다.

→ **적 사망 연출은 절단이 아니다.** 선택지: 사망 애니메이션 / 래그돌 / VFX + 페이드. 이건 결정 사항(§5 Q4).
→ `Slice` 시스템(`MeshSliceBaker`, `SliceSet`, `SlicePiece`, 굽기 툴, 유닛테스트)은 **적 전투에서 직접 쓰이지 않는다.** 배경 소품 파괴로 남길지, 통째로 은퇴시킬지 결정 필요.

### 3.2 데이터 소유권이 어긋난다

현재 표적은 **`Pattern` 에셋이 소유**한다(`sliceTarget`, `sliceTargetOffset`, `sliceTargetImpactOffset`). 이유: "표적은 모양에 종속된 정적 데이터"(패턴 모양 = 칼 궤적 = 어떤 절단면인가).

적은 다르다. **`Pattern(8,4,0)`이라는 모양은 곡 안에서 여러 번 재사용되는데, 그때마다 다른 적이 나와야 한다.** 적 정체성은 모양이 아니라 **채보 위치(웨이브)**에 속한다.

→ 적 배정은 `SongChartEntry`(또는 그 위의 웨이브 레이어)로 올라가야 한다. `Pattern`에는 "이 모양은 어떤 종류의 공격인가"(수직 베기/찌르기/횡베기) 정도만 남는 게 자연스럽다.
→ `SongChartEntry`에 필드를 추가하면 **굽기 툴(`PatternChartWindow`)도 그 필드를 편집·보존해야 한다.** 기존 채보 에셋 4종은 필드가 비어 있어도 동작해야 한다(무연출 폴백).

### 3.3 화면 공간이 이미 꽉 차 있다

- 패턴인풋 3x3, 간격 **700px**, `Point_5`가 **화면 정중앙**(Canvas 3840x2160 기준 world (1920,1080)). 격자 전체가 **1400x1400px**를 먹는다.
- 플레이어 캐릭터가 그 뒤에 있고, 표적은 +Z에서 −Z로 날아와 `impactAnchor`에서 갈라진다.
- → **적 여러 명을 어디에 세울 것인가가 실질적 난제다.** 격자 안쪽은 노브·링·가이드라인·입력 라인이 차지한다. 적은 격자 **바깥 링(좌/우/뒤쪽 원호)** 또는 **깊이(Z)로 층을 나눠** 배치해야 한다.
- 카메라는 `Main Camera`(CinemachineBrain + CameraDirector), `CameraCenter`가 별도 존재. 현재 고정 구도.

### 3.4 플레이어 애니메이션 파이프라인은 "패턴당 1클립"에 맞춰져 있다

`CharacterActionPlayer`는 `Pattern.SuccessAnimationClip` **하나**를 임팩트 프레임 기준으로 정렬해 재생하고, 실패 시 `hitClips`를 재생한다. 복귀 경로가 3갈래(연계 O·간격부족 / 연계 O·간격여유 / 연계 X)로 이미 정교하게 튜닝돼 있다.

→ **"패링 애니 / 공격 애니" 구분을 넣으려면 클립 선택 지점만 바꾸면 된다** — 정렬·배속·복귀 로직은 손대지 않는다. 클립 소스가 `Pattern` 고정인 게 유일한 걸림돌인데, 이건 §3.2와 같은 문제다(정보가 채보 엔트리로 올라가야 한다).
→ 현재 보유 클립(`05. Animations/Clip`): `Swipe_*`(8방향), `Stab_5`, `HardAttk`, `ChargeAttk`, `Flurry_Slashes`, `RollingAttack`, `Release`, `Run`, `Sprint_*`. **패링/가드 전용 클립은 없다.**

### 3.5 적 에셋 재고

- `99. External Assets/CombatGirlsCharacterPack/Humanoid_Bot/` — `Humanoid_F.prefab`, `Humanoid_FeKatana.prefab`(카타나 든 마네킹 봇). **적 후보로 즉시 쓸 수 있다.**
- 플레이어: `Char_School_Katana_FullBody-Magica cloth2.prefab`.
- **적용 애니메이션 클립이 없다.** 위 클립들은 전부 플레이어(Attack Layer)용. 적의 "공격 예고 → 휘두름 → 피격/사망" 모션은 **새로 확보하거나, 플레이어 클립을 재사용(휴머노이드 리타깃)** 해야 한다.

### 3.6 손대면 안 되는 것

- `PatternHandler`의 판정 파이프라인 — 연출을 위해 수정하지 않는다는 원칙이 CLAUDE.md에 명시.
- `IngameInputs.cs`(자동생성), `Tiny.Trail`(외부 에셋).
- 노브 표시 기준(합집합) / 판정영역 축소 기준(JudgeTarget) 의 비대칭 — 의도된 것.

---

## 4. 전환의 실질적 쟁점 (Plan에서 답해야 할 것)

### Q1. 적의 수명 — 패턴 1개당 1체인가, 오래 사는가?

- **(a) 1패턴 = 1적 (현행 표적 모델 그대로)**: 적이 달려와 공격하고, 성공하면 그 자리에서 죽고, 실패하면 플레이어를 때리고 사라진다. **구현이 가장 가볍다**(`SliceTargetDirector`의 예약 구조 그대로). 다만 "무쌍"보다는 "표적이 적 모델로 바뀐 것"에 가깝다.
- **(b) 적이 여러 패턴을 산다(HP/웨이브)**: 적이 등장→대기→공격→피격 리액션→다음 공격→사망. 화면에 상시 여러 적. 진짜 무쌍감. 대신 **적 개체 ↔ 패턴 인스턴스 매칭**, 개체별 FSM, 대기 중 적의 idle/이동, 사망 타이밍 등 상태가 대폭 늘어난다.

### Q2. "패링 vs 공격"의 정체

판정 규칙은 하나뿐인데(패턴 완주 성공/실패) 대응이 둘이다. 가능한 해석:
- **(a) 순수 연출 분기** — 적 공격 종류에 따라 성공 시 재생되는 플레이어 클립과 이펙트가 갈릴 뿐, 입력/판정은 동일. (가장 가볍고, 지금 구조에 그대로 얹힌다)
- **(b) 입력 규칙 분기** — 예: 패링은 마지막 노드를 perfectWindow 안에 눌러야 성립, 공격은 완주만 하면 성립. → `PatternHandler` 수정 필요(원칙 위반 소지).
- **(c) 시각 신호만 분기** — 링/노브/가이드 색으로 "이건 패링 공격"임을 알리고 실제 처리는 동일.

### Q3. 적 배정 데이터를 어디에 두는가
`SongChartEntry`에 적 필드 추가 vs 별도 `EnemyWave` 에셋(시각 배정 툴) vs 당분간 `Pattern`에 그대로 두고 나중에 이관.

### Q4. `Slice` 시스템의 운명
적은 벨 수 없다(§3.1). 배경 소품용으로 유지 / 은퇴(코드·툴·테스트·에셋 삭제) / 동결(호출만 끊고 남김).

### Q5. 적 배치 기하 (§3.3)
격자 바깥 원호 슬롯? Z 레이어? 카메라 구도 변경 동반?

### Q6. 적 모션 조달
새 클립 구매/생성 vs 플레이어 클립 리타깃 vs 최소 연출(접근+임팩트 스케일/플래시로 버티기).

---

## 5. 이 전환에서 **바뀌지 않는** 것 (안전지대)

- 패턴 데이터 모델(`Pattern` / `ActivePattern`), 판정 윈도우, 겹침 큐, 포커스 링, 입력 계층 규칙.
- 채보 시스템 코어(`ChartGen.Core`, 굽기 툴의 온셋 분석부).
- `EffectManager` / `CameraDirector` — 카탈로그 한 줄 추가로 새 연출을 받는다.
- `CharacterActionPlayer`의 임팩트 정렬·복귀 3경로 — **클립 선택 지점만 바뀐다.**
- 타이밍 계약(`Deadline` 기준 임팩트 정렬).

---

## 6. 파일 인벤토리 (영향 범위)

| 영역 | 파일 | 예상 영향 |
|---|---|---|
| 신규 | `Enemy/EnemyDirector.cs`, `Enemy/EnemyView.cs`, `Enemy/EnemyDefinition.cs`(SO) | 신규 — `SliceTargetDirector`에서 파생 |
| 수정 | `ChartGen/SongChart.cs` (엔트리에 적 필드) | 소 |
| 수정 | `ChartGen/Editor/PatternChartWindow.cs` (새 필드 편집·보존) | 중 |
| 수정 | `Character/CharacterActionPlayer.cs` (클립 소스 확장) | 소 (정렬 로직 불변) |
| 수정 | `Effect/EffectCatalog.cs`, `Camera/CameraCueCatalog.cs` (트리거 추가) | 소 |
| 은퇴/동결 후보 | `Slice/*` 전체(9파일 + 에디터 툴 + 테스트 + `04. Datas/Slice` 7세트) | Q4에 종속 |
| 씬 | `DefaultScene` — `SliceTargetDirector` 오브젝트 교체, 적 스폰 앵커 배치 | 중 |
| 불변 | `PatternHandler`, `Point`, `FocusRingView`, `PatternLineRenderer`, `Pool`, `InputHandler` | 없음 |

---

## 7. 요약

1. 판정 계층은 **전혀 건드릴 필요가 없다**. 이 전환은 통째로 연출 계층 교체다.
2. `SliceTargetDirector`가 이미 예약·접근·성패확정·풀링을 다 하고 있어 **적 디렉터의 골격으로 그대로 진화시킬 수 있다**.
3. 진짜 신규 작업은 세 가지: **(a) 적 개체 수명 모델, (b) 적 배정 데이터를 채보 엔트리로 올리기(+굽기 툴), (c) 적 모션/배치 기하**.
4. **절단 시스템은 적에게 쓸 수 없다**(스태틱 메쉬 전용). Slice 계열의 거취 결정이 필요하다.
5. 타이밍은 반드시 기존 `Deadline` 식을 그대로 승계한다 — 세 시스템이 이 식 하나로 동기화돼 있다.
