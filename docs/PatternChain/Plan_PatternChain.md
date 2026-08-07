# Plan_PatternChain.md — 연계 공격 · Pattern 에셋 정리

> 기반 문서: `Research_PatternChain.md`

## 설계 요약

**사슬 = 한 적에게 짧은 패턴 여러 개.** 긴 패턴 하나를 쪼개는 것이 아니라 1~2노드짜리 짧은 패턴이 연달아 온다 — 그래서 사슬 전체가 1~2초에 끝나고, 그 구간은 무대를 가로지르는 대시가 아니라 **근거리 난타**로 읽힌다(감사 C1의 근거).

**새 데이터 모델을 만들지 않는다.** 연계는 이미 `EnemyCue.killOnSuccess = false`의 연속으로 성립하고(Research 참조), 사슬 도중 실패해도 상대가 유지되는 것도 현재 동작이다. 이 플랜이 하는 일은 **그 상태를 화면과 저작 툴에 드러내는 것**뿐이다.

| 무엇 | 어디 | 성질 |
|------|------|------|
| 사슬 = `killOnSuccess=false`의 연속 | `SongChartEntry.enemyCue` | **기존** — 코드 변경 없음 |
| 사슬 처치 판정(일정 타수 이상) | `EnemyDirector` 카운터 + 비율 노브 하나 | 신규 |
| 중간 타격도 히트스톱 | `EnemyView.ApplyHitStop` 살아 있는 경로 | 신규 |
| 리액션을 패턴이 소유(성공=맞는 모션 / 실패=막는 모션) | `Pattern`에 `ClipAlignment` 슬롯 둘 | 신규 |
| 중간 타격 후퇴 = 0 | `EnemyDirector.ResolveRetreatDistance` | 조건 한 줄 |
| 사슬 저작·시각화 | `PatternChartWindow` | 툴만 |
| 레거시 5필드 제거 | `Pattern` · `CharacterActionPlayer` | 삭제 |

**이번 플랜에서 안 하는 것** (의도적):

- **1대 다수(광역 일격)** — 별도 주제. 결투 계획 N인 확장·프레이밍 N인·동시 사망이 연계와 공유하는 부분이 없다.
- **사슬 단위 연출 강조**(마무리 일격만 카메라/히트스톱 세게) — 최소 변경 결정. 필요해지면 `CameraCueCatalog`에 트리거 한 줄이 붙는 자리다.
- **사슬 카운터 UI** — 판정·연출 어디에도 사슬 번호가 필요하지 않다. 필요해지면 그때 `EnemyDirector`가 연속 비처치 수를 세면 된다.

---

## 단계별 구현 계획

### Step 1 — `Pattern` 레거시 애니메이션 5필드 제거

- [x] `Assets/02. Scripts/Pattern/Pattern.cs`
  - `successAnimationClip` · `animationStartOffset` · `animationDuration` · `animationImpactTime` · `animationSpeed` 필드 제거
  - 대응 프로퍼티(`SuccessAnimationClip` / `AnimationStartOffset` / `AnimationDuration` / `AnimationImpactTime` / `AnimationSpeed` / `ResolvedAnimationDuration`) 제거
  - `OnValidate`에서 `ValidateImpactTime()` 호출 제거, 메서드 본문 제거 (`playerAttack.ValidateImpactTime`이 같은 일을 이미 한다)
- [x] `Assets/02. Scripts/Character/CharacterActionPlayer.cs`
  - `ResolveAttack`(또는 해당 메서드)의 **레거시 폴백 분기 제거**(`:620~655` 근처). `playerAttack`/`playerParry`가 비어 있으면 무연출로 끝난다
  - 클래스 상단 요약 주석에서 `AnimationStartOffset/Duration/ImpactTime`, `AnimationSpeed` 언급을 `ClipAlignment` 기준으로 갱신
- [x] `Assets/02. Scripts/ChartGen/Editor/PatternChartWindow.cs:397` 주석의 `Pattern.SuccessAnimationClip` → `Pattern.PlayerAttack`
- [x] 에셋 확인: 템플릿 10개 모두 `playerAttack`이 채워져 있음을 확인 후 진행(Research에서 확인 완료). 필드 제거 후 Unity가 에셋에서 낡은 키를 흘려보낸다 — 에셋 재저장은 하지 않아도 되지만, 한 번 열어 경고가 없는지 본다

> 근거: 코드 참조는 폴백 한 곳뿐이고 에디터 참조는 0곳, 에셋 10개 전부 이관 완료. 남겨 두면 같은 클립이 두 군데 적혀 있어 "뭐가 진짜냐"를 매번 확인해야 한다.

### Step 2 — 리액션을 패턴이 소유한다 (구멍 A)

**패턴마다 다른 리액션을 낸다** — 성공하면 맞는 모션, 실패하면 막는 모션. 지금은 `EnemyView`의 고정 스테이트 이름(`knockBackStateName` / `parryStateName`)이라 획 모양과 무관하게 늘 같은 클립이 나온다. 공격(`EnemyAttack`)·견제(`EnemyFeint`)·사망(`EnemyDeath`)은 이미 `Pattern`이 `ClipAlignment`로 드는데 **리액션만 빠져 있다.**

**덤으로 정렬이 잡힌다.** `Resolve`는 확정(마지막 노드 입력) 즉시 CrossFade하지만 칼은 `Deadline + ImpactOffset`에 닿는다 — 지금 리액션은 **칼보다 `goodWindow`(0.1초)만큼 먼저 나온다**(막는 모션에서 특히 티가 난다). `ClipAlignment`로 올리면 §6의 임팩트 정렬을 그대로 쓴다.

- [x] `Assets/02. Scripts/Pattern/Pattern.cs` — 슬롯 둘 추가
  ```csharp
  [Tooltip("이 패턴에 맞은 적의 리액션. 성공했으나 처치되지 않을 때 재생된다(사슬 중간 타격). 비우면 knockBackStateName 폴백.")]
  [SerializeField] private ClipAlignment enemyHit = new ClipAlignment();

  [Tooltip("이 패턴을 막아낸 적의 리액션. 실패 + 제자리(패링)일 때 재생된다. 비우면 parryStateName 폴백.")]
  [SerializeField] private ClipAlignment enemyParry = new ClipAlignment();
  ```
  - `OnValidate`의 `ValidateImpactTime` 목록에 둘 추가
  - `WarnUnusedSlots`: `enemyHit`은 `Attacker.Player` 전용(적이 공격자면 그 실패는 §11-2대로 언제나 물러난다)
- [x] `Assets/02. Scripts/Enemy/EnemyView.cs` — `Resolve`에 리액션 클립 인수 추가
  - 시그니처에 `ClipAlignment reaction, float impactTime` 추가. **클립이 있으면 `ClipAlignment` 경로**(`ResolveScheduleStart`로 예약 → 임팩트에 정렬), 없으면 **기존 스테이트 이름 CrossFade**(회귀 없음)
  - `Attacker.Player` 성공(= 사슬 중간 타격) 분기 신설: 지금의 `else CrossFadeReaction(idleStateName)` 자리를 `enemyHit`(없으면 `knockBackStateName`)로 바꾼다. Idle이면 칼이 지나갔는데 아무 일도 안 일어난 그림이 된다
  - **재생은 `Attack` 슬롯 + `AttackSpeed`로 한다**(견제와 같은 규율 — 새 애니메이터 스테이트를 만들지 않는다). 리액션 시점에 적의 공격은 이미 끝났고 `Attacker.Player`면 애초에 없다. **이래야 히트스톱이 리액션도 얼린다**(Step 3.6 — `DeathSpeed`는 죽는 적만 탄다)
  - 예약 필드는 **공격과 못 나눈다** — 리액션은 공격이 끝난 뒤에 오지만 사슬에서는 다음 패턴의 견제 예약과 겹친다. 리액션 전용 예약 필드 한 벌(`pendingReaction` / `pendingReactionStart`)을 둔다
  - `PlayAttack`이 건 배속을 필드에 남긴다(`lastAttackSpeed`) — 히트스톱 해제가 되돌릴 값이다
  - `reactionUntil`(로코모션 잠금)은 **예약 시각이 아니라 재생 시작 시각** 기준으로 건다
- [x] `Assets/02. Scripts/Enemy/EnemyDirector.cs` — `ResolveReservation`
  - `opponent.Resolve(...)` 호출에 리액션 클립을 넘긴다: 성공-미처치면 `r.template?.EnemyHit`, 실패-패링(`retreat <= 0`)이면 `r.template?.EnemyParry`, 실패-회피면 `null`(기존 `evadeStateName` 유지 — 이동이 실려 있어 클립만으로 안 끝난다)
  - `r.impactTime`을 같이 넘긴다(정렬 기준)
- [x] `Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs` — 적 슬롯 선택기에 `enemyHit` · `enemyParry` 추가

> **회피는 승격하지 않는다.** 회피는 클립 + 후퇴 이동 + 무대 경계 클램프가 한 덩어리라 `ClipAlignment` 하나로 안 끝난다. 지금 구조를 유지한다.
>
> **폴백을 남기는 이유**: 템플릿 10개에 아직 리액션 클립이 없다. 슬롯이 비면 오늘과 똑같이 동작한다.
>
> 넉백 거리는 이 Step과 무관하다 — 루트모션이 없어(이동은 전부 `ScheduleMove`) 클립은 제자리에서 재생되고, 밀려나는 거리는 `retreatDistance`가 따로 정한다(Step 3).

### Step 3 — 사슬 중간 타격은 넉백 없음 (구멍 B)

- [x] `Assets/02. Scripts/Enemy/EnemyDirector.cs` — `ResolveRetreatDistance`
  - 첫 줄을 `if (AttackerOf(r.template) == Attacker.Enemy) return failRetreatDistance;`로 좁힌다 (패링 성공으로 적이 밀려나는 것은 그 연출의 일부다 — 유지)
  - `playerSucceeded && Attacker.Player`(= 사슬 중간 타격)는 **`0f` 반환**. 창 검사도 거치지 않는다 — 물러날 일이 아예 없다
  - 실패 경로(`!playerSucceeded`)의 창 조건은 **그대로 둔다**
  - 주석 근거: "**사슬 중간 타격에는 넉백이 없다.** 물러나면 플레이어가 매 타격마다 다시 붙어야 하는데, 그 재접근은 `TakeTargetForWindow`를 안 거쳐 거리가 창에 안 맞춰진다(`docs/FailConverge/`) — 사슬은 그 구간을 매 타격 반복하게 된다. 제자리에 세우면 그 문제 자체가 없다."

> `retreatDistance = 0`은 이미 지원되는 값이다(제자리 패링이 쓴다). `Resolve`의 `ScheduleMove`는 그대로 불리고 `minMoveDistance` 아래라 제자리 Run도 안 난다(`EnemyView.cs:460~462`).
>
> `EnemyRing.CanRetreat` 순수 함수 추출은 **하지 않는다** — 조건을 공유할 필요가 없어졌으므로 새 함수도 새 테스트도 없다.

### Step 3.5 — 사슬 처치 판정: 일정 타수 이상

**규칙**: `처치 = 마지막 타 성공 && 성공 수 >= ceil(사슬 길이 × chainKillRatio)`

**왜 마지막 타가 필수인가**: 앞선 타만으로 처치가 확정되면 마지막 칼이 빗나간(적이 패링·회피 모션을 재생 중인) 임팩트 프레임에 몸이 갈라진다. 처치 연출이 걸릴 자리가 없다. 마지막 타를 필수로 두면 **성공 연출이 있는 임팩트에만 절단이 붙는다**.

**왜 비율인가**: 임계를 1로 두면 "마지막 타만 본다", 사슬 길이로 두면 "전부 성공해야 처치"다. 노브 하나가 그 사이를 잇는다. **비사슬 엔트리(길이 1)는 `ceil(1 × 0.6) = 1`이라 기존 채보가 그대로 재생된다** — 회귀가 구조적으로 없다.

- [x] `Assets/02. Scripts/Enemy/EnemyDirector.cs`
  - 인스펙터 노브 추가:
    ```csharp
    [Range(0f, 1f)]
    [Tooltip("사슬을 처치로 인정할 성공 타수의 비율. 1이면 전부 성공해야 하고, 0에 가까우면 마지막 타만 보면 된다.\n" +
             "비사슬 엔트리(길이 1)는 어느 값이든 1타 필요 — 기존 채보 동작과 같다.")]
    [SerializeField] private float chainKillRatio = 0.6f;
    ```
  - 사슬 카운터 필드 둘: `chainHits`(이 상대와 시작한 뒤 총 타수), `chainSuccesses`(그중 성공)
  - **카운터 수명은 `currentOpponent`와 같다** — 상대가 바뀌는 곳이 리셋 지점이다. `BindReservation`에서 새 상대를 뽑았을 때(`currentOpponent == null`이던 분기)와 `KillOpponent`에서 0으로
  - `ResolveReservation`에서 `chainHits++`, 성공이면 `chainSuccesses++` (처치 분기보다 **먼저** — 마지막 타가 카운트에 들어가야 한다)
  - 처치 조건 교체:
    ```csharp
    if (r.cue.killOnSuccess && playerSucceeded && chainSuccesses >= RequiredHits(chainHits))
    ```
    ```csharp
    /// <summary>이 길이의 사슬을 처치로 인정할 최소 성공 타수. 최소 1 — 마지막 타는 언제나 필요하다.</summary>
    private int RequiredHits(int chainLength) =>
        Mathf.Max(1, Mathf.CeilToInt(chainLength * chainKillRatio));
    ```
  - **임계 미달로 안 죽으면** 기존 `else` 분기를 그대로 탄다(리액션 + 넉백 0). 상대가 유지되므로 다음 사슬이 같은 적에게 이어지고, 카운터는 **리셋하지 않는다** — 그 적은 계속 맞아 온 적이고 다음 사슬의 성공이 누적되어 결국 죽는다(링 고갈 방지)

> `ponytail:` 계산은 한 줄이라 별도 순수 클래스·유닛테스트를 만들지 않는다. 검증은 아래 플레이 확인이 한다. 사슬 규칙이 더 복잡해지면(가중치·연속 보너스) 그때 `Enemy/Core`로 뽑는다.

### Step 3.6 — 사슬 중간 타격에도 히트스톱 (감사 C3)

지금은 `EnemyDirector.ApplyHitStop`이 `pendingKills`(죽는 적)만 순회하고, `EnemyView.ApplyHitStop`은 `Phase.Dying`에서만 돌며 `DeathSpeed`만 얼린다(`EnemyView.cs:711`). **사슬 중간 타격은 플레이어만 멈추고 적 리액션은 계속 흐른다** — §7-3이 예외로 두던 "한쪽만 멈춤"이 사슬에서는 매 타격이 된다.

- [x] `Assets/02. Scripts/Enemy/EnemyView.cs` — `ApplyHitStop`에 **살아 있는 경로** 추가
  - `Phase.Dying`이면 지금 그대로(`DeathSpeed = 0`, 절단 시각 밀기)
  - 아니면 `AttackSpeed = 0`으로 얼린다. **절단 시각은 그대로 반환한다** — 죽지 않으므로 밀 것이 없다
  - 해제(`:572`)에서 어느 쪽을 얼렸는지 보고 `deathSpeed` 또는 `lastAttackSpeed`로 되돌린다
  - 가드: 리액션도 공격도 안 도는 상태(순수 Idle)면 얼릴 것이 없으므로 조기 반환
- [x] `Assets/02. Scripts/Enemy/EnemyDirector.cs` — `ApplyHitStop`
  - `pendingKills` 순회 뒤에 `currentOpponent`도 한 번 얼린다(죽는 중이면 이미 `pendingKills`에 있으므로 **중복 호출 방지** — `EnemyView.hitStopped` 가드가 이미 막지만 의도를 주석으로 남긴다)

> `HitStopDirector`는 그대로다 — 성공(`AllCorrect`)마다 창을 열 뿐 누가 어떻게 얼지는 각 층이 정한다. **짧은 패턴이 연달아 오므로 창이 잦아진다**(사슬 3타면 1~2초 안에 3번). `CameraDirector.HoldForHitStop`이 매번 `CinemachineBrain`을 껐다 켜는데, 창이 0.05~0.10초라 누적 문제는 없다.

### Step 4 — 굽기 툴의 사슬 저작 (구멍 C)

- [x] `Assets/02. Scripts/ChartGen/Editor/PatternChartWindow.cs` — `DrawBulkCueTools`
  - 버튼 추가: `사슬 길이 [N]` → `ApplyChainLength(n)`, 즉 `killOnSuccess = (i % n == n - 1)` (**마지막 타가 마무리**)
  - 기존 `매 N번째만 처치`는 그대로 둔다 — 위상이 반대일 뿐 다른 용도다. 툴팁으로 차이를 적는다: "매 N번째 = 첫 타가 마무리 / 사슬 길이 N = 마지막 타가 마무리"
- [x] 엔트리 행(`DrawEntryRow`)에 **사슬 위치 뱃지**
  - 표시: `사슬 2/3 · 2타 이상 필요` 형태. 계산은 순수 — 직전 `killOnSuccess=true` 다음부터 세고, 다음 `true`까지가 그 사슬. 필요 타수는 Step 3.5의 `ceil(길이 × 비율)`과 **같은 식**(툴은 씬 값을 모르므로 창에 비율 필드를 두고 기본값을 코드와 맞춘다)
  - 마무리 엔트리는 기존 `· 성공 시 처치` 요약 유지
- [x] **미마감 사슬 경고**
  - 마지막 엔트리가 `killOnSuccess = false`면 리스트 상단에 `HelpBox`: "마지막 사슬에 마무리가 없습니다 — 곡이 끝나도 그 적이 남습니다"
  - 저장은 막지 않는다(의도한 저작일 수 있다). `spawnTimes` null과 달리 조용히 틀린 채보가 되는 게 아니라 화면에 그대로 보이는 문제다
- [x] **사슬 안에 `attacker` 혼재 경고** (감사 C2)
  - 한 사슬의 엔트리들이 서로 다른 `Pattern.Attacker`를 쓰면 그 사슬 행에 경고 표시
  - 이유: `Attacker.Enemy` 타에서만 넉백 1.5m가 살아나고, 그 재접근이 창에 안 맞춰져 기어간다

### Step 5 — 문서 갱신

- [x] `CLAUDE.md`
  - §11-1 또는 §11-2 뒤에 **§11-5 연계 공격(사슬)** 절 추가:
    - 사슬 = `killOnSuccess=false`의 연속, 새 데이터 모델 없음
    - "실패해도 사슬이 이어진다"는 `BindReservation`의 `currentOpponent == null` 분기 하나가 소유
    - 중간 타격은 `knockBack` 클립을 **제자리에서** 재생하고 **넉백 거리는 0**이다 — 물러나면 재접근이 `TakeTargetForWindow`를 안 거쳐 매 타격 기어가는 구간이 생긴다
    - 처치 = `마지막 타 성공 && 성공 수 >= ceil(사슬 길이 × chainKillRatio)`. **마지막 타 성공은 필수** — 빗나간 임팩트에 절단을 붙이면 처치 연출이 걸릴 자리가 없다. 미달이면 카운터를 유지한 채 다음 사슬로 이어진다(링 고갈 방지)
    - 저작: `사슬 길이 N` 버튼. `매 N번째`와 위상이 반대라는 경고
  - §6 / §3의 `Pattern.SuccessAnimationClip` 언급을 `Pattern.PlayerAttack`(`ClipAlignment`)으로 갱신
- [x] `docs/PatternChain/Plan_PatternChain.md`의 각 단계 완료 표시 갱신

---

## 기존 시스템과의 충돌 감사

구현 전에 훑은 결과. **막는 것 없음** — 아래 넷은 처리를 정해야 하고, 셋은 확인만 하고 넘어간다.

### 처리 필요

**C1. 사슬 동안 플레이어가 완전히 멈춘다** → **(a) 그대로 둔다로 결정.** 사슬은 **짧은 패턴 여러 개**라(저작 전제) 사슬 전체가 1~2초에 끝난다. 그 구간은 "무대를 가로지르는 대시"가 아니라 **근거리 난타**로 읽히는 것이 맞다. 정지가 길게 느껴지면 아래 (b)를 켠다.

원인(기록용): §11-2의 "속도감의 심장"과 정면으로 맞선다.
`BindReservation`은 `currentOpponent == null`일 때만 `TakeTargetForWindow`를 부른다. 사슬은 상대를 유지하므로 **창 기반 표적 선택을 건너뛴다** → `BuildDuelPlan`이 이미 결투 거리에 선 적을 기준으로 계산해 플레이어 목표 위치가 현재 위치와 거의 같다. 거기에 Step 3(넉백 0)이 얹히면 **사슬 길이만큼 이동이 정확히 0**이다(3타 사슬 ≈ 4초 정지).
→ 남겨 둔 대안: (b) 사슬 타마다 결투 각도를 조금 돌린다(같은 거리, 다른 방향 — 플레이어가 적 주위를 도는 그림). (c) 넉백 부활은 Step 3을 되돌리는 것이라 안 한다.

**C2. 사슬에 `Attacker.Enemy` 패턴이 섞이면 넉백이 부활한다.**
Step 3은 `Attacker.Enemy`의 넉백을 유지한다(패링 성공으로 밀어내는 것은 그 연출의 핵심). 사슬 중간에 그런 패턴이 끼면 그 타에서만 적이 1.5m 밀리고, 그 재접근은 `TakeTargetForWindow`를 안 거쳐 **기어가는 구간**이 된다(`docs/FailConverge/`의 그 병).
→ 굽기 툴에서 **사슬 안에 attacker가 섞이면 경고**한다(Step 4에 추가). 코드로 막지는 않는다 — 의도한 저작일 수 있다.

**C3. 사슬 중간 타격에는 히트스톱이 적에게 안 걸린다** → **Step 3.6에서 건다로 결정.**
`EnemyDirector.ApplyHitStop`은 `pendingKills`(죽는 적)만 순회한다. 사슬 중간 타는 처치가 아니므로 **플레이어만 멈추고 적 리액션은 계속 흐른다.** §7-3의 "한쪽만 멈춰도 성립한다"가 예외로 두던 경우가 사슬에서는 **매 타격**이 된다.
→ Step 3.6. 리액션을 `Attack` 슬롯으로 재생하므로(Step 2) `AttackSpeed = 0`으로 얼면 된다 — 새 파라미터가 필요 없다.

**C4. 사슬 패턴이 `sliceTarget`을 들면 매 타마다 표적이 날아온다.**
템플릿 10개 중 다수가 `sliceTarget`을 물고 있다(예: `Pattern(0, 4, 8)`). 사슬에서는 **적을 때리는 옆에서 표적도 같이 갈라진다**가 3연속으로 반복된다.
→ 코드 문제가 아니라 저작 문제. 사슬용 패턴은 `sliceTarget`을 비우는 것이 기본이라고 §11-5 문서에 적는다.

### 확인만 (문제 없음)

**C5. 리액션이 견제 클립에 안 씹힌다 — 단 Step 2 때문에 창이 좁아진다.** `CrossFadeReaction`이 `reactionUntil = +0.6초`(`reactionHoldDuration`)를 걸고 `ApplyLocomotion`이 그 동안 물러난다. 다음 패턴의 `AssignFeint`도 즉시 재생이 아니라 `pendingScheduleStart` 예약이라 실측상 0.5초쯤 뒤에 시작한다.
⚠ **Step 2가 리액션을 임팩트(확정 +0.1초)로 미루므로 그만큼 늦게 시작한다.** 리액션 트림이 길면 견제가 끊는다 — 리액션 클립은 **0.5초 이하**로 트림하는 것을 기본으로 삼는다(§11-4의 견제 0.8초 규율과 같은 종류의 저작 제약).

**C6. 제자리 Sprint(다리만 젓는 그림)는 안 난다.** §6 경로 ②의 "Sprint 노출"은 **수렴 중일 때만** 성립하고, 도착해 서 있으면 `SwitchBaseStateUnlessConverging(idleStateHash)`(`CharacterActionPlayer.cs:363`)가 Idle을 건다. 이동이 0인 사슬은 언제나 Idle 쪽으로 떨어진다.

**C7. 이펙트 조건은 그대로 맞다.** `PatternEffectDirector.ShouldFire`의 `Success`는 `OnPatternComplete.AllCorrect`에서 온다 — 처치 여부가 아니라 **칼이 맞았는가**다. 사슬 중간 성공에 베는 이펙트가 뜨는 것이 맞다.

### 문서 부채 (Step 5에 포함)

- `docs/OpponentBinding/`와 CLAUDE.md §11-1의 **"성공하면 죽고, 실패하면 같은 적이 이어진다"**는 이제 경우가 셋이다 — 성공·처치 / **성공·미처치(사슬)** / 실패.
- CLAUDE.md §6 경로 ②의 "그 사이 Sprint를 노출"은 **수렴 중일 때만** 참이라는 단서가 빠져 있다(C6).

---

## 검증

- [x] 컴파일 통과 — `Assembly-CSharp` / `Assembly-CSharp-Editor` / `Pattern.Core` / `Enemy.Core` / `Enemy.Tests` / `ChartGen.Tests` 전부 오류 0 (`dotnet build`)
- [ ] 기존 `EnemyRingTests` 통과 (신규 테스트 없음 — 순수 로직이 늘지 않는다). ⚠ 컴파일만 확인했다 — 실행은 Unity Test Runner가 필요하다
- [ ] **아래는 전부 플레이 확인 항목이다(에디터에서 사람이 돌려야 한다)**
- [ ] 플레이 확인: 사슬 길이 3으로 구운 채보에서 ①중간 타격에 피격 리액션이 보인다 ②**적이 제자리에 남고 플레이어가 다시 붙으러 가지 않는다** ③마지막 타에서만 갈라진다
- [ ] 사슬 판정 확인(`chainKillRatio = 0.6`, 사슬 길이 3 → 2타 필요):
  - 3타 전부 성공 → 처치
  - 1타 놓치고 마지막 성공(성공 2) → **처치**
  - 2타 놓치고 마지막만 성공(성공 1) → **안 죽고** 같은 적과 다음 사슬로 이어짐
  - 마지막 타 실패(앞 2타 성공) → **안 죽는다.** 적은 회피/패링 모션만 재생
- [ ] 위 3번째 경우에서 그 적이 다음 사슬에 결국 죽는지 확인(카운터가 리셋되지 않아 성공이 누적된다)
- [ ] 히트스톱 확인: 사슬 중간 타격에서 **플레이어와 적이 같이 언다**(적 리액션만 흐르지 않는다). 해제 후 리액션이 원래 배속으로 이어지고, 다음 타격에서 다시 언다
- [ ] 리액션 클립 확인: 성공 = 맞는 모션 / 실패(제자리) = 막는 모션이 **칼이 닿는 순간에** 나온다(확정 시점이 아니라). 슬롯을 비운 패턴은 기존 `knockBack`/`parry` 스테이트로 떨어진다
- [ ] 기존 채보(전부 `killOnSuccess=true`)가 이전과 동일하게 재생된다 — 사슬 경로를 안 타므로 회귀가 없어야 한다
