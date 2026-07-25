# Research — ReleaseRecovery (공격 종료 → Run 복귀 블렌딩)

## 목표
**모든 공격 애니메이션**이 끝난 뒤 Run(달리기)으로 넘어가는 구간을 자연스럽게 만든다.
현재는 임팩트 자세에서 0.08초 만에 Run으로 튀어(popping), 베고 나서의 **마무리 동작(follow-through)이 전혀 보이지 않는다.**

> 이 문서는 기존 "연계 패턴 없을 때만 Release 재생" 범위를 **모든 공격 공통 복귀**로 확장해 통합한 개정판이다.

---

## 증상 (사용자 확인)
- **A. 팝핑** — 상체/팔 포즈가 툭 끊기며 순간이동하듯 Run으로 돌아온다.
- **C. 마무리 부재** — 베고 나서의 복귀 동작 없이 곧바로 달리기가 되어 동작이 잘린 느낌.
- (다리 위상 튐은 현재 문제로 인식되지 않음 → 범위 밖.)

---

## 현재 구조

### 애니메이터 (`Assets/05. Animations/Animator/PlayerAnimator.controller`)
- **Running Layer** — index 0(base layer). Unity는 base layer 웨이트를 항상 1로 취급(`DefaultWeight` 무시). 상태는 `Run`(`Sp_Run.fbx`) 하나뿐, 전이 없음. **어떤 스크립트도 이 레이어 웨이트를 건드리지 않는다.** Attack Layer 웨이트가 0이 되면 자연히 드러난다.
- **Attack Layer** — index 1, `BlendingMode = Override`, **마스크 없음(전신)**, `DefaultWeight = 0`. 상태:
  - `Release`(default, 클립 `Release.anim`, 길이 2.333초, 논루프) — 나가는 전이 없음.
  - `Attack_A`(placeholder `Placeholder_A`, SpeedParam `AttackSpeed`) → `Release` 전이(`HasExitTime`, `ExitTime = 1.0`, `TransitionDuration = 0.15`, fixed).
  - `Attack_B` — 동일 구성.
- 파라미터: `AttackSpeed`(float, 기본 1).

### `CharacterActionPlayer` 현재 흐름
- 구독: `OnJudgeTargetBegan`(성공 애니 예약), `OnJudgeTargetFirstMiss`(힛 재생). `OnPatternComplete`는 미구독.
- `PlaySlot(clip, startOffset, dur, speed)`:
  1. 듀얼 슬롯(A/B 교대)의 placeholder에 클립 주입
  2. `AttackSpeed = speed`
  3. **Attack Layer 웨이트 = 1 (즉시 점프)**
  4. `actionEndTime = now + dur / speed`
  5. `CrossFadeInFixedTime(state, 0.05, layer, startOffset / speed)`
- `Update()`:
  - `Time.time < actionEndTime`이면 웨이트 1 유지
  - 이후 `layerFadeOutDuration`(**0.08초**)로 웨이트를 0까지 `MoveTowards`(선형)
  - 별도로, Attack_A/B에서 Attack이 아닌 스테이트로 빠지는 전이 중이면 `AttackSpeed`를 1로 되돌림

### 트림 데이터 (`Pattern` ScriptableObject)
- `AnimationStartOffset` / `AnimationDuration` / `AnimationSpeed`.
- 예: `Pattern(0, 4, 8)` → `startOffset 0.343`, `duration 0.781`, `speed 1.5` → **실제 노출 0.52초**.
- 트림의 목적: **캐릭터가 앞의 오브젝트를 타이밍에 맞춰 베는 연출 정렬**. 그 이후 구간은 연출로 흘려보내도 무방하다(사용자 확정).

---

## 핵심 발견

### 1. tail(마무리 구간)은 **이미 재생되고 있다. 안 보일 뿐이다.**
`PlaySlot`은 클립 **전체**를 스테이트에 물려 재생한다. `actionEndTime`은 재생을 멈추는 시각이 아니라 **레이어 웨이트를 떨구기 시작하는 시각**일 뿐이다.
따라서 Attack_A/B는 트림 끝 이후로도 계속 진행하다 `ExitTime = 1.0`으로 `Release`까지 흘러가지만, 그 전부가 **웨이트 0 뒤에서** 일어난다.

| 클립 | 전체 길이 | 트림 구간 예 | 버려지는 tail |
|---|---|---|---|
| `Swipe_1To9` | 2.267s | 0.343 ~ 1.125 | **약 1.14s** |
| `Swipe_3To7` | 2.0s | (패턴별) | 약 0.9s |
| `Swipe_4To6` | 2.6s | (패턴별) | 약 1.5s |
| `Swipe_6To4` | 2.4s | (패턴별) | — |
| `Swipe_8To2` | 2.167s | (패턴별) | — |
| `Swipe_9To1` | 2.0s | (패턴별) | — |

→ **증상 C의 직접 원인.** 새 클립을 만들 필요 없이, 이미 존재하는 마무리 동작을 노출시키기만 하면 된다.

### 2. 팝핑의 원인은 블렌드 *방식*이 아니라 **블렌드 대상과 시간**이다.
지금은 *임팩트 순간의 포즈*에서 *0.08초* 만에 Run으로 넘어간다. 어떤 블렌딩 수단을 쓰든 이 두 조건이면 어색하다.
반대로 *마무리 동작을 거친 포즈*에서 *0.25~0.3초*로 넘기면 레이어 웨이트 페이드만으로 충분하다.

### 3. tail은 **배속이 걸린 채** 재생된다.
`AttackSpeed`는 `PlaySlot`에서 설정되고, 현재 코드는 "Attack → 비Attack 전이 중"에만 1로 되돌린다. 그 전이는 클립 normalizedTime 1.0에서야 시작하므로, **tail 대부분이 배속(최대 2.5배) 상태로 지나간다.** 마무리 동작은 정상 속도가 자연스럽다.

### 4. 크로스 레이어 상태 전이는 불가능하다.
`Release`(Attack Layer) → `Run`(Running Layer)을 애니메이터 전이로 만들 수 없다. **Attack Layer 웨이트를 0으로 블렌드해 base Run을 드러내는 방식만 가능하다.** base는 항상 웨이트 1이므로 이것으로 자동 크로스블렌드가 성립한다.

### 5. 다음 액션의 시작 시각은 미리 알 수 있다.
`HandleJudgeTargetBegan`이 다음 판정 대상의 성공 애니를 예약하며 `pendingScheduleStart`(재생 시작 예정 시각)를 계산해 둔다. 따라서 **"곧 다음 베기가 오는가"를 새 이벤트 구독 없이 판단할 수 있다** — 연계 시 레이어를 내리지 않고 유지하는 판단의 근거가 된다.

다만 다음 `PlaySlot`은 웨이트를 1로 **즉시 점프**시킨다. 복귀가 진행 중일 때 이 점프가 일어나면 진입 측 팝핑이 된다.

---

## 용어 정리
임의 조어를 쓰지 않고 아래 표준 용어를 사용한다.
- **follow-through** — 임팩트 이후 관성으로 이어지는 마무리 동작(애니메이션 12원칙).
- **recovery** — 액션게임 프레임 데이터의 startup / active / **recovery** 중 마지막 구간. 폴더명 `ReleaseRecovery`가 이 계열.
- **blend-out** — 레이어/노드가 웨이트를 0으로 내리며 빠지는 구간.

이 작업은 결국 **"recovery 구간을 노출시키고 blend-out과 겹쳐 Run으로 녹인다"**로 요약된다.

---

## 채택 방식과 기각안

**채택: 레이어 웨이트 blend-out (현행 경로의 시간 조정).**
코드(`CharacterActionPlayer.cs`)만 바뀌고 애니메이터는 무수정. 이미 있는 경로의 타이밍을 고치는 것이라 회귀 위험이 가장 낮다.

**대안(보류): Attack Layer 안에 `Run` 스테이트를 추가해 상태 전이.**
`Attack_A/B → Run`을 `TransitionDuration 0.25`로 잇는 방식. 결과는 웨이트 blend-out과 사실상 동일하고, 블렌드 커브를 애니메이터에서 시각적으로 잡을 수 있다는 점만 다르다. 애니메이터 수정과 Run 클립 이중 배치가 필요하므로, **채택안 튜닝이 실패할 경우의 대안**으로만 남긴다.

---

### 6. 연계 간격 실측 — 대부분의 공격은 "거의 바로" 이어진다.
`Dreamer_lv10`(엔트리 112개)에서 `pendingScheduleStart − 직전 actionEndTime`을 채보·트림값으로 계산:

- 최소 0.400 / **중앙값 0.679** / 최대 2.000초
- **95%(105/111)가 1.0초 이내**, 값들이 `0.40 ~ 0.90`에 밀집한 뒤 `1.2 → 1.6 → 2.0`으로 끊긴다

→ **연계(콤보)가 주 경로이고 Run 복귀는 곡당 6회 남짓의 예외 경로다.** 분기 임계값은 1.0초 부근이 자연스럽다.
(`actionEndTime`을 직전 마지막 온셋으로 근사한 계산이다. 배속 상한에 걸리는 케이스는 실제 간격이 조금 더 짧다.)

---

## 요구사항 (사용자 확정)
- **연계되는 다음 공격이 있으면** 그 공격으로 자연스럽게 이어져야 한다(콤보 공격처럼).
- **없으면** Run으로 복귀한다.

→ 복귀 경로는 하나가 아니라 **연계 유무로 갈리는 두 경로**다. 분기 근거는 §5의 `pendingScheduleStart`.

## 미해결 설계 질문 (Plan에서 `>>>`로 결정)
1. recovery 노출 시간과 blend-out 시간의 기본값.
2. 공격 간 크로스페이드 시간(현재 0.05는 사실상 컷 전환).
3. blend 커브를 선형으로 둘지 ease(SmoothStep)로 둘지.
4. 실패(피격) 후에도 동일 복귀를 적용할지(구조상 자연 포함).

---

# 추가 조사 — "Release가 너무 자주 나온다" (개정 2의 근거)

## 증상 (사용자 확인)
- `comboLinkWindow`를 **10초**로 올려 사실상 모든 연결을 "연계"로 만들었는데도 **Release가 계속 재생된다.**
- 애니메이터 창 관측: `Attack` 스테이트 뒤에 `Release`가 나오고, **그다음** 레이어 웨이트가 내려간다.

## 7. 근본 원인 — 애니메이터의 `ExitTime` 전이는 코드가 막을 수 없다

`Assets/05. Animations/Animator/PlayerAnimator.controller`:

| 출발 스테이트 | 전이 fileID | 목적지 | 조건 |
|---|---|---|---|
| `Attack_A` (:32, :35-36) | `7410374101304509780` (:229-250) | `Release` (`1131339499896222762`) | `m_HasExitTime: 1`, `m_ExitTime: 1`, duration 0.15 |
| `Attack_B` (:171, :174-175) | `7638834329385509203` (:251-268) | `Release` (동일) | `m_HasExitTime: 1`, `m_ExitTime: 1`, duration 0.15 |

- `comboLinkWindow`는 `CharacterActionPlayer.Update()`의 **`TriggerRelease()` 호출 여부만** 제어한다(`:172-181`). 위 전이는 애니메이터가 클립 끝에서 자체적으로 발동시키므로 **코드 경로와 완전히 무관하다.**
- 개정 1의 콤보 경로는 트림 끝부터 다음 공격 시작(`pendingScheduleStart`)까지 **웨이트 1을 유지한 채 아무것도 하지 않는다.** 그 사이 공격 클립이 자기 끝(tail 0.9~1.5초)에 도달하면 `ExitTime` 전이가 발동해 **웨이트 1 상태로 Release가 그대로 보인다.**
- 즉 증상은 튜닝 값 문제가 아니라 **Release 진입 경로가 둘(코드 / 애니메이터)이고, 그중 하나가 통제 밖**이라는 구조 문제다.
- 개정 1의 Plan도 이 가능성을 리스크로 적어 두었다(Plan :172, :176 — "tail이 짧은 클립에서 ExitTime 전이로 Release까지 흘러가 예상보다 정적으로 보일 수 있다"). 실제로는 tail이 긴 클립에서도 연계 간격이 tail보다 길면 발생한다.

## 8. 웨이트가 내려가는 케이스

`comboLinkWindow = 10`에서도 웨이트가 내려갔다면 `IsLinkedToNextAction()`(`:225`)의 `hasPending`이 false였다는 뜻이다. 성립 조건:
- 다음 판정 대상이 아직 승계되지 않음(`RaiseJudgeTargetBegan`은 패턴 완료와 같은 프레임 — `PatternHandler.cs:607`),
- 다음 패턴의 `SuccessAnimationClip`이 비어 있음(`:275-276`에서 `hasPending = false`로 조기 반환),
- 첫 미스로 예약이 취소됨(`:316`).

개정 2에서는 **연계일 때도 웨이트를 내리므로** 이 분기는 "웨이트를 내리는가"가 아니라 **"Release를 거치는가"**만 결정하게 된다. 위 오판 케이스의 영향 범위가 그만큼 줄어든다.

## 9. 개정 2의 요구사항 (사용자 확정)
- **연계 있음** — 공격 사이에 Attack Layer 웨이트를 내려 **Run을 노출**하고, 다음 공격에서 다시 올린다. **Release는 재생하지 않는다.**
- **연계 없음** — Release를 재생한 뒤 웨이트를 내려 Run으로 복귀한다. (개정 1과 동일)

→ 개정 1의 전제("연계 사이에 Run이 비집고 들어오면 안 된다")가 뒤집혔다. Run 노출이 **의도된 연출**이 된다.

## 10. 개정 2가 새로 만드는 문제 — 짧은 간격에서의 웨이트 깜빡임

개정 1 Step 8의 런타임 실측(`Dreamer_Lv2`)에서 관측된 연계 간격:

| 간격(초) | 비고 |
|---|---|
| 0.107 / 0.133 / 0.134 | **blendOut(0.30) + blendIn(0.08)보다 짧다** |
| 0.402 / 0.674 / 0.828 | Run 노출에 충분 |

- 앞 3건은 웨이트를 내리기 시작하자마자 다시 올려야 해서, **Run이 보이지도 않는 채 웨이트만 출렁인다.** 정적 계산(`Dreamer_lv10`)의 최소 간격 0.400과 달리, 배속 상한(씬 값 3.5)에 걸린 케이스에서 실제 간격이 훨씬 짧아진 결과다.
- → **최소 노출 간격 가드가 필요하다.** 간격이 일정 값 미만이면 웨이트를 내리지 않고 유지한다(개정 1의 콤보 경로와 동일하게 동작).

---
---

# 추가 조사 — 경로별 Run 클립 분리 (개정 3의 근거)

## 요구사항 (사용자 확정)
- **경로 ②(연계 O · 간격 여유, Release 생략하고 콤보 사이 잠깐 달리는 구간)** → **`Sprint_HS`** 클립.
- **경로 ③(연계 X, 곡 공백 → Release 완주 후 복귀)** → **현재 `Run`** 클립 유지.
- 즉 "빠른 연계가 이어지는 동안엔 Sprint, 곡이 잦아들어 쉬는 구간엔 Run".

## 11. 현재 구조 — 두 경로가 같은 base 스테이트 하나를 드러낸다
- `CharacterActionPlayer`는 **Attack Layer(index 1) 웨이트만** 0으로 내린다. base `Running Layer`(index 0)는 손대지 않는다.
- base `Running Layer`에는 `Run` 스테이트 하나뿐이며 default 스테이트다(`m_Motion` = `Run.anim`, guid `dadb429571d0b174faf839367851eb26`).
- 따라서 경로 ②·③ 모두 blend-out 시 **동일한 `Run`**이 드러난다. 클립을 경로별로 다르게 하려면 base 레이어를 **상황에 맞게 전환**해야 한다(클립 하나 교체로는 불가능).

## 12. 클립 자산
- `Sprint_HS.anim` — `Assets/05. Animations/Clip/Sprint_HS.anim`, guid `c6ce09bd2c3f38645b61adeccab81781`. 원본 FBX는 `Assets/Samurai_Katana/Animations/Lock-On/Sprint_HS.FBX`.
- 연속 콤보 구간에서 끊기지 않으려면 **Loop Time**이 켜져 있어야 한다(확인 필요).

## 13. 전환 방식 — 코드 CrossFade (파라미터/전이 배제)
- 기존 코드는 ExitTime 전이를 의도적으로 제거하고 `CrossFadeInFixedTime`으로 스테이트 진입을 코드가 일원화하는 방향이다(개정 1 Step 11~12). base 레이어 전환도 같은 방식으로 통일한다.
- base 레이어 CrossFade는 Attack 레이어 웨이트가 아직 남아 있는(=base가 가려진) 동안 일어나므로, 커버 아래에서 Run↔Sprint 포즈 블렌딩이 끝나 전환이 눈에 띄지 않는다.
- 중복 진입 방지: 목표 스테이트가 현재와 다를 때만 CrossFade하도록 `currentBaseStateHash`를 추적한다(매 프레임 `ApplyBlendOut`에서 호출돼도 1회만 전환).
