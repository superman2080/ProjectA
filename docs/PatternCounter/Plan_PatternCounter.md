# Plan — 상호 공격 패턴 (PatternCounter)

근거: `Research_PatternCounter.md`

## 설계 요약

> **`Attacker.Player` 패턴에 `enemyAttack` 클립이 배선돼 있으면, 적은 견제 대신 진짜 공격을 같이 휘두른다.**
> 두 칼이 같은 시각(`Deadline + ImpactOffset`)을 겨눈다.
> **성공하면** 적이 먼저 맞아 공격이 `EnemyHit`/`EnemyDeath`로 끊기고,
> **실패하면** 적의 공격이 끝까지 재생돼 그 임팩트에 `OnPlayerHit`이 난다 → 체력이 깎인다.

**견제(`EnemyFeint`)를 진짜 공격으로 바꾼 것이 전부다.** 구간·정렬 시각·배정 지점이 견제와 같아서
`Attacker.Enemy` 패턴과 창이 완전히 같다 — 반격 클립의 창이 0.1초로 쪼그라드는 문제가 없다.

**새 필드가 0개다.** `Attacker.Player` + `enemyAttack`은 지금 `OnValidate`가 "안 쓰이는 슬롯"으로
경고만 하던 표현 불가능한 조합이다. 거기에 의미를 준다.

### 왜 새 bool을 안 만드는가
bool이면 "켰는데 클립이 없다"/"클립은 있는데 껐다" 두 어긋난 조합이 새로 생긴다.
클립 유무가 곧 의도라 그 부류가 원천 소멸한다.

### 성공 경로는 이미 완성돼 있다
`ResolveReservation`이 성공+`Attacker.Player`에 `r.template.EnemyHit`을 골라
`impactTime - ReactionLead`에 예약한다. 그게 적의 공격 클립을 자기 임팩트 직전에 끊는다.
`killOnSuccess`면 `KillOpponent`의 `EnemyDeath`가 끊는다. **양쪽 다 한 줄도 안 고친다.**

---

## 단계

- [x] **Step 1 — `Pattern.CountersOnFail`**
  - 게터 하나: `public bool CountersOnFail => attacker == Attacker.Player && enemyAttack != null && enemyAttack.IsUsable;`
  - `OnValidate`의 미사용 슬롯 경고(`Pattern.cs:511-514`)에서 **`Attacker.Player` + `enemyAttack`을 뺀다.**
  - 대신 **`enemyAttack`과 `enemyFeint`가 둘 다 배선되면 경고**한다 — 상호 공격이 견제를 대체하므로 견제는 죽은 데이터다.
  - `enemyAttack` 툴팁 갱신: "`Attacker.Enemy`면 적의 공격. **`Attacker.Player`면 상호 공격** — 실패하면 이 칼이 닿아 플레이어가 대미지를 입는다."

- [x] **Step 2 — 적이 같이 휘두른다 (`EnemyDirector.AssignEngageClip`)**
  - 견제 선택식 앞에 한 줄:
    ```csharp
    if (r.template != null && r.template.CountersOnFail)
    {
        opponent.AssignAttack(r.template.EnemyAttack, r.impactTime, plan.EnemyPosition, plan.PlayerPosition, maxAttackSpeed);
        return;
    }
    ```
  - ⚠ **견제의 `minFeintWindow` 가드를 물려받지 않는다.** 견제는 짧으면 깜빡임이라 안 거는 게 나았지만,
    상호 공격은 **안 걸면 실패해도 안 맞는다** — 창이 짧으면 배속으로 압축해서라도 재생해야 한다
    (`AssignAttack`이 시작 시점에 남은 시간으로 배속을 재계산하므로 자기 수정된다).

- [x] **Step 3 — 플레이어가 맞는다 (`CharacterActionPlayer`)**
  - `SchedulePendingSuccess`(:838)에서 `currentAttacker` 옆에 `currentCountersOnFail = info.Template.CountersOnFail;` 캐시.
  - `HandleJudgeTargetFirstMiss`(:1195)의 역할 가드:
    ```csharp
    if (currentAttacker != Attacker.Enemy && !currentCountersOnFail)
    {
        RaiseSwingEnded();
        return;
    }
    hasPendingHit = true;
    pendingHitTime = pendingImpactAlignTime;
    ```
  - **플레이어 쪽은 이게 전부다.** `TryStartPendingHit` → `PlayHitReaction` → `OnPlayerHit` →
    `PlayerHealth`·`CameraDirector`(`PatternMiss` 큐)가 기존 배선 그대로 따라온다.

- [x] **Step 4 — 실패에서 적의 공격을 안 끊는다 (`EnemyView.Resolve`)**
  - 선택 인자 하나 추가: `bool keepAttackClip = false`.
  - 리액션 분기의 `else`를 감싼다:
    ```csharp
    if (keepAttackClip)
        reactionUntil = Time.time + Mathf.Max(reactionHoldDuration, 0f); // 따라베기 잔여를 노출하고 로코모션을 막는다
    else { /* 기존 parry/evade/knockBack 크로스페이드 */ }
    ```
  - ⚠ **`Current = Phase.Recover`는 그대로 부른다.** `ApplyLocomotion`이 `Phase.Windup`에서 물러나므로
    `Resolve`를 아예 건너뛰면 적이 **Windup에 영구히 갇힌다**(빠져나오는 경로가 `Resolve`/`MarkDying`뿐).
    `reactionUntil`이 그 사이 로코모션·상체 마스크를 막아 클립이 살아남는다 — **새 상태 필드가 0개다.**

- [x] **Step 5 — 실패 결말 배선 (`EnemyDirector.ResolveReservation`)**
  - `ResolveRetreatDistance`에 한 줄(`Attacker.Enemy` 분기 뒤, `playerSucceeded` 분기 앞):
    ```csharp
    if (!playerSucceeded && r.template != null && r.template.CountersOnFail) return 0f; // 물러나면 칼이 안 닿는다
    ```
  - `reactionClip` 실패 선택식에서 상호 공격을 뺀다(`EnemyParry`로 덮이면 안 된다).
  - `opponent.Resolve(..., keepAttackClip: 상호공격 && !playerSucceeded)`.
  - `OnEnemyReacted`를 **`Evade`로** 보낸다 — 넉백 0이라 그냥 두면 `Parry`가 나오는데 적은 막은 게 아니라 베었다.
    `Attacker.Player`에서 `Evade`의 뜻이 이미 "플레이어가 맞았다"라 저작 규칙이 안 늘어난다(§7-4).

- [x] **Step 6 — 저작 툴 표시**
  - `Tools/Pattern Chart Tool`: `Attacker.Player` + `CountersOnFail` 엔트리에 **`상호 공격`** 배지.
    실패 시 체력이 깎이므로 그 밀도가 보여야 한다.
  - `Tools/Animation Clip Trimmer`의 적 슬롯 선택기에서 `enemyAttack`이 `Attacker.Player` 패턴에도 뜨게 한다
    (지금은 `Attacker.Enemy` 전용으로 걸러질 가능성 — 확인 후 필요하면 수정).

- [x] **Step 7 — 검증**
  - ~~`Pattern/Tests`에 `CountersOnFail` 조합 테이블 테스트~~ — **불가능하고 불필요하다.**
    `Pattern.Tests` asmdef는 `Pattern.Core`만 참조하는데 `Pattern`(ScriptableObject)은 Assembly-CSharp에 있어
    닿지 않는다. 그 하나 때문에 asmdef를 늘리는 것은 `CountersOnFail`이 `IsUsable` 위의 한 줄 논리곱인 데 비해 과하다.
    실제 위험은 그 식이 아니라 **런타임 배선**이므로 아래 실측이 진짜 검증이다.
  - 컴파일 확인(Unity 콘솔).
  - 에디터 실측, 상호 공격 패턴 하나로:
    - **성공** → 적 공격이 임팩트 직전 `EnemyHit`(또는 `EnemyDeath`)으로 끊긴다 · 플레이어 무피해
    - **실패** → 적 공격이 끝까지 재생된다 · 임팩트에 피격 모션 · 체력 1 감소 · 카메라 피격 큐
    - **실패 직후 다음 패턴** → 적이 Windup에 안 갇히고 정상 배정된다

---

## 열어 두는 것 (지금 안 한다)

- **반격 전용 대미지 배수** — `PlayerHealth`는 목숨 카운터지 HP 바가 아니다(§7-6). 1이면 충분하다.
- **상호 공격에서 플레이어가 늦게 성공했을 때의 상쇄 연출** — 지금은 성공이면 무조건 적이 먼저 맞는다.
- **플레이어 회피(Space)로 흘리기** — §11-8의 `DodgeDirector`가 이미 그 사건을 소유한다. 겹치면 회피 키가 두 가지 뜻을 갖는다.
