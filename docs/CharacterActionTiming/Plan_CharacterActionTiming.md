# Plan — 캐릭터 액션 타이밍(뒷부분 잘림) 수정 (CharacterActionTiming)

근거: `docs/CharacterActionTiming/Research_CharacterActionTiming.md`

## 개요
액션 애니메이션 뒷부분 잘림을 두 갈래로 해결한다.
- **원인 1(회귀 버그)**: budget 기준을 실제 재생 시작(`Time.time`)으로 되돌린다 — 코드 수정.
- **원인 2(튜닝 한계)**: 실제 휘두르지 않는 선딜/후딜 구간을 **인스펙터 트리밍**(`AnimationStartOffset`/`AnimationDuration`)으로 제거해, 배속 상한(`maxAttackSpeed`)에 덜 걸리게 한다 — 데이터 튜닝 + 워크플로 확정.

---

## Step 1 — 원인 1 수정 (budget 기준 복원)  (완료) 적용됨
- [x] `PlayActionClip`의 budget을 `nextLastNodeTime - Time.time`으로 복원(기준을 `actionEndTime`과 일치시킴).
- [x] 트림된 `duration`은 유지(`clip.length`가 아니라 트리밍 결과 길이로 배속/종료 계산).
- [x] 불필요해진 `currentLastNodeTime` 인자를 시그니처/호출부에서 제거.
- [x] 컴파일 에러 없음 확인(`read_console`).
- 파일: `Assets/02. Scripts/Character/CharacterActionPlayer.cs`

## Step 2 — 트리밍 메커니즘 확정(원인 2 해법)  (완료)
- [x] `Pattern.AnimationStartOffset`(선딜 제거) / `AnimationDuration`(후딜 제거, 0이면 클립 끝까지) 이미 존재·배선.
- [x] `PlayActionClip`이 `startOffset`으로 재생 시작(`CrossFadeInFixedTime(..., startOffset)`), `duration`으로 배속/종료 계산.
- [x] 트림 데이터가 패턴 에셋에 저작되어 있음 확인(off 0.05~0.25 / dur 0.3~1.8).

## Step 3 — 트리밍 저작 워크플로 문서화  (완료)
- [x] `docs/!Guides/Guide_CharacterActionTrim.md` 작성:
  - `Pattern` 인스펙터에서 `SuccessAnimationClip` / `AnimationStartOffset` / `AnimationDuration` 의미와 조정 방법.
  - "선딜(준비 자세) = startOffset로 스킵, 실제 휘두름 = duration로 한정, 후딜(마무리) = 잘라냄" 가이드라인.
  - 배속 상한(`maxAttackSpeed`)과의 관계: `duration`이 짧을수록 촘촘한 구간에서 잘림 위험↓.

## Step 4 — 패턴별 트림 값 튜닝(밸런싱)  (대기) Play 모드 관찰 필요(사용자)
- [ ] Play 모드에서 촘촘한 구간(짧은 budget) 위주로 재생하며, 뒷부분 잘림이 남는 패턴을 식별.
- [ ] 해당 패턴의 `AnimationDuration`을 실제 휘두름 구간까지로 줄여 재확인.
- [ ] 그래도 남으면 `maxAttackSpeed` 상향 여부를 별도 판단(전역 튜닝이라 신중히).
- 주: 실제 타격감/잘림은 눈으로 봐야 판단 가능 → 자동 조정하지 않고 사용자 확인 후 값 조정.

## Step 5 — 검증  (대기) Play 모드 관찰 필요(사용자)
- [ ] 오토퍼펙트(디버그 입력)로 연속 패턴을 재생해 각 액션이 다음 액션 시작 전에 자연스럽게 마무리되는지 확인.
- [ ] 만료(입력 놓침) 구간에서도 뒷부분 잘림이 눈에 띄지 않는지 확인(원인 1 수정으로 δ 마진 확보됨).
- [ ] 곡 마지막 패턴(다음 없음, `nextLastNodeTime < 0`)은 1배속 전체(트림 범위) 재생 후 페이드되는지 확인.
- [x] 컴파일 클린(에러 0) 확인 완료.

---

## 범위 밖 (하지 않음)
- 배속 상한 자동 조정/동적화 없음(필요 시 별도 플랜).
- 애니메이터 스테이트/레이어 구조 변경 없음(듀얼 슬롯 유지).
- `PatternHandler` 판정/완료 타이밍 로직 변경 없음.

## 참고 — 타이밍 불변식
- `budget = nextLastNodeTime - Time.time` (재생 시작 기준)
- `speed  = min(duration / budget, maxAttackSpeed)`  (`duration > budget`일 때만)
- `actionEndTime = Time.time + duration / speed`  ← budget과 **같은 기준(Time.time)**
- 상한에 안 걸리면 `actionEndTime ≈ nextLastNodeTime` → 다음 액션 시작 직전에 마무리, 잘림 없음.
