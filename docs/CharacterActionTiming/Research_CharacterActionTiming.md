# Research — 캐릭터 액션 타이밍(뒷부분 잘림) (CharacterActionTiming)

## 문제
패턴이 연속으로 이어질 때 캐릭터 액션(베기) 애니메이션의 **뒷부분이 잘린다.** 의도는 "애니메이션 시작 ~ 다음 판정 사이 창에 맞춰 배속 재생해 안 잘리게" 하는 것.

## 관련 파일
- `Assets/02. Scripts/Character/CharacterActionPlayer.cs` — 액션 재생/배속.
- `Assets/02. Scripts/Pattern/Pattern.cs` — `SuccessAnimationClip`, `AnimationStartOffset`, `AnimationDuration`(트리밍 데이터).
- `Assets/02. Scripts/UI/PatternHandler.cs` — `OnPatternComplete(PatternCompletionInfo)` 발행.
- `Assets/02. Scripts/Pattern/PatternCompletionInfo.cs` — `LastNodeTime`, `NextLastNodeTime`.

## 재생/잘림 메커니즘
- 패턴 N이 **완료되는 실제 순간**(`Time.time`, = Sₙ)에 애니메이션 N이 재생을 시작한다(`CrossFadeInFixedTime`).
- 다음 애니메이션 N+1의 CrossFade가 들어오는 순간(Sₙ₊₁)에 애니메이션 N이 **끊긴다**(단일/듀얼 슬롯 모두 다음 재생이 현재를 덮음).
- 따라서 안 잘리려면: `duration / speed ≤ Sₙ₊₁ − Sₙ`.
- `actionEndTime = Time.time + duration / speed` (이 시각 후 레이어 웨이트를 0으로 페이드).

## 근본 원인 1 — budget 기준 시각 불일치 (회귀 버그)
직전 수정(Unity AI)에서 budget 기준이 바뀌었다:
```
원본(안전): budget = nextLastNodeTime - Time.time          ← 실제 시작(Sₙ) 기준
변경(버그): budget = nextLastNodeTime - currentLastNodeTime ← 채보상 노드시각 기준
```
- `speed`는 `currentLastNodeTime` 기준으로 계산되는데 `actionEndTime`은 `Time.time` 기준 → **두 기준이 다르다.**
- 패턴은 자기 마지막 노드 채보시각(`currentLastNodeTime`)보다 **항상 조금 늦게 완료**된다:
  - 만료(expire): `Deadline = LastNodeTime + goodWindow(0.1s)` → 최대 +0.1s
  - 정상/오토퍼펙트: 프레임 지연 등으로 ≥ 0
- 완료 지연 `δ = Time.time − currentLastNodeTime (≥ 0)`이면 배속 미적용 시:
  `actionEndTime = (currentLastNodeTime + δ) + (nextLastNodeTime − currentLastNodeTime) = nextLastNodeTime + δ`
  → 애니메이션은 `nextLastNodeTime + δ`까지 재생하려 하지만 다음 CrossFade는 `Sₙ₊₁ ≈ nextLastNodeTime`에 들어옴 → **δ만큼 뒷부분 잘림.** 지연이 클수록(만료 구간) 더 잘림.
- 원본 공식은 `actionEndTime = Time.time + (nextLastNodeTime − Time.time) = nextLastNodeTime`으로 끝나 안전 마진이 있었다. 기준 변경이 이 마진을 없앴고, 사용자의 의도("애니메이션 시작 기준")와도 원본이 맞다.

## 근본 원인 2 — 배속 상한(`maxAttackSpeed = 2.5`)
- budget을 올바르게 고쳐도 `duration/budget > 2.5`면 상한에 걸려 창에 못 들어가 잘린다.
- 트림된 길이가 긴 패턴이 촘촘한 구간에 오면 발생. 로직 버그가 아니라 **튜닝 한계.**

## 트리밍 데이터 현황 (원인 2 해법의 근거)
`Pattern` 에셋에 트림 값이 이미 저작되어 있다(예):
- `Pattern(0,4,8)` off 0.25 / dur 0.6, `Pattern(4)` 0.15 / 0.7, `Pattern(5,2)` 0.05 / 0.3,
- `Pattern(6,3,0)` 0.1 / 1.2, `Pattern(8,5,2,1)` 0.1 / 1.8 …
- 즉 트리밍 메커니즘(선딜/후딜 제거)은 **이미 배선·저작되어 동작 중**이며, 재생 길이를 줄여 배속 부담을 낮추는 정공법이다. 긴 클립(1.2~1.8s)은 여전히 상한에 걸릴 수 있어 개별 튜닝이 필요하다.

## 제약 / 방향
- 원인 1은 명백한 회귀 → **budget 기준을 `Time.time`으로 되돌린다**(트림된 `duration` 유지).
- 원인 2는 **트리밍(비-휘두름 구간 제거)으로 흡수**한다 — 인스펙터에서 패턴별 `AnimationStartOffset`/`AnimationDuration`을 조정해 실제 휘두르는 구간만 남긴다.
