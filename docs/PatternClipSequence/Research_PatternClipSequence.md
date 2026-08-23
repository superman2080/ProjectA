# Research — 한 패턴 안의 클립 순차 재생 (PatternClipSequence)

> 요구: **한 패턴이 애니메이션 클립을 여러 개 들고 A → B → C 순서로 이어 재생한다.**
> 이 문서는 설계가 아니라 **현재 구조가 무엇을 전제하고 있는지**와 **그 전제를 깨는 지점**을 정리한다.

---

## 0. 먼저 — 이 요구를 이미 덮는 두 경로 (기각 근거를 남긴다)

| 기존 경로 | 무엇을 하는가 | 왜 이 요구를 못 덮는가 |
|---|---|---|
| `ClipAlignment.extraImpactTimes` (§7-3-1) | **클립 하나** 안의 여러 칼질에 히트스톱을 건다 | **클립이 하나다.** 서로 다른 모션 3개를 잇는 게 아니라, 한 모션 안의 마크일 뿐 |
| `EnemyCue.killOnSuccess = false` 사슬 (§11-5) | 짧은 **패턴 여러 개**를 같은 적에게 잇는다 → 패턴마다 자기 `PlayerAttack` | **패턴이 여러 개다.** 노드 입력도 그만큼 나뉜다. "노드 3개짜리 패턴 하나에 모션 3개"는 표현 불가 |

즉 이 요구가 요청하는 것은 **"입력 단위는 패턴 하나인데 모션은 여럿"**이다. 위 둘 중 어느 쪽도 그 조합을 표현하지 못한다.

> ⚠ **용어 충돌**: §11-5가 이미 "사슬(chain)"을 선점했다(`PatternChain(4, 6)` 에셋도 있다). 이 기능은 반드시 **`ClipSequence`**로 부른다 — 문서·필드·툴 전부.

---

## 1. 지금 클립 하나가 재생되는 전체 흐름

`CharacterActionPlayer` 하나가 전부 든다. 관여하는 것은 **7단계**다.

```
OnJudgeTargetBegan(JudgeTargetInfo)
  └─ SchedulePendingSuccess()               ← 예약 (클립·트림·배속·시작시각 확정)
       impactAlignTime = info.ImpactTime()   = Deadline + Pattern.ImpactOffset
       alignment = Attacker.Enemy ? PlayerParry : PlayerAttack
       pendingFreeze = extras.Length × HitStopDirector.HitStopDuration     ← 정지 예산(§7-3-1)
       pendingScheduleStart = max(impactAlign − pendingFreeze − impactSpan/speed,
                                  info.FirstNodeTime)                       ← ⚠ 하한 클램프
  └─ RaiseIdleWindow()                       ← 기습 창(§11-8)이 pendingScheduleStart를 창의 끝으로 쓴다

Update()
  └─ TryStartPendingSuccess()                ← 시작 시각 도달 → 배속 역산 후 PlaySlot()
       speed = clamp(impactSpan / 남은시간, baseSpeed, maxAttackSpeed=2.5)
  └─ PlaySlot(clip, startOffset, dur, speed, isSwing:true)
       듀얼 슬롯(Attack_A/Attack_B) 교대 + CrossFadeInFixedTime(0.15초)
       ⚠ 여기서 재생 상태 전부를 리셋한다 (아래 §2)
  └─ actionEndTime 통과 → AttackSpeed=1 복원 + RaiseSwingEnded()
  └─ recoveryEndTime 통과 → 세 복귀 경로(연계 유지 / Sprint 노출 / Release)
```

**정렬의 계약은 하나다**: 클립의 임팩트 프레임이 `Deadline + ImpactOffset`에 온다. 시작 시점과 배속을 **역산**해서 맞춘다(§6).

---

## 2. "클립은 언제나 하나"를 전제하는 상태 (`PlaySlot`이 전부 리셋한다)

`PlaySlot`은 호출될 때마다 아래를 **0/NaN으로 밀어 버린다**. 순차 재생은 이 함수를 N번 부르는 일이므로, **여기 리셋되는 것 하나하나가 그대로 회귀 지점**이다.

| 필드 | 지금 의미 | 순차 재생에서 깨지는 방식 |
|---|---|---|
| `clipConsumed` | 이번 재생에서 소비한 클립 초 | **0으로 리셋된다.** 아래 넷이 전부 이 값에서 파생되므로 연쇄로 깨진다 |
| `playingImpactSpan` | 정렬 대상의 임팩트 스팬 | 클립마다 다름. "체인 전체에서 마지막 임팩트까지" 개념이 없다 |
| `playingImpactAlignTime` | 이 헤드가 **어느 패턴의 것인지**(신분증) | 체인 중간 클립도 같은 패턴이므로 값은 같아야 한다 — 리셋되면 `PlayerCombatMover`가 커브 구동을 놓는다 |
| `playingBaseSpeed` | 저작 배속. `DuelCurveTime`의 시간 단위 | 클립마다 다르면 **거리 커브의 t 축이 클립 경계에서 튄다**(§11-9) |
| `playingExtraSpans` / `extraCursor` | 다중 히트스톱 커서 | 클립마다 따로 있어야 하는데 예산(`pendingFreeze`)은 체인 전체 합이어야 한다 |
| `actionEndTime` | 트림 끝 = **복귀 판단 시작점** | 중간 클립의 트림 끝에서 복귀가 시작되면 **체인이 1타 만에 끝난다** |
| `recoveryEndTime` | 마무리 노출 끝 | 위와 동일 |
| `speedRestored` | 트림 끝에서 배속 1 복원 래치 | 중간 클립마다 서면 배속 역산이 무의미해진다 |
| `hitStopped` | 정지 중 | `PlaySlot`이 무조건 false로 끈다 — **체인 전환이 진행 중인 히트스톱을 삼킨다** |
| `segmentStartTime` / `playSpeed` | 배속 구간 스냅샷 | 클립 경계마다 새 구간이 열려야 함(그건 맞다) |
| `blendInStartTime` / `blendOutLatched` | 레이어 웨이트 블렌드 | 클립 경계마다 blend-in을 재시작하면 **웨이트가 매 타 깜빡인다** |
| `releaseTriggered` / `releaseEndTime` | Release 진입 래치 | 중간 클립에서 Release가 새면 안 된다(과거 그 버그가 있었다 — §6) |
| `useSlotA` | 듀얼 슬롯 핑퐁 | **여기만 순차 재생에 이미 맞다.** A→B→A→… 로 자연히 이어진다 |

### 파생 소비자 — `clipConsumed`가 살아 있어야 성립하는 것들

```
DuelCurveTime = (clipConsumed − playingImpactSpan) / playingBaseSpeed     ← §11-9 결투 거리 커브
IsBeforeImpact() = playingImpactSpan > 0 && clipConsumed < playingImpactSpan
   ├─ ApplyHitStop(): 임팩트 前이면 복귀 스케줄을 밀지 '않는다'
   └─ ReleaseHitStop(): 임팩트 前이면 배속 따라잡기
EmitNextExtraImpact(): remainingSpan = playingExtraSpans[cursor] − clipConsumed
```

> **핵심**: `clipConsumed`는 지금 **"이 클립에서 얼마나 소비했나"**인데, 순차 재생에서는 **"이 체인에서 마지막 임팩트까지 얼마나 남았나"**가 되어야 한다. 이 한 값의 의미 변경이 위 세 소비자를 전부 좌우한다.

---

## 3. 소유·소비 지도 (blast radius)

`Pattern.PlayerAttack` / `PlayerParry`를 읽는 곳은 **네 군데뿐이다.** 예상보다 좁다.

| 위치 | 무엇에 쓰는가 | 순차 재생이 요구하는 것 |
|---|---|---|
| `CharacterActionPlayer.cs:769` | 런타임 재생 (유일한 런타임 소비자) | **전면 개편 대상** |
| `PatternEffectWindow.cs:755` | 이펙트 툴의 애니메이션 프리뷰 | 어느 클립 구간인지 알아야 프리뷰가 맞는다 |
| `MeshSliceBakerWindow.cs:332,1156` | **`playerAttack`의 임팩트 프레임에서 칼 평면 유도**(§11-3) | "마지막 클립의 임팩트"를 봐야 한다 |
| `AnimationClipTrimmerWindow.cs:382` | 저작. `slotPath = "playerAttack"` **문자열 경로**로 `SerializedProperty` 접근 | 리스트가 되면 `playerAttackSequence.Array.data[i]` — 인덱스 선택 UI 필요 |

간접 소비자:

| 위치 | 의존하는 것 |
|---|---|
| `HitStopDirector` | `OnExtraImpact(float)` 구독 + `HitStopDuration`을 플레이어가 pull |
| `PlayerCombatMover` | `DuelCurveTime` · `PlayingImpactAlignTime` (§11-9) |
| `DodgeDirector` | `OnIdleWindow(start, end)` — 끝이 `pendingScheduleStart`다(§11-8) |
| `EnemyView` | `ClipAlignment`를 **공유**하지만 자기 슬롯은 여전히 하나 |

> **적 쪽은 안 건드려도 된다** — 적은 `pendingAttack` 슬롯 하나에 자기 임팩트를 같은 `impactAlignTime`에 정렬할 뿐이라(§6), 플레이어가 그 시각에 마지막 클립의 임팩트를 가져다 놓기만 하면 계약이 그대로 성립한다.

---

## 4. 정렬 모델 후보 — 무엇을 무엇에 맞출 것인가

이것이 이 설계의 **유일한 진짜 결정**이다. 재생 자체는 `PlaySlot`을 이어 부르면 되지만, **N개의 임팩트를 무엇에 정렬하느냐**에 따라 요구되는 코드가 통째로 달라진다.

### 모델 A — 마지막 클립만 정렬, 앞 클립들은 역방향 back-schedule

```
시작 = impactAlign − (A재생시간 + B재생시간) − C의임팩트스팬/speed − 정지예산
       ─────────── A ──────────►─── B ───►─── C(임팩트) ───► 마무리
                                              ▲ Deadline + ImpactOffset
```

- **새 시계·새 기준점이 0개다.** `ClipAlignment` 그대로, `ImpactTime`은 **마지막 원소에만** 정렬 의미가 있다.
- `extraImpactTimes`가 클립 하나 안에서 하던 일을 **클립 경계로 확장**한 것 — 저작 모델이 이미 있는 것과 같다.
- 앞 클립들의 임팩트도 히트스톱을 걸고 싶으면 그 원소의 `ImpactTime`을 `OnExtraImpact`로 흘리면 된다(**기존 이벤트 재사용**).
- **비용**: 창 예산(§5). 앞 클립 길이가 통째로 시작 시각을 앞으로 민다.

### 모델 B — 원소마다 기준점을 든다 (`Node[i]` / `LastNode` / `Impact` ± offset)

- `PatternEffectCue.EffectTiming`과 **같은 저작 모델**을 클립에 재사용. 표현력이 가장 크다("첫 노드에 A, 3번 노드에 B, 임팩트에 C").
- **⚠ 그 대가로 `JudgeTargetInfo`에 `NodeTimes`가 없다.** 지금은 `PatternQueuedInfo`만 든다. 추가는 싸다 — `PatternHandler.cs:371`에서 `target.BuildNodeTimes()`를 한 번 더 부르면 된다(이미 `ActivePattern.BuildNodeTimes()`가 있고 큐 투입 시 쓰고 있다).
- **⚠ `NodeTimes`는 '예정'이지 '실제'가 아니다**(§7-4). 늦게 눌러도 안 밀린다 → 클립이 입력보다 먼저 시작하는 그림이 난다.
- **⚠ 원소끼리 겹치거나 구멍이 생기는 상태를 표현할 수 있다.** 검증(`OnValidate`)과 툴 경고가 필수가 된다.

### 모델 C — 균등 분할 (트림 총합을 창에 맞춰 늘리고 줄임)

- 저작 필드 0개. 대신 **모션 길이를 저작자가 통제할 수 없다** — 창이 0.5초인 엔트리에서 3클립이면 각 0.17초, 동작이 아니라 깜빡임이 된다.
- 기각 후보로만 기록한다.

---

## 5. ⚠ 실측 — 창 예산이 이 기능의 실질적 상한이다

`Dreamer_Lv10` (90엔트리) 측정. **가용 창 = `impact − FirstNodeTime`** (시작 시각이 `FirstNodeTime`으로 하한 클램프되므로).

| | min | p25 | **p50** | p75 | max |
|---|---|---|---|---|---|
| **가용 창(초)** | 0.10 | 0.50 | **0.90** | 1.30 | **1.70** |

| 창 ≥ | 엔트리 비율 |
|---|---|
| 0.8초 | 61% |
| 1.2초 | 39% |
| 1.6초 | 19% |
| **2.0초** | **0%** |

현재 저작된 `playerAttack`의 **와인드업**(임팩트까지의 재생 시간, 17개 템플릿):

```
0.18 0.19 0.19 0.22 0.23 0.25 0.26 0.26 0.26 0.28 0.43 0.43 0.50 0.54 0.65 0.67 1.25
                              p50 = 0.26초                              max = 1.25초
```

### 이 숫자들이 말하는 것

1. **3클립 체인의 총 재생 시간은 p50 기준 0.90초 안에 들어가야 한다.** 클립당 평균 **0.30초**다. 지금 와인드업 p50이 0.26초이니 "짧은 모션 3개"는 성립하지만, **§11-4가 견제 클립에 권장한 0.8초짜리를 3개 잇는 것은 이 채보에서 0% 가능하다**.
2. `maxAttackSpeed = 2.5` 클램프가 뒤를 받치지만, 2.5배로 압축된 모션은 §6의 경고 대상이다(패링에서는 즉시 눈에 보인다). **배속으로 때우는 것은 해결이 아니다.**
3. **클램프를 `FirstNodeTime` → 패턴 큐 투입 시각(`StartTime`)으로 완화하면 창이 크게 열린다**:

   | | min | **p50** | max | ≥2.0초 |
   |---|---|---|---|---|
   | `FirstNodeTime` 하한(현재) | 0.10 | **0.90** | 1.70 | 0% |
   | `StartTime` 하한(= 첫 노드 −0.5초) | 0.60 | **1.40** | 2.20 | 19% |

   ⚠ 이건 공짜가 아니다. **"첫 노드보다 먼저 휘두를 수 없다"는 규율을 깨는 것**이고, `RaiseIdleWindow`의 창(§11-8 기습)이 그만큼 줄어들며(끝이 `pendingScheduleStart`다), 포커스 링이 아직 수축 중인데 캐릭터가 이미 베고 있는 그림이 된다. **Plan에서 결정할 사항이지 자명한 개선이 아니다.**
4. **패턴 간 여유는 거의 없다** — `impact → 다음 FirstNode` 간격이 min/p50 모두 **0.30초**다. 즉 마지막 클립의 임팩트 이후 마무리 동작에 쓸 수 있는 시간이 사실상 없고, **체인을 임팩트 뒤로 늘리는 방향은 막혀 있다**.

> **결론**: 이 기능은 "긴 모션 여러 개"가 아니라 **"짧은 모션 여러 개"**로만 성립한다. 저작 툴이 **창 예산을 실시간으로 보여 주지 않으면** 저작자가 만든 대부분이 조용히 배속 압축된다(§7-3-1의 `정지 예산 표시`가 정확히 같은 문제를 같은 방식으로 이미 풀었다).

---

## 6. 그 외 발견한 함정

1. **크로스페이드가 체인 길이에 포함되지 않는다.** `attackCrossFadeDuration = 0.15초`. 3클립이면 전환이 2번이라 0.30초가 두 모션에 겹쳐 흐른다 — 시간을 '먹지'는 않지만 **각 클립의 앞 0.15초는 이전 모션과 섞여 보인다.** 클립이 0.30초짜리면 그 절반이다. 체인 전용으로 더 짧은 페이드가 필요할 수 있다.
2. **레이어 웨이트 블렌드를 클립 경계마다 재시작하면 안 된다.** `PlaySlot`이 `blendInStartTime`을 리셋하고 `blendOutLatched`를 내린다 — 매 타 웨이트가 0 근처에서 다시 올라오면 깜빡인다. §6이 "연계 O·간격 부족" 경로에서 웨이트 1을 **유지**하기로 한 것과 정확히 같은 이유다.
3. **`hitStopped = false`**를 `PlaySlot`이 무조건 실행한다. 체인 전환이 정지 창 안에서 일어나면 **정지가 통째로 삼켜진다**. §7-3-1의 예산·따라잡기가 자기 수정하므로 정렬은 살아남지만 **화면에서는 멈춤이 사라진다**.
4. **첫 미스 취소 경로**가 체인 전체를 죽여야 한다. 지금은 `hasPending = false` 하나로 끝나지만(예약이 하나라서), 체인은 "다음 원소 예약"이 재생 중에도 살아 있다.
5. **`OnSwingBegan`/`OnSwingEnded`는 원소마다 나야 하는가 체인당 한 번인가.** 지금 구독자는 없지만(§6) `swingActive`가 `ApplyHitStop`의 가드로 쓰인다 — **원소 사이에서 false가 되면 그 순간 히트스톱이 통째로 무시된다.**
6. **`MeshSliceBakerWindow`의 칼 평면 유도가 조용히 틀린 답을 낸다.** 지금은 `playerAttack.ImpactTime` 프레임에서 평면을 유도하는데, 체인이면 **마지막 원소**를 봐야 한다. 잘못 보면 §11-3대로 **에러 없이 엉뚱한 각도의 세트가 재사용된다**(굽기가 실패하지 않는다는 것이 그 절의 경고다).
7. **`Attacker.Enemy`(패링)에서 체인이 성립하는가.** 적은 `EnemyAttack` 클립 하나로 한 번 휘두른다 — 플레이어가 3번 받아치는 그림은 서사적으로 성립하지 않는다. **역할별로 허용 여부가 갈릴 수 있다.**
8. **기존 데이터 회귀**: `playerAttack`은 17개 템플릿에 이미 저작돼 있다. 리스트로 바꾸면 **마이그레이션이 필요하다**(`ClipAlignment.EditorAssign`이 이미 같은 목적의 전례를 남겼다 — 구 `SuccessAnimationClip` → `playerAttack` 이관).

---

## 7. 열린 질문 (Plan 전에 답이 필요하다)

1. **정렬 모델 A/B/C 중 무엇인가?** — 이것이 나머지를 전부 결정한다.
2. **원소마다 히트스톱을 거는가?** 건다면 기존 `OnExtraImpact` 재사용으로 충분하다(코드 추가 거의 0).
3. **시작 시각 하한을 `FirstNodeTime`에서 `StartTime`으로 완화하는가?** §5-3의 트레이드오프. 완화 없이는 3클립 체인이 대부분의 엔트리에서 압축된다.
4. **`Attacker.Enemy`에도 허용하는가?**
5. **저작 툴**: `AnimationClipTrimmerWindow`에 원소 선택 탭을 붙이는가, 아니면 `PatternEffectWindow`처럼 **체인 전용 타임라인**을 새로 만드는가? (전자가 훨씬 싸지만 여러 원소의 시간 관계를 한 화면에서 못 본다 — §5의 창 예산 표시가 필요하다면 후자가 맞다.)
6. **거리 커브(§11-9)의 t 축은 무엇을 원점으로 삼는가?** 마지막 임팩트로 두면 앞 클립 구간이 전부 음수 t다 — 커브 저작이 지금보다 훨씬 길어진다.

---

## 참고 파일

| 파일 | 왜 |
|---|---|
| `Assets/02. Scripts/Character/CharacterActionPlayer.cs` | 유일한 런타임 재생 지점. §2의 상태 전부 |
| `Assets/02. Scripts/Pattern/Core/ClipAlignment.cs` | 정렬 계산의 공유 지점(플레이어·적) |
| `Assets/02. Scripts/Pattern/Pattern.cs` | 슬롯 소유자 · `OnValidate` 검증 |
| `Assets/02. Scripts/Pattern/JudgeTargetInfo.cs` | ⚠ `NodeTimes`가 **없다**(모델 B의 전제) |
| `Assets/02. Scripts/Pattern/PatternQueuedInfo.cs` | `NodeTimes`를 드는 쪽. `ActivePattern.BuildNodeTimes()` |
| `Assets/02. Scripts/Pattern/Core/PatternEffectCue.cs` | 모델 B가 재사용할 저작 모델(`EffectTiming.ResolveTime`) |
| `Assets/02. Scripts/HitStop/HitStopDirector.cs` | `OnExtraImpact` 예약 리스트 · `HitStopDuration` pull |
| `Assets/02. Scripts/Character/PlayerCombatMover.cs` | `DuelCurveTime` · `PlayingImpactAlignTime` 소비자 |
| `Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs` | 저작. `slotPath` 문자열 경로 접근 |
| `Assets/02. Scripts/Slice/Editor/MeshSliceBakerWindow.cs` | 칼 평면 유도(§6-6의 함정) |
