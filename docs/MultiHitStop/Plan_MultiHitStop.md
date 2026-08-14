# Plan — 한 클립 안의 다중 히트스톱 (MultiHitStop)

근거: `docs/MultiHitStop/Research_MultiHitStop.md` → **설계 B**(클립 초 저작 + 배우가 환산)

## 확정된 저작 모델 (2026-08-10 결정)

```
클립:   [ 1타 ]---[ 2타 ]---[ 3타 ]---[ 마지막 베기 ]
스톱:      ●        ●        ●            ●
마크:    extra    extra    extra      ImpactTime  ← 정렬 앵커
                                          ↑
                            Deadline + Pattern.ImpactOffset
                            (표적 절단 · 적 사망 · 카메라 큐가 붙는 그 시각)
```

**추가 스톱은 전부 메인 임팩트 이전이다.** 그래서 Research가 "가장 위험한 단계"로 분류했던 따라잡기가
**예외가 아니라 주 경로**가 된다 — 그 전제로 계획을 다시 세운다.

## 문제의 핵심 — 정지 시간은 어디선가 나와야 한다

정지 N회 × `hitStopDuration` = **총 정지 시간 F**. 그 F 동안 클립은 안 흐르는데
마지막 베기는 여전히 `Deadline + ImpactOffset`에 도달해야 한다. 방법은 둘뿐이다:

| 방식 | 내용 | 판정 |
|---|---|---|
| **예산(budget)** | 재생을 **F만큼 일찍 시작**한다. 클립은 저작 배속 그대로 돌다가 N번 멈추고, 결국 제시각에 도착 | **주 경로.** 배속을 안 건드려 모션이 안 뭉갠다 |
| **따라잡기(catch-up)** | 멈춘 만큼 이후 배속을 올려 벌충 | **보정용.** 예산이 어긋났을 때(스톱 누락·창 부족) 자동으로 맞춘다 |

**둘 다 넣는다.** 예산이 정상 경로이고, 따라잡기는 **실제로 멈춘 시간을 보고 다시 계산**하므로
스톱이 하나 빠지거나 창이 모자라도 마지막 베기는 제자리를 지킨다.

## 원칙 넷

1. **마지막 베기 = `ImpactTime` = 성역.** 표적 절단·적 사망·카메라 큐의 앵커는 그대로다.
2. **정지 시간은 미리 예산으로 잡는다** — 클립 시작을 앞당겨서. 배속으로 때우면 모션이 뭉개진다.
3. **해제할 때마다 다시 계산한다** — 예산이 틀려도 자기 수정된다.
4. **추가 스톱은 적을 얼리지 않는다**(§Step 5). 적 클립도 같은 시각에 정렬돼 있어 함께 얼리면 그쪽 정렬이 깨진다.

---

## Step 1 — 데이터: `ClipAlignment.extraImpactTimes`

- [x] 1-1. `ClipAlignment`에 필드 추가:
  ```csharp
  [Tooltip("마지막 베기 '이전'의 칼질 프레임들(클립 절대 초). ImpactTime과 같은 좌표다.\n" +
           "ImpactTime은 언제나 마지막 베기에 찍는다 — 표적이 갈라지는 시각이 거기 정렬되기 때문.\n" +
           "여기 값들은 타격감 전용이며 절단·판정을 움직이지 않는다.")]
  [SerializeField] private float[] extraImpactTimes;
  ```
- [x] 1-2. `ResolvedExtraImpactSpans` — 트림 시작 기준 상대 초로 변환 + **오름차순 정렬**.
      버리는 것: 트림 밖, 그리고 **`ResolvedImpactSpan` 이상인 값**(= 마지막 베기보다 뒤).
      뒤쪽 값을 버리는 이유는 저작 모델이 "임팩트 이전"이기 때문이고, 남겨 두면 정지가 절단 뒤에 와서
      **몸이 갈라진 뒤에 화면이 멈춘다.**
- [x] 1-3. `TotalFreeze(float perStop)` = `유효 마크 수 × perStop`. 예산 계산의 유일한 출처다.
- [x] 1-4. `ValidateImpactTime`에 검증 추가 — 트림 밖 / 임팩트 이후 마크는 경고(메인과 같은 규율).

## Step 2 — 정렬식에 예산을 넣는다 (⚠ 이 Step이 정렬의 심장이다)

기존 두 메서드는 `impactAlignTime` 하나만 본다. **정지로 소비될 F를 빼면 나머지 수학이 그대로 성립한다.**

- [ ] 2-1. `ResolveScheduleStart(impactAlignTime, earliest, freeze)`:
      `Mathf.Max(impactAlignTime - freeze - ResolvedImpactSpan / Speed, earliest)`
- [ ] 2-2. `ResolvePlaySpeed(now, impactAlignTime, maxSpeed, out clamped, freeze)`:
      남은 시간에서 F를 먼저 뺀다 — `remaining = impactAlignTime - freeze - now`.
- [ ] 2-3. **기본값 `freeze = 0`으로 오버로드**해 기존 호출부(적 공격·견제·리액션·사망)를 **한 줄도 안 고친다.**
      이 기능은 플레이어 공격 슬롯에만 붙는다.
- [ ] 2-4. ⚠ `remaining`이 0 이하가 되면(창이 예산을 못 감당) **정렬이 원리적으로 불가능하다.**
      그때는 예산을 포기하고 `freeze = 0`으로 재계산한 뒤 **경고 1회** — 스톱은 여전히 걸리지만
      따라잡기(Step 6)가 마지막 베기를 제자리로 끌어온다.

## Step 3 — 배우: 환산과 통지

- [x] 3-1. `CharacterActionPlayer`에 확장 포인트:
  ```csharp
  /// <summary>마지막 베기 이전의 칼질이 도달하는 절대 시각. 재생 시작 시점에 배속으로 환산해 미리 알린다.</summary>
  public event Action<float> OnExtraImpact;
  ```
- [x] 3-2. `SchedulePendingSuccess`에서 F를 구해 예약에 반영한다(`pendingFreeze`).
      **`hitStopDuration`을 알아야 한다** — 인스펙터에 두 번 적으면 언젠가 하나만 고쳐지므로
      `HitStopDirector`에서 **읽어 온다**(참조가 없으면 `freeze = 0`, 기존 동작).
      ⚠ 이 방향(`Player → Director`)은 새 결합이다. 반대로 디렉터가 밀어 넣으면 큐가 둘이 되어
      어긋나는 순간이 생긴다(§6 `CurrentAttacker`를 pull로 당겨 오는 것과 같은 근거).
- [x] 3-3. `TryStartPendingSuccess`의 `PlaySlot` 직후, **확정된 배속으로** 각 마크의 절대 시각을 환산해 발행:
      `worldTime = Time.time + span / speed` (그 사이 정지가 끼면 실제 도달은 늦지만
      Step 6의 재계산이 남은 마크 시각도 함께 갱신한다 — 3-4 참조).
- [x] 3-4. **아직 안 온 마크는 정지 해제 때마다 함께 밀어 준다.** 안 그러면 두 번째 스톱이
      "첫 정지 시간만큼" 이르게 터진다. 발행 시각을 리스트로 들고 있다가 해제 시 남은 것들에 실제 정지 시간을 더한 뒤
      **디렉터에 재통지**한다 — 또는 (더 단순하게) **한 번에 하나씩만 발행**한다: 스톱이 끝나면 다음 마크를 계산해 발행.
      **후자를 택한다** — 리스트 동기화가 없어 어긋날 자리가 없다.
- [x] 3-5. ⚠ 미스로 예약이 취소되면 발행하지 않는다 — `missedThisTarget` 가드 뒤라 이미 만족한다.

## Step 4 — 디렉터: 예약 큐

- [x] 4-1. `HitStopDirector`: `hasPending`/`pendingFireTime` → **작은 리스트**.
      기존 "예약은 최대 하나" 주석은 지우지 말고 **왜 더는 성립하지 않는지**로 고쳐 쓴다
      (근거였던 "패턴 완료의 순차성"은 여전히 참이지만, 이제 한 패턴이 스톱을 N개 낸다).
- [x] 4-2. `OnExtraImpact` 구독 → 예약 추가. `hitStopEnabled`가 꺼져 있으면 예약하지 않는다(기존 규율).
- [x] 4-3. `Fire(bool pushBurst, bool freezeEnemy)` — 메인만 `true, true`, 추가 스톱은 `false, false`.
- [x] 4-4. `hitStopDuration`을 읽을 수 있게 공개(`public float HitStopDuration => hitStopEnabled ? hitStopDuration : 0f`).
      **꺼져 있으면 0을 돌려준다** — 그래야 예산과 실제 정지가 자동으로 일치한다.
- [x] 4-5. `OnAllPatternsCleared`에서 리스트 clear.

## Step 5 — 적과 절단은 건드리지 않는다

- [x] 5-1. `EnemyView.ApplyHitStop(duration, burstTime, bool pushBurst)` — false면 `burstTime`을 그대로 반환.
- [x] 5-2. `EnemyDirector.ApplyHitStop(duration, bool pushBurst)`로 전달.
- [x] 5-3. **추가 스톱은 `EnemyDirector`를 아예 호출하지 않는다**(4-3의 `freezeEnemy = false`).
      ⚠ 근거를 주석에 남긴다: 적의 공격·사망 클립도 **같은 `impactAlignTime`에 정렬**돼 있는데(§6·§11-3)
      적에게는 예산·따라잡기가 없다. 함께 얼리면 **적 쪽 정렬만 정지 시간만큼 밀린다.**
      마지막 베기의 스톱(메인)에서는 지금처럼 전원 정지한다 — 그때는 이미 임팩트가 지나간 뒤라 안전하다.
- [x] 5-4. 카메라·파티클은 추가 스톱에서도 **같이 얼린다.** 정렬을 안 들고 있고,
      캐릭터만 멈추면 "멈췄다"가 아니라 "캐릭터만 렉"으로 읽힌다(§7-3).

## Step 6 — 따라잡기 (자기 수정)

- [x] 6-1. `CharacterActionPlayer`에 재생 스냅샷 `playStartTime`을 **되살린다.**
      ⚠ 주석에 이력을 남긴다 — 2026-08-10 리팩토링이 "소비자 없음"으로 지웠던 필드이고
      (`docs/Refactoring/`), **이 기능이 그 소비자다.**
- [x] 6-2. `ApplyHitStop`에서 임팩트 통과 여부를 판단: `소비한 클립 초 = (Time.time - playStartTime) * playSpeed − 누적 정지 보정`.
      임팩트 스팬 이상이면 **기존 경로**(밀기: `actionEndTime`·`recoveryEndTime` += d, 해제 시 `playSpeed` 복원).
- [x] 6-3. 아직 전이면 **밀지 않는다.** 해제 시각에 배속을 재계산:
      `speed = 남은 임팩트 스팬 ÷ (pendingImpactAlignTime − 해제시각)`, `maxAttackSpeed`로 클램프.
      **예산이 정확하면 이 값은 원래 배속과 거의 같다** — 따라잡기는 오차 보정이지 주 수단이 아니다.
- [x] 6-4. 클램프에 걸리면 경고(정렬이 깨졌다는 뜻). 기존 패링 경고와 같은 형식.
- [x] 6-5. 해제 시 **다음 마크 하나를 발행**한다(3-4의 "한 번에 하나씩").

## Step 7 — 저작 도구

- [x] 7-1. `Tools/Animation Clip Trimmer`에 추가 마크 리스트(추가/삭제/현재 프레임 집기). `Mark Impact` 옆에 `+ Mark Extra`.
- [x] 7-2. 타임라인에 추가 마크를 **다른 색 배지**로(메인 `✦ IMPACT`와 구분).
- [x] 7-3. **예산 표시가 이 도구의 핵심 기능이다** — `마크 수 × hitStopDuration = F`와
      "그만큼 일찍 시작할 여유가 있는가"(= 앞 패턴과의 간격)를 나란히 보여 준다.
      실측 기준: 엔트리 간 최소 입력 간격 0.4초 · 창 p50 1.30초 → **F가 0.3초를 넘으면 대부분의 채보에서 창을 넘긴다.**
      넘치면 경고(저작 단계에서 잡는 게 런타임 경고보다 싸다).
- [x] 7-4. 저장 시 트림 밖·임팩트 이후 마크는 버린다(경고 1회).

## Step 8 — 검증

- [x] 8-1. 컴파일 + EditMode 테스트 green.
- [ ] 8-2. **회귀가 최우선이다** — 추가 마크가 없는 기존 패턴에서 절단·칼날·카메라 큐가 예전과 **완전히 동일**한지.
      (가장 큰 위험은 새 기능이 아니라 §6 정렬식을 건드리는 것이다. Step 2가 그 자리다.)
- [ ] 8-3. 다중 마크 패턴: **칼질마다 멈추고**, 마지막 베기와 **절단이 여전히 같은 프레임**인지.
- [ ] 8-4. `hitStopEnabled`를 끄면 예산도 0이 되어 정렬이 그대로인지(4-4).
- [ ] 8-5. 창이 빠듯한 엔트리(간격 0.4초)에서 마크 3개 → 2-4 폴백과 6-3 따라잡기가 실제로 마지막 베기를 제자리에 두는지.
- [ ] 8-6. 사슬(§11-5)·기습 회피와 섞였을 때 예약이 새지 않는지 — 곡 중단 후 리스트가 비는지.

---

## 범위 밖

- **적 공격(`Attacker.Enemy`)의 다중 타격** — 플레이어 패링과 프레임을 짝맞춰야 하고, 적에게도 예산·따라잡기를 줘야 한다.
  구조는 그대로 확장 가능하다(`EnemyView`가 같은 이벤트를 발행하고 Step 2의 `freeze` 인자를 쓰면 된다).
- **스톱마다 다른 길이** — 지금은 `hitStopDuration` 하나를 공유한다. "마지막 타만 길게"가 필요하면
  마크를 `(시각, 배율)` 쌍으로 올린다. 그때 예산 계산도 합으로 바뀐다.
- **칼질마다 카메라 쉐이크/펀치** — `CameraCueCatalog` 관할이고 층이 다르다(§7-3).

---

## 실행 결과 (2026-08-10)

Step 1·3~7 구현 완료. **컴파일 에러 0 · 경고 0, EditMode 120/120 green**(8-1).

### Step 2를 계획과 다르게 했다 (의도적)
Plan은 `ClipAlignment.ResolveScheduleStart`/`ResolvePlaySpeed`에 `freeze` 인자를 추가하라고 했다.
**하지 않았다** — 확인해 보니 **플레이어는 그 두 메서드를 안 쓴다**(자기 안에서 같은 식을 인라인으로 계산한다).
쓰는 쪽은 `EnemyView`뿐인데 적은 이번 범위 밖이다.

그대로 했으면 **아무도 안 넘기는 인자**를 공용 클래스에 심는 꼴이라, 예산 계산은
`CharacterActionPlayer`의 실제 계산 자리 두 곳(`SchedulePendingSuccess`·`TryStartPendingSuccess`)에 넣었다.
데이터(`ResolvedExtraImpactSpans`·`TotalFreeze`)는 계획대로 `ClipAlignment`에 있다.
적까지 확장할 때 그때 인자를 추가하면 된다(범위 밖 절 참조).

### 구현 요약
- **`ClipAlignment`**: `extraImpactTimes`(클립 초) + `ResolvedExtraImpactSpans`(트림 기준 상대 초, 오름차순,
  **임팩트 이상은 버림**) + `TotalFreeze(perStop)` + `OnValidate` 경고.
- **`CharacterActionPlayer`**:
  - 예약 시 `pendingFreeze = 마크 수 × HitStopDirector.HitStopDuration` → **시작 시각을 그만큼 앞당긴다.**
  - 재생 시작 시 배속도 `impactAlignTime − freeze` 기준으로 역산. 창이 예산을 못 감당하면 예산 포기 + 경고 1회.
  - `segmentStartTime`/`clipConsumed`/`playingImpactSpan` 스냅샷 — **정지 전에 소비 클립 초를 확정**한다.
  - `ApplyHitStop`: **임팩트 이전이면 복귀 스케줄을 밀지 않는다**(밀면 마지막 베기가 절단보다 늦는다).
  - `ReleaseHitStop`: 임팩트 이전이면 `남은 스팬 ÷ 남은 시간`으로 배속 재계산(따라잡기), 상한 초과 시 경고.
  - `OnExtraImpact` — **한 번에 하나씩** 발행(정지가 끼면 이후 마크가 밀리므로).
- **`HitStopDirector`**: 예약을 리스트로, `Fire(bool isMainImpact)`, `minHitStopGap`(기본 = `hitStopDuration`),
  `HitStopDuration` 공개(꺼져 있으면 **0** → 예산과 실제가 자동 일치).
- **`EnemyView`/`EnemyDirector`**: `pushBurst` 인자. 추가 스톱은 **적을 아예 호출하지 않는다.**
- **`Pattern Action Editor`**: `+ Mark Extra` · 마크 리스트(점프 `→` / 삭제 `×`) · 타임라인 `◆ N타` 배지 ·
  **정지 예산 표시**(0.3초 초과 시 경고) · 저장 시 구간 밖 마크 제외.
- **씬**: 플레이어의 `hitStopDirector` 참조 배선.

### 남은 것 — 플레이 검증 (8-2 ~ 8-6)
1. **회귀가 최우선** — 추가 마크가 없는 기존 패턴에서 절단·칼날·카메라가 예전과 동일한가
2. 다중 마크 패턴에서 칼질마다 멈추고, **마지막 베기와 절단이 같은 프레임**인가
3. `hitStopEnabled`를 끄면 정렬이 그대로인가
4. 창이 빠듯한 엔트리(0.4초)에서 마크 3개 → 예산 포기·따라잡기가 실제로 제자리에 두는가
5. 사슬·기습과 섞였을 때 예약이 새지 않는가(곡 중단 후 리스트 clear)

⚠ **아직 마크가 찍힌 패턴이 없다.** 여러 번 베는 클립에서 `Pattern Action Editor`로 마크를 찍어야 이 기능이 화면에 나타난다.
