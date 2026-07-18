# Plan — 캐릭터 액션 출력 타이밍 전면 재설계 (ActionTimingRedesign)

근거: `docs/ActionTimingRedesign/Research_ActionTimingRedesign.md`

## 개요
성공(베기) 애니를 **패턴 입력 도중 조기 재생**해 **마지막 노드 판정 시각에 끝나도록** 하고, 입력 중 **첫 미스 시 힛 애니로 대체(성공 애니 취소)**한다.
이를 위해 `PatternHandler`에 "판정 대상 시작"·"첫 미스" 이벤트를 추가하고, `CharacterActionPlayer`의 재생 로직을 스케줄 기반으로 교체한다.

핵심 산식:
- `dur` = 트림 재생시간, `baseSpeed = Pattern.AnimationSpeed`(기본 1, 배속 하한).
- `playTime = dur / baseSpeed`, `scheduleStart = max(lastNodeTime − playTime, firstNodeTime)`.
- 시작 순간 `speed = clamp(dur / max(lastNodeTime − Time.time, ε), baseSpeed, max(maxAttackSpeed, baseSpeed))`
  → 구간 넉넉하면 baseSpeed, 짧으면 자동 상향(상한 캡).

---

## Step 1 — Pattern: 배속 필드 추가
- [x] `[SerializeField] private float animationSpeed = 1f;` + `public float AnimationSpeed => Mathf.Max(animationSpeed, 0.01f);` 추가.
- [x] 툴팁: "이 패턴 베기의 기본 배속(하한). 패턴 입력 구간이 짧으면 자동으로 더 배속된다."
- 파일: `Assets/02. Scripts/Pattern/Pattern.cs`.

## Step 2 — ActivePattern: FirstNodeTime 추가
- [x] `public float FirstNodeTime => StartTime + inputTimes[0];` 추가.

## Step 3 — 이벤트 페이로드 타입 신설
- [x] `JudgeTargetInfo`(readonly struct, `PatternSpace`) 생성: `Pattern Template`, `float FirstNodeTime`, `float LastNodeTime`.
- 파일: `Assets/02. Scripts/Pattern/JudgeTargetInfo.cs`.
- 주: `Template.AnimationSpeed`로 배속 접근하므로 별도 필드 불필요.

## Step 4 — PatternHandler: 이벤트 2개 추가/발행
- [x] `public event Action<JudgeTargetInfo> OnJudgeTargetBegan;`
- [x] `public event Action OnJudgeTargetFirstMiss;`
- [x] 발행 헬퍼 `RaiseJudgeTargetBegan()`: 현재 `JudgeTarget`이 있으면 `OnJudgeTargetBegan(new JudgeTargetInfo(target.Template, target.FirstNodeTime, target.LastNodeTime))`.
  - 호출 지점: `SetPattern()`의 `becomesJudgeTarget` 분기, `CompletePattern()`의 승계 직후(다음 대상이 있을 때). `RefreshJudgeTargetVisuals()`와 나란히.
- [x] `AddPattern()`에서 **첫 미스 전이 감지 후 발행**:
  - 오답 인덱스 분기: `MarkIncorrect()` 직전 `bool wasCorrect = target.AllCorrect;` → `MarkIncorrect()` → `if (wasCorrect) OnJudgeTargetFirstMiss?.Invoke();`
  - 타이밍 Miss 분기(`result == Miss`): 동일 패턴.
- 주: 판정/스폰/드래그 로직 자체는 불변, 이벤트 발행만 추가.

## Step 5 — CharacterActionPlayer: 구독 교체
- [x] `OnEnable/OnDisable`에서 `OnPatternComplete` 구독 제거, `OnJudgeTargetBegan`·`OnJudgeTargetFirstMiss` 구독으로 교체.
- [x] 내부 상태 필드: `pendingSuccess`(clip, startOffset, dur, baseSpeed, scheduleStart, lastNodeTime 보유; null 가능), `bool missedThisTarget`.

## Step 6 — 성공 애니 스케줄/시작
- [x] `HandleJudgeTargetBegan(JudgeTargetInfo info)`:
  - `missedThisTarget = false;`
  - `clip = info.Template.SuccessAnimationClip;` null이면 `pendingSuccess = null; return;`
  - `startOffset = info.Template.AnimationStartOffset;`
  - `dur = info.Template.AnimationDuration > 0 ? info.Template.AnimationDuration : clip.length − startOffset;`
  - `baseSpeed = info.Template.AnimationSpeed;`  `playTime = dur / baseSpeed;`
  - `scheduleStart = max(info.LastNodeTime − playTime, info.FirstNodeTime);`
  - `pendingSuccess = { clip, startOffset, dur, baseSpeed, scheduleStart, lastNodeTime = info.LastNodeTime };`
- [x] `Update()`에서:
  - 기존 배속 해제/레이어 페이드 로직 유지.
  - `if (pendingSuccess != null && !missedThisTarget && Time.time >= pendingSuccess.scheduleStart)` →
    `float needed = dur / max(lastNodeTime − Time.time, ε);`
    `float speed = clamp(needed, baseSpeed, max(maxAttackSpeed, baseSpeed));`
    `PlaySlot(clip, startOffset, dur, speed);` → `pendingSuccess = null;`

## Step 7 — 첫 미스 → 힛 + 성공 취소
- [x] `HandleJudgeTargetFirstMiss()`:
  - `if (missedThisTarget) return;` (방어)
  - `missedThisTarget = true; pendingSuccess = null;` (예약 취소)
  - `AnimationClip hit = NextHitClip(); if (hit != null) PlaySlot(hit, 0f, hit.length, 1f);` (진행 중 성공 애니가 있으면 크로스페이드로 대체 → 원래 베기 안 나옴)

## Step 8 — 재생 프리미티브 정리(PlaySlot)
- [x] 기존 `PlayActionClip`을 `PlaySlot(AnimationClip clip, float startOffset, float dur, float speed)`로 일반화:
  - 듀얼 슬롯 교대, 오버라이드 클립 주입, `SetFloat(AttackSpeed, speed)`, `SetLayerWeight(1)`, `actionEndTime = Time.time + dur/speed`, `CrossFadeInFixedTime(state, xfade, layer, startOffset)`.
  - 배속/버짓 산식은 호출부(Step 5/6)로 이동 — PlaySlot은 "주어진 speed로 재생"만.

## Step 9 — 컴파일 & 스모크 검증
- [x] `read_console` 에러 0 확인.
- [ ] Play 모드에서 오토퍼펙트 연속 재생 시 각 성공 애니가 **마지막 노드 근처에서 끝나는지** 확인(육안 — 사용자).
- [ ] 일부러 미스 시 **힛이 즉시 나오고 성공 베기가 안 나오는지** 확인(육안 — 사용자).

---

## 검증 시나리오 (구현 후, 사용자 육안)
1. 성공만: 각 패턴의 베기가 입력 도중 시작해 마지막 노드에서 마무리.
2. 짧은 입력구간 패턴: 첫 노드에서 시작해 배속으로 마지막 노드에 정렬.
2-1. 패턴 `AnimationSpeed`를 2로 지정: 구간이 넉넉하면 2배속으로 재생(더 늦게 시작), 구간이 짧으면 2배속 이상으로 자동 상향.
3. 중간 미스: 미스 순간 힛 재생, 그 패턴 베기 취소. 이후 추가 미스는 힛 반복 안 함.
4. 미스 후 남은 노드 정타: 그 패턴은 여전히 베기 안 나옴(첫 미스로 취소됨).
5. 곡 마지막 패턴: 정상적으로 마지막 노드에서 끝나고 페이드.

## 범위 밖 (하지 않음)
- 입력 판정/노드 스폰/드래그 로직 변경 없음(이벤트 발행만 추가).
- `OnPatternComplete` 제거 안 함(EffectManager 등 유지). CharacterActionPlayer만 사용 중단.
- 힛 애니 트림/정렬 없음(즉시 1배속). 필요 시 별도 플랜.
- 애니메이터 스테이트/레이어 구조 변경 없음(듀얼 슬롯 유지).
