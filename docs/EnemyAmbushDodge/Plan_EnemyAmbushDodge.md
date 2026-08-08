# Plan — EnemyAmbushDodge (플레이어 애니메이션 공백에 들어오는 기습과 회피)

근거: `docs/EnemyAmbushDodge/Research_EnemyAmbushDodge.md`

## 정정 — "공백"의 정의가 바뀌었다

초안은 공백을 **채보 공백**(패턴이 하나도 안 살아 있는 구간)으로 잡았다. **아니다.**
여기서 말하는 공백은 **적에게 도달한 뒤 ~ 다음 액션 클립이 시작되기 전까지, 플레이어가 서 있기만 하는 구간**이다.

```
공백 시작 = max(recoveryEndTime, convergeUntil, Time.time)      // 마무리 노출도 끝나고 도착도 끝난 시점
공백 끝   = pendingScheduleStart (클립 슬롯이 비면 Deadline)     // 다음 액션 클립이 시작되는 시각
            ⚠ 첫 노드로 자르지 않는다 — 정정 3 참조(그 캡이 창을 0.15초 상수로 만들었다)
```

이 변경의 결과:

- **`ChartPlayer`는 이 기능과 무관해졌다** — 초안의 `TimeUntilNextEntry`는 폐기한다.
- **`PatternHandler`도 안 건드린다** — 초안의 `HasActivePattern`도 폐기.
- **공백을 아는 클래스는 `CharacterActionPlayer` 하나다**(네 값을 동시에 아는 유일한 곳, Research §1).
  → 새 이벤트 **`OnIdleWindow(start, end)`** 하나가 이 기능 전체의 트리거다. 폴링이 사라진다.
- **§11-6 무리 배치가 이 공백을 키운 원인**이다 — 이동이 0에 가까워지면서 대시가 채우던 시간이 통째로 정지가 됐다.

## 정정 2 — 적 이동은 링과 병렬이 아니다 · 텔레그래프를 먼저 보여준다

초안은 "링이 수축하는 동안 적이 달려오면 되니 이동은 공짜"라고 했다. **틀렸다** — `ApproachDuel`은
**클립 시작 전에** 이동을 끝낸다(스윙 중 기하가 바뀌면 칼이 어긋나므로). 이동·와인드업·판정·구르기가 전부 **직렬**이라
실제 요구는 1.85초가 아니라 **≈2.3초**다. 그 창은 거의 없다.

그래서 구조를 셋으로 고친다(Research §4-2):

- **(A) 링 노출을 적 클립에서 뗀다** — 적 클립은 `impact − 와인드업`에 먼저 시작해 **텔레그래프**가 되고,
  링은 `impact − ringExposure`(0.4초)에 **늦게** 뜬다. **모션이 "온다"를, 링이 "지금"을 맡는다.**
  링 수축 속도가 패턴 링과 같아져 학습한 감각이 유지된다. ⚠ 이것만으로는 총 예산이 안 준다.
- **(B) 이동을 창 밖으로 뺀다 — 사전 접근** — 기습할 적은 지금 상대가 아니라 아무도 안 쓴다.
  **⚠ 기준점은 플레이어의 '지금 위치'가 아니라 '갈 위치'다.** `EnemyDirector.OnDuelScheduled`의
  `DuelPlan.PlayerPosition`/`PlayerArriveTime`(둘 다 이미 public)으로 **역산해서** 붙인다.
  현재 위치로 붙이면 플레이어가 `cruiseSpeed × 창`만큼 대시해 **적만 뒤에 남고**, 이동이 창 안으로 되돌아온다.
  `OnPatternQueued`는 **못 쓴다** — 그 시점엔 상대 배정 자체가 안 끝났다(§11-1).
  둘 다 그냥 이동이라 이번엔 **진짜로 병렬**이고, **0.67초가 창에서 통째로 빠진다.**
- **(C) 구르기 압축** — `PlayOneShot(clip, 1.6)`로 0.8 → 0.5초.

→ 필요시간 `와인드업 0.5 + 판정 0.15 + 구르기 0.5 + margin 0.2 ≈ 1.35초`.

## 정정 3 — 첫 노드 캡이 기능을 죽였다 (플레이 실측 후)

1차 구현은 **한 번도 발동하지 않았다.** 로그 전부 `무시(창 부족)`, 창 0.10~0.50초.

**원인은 예산이 아니라 창 계산의 캡이었다.** `end = min(pendingScheduleStart, FirstNodeTime)`의 `FirstNodeTime`.

채보 실측(`Dreamer_Lv10`, 89개 연결):

| 구간 | min | p50 | p90 | max |
|---|---|---|---|---|
| 앞 패턴 마지막 노드 → **다음 첫 노드** (= 그 캡) | 0.40 | **0.40** | 0.40 | 1.60 |
| 앞 패턴 마지막 노드 → 이번 임팩트 (W) | 0.50 | 1.30 | 2.10 | 2.10 |

**89개 중 83개가 정확히 0.40초**다. 거기서 `recoveryHoldDuration`(0.25)을 빼면 로그의 0.10~0.28이 그대로 나온다 —
필요시간을 0.15초로 줄여도 영원히 안 뜬다. **캡이 창을 상수로 만들었다.**

시간의 공급처는 전부 **노드를 입력하는 구간 안**에 있다. 플레이어 공격 클립은 첫 노드가 아니라
`임팩트 − 와인드업`(템플릿 실측 p50 **0.26초**)에 시작하므로, 첫 노드부터 클립 시작까지 캐릭터는 서 있기만 한다.

**A. 캡 제거** — 창의 끝을 `pendingScheduleStart` 하나로 잡는다(클립 슬롯이 비면 `Deadline`).
결과: 빈 구간 p50 **0.54초**, ≥0.8초 45%, ≥1.0초 25%.
**대가는 회피가 패턴 입력 도중에 뜬다는 것**이고, 실측상 그걸 피하면 기능이 존재할 수 없으므로 요구로 받아들인다.

**B. 구르기를 예산에서 뺀다** — "피했다"는 임팩트에서 이미 성립하므로 클립 뒷부분은 다음 공격 크로스페이드에 끊겨도 된다.
창 안에 들어가야 하는 것은 **자리를 옮기는 시간**(`rollMoveDuration` 0.25초)뿐이다 — 그건 다음 스윙의 기하가 얼기 전에 끝나야 한다.

⚠ **예산과 재생을 나눈다.** 예산은 0.25초만 요구하되 **실제 이동은 창이 허락하는 만큼 늘려 쓴다**
(`min(클립 길이, 다음 클립 시작 − margin)`, 하한 0.25초). Apply Root Motion이 꺼져 있어 이동은 코드가 하므로,
이동이 클립보다 짧으면 **남은 절반을 제자리에서 구른다** — 늘리면 둘이 맞고, 짧은 창에서는 0.25초로 줄 뿐 회피는 그대로 성립한다.
반대로 클립 전체를 예산에 넣으면 발동 구간이 **25% → 4%**로 떨어진다.

**D. 필요시간 계산 버그** — `ringExposure`가 빠져 있었다. 배속을 올려 와인드업(0.20초)이 링(0.4초)보다 짧아지면
**링이 적 모션보다 먼저 뜬다** = 텔레그래프/타이밍 단서의 분리(정정 2 A)가 뒤집힌다.
→ `max(와인드업, ringExposure)`.

```
필요시간 = max(와인드업, ringExposure) + dodgeWindow + rollMoveDuration + margin
         = max(0.20, 0.4) + 0.15 + 0.25 + 0.2 = 1.00초
```
`minIdleWindow` 1.2 → **0.8**(실측 45% 지점).

## 설계 요약

```
자리   : 플레이어 애니메이션 공백 (§11-4 견제가 채운 '적 쪽 공백'의 반대편, 같은 창)
사전   : OnDuelScheduled에서 DuelPlan.PlayerPosition(= 플레이어가 '갈 자리')으로 기습 후보를 미리 붙인다
         마감은 PlayerArriveTime — 플레이어와 같은 시각에 도착한다(이동을 창 밖으로 뺀다)
조건   : ① 공백 길이 >= 필요시간   ② 사전 접근한 후보가 아직 Idle이고 화면 안이다
         ③ Attacker.Player 패턴일 것 (적 칼이 이미 오는 구간에는 안 띄운다 — Research §5)
발동   : 적 클립 먼저(텔레그래프) → 링은 impact − ringExposure에 늦게 뜬다
판정   : |입력 − impact| <= dodgeWindow → 성공 / 없으면 impact + window에 실패
성공   : 구르기 클립(Dodge_Left/Right) + 중심(현재 적 ?? 기습한 적)을 축으로 한 원호 이동
실패   : 기존 피격 클립 + OnPlayerHit 재발행 (카메라 큐·체력이 공짜로 따라온다)
```

**새로 만드는 것은 둘** — `DodgeDirector`(유일 관리 지점) · `DodgePromptView`(원형 UI).
나머지는 전부 이미 있는 값을 밖에서 읽게 여는 수준이다. **새 애니메이터 스테이트·레이어 없음**(§11-4와 같은 규율).

---

## 단계

### - [~] Step 0 — 사전 실측 (2·3 완료 / 1은 플레이 필요)

**결과**
- ✅ **2. Apply Root Motion = 꺼짐**(`Char_School_Katana_FullBody-Magica cloth2.prefab: m_ApplyRootMotion: 0`) — 구르기가 이중으로 안 간다.
- ✅ **3. Attack Layer 아바타 마스크 없음**(`PlayerAnimator.controller`의 Attack Layer `m_Mask: {fileID: 0}`) — 전신이라 구르기가 다리까지 걸린다.
- ⏳ **1. 공백 길이 분포** — 런타임 값이라 정적 계산 불가. `DodgeDirector.logWindows`(기본 켜짐)가
  공백마다 `[DodgeDirector] 공백 1.34s → 발동/무시(사유)`를 한 줄씩 찍는다. **한 곡 돌려 그 로그로 `minIdleWindow`를 확정할 것.**
  현재 기본값 **1.2초**는 예산(와인드업 0.5 + 판정 0.15 + 구르기 0.5 + margin 0.2 ≈ 1.35초)에서 역산한 잠정값이다.

**원래 항목**

1. **공백 길이의 분포** — 채보(`Dreamer_Lv10`) 전 엔트리에 대해
   `pendingScheduleStart − max(recoveryEndTime, convergeUntil)`. **완료** — 정정 3의 표가 그 결과다.
   가장 싼 방법: `OnIdleWindow`를 Step 1에서 먼저 넣고 **한 곡 플레이하며 로그로 히스토그램**을 뽑는다
   (정적 계산은 `convergeUntil`·클립 배속이 런타임 값이라 재현이 안 된다).
   → `minIdleWindow` 기본값의 유일한 근거. 1.85초 이상이 0개면 **예산을 깎는다**(구르기 배속 압축 → `exposureDuration` 축소 순).
2. **플레이어 `Animator`의 Apply Root Motion** — 켜져 있으면 구르기가 코드 이동과 이중으로 간다.
3. **`Attack Layer`의 아바타 마스크가 전신인가** — 상체면 구르기가 다리에 안 걸린다.

### - [x] Step 1 — `CharacterActionPlayer`: 공백을 알리고, 일회성 리액션을 연다

이 기능의 트리거 전부가 여기 있다.

- **`public event Action<float, float> OnIdleWindow`** — `HandleJudgeTargetBegan` **끝**에서 발행.
  - `start = max(recoveryEndTime, convergeUntil, Time.time)`, `end = pendingScheduleStart`
    ⚠ **첫 노드로 자르지 않는다**(정정 3 A) — 그 캡이 창을 0.15초 상수로 만들어 기능을 죽였다.
  - `end - start <= 0`이면 발행하지 않는다.
  - ⚠ **`OnDuelScheduled`보다 뒤라는 순서에 의존한다**(`convergeUntil`이 그 핸들러에서 갱신되므로).
    이미 보장돼 있다 — `OnPatternComplete`(디렉터) → `RaiseJudgeTargetBegan`. 코드에 주석으로 못박는다.
  - 슬롯이 비어 무연출인 패턴(`hasPending == false`)에서는 `pendingScheduleStart`가 낡은 값이다 →
    그 경우 `end = info.Deadline`(이 패턴이 끝나는 시각)으로 잡는다.
- **`public void PlayHitReaction()`** — `TryStartPendingHit`의 재생 부분을 그대로 추출(중복 없음).
  `OnPlayerHit` 발행이 그 안에 남아 카메라 큐·체력이 따라온다.
- **`public void PlayOneShot(AnimationClip clip, float speed = 1f)`** — `PlaySlot(clip, 0, clip.length, speed, isSwing:false)`.
  구르기가 쓴다. `speed`는 예산이 빠듯할 때 구르기를 압축하는 노브다(Research §4).
- ⚠ 둘 다 **`hasPending`을 안 건드린다.** 다음 공격은 자기 `Deadline`에서 독립 예약이라 제시각에 시작하고,
  겹치면 크로스페이드가 회피 클립을 끊을 뿐 §6 정렬은 안 깨진다.

### - [x] Step 2 — 나머지 접점을 연다 (읽기 전용·소규모)

- `EnemyDefinition` — **`AmbushAttacks`(`ClipAlignment[]`) 배열 추가**(§2-6).
  **적 종류가 기습 모션의 소유자다** — 클립은 리그·무기에 종속이라 `DeathSliceSet`이 여기 사는 것과 같은 논리다.
  패턴에 두면 안 된다(기습은 패턴 밖 사건이라 어느 패턴에도 안 묶인다).
  **배열인 이유**: 같은 적이 곡 내내 똑같이 찌르면 두 번째부터는 사건이 아니라 리듬이 된다.
- `EnemyView` — `public bool IsIdle => CanWander();`
  조건이 정확히 일치한다(`Dying/dissolving 아님 && 예약 없음 && reactionUntil 지남 && moving 아님`). **새 판정을 만들지 않는다.**
- `EnemyDirector` —
  - `public Func<Vector3,bool> BuildVisibilityTest()` 무인자 오버로드(카메라 폴백 포함).
  - `public EnemyView PickIdleAmbusher(Func<Vector3,bool> isVisible)` —
    `activeCluster` 중 `currentOpponent 아님 && IsIdle && 화면 안 && 기습 클립 있음`인 것 중 **랜덤 하나**(사용자 요구: 무리에서 랜덤).
    ⚠ 클립 조건은 **후보 단계에서** 건다 — 무연출 기습은 "회피할 대상이 없는 프롬프트"라 존재할 수 없다
    (다른 무연출 폴백들과 성질이 다르다). 판단은 "`Definition.AmbushAttacks`에 쓸 수 있는 게 하나라도 있는가,
    없으면 `fallbackAmbushClips`가 비지 않았는가".
    ⚠ 후보를 `activeCluster`로 묶는 이유는 §11-6과 같다 — `staged`를 집으면 집결 중인 적이 이탈해 무리가 깨진다.
    ⚠ 이동 시간 조건은 없다 — **사전 접근(Step 5 흐름 0)이 이동을 창 밖으로 뺐기 때문**이다.
  - 사전 접근은 **`view.ScheduleMove(현재, 목표, now, plan.PlayerArriveTime)`** 하나면 된다(이미 public).
    목표는 `plan.PlayerPosition`에서 `stageDistance` 떨어진 지점이고, 그 계산은 `DodgeDirector`가 한다 —
    **`EnemyDirector`에 새 메서드를 만들지 않는다**(무대 반경 클램프가 필요하면 그때 옮긴다).
  - `public void ReleaseAmbusher(EnemyView view, float retreatDistance, float retreatDuration)` —
    `EnemyRing.PickRetreatTarget`으로 무대 안 자리를 잡아 `view.Resolve(false, Attacker.Enemy, …)`.
    무대 중심·반경을 아는 곳이 여기뿐이라 여기 둔다.
- `PatternHandler` — 기존 `#if UNITY_EDITOR` 블록 **안에** 읽기 프로퍼티 하나:
  `public bool DebugAutoPerfect => debugInputEnabled && debugAutoPerfect;`
  **새 토글을 만들지 않는다**(§3-1) — 회피가 자기 토글을 들면 "패턴만 오토 / 회피만 오토"라는 아무도 안 원하는 조합이 생긴다.
  빌드에는 안 들어간다(§8의 기존 규율 그대로).
- `ChartPlayer` — **수정 없음**(초안에서 폐기).

### - [x] Step 2-b — `CameraDirector`: 기습적을 담을 3번째 고정 칸

요구 "공격하는 적이 있으면 카메라 타깃에 바로 추가"의 구현. **⚠ `AddMember`로 넣고 빼면 안 된다** —
§7-2가 멤버를 **고정 2칸**으로 못박은 이유가 *"넣었다 뺐다 하면 바운드가 계단식으로 튄다"*이기 때문이다.

- `SetupFraming` — `AddMember(null, 0f, 1f)` **한 줄 추가**(2번 칸). 평소엔 대상 null·가중치 0이라 구도에 영향이 없다.
- `UpdateFraming` — 상대 가중치 계산을 **공통 함수로 뽑아**(`ResolveWeight(Transform)`: `fullFrameDistance`/`dropoffDistance` 식)
  두 칸이 같은 식·같은 `weightDamping`을 쓰게 한다. **계산이 하나라 두 칸이 어긋날 수가 없다.**
- `ApplyFraming` — 가드를 `Count < 2` → `< 3`으로 올리고 2번 칸도 같이 갱신.
- `public void SetAmbusher(EnemySpace.EnemyView view)` — `SetOpponent`과 **같은 규율**:
  대상이 바뀌면 **가중치를 0으로 리셋**한다(§7-2 — 대상 위치는 순간이동하므로 이월하면 카메라가 밖으로 튄다).
- `[SerializeField] float ambusherMaxWeight = 1f` — 기습자는 `stageDistance`(2.5m)라 가중치가 거의 1이라
  화면이 크게 물러난다. 너무 넓으면 `fullFrameDistance`가 아니라 **이 상한**으로 조인다(다른 구도에 영향 없음).
- `framingEnabled == false`(배선 없음)면 이 경로도 통째로 조용히 죽는다 — 기존 규율 그대로.

### - [x] Step 3 — `PlayerCombatMover`: 원호 이동

- `public void RollArc(Vector3 center, float signedDegrees, float duration)`
  - 시작 반경·각도를 잡아 두고 **각도를 보간**한다. 직선 Lerp면 반경 1.5m·60°에서 0.2m 파고든다(Research §2-4).
  - 높이 유지, 회전은 매 프레임 중심을 바라보게 직접 갱신(`ScheduleTurn` 0.15초가 0.8초 구르기와 싸운다).
  - `rolling` 동안 `moving` 보간은 건너뛴다. **`HandleDuelScheduled`가 오면 구르기를 즉시 중단하고 이동에 양보한다** —
    결투 도착 시각은 칼이 맞는 시각이라 놓칠 수 없다.

### - [x] Step 4 — `DodgePromptView` (원형 UI + 포커스 링 + 입력)

씬의 `Canvas` 아래(형제: `FocusRingParent` 옆)에 **하나만** 둔다. 동시에 둘이 뜰 일이 없어 풀이 필요 없다.

- 구성: `RectTransform` + 원형 `Image`(Point 노브와 **같은 스프라이트·같은 지름 90px** — 링 `endSize`와 어긋나면
  "딱 맞았다"가 거짓이 된다) + `IPointerDownHandler`.
- `Show(Transform follow, Vector3 worldOffset, float duration)` — 활성화 + `Pool.Instance.Get<FocusRingView>(PoolKey.FocusRing, …)`을
  **자기 자식**으로 붙인다(`targetLocalPos = (0,0)`) → 프롬프트만 움직이면 링이 따라온다.
  링의 `OnArrived`는 구독하지 않는다 — 판정 시각의 소유자는 `DodgeDirector`다(시계를 둘로 만들지 않는다).
- `LateUpdate`에서 `follow.position + worldOffset` → 화면 → 캔버스 로컬.
  **`LateUpdate`인 이유**: 카메라 확정 뒤여야 한 프레임 안 밀린다(앵글 교체 중에는 매 프레임 카메라가 움직인다).
- `Hide()` — 링 반납 + 비활성. `public event Action OnPressed`.

### - [x] Step 5 — `DodgeDirector` (유일 관리 지점)

`Assets/02. Scripts/Enemy/DodgeDirector.cs` (`EnemySpace`). `EnemyDirector`와 같은 오브젝트.

| 필드 | 기본 | 뜻 |
|---|---|---|
| `dodgeEnabled` | on | 끄면 이 층만 죽는다(기존 연출 토글 규율) |
| `fallbackAmbushClips` (`ClipAlignment[]`) | — | **`EnemyDefinition.AmbushAttacks`가 진짜 출처**(§2-6). 이건 미배선 종류용 폴백이며, 둘 다 비면 그 적은 후보에서 빠진다 |
| `dodgeClipLeft/Right` | Dodge_Left/Right | 구르기(0.8초) |
| `rollSpeed` | 1.6 | 구르기 <b>클립</b> 배속. 클립은 예산에 안 들어간다(정정 3 B) |
| `rollMoveDuration` | 0.25 | 원호로 <b>자리를 옮기는</b> 시간. 예산에 들어가는 건 이것뿐 |
| `ringExposure` | 0.4 | **링 수축 시간만**. 적 클립 길이와 무관하다(§4-2 A) — 패턴 링과 같은 감각을 유지한다 |
| `dodgeWindow` | 0.15 | 판정 창(±초). 패턴 `goodWindow`(0.10)보다 관대 — 노드는 손가락, 회피는 온몸 |
| `minIdleWindow` | 0.8 | 이보다 짧은 공백에서는 발동하지 않는다(실측 45% 지점) |
| `margin` | 0.2 | 공백 끝과의 여유 |
| `cooldown` | 6 | 연속 발동 방지 |
| `stageDistance` | 2.5 | **사전 접근** 대기 거리. 결투 거리(≈1m)보다 확실히 커야 현재 상대로 안 보인다 |
| `lungeDistance` | 1.5 | 기습 순간 마저 좁혀 멈춰 서는 거리 |
| `rollDegrees` | 60 | 원호 각 |
| `dodgeKey` | Space | 클릭 대신 키로도 |

흐름:

0. **사전 접근** — `EnemyDirector.OnDuelScheduled(DuelPlan)` 구독.
   - 쿨다운이 지났고 대기 후보가 없으면 `PickIdleAmbusher(BuildVisibilityTest())`로 하나 고른다.
   - 자리는 **`plan.PlayerPosition`(플레이어가 갈 자리)에서 `stageDistance` 떨어진 지점**,
     마감은 **`plan.PlayerArriveTime`**(플레이어와 같은 도착 시각). **이동만 건다 — 공격 예약은 아직 안 건다.**
     ⚠ `plan.PlayerPosition`이지 플레이어의 현재 위치가 아니다. 현재 위치로 붙이면 적만 뒤에 남는다(정정 2 B).
   - 이 적을 `stagedAmbusher`로 들고 있는다. **발동 여부는 여기서 안 정한다** — 창 길이를 아직 모른다.
   - ⚠ 붙여 놓고 발동을 안 해도 손해가 없다. 그냥 배회로 돌아간다(§11-6 "보이는 이동은 아무 일도 아니다").
   - 못 따라온 경우는 1단계의 `IsIdle` 조건이 알아서 거른다 — **새 예외 처리 없음**.
     (그리고 그런 구간은 애초에 플레이어가 대시 중이라 공백이 없다.)
1. **트리거** — `CharacterActionPlayer.OnIdleWindow(start, end)` 구독. **폴링 없음.**
   - **기습 클립 목록** = `stagedAmbusher.Definition.AmbushAttacks`(비면 `fallbackAmbushClips`) — §2-6, 적 종류가 소유한다.
   - `클립별 필요시간 = 와인드업(ResolvedImpactSpan / Speed) + dodgeWindow + rollDuration/rollSpeed + margin`
     ⚠ **클립마다 와인드업이 달라 필요시간도 다르다** — 상수로 굳히면 안 된다.
   - ⚠ **랜덤은 예산 뒤가 아니라 예산 안에서 뽑는다**(§2-6): 남은 창에 **들어가는 클립만 추린 뒤 그중 랜덤**.
     먼저 뽑고 안 맞으면 포기하는 방식은 긴 클립을 뽑을 때마다 이벤트가 통째로 사라진다.
     추린 목록이 비면 이번 공백은 발동하지 않는다. 후보가 둘 이상이면 **직전에 쓴 클립은 제외**한다
     (`NextHitClip`이 번갈아 돌리는 것과 같은 목적 — 같은 모션이 연달아 나오면 랜덤으로 안 읽힌다).
   - `end − max(start, Time.time) < 필요시간` → 무시(대기 후보는 그대로 두거나 놓아준다).
   - `enemyDirector.CurrentAttacker == Attacker.Enemy` → **무시**(Research §5 — 적 칼이 이미 오는 구간).
   - `stagedAmbusher`가 null이거나 `!IsIdle`이거나 화면 밖이면 무시.
   - 발동 시각 = `max(start, Time.time)`. `start`가 미래면 그때까지 대기한다(도착 전에 띄우면 달리면서 회피하게 된다).
2. **발동** — `impact = end − (rollDuration/rollSpeed + margin)`, 즉 **구르기가 창 안에 끝나도록 임팩트를 역산한다.**
   - 적: `AssignAttack(기습 클립, impact, 플레이어에서 lungeDistance 떨어진 자리, 플레이어, maxAttackSpeed)`.
     `ClipAlignment`가 시작 시각·배속을 스스로 역산하므로 **와인드업이 먼저 보이는 것은 공짜다**(§4-2 A).
     자리는 **적 → 플레이어 방향** 위에서 잡는다(플레이어 쪽으로만 오므로 무대를 안 넘는다).
   - **카메라: `cameraDirector.SetAmbusher(view)`를 여기서 부른다**(Step 2-b).
     ⚠ 흐름 0(사전 접근)에서 부르면 안 된다 — 발동 없이 끝날 수도 있는 후보를 담으면
     **매 패턴 화면이 넓어졌다 좁아졌다** 한다. "공격하는 적"이 확정된 순간이 여기다.
   - UI: `impact − ringExposure`에 `prompt.Show(view.transform, Vector3.up * 1.7f, ringExposure)`.
     **링은 여기서 처음 뜬다** — 그 전 구간은 적 모션만 보인다.
3. **입력** — `prompt.OnPressed` 또는 `dodgeKey`. `|Time.time − impact| <= dodgeWindow`면 성공.
   **이른 입력은 무시한다**(실패로 치면 연타로 자멸한다). 늦은 것만 실패다.
   ⚠ 링이 뜨기 전(텔레그래프 구간)의 입력도 무시다 — 그게 곧 "링이 타이밍의 유일한 단서"라는 계약이다.
   - **오토퍼펙트**(§3-1): `#if UNITY_EDITOR` 안에서 `handler.DebugAutoPerfect`가 켜져 있으면
     `Time.time >= impact`가 되는 **첫 프레임에 성공 처리**한다(= delta ≈ 0, 곧 Perfect).
     기존 오토플레이가 `ExpectedTime`에 `DebugForceInput`을 거는 것과 **같은 규율**이다.
     프롬프트·링은 그대로 띄운다 — 오토플레이는 연출을 보려고 켜는 것이다.
4. **성공** — `prompt.Hide()` + 구르기
   - 중심 = `enemyDirector.CurrentOpponent ?? 기습한 적`. **반경이 보존돼 `duelAnchor`(ImpactAnchor)가 안 벗어난다**(Research §5).
   - 방향: `±rollDegrees` 두 후보 중 **구른 뒤 위치에서 가장 가까운 적까지의 거리**가 큰 쪽. 무대 밖이면 감점.
   - `mover.RollArc(center, 부호각, rollDuration/rollSpeed)` + `action.PlayOneShot(부호 > 0 ? Right : Left, rollSpeed)`
5. **실패** — `dodgeTime + dodgeWindow`에 `prompt.Hide()` + `action.PlayHitReaction()`.
   카메라 피격 큐·체력은 `OnPlayerHit` 구독자가 이미 처리한다(새 배선 없음).
6. **뒷정리** — 성패와 무관하게 `enemyDirector.ReleaseAmbusher(view, failRetreatDistance, retreatDuration)`.
   어느 쪽이든 적은 찌르고 물러난다 — 가를 이유가 없다.
   **`cameraDirector.SetAmbusher(null)`도 여기서**(구르기·피격이 끝나는 시점). 대상만 비우면
   가중치가 감쇠로 빠져 **컷이 안 생긴다** — 칸 자체는 계속 남아 있다(Step 2-b).
7. **곡 정리** — `PatternHandler.OnAllPatternsCleared` 구독 → 진행 중이면 프롬프트·예약 회수(잔존물 규율).

### - [x] Step 6 — 씬 배선  *(⚠ 기습 클립 저작 1건만 남음)*

**완료된 배선**(BattleScene 저장됨)
- `UI/Canvas/PatternHandler/DodgePrompt` 생성 — `Image`(Knob 스프라이트, 90x90, raycastTarget) + `DodgePromptView`,
  `FocusRingParent`와 같은 층. `ringStartScale`은 씬의 `focusRingStartScale`과 같은 **5**로 맞춤.
- `EnemyDirector` 오브젝트에 `DodgeDirector` 추가 — actionPlayer·mover(플레이어) / enemyDirector / prompt / handler / cameraDirector 전부 배선.
- 구르기 클립 `Dodge_Left.anim`·`Dodge_Right.anim` 배선.

**남은 것: `AmbushAttacks` 저작** — `Assets/04. Datas/EnemyDefinitions/Samurai_Male_Diagonal.asset`의 배열이 비어 있어
**지금은 후보 자격 미달로 한 번도 발동하지 않는다**(무연출 기습을 만들지 않기 위한 의도된 동작).
클립 선택은 저작 판단이라 비워 뒀다 — `Stab_5` / `LightAttk1~4` / `SideKick` 같은 짧은 것을 2~3개, **길이를 섞어** 넣을 것.

- `Canvas` 아래 `DodgePrompt`(Image + `DodgePromptView`), `FocusRingParent`와 같은 층.
- `EnemyDirector` 오브젝트에 `DodgeDirector` 추가 → action / enemyDirector / mover / prompt / handler 배선.
- **기습 클립은 `EnemyDefinition` 에셋마다 배선한다**(`AmbushAttacks` 배열, 종류당 2~3개 권장).
  기존 적 공격 클립을 `Tools/Animation Clip Trimmer`로 잘라 쓰면 새 클립 저작이 없다.
  `ImpactTime`을 안 찍으면 **트림 끝이 임팩트**로 폴백된다 — 찌르는 동작을 통째로 보여 주고 끝나는 순간이 닿는 순간이라
  기본값으로 자연스럽다(견제와 같은 성질).
  ⚠ **길이를 섞어 넣는 게 유리하다** — 창이 짧을 때는 짧은 클립만 후보에 남으므로,
  긴 것만 넣으면 좁은 공백에서 아예 안 뜬다.
  종류별로 비면 `DodgeDirector.fallbackAmbushClips`가 대신하고, 둘 다 비면 그 종류는 기습을 안 한다.

### - [ ] Step 7 — 검증

- Step 0의 로그로 **실제 발동 빈도**를 확인한다. 한 곡에 0번이면 예산을 깎는다(구르기 배속 → `ringExposure` 순).
- **적 모션이 링보다 먼저 보이는가**(§4-2 A의 목적 — 텔레그래프와 타이밍 단서의 분리).
- **사전 접근한 적이 현재 상대로 안 보이는가**(`stageDistance` 검증). 발동을 안 했을 때 조용히 배회로 돌아가는가.
- **카메라**: 발동 순간 기습자가 구도에 들어오고 끝나면 감쇠로 빠지는가. **컷·튐이 없는가**(가중치 0 리셋 검증).
  발동 안 한 패턴에서 화면이 안 흔들리는가(사전 접근 구간을 안 담은 게 실제로 지켜지는가).
- 링 수축이 끝나는 순간과 적 칼이 닿는 순간이 같은가.
- 아이콘이 적을 정확히 따라가는가 — **앵글 교체 중에도**(`LateUpdate` 검증).
- 구른 뒤 플레이어–현재 적 거리가 유지되는가(원호가 실제로 원호인가), 적이 다시 플레이어를 보는가(`TickGaze`).
- 구르기가 다음 공격 클립 시작 전에 끝나는가. 실패 경로에서 카메라 피격 큐가 나오는가.
- **오토퍼펙트를 켜고 한 곡 돌렸을 때 한 번도 안 맞는가**(연출 확인용 오토플레이가 실제로 쓸 수 있는가).
  오토퍼펙트를 끄면 자동 회피도 같이 꺼지는가(토글이 하나라는 게 지켜지는가).

---

## 안 하는 것 (지금은)

- **`Attacker.Enemy` 패턴에서의 발동** — 적 칼이 이미 오는 구간이라 기하가 얼어 있어야 하고, 둘을 동시에 피하게 된다.
- **회피 전용 애니메이터 스테이트·레이어** — 기존 듀얼 슬롯으로 충분하다(§11-4와 같은 판단).
- **히트스톱·전용 카메라 큐** — 회피 성공은 "안 맞았다"라 멈출 임팩트가 없다. 실패는 기존 피격 큐를 그대로 탄다.
- **동시 다중 기습** — 프롬프트가 둘이면 어느 링이 어느 적 것인지 읽을 수 없다.
- **회피 성공 보상(점수·게이지)** — 판정 시스템에 손대는 순간이라 별도 논의.
</content>
