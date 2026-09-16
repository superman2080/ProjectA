# Plan — 관통 패턴의 복귀 (뚫고 지나간 자리에서 다시 마주본다)

근거: `Research_PassThroughRecovery.md`

## 0. 설계 요지

**고치는 것은 축 하나다.** `playerShare = 1`이라 다음 패턴에서 플레이어가 적의 어느 쪽에 설지는 `BuildDuelPlan`의 `dir`만이 정한다(플레이어 위치는 식에서 소거된다). 관통 패턴이 끝난 뒤 그 축을 뒤집으면, 플레이어는 **서 있던 자리가 곧 새 앞자리**가 되고 회전·간격 조절은 기존 경로가 그대로 한다.

| 항목 | 값 |
|---|---|
| 새 저작 필드 | **0개** — 커브 끝 키의 부호가 곧 의도다 |
| 고치는 파일 | 순수 함수 1 + 게터 1 + 디렉터 1줄 |
| `PlayerCombatMover` · `PatternHandler` · `ChartPlayer` | **한 줄도 안 고친다** |

**⚠ 조건은 성패가 아니라 "상대가 살아남았다 + 커브가 음수로 끝난다"다.** 사용자는 실패에서 만났지만 사슬(§11-5, `killOnSuccess = false`)의 성공 경로도 상대가 살아남아 **같은 증상**이다. 성패로 가르면 두 경로가 다르게 동작하는 규칙이 하나 더 생긴다.

**⚠ `ResolveAxis`의 기존 가드는 안 건드린다.** 그것은 *전이 상태에서 축을 파생시키는 것*을 막는 장치이고(Research §3), 여기서 하는 일은 *저작된 최종 상태*를 반영해 축을 **의도적으로** 뒤집는 것이다. 둘은 다른 사건이다.

---

## Step 1 — `DuelGap`에 질문 하나 추가
- [x] `Assets/02. Scripts/Pattern/Core/DuelGap.cs`에 `EndsBehind(AnimationCurve)` 추가 — `Has(curve) && curve[curve.length - 1].value < 0f`.
- [x] 주석에 근거를 적는다: **커브가 음수로 끝난다는 것은 "이 패턴이 끝나면 플레이어가 적 반대편에 서 있다"는 뜻**이고, 그게 전이가 아니라 최종 상태라는 사실이 축을 뒤집어도 되는 유일한 근거다.
- [x] ⚠ `EndTime`(시각)과 헷갈리지 않게 이름을 부호에 맞춘다 — 이쪽은 **값**을 본다.

## Step 2 — 테스트
- [x] `Assets/02. Scripts/Pattern/Tests/DuelGapTests.cs`에 추가:
  - `EndsBehind` — 음수로 끝나는 커브 / 양수로 끝나는 커브 / 중간만 음수이고 양수로 끝나는 커브(**false여야 한다**) / 빈 커브(false).
  - **뒤집은 축이 두 경우 모두 이긴다** — `ResolveAxis(원래방향, 뒤집은축)`과 `ResolveAxis(뒤집은방향, 뒤집은축)`이 **둘 다 뒤집은 축**을 돌려준다. Research §6 표가 이 성질에 통째로 걸려 있다(계획이 관통 전에 잡히든 후에 잡히든 결과가 같아야 한다).

## Step 3 — `Pattern` 게터
- [x] `Assets/02. Scripts/Pattern/Pattern.cs`에 `DuelCurveEndsBehind => DuelGap.EndsBehind(duelDistanceCurve)` — 기존 `HasDuelDistanceCurve`·`DuelCurveStartTime`·`DuelCurveEndTime`과 같은 자리·같은 관용구.

## Step 4 — `EnemyDirector`에서 축 뒤집기
- [x] `ResolveReservation`의 **상대가 살아남는 `else` 분기 안**에서, 해결된 패턴이 `DuelCurveEndsBehind`이고 그 상대가 `duelAxisOwner`이면 `duelAxis = -duelAxis`.
- [x] **⚠ `BindNextReservation()`보다 앞이어야 한다** — 그 줄 안에서 다음 `BuildDuelPlan`이 돈다(§11-1).
- [x] **⚠ 0으로 비우지 않고 뒤집는다.** 비우면 `BuildDuelPlan`이 그 순간의 플레이어 위치를 믿는데, 계획은 임팩트보다 이르게 잡혀 **아직 관통 전일 수 있다** → 축이 안 뒤집혀 증상이 그대로 남는다(Research §6 표).
- [x] 처치 분기에는 아무것도 안 넣는다 — 상대가 바뀌면 `BuildDuelPlan`이 이미 `previousAxis`를 비운다.

## Step 5 — 검증
- [x] `Pattern.Tests` 통과 확인 — **13/13**(기존 11 + 신규 2).
- [x] 실제 에셋 검출 확인 — 커브 보유 7개 중 **3개**가 관통 종료: `Pattern(4, 3, 1, 5, 7)`(끝값 −5.00m) · `Pattern(6, 1, 8)`(−1.50m) · `Pattern(6, 7, 3, 4)`(−0.60m). 나머지 4개는 양수로 끝나 **축을 안 건드린다**(회귀 없음).
- [ ] 플레이 검증 — 커브가 음수로 끝나는 패턴(`Pattern(4, 3, 1, 5, 7)` 등)을 **일부러 실패**시키고 확인할 것 넷:
  1. 플레이어가 적 뒤에 **선 채로 남는다**(앞으로 되돌아가지 않는다).
  2. 그 자리에서 **적을 향해 돌아선다**(`PlayerCombatMover.ApplyPlan`의 기존 회전).
  3. 적도 플레이어 쪽으로 돌아선다(`EnemyView.TickGaze`).
  4. 다음 패턴의 커브가 **거울이 아니라 정상으로 돈다** — 양수 구간에서 물러나고 음수 구간에서 다시 관통한다(이번엔 반대 방향으로).
- [ ] **⚠ 사슬(§11-5) 확인** — `killOnSuccess = false`인 관통 패턴을 **성공**시켰을 때도 같은 동작인지. 같아야 한다(Step 0의 조건이 성패를 안 본다).
- [ ] **⚠ 회귀 확인** — 커브가 **양수로 끝나는** 기존 패턴은 실패해도 예전과 똑같아야 한다(축을 안 건드리므로 구조적으로 그래야 한다).

## Step 6 — CLAUDE.md 갱신
- [x] §11-9에 한 줄: **커브가 음수로 끝나는 패턴은 그 교전이 끝날 때 결투 축이 뒤집힌다** — 뚫고 지나간 자리가 다음 패턴의 앞자리가 된다. ⚠ 조건은 성패가 아니라 "상대 생존 + 커브 끝이 음수"이고, **비우는 게 아니라 뒤집는** 이유(계획이 관통보다 먼저 잡힌다)를 같이 적는다.

---

## 하지 않는 것
- `ResolveAxis`의 기존 가드 수정 — 다른 사건을 막는 장치다(Research §3).
- 저작 토글(`flipAxisOnPassThrough` 같은 bool) — 커브 끝 부호가 이미 의도를 말한다. 두면 "음수인데 껐다"/"양수인데 켰다"가 새로 생긴다.
- 실패 전용 분기 — 사슬 성공 경로가 같은 증상이라 규칙이 둘로 갈린다.
- 관통 후 위치를 보정하는 별도 이동 — 기존 `ShouldDeferHandover` + `ApplyPlan`이 이미 그 일을 한다.
