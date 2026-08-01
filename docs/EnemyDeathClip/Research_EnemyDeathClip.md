# Research — EnemyDeathClip (사망 클립 재생 + 클립 끝에서 절단)

## 1. 요구

1. **적 사망 클립의 임팩트 프레임을 플레이어 공격의 임팩트와 맞물리게** 재생한다(배속 역산 포함).
2. **절단(시체 교체 + 폭발)은 클립 끝에서** 일어난다. 지금은 임팩트 순간이다.

## 2. 지금 상태 — `enemyDeath`는 런타임에 재생되지 않는다

`Pattern.EnemyDeath`(`Assets/02. Scripts/Pattern/Pattern.cs:65`) 소비처를 전수 조사한 결과:

| 슬롯 | 런타임 재생 | 소비처 |
|---|---|---|
| `playerAttack` | O | `CharacterActionPlayer` |
| `enemyAttack` | O | `EnemyDirector:594` (`Attacker.Enemy`일 때만) |
| `playerParry` | O | `CharacterActionPlayer:547` (`Attacker.Enemy`일 때만) |
| **`enemyDeath`** | **X** | `MeshSliceBakerWindow`만 — **에디터 전용** |

에셋 주석도 그렇게 적혀 있다:

> 적이 베어지는 클립. **슬라이서가 이 임팩트 프레임 포즈로 굽는다(저작 보조).**
> 런타임 정합성 요구는 아니다 — 조각이 스키닝을 유지해 어떤 포즈든 따라가기 때문.

휴머노이드 절단이 스키닝 보존으로 바뀌면서 이 클립이 **런타임 정합성 요구에서 저작 보조로 강등**됐고, 그대로 남았다.

현재 채보(`Dreamer_Lv10`)는 **87/87 `killOnSuccess`, `Attacker.Enemy` 0개**다. 즉 지금 실제로 재생되는 적 클립은 **하나도 없다.**

## 3. 지금의 처치 흐름

```
EnemyDirector.KillOpponent(opponent, set, impactTime)     ← 패턴 완료 시점(마지막 노드 입력, L)
  ├ opponent.MarkDying()                                   Phase.Dying
  ├ pendingKills.Add({opponent, set, impactTime})
  ├ OnEnemyKilled?.Invoke(opponent)                        ← 카메라 쉐이크 큐
  ├ currentOpponent = null / onDeck 정리
  ├ SpawnIntoRing()                                        링 보충
  └ PromoteOpponent()                                      다음 상대로 즉시 전환

TickPendingKills()  →  now >= impactTime  →  ExecuteKill()
  ├ 스킨드: SwapToCorpse(opponent, set)   시체 프리팹 대여 → AdoptPose → Burst → 산 적 풀 반납
  └ 정적:   opponent.Kill(set, pieces, …)
```

**확정(L)과 연출(impactTime)이 이미 분리돼 있다**(`PendingKill`). 이번 변경은 그 분리를 **한 칸 더 미는 것**이다 — 연출 시각을 `impactTime`에서 `클립 끝`으로.

## 4. 재생 기반은 이미 있다

`EnemyView`가 `ClipAlignment` 재생 machinery를 **완비**하고 있다:

- `AnimatorOverrideController`(`:125`, `Awake`에서 교체) + `attackPlaceholder` 슬롯
- `attackStateName`(`"Attack"`), `attackSpeedParam`(`"AttackSpeed"`)
- `PlayAttack(alignment, speed)`(`:558`) — 슬롯 덮어쓰기 → `SetFloat` → `CrossFadeInFixedTime`
  - **`fixedTimeOffset`은 '스테이트 재생 초'라 speed가 곱해진다** → 클립 초를 speed로 나눠 넘긴다(`:566`). 새 경로도 같은 함정을 밟는다.
- `TryStartAttack()`(`:541`) — `pendingScheduleStart`에 도달하면 `ResolvePlaySpeed`로 배속을 **재계산**해 임팩트 정렬

`EnemyAnimator` Base Layer 스테이트: `Idle` / `Run` / `Attack`(`AttackSlot_Placeholder`) / `KnockBack` / `Evade` / `Death`(`Dead`) / — **전이는 없다**(코드 주도).

→ `Death` 스테이트가 **고정 클립 `Dead`**를 물고 있다. 패턴별 사망 클립을 재생하려면 `Attack`처럼 **오버라이드 슬롯**이 필요하다.

## 5. `ClipAlignment` 정렬 API (`Assets/02. Scripts/Pattern/Core/ClipAlignment.cs`)

```csharp
ResolvedImpactSpan                                 // 트림 시작 → 임팩트까지(클립 초). 미오서링이면 트림 끝
ResolveScheduleStart(impactAlignTime, earliest)    // max(impact − span/speed, earliest)
ResolvePlaySpeed(now, impactAlignTime, maxSpeed, out clamped)
                                                   // needed = span / (impact − now), [Speed, maxSpeed]로 클램프
ResolvedDuration                                   // 트림 길이(클립 초)
```

**플레이어와 적이 같은 타입을 쓰는 것이 이 타입의 존재 이유**다(주석). 사망 클립도 같은 규칙을 쓰면 "플레이어 칼이 지나가는 순간 = 적이 맞는 순간"이 구조적으로 성립한다.

## 6. 제약

### 6-1. 처치 확정은 마지막 노드 입력(L)보다 이를 수 없다

임팩트는 `Deadline + impactOffset` = `L + goodWindow(0.1) + offset`.
→ **사망 클립의 임팩트 이전 구간에 주어지는 실시간은 약 0.1초 + offset뿐이다.**

`ResolvedImpactSpan`이 그보다 길면 `ResolvePlaySpeed`가 배속을 올리고, `maxAttackSpeed`(2.5) 상한에 걸리면 `clamped = true` — **정렬이 깨진다.**

플레이어 공격 클립도 같은 제약을 받는다(같은 시점에 시작해 같은 시각에 정렬). **새로운 제약이 아니라 공유 제약**이다.

### 6-2. 죽은 적이 결투 위치를 계속 점유한다

지금은 임팩트에 사라진다. 클립 끝까지 남으면 **다음 상대가 접근할 자리에 시체가 서 있다.** 승격은 확정 시점(L)에 즉시 일어나므로 다음 적은 곧바로 같은 지점으로 온다.

### 6-3. 굽기 포즈는 임팩트 프레임이다

`MeshSliceBakerWindow.DrawPoseInfo`(`:338`)가 `death.ImpactTime`의 포즈로 굽는다. 절단 순간이 **클립 끝**으로 바뀌면 굽기 포즈와 실제 폭발 포즈가 달라진다.

정합성 요구는 아니지만(스키닝 보존) *"죽는 자세와 가까울수록 관절 뒤틀림이 줄어든다"*.

### 6-4. 산 적 인스턴스가 더 오래 살아 있다

`SwapToCorpse`에서야 풀로 반납된다(`:753`). 클립 길이만큼 반납이 늦어지고, 그동안 다음 적도 활성이다.

### 6-5. 정리 경로

`HandleAllCleared`가 `pendingKills`를 비운다 — *"안 그러면 곡이 끝난 뒤에 적이 갈라진다"*. 새 상태도 같은 곳에서 정리돼야 한다.

## 6-6. 두 클립의 배속이 서로 다르다 — 문서화되지 않은 규칙

플레이어 공격과 적 사망은 **각자 배속을 갖는다**:

- 플레이어: `ClipAlignment.Speed`(저작값) + `CharacterActionPlayer`의 겹침 방지 압축(`maxAttackSpeed` 2.5)
- 적: `ClipAlignment.Speed`(저작값) + `ResolvePlaySpeed`의 정렬 압축

두 배속은 **원리적으로 같아질 이유가 없다.** 클립 길이가 다르고, 압축을 유발하는 제약도 다르다(플레이어는 다음 패턴까지의 여유, 적은 확정~임팩트 간격).

따라서 **클립 시작이나 끝을 맞추는 정렬은 성립하지 않는다.** 공유할 수 있는 시각은 **임팩트 하나뿐**이다.

`ClipAlignment`가 이미 그렇게 설계돼 있다 — `ResolveScheduleStart`/`ResolvePlaySpeed`가 전부 `impactAlignTime`을 인자로 받는다. **코드는 맞는데 문서가 없다:**

- `CLAUDE.md:179`(§6)은 **플레이어 단독** 정렬만 적는다(클립 ↔ 표적).
- `CLAUDE.md:237`(§11)은 *"정렬 식이 양쪽에서 동일하므로"*라고만 하고 **배속이 달라도 성립하는 이유**는 적지 않는다.
- **두 배우의 클립을 맞추는 규칙은 어디에도 없다.**

### 6-7. 배속은 클립 전체에 걸린다 — 임팩트 이후도 같이 빨라진다

`ResolvePlaySpeed`는 **임팩트 이전 구간**(`ResolvedImpactSpan`)이 남은 시간에 들어가도록 배속을 정한다. 그런데 그 배속은 **클립 전체**에 적용된다(Animator의 Speed Multiplier).

사망 클립에서는 이게 직접적인 결과를 낳는다 — **쓰러지는 구간(임팩트 이후)도 같이 압축되고, 절단 시각 `T_end`도 그만큼 당겨진다.**

```
ImpactSpan 0.05초 → speed ≈ 1   → 쓰러짐 자연 속도
ImpactSpan 0.30초 → speed = 3   → 쓰러짐도 3배속, T_end도 1/3
```

즉 **사망 클립의 `ImpactTime` 위치가 쓰러지는 속도까지 결정한다.**

## 7. 관련 이벤트

- `OnEnemyKilled`(`:109`) — 지금은 **확정 시점(L)**에 발행. `CameraDirector`가 `EnemyKilled` 쉐이크 큐로 쓴다. 절단이 뒤로 밀리면 쉐이크와 화면 사건이 어긋난다.
