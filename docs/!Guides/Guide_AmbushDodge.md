# Guide — 기습과 회피 (AmbushDodge) 사용법

대상: `ac2dbf0 feat(dodge): 기습 가시성 · 경고 1초 고정 · 후보 선정 개선` 이후 상태.
설계 근거는 `CLAUDE.md` §11-8 · `docs/EnemyAmbushDodge/` · `docs/AmbushVisibility/` · `docs/AmbushSlot/`.
이 문서는 **씬에서 무엇을 꽂고 무엇을 돌리는가**만 다룬다.

---

## 0. 한 줄 요약

플레이어가 서 있는 공백에 적 하나가 끼어들어 찌른다. **경고는 언제나 1.0초**,
회피는 **Space**(또는 화면 우하단 닷지 포인트 클릭). 그 1초 동안 아무 때나 누르면 성공(±판정 없음).

---

## 1. 배선 체크리스트 (`BattleScene`)

### 1-1. `DodgeDirector` 참조

| 필드 | 꽂는 것 | 비우면 |
|---|---|---|
| `actionPlayer` | `CharacterActionPlayer` | **기능 전체 정지**(공백을 못 받는다) |
| `enemyDirector` | `EnemyDirector` | **기능 전체 정지** |
| `mover` | `PlayerCombatMover` | 원호 회피 이동 없음 |
| `dodgePoint` | `DodgePointView` | 링 안 뜸(클릭 입력도 없음) |
| `handler` | `PatternHandler` | 곡 중단 시 정리 안 됨 · 오토퍼펙트 연동 없음 |
| `cameraDirector` | `CameraDirector` | 기습자를 구도에 안 담는다 |
| `inputHandler` | `InputHandler` | **Space 회피 없음**(닷지 포인트 클릭만) |

배선 누락은 **그 층만 조용히 죽는다**(기존 연출 토글 규율). 전체를 끄려면 `dodgeEnabled` 하나.

### 1-2. `DodgePointView` 위치 ⚠

**반드시 `Canvas` 직속.** `PatternHandler`(1400x1400) 자식으로 두면 앵커가 부모 rect 기준이 되어
캔버스 좌표로는 **패턴 영역 한가운데**에 온다(Point_3이 (700,−700)이라 겹친다).
자리는 코드가 화면 우하단으로 고정한다 — `pointMargin`(기본 320,320)이 모서리 여백.

### 1-3. 기습 클립

- 진짜 출처는 **`EnemyDefinition.ambushAttacks`**(적 종류별 `ClipAlignment` 배열).
- `DodgeDirector.fallbackAmbushClips`는 미배선 종류용 폴백.
- **둘 다 비면 그 적은 후보에서 아예 빠진다.** 저작은 `Tools/Animation Clip Trimmer`(Start/Impact/End).

### 1-4. 아웃라인 (기습자 강조)

- 레이어 **`AmbushOutline`**이 프로젝트에 존재해야 한다(`EnemyView.highlightLayerName` 기본값).
  없으면 `LayerMask.NameToLayer`가 −1이라 **아웃라인만 조용히 꺼진다**.
- 그리는 쪽은 URP `RenderObjects` 피처 — `Assets/Settings/PC_Renderer.asset`에서
  그 레이어를 `Assets/Shaders/Materials/AmbushOutlineMaterial.mat`으로 한 번 더 그린다.
- 색·두께 튜닝은 **머티리얼 한 곳**(`_OutlineColor` · `_OutlineWidth`).
- ⚠ **피처의 Depth 옵션을 건드리지 말 것.** `CelOutline`은 메쉬를 부풀려 뒷면만 그리는 셸 방식이라
  깊이 테스트가 겹치는 부분을 잘라내야 테두리만 남는다. `Depth Test = Always`로 열면 **적 표면 전체가 칠해진다.**

### 1-5. 효과음 (현재 미배선 = 무음)

`SfxManager`의 카탈로그 리스트에 세 줄을 추가하면 **코드 변경 없이** 난다.

| 트리거 | 순간 |
|---|---|
| `AmbushTelegraph` (3) | 적이 찌르기 시작 — 링보다 먼저 |
| `DodgeSuccess` (4) | 회피 성공 |
| `DodgeFail` (5) | 회피 실패(맞음) |

⚠ `SfxTrigger` enum의 **정수 값이 직렬화**된다. 순서를 바꾸면 기존 배선이 밀린다.

---

## 2. 튜닝 노브 (`DodgeDirector` 인스펙터)

### 2-1. 경고 시간 — **바꾸려면 두 필드를 같이**

| 필드 | 기본 | 의미 |
|---|---|---|
| `minRingExposure` | **1.0** | 링 최소 노출. 후보 자격(`RequiredLead`)도 이 값을 쓴다 |
| `maxRingExposure` | **1.0** | 링 최대 노출. 실제 노출 = `clamp(리드, min, max)` |

**둘이 같으면 노출이 상수**다 — 지금 의도가 그것이다. 1.2초로 늘리려면 **둘 다 1.2**로 바꾼다.
한쪽만 바꾸면 노출이 리드마다 흔들려 "크기 = 남은 시간" 학습이 깨진다.

⚠ **올리면 빈도가 깎인다.** `RequiredLead = max(와인드업, minRingExposure)`이므로
리드가 모자란 공백은 통째로 탈락한다. 되찾는 방법은 §2-3.

### 2-2. 회피 난이도 · 예산

| 필드 | 기본 | 무엇이 바뀌나 |
|---|---|---|
| `dodgeWindow` | 0.15 | 임팩트 **이후** 유예. 늘리면 관대해진다 |
| `rollDuration` / `rollSpeed` | 0.8 / 1.6 | 구르기 실효 시간 = 0.8÷1.6 = 0.5초 |
| `rollMoveDuration` | 0.25 | 예산에 잡는 **최소** 이동 시간(실제는 창이 허락하는 만큼 늘어남) |
| `margin` | 0.2 | 공백 끝과의 여유 |
| `minIdleWindow` | 0.6 | 요구하는 최소 정지 구간 = `dodgeWindow + rollMoveDuration + margin` |
| `rollDegrees` | 60 | 원호 각도(적 없는 쪽으로) |
| `cooldown` | **3** | 발동 간 최소 간격 |

⚠ `minIdleWindow`를 바꾸면 위 셋의 합과 어긋난다 — 셋 중 하나를 바꿨으면 여기도 맞춰라.

### 2-3. 빈도가 부족할 때 — 순서대로

1. `cooldown` 내리기(3 → 2). 지금 빈도를 붙잡는 **유일한 장치**다.
2. `EnemyDirector.clusterSize`(4) — 무리가 작으면 `active`가 빨리 마른다.
3. `EnemyDirector.stagedMaxDistance`(8m) 내리기 — `staged` 후보가 제때 못 닿아 탈락 중이면 효과.
   ⚠ `minPlayerDistance`(5)가 실질 하한이라 그 아래로 가려면 둘을 같이 내린다.
4. `minRingExposure`/`maxRingExposure` 내리기 — **경고 시간을 파는 것**이므로 마지막 수단.

`minIdleWindow`를 줄여 빈도를 사는 건 하지 마라 — 구르기가 서 있는 구간에 들어가야 한다는 요구는 안 바뀐다.

### 2-4. 배치

| 필드 | 기본 | 의미 |
|---|---|---|
| `stageDistance` | 2.5 | 사전 접근 대기 거리. 결투 거리(≈1m)보다 확실히 커야 '현재 상대'로 오인 안 됨 |
| `lungeDistance` | 1.5 | 찌를 때 멈춰 서는 거리 |
| `lateStageWindup` | 0.3 | 늦은 선정의 도착 마감 여유(= 예상 와인드업) |

---

## 3. 로그 읽기 (`logWindows`, 에디터 전용 · 기본 켜짐)

한 사건이 **세 줄**로 남는다. 셋이 같은 토글을 쓴다.

```
[DodgeDirector] 공백 0.85s (리드 1.10s · 도착까지 -0.12s) → 무시(...) [늦은선정] · (후보 없음 — ...)
[DodgeDirector] 기습 발동 — 적 Enemy_03 · 클립 Stab (트림 0.60s ×1.2) · 와인드업 0.50s vs 링 1.00s (링이 먼저) · 임팩트까지 1.10s · 창 끝까지 1.35s
[DodgeDirector] 회피 성공 · 적 Enemy_03
```

| 보이는 것 | 뜻 |
|---|---|
| `링 1.00s`가 아닌 값 | min/max가 안 맞았다(§2-1) |
| `무시(...)` | 괄호 안이 탈락 사유 — 공백 부족·쿨다운·바쁨 |
| `(후보 없음 — ...)` | 디렉터의 탈락 내역. 후보 선정이 병목이면 여기가 대부분이다(§2-3) |
| `이동마감 +0.40s (임팩트 +1.10s)` | 마감이 임팩트보다 **뒤**면 다른 이동(집결·복귀·후퇴)이 자격을 막는 중 |
| `모션 먼저` / `링이 먼저` | 와인드업 vs 링 노출 순서. 둘을 맞추려 하지 마라(두 배우 규칙) |
| `×2.5 클램프` | 배속 상한에 걸림 — 클립 트림이 창에 비해 길다 |

---

## 4. 증상 → 원인

| 증상 | 보는 곳 |
|---|---|
| 기습이 아예 안 뜸 | `dodgeEnabled` · `actionPlayer`/`enemyDirector` 배선 · 적 `ambushAttacks` 비었나 |
| 링은 뜨는데 Space가 안 먹음 | `inputHandler` 미배선(닷지 포인트 클릭은 될 것) |
| 링이 패턴 노드와 겹침 | `DodgePointView`가 `Canvas` 직속이 아니다(§1-2) |
| 적 몸통이 통째로 칠해짐 | RenderObjects 피처의 Depth Test를 열었다(§1-4) |
| 다음에 나온 적이 계속 빛남 | 풀 반납에서 `SetHighlight(false)` 경로가 끊겼다 — `EnemyView.ResetState` 확인 |
| 소리가 안 남 | 정상. 카탈로그에 클립 미배선(§1-5) |
| 노출 시간이 매번 다름 | min ≠ max(§2-1) |
| 링 떴다가 아무 일도 안 일어남 | 기습자를 결투 상대가 집어갔다. `EnemyDirector.TakeTargetForWindow`의 `HasPendingAction` 스킵이 살아 있는지 확인 |

---

## 5. 손대면 안 되는 것

- **판정 파이프라인.** 회피는 자기 시계로 판정한다. `PatternHandler`는 패턴 판정의 단일 소유자로 남는다.
- **`DodgePointView`에 카메라를 다시 붙이는 것.** 월드→화면 투영을 되살리면 "화면 밖이면 사라짐 ·
  중앙에서 겹침" 두 문제가 같이 돌아온다.
- **`Fire()`/`Finish()`/`Abort()` 중 한 곳만 아웃라인을 끄는 것.** 세 곳(+`ResetState`) 전부 짝이 맞아야 한다.
- **링과 적 클립의 시작을 맞추려는 것.** 둘은 각자 자기 시각에 독립 예약된다(§6 두 배우 규칙).
