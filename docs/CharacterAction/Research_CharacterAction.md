# Research: 패턴 성공 시 캐릭터 베기 액션

## 목표

패턴을 **끝까지 정확히** 처리했을 때(`AllCorrect`), 씬의 캐릭터가 그 패턴에 지정된 베기 모션을 재생한다.
애니메이션 정보는 **Pattern 에셋에 저장**한다.

## 확정된 설계 결정 (사용자 확인 완료)

1. **저장 형태**: Pattern SO에 **`AnimationClip`을 직접 참조**로 저장한다.
   컨트롤러에는 클립을 주입받을 **단일 슬롯 스테이트("Attack")** 하나만 두고(빈 더미 클립을 키로 물려 둠), 런타임에 `AnimatorOverrideController`로
   그 슬롯의 클립을 패턴의 클립으로 덮어쓴 뒤 재생한다.
   → 모션을 늘릴 때 **컨트롤러를 전혀 건드리지 않고** Pattern SO에 클립만 꽂으면 된다. 문자열 스테이트 이름이 없어 오타 실패 모드도 사라진다.
   - (초기 검토안이던 "스테이트 이름 문자열 + `CrossFadeInFixedTime` 직접 재생"은 모션마다 컨트롤러에 스테이트를 추가해야 하고 문자열 오타 위험이 있어, 확장성을 위해 사용자 요청으로 이 안으로 변경됨.)
2. **성공 조건**: `ActivePattern.AllCorrect == true`. 즉 **모든 노드를 Good/Perfect로 치고, 그 과정에서 오답 Point를 한 번도 건드리지 않은 경우**.
3. **트리거 시점**: `PatternHandler.CompletePattern()` — 마지막 노드가 판정된 그 프레임.
4. **실패 시 피격(Hit)**: `AllCorrect == false`(타이밍 Miss / 오답 Point / 미입력 만료)면 **피격 리액션 클립**을 재생한다.
   Hit 클립은 패턴별이 아니라 **캐릭터 공용**이며 2개를 **번갈아** 재생한다. 원본: `Hit1.fbx`, `Hit2.fbx`
   (`Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Normal/`).
   베기와 **같은 단일 슬롯(Attack 스테이트)에 주입**하므로 컨트롤러에 Hit 스테이트를 새로 만들지 않는다. Hit은 도달 시각 정렬이 불필요하므로 오프셋 0에서 재생한다.

### AnimatorOverrideController 주입 방식 메모

- AOC는 오버라이드를 **원본 클립을 키로** 매핑한다 (`aoc[originalClip] = newClip`). 따라서 Attack 스테이트에는
  author 타임에 **빈 더미 placeholder 클립**이 하나 들어 있어야 하고(Motion을 None으로 두면 키가 없어 주입 불가),
  런타임에 그 더미를 키로 패턴 클립을 덮어쓴다. 더미는 실제로 재생되지 않는다.
- 교체 후 같은 스테이트를 다시 시작하려면 `CrossFadeInFixedTime`(또는 `Play(..., 0f)`)로 재진입한다. 스테이트가 항상 하나뿐이므로
  이전 베기가 재생/복귀 중이어도 새 성공이 자연스럽게 그것을 끊고 새 클립으로 재시작한다 — 리듬게임에서 원하는 동작.
- Attack → `Sprint_Forward` 복귀 전이는 **정규화된 `ExitTime`** 기반이므로, 주입되는 클립의 길이가 달라도 복귀가 정상 동작한다.
- 오버라이드 교체 비용은 오버라이드 개수에 비례(여기선 1개)하며 패턴 완료 빈도(초당 수 회)에서 무시할 수준.

## 현재 구현 상태

### 판정·완료 흐름 (`Assets/02. Scripts/UI/PatternHandler.cs`)

`AddPattern(int index)`가 판정의 중심이다.

| 상황 | 코드 위치 | `AllCorrect` |
|---|---|---|
| 기대하지 않은 Point 입력 | `PatternHandler.cs:466` | → false (판정 자체가 나지 않고 Miss 색만 칠하고 리턴) |
| 올바른 Point, 타이밍 Miss | `PatternHandler.cs:477` | → false |
| 올바른 Point, Good/Perfect | — | 유지 |
| `Deadline` 초과로 만료 | `PatternHandler.cs:174` (`ExpireOverduePatterns`) | 미완료면 → false |

마지막 노드가 판정되면 `AddPattern`이 그 자리에서 `CompletePattern(target)`을 호출한다 (`PatternHandler.cs:497`).
`CompletePattern`은 **완주와 만료 양쪽에서 모두** 호출되며, 두 경우 다 `OnPatternComplete?.Invoke(pattern.AllCorrect)`를 발행한다 (`PatternHandler.cs:514`).
만료 경로에선 `MarkIncorrect()`가 먼저 불리므로, **`allCorrect == true`는 사실상 "끝까지 다 맞게 쳤다"와 동치**다.

### 기존 확장 이벤트

`PatternHandler`는 이미 캐릭터 액션을 염두에 둔 확장 포인트를 노출하고 있다 (`PatternHandler.cs:89~99`).

```csharp
public event Action<JudgementResult, int> OnJudged;
public event Action<bool> OnPatternComplete;            // bool: 전체 정답 시 true
public event Action<int, Vector3> OnNodeConnected;
public event Action<int, NodeType, Vector3> OnFallingNodeSpawned;
public event Action<int, NodeType, Vector3, JudgementResult> OnFallingNodeResolved;
public event Action<int, NodeType, Vector3> OnFallingNodeMissedArrival;
```

**제약**: `OnPatternComplete`는 **bool 하나만** 넘긴다. 구독자가 "어느 `Pattern`이 완성됐는지" 알 수 없으므로,
패턴별 애니메이션을 고르려면 `Pattern`을, 타이밍 정렬을 위해 **마지막 노드 도달 시각**을, 겹침 방지를 위해 **다음 대기 패턴 도달 시각**을 실어야 한다.

**확장성 결정**: 인자를 나열(`Action<bool, Pattern, float, float>`)하는 대신 **단일 페이로드 구조체 `PatternCompletionInfo`** 로 전달한다
(`Action<PatternCompletionInfo>`). 향후 카메라 연출 등 소비자가 늘거나 필드(예: 마지막 노드 월드 좌표, 콤보 수)가 추가돼도
기존 구독자 시그니처가 깨지지 않는다. `NextLastNodeTime`은 `CompletePattern`에서 완료 패턴을 큐에서 제거한 뒤의 선두(`activePatterns[0]`)에서 얻고, 없으면 음수 sentinel.
현재 구독자는 `EffectManager.HandlePatternComplete` 하나뿐(`EffectManager.cs:56`)이라 리팩터링 파급 범위는 작다. 카메라 연출 자체는 **이번 범위 밖**이며 이 이벤트 훅만 준비한다.

### 타이밍 정렬 (베기 contact = 마지막 노드 도달 시각)

> ⚠️ **2026-07-18 재설계로 이 절의 contact 정렬 방식은 폐기됨.** 실제 채택: 클립을 **처음부터 온전히** 재생하고,
> 다음 패턴 애니메이션 시작 시각까지의 창보다 길면 그 창에 맞춰 **배속 재생**(잘림 방지)한다. `successImpactNormalizedTime`과 startOffset은 제거.
> "칼이 닿는 순간을 노드 도달에 맞추기"는 후속(예측 재생)으로 미룸. 아래 원래 논의는 이력으로 남긴다.

**요구**: 벨 오브젝트는 마지막 노드가 각 Point에 떨어지는 시각에 캐릭터 앞에 도달한다. 따라서 베기 클립의 **칼이 닿는 순간(contact 프레임)** 이 그 시각에 와야 한다.

관련 사실:
- 마지막 노드의 이상적 도달 시각(절대) = `ActivePattern.StartTime + inputTimes[NodeCount-1]`. 이 값은 채보상 고정이며, **플레이어 입력 시각과 별개**다.
  - `ActivePattern`은 현재 이 값을 직접 노출하지 않는다(`ExpectedTime`은 `CurrentPosition` 기준이라 완료 후엔 범위를 벗어난다). → **`LastNodeTime` 프로퍼티를 추가해야 한다.**
- 패턴 완료 이벤트는 **플레이어가 마지막 노드를 실제로 누른 시각**(`Time.time`)에 발생한다. 이 시각은 도달 시각과 판정 윈도우(최대 `goodWindow`, ±0.1초)만큼 어긋날 수 있다.
  - 오브젝트는 도달 시각에 도착하므로, 베기는 **입력 시각이 아니라 도달 시각(`LastNodeTime`)에 앵커**해야 일찍/늦게 친 경우에도 contact가 오브젝트 도착 순간과 맞는다.
- 클립 내에서 contact가 몇 번째 프레임인지는 클립마다 다르다. 이를 **정규화 시각(0~1)** 으로 데이터화해야 한다(클립 길이가 달라도 유효). → Pattern에 `successImpactNormalizedTime` 필드.

**정렬 수식** (구독자 `CharacterActionPlayer`가 완료 시점에 계산):
```
impactSeconds = successImpactNormalizedTime * clip.length
delta         = LastNodeTime - Time.time            // 도달까지 남은 시간(일찍 쳤으면 +, 늦었으면 -)
startOffset   = clamp(impactSeconds - delta, 0, clip.length)   // 클립을 이 지점부터 시작
Animator.CrossFadeInFixedTime(attackState, dur, attackLayer, startOffset)
```
→ 클립을 `startOffset`부터 재생하면 contact 프레임이 정확히 `LastNodeTime`에 온다.
`successImpactNormalizedTime`을 0으로 두면 `startOffset = -delta → clamp 0`이 되어 **완료 시각에 클립을 처음부터 재생**하는 단순 동작으로 자연 degrade된다.

**트레이드오프(반응형 방식의 한계)**: 완료(=마지막 입력) 시점에 재생을 시작하므로, contact를 도달 시각에 맞추려면 클립을 중반부터 시작(예비동작 일부 스킵)하게 된다. 예비동작까지 온전히 보여주려면 마지막 노드 **이전에** 미리 재생을 시작하는 예측 방식이 필요한데, 이는 완료(성공 여부) 확정 전에 재생을 걸어야 하므로 별도 훅이 필요하다 → 이번 범위 밖(Plan '범위 밖'에 명시).

### 겹침 방지 (연속 베기 재생 구간이 겹치지 않게)

채보의 패턴은 판정 시각이 겹치지 않지만 간격이 최소 ~0.4초라, 베기 클립이 그보다 길면 앞 베기가 재생 중일 때 다음 패턴이 완료될 수 있다.
**결정(사용자 확인)**: 앞 베기를 **끊지 않고**, 다음 베기 시작 전에 끝나도록 **재생 속도를 높여 압축**한다.
- 예산 `budget = nextLastNodeTime - Time.time`, 잔여 `remaining = clip.length - startOffset`. `remaining > budget`이면 `speed = min(remaining/budget, maxAttackSpeed)`.
- 속도는 전역 `animator.speed`가 아니라 **Attack 스테이트의 Speed Multiplier 파라미터(`AttackSpeed`)** 로 적용한다(Run/Idle에 영향 없음). → 컨트롤러에 `float AttackSpeed` 파라미터 1개 + 스테이트 Speed Multiplier 설정 필요.
- 단일 스테이트/단일 레이어라 애니메이션이 물리적으로 동시에 두 개 재생되는 일은 없다. `maxAttackSpeed`로도 못 맞추는 극단 간격에선 다음 베기의 CrossFade 재진입이 잔여를 끊으므로 **겹침은 어떤 경우에도 발생하지 않는다.**
- 속도 상승 시 pre-contact 구간(일찍 친 경우)에 한해 contact 정렬 오차가 생길 수 있으나 `≤ delta·(1−1/speed) ≤ 0.1초`이며, 정타에선 오차 0.

### Pattern 에셋 (`Assets/02. Scripts/Pattern/Pattern.cs`)

- '모양 원본'일 뿐이며 진행 상태를 갖지 않는다. `PatternData[]`(인덱스 0~8)와 `GetNodeType()`만 제공.
- 애니메이션 클립 참조는 **모양에 종속된 정적 데이터**이므로, 이 에셋에 두는 것이 이 원칙과 충돌하지 않는다.
- 기존 `OnValidate()`가 중복 인덱스를 검사하는 선례가 있다.

현재 템플릿 에셋 (`Assets/04. Datas/Patterns/Templates/`):

| 에셋 | Point 번호(1-based) | 대응 가능한 기존 클립 |
|---|---|---|
| `Pattern_1Node_(4).asset` | 5 | 없음 |
| `Pattern_3Node_(0, 3, 6).asset` | 1, 4, 7 | 없음 |
| `Pattern_3Node_(0, 4, 8).asset` | 1, 5, 9 | **`Swipe_1To9`** |
| `Pattern_4Node_(6, 3, 0, 1).asset` | 7, 4, 1, 2 | 없음 |
| `Pattern_5Node_(8, 5, 2, 1).asset` | 9, 6, 3, 2 | 없음 |

→ **모든 패턴에 대응 모션이 있지는 않다.** 클립 참조가 비어 있으면(`null`) **무연출**로 조용히 넘어가야 한다
(`EffectManager`의 "프리팹이 지정되지 않은 트리거는 무연출" 선례와 동일).

Swipe 클립 원본은 `Assets/05. Animations/Clip/` (`Swipe_1To9.anim` 등)에 있으므로, Pattern SO에서 직접 참조할 수 있다.

### 캐릭터 & Animator

- 씬(`Assets/01. Scenes/DefaultScene.unity`)에 `School_Katana_FullBody-Magica cloth2.prefab`
  (`Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Prefab/`) 인스턴스가 있고,
  Animator의 `m_Controller`가 `Assets/05. Animations/Animator/PlayerAnimator.controller`로 **오버라이드**되어 있다.
- 캐릭터를 제어하는 스크립트는 **아직 없다** (`Assets/02. Scripts/` 전체에 Animator 참조 없음).

### `PlayerAnimator.controller` 구조

레이어 3개:

| # | 레이어 | 마스크 | `DefaultWeight` | 스테이트 |
|---|---|---|---|---|
| 0 | Running Layer | 없음 | 0 (베이스라 무시됨, 항상 1) | `Run` |
| 1 | Attack Layer | `0399ae15…` | **0** | `Sprint_Forward`(기본) + Swipe 6종 |
| 2 | Eye Blink Layer | 없음 | 1 | 없음 |

Attack Layer의 Swipe 스테이트 6종 — 이름이 `Swipe_{from}To{to}` (Point 번호 1-based):

`Swipe_1To9`, `Swipe_3To7`, `Swipe_4To6`, `Swipe_6To4`, `Swipe_8To2`, `Swipe_9To1`

각 Swipe 스테이트는 `Sprint_Forward`로 **되돌아가는 전이**를 이미 갖고 있다(`HasExitTime`). 즉 재생 후 자동 복귀는 배선 완료.

> **현재 상태(사용자 편집 반영)**: `Swipe_1To9`는 `Attack`으로 이름이 바뀌어 있고 아직 `Swipe_1To9.anim`이 물려 있다. Plan에서 이 스테이트를 단일 주입 슬롯으로 정리한다(더미 클립 키 + 나머지 Swipe 스테이트 삭제).
> 겹침 방지 속도 스케일을 위해 컨트롤러에 `float AttackSpeed` 파라미터 1개를 추가하고 `Attack` 스테이트의 Speed Multiplier로 지정해야 한다(현재 파라미터 없음).

## 발견된 문제점 (이 작업에서 반드시 처리해야 함)

### 1. Attack Layer의 `DefaultWeight`는 0 — 런타임 동적 제어 (영구 변경 아님)

`PlayerAnimator.controller:372`. 웨이트 0은 **휴지 상태의 정상값**이다(마스크된 레이어가 베이스 Run을 상시 덮지 않아야 하므로).
따라서 인스펙터에서 영구히 1로 올리지 **않는다**. 대신 `CharacterActionPlayer`가 **액션 재생 중에만** 웨이트를 1로 올렸다가
재생 종료 후 0으로 페이드한다(Plan Step 4). 즉 이 항목은 "버그"가 아니라 런타임 제어 대상이다.
(초기 검토에선 영구 1로 올리려 했으나, "재생 중에만 활성/끝나면 비활성" 요구에 따라 동적 제어로 변경됨.)

### 2. 조건 없는 AnyState 전이 6개

`m_AnyStateTransitions`에 Swipe 6종으로 가는 전이가 걸려 있는데, 전부 `m_Conditions: []`(조건 없음)이고
`m_AnimatorParameters: []`(파라미터 자체가 하나도 없음)이다.
**조건 없는 AnyState 전이는 매 프레임 무조건 성립**하므로, 레이어 웨이트를 올리는 순간 컨트롤러가 스테이트 사이를 계속 튕긴다.
CrossFade로 직접 구동할 것이므로 **이 전이 6개는 제거해야 한다.**

### 3. AttackSlot placeholder 클립 유지

AOC 오버라이드는 원본 클립을 키로 쓴다. AttackSlot 스테이트에 author 타임에 넣어 둔 placeholder 클립과,
`CharacterActionPlayer`가 키로 들고 있는 placeholder 참조가 **같아야** 오버라이드가 걸린다.
placeholder 클립 참조 필드 하나를 인스펙터로 노출해 명시적으로 배선한다 (Step 4). 문자열 스테이트 이름 방식의 오타 위험은
클립을 직접 참조하므로 **원천적으로 사라진다** — 이 방식으로 바꾼 이점 중 하나.

## 관련 파일 요약

| 파일 | 역할 | 수정 필요 |
|---|---|---|
| `Assets/02. Scripts/Pattern/Pattern.cs` | 패턴 모양 원본 SO | `AnimationClip` 참조 필드 추가 |
| `Assets/02. Scripts/UI/PatternHandler.cs` | 판정/완료 | `OnPatternComplete` 시그니처에 `Pattern` 추가 |
| `Assets/02. Scripts/Effect/EffectManager.cs` | 이펙트 (기존 구독자) | 핸들러 시그니처만 맞춤 |
| `Assets/05. Animations/Animator/PlayerAnimator.controller` | 캐릭터 컨트롤러 | 레이어 웨이트 + AnyState 전이 정리 + 단일 AttackSlot 스테이트 |
| `Assets/01. Scenes/DefaultScene.unity` | 메인 씬 | 신규 컴포넌트 배선 |
| (신규) `Assets/02. Scripts/Character/CharacterActionPlayer.cs` | 이벤트 구독 → 모션 재생 | 신규 |

## 참고할 선례

`EffectManager`(`Assets/02. Scripts/Effect/EffectManager.cs`)가 거의 같은 형태의 문제를 이미 푼다:
`OnEnable`/`OnDisable`에서 `PatternHandler` 이벤트를 구독/해제하고, 인스펙터 카탈로그로 트리거→연출을 매핑하며,
매핑이 없으면 무연출로 넘어간다. 신규 `CharacterActionPlayer`는 이 구조를 그대로 따른다.
