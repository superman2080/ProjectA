# Research_PatternChain.md — 연계 공격(한 적에 패턴 여러 개)과 Pattern 에셋 정리

## 문제 정의

두 가지를 하려 했다.

1. **1대 다수(광역 일격)** — 패턴 하나로 적 N명을 동시에 벤다.
2. **연계 공격** — 한 적에게 패턴 여러 개를 이어 입력하고 마지막에 처치.

**1번은 이번 주제에서 뺀다**(사용자 결정). 배치 연출(결투 계획 N인 확장)·카메라 프레이밍 N인·사망 N건 동시가 전부 딸려 오는데, 그 셋은 연계와 공유하는 부분이 없어 같은 문서에 묶을 이유가 없다. 별도 주제로 남긴다.

**결정된 연계의 성질**(사용자 확정):

| 항목 | 결정 |
|------|------|
| 저작 위치 | 채보 엔트리(=`EnemyCue.killOnSuccess`). 새 `PatternChain` 에셋 만들지 않는다 |
| 사슬 도중 실패 | **사슬 계속** — 그 타격만 빗나가고 같은 적과 이어 싸운다 |
| 광역 | 이번 플랜에서 제외 |

---

## 핵심 발견: 연계는 이미 런타임에 성립한다

`EnemyDirector.BindReservation` (`Enemy/EnemyDirector.cs:709`):

```csharp
// 상대가 비어 있으면(직전 교전이 처치로 끝났다) 창에 맞는 거리의 적을 새로 고른다.
// 살아 있으면 그대로 이어 싸운다 — "실패하면 같은 상대와 계속"이 여기서 지켜진다.
if (currentOpponent == null) { ... TakeTargetForWindow ... }
```

`currentOpponent`가 비는 유일한 경로는 `KillOpponent`(`:927`)이고, 그건 `ResolveReservation`(`:829`)의

```csharp
if (r.cue.killOnSuccess && playerSucceeded) KillOpponent(...);
```

한 줄이 정한다. 즉 **`killOnSuccess = false`인 엔트리는 성공해도 상대가 유지되어 다음 패턴이 같은 적에게 붙는다.** 이것이 곧 사슬이고, 실패해도 유지되므로 "사슬 계속"도 이미 현재 동작이다.

**따라서 연계에 필요한 새 데이터 모델은 없다.** `killOnSuccess = false`의 연속이 곧 사슬이고, `true`인 엔트리가 그 사슬의 마무리다. 남는 문제는 **그 상태가 화면과 저작 툴에 안 나타난다**는 것뿐이다.

---

## 구멍 A — 안 죽는 성공에 리액션이 없다

`EnemyView.Resolve` (`Enemy/EnemyView.cs:454`):

```csharp
if (!playerSucceeded)              CrossFadeReaction(parried ? parryStateName : evadeStateName);
else if (attacker == Attacker.Enemy) CrossFadeReaction(knockBackStateName); // 적 공격을 막아냈다 → 밀려난다
else                                 CrossFadeReaction(idleStateName);
```

마지막 줄이 사슬 중간 타격이다 — `Attacker.Player`(플레이어가 벤다) + 성공 + `killOnSuccess = false`. **Idle로 간다.** 칼은 지나갔는데 적은 아무 일 없다는 듯 서 있다.

`killOnSuccess` 기본값이 `true`라 지금까지 이 경로가 거의 안 밟혔고(성공하면 늘 죽었다), 연계를 켜는 순간 **주 경로가 된다**.

`knockBackStateName`은 이미 존재하고 배선돼 있다(바로 윗줄이 쓴다). 새 스테이트가 필요 없다.

---

## 구멍 B — 성공 후퇴가 무조건 1.5m

`EnemyDirector.ResolveRetreatDistance` (`:869`):

```csharp
if (playerSucceeded || AttackerOf(r.template) == Attacker.Enemy) return failRetreatDistance;
```

성공이면 창을 보지 않고 즉시 `failRetreatDistance`(1.5m)를 돌려준다. `Attacker.Enemy` 성공(패링 성공 → 적이 밀려남)에는 옳지만, **사슬 중간 타격마다 적이 1.5m 밀려나고 플레이어가 매번 따라붙는다.**

그리고 그 재접근은 `TakeTargetForWindow`를 거치지 않으므로(같은 적이 유지된다) **거리를 창에 맞추는 §11-2 규율이 빠져 있다** — 창이 짧으면 그대로 늘어져 0.9 m/s로 기어간다. 이건 이미 문서화된 병이고(`docs/FailConverge/`), 그래서 **실패 경로에는 창 조건이 붙어 있다**(같은 함수 `:874~890`):

```csharp
float travel  = failRetreatDistance * playerShare / cruiseSpeed;
float needed  = retreatDuration + travel + retreatWindowMargin;
return (arriveTime - Time.time) >= needed ? failRetreatDistance : 0f;
```

**성공-비처치는 실패와 정확히 같은 처지다**(같은 적이 유지되고, 재접근이 필요하고, 거리가 창에 안 맞춰진다). 조건을 공유하면 된다 — 새 노브가 아니다.

---

## 구멍 C — 저작 툴에 사슬이 안 보인다

`PatternChartWindow` (`ChartGen/Editor/PatternChartWindow.cs`):

- 일괄: `전부 켜기` / `전부 끄기` / `매 N번째만 처치` / `패턴 단위 켜기·끄기` (`:400~461`)
- 엔트리 행: `killOnSuccess` 토글 하나(`:563`) + 요약 문자열에 `· 성공 시 처치`(`:534`)

문제 둘:

1. **`ApplyKillInterval`의 위상이 사슬과 반대다** (`:459`):
   ```csharp
   drafts[i].enemyCue.killOnSuccess = i % interval == 0;
   ```
   `interval = 3`이면 `[처치][비][비][처치][비][비]` — **첫 타가 마무리**다. 사슬은 `[비][비][처치]`, 즉 `i % N == N-1`이어야 한다. 지금 툴로 사슬을 만들면 매 사슬의 첫 타에서 적이 죽고 남은 두 타가 새 적에게 간다.
2. **사슬 경계가 리스트에서 안 읽힌다.** 엔트리 수백 개에서 토글 하나만 보고 "이게 몇 번째 타인지"를 셀 수 없다.

또 하나: 채보 끝이 `killOnSuccess = false`로 끝나면 **마지막 적이 안 죽고 남는다**(마무리 없는 사슬). 지금은 경고가 없다.

---

## 곁가지 발견 — Pattern 에셋의 죽은 레거시 5필드

`Pattern.cs:33~46`:

```csharp
successAnimationClip / animationStartOffset / animationDuration / animationImpactTime / animationSpeed
```

이건 `playerAttack`(`ClipAlignment`)로 이관되기 전의 구식 슬롯이다. 확인 결과:

| 확인 항목 | 결과 |
|-----------|------|
| 런타임 코드 참조 | `CharacterActionPlayer.cs:625~651` **폴백 분기 한 곳뿐** ("아직 ClipAlignment로 이관되지 않은 기존 패턴") |
| 에디터/툴 참조 (`serializedProperty` 포함) | **0곳** — 짝 에디터·차트 툴·이펙트 툴·슬라이서 전부 `playerAttack`을 읽는다 |
| 템플릿 에셋 10개의 `playerAttack` | **전부 채워져 있음.** `Pattern(4).asset`은 오히려 `playerAttack`이 최신이고 레거시 필드가 낡은 클립을 가리킨다 |

즉 **폴백은 아무도 안 타는데, 낡은 값이 남아 있어서 "둘 중 뭐가 진짜냐"를 매번 확인해야 하는 상태**다. 연계 작업이 `Pattern`을 건드리는 김에 지운다. 관련 주석도 낡았다 — `PatternChartWindow.cs:397`이 아직 `Pattern.SuccessAnimationClip`을 근거로 설명한다.

---

## 건드리지 않는 것 (사슬이 이미 무해한 층)

| 시스템 | 사슬에서의 거동 | 판단 |
|--------|----------------|------|
| 히트스톱 | 타격마다 발동(`AllCorrect` 기준) | 그대로 — 사슬에서 오히려 맞다 |
| 카메라 앵글 교체 | 패턴 경계마다 자격 검사 | 그대로 — 같은 적을 계속 담으므로 무해 |
| 카메라 프레이밍 | 상대 불변이라 갱신 없음 | 그대로 |
| `SliceTargetDirector` | 패턴별 `sliceTarget`이 뜬다 | 그대로 — 사슬 이전과 같은 성질 |
| `PatternEffectDirector` | 조건 `Success`/`Parry`/`Evade`는 반응 이벤트를 따른다 | 그대로 — 구멍 A를 고치면 자동으로 맞다 |
| `CharacterActionPlayer` | 패턴별 클립, 연계 링크(`comboLinkWindow`)가 이미 있다 | 그대로 |

---

## 참조

- `Assets/02. Scripts/Enemy/EnemyDirector.cs` — `BindReservation:709`, `ResolveReservation:816`, `ResolveRetreatDistance:869`, `KillOpponent:916`
- `Assets/02. Scripts/Enemy/EnemyView.cs` — `Resolve:440`
- `Assets/02. Scripts/Enemy/EnemyCue.cs` — `killOnSuccess`
- `Assets/02. Scripts/Pattern/Pattern.cs` — 레거시 5필드
- `Assets/02. Scripts/Character/CharacterActionPlayer.cs:620~655` — 레거시 폴백
- `Assets/02. Scripts/ChartGen/Editor/PatternChartWindow.cs:390~570` — 일괄 툴·엔트리 행
- 배경: `docs/OpponentBinding/`, `docs/FailConverge/`, `docs/StageTraversal/`
