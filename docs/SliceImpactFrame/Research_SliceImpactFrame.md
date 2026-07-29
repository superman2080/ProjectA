# Research: 베는 임팩트 프레임 정렬 (SliceImpactFrame)

## 문제

패턴 성공 시 캐릭터가 베기 클립을 재생하고, 같은 타이밍대에 `SliceTargetView`가 표적을 절단한다.
그런데 **칼날이 표적을 실제로 지나가는 프레임은 클립 중간**인 반면, 현재 정렬 기준은 **클립(트림)의 끝**이다.
그래서 "칼은 이미 지나갔는데 한참 뒤에 갈라진다" 혹은 그 반대의 어긋남이 생긴다.

---

## 현재 구현 분석

### 1. `Assets/02. Scripts/Pattern/Pattern.cs`

베기 애니메이션 관련 필드(모두 '모양에 종속된 정적 데이터'):

| 필드 | 단위 | 의미 |
|---|---|---|
| `successAnimationClip` | - | 완주 성공 시 재생할 클립 |
| `animationStartOffset` | 클립 초 | 트림 시작(선딜 제거) |
| `animationDuration` | 초 | 트림 길이. 0 이하면 `clip.length - startOffset` |
| `animationSpeed` | 배 | 기본 배속(**하한**) |

표적 관련:

| 필드 | 의미 |
|---|---|
| `sliceTarget` (`SliceSet`) | 이 패턴에서 등장할 표적. 비면 표적 없음 |
| `sliceTargetOffset` (`Vector2`) | 임팩트 지점 기준 XY 배치 |
| `sliceTargetImpactOffset` (`float`) | **`Deadline` 대비 ±초** — 표적이 닿는 순간을 앞뒤로 민다 |

**클립 안에서 '칼이 지나가는 순간'을 가리키는 데이터가 없다.** 이것이 문제의 근본 원인이다.

### 2. `Assets/02. Scripts/Pattern/JudgeTargetInfo.cs`

```csharp
public readonly struct JudgeTargetInfo
{
    public readonly Pattern Template;
    public readonly float FirstNodeTime;   // 성공 애니 시작 하한
    public readonly float LastNodeTime;    // 현재 정렬 기준
}
```

**`Deadline`이 없다.** `PatternHandler.RaiseJudgeTargetBegan`(`PatternHandler.cs:314`)이
`new JudgeTargetInfo(target.Template, target.FirstNodeTime, target.LastNodeTime)`로만 발행한다.
`ActivePattern.Deadline`(`ActivePattern.cs:34`, `= StartTime + 마지막 inputTime + goodWindow`)은 존재하지만 이 이벤트로 새어 나오지 않는다.

참고: `PatternQueuedInfo`(SliceTargetDirector가 구독)는 이미 `Deadline`을 들고 있다.

### 3. `Assets/02. Scripts/Character/CharacterActionPlayer.cs`

정렬 로직은 두 군데다.

**예약** — `HandleJudgeTargetBegan` (334~358행):

```csharp
float startOffset = info.Template.AnimationStartOffset;
float dur         = AnimationDuration > 0 ? AnimationDuration : clip.length - startOffset;
float baseSpeed   = info.Template.AnimationSpeed;
float playTime    = dur / baseSpeed;
float scheduleStart = Mathf.Max(info.LastNodeTime - playTime, info.FirstNodeTime);
```

→ **트림 전체를 `LastNodeTime`에 끝내도록** 시작점을 역산한다.

**시작** — `TryStartPendingSuccess` (361~372행):

```csharp
float remaining = Mathf.Max(pendingLastNodeTime - Time.time, 0.0001f);
float needed    = pendingDur / remaining;
float speed     = Mathf.Clamp(needed, pendingBaseSpeed, Mathf.Max(maxAttackSpeed, pendingBaseSpeed));
PlaySlot(pendingClip, pendingStartOffset, pendingDur, speed);
```

→ 배속도 **트림 전체 길이 기준**으로 역산된다.

**재생 프리미티브** — `PlaySlot` (401~430행):

```csharp
animator.SetFloat(attackSpeedHash, speed);
actionEndTime   = Time.time + dur / Mathf.Max(speed, 0.01f);
recoveryEndTime = actionEndTime + recoveryHoldDuration;
animator.CrossFadeInFixedTime(targetStateHash, attackCrossFadeDuration, attackLayerIndex,
                              startOffset / Mathf.Max(speed, 0.01f));
```

`actionEndTime`은 **트림 끝 = 복귀 판단 시작점**이며, 클립 자체는 계속 재생된다.
`Update`(180~238행)의 3경로 복귀 로직(연계·간격부족 / 연계·간격여유 → Sprint / 연계 X → Release)이 전부 `actionEndTime`을 기준으로 돈다.

### 4. `Assets/02. Scripts/Slice/SliceTargetDirector.cs`

`HandlePatternQueued`(136~172행):

```csharp
float impactTime = info.Deadline + template.SliceTargetImpactOffset;
```

**임팩트 시각은 `Deadline`**이다. 문서화된 이유(클래스 주석 11~13행): 마지막 노드를 `goodWindow` 안에
늦게 눌러도 Good 성공이므로, `LastNodeTime`에 도착시키면 **정상적인 늦은 입력이 실패로 연출된다**.

### 5. `Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs`

`Tools/Animation Clip Trimmer`. 클립을 프레임 단위로 스크럽하며 트림 구간을 잡아 `Pattern`에 저장한다.

- 상태: `clip`, `previewModel`, `targetPattern`, `currentTime`, `isPlaying`, **`startTime`**, **`endTime`**
- `LoadFromPattern`(104~119행): `successAnimationClip` / `animationStartOffset` / `animationDuration`를 `SerializedObject`로 읽어 마크 복원
- `DrawPreview`(123~150행): `PreviewRenderUtility` + `clip.SampleAnimation`으로 포즈 표시. 드래그 회전 / 휠 줌
- `DrawTimeline`(241~279행): Play/Pause, 프레임 이송 버튼, 스크럽 슬라이더
- `DrawMarkers`(281~294행): start=초록 / end=빨강 세로선
- `DrawMarking`(298~328행): `Mark Start/End = 현재` 버튼, 직접 편집 필드, `IN SWING` 상태 박스
- `ApplyToPattern`(357~368행): `SerializedObject`로 `animationStartOffset` / `animationDuration` 저장 + `SaveAssetIfDirty`

**임팩트 프레임을 잡는 UI가 없다.** 다만 마크를 하나 더 추가하기에 구조는 그대로 재사용 가능하다.

---

## 어긋남의 크기

```
현재:   [트림 시작] ──── 칼 지나감 ──── [트림 끝]           표적 절단
                          ↑                 ↑                    ↑
                       클립 중간        LastNodeTime      Deadline(= LastNode + 0.10s)
                          └──────── 어긋남 = (트림끝 − 임팩트프레임)/배속 + 0.10s ────┘
```

트림 길이가 0.6초·배속 1.5배·임팩트가 트림 중앙이라면 어긋남은 약 0.3초다. 명백히 체감된다.

---

## 제약사항

1. **표적 임팩트는 `Deadline`에서 옮길 수 없다.** 옮기면 `goodWindow` 안의 정상적인 늦은 Good 입력이 성패 확정 전에 표적과 충돌한다. → 애니메이션이 `Deadline`에 맞춰야 한다.
2. **배속 상한이 있다.** `maxAttackSpeed`(2.5) 이상으로는 압축되지 않으므로, 정렬은 "가능한 만큼" 근사된다. 이는 기존과 동일한 성질이다.
3. **`actionEndTime`은 복귀 로직 전체의 기준점**이다. 정렬 앵커를 바꾸면 `actionEndTime`이 뒤로 밀려 연계 판정(`IsLinkedToNextAction` / `HasRoomForRunExposure`)에 영향을 준다.
4. **하위호환.** 이미 오서링된 패턴 에셋들(`Assets/04. Datas/Patterns/Templates/*.asset`)에는 임팩트 프레임 값이 없다. 미지정 시 동작이 정의되어야 한다.
5. **에디터 툴은 `SerializedObject` 문자열 필드명으로 `Pattern`에 접근**한다. 필드명을 바꾸면 툴이 조용히 깨진다(널 역참조). 필드는 추가만 하고 기존 이름은 건드리지 않는다.
