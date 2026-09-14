# Plan — 실패해도 칼은 나간다 (FailSwing)

## 목표

`Attacker.Player` 패턴을 실패해도 **베기 클립이 끝까지 재생되고**, 적이 그 임팩트에 맞춰 **막는 자세**를 취한다.
성공과 실패의 차이는 "칼이 나가느냐"가 아니라 **"막히느냐"**가 된다.

## 설계 요약

- **새 클래스·새 필드·새 클립·새 스테이트가 0개다.** 패링 경로(`Pattern.EnemyParry` → 없으면 `parryStateName`)는 이미 있고,
  임팩트 정렬도 이미 실패 경로에서 성립한다(Research §5).
- 고치는 것은 **분기 둘**뿐이다 — 하나는 뒤집고, 하나는 지운다.

## Step 1 — 첫 미스가 스윙을 죽이지 않게 (`CharacterActionPlayer`)

- [x] `HandleJudgeTargetFirstMiss`에서 **`Attacker.Player` · 비(非)상호공격이면 곧바로 return** 한다.
      `hasPending`을 유지하므로 예약된 베기가 제시각에 시작하고, 이미 시작했다면 그대로 이어진다.
- [x] 그 분기의 `RaiseSwingEnded()` 호출을 **뺀다** — 스윙이 계속되는데 끝났다고 알리면
      `swingActive`가 내려가 히트스톱 가드가 풀리고 트레일이 중간에 꺼진다(Research §6).
- [x] 나머지 둘(`Attacker.Enemy` · `CountersOnFail`)은 **그대로** 예약을 버리고 `impactTime`에 피격을 예약한다.
- [x] `inLeadIn` 정리 블록은 **취소 경로 쪽에만 남긴다**. 스윙을 살리는 경로에서는 리드인 시퀀스가
      마지막 클립까지 이어져야 한다(끊으면 정렬 앵커인 마지막 클립이 영영 안 나온다, §6-1).
- [x] `TryStartPendingSuccess`의 `missedThisTarget` 가드를 **지운다.**
      취소해야 하는 경우는 `hasPending = false`가 이미 막는다. **가드를 남기면 Step 1이 아무 효과가 없다.**
- [x] ⚠ **필드 자체는 남긴다** — Research §2의 *"사용처가 그 한 줄뿐"*이 틀렸다.
      `HandleJudgeTargetFirstMiss` 맨 앞의 재진입 가드가 그 필드를 읽는다. 지우면 첫 미스가 두 번 들어올 때
      피격이 두 번 예약된다. bool 하나 값의 안전장치다.

## Step 2 — 실패한 적은 물러나지 말고 막는다 (`EnemyDirector`)

- [x] `ResolveRetreatDistance`에서 `Attacker.Player`의 실패가 **창을 보지 않고 `0f`를 돌려주게** 한다.
      거리 0이 곧 패링이라는 규칙은 `EnemyView.Resolve`의 `parried` 식에 이미 있고,
      `reactionClip` 선택식도 그 값을 보고 `Pattern.EnemyParry`를 고른다 — **그림 쪽에 새 코드가 0줄이다.**
- [x] 그 아래의 창 계산(`next` · `arriveTime` · `travel` · `needed`)을 **지운다.**
      연타(§2-1)와 상호 공격(§11-10)이 이미 같은 이유로 그 판단을 건너뛰고 있어, 그 둘의 조기 `return 0f`도
      이 한 줄에 흡수된다.
- [x] 그 결과 쓰이지 않게 되는 `retreatWindowMargin` 필드를 **지운다**(마지막 사용처였다).
- [x] ⚠ `failRetreatDistance`·`evadeStateName`은 **남는다** — `Attacker.Enemy`의 결말(벤 뒤의 여파)이 여전히 쓴다.
- [x] ⚠ `PatternEffectCue`의 `Evade` 조건은 `Attacker.Player`에서 이제 **상호 공격의 피격에만** 뜬다(§11-10).
      기존 큐가 그 조건으로 저작돼 있으면 안 뜨게 된다 — 실측 확인 대상이다.

## Step 3 — 문서

- [x] CLAUDE.md §6의 *"`PatternHandler.OnPatternComplete` 구독"*을 고친다 — 이 클래스는 그 이벤트를 구독하지 않는다.
      예약은 `OnJudgeTargetBegan`에서, 취소는 `OnJudgeTargetFirstMiss`에서 일어난다.
- [x] §6의 *"헛스윙으로 끝난다"* · §11-2의 *"실패한 적의 반응은 창이 고른다"*를 새 규칙으로 바꾼다.
- [x] §11-2의 `retreatWindowMargin` 언급을 지운다.

## Step 4 — 검증

- [x] 컴파일 에러 0.
- [ ] `Attacker.Player` 패턴의 **첫 노드**를 일부러 틀린다 → 베기 클립이 전부 재생되고, 임팩트에 적이 막는 자세를 취한다.
- [ ] **마지막 노드**만 틀린다 → 위와 같은 그림(시작 시점이 달랐던 비대칭이 사라진다).
- [ ] 성공했을 때의 절단·처치·히트스톱이 **예전 그대로**다(실패 경로만 바뀐다).
- [ ] `Attacker.Enemy` 패턴 실패 → 여전히 피격이 나고 체력이 깎인다(§7-6).
- [ ] 상호 공격 패턴(`enemyAttack` 배선된 `Attacker.Player`) 실패 → 여전히 맞는다(§11-10).
- [ ] 실패가 이어져도 적이 제자리에 남아 플레이어가 기어가는 구간이 안 생긴다(`docs/FailConverge/`).
