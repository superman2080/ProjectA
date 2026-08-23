# Plan: 패턴 성공 시 캐릭터 베기 액션

근거: `docs/CharacterAction/Research_CharacterAction.md`

## 구현 현황 (2026-07-18)

- (완료) **코드 전부 완료** — Step 1(Pattern), Step 2(PatternCompletionInfo/ActivePattern/PatternHandler/EffectManager), Step 4(CharacterActionPlayer). 컴파일 클린.
- (완료) **에디터 작업 완료(Unity MCP)** — Step 3(컨트롤러), Step 5(씬 배선), Step 6(Pattern 에셋). 아래 반영됨.
  - Step 3: `AttackSpeed` float 파라미터 추가, `Hit` 트리거 제거, AnyState 전이 전부 제거, `Hit1`/`Hit2` 스테이트 제거, `Attack` 스테이트에 `AttackSlot_Placeholder.anim` 주입 + Speed Multiplier=`AttackSpeed`, 레이어 웨이트 0 유지, `Attack→Sprint_Forward` 복귀 전이 유지.
  - Step 5: `School_Katana_FullBody-Magica cloth2`에 `CharacterActionPlayer` 부착, handler=`PointBackground`, animator=캐릭터, placeholder/hitClips(Hit1,Hit2) 배선, 씬 저장.
  - Step 6: 패턴 5개에 SuccessAnimationClip 배선됨(예: `Pattern(0,4,8)`→`Swipe_1To9`).
  - (참고: 사용자가 먼저 만들어 둔 Hit1/Hit2 스테이트+Hit 트리거는 '주입 방식' 채택에 따라 제거함.)
- (완료) **타이밍 재설계(2026-07-18)** — contact 정렬/`successImpactNormalizedTime` 제거, **창에 맞춘 배속 재생**으로 전환(클립 앞부분을 자르지 않음).
- (완료) **무기 본 베이크** — 커스텀 Humanoid 스윙 클립들이 무기 본(`add_weapon_r`)을 안 움직여 칼이 손에서 떨어지던 문제를, 손 그립을 따라가는 커브를 클립에 구워 해결(`docs/CharacterAction` 별도 기록 없이 클립 자산에 반영).
- (대기) **Step 7 검증** — Play 모드에서 수행 필요(아래 체크리스트).

## 설계 요약

`Pattern` 에셋에 **`AnimationClip`을 직접 참조**로 저장한다.
패턴이 `AllCorrect`로 완료되면 `PatternHandler`가 완성된 `Pattern`을 함께 실어 이벤트를 발행하고,
신규 `CharacterActionPlayer`가 이를 구독해 **`AnimatorOverrideController`로 단일 AttackSlot 스테이트의 클립을 패턴 클립으로 덮어쓴 뒤** 재생한다.

```
PatternHandler.CompletePattern()
        │  OnPatternComplete(PatternCompletionInfo info)   // 페이로드 구조체 하나로 전달(확장성)
        ├─────────────────────────► EffectManager          (기존, 구독)
        ├─────────────────────────► CharacterActionPlayer   (신규, 구독)
        └─────────────────────────► (향후) CameraDirector    (지금 구현 안 함 — 이 이벤트에 구독만 추가하면 됨)
                                          ▼ CharacterActionPlayer
             ├ info.AllCorrect  && Pattern.SuccessAnimationClip != null  → 베기(Attack): 패턴 클립
             └ !info.AllCorrect && hitClips 있음                         → 피격(Hit):  Hit 클립 번갈아
                                          ▼ (공통)
                                    aoc[placeholderClip] = clip
                                    speed = (clip.length > 창) ? min(clip.length/창, max) : 1   // 창에 맞춘 배속
                                    animator.SetFloat(AttackSpeed, speed)
                                    Animator.CrossFadeInFixedTime(Attack, dur, AttackLayer, 0f)  // 항상 처음부터
```

**이벤트 확장성(카메라 연출 대비)**: 완료 이벤트는 인자를 나열하지 않고 **단일 페이로드 구조체 `PatternCompletionInfo`** 로 전달한다.
카메라 연출은 **지금 구현하지 않는다.** 나중에 붙일 때 (1) 이 이벤트에 구독자만 추가하고, (2) 카메라가 필요로 하는 데이터가 더 있으면 구조체에 필드 한 줄만 더한다 —
**기존 구독자(EffectManager, CharacterActionPlayer)의 시그니처는 그대로**다. 인자를 늘릴 때마다 모든 핸들러가 깨지는 것을 막는 게 이 구조체의 목적이다.

**성공/실패 분기**: 완주 성공(`allCorrect`)이면 베기, 그 외(`!allCorrect` — 타이밍 Miss / 오답 Point / 미입력 만료)면 **피격(Hit)** 을 재생한다.
Hit 클립은 **패턴별이 아니라 캐릭터 공용 리액션**이므로 `Pattern`이 아니라 `CharacterActionPlayer`가 리스트로 들고 **번갈아** 재생한다(클립 2개). Hit도 같은 단일 슬롯(Attack 스테이트)에 주입한다 — 컨트롤러에 Hit 스테이트를 새로 만들지 않는다.

**타이밍 모델(창에 맞춘 배속 + 복귀 시 배속 해제) — 2026-07-18 확정**: 클립은 처음부터 재생하되, 다음 패턴 애니메이션 시작 시각까지의 창보다 길면 잘리지 않게 그 창에 맞춰 **배속**한다(`speed = clip.length/budget`, 창 넉넉하면 1배속, `maxAttackSpeed` 상한). 배속은 Attack 스테이트의 Speed Multiplier(`AttackSpeed`)로만 적용.
- **핵심 수정**: 배속된 공격에 이어지는 복귀 애니메이션이 함께 빨라 보이던 문제를 해결. 정적으로 Sprint_Forward/Run은 speed 파라미터가 없어 배속되지 않지만, **배속된 Attack이 Sprint_Forward로 빠져나가는 전이(블렌드) 구간에 Attack 꼬리가 배속으로 섞여** 복귀가 빨라 보였다.
- `CharacterActionPlayer.Update()`가 **Attack→(비Attack) 전이 중이면 `AttackSpeed`를 1로 되돌린다** → 복귀 구간이 정상 속도로 나온다. 연속 공격(Attack→Attack 재진입)은 next가 Attack이라 제외되어 다음 공격 배속은 유지된다.
- contact 정렬/`successImpactNormalizedTime`은 이전에 제거됨.

**레이어 활성/비활성(중요)**: Attack Layer는 **휴지 상태에서 웨이트 0**(베이스 Run만 보임)이고, 액션(베기/피격)을 **재생하는 동안에만 웨이트 1**로 올렸다가 **재생이 끝나면 다시 0**으로 내린다.
`CharacterActionPlayer`가 재생 시작 시 웨이트를 1로 올리고, `actionEndTime = Time.time + clip.length`(1배속) 이후 짧게 페이드하며 0으로 내린다. 연속 액션이면 종료 시각이 뒤로 밀려 계속 1을 유지한다.

**Attack Layer 아바타 마스크: 사용 안 함(2026-07-18 결정)**. 마스크 없이 **전신**으로 재생한다. 따라서 액션(특히 배속된 베기)은 다리까지 전신이 움직인다.
(초기엔 상체 전용 `PlayerAvatarMask`를 썼으나, 사용자가 마스크를 쓰지 않기로 결정 → 레이어의 `avatarMask = null`. 무기 본은 Run/베이크된 스윙 클립 모두에 커브가 있어 마스크 없이도 칼 쥠이 유지된다.)

**확장성**: 모션 추가 = Pattern SO에 클립을 꽂기만 하면 된다(재생 속도는 클립 길이와 창으로 자동 결정 — 별도 파라미터 없음). **컨트롤러(스테이트/전이)는 건드리지 않는다** (Speed 파라미터는 최초 1회만 구성).
문자열 스테이트 이름이 없으므로 오타 실패 모드도 없다.

**원칙**: 클립 참조가 `null`이면 **무연출**로 조용히 넘어간다
(`EffectManager`의 "프리팹이 지정되지 않은 트리거는 무연출" 선례와 동일). 패턴 처리 흐름은 어떤 경우에도 막지 않는다.

---

## Step 1: `Pattern`에 애니메이션 클립 참조 추가 (완료)

- [x] `Assets/02. Scripts/Pattern/Pattern.cs`
  - `[SerializeField] private AnimationClip successAnimationClip;` + `public AnimationClip SuccessAnimationClip => successAnimationClip;`
    - `[Tooltip]` "패턴을 전 노드 Good/Perfect로 완주했을 때 캐릭터가 재생할 애니메이션 클립. 비우면 무연출."
  - Pattern은 여전히 '모양 원본'이다 — 이 필드는 **정적 데이터**이지 진행 상태가 아니므로 기존 원칙과 충돌하지 않는다.
  - (재설계로 `successImpactNormalizedTime`은 **제거**됨 — contact 정렬을 쓰지 않고 창에 맞춘 배속만 쓴다.)

## Step 2: 완료 이벤트를 `PatternCompletionInfo` 페이로드로 리팩터링 (확장성) (완료)

- [x] `Assets/02. Scripts/Pattern/PatternCompletionInfo.cs` (신규) — 네임스페이스 `PatternSpace`
  - 완료 순간에 대한 **불변 페이로드**. 향후 소비자(카메라 등)가 필드를 추가해도 기존 구독자가 깨지지 않도록 하는 컨테이너.
    ```csharp
    public readonly struct PatternCompletionInfo
    {
        public readonly bool AllCorrect;         // 전 노드 Good/Perfect 완주 여부
        public readonly Pattern Pattern;         // 완성된 패턴 템플릿(모양 원본)
        public readonly float LastNodeTime;      // 마지막 노드 도달 시각(절대, 타이밍 정렬용)
        public readonly float NextLastNodeTime;  // 다음 대기 패턴의 도달 시각(겹침 방지용, 없으면 음수)
        // 향후 카메라 연출 등에서 필요해지면 여기 필드를 추가한다(예: 마지막 노드 월드 좌표, 콤보 수). 기존 구독자 영향 없음.

        public PatternCompletionInfo(bool allCorrect, Pattern pattern, float lastNodeTime, float nextLastNodeTime) { ... }
    }
    ```

- [x] `Assets/02. Scripts/Pattern/ActivePattern.cs`
  - `public float LastNodeTime => StartTime + inputTimes[inputTimes.Length - 1];` 프로퍼티 추가.
    - 마지막 노드의 이상적 도달 시각(절대). `ExpectedTime`은 `CurrentPosition` 기준이라 완료 후엔 범위를 벗어나므로 별도 프로퍼티가 필요하다.

- [x] `Assets/02. Scripts/UI/PatternHandler.cs`
  - `public event Action<bool> OnPatternComplete;` → `public event Action<PatternCompletionInfo> OnPatternComplete;`
    - 주석 갱신: `// 패턴 완료(완주/만료) 순간의 페이로드. 성공/실패·타이밍·다음 패턴 정보를 담는다.`
  - `CompletePattern()`의 발행부(`PatternHandler.cs:514`)를 아래로 변경. **`activePatterns.Remove(pattern)`이 먼저 실행된 뒤**이므로, 이 시점의 `activePatterns[0]`이 곧 다음 대기 패턴이다:
    ```csharp
    float nextLastNodeTime = activePatterns.Count > 0 ? activePatterns[0].LastNodeTime : -1f;
    OnPatternComplete?.Invoke(new PatternCompletionInfo(pattern.AllCorrect, pattern.Template, pattern.LastNodeTime, nextLastNodeTime));
    ```
  - `NextLastNodeTime`은 **다음 베기가 시작될 시각의 상한**이다(다음 패턴이 성공할지는 미정이지만, 겹침 방지는 보수적으로 "다음 베기가 있을 수 있다"고 가정해 그 전에 끝내면 된다). 다음 패턴이 큐에 없으면(채보상 공백) `-1f` → 속도 제약 없음.
  - **`ActivePattern`이 아니라 `Template`(`Pattern`)과 스칼라 값만 담는다** — 런타임 상태 객체를 외부에 노출하지 않기 위함.
  - 이 Step의 변경은 **이벤트 페이로드 형태 변경뿐**이다. 판정 로직/완료 조건은 건드리지 않는다.

- [x] `Assets/02. Scripts/Effect/EffectManager.cs`
  - `HandlePatternComplete(bool allCorrect)` → `HandlePatternComplete(PatternCompletionInfo info)`.
  - 본문은 `info.AllCorrect`를 쓰도록만 바꾸고 동작은 **그대로**다 (`allCorrect ? PatternCompleteFull : PatternComplete`). EffectManager의 동작은 **변하지 않는다**.

## Step 3: `PlayerAnimator.controller` 정리 (단일 Attack 스테이트 + 더미 키 클립) (완료)

Research에서 발견한 결함을 고치고, 클립을 주입받을 **단일 Attack 스테이트**를 더미 키 클립으로 구성한다.
현재 상태: Attack Layer에 `Attack` 스테이트(예전 `Swipe_1To9`를 이름만 바꾼 것)가 있고 아직 `Swipe_1To9.anim`이 물려 있다.
**Animator 창에서 수동 작업**한다 (`.controller` YAML 직접 편집은 fileID 정합성이 깨지기 쉬움).

- [ ] **빈 더미 클립 생성**: `Assets/05. Animations/Clip/AttackSlot_Placeholder.anim` (커브 없는 빈 `AnimationClip`).
  - 이 더미가 오버라이드의 **키**다. 런타임엔 패턴 클립으로 덮어써지므로 **실제로 재생되지 않는다**(길이 0이어도 무방).
- [ ] **Attack 스테이트의 Motion을 더미로 교체**: `Attack` 스테이트의 Motion을 `Swipe_1To9.anim` → `AttackSlot_Placeholder.anim`으로 바꾼다.
  - `Swipe_1To9.anim`은 이제 스테이트에서 떼어 내고, **Pattern SO가 직접 참조하는 실제 모션 클립**으로만 쓴다(Step 6).
- [ ] **Attack Layer의 DefaultWeight는 0으로 유지**한다 (현재 값 그대로).
  - 웨이트 0은 **휴지 상태의 정상값**이다 — 액션이 없을 땐 마스크된 레이어가 베이스 Run을 덮지 않아야 한다.
  - 재생 중에만 1로 올리는 것은 **런타임에 `CharacterActionPlayer`가 제어**한다(Step 4). 인스펙터에서 영구 1로 두지 **않는다**.
- [ ] **조건 없는 AnyState 전이 제거**
  - Attack Layer의 AnyState → `Attack`(및 남아 있다면 다른 Swipe) 전이 중 `m_Conditions: []`인 것을 모두 삭제.
  - 컨트롤러에 파라미터가 하나도 없어 조건 없는 AnyState 전이는 매 프레임 성립한다 → 웨이트를 1로 올리는 순간 스테이트가 계속 튄다.
- [ ] **잉여 Swipe 스테이트 삭제**: `Attack` 하나만 남기고 `Swipe_3To7` / `4To6` / `6To4` / `8To2` / `9To1` 스테이트가 남아 있으면 삭제한다. 클립은 스테이트가 아니라 오버라이드로 갈아끼우므로 스테이트가 여러 개일 필요가 없다.
- [ ] `Attack` → `Sprint_Forward` 복귀 전이는 **유지**한다 (`HasExitTime`, 정규화 ExitTime이라 주입 클립 길이/속도가 달라도 복귀 정상). 이미 붙어 있다.
- [ ] **Speed Multiplier 파라미터 구성(겹침 방지용)**:
  - 컨트롤러에 `float` 파라미터 `AttackSpeed`(기본값 `1`) 추가.
  - `Attack` 스테이트 인스펙터에서 **Speed = 1**, **Multiplier ✔ → `AttackSpeed`** 로 설정(`SpeedParameterActive`).
  - 런타임에 `animator.SetFloat("AttackSpeed", s)`로 이 스테이트만 속도 스케일한다. `animator.speed`(전역)는 Run/Idle까지 빨라지므로 쓰지 않는다.
- [ ] 기본 스테이트가 `Sprint_Forward`인 것은 그대로 둔다.

> **주의**: Attack 스테이트의 Motion을 완전히 비워(None) 두면 오버라이드를 걸 키가 없어 주입이 동작하지 않는다. 반드시 더미 클립을 물려 둬야 한다.
> `AttackSpeed`는 겹침 방지를 위해 추가하는 **유일한 파라미터**다. 모션을 늘려도 이 파라미터는 그대로 재사용되며 추가 파라미터/전이는 필요 없다.
> **이 `Attack` 스테이트는 베기·피격(Hit) 공용 슬롯**이다. 성공이면 베기 클립, 실패면 Hit 클립이 같은 슬롯에 주입된다 → Hit용 스테이트를 따로 만들지 않는다.

## Step 4: `CharacterActionPlayer` 신규 작성 (완료)

- [x] `Assets/02. Scripts/Character/CharacterActionPlayer.cs` (신규) — 아래 설계대로 구현 완료

구조는 `EffectManager`의 구독 패턴을 그대로 따른다.

```csharp
/// <summary>
/// 패턴 완료 시 캐릭터 액션을 재생한다. 완주 성공(AllCorrect)이면 패턴별 베기 클립을, 실패면 공용 피격(Hit) 클립을 재생한다.
/// AnimatorOverrideController로 단일 슬롯(Attack) 스테이트의 클립을 그때그때 덮어쓴 뒤 그 스테이트를 재생한다.
/// 재생할 클립이 없으면 무연출로 넘어간다 — 연출 부재가 게임 흐름을 막지 않는다.
/// </summary>
public class CharacterActionPlayer : MonoBehaviour
{
    [SerializeField] private PatternHandler handler;
    [SerializeField] private Animator animator;
    [Tooltip("클립을 주입받는 슬롯 스테이트 이름(베기·피격 공용).")]
    [SerializeField] private string attackStateName = "Attack";
    [Tooltip("액션 모션이 재생되는 레이어 이름.")]
    [SerializeField] private string attackLayerName = "Attack Layer";
    [Tooltip("Attack 스테이트에 author 타임에 물려 있는 더미 placeholder 클립(오버라이드 키). AttackSlot_Placeholder.anim.")]
    [SerializeField] private AnimationClip placeholderClip;
    [Tooltip("패턴 실패 시 재생할 피격 리액션 클립들. 번갈아 재생된다.")]
    [SerializeField] private AnimationClip[] hitClips;   // Hit1, Hit2
    [SerializeField] private float crossFadeDuration = 0.05f;
    [Tooltip("Attack 스테이트의 Speed Multiplier 파라미터 이름.")]
    [SerializeField] private string attackSpeedParam = "AttackSpeed";
    [Tooltip("겹침 방지 속도 스케일의 상한. 이 값을 넘겨야 겹침이 해소되는 극단적 경우엔 이 값에서 멈추고 겹침을 감수한다.")]
    [SerializeField] private float maxAttackSpeed = 2.5f;
    [Tooltip("액션 종료 후 Attack Layer 웨이트를 0으로 내릴 때의 페이드 시간(초).")]
    [SerializeField] private float layerFadeOutDuration = 0.08f;

    private AnimatorOverrideController overrideController;
    private int attackLayerIndex = -1;
    private int attackStateHash;
    private int attackSpeedHash;
    private int hitIndex;        // Hit 클립 번갈아 재생용 커서
    private float actionEndTime; // 현재 액션의 재생 종료 예정 시각(이 시각 이후 레이어 웨이트를 0으로 페이드)
}
```

- [ ] **`Awake()`**:
  - `attackLayerIndex = animator.GetLayerIndex(attackLayerName)`. `-1`이면 `Debug.LogError` 후 이후 재생을 건너뛴다.
  - `attackStateHash = Animator.StringToHash(attackStateName)`; `attackSpeedHash = Animator.StringToHash(attackSpeedParam)`.
  - `overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);` 후 `animator.runtimeAnimatorController = overrideController;`
    - 원본 컨트롤러를 감싼 오버라이드 인스턴스를 씌운다. 이후 이 인스턴스의 클립만 런타임에 교체한다.
  - `placeholderClip == null`이면 `Debug.LogError`로 배선 누락을 알린다 (오버라이드 키가 없으면 주입 불가).
  - `if (attackLayerIndex >= 0) animator.SetLayerWeight(attackLayerIndex, 0f);` — 휴지 상태 웨이트 0 보장.
- [ ] **`OnEnable()` / `OnDisable()`**: `handler.OnPatternComplete` 구독/해제. `handler != null` 가드는 `EffectManager`와 동일하게 둔다.
- [ ] **`HandlePatternComplete(PatternCompletionInfo info)`** — 성공/실패로 재생할 클립만 정하고 공통 메서드에 위임한다:
  ```csharp
  AnimationClip clip = info.AllCorrect
      ? (info.Pattern != null ? info.Pattern.SuccessAnimationClip : null)
      : NextHitClip();                    // 실패면 Hit 클립 번갈아
  if (clip == null) return;               // 미지정 클립 — 무연출(경고 없이)
  PlayActionClip(clip, info.NextLastNodeTime);
  ```
- [ ] **`AnimationClip NextHitClip()`**: `hitClips`가 비면 `null`. 아니면 `hitClips[hitIndex]` 반환 후 `hitIndex = (hitIndex + 1) % hitClips.Length` — **번갈아**. (랜덤을 원하면 이 한 줄만 `Random.Range`로 교체.)
- [ ] **`void PlayActionClip(AnimationClip clip, float nextLastNodeTime)`** (베기·피격 공통):
  1. `overrideController == null || placeholderClip == null || attackLayerIndex < 0` → 반환.
  2. `overrideController[placeholderClip] = clip;` — 슬롯 클립을 이번 액션 클립으로 교체.
  3. **창에 맞춘 배속 계산** — 클립은 처음부터 온전히 재생하되, 다음 액션 시작 전 창보다 길면 그 창에 맞춰 압축:
     ```csharp
     float speed = 1f;
     if (nextLastNodeTime >= 0f)                        // 다음 대기 패턴이 있을 때만 제약
     {
         float budget = nextLastNodeTime - Time.time;   // 다음 액션 시작까지 남은 실시간(창)
         if (budget > 0f && clip.length > budget)
             speed = Mathf.Min(clip.length / budget, maxAttackSpeed);
     }
     animator.SetFloat(attackSpeedHash, speed);
     ```
     - `nextLastNodeTime < 0`(다음 패턴 없음)이거나 창이 넉넉하면 **1배속**으로 온전히 재생.
     - 클립이 창보다 길면 **배속으로 압축**해 잘리지 않게 한다. `maxAttackSpeed`로도 못 맞추는 극단 간격에선 다음 액션의 CrossFade 재진입이 잔여를 끊는다(단일 스테이트라 동시 재생 불가). → **겹침은 어떤 경우에도 발생하지 않는다.**
     - **클립 앞부분을 잘라내지 않는다**(startOffset 없음) — 항상 처음부터 재생.
  4. **레이어 활성화 + 종료 시각 기록**:
     ```csharp
     animator.SetLayerWeight(attackLayerIndex, 1f);           // 재생 동안 마스크 레이어 활성
     actionEndTime = Time.time + clip.length / speed;         // 이 시각 이후 웨이트를 0으로 페이드
     ```
     연속 액션이면 다음 재생이 `actionEndTime`을 더 뒤로 밀어 웨이트 1을 유지한다.
  5. `animator.CrossFadeInFixedTime(attackStateHash, crossFadeDuration, attackLayerIndex, 0f);` — 항상 처음부터.
- [ ] **`Update()`** — 액션 종료 후 레이어 비활성화:
  ```csharp
  if (attackLayerIndex < 0) return;
  float w = animator.GetLayerWeight(attackLayerIndex);
  if (Time.time >= actionEndTime && w > 0f)                    // 재생이 끝났으면 0으로 페이드
  {
      w = Mathf.MoveTowards(w, 0f, Time.deltaTime / Mathf.Max(layerFadeOutDuration, 0.0001f));
      animator.SetLayerWeight(attackLayerIndex, w);
  }
  ```
  - 재생 중(`Time.time < actionEndTime`)에는 손대지 않아 웨이트 1 유지. 종료 후에만 0으로 내린다.
  - `Attack → Sprint_Forward` 복귀 전이는 그대로 유지한다(웨이트가 0으로 내려가는 동안 레이어 내부가 기본 상태로 정리되어 다음 액션의 CrossFade 시작점이 깔끔해진다).
- [ ] **불필요한 재설정 회피(선택)**: 마지막으로 주입한 클립을 캐싱해, 같은 클립이 연속되면 오버라이드 대입을 건너뛴다. (비용이 작아 필수는 아님.)

## Step 5: 씬 배선 (완료)

- [ ] `Assets/01. Scenes/DefaultScene.unity`
  - 씬의 `School_Katana_FullBody-Magica cloth2` 인스턴스에 `CharacterActionPlayer` 컴포넌트 부착.
  - `handler` ← 씬의 `PatternHandler` 오브젝트.
  - `animator` ← 같은 오브젝트의 Animator (`PlayerAnimator` 컨트롤러가 오버라이드된 것).
  - `placeholderClip` ← `Assets/05. Animations/Clip/AttackSlot_Placeholder.anim` (Attack 스테이트에 물린 더미 클립 = 오버라이드 키).
  - `hitClips` ← `Hit1` / `Hit2` (`Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Normal/Hit1.fbx`, `Hit2.fbx`의 임베디드 AnimationClip 서브에셋 2개).
  - `attackStateName`(`Attack`) / `attackLayerName`(`Attack Layer`) / `attackSpeedParam`(`AttackSpeed`)은 기본값 그대로. `maxAttackSpeed`는 기본 2.5(필요 시 조정).

## Step 6: Pattern 에셋에 클립 참조 + contact 시각 기입 (완료)

- [ ] `Assets/04. Datas/Patterns/Templates/Pattern_3Node_(0, 4, 8).asset`
  - `successAnimationClip` ← `Swipe_1To9.anim` (Point 1→5→9, 기존 클립 중 **유일하게 대응되는 패턴**).
  - `successImpactNormalizedTime` ← `Swipe_1To9.anim`에서 칼이 닿는 프레임의 정규화 위치. Play 모드 검증에서 눈으로 맞춰 조정한다(초기값은 대략 0.3~0.4로 두고 Step 7에서 튜닝).
- [ ] 나머지 4개 템플릿(`(4)`, `(0, 3, 6)`, `(6, 3, 0, 1)`, `(8, 5, 2, 1)`)은 대응 모션이 없으므로 **비워 둔다** → 성공해도 베기 무연출.
  - 모션을 늘리려면 클립을 만들어 이 필드에 꽂고 contact 시각만 맞추면 된다. **컨트롤러·코드 수정 불필요.**
- [ ] 실패 시 피격(Hit)은 **패턴별 데이터가 아니다.** 위 4개 패턴을 포함해 모든 패턴이 실패하면 공용 Hit 클립(번갈아)이 재생된다 → 여기서 별도로 기입할 것 없음.

## Step 7: 검증

- [ ] 컴파일 확인 (`OnPatternComplete`를 `Action<PatternCompletionInfo>`로 바꿨으므로, 기존 구독자 `EffectManager` 핸들러가 새 시그니처로 갱신됐는지 — Research 기준 구독자는 하나뿐).
- [ ] Play 모드에서 `PatternHandler`의 `Debug: Set Test Pattern` 컨텍스트 메뉴로 `Pattern_3Node_(0, 4, 8)` 재생:
  - **전 노드를 Good/Perfect로 완주** → 캐릭터가 `Swipe_1To9` 클립(베기) 재생 후 `Sprint_Forward`로 복귀.
  - **한 노드를 Miss/오답** → **피격(Hit)** 재생.
  - **입력하지 않고 만료** → **피격(Hit)** 재생.
- [ ] **피격 분기 검증**: 실패를 여러 번 반복해 Hit 클립이 **번갈아**(Hit1 → Hit2 → Hit1 …) 나오는지 확인.
  - Hit 클립이 Attack Layer 마스크(`0399ae15…`) 하에서 자연스럽게 보이는지 확인 — 상체 마스크라면 피격 리액션이 상체만 표현될 수 있다. 어색하면 마스크/레이어 조정은 후속으로(범위 밖).
- [ ] **재생/배속 검증**: 클립이 **처음부터 온전히**(앞부분 잘림 없이) 재생되는지 확인. 다음 패턴까지의 창보다 클립이 길면 **배속으로 압축**되어 잘리지 않는지, 창이 넉넉하면 1배속인지 확인.
- [ ] 모션 미지정 패턴(예: `Pattern_1Node_(4)`) 완주 → 모션 없음, **에러/경고 로그도 없음**.
- [ ] **서로 다른 클립을 가진 두 패턴을 연달아 완주** → 두 번째가 첫 번째 클립이 아니라 자기 클립으로 재생되는지(오버라이드 교체가 반영되는지) 확인. (지금은 대응 클립이 하나뿐이라, 임시로 다른 템플릿에 다른 클립을 꽂아 확인.)
- [ ] **겹침 방지 검증**: 클립 길이보다 짧은 간격으로 두 패턴을 연속 완주시켜, 앞 베기가 **다음 베기 시작 전에 끝나도록 가속**되는지 확인. 로그로 계산된 `speed`/`budget`을 찍어 검증하면 편하다.
  - 간격이 충분한 경우 → 1배속으로 온전히 재생 후 복귀하는지도 확인.
  - `maxAttackSpeed`를 넘겨야 하는 극단 간격에서도 두 베기가 **동시에 재생되지 않는지**(단일 스테이트 보장) 확인.
- [ ] **레이어 활성/비활성 검증**: 휴지 상태에서 Attack Layer 웨이트가 0이라 **베이스 Run만** 보이고, 액션(베기/피격) 재생 중에만 웨이트가 1로 올랐다가 종료 후 0으로 내려가는지 확인(Animator 창의 Layers 웨이트 슬라이더로 실시간 관찰).
  - 연속 액션 시 웨이트가 중간에 0으로 떨어지지 않고 1을 유지하는지 확인.
- [ ] Step 3의 AnyState 전이를 지웠으므로, 대기 중 캐릭터가 스테이트 사이를 튀지 않는지 확인.
- [ ] 기존 이펙트(`EffectManager`의 `PatternCompleteFull`/`PatternComplete`)가 이전과 동일하게 동작하는지 확인 — 이번 변경으로 회귀가 없어야 한다.

---

## 범위 밖 (이번에 하지 않음)

- 새 베기 클립 제작 — 기존 클립을 쓰고, 대응 없는 패턴은 무연출.
- 판정별 모션 분기(Perfect일 때만 강한 베기 등) — 이번엔 `AllCorrect` 단일 기준.
- 노드 단위 모션(`OnFallingNodeResolved` 구독) — 이번엔 패턴 단위만.
- **예비동작(windup)까지 온전히 보여주는 예측 재생** — 마지막 노드 도달 *이전에* 미리 베기를 시작해야 하므로, 완료(성공 여부) 확정 전에 재생을 거는 별도 훅이 필요하다. 이번엔 완료 시점에 시작하고 contact를 도달 시각에 정렬(클립 중반부터 시작)하는 반응형 방식만 구현한다.
- 벨 오브젝트(잘리는 대상) 스폰/이동 시스템 자체 — 이번 플랜은 "오브젝트가 마지막 노드 도달 시각에 캐릭터 앞에 온다"는 가정만 두고 베기 타이밍을 그 시각에 맞춘다.
- **카메라 연출 자체** — 지금 구현하지 않는다. `OnPatternComplete(PatternCompletionInfo)` 이벤트만 확장성 있게 준비해 두어, 나중에 `CameraDirector`(가칭)가 이 이벤트를 구독하고 필요한 데이터는 구조체에 필드로 추가하면 되도록 한다.
- Eye Blink Layer / Running Layer 관련 정리.
