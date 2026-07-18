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

즉 실제 재생 구간 = 클립의 `[startOffset, startOffset + duration]`.

## 조정 방법 (권장 절차)
1. 대상 클립을 Animation 창/미리보기로 열어, **실제 검을 휘두르는 프레임 구간**을 확인한다.
2. 휘두름 시작 직전까지의 준비 시간을 `Animation Start Offset`에 입력(예: 0.15).
3. 휘두름이 끝나는 지점까지의 길이를 `Animation Duration`에 입력(예: 0.5). 마무리 회수 동작은 포함하지 않는다.
4. Play 모드에서 해당 패턴을 재생(디버그 오토퍼펙트로 연속 재생)해 잘림/어색함을 확인하고 미세 조정.

## 튜닝 지침
- **짧을수록 안전**: `Animation Duration`이 짧을수록 촘촘한 구간에서 잘릴 위험이 낮다. 단 너무 짧으면 타격감이 빈약해지므로 "휘두름 핵심"은 남긴다.
- 긴 클립(예: 1.2~1.8초)을 촘촘한 구간에 쓰면 트림해도 상한에 걸릴 수 있다. 그럴 땐 ⑴ `Animation Duration`을 더 줄이거나, ⑵ 그 구간에 더 짧은 클립을 배정한다.
- `maxAttackSpeed`(전역 상한) 상향은 최후의 수단 — 모든 액션의 배속감에 영향을 주므로 신중히.

## 관련 코드/문서
- 재생·배속 로직: `Assets/02. Scripts/Character/CharacterActionPlayer.cs` (`PlayActionClip`)
- 트림 데이터: `Assets/02. Scripts/Pattern/Pattern.cs` (`AnimationStartOffset`, `AnimationDuration`)
- 타이밍 불변식/원인 분석: `docs/CharacterActionTiming/`
