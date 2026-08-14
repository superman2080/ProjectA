# Research — 적 배회 상하체 분리 (EnemyUpperBody)

## 문제

`WalkBlend`(2D 블렌드 트리)가 섞는 네 클립의 **칼 파지가 다르다**.

| 자식 | 클립 | 파지 |
|---|---|---|
| 0 | `WalkForward.anim` | **한 손** |
| 1 | `WalkBackward_HS.anim` | 두 손 |
| 2 | `WalkLeft_HS.anim` | 두 손 |
| 3 | `WalkRight_HS.anim` | 두 손 |

(`_HS` = 접미사가 곧 파지 구분이다. `Sprint_HS`도 두 손.)

블렌드는 **본 회전을 가중 평균**하므로, 전진 성분과 옆걸음 성분이 함께 섞이는 대각선 이동에서
왼손이 칼자루와 허공 사이 어중간한 위치로 간다 — **"두 손도 한 손도 아닌" 포즈**가 나온다.
배회는 궤도 슬롯을 따라가는 동작이라 대각선 성분이 상시 존재해서, 이 구간 대부분이 그 상태다.

## 현재 구조

### 애니메이터 — `Assets/05. Animations/Animator/EnemyAnimator.controller`
- **레이어가 `Base Layer` 하나뿐이다.** 아바타 마스크는 어디에도 쓰이지 않는다.
- 스테이트: `Idle`(= `Samurai_Idle`) · `Run`(= `Samurai_Run`) · `Walk`(= `WalkBlend`, `m_BlendType: 2` = 2D Freeform Cartesian) · `Attack` · `Death` · `Parry` · `Evade` · `KnockBack`
- 파라미터: `AttackSpeed` · `DeathSpeed` · `ParrySpeed` · `MoveX` · `MoveY` · `WalkSpeed`

### 리그 — 둘 다 **Humanoid**
| 대상 | 아바타 | 본 이름 |
|---|---|---|
| 적 `Enemy_Samurai_Male.prefab` | `Samurai_Katana` FBX (`animationType: 3`) | `Spine` · `RightArm` · `Bip001 …` |
| 플레이어 | `CombatGirls` (`animationType: 3`) | `root/pelvis/spine_01 …` |

**⚠ 본 이름 체계가 완전히 다르다** → 기존 `Assets/05. Animations/Avatar/PlayerAvatarMask.mask`는
**transform path 기반**이라 적에게 재사용할 수 없다(그리고 그 마스크는 지금 어느 컨트롤러도 쓰지 않는다).

반면 둘 다 Humanoid이므로 **body-part 토글 마스크**(`m_Mask` 13비트)는 리그와 무관하게 동작한다 —
새 마스크는 transform 목록 없이 body-part만 켜서 만든다.

### 코드 — `Assets/02. Scripts/Enemy/EnemyView.cs`
- 로코모션 진입은 **두 경로**다:
  - `ApplyLocomotion()` — 결투 이동. `moving && wantsLocomotion` 이면 `moveStateName`("Run"), 아니면 `idleStateName`("Idle")
  - `TickWander()` — 배회. `walkingNow` 이면 `walkStateName`("Walk"), 아니면 `Idle`. **문제가 나는 곳은 여기다**
- `walkingNow`는 히스테리시스(`walkEnterSpeed` 0.30 / `walkExitSpeed` 0.12)로 켜지고 꺼진다 — **이미 "지금 걷는 중인가"의 진실의 원천이 있다.**
- 블렌드 파라미터는 **방향만**(정규화) 넣고 속도는 `WalkSpeed` 배속이 담당한다(`walkClipSpeed` 1.297 m/s 기준).
- `CrossFade`/`CrossFadeSticky`는 **레이어 0을 하드코딩**한다(`HasState(0, …)`, `GetCurrentAnimatorStateInfo(0)`).
  새 레이어를 추가해도 이 경로는 그대로 base만 본다 — 충돌하지 않지만, 새 레이어 제어는 별도 코드가 필요하다.
- 인스턴스마다 `AnimatorOverrideController`를 이미 만든다(Awake). `attackPlaceholder`/`deathPlaceholder`를
  런타임에 갈아끼우는 **기존 관례가 있다** — 상체 클립도 같은 방식을 쓸 수 있다.
- `ResetState()`가 풀 반납 시 상태를 되돌린다. **레이어 웨이트도 여기서 0으로 되돌려야 한다**(안 하면 다음 대여가 상체가 굳은 채 나온다).

### 상체에 쓸 클립
| 클립 | GUID | 비고 |
|---|---|---|
| `Idle/Samurai_Idle.anim` | `822a0587…` | **이미 `Idle` 스테이트가 쓰는 클립** |
| `Idle/Samurai_BlockIdle.anim` | `6c6efaac…` | 현재 미사용 |

`Samurai_Idle`이 base `Idle`과 같은 클립이라는 점에 주의 — 배회를 멈춘 순간 상·하체가 같은 클립이 되어
자연스럽게 이어진다(오히려 이득). 두 클립 다 두 손 파지로 확인 필요(저작 단계 검증 항목).

## 대안 비교

| 안 | 내용 | 판정 |
|---|---|---|
| **A. 전진 클립 교체** | 두 손 파지 `WalkForward_HS`를 구해/저작해 블렌드 트리에 꽂는다 | 코드 0줄로 끝나는 가장 싼 해법. **다만 그 클립이 없다** — 새로 저작하거나 외부 에셋에서 찾아야 하고, 그건 이 작업의 범위 밖이다 |
| **B. 상체 마스크 레이어** (요청안) | 하체는 블렌드 트리, 상체는 Idle 클립 하나로 고정 | 파지가 **클립 하나에서만** 오므로 섞일 수가 없다. 방향이 몇 개로 늘어도 안 깨진다 |
| C. 블렌드 트리에서 전진 제외 | `WalkForward`를 빼고 세 방향만 | 전진이 옆걸음으로 대체돼 이동 방향과 발 방향이 어긋난다 |

**B로 간다.** A는 클립이 생기면 그때 다시 볼 수 있고, B와 배타적이지 않다(마스크를 꺼도 A가 성립한다).

## 제약·함정

1. **마스크에 척추(Body)를 넣을지가 핵심 튜닝 노브다.** 팔만 덮으면 걷기의 상체 흔들림은 살지만, Idle 클립의 팔 포즈가
   자기 척추 포즈를 전제로 저작돼 있어 파지 위치가 조금 흔들릴 수 있다. 척추까지 덮으면 파지는 완벽히 고정되지만
   **걷는 몸통이 뻣뻣해진다.** 먼저 팔·손·손가락만으로 시작하고 부족하면 Body를 추가한다.
2. **레이어 웨이트를 즉시 0↔1로 던지면 팝이 난다.** 배회 진입/이탈은 히스테리시스로 이미 부드럽지만
   웨이트 자체는 별도로 페이드해야 한다(`CharacterActionPlayer.ApplyBlendIn/Out`과 같은 규율).
3. **공격·사망·리액션 구간에는 절대 켜져 있으면 안 된다.** 그 클립들은 상체가 전부다 — 마스크가 덮으면 칼을 안 휘두른다.
   `walkingNow`가 그 구간에 true가 될 수 없는지 확인 필요(`CanWander()` → `IsIdle` → `BusyReasonBy`가 이미 막는다).
4. **풀 반납 시 웨이트 리셋**(위 `ResetState`).
5. **`Run`(결투 접근)은 단일 클립이라 이 문제가 없다.** 레이어를 켜면 오히려 달리기 상체가 죽는다 — **Walk에서만** 켠다.
6. 랜덤 선택 시점은 **배회 진입 순간**이어야 한다. 매 프레임 뽑으면 클립이 떨리고, 스폰 시 한 번만 뽑으면 그 적은 곡 내내 같은 포즈다.
7. 같은 클립이 연달아 나오면 랜덤이 아니라 고장으로 보인다 — `DodgeDirector.lastClip`, `CharacterActionPlayer.hitIndex`와 같은
   "직전 것 피하기" 관례가 이미 프로젝트에 있다.

## 플레이어에게도 같은 문제가 있는가

플레이어 base 로코모션은 `Sprint`(단일) / `Quickshift`(단일) / `Katana_Idle`(단일)이라 **블렌드가 없다** → 해당 없음.
이 작업은 적 전용이다.
