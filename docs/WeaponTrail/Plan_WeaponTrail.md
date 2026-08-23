# Plan: 칼날 트레일 (WeaponTrail)

근거: [Research_WeaponTrail.md](Research_WeaponTrail.md)

## 목표

**베기(성공) 클립이 재생되는 트림 구간 동안에만** 칼날의 `Tiny.Trail`을 enable하고, 트림이 끝나면 disable한다.
트레일 컴포넌트와 `points` 오서링은 이미 씬에 되어 있으므로 **켜고 끄는 시점만** 만든다.

## 확정된 설계 결정

| 결정 | 내용 |
|---|---|
| 활성 구간 | **트림 시작(`PlaySlot`) ~ 트림 끝(`actionEndTime`)** — 스윙 구간 전체 |
| Hit(피격) 클립 | **트레일 없음.** 휘두르는 동작이 아니고, 성공/실패의 시각적 대비도 생긴다 |
| 끌 때 처리 | 즉시 disable(기본). 페이드는 선택 단계(Step 5)로 남긴다 |
| 외부 에셋 | `Tiny.Trail`은 **수정하지 않는다** (`Assets/99. External Assets/` 아래) |

## 구조 — 왜 별도 컨트롤러인가

세 가지 안을 놓고 비교했다.

| 안 | 내용 | 평가 |
|---|---|---|
| **A. 별도 `WeaponTrailController` + 이벤트** | `CharacterActionPlayer`가 스윙 시작/끝 이벤트만 발행하고, 컨트롤러가 구독해 트레일을 토글 | **채택.** 프로젝트 관례(`EffectManager`/`SliceTargetDirector`)와 일치. 트레일 고유 배선·튜닝값이 `CharacterActionPlayer`를 오염시키지 않고, 칼날 변형이 늘어도 여기서만 흡수된다 |
| B. `CharacterActionPlayer`가 `Tiny.Trail`을 직접 토글 | 필드 하나 + 3~4줄 | 가장 짧지만, 애니메이션 재생기가 특정 외부 에셋 타입을 직접 알게 된다. 칼날이 둘이라 곧 배열·널가드가 따라붙는다 |
| C. 애니메이션 이벤트(AnimationEvent)로 클립에서 토글 | 클립마다 켜기/끄기 이벤트 삽입 | 클립이 배속·트림되어 재생되므로 이벤트 시점이 트림 구간과 어긋난다. 클립마다 오서링이 필요해 유지비가 크다 |

A를 택하되, **이벤트는 최소 2개만** 추가한다(YAGNI).

## 타이밍 정의

```
PlaySlot(성공 클립)          actionEndTime               recoveryEndTime
      │                            │                            │
      ├────── 트레일 ON ───────────┤ OFF                        │
      │      (트림 = 스윙 구간)     │   (마무리 동작엔 트레일 없음)
```

- 인터럽트 안전: `PlaySlot`은 **항상 이전 스윙을 먼저 끝낸 뒤** 새 스윙을 시작한다.
  (베기 도중 미스 → Hit `PlaySlot` → 트레일 OFF, 켜진 채 남지 않는다.)

---

## 구현 단계

- [x] **Step 1 — `CharacterActionPlayer`에 스윙 구간 이벤트 추가**
  - `Assets/02. Scripts/Character/CharacterActionPlayer.cs`
  - `public event Action OnSwingBegan;` / `public event Action OnSwingEnded;` 추가. XML 주석에 "스윙(베기) 트림 구간의 시작/끝. 무기 트레일 등 스윙에 종속된 연출이 구독한다"를 명시.
  - `PlaySlot`에 `bool isSwing` 파라미터 추가:
    - 진입 즉시 **무조건** `RaiseSwingEnded()` (이전 액션이 트림 끝 전에 인터럽트됐을 수 있다).
    - `isSwing`이면 `RaiseSwingBegan()`.
    - 호출부: `TryStartPendingSuccess()` → `isSwing: true`, `HandleJudgeTargetFirstMiss()` → `isSwing: false`.
  - `Update()`의 트림 끝 래치(`if (!speedRestored && Time.time >= actionEndTime)`)에서 `RaiseSwingEnded()` 호출. **이 래치는 이미 트림 끝을 정확히 1회만 통과한다** — 새 타이밍 개념을 만들지 않는다.
  - 중복 발행 방지용 `bool swingActive` 플래그를 두고 `RaiseSwingBegan/Ended`가 이를 검사한다(끄기가 두 경로에서 올 수 있다).
  - `OnDisable()`에서도 `RaiseSwingEnded()` — 컴포넌트가 꺼질 때 트레일이 켜진 채 남지 않도록.

- [x] **Step 2 — `WeaponTrailController` 신규 작성**
  - `Assets/02. Scripts/Character/WeaponTrailController.cs` (전역 네임스페이스 — `CharacterActionPlayer`와 동일)
  - 직렬화 필드:
    - `CharacterActionPlayer actionPlayer` — 구독 대상
    - `Tiny.Trail[] bladeTrails` — **배열로 둔다.** 칼날 변형이 둘(`_01`/`_02`)이라 교체 기능이 붙어도 여기서 흡수된다
  - `OnEnable`/`OnDisable`에서 `actionPlayer.OnSwingBegan/OnSwingEnded` 구독·해제 (`SliceTargetDirector` 선례와 동일한 널가드 형태).
  - `Awake`에서 모든 트레일을 **disable로 시작**한다(씬에 켜진 채 저장돼 있어도 휴지 상태가 보장되도록).
  - 토글 시 `trail == null` 스킵, `trail.gameObject.activeInHierarchy == false`면 스킵(비활성 칼날 변형).
  - 배선 누락 경고: `Awake`에서 `actionPlayer == null` 또는 `bladeTrails`가 비었으면 `Debug.LogError`.
    **Research 제약 3 — 같은 이름 노드가 2단이라 오배선이 조용히 무연출로 끝난다. 이 로그가 유일한 방어선이다.**

- [x] **Step 3 — 씬 배선**
  - `DefaultScene`에 `WeaponTrailController`를 배치한다(캐릭터 루트 또는 `CharacterActionPlayer`와 같은 오브젝트).
  - `bladeTrails`에 **안쪽 메쉬 노드**의 `Trail`을 넣는다:
    `Char_School_Katana_FullBody-Magica cloth2/root/add_weapon_r/Weapon_Katana_01_Blade/`**`Weapon_Katana_01_Blade`**
    (바깥 노드에는 `Trail`이 없다 — Research §2)
  - 씬에 저장된 `Trail` 컴포넌트의 enabled 상태는 무관하다(Step 2의 `Awake`가 끈다).

- [x] **Step 4 — 문서 갱신**
  - `CLAUDE.md` §6(캐릭터 액션)에 스윙 이벤트 확장 포인트 한 줄, §폴더 구조에 `WeaponTrailController.cs` 한 줄 추가.
  - 본 Plan의 체크박스 갱신.

- [ ] **Step 5 — (선택) 종료 시 페이드아웃** *(미진행 — 의도적으로 보류)*
  - **기본 구현에는 넣지 않는다.** 먼저 Step 1~4로 플레이해 보고, 리본이 끊기는 게 거슬릴 때만 진행한다.
  - 방식: `WeaponTrailController`가 트레일 머티리얼의 **인스턴스**를 들고 종료 시 알파를 `trailFadeDuration` 동안 0으로 내린 뒤 disable, 다음 켜기에서 알파 복원.
  - `Tiny.Trail`은 `material`을 `MeshRenderer.material`에 넣으므로 이미 인스턴스가 만들어진다 — 원본 에셋(`TrailStick.mat`)을 건드리지 않도록 반드시 인스턴스를 잡을 것.

- [x] **Step 6 — 검증** *(정적 검증만 완료 — 플레이 확인은 사람이 해야 함)*
  - `refresh_unity(force, compile)` 후 `read_console` — **에러/경고 0건**. (완료)
  - 씬 배선 확인: `WeaponTrailController.actionPlayer` → 캐릭터의 `CharacterActionPlayer`,
    `bladeTrails[0]` → `Weapon_Katana_01_Blade`(안쪽 메쉬 노드)의 `Tiny.Trail`. 컴포넌트 리소스로 재조회해 확인. (완료)
  - **미검증(플레이 모드 육안 확인 필요)**:
    - 베기 중에만 트레일이 보이는가, 트림 끝에 사라지는가.
    - **연계 구간**: 다음 스윙이 이전 스윙을 끊을 때 트레일이 새로 시작되는가(잔상이 이어붙지 않는가).
    - **스윙 도중 미스**: Hit으로 끊길 때 트레일이 즉시 꺼지는가.
    - 패턴 없는 대기 구간(Run/Sprint)에 트레일이 남지 않는가.
  - 리본이 각져 보이면 `Tiny.Trail.duration`을 올려 튜닝한다(물리 레이트 샘플링 — Research §4). **Fixed Timestep은 건드리지 않는다.**

---

## 건드리지 않는 것

- `Tiny.Trail` 본체 (`Assets/99. External Assets/` — 외부 에셋).
- `Trail`의 `points` 오서링(이미 칼날에 맞게 찍혀 있다).
- `PatternHandler`, `SliceTargetDirector`, 판정 파이프라인.
- `CharacterActionPlayer`의 복귀 3경로 / Release / base 로코모션 로직 — 이벤트 발행만 추가한다.
- 프로젝트 Fixed Timestep 설정.
