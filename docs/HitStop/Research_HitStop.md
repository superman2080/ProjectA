# Research — HitStop

요구: **패턴 성공 시 임팩트 프레임에 히트스톱.** 애니메이터 정지(A) + 카메라 펀치(B), 그리고 **정지로 생긴 공백은 뒤에서 배속으로 흡수해 없앤다.**

---

## 1. ⚠ `Time.timeScale`은 이 게임에서 쓸 수 없다 (실측)

`Assets/02. Scripts` 전수 검색:

| 검색어 | 결과 |
|---|---|
| `timeScale` | **0곳** |
| `unscaled` (`unscaledTime`/`unscaledDeltaTime`) | **0곳** |
| `Time.time` / `Time.deltaTime` | **78곳 / 13파일** |

78곳에는 판정(`PatternHandler`), 클립 정렬(`CharacterActionPlayer`·`EnemyView`), 표적 운동(`SliceTargetView`), 카메라 예약(`CameraDirector`), 링 수축(`FocusRingView`)이 전부 포함된다.

반면 채보 진행은 `audioSource.time` — **오디오는 `timeScale`의 지배를 받지 않는다.** `timeScale`을 내리면 게임 시계만 느려지고 음악은 그대로 흐른다 → 그 차이가 **되돌릴 수 없이 누적**된다. `perfectWindow`가 0.05초인데 통상 히트스톱이 0.05~0.10초이므로 **단 한 번으로 판정이 무너진다.**

`CameraCueCatalog.cs:9`에 이미 결론이 적혀 있다: `EnemyKilled, // 적 처치 — 무쌍 타격감의 주 큐(히트스톱을 못 쓰므로 여기서 낸다)`.

**결론: 시계를 건드리지 않고, 애니메이터 재생 속도만 0으로 만든다.**

---

## 2. 임팩트 시각은 이미 세 곳이 각자 알고 있다

`임팩트정렬시각 = Deadline + Pattern.ImpactOffset` (= `LastNodeTime + GoodWindow + ImpactOffset`)

| 소유자 | 변수/식 | 위치 |
|---|---|---|
| `CharacterActionPlayer` | `pendingImpactAlignTime` | `HandleJudgeTargetBegan` (L558) |
| `EnemyDirector` → `EnemyView` | `impactTime` 인자 | `KillOpponent` → `AssignDeath` (L761, L573) |
| `CameraDirector` | `fireTime` | `HandlePatternComplete` (L244) |
| `SliceTargetDirector` | 표적 절단 시각 | §11 |

**새 이벤트를 만들 필요가 없다** — 네 곳 다 같은 식으로 같은 값을 이미 갖고 있다.

---

## 3. 정지시켜야 할 대상과 각자의 스케줄

### 3-1. 플레이어 (`CharacterActionPlayer`)

- 재생: `PlaySlot(clip, startOffset, dur, speed, isSwing)` — `animator.SetFloat(attackSpeedHash, speed)` (L709)
- **`actionEndTime = Time.time + dur / speed`** (L712) ← 절대 시각. 트림 끝 = 복귀 시작점
- `recoveryEndTime = actionEndTime + recoveryHoldDuration` (L713)
- `Update`가 `Time.time >= actionEndTime`에서 `AttackSpeed`를 1로 되돌리고 `OnSwingEnded` 발행 (L296~301)

**임팩트 이후 남는 클립 구간**: `(dur − impactSpan) / speed`. `pendingImpactSpan`은 이미 필드로 있다(L184).

⚠ **`PlaySlot`은 재생 시작 시각과 배속을 보관하지 않는다.** `actionEndTime`만 남긴다 — 캐치업 배속을 역산하려면 "지금까지 소비한 클립 초"를 알아야 하므로 **`playStartTime`·`playSpeed`·`playDur` 보관이 필요**하다.

### 3-2. 적 사망 (`EnemyView.AssignDeath` → `EnemyDirector.PendingKill`)

- `AssignDeath`가 배속을 역산해 `PlayDeath` 후 **`burstTime = Time.time + death.ResolvedDuration / speed`** 반환 (L597)
- `EnemyDirector.pendingKills`가 그 값을 들고, `TickPendingKills`가 `now >= burstTime`에 `ExecuteKill`(절단·시체 교체·`OnEnemyBurst`) (L780~791)

**임팩트 이후 남는 구간이 사망 클립의 대부분이다** — §11-3에 따르면 사망 클립의 `ImpactTime`은 트림 시작 근처에 찍는다. **캐치업 여유가 가장 넉넉한 배우다.**

⚠ `burstTime`은 `EnemyDirector`가 들고 있고 배속은 `EnemyView`가 안다. 캐치업으로 배속을 바꾸면 **둘 다 갱신**해야 한다.

### 3-3. 표적 조각 (`SlicePiece`) — 이번 범위 밖 후보

물리 없이 경과 시간 `t`의 닫힌 식(§11). 정지시키려면 `t`에 오프셋을 넣어야 하는데, **애니메이터가 아니라 별도 메커니즘**이다.

타이밍상 표적 절단은 **임팩트 시각 바로 그때**라 히트스톱과 겹친다 → 캐릭터는 멈췄는데 조각만 날아가는 **이음매가 생긴다.** 적 사망 폭발(`burstTime`)은 트림 끝이라 히트스톱보다 훨씬 뒤라서 무관하다.

### 3-4. 정지시키면 **안 되는** 것

| 대상 | 이유 |
|---|---|
| `PatternHandler` 판정 | 리듬게임의 전부 |
| `ChartPlayer` / `audioSource` | 음악은 안 멈춘다 |
| `FocusRingView` 수축 | 타이밍 단서 그 자체 |
| `PlayerCombatMover` 이동/회전 | 다음 결투 도착 시각을 지켜야 한다(§11-2) |
| `CameraDirector` 예약 타이머 | 큐가 밀린다 |

---

## 4. 캐치업(공백 흡수)의 성립 조건

정지 시간 `D` 동안 클립이 안 나아가므로, 해제 후 남은 클립을 **원래 예정된 절대 시각까지** 밀어 넣어야 한다.

```
남은클립초 R = playDur − (freezeTime − playStartTime) × playSpeed
남은실시간 T = originalEndTime − (freezeTime + D)
캐치업배속   = R / T
```

이건 **`ClipAlignment.ResolvePlaySpeed`와 같은 꼴**이다(`ResolvedImpactSpan / remaining`). 새로운 개념이 아니다.

### 성립 한계

`D`가 임팩트 이후 잔여 구간에 비해 크면 캐치업 배속이 폭발한다. `T → 0`이면 무한대.

- **플레이어**: 잔여 = `(dur − impactSpan)/speed`. `maxAttackSpeed` 2.5 상한에 이미 걸려 있는 패턴이면 여유가 거의 없다.
- **적 사망**: 잔여가 클립 대부분이라 여유가 크다.

⚠ **상한에 걸리면 원래 시각을 못 지킨다.** 그때 `actionEndTime`/`burstTime`을 그만큼 **밀어야** 한다 — 안 밀면 클립이 중간에 끊긴 채 다음 단계로 넘어간다. 판정은 어차피 무관하므로 **연출만 조금 늦어지는 쪽이 옳은 열화**다.

### 안전 가드

`D`보다 잔여 구간이 충분히 크지 않으면 **그 배우는 히트스톱을 건너뛴다.** 카메라 펀치(B)는 그래도 나가므로 타격감은 남는다.

---

## 5. 카메라 펀치(B)가 붙을 자리

- `CameraDirector.HandlePatternComplete`가 **이미 `fireTime`에 큐를 예약**한다(L239~255). 예약 슬롯은 하나고 그 근거(패턴 완료가 순차적, 최소 0.4초 간격)는 히트스톱에도 그대로 유효하다.
- `CameraCueEntry`는 `trigger` / `shakeAmplitude` / `shakeDuration` 세 필드. **"연출 추가 = 카탈로그에 한 줄"** 관례 → 펀치도 여기 필드로 붙이는 게 결이 맞다.
- 씬 실측: vcam FOV **40**, `GroupFraming.SizeAdjustment = DollyOnly`, `DollyRange (−2, +4)`.

⚠ CLAUDE.md는 `DollyOnly`의 근거로 "Zoom이면 원근이 왜곡된다"를 든다. 그건 **프레이밍이 상시 FOV를 흔드는 것**에 대한 경고다. 0.1초짜리 **일시적** FOV 펀치는 성격이 다르다(왜곡 자체가 타격감의 재료). 다만 `GroupFraming`이 매 프레임 돌리를 계산하므로 **펀치는 `Lens.FieldOfView`에만 걸어야** 둘이 안 싸운다.

---

## 6. 정리 — 필요한 변경

| # | 대상 | 성격 |
|---|---|---|
| 1 | 히트스톱 시각을 잡고 배우들에게 지시하는 **한 곳** | 신규 |
| 2 | `CharacterActionPlayer` — 재생 시작 시각/배속 보관 + 프리즈/캐치업 | 기존 수정 |
| 3 | `EnemyView` + `EnemyDirector` — 사망 클립 프리즈/캐치업 + `burstTime` 갱신 | 기존 수정 |
| 4 | `CameraCueEntry` + `CameraDirector.ApplyPunch` | 기존 확장(카탈로그 관례) |

**성공(`AllCorrect`)에서만** 발동한다 — 실패는 표적이 부딪혀 소멸하는 것이라 멈출 임팩트가 없다.
