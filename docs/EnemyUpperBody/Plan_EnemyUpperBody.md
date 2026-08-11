# Plan — 적 배회 상하체 분리 (EnemyUpperBody)

근거: `docs/EnemyUpperBody/Research_EnemyUpperBody.md`

## 목표

배회(`Walk`) 구간에서 **하체는 4방향 블렌드 트리, 상체는 Idle 클립 하나**로 분리해
칼 파지가 섞이는 문제를 없앤다. 상체 클립은 `Samurai_Idle` / `Samurai_BlockIdle` 중 **랜덤 1종**.

## 설계 요약

```
Base Layer          : Idle / Run / Walk(WalkBlend) / Attack / Death / Parry / Evade / KnockBack   ← 그대로
Upper Body Layer(신): UpperIdle 스테이트 하나, 마스크 = 상체, weight 0↔1을 코드가 몬다
                      └ placeholder 클립을 런타임에 두 Idle 중 하나로 교체(기존 attack/death 슬롯과 같은 방식)
```

**켜는 조건은 `walkingNow` 하나다.** 이미 "지금 배회 걷는 중인가"의 진실의 원천이고 히스테리시스까지 들어 있어,
새 판정을 만들면 그것과 어긋나는 순간이 생긴다. 결투 이동(`Run`)은 단일 클립이라 켜지 않는다.

---

## Step 1 — 아바타 마스크 에셋

- [x] 1-1. `Assets/05. Animations/Avatar/EnemyUpperBodyMask.mask` 생성 (`Create > Avatar Mask`).
- [x] 1-2. **Humanoid body-part 토글만 쓴다. Transform 목록은 비운다.**
      적·플레이어의 본 이름 체계가 완전히 달라(`Spine` vs `root/pelvis/spine_01`) path 기반 마스크는 리그에 묶인다.
      body-part 마스크는 Humanoid 리그 어디에도 붙는다.
- [x] 1-3. 초기값: **`LeftArm` · `RightArm` · `LeftFingers` · `RightFingers` 만 켠다.**
      `Root` · `Body`(척추) · `Head` · 다리 · IK는 **끈다** — 걷는 몸통·머리 흔들림과 이동은 base가 계속 소유해야 한다.
      ⚠ 파지가 여전히 흔들리면 그때 `Body`를 추가한다(몸통이 뻣뻣해지는 대가와 맞바꾸는 노브다. Research 제약 1).
- [x] 1-4. 기존 `PlayerAvatarMask.mask`는 **건드리지 않는다**(어느 컨트롤러도 안 쓰는 잔해지만 이 작업과 무관하다).

## Step 2 — 애니메이터 레이어

- [x] 2-1. `EnemyAnimator.controller`에 레이어 추가:
      - 이름 **`Upper Body Layer`**
      - Mask = `EnemyUpperBodyMask`
      - Blending = **Override**
      - Weight = **0** (기본값. 코드가 올린다 — 씬/에셋 값이 1이면 곡 시작부터 상체가 굳는다)
      - IK Pass 끔, Sync 끔
- [x] 2-2. 그 레이어에 스테이트 하나: 이름 **`UpperIdle`**, Motion = **placeholder 클립**.
      placeholder는 `Samurai_Idle`을 그대로 꽂아 둔다(런타임 교체 전에도 그림이 성립하도록).
      전이 없음 — 진입은 코드의 `CrossFadeInFixedTime` 하나뿐이다(§6 "Release 진입은 코드가 유일하게 통제한다"와 같은 규율).
- [x] 2-3. **파라미터를 추가하지 않는다.** 상체는 배속을 조절할 이유가 없다(Idle 루프).

## Step 3 — `EnemyView` 필드

- [x] 3-1. `[Header("Upper Body")]` 아래 직렬화 필드 추가:
  ```csharp
  [Tooltip("배회 중 상체를 덮을 레이어 이름. 비우면 이 기능 전체가 조용히 꺼진다(기존 배선 누락 규율).")]
  [SerializeField] private string upperBodyLayerName = "Upper Body Layer";
  [SerializeField] private string upperBodyStateName = "UpperIdle";
  [Tooltip("UpperIdle 스테이트에 물려 있는 placeholder. 런타임에 아래 후보 중 하나로 교체된다.")]
  [SerializeField] private AnimationClip upperBodyPlaceholder;
  [Tooltip("배회 진입마다 하나를 뽑는다. 직전에 쓴 것은 피한다. 비면 placeholder가 그대로 쓰인다.")]
  [SerializeField] private AnimationClip[] upperBodyIdleClips;   // Samurai_Idle, Samurai_BlockIdle
  [Tooltip("상체 레이어 웨이트를 올리고 내리는 시간(초). 0이면 파지가 순간이동한다.")]
  [SerializeField] private float upperBodyBlendDuration = 0.18f;
  ```
- [x] 3-2. 런타임 상태: `upperBodyLayerIndex`(-1 = 미배선) · `upperBodyStateHash` · `upperBodyWeight` · `lastUpperClipIndex`.
- [x] 3-3. `Awake`에서 `animator.GetLayerIndex(upperBodyLayerName)` 해석. **못 찾으면 경고 없이 -1로 두고 기능만 끈다**
      — 이 프로젝트의 연출 배선 규율(빠진 기능만 조용히 비활성)을 따른다.

## Step 4 — 켜고 끄기

- [x] 4-1. `TickWander()`의 마지막 `CrossFadeSticky(walkingNow ? walkStateName : idleStateName)` **직후**에
      `ApplyUpperBody(walkingNow)` 한 줄을 넣는다. **판정을 새로 만들지 않는다.**
- [x] 4-2. `ApplyUpperBody(bool want)`:
      - `want`가 **false→true로 바뀌는 순간에만** 클립을 뽑아 `overrideController[upperBodyPlaceholder] = clip` 하고
        `CrossFadeInFixedTime(upperBodyStateHash, upperBodyBlendDuration, upperBodyLayerIndex)`.
        (매 프레임 뽑으면 클립이 떨리고, 스폰 때 한 번만 뽑으면 그 적은 곡 내내 같은 포즈다 — Research 제약 6)
      - 목표 웨이트(1 또는 0)로 `upperBodyWeight`를 `upperBodyBlendDuration` 동안 이동시켜 `SetLayerWeight`.
- [x] 4-3. `Update()`에서 웨이트 보간을 매 프레임 진행시킨다. 배회가 아닌 프레임에도 **내려가는 중일 수 있으므로**
      `TickWander` 안이 아니라 `Update` 본문에서 돌린다(`wandering`이 false가 되면 `TickWander`가 즉시 return하기 때문).
- [x] 4-4. **공격·사망·리액션에서 즉시 0으로 강제한다.** 그 클립들은 상체가 전부라 마스크가 덮으면 칼을 안 휘두른다.
      진입점이 여럿이므로 **한 곳에서 막는다** — `ApplyUpperBody`가 스스로 아래를 확인하고 want를 false로 꺾는다:
      `Current == Phase.Windup || Current == Phase.Dying || Time.time < reactionUntil`
      (`ApplyLocomotion`이 쓰는 것과 **같은 조건**이다. 새 술어를 만들지 않는다.)
- [x] 4-5. `StopWander()`에서도 `ApplyUpperBody(false)` — 배회가 외부에서 끊길 때 웨이트가 남지 않도록.

## Step 5 — 클립 선택

- [x] 5-1. `NextUpperIdleClip()`: 후보가 2개 이상이면 **직전 인덱스를 제외하고** 랜덤.
      1개면 그것, 0개면 null(→ placeholder 유지).
      `CharacterActionPlayer.NextHitClip`(교대) · `DodgeDirector.lastClip`(직전 회피)과 같은 관례다.
- [x] 5-2. ⚠ **`Samurai_Idle`은 base `Idle` 스테이트가 쓰는 바로 그 클립이다.** 뽑혔을 때 배회를 멈추면
      상·하체가 같은 클립이 되어 매끄럽게 이어진다 — 버그가 아니라 이득이므로 제외하지 않는다.

## Step 6 — 풀 반납 정리

- [x] 6-1. `ResetState()`에 추가: `upperBodyWeight = 0f`, `SetLayerWeight(upperBodyLayerIndex, 0f)`, `lastUpperClipIndex = -1`.
      안 하면 **다음 대여가 상체가 굳은 채 무대에 나온다**(`reactionUntil`을 여기서 지우는 것과 같은 이유).

## Step 7 — 검증

- [x] 7-1. 컴파일 + EditMode 테스트 green (이 작업은 코어 로직을 안 건드리므로 기존 120건이 그대로 통과해야 한다).
- [ ] 7-2. **대각선 배회를 눈으로 본다** — 전진+옆걸음이 섞이는 각도에서 왼손이 칼자루에 붙어 있는가.
      (이게 이 작업의 유일한 성공 기준이다.)
- [ ] 7-3. **공격·사망·패링·회피에서 상체가 정상인가.** 특히 배회하다 바로 표적이 되는 전환에서
      웨이트가 내려가기 전에 공격이 시작되지 않는지 본다(4-4가 그 자리다).
- [ ] 7-4. 배회 시작/정지에서 파지가 **튀지 않고 스며드는지**(`upperBodyBlendDuration` 튜닝).
- [ ] 7-5. 적이 여럿 동시에 배회할 때 **서로 다른 상체 클립이 섞여 있는지**(같은 클립만 나오면 5-1이 안 도는 것이다).
- [ ] 7-6. 무대에서 적이 죽고 새로 스폰된 뒤 **상체가 굳어 있지 않은지**(6-1 확인).

---

## 범위 밖 (하지 않는다)

- **플레이어** — base 로코모션이 전부 단일 클립이라 블렌드 자체가 없다.
- **`Run`(결투 접근)** — 단일 클립. 레이어를 켜면 오히려 달리기 상체가 죽는다.
- **`WalkForward` 클립 교체(Research 대안 A)** — 두 손 전진 클립이 생기면 그때 별도로 본다.
  이 작업과 배타적이지 않다(마스크를 꺼도 그 해법은 성립한다).
- **`PlayerAvatarMask.mask` 정리** — 어느 컨트롤러도 안 쓰는 잔해지만 이 작업과 무관하다.

---

## 실행 결과 (2026-08-10)

Step 1~6 구현 완료. **컴파일 에러 0 · 경고 0, EditMode 테스트 120/120 green**(7-1).

### 만든 것
- `Assets/05. Animations/Avatar/EnemyUpperBodyMask.mask` — body-part 토글만 켠 Humanoid 마스크.
  검증: `LeftArm·RightArm·LeftFingers·RightFingers = True`, 나머지 9개 전부 False.
- `EnemyAnimator.controller` 레이어 `[1] Upper Body Layer` — Override · weight 0 · mask 연결 · `UpperIdle`(default, motion=`Samurai_Idle`).
  ⚠ 레이어 추가는 YAML 직접 편집이 아니라 **에디터 API**(`AnimatorController.AddLayer`)로 했다 — 스테이트 머신 서브에셋과 fileID를 손으로 맞추면 조용히 깨진다.
- `Enemy_Samurai_Male.prefab`의 `EnemyView`에 클립 배선: placeholder=`Samurai_Idle`, 후보 2종(`Samurai_Idle`, `Samurai_BlockIdle`).

### 코드 (`EnemyView`)
- `SetUpperBody(bool)` — 목표 전환. **진입 순간에만** 클립을 뽑아 `overrideController`로 교체 후 해당 레이어에 `CrossFadeInFixedTime`.
- `TickUpperBody()` — `Update`에서 매 프레임 웨이트 보간. **`TickWander` 안이 아니다**(배회가 끝나면 그 메서드가 즉시 return해 웨이트가 1에 굳는다).
- `NextUpperIdleClip()` — 직전 인덱스 회피 랜덤.
- 강제 해제는 **두 곳**이다: `SetUpperBody` 진입 시(요청을 꺾음) + `TickUpperBody` 매 프레임(이미 켜진 뒤 공격이 시작된 경우).
  조건은 `ApplyLocomotion`과 같은 식(`Windup || Dying || reactionUntil`).
- `StopWander` · `ResetState`에서 해제/리셋.

### 남은 것 — 플레이 검증 (7-2 ~ 7-6)
에디터 검증으로는 여기까지다. 실제로 봐야 할 것:
1. **대각선 배회에서 왼손이 칼자루에 붙는가** — 이 작업의 유일한 성공 기준
2. 공격·사망·패링·회피 상체가 정상인가(특히 배회→표적 전환)
3. 파지 전환이 튀지 않는가(`upperBodyBlendDuration` 0.18 튜닝)
4. 적 여럿이 서로 다른 상체 클립을 쓰는가
5. 죽고 새로 스폰된 적의 상체가 굳어 있지 않은가

### 튜닝 노브
파지가 여전히 흔들리면 마스크에 **`Body`(척추)를 추가**한다(Step 1-3). 대가는 걷는 몸통이 뻣뻣해지는 것.
