# Guide — 캐릭터 액션 애니메이션 트리밍

패턴 완주 성공 시 재생되는 **베기 애니메이션**에서, 실제로 "휘두르는" 구간만 남기고 앞뒤 불필요한 구간(준비 자세=선딜, 마무리=후딜)을 잘라내는 방법. 트리밍은 재생 길이를 줄여 촘촘한 구간에서 **뒷부분 잘림**을 예방한다.

## 왜 트리밍이 필요한가
- 캐릭터 액션은 다음 패턴의 액션이 시작되면 CrossFade로 끊긴다. 그래서 각 액션은 "다음 액션 시작 전"까지 마쳐야 한다.
- 창(budget)보다 클립이 길면 배속으로 압축하지만 상한(`CharacterActionPlayer.maxAttackSpeed`, 기본 2.5)이 있어, 지나치게 긴 클립은 상한에서도 다 못 담아 **뒷부분이 잘린다.**
- 준비/마무리 같은 "안 휘두르는" 구간을 잘라 실제 휘두름만 남기면, 압축해야 할 길이 자체가 줄어 잘림이 사라진다.

## 어디서 설정하나 — `Pattern` 에셋 인스펙터
경로: `Assets/04. Datas/Patterns/Templates/*.asset` → 각 `Pattern` 에셋 선택.

| 필드 | 의미 |
|---|---|
| **Success Animation Clip** | 이 패턴 완주 성공 시 재생할 베기 클립. 비우면 무연출. |
| **Animation Start Offset**(초) | 재생을 클립 앞에서 이만큼 **건너뛴다**. 준비 자세(선딜) 제거용. |
| **Animation Duration**(초) | 재생 시작(offset 지점)부터 이 길이만큼만 재생. 마무리(후딜) 제거용. **0 이하면 클립 끝까지.** |
| **Animation Impact Time**(초) | **칼날이 표적을 지나가는 프레임의 클립 절대 시각.** 이 프레임이 표적이 갈라지는 시각에 오도록 정렬된다. 0 이하 또는 트림 범위 밖이면 **트림 끝**으로 폴백. |

즉 실제 재생 구간 = 클립의 `[startOffset, startOffset + duration]`, 그 안의 한 지점이 `impactTime`이다.

## 임팩트 프레임이 정렬 기준이다

베기 클립에서 칼날이 실제로 지나가는 순간은 **클립 중간**이지, 트림 끝이 아니다.
그래서 정렬은 트림 끝이 아니라 **임팩트 프레임**을 기준으로 한다:

```
임팩트정렬시각 = Deadline + Pattern.SliceTargetImpactOffset
```

이는 `SliceTargetDirector`가 표적 도착에 쓰는 식과 **동일하다** — 칼날이 지나가는 순간과 표적이 갈라지는 순간이 구조적으로 일치한다.
임팩트 프레임 **이후** 남은 트림 구간은 같은 배속으로 그대로 이어 재생되어, 마무리 동작이 뒤에 남는다.

`Animation Impact Time`을 찍지 않은 패턴은 트림 끝을 임팩트로 간주하므로, 기존 오서링도 그대로 동작한다(다만 정렬은 어긋난 채다).

## 조정 방법 — `Tools/Animation Clip Trimmer`

이 툴이 세 마크를 프레임 단위로 찍어 `Pattern`에 바로 저장한다.

1. **Clip**에 베기 클립, **Preview Model (rig)**에 캐릭터 리그를 지정한다. 프리뷰는 드래그로 회전, 휠로 줌.
2. **Target Pattern**을 지정하고 `Load Clip / Marks from Pattern`으로 기존 값을 불러온다.
3. `◀ Frame` / `Frame ▶`으로 이송하며 마크를 찍는다:
   - **Mark Start = 현재** — 휘두름 시작 직전(준비 자세 제거).
   - **Mark Impact = 현재** — **칼날이 표적을 통과하는 바로 그 프레임.** 여기서 상태 박스가 `✦ IMPACT (베는 프레임)`으로 바뀐다.
   - **Mark End = 현재** — 마무리 동작 중 잔여로 남길 만큼까지.
4. 타임라인 마커로 확인한다 — 초록=Start / **시안=Impact** / 빨강=End.
5. `Apply to Pattern`으로 저장한다. Impact가 Start~End 밖이면 경고가 뜨고, 저장 시 구간 안으로 클램프된다.
6. Play 모드에서 재생(디버그 오토퍼펙트로 연속 재생)해 확인하고 미세 조정. `SliceTargetDirector`의 `drawGizmos`를 켜면 표적 도착 지점과 칼 궤적을 씬 뷰에서 대조할 수 있다.

## 튜닝 지침
- **짧을수록 안전**: `Animation Duration`이 짧을수록 촘촘한 구간에서 잘릴 위험이 낮다. 단 너무 짧으면 타격감이 빈약해지므로 "휘두름 핵심"은 남긴다.
- **임팩트 이후 잔여 구간도 짧게**: 임팩트 프레임이 정렬 기준이므로, 그 뒤 구간이 길수록 액션 종료(`actionEndTime`)가 뒤로 밀려 다음 공격의 크로스페이드에 끊긴다. 잔여는 "칼을 거두는 낌새" 정도만 남긴다.
- 긴 클립(예: 1.2~1.8초)을 촘촘한 구간에 쓰면 트림해도 상한에 걸릴 수 있다. 그럴 땐 ⑴ `Animation Duration`을 더 줄이거나, ⑵ 그 구간에 더 짧은 클립을 배정한다.
- `maxAttackSpeed`(전역 상한) 상향은 최후의 수단 — 모든 액션의 배속감에 영향을 주므로 신중히.

## 관련 코드/문서
- 재생·배속 로직: `Assets/02. Scripts/Character/CharacterActionPlayer.cs` (`HandleJudgeTargetBegan`, `TryStartPendingSuccess`, `PlaySlot`)
- 오서링 툴: `Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs`
- 트림 데이터: `Assets/02. Scripts/Pattern/Pattern.cs` (`AnimationStartOffset`, `AnimationDuration`, `AnimationImpactTime`)
- 임팩트 프레임 정렬 설계: `docs/SliceImpactFrame/`
- 타이밍 불변식/원인 분석: `docs/CharacterActionTiming/`
