# Plan: 베는 임팩트 프레임 정렬 (SliceImpactFrame)

근거: [Research_SliceImpactFrame.md](Research_SliceImpactFrame.md)

## 목표

베기 클립의 **정렬 앵커를 "트림 끝" → "베는 프레임(임팩트 프레임)"으로 옮긴다.**
임팩트 프레임은 `SliceTargetDirector`가 쓰는 표적 절단 시각과 **같은 식**으로 정렬되어,
칼날이 지나가는 순간과 표적이 갈라지는 순간이 구조적으로 일치한다.
임팩트 이후 남은 트림 구간(잔여 애니메이션)은 그대로 뒤에 이어 재생된다.

## 확정된 설계 결정

| 결정 | 내용 |
|---|---|
| 정렬 앵커 | **표적 임팩트 시각** = `Deadline + Pattern.SliceTargetImpactOffset` (표적 쪽 식 그대로 재사용) |
| 표적 절단 시각 | **변경 없음.** `SliceTargetDirector`는 손대지 않는다 |
| 잔여 구간 배속 | **트림 끝까지 같은 배속 유지.** 트림 끝에서 기존대로 `AttackSpeed = 1` 복귀 |
| 오서링 | `Pattern`에 필드 1개 추가 + `AnimationClipTrimmerWindow`에 마크 1개 추가 |
| 하위호환 | 임팩트 프레임 미지정(0 이하 또는 범위 밖) → **트림 끝**으로 간주 |

## 정렬 수식

```
impactAlignTime = Deadline + Pattern.SliceTargetImpactOffset      // 표적 임팩트와 동일
impactSpan      = clamp(animationImpactTime - startOffset, ε, dur) // 클립 초, 트림 시작 → 임팩트

예약:  scheduleStart = max(impactAlignTime - impactSpan / baseSpeed, FirstNodeTime)
시작:  speed = clamp(impactSpan / (impactAlignTime - now), baseSpeed, maxAttackSpeed)
       PlaySlot(clip, startOffset, dur, speed)      // dur는 전체 트림 길이 그대로
```

`PlaySlot`은 수정하지 않는다. 트림 전체가 한 배속으로 재생되므로
`actionEndTime = impactAlignTime + (트림끝 − 임팩트프레임) / speed`가 자동으로 따라오고,
기존 3경로 복귀 로직(연계·간격부족 / 연계·간격여유→Sprint / 연계X→Release)은 그대로 재사용된다.

## 의도된 부수 효과

- 임팩트 프레임 **미지정** 패턴은 기존 대비 정렬이 `goodWindow`(0.10초)만큼 뒤로 밀린다 — 앵커가 `LastNodeTime` → `Deadline`으로 바뀌었기 때문. 승인된 변경이다.
- 잔여 구간이 길면 `actionEndTime`이 뒤로 밀려 다음 공격의 크로스페이드에 끊길 수 있다(기존에도 있던 동작). 오서링 가이드로 "잔여 구간은 짧게"를 명시한다.

---

## 구현 단계

- [x] **Step 1 — `Pattern`에 임팩트 프레임 필드 추가**
  - `Assets/02. Scripts/Pattern/Pattern.cs`
  - `[SerializeField] private float animationImpactTime;` 추가 (툴팁: "칼날이 표적을 지나가는 프레임의 **클립 절대 시각(초)**. 0 이하거나 트림 범위 밖이면 트림 끝으로 간주한다.")
  - 기존 필드명(`animationStartOffset` / `animationDuration`)은 **절대 변경하지 않는다** — 에디터 툴이 문자열로 참조한다(Research 제약 5).
  - 공개 프로퍼티 `public float AnimationImpactTime => animationImpactTime;`
  - `OnValidate`에 범위 경고 추가: 값이 0보다 크고 `[animationStartOffset, animationStartOffset + 유효duration]` 밖이면 `Debug.LogWarning`. (유효 duration = `animationDuration > 0 ? animationDuration : (clip != null ? clip.length - startOffset : 0)`)
  - **구현 메모**: 유효 duration 계산이 `OnValidate`와 다른 곳에서 재사용되므로 `ResolvedAnimationDuration` 프로퍼티로 뽑아냈다. 검증은 `ValidateImpactTime()`으로 분리.

- [x] **Step 2 — `JudgeTargetInfo`에 `Deadline` 전달**
  - `Assets/02. Scripts/Pattern/JudgeTargetInfo.cs`: `public readonly float Deadline;` 필드 + 생성자 파라미터 추가. XML 주석에 "성패가 확정되는 시각. 임팩트 정렬 기준"을 명시.
  - `Assets/02. Scripts/UI/PatternHandler.cs:314` `RaiseJudgeTargetBegan`: `new JudgeTargetInfo(target.Template, target.FirstNodeTime, target.LastNodeTime, target.Deadline)`
  - `LastNodeTime`은 **제거하지 않는다** — 다른 소비자가 생길 수 있고, 제거해도 얻는 게 없다.

- [x] **Step 3 — `CharacterActionPlayer` 정렬 앵커 전환**
  - `Assets/02. Scripts/Character/CharacterActionPlayer.cs`
  - 예약 필드 교체: `pendingLastNodeTime` → `pendingImpactAlignTime`, 신규 `pendingImpactSpan` 추가 (`pendingDur`는 트림 전체 길이로 유지).
  - `HandleJudgeTargetBegan`: 위 "정렬 수식"의 예약 식으로 교체. `impactSpan`은 `Pattern.AnimationImpactTime`이 유효 범위 밖이면 `dur`로 폴백.
  - `TryStartPendingSuccess`: 배속 역산을 `pendingImpactSpan / (pendingImpactAlignTime - Time.time)`으로 교체. `PlaySlot` 호출 인자는 그대로(`pendingDur` 전달).
  - `PlaySlot` / `Update` / 복귀 3경로 / `HandleJudgeTargetFirstMiss`는 **수정하지 않는다**.
  - 클래스 XML 주석 갱신: "마지막 노드 판정 시각에 끝나도록" → "임팩트 프레임이 표적 절단 시각(Deadline 기준)에 오도록 정렬하고, 잔여 구간은 같은 배속으로 이어 재생한다".
  - **구현 메모**: 폴백 규칙을 `ResolveImpactSpan(template, startOffset, dur)` 정적 메서드로 분리했다.

- [x] **Step 4 — `AnimationClipTrimmerWindow`에 임팩트 마크 추가**
  - `Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs`
  - 상태: `private float impactTime;` 추가.
  - `LoadFromPattern`: `animationImpactTime` 읽기. 값이 0 이하면 `impactTime = endTime`으로 초기화.
  - `DrawMarkers`: 세 번째 마커 — start=초록 / **impact=시안(`0.3, 0.85, 0.95`)** / end=빨강.
  - `DrawMarking`: `Mark Impact = 현재` 버튼 + `Impact (s)` 직접 편집 필드 추가. `impactTime < startTime || impactTime > endTime`이면 `HelpBox(Warning)`.
  - 상태 박스: 현재 시각이 임팩트 프레임과 **같은 프레임**이면 `✦ IMPACT (베는 프레임)`으로 강조(그 외에는 기존 `IN SWING` / `— (구간 밖)` 유지).
  - `ApplyToPattern`: `animationImpactTime` 함께 저장. 저장 버튼의 `DisabledScope` 조건은 기존(`duration <= 0`) 그대로 두되, 임팩트가 범위 밖이면 저장 전 `startTime~endTime`으로 클램프.
  - "Pattern 현재값" 라벨: `offset / impact / duration` 3개 표시.
  - 창 상단 XML 주석 갱신.
  - **구현 메모**: 상태 박스가 3분기로 늘어 `DrawStatusBox(duration)`으로 분리했다. 마크가 전부 0인 초기 상태에서 프레임 0을 IMPACT로 오인하지 않도록, `atImpact`는 `inSwing`일 때만 판단한다.

- [x] **Step 5 — 문서 갱신**
  - `CLAUDE.md` §6 (캐릭터 액션): 정렬 기준이 "마지막 노드 판정 시각에 끝나도록"에서 "임팩트 프레임을 표적 절단 시각에 맞추도록"으로 바뀐 점 반영. (완료)
  - `CLAUDE.md` §11 (베이는 표적): 임팩트 시각이 `Deadline`이라는 서술은 유지하고, **캐릭터 애니메이션이 이 시각에 임팩트 프레임을 맞춘다**는 한 줄 추가. (완료)
  - 가이드: `docs/!Guides/Guide_CharacterActionTrim.md`가 트림 오서링 가이드였으므로(별도 `Guide_ClipTrimTool.md`는 없음) 여기에 **임팩트 프레임 절과 Clip Trimmer 사용 절차**를 추가했다. (완료)
  - 본 Plan의 각 Step 체크박스 갱신. (완료)

- [x] **Step 6 — 검증**
  - `refresh_unity(force, compile)` 후 `read_console` — **에러/경고 0건**, `editor/state`의 `compilation.is_compiling = false`로 컴파일 완료 확인. (완료)
  - 기존 패턴 에셋(임팩트 미지정)에 대해 `manage_scriptable_object(dry_run)`으로 `animationImpactTime` 프로퍼티 인식 확인 — `propertyType: Float`, 로드 경고 없음. (완료)
  - **미검증(플레이 모드 육안 확인 필요 — 사람이 해야 함)**:
    - Clip Trimmer로 패턴에 임팩트 프레임을 찍고 저장 → 인스펙터 반영 확인.
    - 플레이 모드에서 `SliceTargetDirector.drawGizmos`로 표적 도착 시점과 칼 궤적이 겹치는지 확인.
    - 연계 구간에서 잔여 애니메이션이 다음 공격에 과하게 끊기지 않는지 확인.

---

## 건드리지 않는 것

- `SliceTargetDirector` / `SliceTargetView` / `SlicePiece` — 표적 임팩트 시각은 그대로.
- `PatternHandler`의 판정 파이프라인 — Step 2의 이벤트 페이로드 한 줄 외에는 변경 없음.
- `CharacterActionPlayer`의 복귀 3경로, Release 로직, base 로코모션 전환.
- `Pattern`의 기존 필드명(에디터 툴이 문자열 참조).
