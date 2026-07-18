# Research — 캐릭터 액션 출력 타이밍 전면 재설계 (ActionTimingRedesign)

## 목표(새 설계)
캐릭터 액션(베기) 출력 시점을 **패턴 완료 시점 → 패턴 입력 도중**으로 옮긴다.
- 성공(베기) 애니메이션이 **패턴 입력 시간 안에** 재생된다.
- **마지막 노드 판정 시각에 애니메이션이 끝나도록**, `시작 = 마지막노드시각 − 재생시간` 지점에서 조기 시작한다.
- 입력 도중 **미스가 나면 그 순간 힛(Hit) 애니메이션을 재생**하고, **예약/진행 중이던 성공 애니메이션은 취소**한다(원래 나올 베기 안 나옴).

### 확정된 설계 결정
1. **재생시간 > 입력구간(첫노드~마지막노드)인 경우**: 시작을 **첫 노드로 클램프**하고 **배속**으로 압축해 여전히 마지막 노드에 끝을 맞춘다. (배속 상한 `maxAttackSpeed` 유지)
2. **미스 기준**: 타이밍 Miss + 오답 인덱스 **둘 다** 힛 트리거(둘 다 `AllCorrect`를 깬다).
3. **미스 반복**: **첫 미스에만** 힛 재생 + 성공 애니 취소. 이후 미스는 무시.
4. **재생 중 미스**: 이미 재생 중이던 베기를 힛이 **즉시 끊는다**(크로스페이드).
5. **패턴별 배속 옵션 + 자동 배속**: `Pattern`에 기본 배속(`AnimationSpeed`, 기본 1)을 둔다. 그 배속으로 재생하되, 그 배속으로도 패턴 입력 구간에 안 들어가면 들어가도록 **자동으로 더 배속**한다. 즉 **배속 하한 = 패턴 지정 배속**, 구간이 짧으면 그 위로 자동 상향(상한 `maxAttackSpeed`).

## 현재 구조 분석

### CharacterActionPlayer (현행)
- `OnPatternComplete(PatternCompletionInfo)` **하나만** 구독.
- 완료 시 `AllCorrect`면 `Pattern.SuccessAnimationClip`, 아니면 Hit 클립을 재생.
- `PlayActionClip(clip, pattern, nextLastNodeTime)`: 트림 구간(`AnimationStartOffset`/`AnimationDuration`)만 재생하고, `nextLastNodeTime - Time.time` 창에 맞춰 배속(상한 `maxAttackSpeed`).
- 듀얼 슬롯(Attack_A/Attack_B) 교대 + `CrossFadeInFixedTime`으로 팝핑 방지. Attack Layer 웨이트: 재생 중 1, `actionEndTime` 후 0으로 페이드.
- **문제**: 완료 시점 재생이라 "입력 도중 재생/마지막 노드 정렬/미스 시 즉시 힛"을 표현 못 함.

### PatternHandler (현행 이벤트 표면)
- `OnJudged(JudgementResult, int)` — **정답 인덱스 판정 때만** 발행. 오답 인덱스 분기는 `OnJudged`를 발행하지 않고 조기 return.
- `OnPatternComplete(PatternCompletionInfo)` — 완료(완주/만료) 시.
- 낙하노드 관련 이벤트들.
- **판정 대상이 바뀌는 순간(승계/최초)**을 알리는 이벤트가 없다. → 새로 필요.
- **첫 미스 순간**을 알리는 신호가 없다(오답은 아예 이벤트 없음). → 새로 필요.

### ActivePattern (현행)
- `StartTime`, `LastNodeTime`(= StartTime + inputTimes[last]), `GetInputTime(0)`(첫 노드 상대시각), `ExpectedTime`, `AllCorrect`, `MarkIncorrect()`.
- **첫 노드 절대시각(FirstNodeTime)** 프로퍼티 없음 → 추가 필요(= StartTime + GetInputTime(0)).

### 판정 대상 승계 지점
- 최초: `SetPattern()`에서 `becomesJudgeTarget`일 때 `RefreshJudgeTargetVisuals()`.
- 승계: `CompletePattern()`에서 앞 패턴 제거 후 `RefreshJudgeTargetVisuals()`.
- → 이 두 지점에서 "새 판정 대상 시작" 이벤트를 발행하면 된다.

### 입력(판정) 비겹침 보장
- 채보 규칙상 **입력 시각은 겹치지 않는다**: 패턴 N+1의 첫 노드 ≥ 패턴 N의 마지막 노드. 그리고 N+1은 N 완료(≈N.lastNode) 시 판정 대상이 된다.
- 따라서 N+1이 판정 대상이 되는 시점(≈N.lastNode)은 N+1.firstNode 이하 → **성공 애니 시작 예약시각(= max(N+1.lastNode − dur, N+1.firstNode))은 항상 미래**다. 스케줄링이 안전하다.

## 새 이벤트 설계(추가 필요)
1. **`OnJudgeTargetBegan(JudgeTargetInfo)`** — 새 판정 대상이 선두가 될 때. 페이로드: `Pattern Template`, `float FirstNodeTime`, `float LastNodeTime`(절대시각). 소비자가 성공 애니 시작을 예약.
2. **`OnJudgeTargetFirstMiss()`** — 판정 대상의 `AllCorrect`가 처음 true→false로 바뀌는 순간(오답/타이밍Miss 공통). 패턴당 1회. 소비자가 힛 재생 + 성공 애니 취소.
   - 발행 지점: `AddPattern`의 오답 분기와 타이밍 Miss 분기 각각에서 `MarkIncorrect` 직전 `wasCorrect = target.AllCorrect` 확인 후 전이면 발행.

## 타이밍 산식(성공 애니)
- `dur` = 트림 재생시간(`AnimationDuration>0`이면 그 값, 아니면 `clip.length − AnimationStartOffset`).
- `baseSpeed` = 패턴 지정 배속(`Pattern.AnimationSpeed`, 기본 1). **배속의 하한**이다.
- `playTime = dur / baseSpeed` — 지정 배속으로 재생했을 때의 소요 시간. 이 값을 기준으로 조기 시작 예약.
- `scheduleStart = max(lastNodeTime − playTime, firstNodeTime)`.
- 실제 시작 시(Time.time ≥ scheduleStart) **남은 시간 기준으로 배속 재계산**해 마지막 노드에 정렬:
  - `needed = dur / max(lastNodeTime − Time.time, ε)`
  - `speed = clamp(needed, baseSpeed, max(maxAttackSpeed, baseSpeed))`
  - 구간이 넉넉하면(클램프 안 됨) `speed = baseSpeed` → 지정 배속 그대로.
  - 구간이 `playTime`보다 짧으면 `needed > baseSpeed` → **자동으로 더 배속**. 상한 `maxAttackSpeed` 초과분은 캡(약간 오버슈트, 엣지). 단 지정 배속이 상한보다 크면 지정 배속이 우선.
- `CrossFadeInFixedTime(state, xfade, layer, AnimationStartOffset)`로 트림 시작점부터, `actionEndTime = Time.time + dur/speed`.

## 힛 애니(미스 시)
- 첫 미스 순간 즉시 `NextHitClip()`을 1배속 재생(트림 없음, 기존 hitClips 배열). 진행 중 성공 애니가 있으면 크로스페이드로 대체.
- `OnJudgeTargetFirstMiss`가 패턴당 1회만 오므로 반복 재생 없음(결정 3 충족).

## 영향 범위 / 제약
- 변경: `Pattern`(`AnimationSpeed` 배속 필드 추가), `PatternHandler`(이벤트 2개 추가·발행), `ActivePattern`(FirstNodeTime 추가), `CharacterActionPlayer`(구독·재생 로직 전면 교체), 새 페이로드 타입 `JudgeTargetInfo`(배속 산식에 `Template.AnimationSpeed` 사용).
- `OnPatternComplete`는 유지(EffectManager 등 다른 소비자 사용). **CharacterActionPlayer는 더 이상 성공/힛을 완료 시점에 재생하지 않는다.**
- 트리밍 시스템(`AnimationStartOffset/Duration`, ClipTrimTool)과 그대로 연동 — `dur`가 트림 길이.
- 판정/스폰/드래그 등 입력 판정 로직 자체는 바꾸지 않는다(이벤트 발행만 추가).
