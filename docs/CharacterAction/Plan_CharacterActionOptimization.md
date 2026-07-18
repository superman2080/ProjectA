# Plan: Character Action Animation Optimization

근거: `docs/CharacterAction/Research_CharacterActionOptimization.md`

이 계획은 캐릭터 애니메이션의 팝핑 현상(툭 끊김)을 해결하고 불필요한 선/후동작을 제거하여 물 흐르듯 자연스럽고 반응성 좋은 연출을 만드는 것을 목표로 합니다.

---

## 단계별 구현 계획

### 1단계: `Pattern.cs` 트리밍(Trim) 필드 추가
- [ ] `Assets/02. Scripts/Pattern/Pattern.cs` 수정:
  - `animationStartOffset` (SerializeField, float): 애니메이션의 시작 지점 오프셋(초). (기본값: 0)
  - `animationDuration` (SerializeField, float): 애니메이션 재생 지속 시간(초). (기본값: 0 - 0 이하면 클립 끝까지 재생)
  - 각각에 대한 Getter 프로퍼티 제공: `AnimationStartOffset`, `AnimationDuration`.

---

### 2단계: `CharacterActionPlayer.cs` 듀얼 슬롯 및 트리밍 반영 리팩터링
- [ ] `Assets/02. Scripts/Character/CharacterActionPlayer.cs` 수정:
  - **듀얼 슬롯 필드 정의**:
    - `attackStateName` 단일 스트링 필드를 제거하거나 유지하되, 내부적으로 `Attack_A`, `Attack_B` 스테이트 명을 가질 수 있도록 확장합니다:
      - `[SerializeField] private string attackStateAName = "Attack_A";`
      - `[SerializeField] private string attackStateBName = "Attack_B";`
    - `placeholderClip` 단일 필드를 제거하고 각각에 대응하는 더미 키 클립 필드 정의:
      - `[SerializeField] private AnimationClip placeholderA;`
      - `[SerializeField] private AnimationClip placeholderB;`
  - **런타임 토글 상태 관리**:
    - `private bool useSlotA = true;` 변수를 두어 연출 시점마다 슬롯을 교대로 전환합니다.
  - **`Awake()` 수정**:
    - 양쪽 스테이트의 해시값 구하기 (`attackStateAHash`, `attackStateBHash`).
    - `overrideController` 초기화 후, `placeholderA`와 `placeholderB` 양쪽에 배선 경고 로그 체크 추가.
  - **`PlayActionClip(AnimationClip clip, Pattern pattern, float nextLastNodeTime)` 수정**:
    - `Pattern` 데이터를 파라미터로 직접 전달받거나 `PatternCompletionInfo`를 전달받도록 서그니처 변경.
    - 현재 재생할 슬롯 선택:
      - `string targetStateName = useSlotA ? attackStateAName : attackStateBName;`
      - `int targetStateHash = useSlotA ? attackStateAHash : attackStateBHash;`
      - `AnimationClip targetPlaceholder = useSlotA ? placeholderA : placeholderB;`
    - 새로운 클립을 선택한 슬롯에 오버라이딩:
      - `overrideController[targetPlaceholder] = clip;`
    - **트리밍(Trim) 계산**:
      - `float startOffset = pattern != null ? pattern.AnimationStartOffset : 0f;`
      - `float duration = pattern != null && pattern.AnimationDuration > 0f ? pattern.AnimationDuration : (clip.length - startOffset);`
    - **배속(Speed) 계산**:
      - `float speed = 1f;`
      - `if (nextLastNodeTime >= 0f)` 조건문 내에서 `clip.length` 대신 트리밍된 `duration`을 기준으로 budget 계산:
        - `float budget = nextLastNodeTime - Time.time;`
        - `if (budget > 0f && duration > budget) speed = Mathf.Min(duration / budget, maxAttackSpeed);`
    - `animator.SetFloat(attackSpeedHash, speed);` 적용.
    - **재생 및 페이드 개시 시각 설정**:
      - `animator.SetLayerWeight(attackLayerIndex, 1f);`
      - `actionEndTime = Time.time + duration / speed;`
    - **크로스페이드 호출**:
      - `animator.CrossFadeInFixedTime(targetStateHash, crossFadeDuration, attackLayerIndex, startOffset);`
      - 4번째 인자 `fixedTimeOffset`에 `startOffset`을 대입하여 선동작(선딜레이)을 스킵하고 즉시 타격 모션이 나오도록 구동!
    - **슬롯 전환**:
      - `useSlotA = !useSlotA;` 토글 수행.

---

### 3단계: `PlayerAnimator.controller` 듀얼 슬롯 상태 구성
- [ ] Animator Controller 에디터 작업 (또는 스크립트를 통해 에셋 편집):
  - `Attack Layer` 내부에 `Attack_A` 와 `Attack_B` 스테이트를 신설합니다.
  - `Attack_A` 스테이트의 Motion에 `placeholderA` 클립을 물리고, Speed Multiplier에 `AttackSpeed` 파라미터를 연동합니다.
  - `Attack_B` 스테이트의 Motion에 `placeholderB` 클립을 물리고, Speed Multiplier에 `AttackSpeed` 파라미터를 연동합니다.
  - 각 스테이트에서 `Sprint_Forward`로 빠져나가는 복귀 전이(`HasExitTime = true`)를 추가해 줍니다.
  - 기존의 단일 `Attack` 스테이트 및 쓰지 않는 Swipe 관련 찌꺼기 스테이트들을 모두 삭제하여 레이어를 아주 깔끔하게 정리합니다.

---

### 4단계: 씬 및 에셋 데이터 튜닝
- [ ] 더미 placeholder 클립 2개 확보: `Placeholder_A.anim`, `Placeholder_B.anim`
- [ ] 씬의 `CharacterActionPlayer` 컴포넌트 인스펙터 배선:
  - `placeholderA` <- `Placeholder_A.anim`
  - `placeholderB` <- `Placeholder_B.anim`
- [ ] 패턴 에셋 데이터 튜닝:
  - `Pattern_3Node_(0, 4, 8).asset` (Swipe_1To9.anim 사용):
    - `animationStartOffset`: 선딜레이 분석 후 적절히 설정 (예: 0.1s ~ 0.2s)
    - `animationDuration`: 후딜레이 검집 복귀동작 스킵을 위해 적절히 설정 (예: 0.3s ~ 0.4s)
  - 기타 패턴들도 필요시 트리밍 설정 추가.

---

### 5단계: 검증 및 고도화
- [ ] 컴파일 성공 여부 및 콘솔 로그 오류 없음 확인.
- [ ] Play 모드 테스트 실행:
  - 패턴 완료 시 동작 끊김이나 T-포즈 튀는 현상(Popping)이 완전히 사라지고 부드럽게 크로스페이드되는지 확인.
  - 패턴 완료 즉시 시원하게 베어넘기는 모션이 발동되는지 확인.
  - 타격이 끝난 후 허우적대지 않고 신속하게 달리기 모션으로 돌아오는지 확인.
