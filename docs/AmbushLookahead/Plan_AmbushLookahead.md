# Plan — 기습 예보 (AmbushLookahead) — ⚠ 폐기 (2026-08-12)

> **착수하지 않았다.** 이 문서의 목적(경고 시간 1초 고정)은 예보 없이 달성됐다 —
> `docs/AmbushSlot/Plan_AmbushSlot.md`의 "결과" 절 참조.
>
> **왜 예보가 필요 없었나**: 링은 이미 임팩트에서 역산해 뜨고, `lead`는 창 길이가 아니라
> `now`부터 재는 값이라 대시 구간을 포함한다. 즉 **`maxRingExposure` 0.8이 자르고 있었을 뿐**이고,
> 두 값을 1.0으로 맞추자 자격 검사(`RequiredLead`)가 저절로 안전장치가 됐다.
>
> **그래도 남겨 두는 이유**: §1~3의 코드 사실(공백이 어디서 오는가 · 예보에 필요한 값이 전부 정적인가 ·
> `ClipAlignment`가 이미 식을 소유한다)은 그대로 유효하고, **§5-2의 경합 분석**은
> 지금도 실재하는 구멍이다. 나중에 "기습 시각을 저작한다"로 갈 때 이 조사가 재사용된다.

---


근거: `docs/AmbushLookahead/Research_AmbushLookahead.md`
요구: **모든 기습 경고를 언제나 정확히 같은 시간 동안 보여준다.** 기본 1.0초, **인스펙터에서 바꿀 수 있어야 한다.**

핵심 근거 한 줄(Research §2): **링 노출은 공백 안에 있을 필요가 없다.**
공백에 요구되는 것은 구르기뿐이고, 링은 `ScreenSpaceOverlay`라 대시 중에도 성립한다.
→ 노출을 늘리는 방법은 공백을 늘리는 게 아니라 **결정을 앞당기는 것**이며, 그래서 **발동 빈도가 안 깎인다.**

---

## Step 1 — 시각의 출처: `ChartPlayer` 선행 조회

- [ ] 1-1. `ChartPlayer`에 **읽기 전용 선행 조회**를 연다. `pendingEntries`는 이미 시각순 정렬이라
      인덱스로 집으면 된다(`ChartPlayer.cs:128`). 시그니처는 `bool TryPeekEntry(int ahead, out SongChartEntry entry)`
      정도 하나 — 리스트를 통째로 노출하지 않는다(소비자가 순서를 재해석하기 시작하면 재생 글루가 둘이 된다).
- [ ] 1-2. ⚠ **오디오→실시간 변환은 `ChartPlayer`가 한다.** 채보 시각은 `audioSource.time`,
      회피·클립·판정은 `Time.time`이다. `float ToRealtime(float audioTime)` 하나를 같이 연다.
      **다른 클래스가 `audioSource`를 만지게 두지 않는다** — 시계를 둘로 만드는 것이 §7-3 `timeScale` 함정과 같은 부류다.
- [ ] 1-3. 곡이 안 돌거나(카운트다운 중) 남은 엔트리가 모자라면 `false`. 호출자는 그 프레임을 조용히 건너뛴다.

## Step 2 — 공백 끝 예보: 식을 새로 쓰지 않는다

- [ ] 2-1. 예보 대상은 **다음 엔트리의 클립 시작 시각**이다(= 그 공백의 끝, Research §1-1):
  ```
  deadline    = ToRealtime(마지막 온셋) + PatternHandler.goodWindow
  impactAlign = deadline + template.ImpactOffset
  alignment   = template.Attacker == Enemy ? template.PlayerParry : template.PlayerAttack
  windowEnd   = alignment.ResolveScheduleStart(
                    impactAlign - alignment.TotalFreeze(hitStop.HitStopDuration),
                    ToRealtime(첫 온셋))
  ```
- [ ] 2-2. ⚠ **`ClipAlignment`의 기존 메서드를 그대로 부른다**(`ResolveScheduleStart` · `TotalFreeze`).
      새 수학을 만들면 `CharacterActionPlayer.cs:709`와 두 벌이 되고, 어긋나는 날 원인이 안 보인다.
- [ ] 2-3. `alignment.IsUsable`이 아니면(무연출 패턴) `windowEnd = deadline`.
      `SchedulePendingSuccess`의 분기와 같은 값을 보므로 자연히 일치한다(Research §5-3).
- [ ] 2-4. **선택 정리**: `CharacterActionPlayer.cs:709`의 손으로 펼쳐 쓴 식을 같은 메서드 호출로 접는다.
      지금 있는 중복이 하나 줄어든다. ⚠ 동작이 바뀌면 안 된다 — 값이 같은지 로그로 한 번 확인하고 넣는다.
- [ ] 2-5. `PatternHandler.goodWindow`를 읽는 공개면이 필요하다(현재 private). **읽기 전용 프로퍼티 하나**만 연다.

## Step 3 — 결정 시점 이동

- [ ] 3-1. `DodgeDirector`가 매 프레임(또는 엔트리 소진 시) 선행 엔트리를 보고
      **`impact = windowEnd - (rollMoveDuration + margin)`**, **`showTime = impact - ringExposure`**를 계산한다.
      `showTime`에 도달하면 그때 발동한다.
- [ ] 3-2. **`ringDuration`은 상수가 된다** — `Mathf.Clamp(lead, min, max)`가 사라진다.
      `minRingExposure`/`maxRingExposure` 두 필드를 **`ringExposure`(기본 1.0) 하나로 합친다.**
      요구가 "언제나 정확히"이므로 범위를 둘 이유가 없다. 값은 인스펙터에서 바꾼다.
- [ ] 3-3. ⚠ **`RequiredLead`의 의미가 바뀐다.** 지금은 "공백 안에서 재는 값"이라 하한을 올리면 빈도가 깎였다
      (Research §1-2). 예보에서는 **`showTime`부터 임팩트까지가 언제나 `ringExposure`**이므로,
      클립 선별은 `windup ≤ ringExposure`인지만 보면 된다 — **공백 길이와 무관해진다.**
      즉 `ringExposure`를 올려도 빈도가 안 깎이고, 오히려 **긴 와인드업 클립이 더 많이 통과한다.**
- [ ] 3-4. **공백 검사(`minIdleWindow`)는 남긴다.** 구르기는 여전히 서 있는 구간에 들어가야 한다.
      ⚠ 다만 예보 시점에는 공백의 **시작**을 모른다(`recoveryEndTime`·`convergeUntil`이 확정 전) —
      끝만 정확하다. 그래서 예보 시점에는 **"이전 엔트리의 임팩트 + `recoveryHoldDuration`"**을 시작의 추정치로 쓰고,
      추정 오차는 `margin`이 흡수한다. 정확한 값은 3-6이 본다.
- [ ] 3-5. 텔레그래프(적 클립)는 지금처럼 **자기 시각에 독립 예약**한다 —
      `AssignAttack(clip, impactTime, ...)`이 `ClipAlignment`로 시작 시점을 역산하므로
      링보다 먼저 뜰 수도, 나중일 수도 있다. **둘을 맞추려 하지 않는다**(§6의 두 배우 규칙과 같은 결).
- [ ] 3-6. 실제 `OnIdleWindow`가 열리면 **검증만** 한다 — 예보와 실제 공백이 어긋났으면 로그로 남긴다.
      ⚠ **여기서 취소하지 않는다**(Step 4).

## Step 4 — 늦은 취소 정책 (Research §5-2, 이 설계의 진짜 위험)

지금은 `Fire()`가 `BusyReasonBy(impactTime)`로 조용히 취소한다. 링이 먼저 뜨면 **취소가 화면에 보인다** —
"링만 떴다가 아무 일도 안 일어남"은 회피 학습을 통째로 망가뜨린다(누른 사람은 자기가 틀렸다고 읽는다).

- [ ] 4-1. **적을 예보 확정 순간에 잡는다.** 지금 `Fire()`에서 하는 `AssignAttack`을 그대로 앞당긴다.
      그러면 그 적은 공격 예약을 든 상태가 되고, `EnemyView.IsIdle`이 false가 된다.
      ⚠ 그러면 텔레그래프 클립도 같이 앞당겨지는가? 아니다 — `AssignAttack`은 `impactTime`을 받아
      시작 시점을 스스로 역산한다(3-5). **예약과 재생 시작은 원래 분리돼 있다.**
- [ ] 4-1-1. ⚠ **그것만으로는 경합이 안 막힌다 — 이 Step의 핵심.**
      적을 고르는 곳이 둘인데 **서로를 안 본다**:
      | 고르는 곳 | 바쁜지 확인하나 |
      |---|---|
      | `EnemyDirector.PickIdleAmbusher` (기습자) | **예** — `view.IsIdle` (`EnemyDirector.cs:469`) |
      | `EnemyDirector.TakeTargetForWindow` (결투 상대) | **아니오** — 무리 소속 + **거리만** 본다 (`:925~947`) |

      즉 4-1의 예약을 **상대 배정 쪽은 읽지 않는다.** 게다가 하필 잘 뽑힌다 —
      기습자는 `stageDistance`(2.5m)로 플레이어 옆에 붙여 두는데, 짧은 창에서는 `desired`가 작아
      **그 거리가 정확히 상대 선택이 찾는 값**이다. 뽑히면 `ring.Remove(picked)`로 링에서 빠진다.
- [ ] 4-1-2. **`TakeTargetForWindow`의 후보 루프에서 '클립이 예약된 적'만 제외한다.**
      `EnemyView`에 좁은 읽기면 하나(`HasPendingAction` — `hasPendingAttack || hasPendingReaction`)를 열고
      후보 루프에서 `continue`한다.
      ⚠ **`BusyReasonBy(t) != null`로 넓게 막으면 안 된다** — 그러면 *이동 중*인 적까지 후보에서 빠진다.
      무리 집결·링 복귀·등장 걸어오기는 상시 일어나는 정상 상태라(§11-6), 통째로 빼면
      **거리를 창에 맞추는 §11-2의 표적 선택이 좁아진다.** 드문 기습 경합을 고치려고 전투 주 경로를 바꾸는 셈이다.
- [ ] 4-1-3. 이 구멍은 **지금도 있다.** 다만 `Fire()`의 `BusyReasonBy` 검사가 링이 뜨기 **전에** 잡아
      조용히 취소하는 것으로 끝나 있어 화면에 안 보였을 뿐이다. 예보는 그 취소를 **보이게** 만들 뿐,
      새로 만드는 것이 아니다 — 그래서 4-1-2는 예보와 무관하게 그 자체로 옳은 수정이다.
- [ ] 4-2. 그래도 취소가 필요한 경우(적이 죽는다·곡이 끊긴다·`Abort`)는 **링을 즉시 걷는다.**
      드물고, 원인이 화면에 보이는 사건들이라 오해가 안 생긴다.
- [ ] 4-3. ⚠ **미스로 인한 예약 취소는 취소 사유가 아니다**(Research §5-1) —
      그때는 공백이 **길어진다**. 예보가 잡은 임팩트가 실제보다 이를 뿐이고, 이른 것은 언제나 안전하다.
- [ ] 4-4. 쿨다운(`cooldown` 6초)은 **예보 확정 시각**을 기준으로 재계산한다.
      지금은 `Finish()`에서 걸리는데, 결정이 앞당겨지면 두 사건이 겹칠 수 있다.

## Step 5 — 정리·제거

- [ ] 5-1. `HandleIdleWindow`의 결정 로직(클립 선별·`ringDuration` 계산·`fireTime`)이 예보 쪽으로 옮겨간다.
      **남는 것은 검증과 로그뿐**(3-6). 함수를 지우지 말고 역할을 줄인다 — 실측 로그가 이 기능의 유일한 계기판이다.
- [ ] 5-2. `docs/AmbushVisibility/Plan_AmbushVisibility.md` **Step 4(E: 노출 시간)를 폐기 표시**한다.
      `maxRingExposure`를 올리자는 안이었는데 이 문서가 그 필드째 없앤다.
- [ ] 5-3. `DodgeDirector`의 클래스 주석에 **구조가 넷으로 갈린다**고 갱신(지금 셋으로 적혀 있다):
      ① 사전 접근 ② **예보** ③ 텔레그래프 ④ 원호 회피.

## Step 6 — 검증

- [ ] 6-1. 컴파일 + EditMode 120/120.
- [ ] 6-2. **로그로 노출 시간을 찍어 전부 `ringExposure`와 같은지** 확인한다(±0.02초).
      이 기능의 성공 조건이 그 한 줄이다.
- [ ] 6-3. `ringExposure`를 0.6 / 1.0 / 1.5로 바꿔가며 **발동 빈도가 안 변하는지** 확인(3-3의 주장).
      변하면 `RequiredLead`가 아직 공백을 보고 있다는 뜻이다.
- [ ] 6-4. 예보 창 끝 vs 실제 `OnIdleWindow`의 `end` 차이를 로그로 축적 — 계통 오차가 있으면 식이 틀린 것이다.
- [ ] 6-5. **일부러 미스**했을 때 링이 그대로 완주하고 회피가 성립하는지(4-3).
- [ ] 6-5-1. **경합 검증(4-1-2)** — 링이 뜬 뒤 기습자가 결투 상대로 뽑히는 일이 없는지.
      ⚠ 로그로 봐야 한다. `stageDistance`(2.5m)가 짧은 창의 `desired`와 겹치는 구간이 노려야 할 자리다 —
      우연히 안 나올 수 있으므로, `TakeTargetForWindow`가 고른 적이 `HasPendingAction`이었던 횟수를
      **수정 전에 먼저 세어** 구멍이 실재하는지 확인하고 넣는다.
- [ ] 6-6. 곡 마지막 엔트리 근처에서 예보가 없을 때 조용히 안 뜨는지(Research §5-4).
- [ ] 6-7. 곡 정지·재시작에서 예보가 남지 않는지(`OnAllPatternsCleared` → `Abort`).

---

## 범위 밖

- **아웃라인을 더 일찍 켜기** — `docs/AmbushVisibility/`의 후속 안(사전 접근에서 약한 아웃라인).
  이 문서가 링을 1초로 고정하면 그것만으로 충분할 수 있으므로, 넣고 본 다음 판단한다.
- **`minIdleWindow` 자체를 줄여 발동 빈도를 올리는 것** — 구르기가 서 있는 구간에 들어가야 한다는 요구는 안 바뀐다.
- **회피 판정 정밀도** — 링이 떠 있는 동안 아무 때나 성공이라는 규칙은 그대로다.
