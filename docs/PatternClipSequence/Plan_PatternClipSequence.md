# Plan — 한 패턴 안의 클립 순차 재생 (PatternClipSequence)

> 근거: `docs/PatternClipSequence/Research_PatternClipSequence.md`
> 확정 사항: **모델 A(마지막 클립만 임팩트 정렬)** · **창이 모자라면 전 원소를 같은 비율로 배속** · 창 예산은 채보 길이로 해결한다(설계 제약으로 두지 않는다).

---

## 0. 설계 결정 (구현 전에 못 박는다)

### 0-1. 데이터 — `playerAttack`은 그대로 두고 **앞에 붙는 리스트**를 추가한다

```
playerLeadInClips[0] ─► [1] ─► … ─► [N-1] ─► playerAttack(또는 playerParry)
                                               ▲ ImpactTime → Deadline + ImpactOffset
```

- **마지막 클립 = 기존 슬롯**이다. 리스트로 통째로 갈아엎지 않는다.
- 그래서 **마이그레이션이 0**이고, 기존 17개 템플릿은 리스트가 비어 있어 **예전 경로 그대로**다.
- 그리고 §Research 6-6의 함정이 **원천 소멸한다** — `MeshSliceBakerWindow`의 칼 평면 유도는 여전히 `playerAttack.ImpactTime`을 보면 되고, 그게 정확히 마지막 클립이다. **에디터 툴 3곳 중 2곳이 한 줄도 안 바뀐다.**

**리스트는 하나뿐이다**(`playerParry`용을 따로 두지 않는다). CLAUDE.md §3 "**한 패턴은 한 역할만 갖는다**"가 이미 참이고 `WarnUnusedSlots`가 그것을 지키고 있으므로, 리드인은 그 패턴의 **활성 슬롯 앞에** 붙는 것으로 정의된다. 역할별 금지 코드를 쓰는 것보다 싸다.

### 0-2. 리드인 원소의 `ImpactTime`은 **정렬 의미가 없다**

정렬 앵커는 언제나 마지막 클립 하나다(§6). 리드인 원소는 **트림 전체**(`ResolvedDuration`)가 재생 길이이고, 그 안에서 칼질에 히트스톱을 걸고 싶으면 `extraImpactTimes`에 찍는다 — **기존 저작 모델 그대로, 새 필드 0개**.
→ 리드인 원소에 `ImpactTime`이 찍혀 있으면 `OnValidate`가 경고한다(오해의 유일한 진입점이라).

### 0-3. 배속은 **시퀀스 전체에 걸리는 비율 `k` 하나**다

```
authoredSpan = Σ(리드인 i의 dur_i / speed_i) + (마지막의 impactSpan / speed_last)
k            = max(authoredSpan / (impactAlign − freeze − now), 1)
원소 i의 애니메이터 배속 = speed_i × k
```

- **`k`는 1 미만으로 내려가지 않는다** — 저작 배속보다 느리게 재생하지 않는 기존 규칙(`Mathf.Clamp(needed, baseSpeed, cap)`의 하한)과 같다.
- **리스트가 비면(= 원소 1개) 기존 경로를 그대로 탄다.** `k` 계산식이 현재 식과 대수적으로 동일하므로 **회귀가 원리적으로 없다**(Step 8이 이것을 테스트로 고정한다).
- **⚠ 상한**: 원소가 2개 이상이면 `maxAttackSpeed` 상한을 **걸지 않는다**(= "전부 재생된다"는 요구가 상한과 양립하지 않는다). 대신 `k > maxAttackSpeed`면 경고를 찍는다. 원소가 1개면 **기존 상한을 유지**한다 — 17개 템플릿의 실패 양상을 바꾸지 않기 위해서다.

### 0-4. 재생 헤드를 **저작 초(authored seconds) 하나로 일원화**한다

§Research 2의 연쇄 붕괴는 `clipConsumed`가 "이 클립에서 소비한 클립 초"라서 생긴다. 시퀀스에는 **원소를 가로지르는 시계**가 필요하다.

```
headAuthored += (Time.time − segmentStartTime) × k        // 정지 중에는 0
impactAuthored = authoredSpan                              // 마지막 임팩트까지의 저작 초

DuelCurveTime  = headAuthored − impactAuthored             // §11-9 (playingBaseSpeed 나눗셈이 사라진다)
IsBeforeImpact = headAuthored < impactAuthored
캐치업          = k ← (impactAuthored − headAuthored) / 남은 실시간
```

**이것은 새 시계가 아니라 기존 식의 일반화다.** 원소 1개일 때 `headAuthored ≡ clipConsumed / playingBaseSpeed`이므로 세 소비자 전부 지금과 같은 값을 본다.
**`clipConsumed`/`playSpeed`는 그대로 남는다** — `EmitNextExtraImpact`가 *현재 원소의* 클립 초를 쓰기 때문이다(단위가 다르므로 합칠 수 없다. 두 시계인 것이 정직하다).

### 0-5. 원소 경계에서 **하지 말아야 할 것들**

`PlaySlot`은 호출마다 재생 상태를 전부 리셋한다. 원소 전환에서는 아래를 **건너뛴다**(§Research 2·6):

| 건너뛸 것 | 안 건너뛰면 |
|---|---|
| `actionEndTime` / `recoveryEndTime` | **시퀀스가 1타 만에 끝난다**(중간 원소의 트림 끝에서 복귀가 열린다) |
| `blendInStartTime` / `blendOutLatched` | 매 타 레이어 웨이트가 깜빡인다 |
| `releaseTriggered` / `releaseEndTime` | 중간에서 Release가 샌다(과거 버그, §6) |
| `hitStopped = false` | **원소 전환이 진행 중인 히트스톱을 삼킨다** |
| `RaiseSwingBegan()` (재발행) | `swingActive`가 잠깐 false → 그 순간 `ApplyHitStop`이 통째로 무시된다 |
| `playingImpactAlignTime = NaN` | `PlayerCombatMover`가 거리 커브 구동을 놓는다(§11-9) |

`useSlotA` 핑퐁만은 **그대로 둔다** — A→B→A로 이어지는 것이 정확히 원하는 동작이다.

### 0-6. 크로스페이드는 기존 값을 그대로 쓴다

`attackCrossFadeDuration`(0.15초)을 원소 전환에도 쓴다. 짧은 클립을 잇게 되면 그때 전용 값을 뺀다 — 지금 넣으면 근거 없는 노브가 하나 는다.

---

## 1. 구현 단계

### - [x] Step 1 — 순수 계산 `Pattern/Core/ClipSequence.cs` (asmdef)

`DuelGap`과 같은 자리·같은 규율. **런타임과 저작 툴이 같은 함수를 부른다**.

```csharp
public static class ClipSequence
{
    // 리드인 전체 + 마지막의 임팩트까지 = 저작 초
    public static float AuthoredSpan(IReadOnlyList<ClipAlignment> leadIn, ClipAlignment final);

    // 리드인 원소 i의 저작 초(트림 전체)
    public static float AuthoredDuration(ClipAlignment element);

    // 창에 맞추는 균일 비율. 1 미만으로 내려가지 않는다.
    public static float ResolveRatio(float authoredSpan, float availableTime);

    // 원소 i가 시작하는 시각(시퀀스 시작 기준 실시간 오프셋)
    public static float ElementStartOffset(IReadOnlyList<ClipAlignment> leadIn, int index, float ratio);
}
```

- `IsUsable`이 false인 원소는 **전부 걸러낸 뒤** 계산한다(빈 슬롯이 리스트에 섞이는 것은 정상 상태다).
- 여기에는 `UnityEngine.Object` 의존이 없다 → Step 8의 테스트가 그대로 붙는다.

### - [x] Step 2 — `Pattern`에 리드인 리스트 추가

- `[SerializeField] private List<ClipAlignment> playerLeadInClips = new();`
- `public IReadOnlyList<ClipAlignment> PlayerLeadInClips => playerLeadInClips;`
- `public bool HasLeadInClips` — 쓸 수 있는 원소가 하나라도 있는가(`ClipAlignment.IsUsable`).
- `OnValidate`:
  - 각 원소에 `ValidateImpactTime(this, $"LeadIn[{i}]")` (트림 검증은 그대로 필요하다).
  - **원소에 `ImpactTime`이 찍혀 있으면 경고**(§0-2 — 정렬 의미가 없다).
  - **리드인이 있는데 활성 슬롯(`playerAttack`/`playerParry`)이 비어 있으면 경고** — 정렬 앵커가 없으면 시퀀스 전체가 무연출이다.

### - [x] Step 3 — 예약: 시퀀스 총 길이로 시작 시각을 역산

`CharacterActionPlayer.SchedulePendingSuccess`:

```
pendingLeadIn     = 쓸 수 있는 원소만 걸러낸 배열
pendingFreeze     = (리드인 원소들의 extras 수 + 마지막의 extras 수) × HitStopDuration   ← 합산으로 확장
pendingAuthored   = ClipSequence.AuthoredSpan(pendingLeadIn, alignment)
pendingScheduleStart = max(impactAlign − pendingFreeze − pendingAuthored, info.FirstNodeTime)
```

- 현재 식의 `impactSpan/baseSpeed` 자리에 `pendingAuthored`가 들어갈 뿐이다. **리스트가 비면 값이 같다.**
- `RaiseIdleWindow`는 손대지 않는다 — 창의 끝이 `pendingScheduleStart`라 **자동으로 줄어드는 것이 맞다**(§11-8).

### - [x] Step 4 — 재생: 비율 `k` 확정 + 원소 진행

`TryStartPendingSuccess`:

```
freeze = 창이 예산을 못 감당하면 0으로 포기(기존 로직 그대로)
k = ClipSequence.ResolveRatio(pendingAuthored, impactAlign − freeze − Time.time)
   원소 1개  → 기존대로 maxAttackSpeed 상한 적용
   원소 2개+ → 상한 없음. k > maxAttackSpeed면 경고 1줄
sequenceRatio = k
headAuthored = 0
impactAuthored = pendingAuthored
→ 첫 원소를 PlaySlot(clip_0, off_0, dur_0, speed_0 × k, isSwing:true, continuesSequence:false)
```

`Update()`에 원소 전환을 넣는다(정지 가드 **뒤**, `TryStartPendingSuccess` 옆):

```
if (시퀀스 진행 중 && Time.time >= elementEndTime)
    다음 원소 → PlaySlot(..., speed_i × sequenceRatio, isSwing:true, continuesSequence:true)
    마지막 원소면 정렬 대상 필드(playingImpactSpan 등)를 채운다
```

`PlaySlot`에 `bool continuesSequence = false` 파라미터를 추가하고 §0-5의 여섯 가지를 그 플래그로 건너뛴다.
`actionEndTime`/`recoveryEndTime`은 **시퀀스 시작 시 한 번만** 세운다(= 마지막 원소의 트림 끝).

### - [x] Step 5 — 헤드 일원화 (`headAuthored`)

- `headAuthored += (Time.time − segmentStartTime) × sequenceRatio` — `clipConsumed`를 갱신하는 **모든 지점에서 나란히** 갱신한다(`Update`의 트림 끝 래치 · `ApplyHitStop` · 원소 전환).
- `DuelCurveTime` → `headAuthored − impactAuthored`. **`playingBaseSpeed` 나눗셈을 제거**한다(그 필드는 더 이상 시간 단위를 들지 않는다).
- `IsBeforeImpact()` → `headAuthored < impactAuthored`.
- `ReleaseHitStop`의 캐치업 → **`k`를 재계산**하고 현재 원소의 애니메이터 배속을 `speed_i × k`로 다시 건다. 이후 원소도 새 `k`를 쓴다(그래서 시퀀스 전체가 자기 수정된다).
- `PlayerCombatMover`는 **한 줄도 안 고친다** — `DuelCurveTime`/`PlayingImpactAlignTime`의 의미가 보존되기 때문이다.

### - [x] Step 6 — 추가 히트스톱을 원소 경계 너머로 잇는다

- `EmitNextExtraImpact`는 **현재 원소 기준 그대로** 둔다(단위가 그 원소의 클립 초라 정확하다).
- 원소가 바뀌면 `playingExtraSpans = 새 원소의 ResolvedExtraImpactSpans`, `extraCursor = 0`, 즉시 `EmitNextExtraImpact()`.
- `HitStopDirector`는 **한 줄도 안 고친다** — 이미 `OnExtraImpact`를 리스트로 예약하고 `isMainImpact:false`로 절단을 안 민다(§7-3-1).

### - [x] Step 7 — 인터럽트·경계 정리

- `CancelPendingSuccess`(첫 미스) / 피격 재생 / 다음 패턴 시작: **시퀀스 상태를 전부 내린다**(`sequenceActive`, 커서, `pendingLeadIn`).
- 피격 클립은 `PlaySlot(..., isSwing:false, continuesSequence:false)`이므로 자연히 시퀀스를 끊는다 — 그 경로에서 커서만 내리면 된다.
- `OnSwingBegan`은 시퀀스 시작에 1회, `OnSwingEnded`는 기존 `actionEndTime` 래치에서 1회(§0-5).

### - [x] Step 8 — 유닛테스트 `Pattern/Tests/ClipSequenceTests.cs`

`DuelGapTests`와 같은 자리. **최소한 아래 넷**:

1. **원소 0개일 때 `AuthoredSpan` == `impactSpan / speed`** — 기존 경로와 대수적으로 같다는 것을 고정한다(이 기능의 회귀 0 보장 전부가 여기 걸려 있다).
2. 창이 충분하면 `ResolveRatio` == 1 (저작 배속보다 빨라지지 않는다).
3. 창이 절반이면 `ResolveRatio` == 2, 그리고 **모든 원소가 같은 배율을 받는다**.
4. `IsUsable`이 false인 원소는 길이·오프셋 계산에서 빠진다.

### - [x] Step 9 — 저작 툴 `AnimationClipTrimmerWindow`

`EnemyAuxSlot` 팝업이 이미 선례다(적 보조 슬롯 셋을 한 배우 자리에서 갈아 끼운다). **같은 방식을 플레이어 배우에 적용한다.**

- 플레이어 슬롯 팝업: `마지막(playerAttack/playerParry)` · `리드인 0` · `리드인 1` · … → `player.slotPath`를 `playerLeadInClips.Array.data[i]`로 갈아 끼운다.
- 원소 추가/삭제/순서 이동 버튼.
- **총 저작 시간 표시** — `ClipSequence.AuthoredSpan`(Step 1의 그 함수)을 그대로 불러 `총 N.NN초`를 찍는다. 채보 창을 넘어서는지는 저작자가 채보를 늘려 해결하므로 **경고가 아니라 숫자만** 보여 준다.
- ⚠ 타임라인 `t`축(임팩트 기준 상대초)에서 리드인 원소는 **음수 구간**에 산다. 이번 단계에서는 선택된 원소 하나만 그 위치에 그린다(전 원소 동시 오버레이는 범위 밖 — 필요해지면 별건으로 뺀다).

### - [x] Step 10 — 문서

- `CLAUDE.md`에 **§6-1 "패턴 안의 클립 순차 재생"** 절 추가(§7-3-1 옆). 담을 것: 마지막 클립만 정렬 · 균일 비율 `k` · 리드인의 `ImpactTime`은 무의미 · 원소 경계에서 리셋하면 안 되는 여섯.
- `docs/PatternClipSequence/`에 최종 결정 반영.

---

## 2. 안 하는 것 (범위 밖임을 명시한다)

| 안 함 | 이유 |
|---|---|
| 원소별 `Node[i]` 기준점(모델 B) | 마지막 클립 정렬로 확정됐다. `JudgeTargetInfo`에 `NodeTimes`를 넣지 않는다 |
| 적(`EnemyAttack`/`EnemyDeath`) 시퀀스 | 적은 같은 `impactAlignTime`에 자기 클립 하나를 정렬할 뿐이라 계약이 그대로 성립한다(§6) |
| 원소 전용 크로스페이드 값 | §0-6 |
| 창 예산 경고/차단 | 채보 길이로 해결한다(사용자 결정) — 툴은 숫자만 보여 준다 |
| 마이그레이션 스크립트 | §0-1이 필요 없게 만든다 |

---

## 3. 회귀 위험 지점 (구현 중 계속 확인)

1. **`playingBaseSpeed`의 소비자가 정말 `DuelCurveTime` 하나뿐인가** — Step 5에서 나눗셈을 걷어내기 전에 `grep`으로 재확인.
2. **`PlaySlot`의 `continuesSequence` 분기 누락** — §0-5의 여섯 중 하나만 빠져도 증상이 다 다르다(1타 종료 / 깜빡임 / 정지 삼킴 / Release 누출).
3. **`k` 재계산 시 이후 원소가 옛 `k`를 쓰는 것** — `sequenceRatio` 필드 하나만 보게 하고 지역 변수로 복사하지 않는다.
4. **원소 1개 경로가 조금이라도 달라지는 것** — Step 8-1 테스트가 1차 방어선, 17개 템플릿 실제 재생이 2차.

---

## 4. 구현 후 기록 (Plan 대비 달라진 것)

1. **헤드 환산율이 `k`가 아니라 `playSpeed / playingBaseSpeed`다.** Plan §0-4는 `headAuthored += 경과 × k`였는데, 그러면 **트림 끝에서 배속이 1로 복원된 뒤의 구간**이 어긋난다(그 구간의 환산율은 `1 / 저작배속`이다). 지금 식은 재생 중에는 `k`와 같은 값이고 복원 뒤에는 자동으로 갈린다 — 거리 커브의 임팩트 **이후** 키가 여기서 살아난다.
2. **`playingImpactSpan` 필드를 삭제했다.** 세 소비자가 전부 `impactAuthored`/`headAuthored`로 옮겨 가 소비자가 0이 됐다.
3. **`inLeadIn` 게이트를 `Update`에 넣었다.** Plan에는 "중간 원소의 트림 끝에서 복귀가 열리면 안 된다"만 있었는데, 리드인 동안 `actionEndTime`이 추정값이라 값만으로는 못 막는다. 게이트가 그 부류를 통째로 소멸시킨다.
4. **`elementEndTime`을 남은 클립 내용에서 매번 다시 잡는다**(`RefreshElementEnd`). 시작 시각에서 한 번 재면 히트스톱·따라잡기로 배속이 바뀐 뒤를 못 따라간다.
5. **툴의 `CurveDriveStart`가 시퀀스 전체 시작을 쓴다.** 앵커 클립만 보고 재면 구동 구간을 실제보다 짧게 잡아 **멀쩡한 커브 키에 경고가 뜬다**.
6. **Step 1의 `ElementStartOffset`을 `AuthoredEndOffset`으로 바꿨다** — 툴이 실제로 필요한 것은 "원소의 트림 끝이 t 축 어디에 오는가"였다.
7. Step 9의 "전 원소 동시 오버레이"는 예정대로 범위 밖이다. 선택된 원소 하나만 제자리에 그린다.

**검증**: `Pattern.Tests` EditMode **65/65 통과**(신규 `ClipSequenceTests` 7건 포함), 컴파일 에러 0.
