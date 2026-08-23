# Plan: 전투 저작값을 패턴으로 모으기 (공격 주체 + 임팩트 오프셋)

> 관련: `docs/EnemyCombat/` · `docs/HumanoidSlice/Plan_HumanoidSlice.md` · `docs/!Guides/Guide_EnemyCombat.md`
> 피드백은 이 문서에 `>>>`로 남겨주세요. 확정 전까지 구현하지 않습니다.

---

## 0. 문제

`Pattern`에 `ClipAlignment` 슬롯이 4개인데 **한 패턴에서 실제로 쓰이는 것은 항상 2개**다.

| `attacker` | 사는 슬롯 | 죽는 슬롯 |
|---|---|---|
| `Enemy` (적 공격 → 플레이어 패링) | `enemyAttack` + `playerParry` | `playerAttack`, `enemyDeath` |
| `Player` (무방비 적 → 플레이어 공격) | `playerAttack` + `enemyDeath` | `enemyAttack`, `playerParry` |

대칭이다 — 어느 쪽이든 **플레이어 클립 1 + 적 클립 1**.

원인: `attacker`가 **채보 엔트리**(`EnemyCue.attacker`)에 있어서 패턴이 두 경우를 다 대비해야 한다.

### 왜 옮기는 게 맞나

**`attacker`는 패턴의 성질이지 채보 순간의 성질이 아니다.**
3x3 획 모양이 스윙 모션을 정하고, 그 모션이 "공격으로 읽히는가 패링으로 읽히는가"를 정한다.
같은 획을 양쪽으로 쓰려면 같은 획에 대한 플레이어 모션 2벌이 필요한데 그럴 이유가 없다.

부수 문제: 지금은 **어긋난 조합도 저작 가능**하다. `enemyAttack`을 채우고 채보에서 `attacker = Player`로 두면
조용히 무시된다. 경고도 없다.

`EnemyCue`에 남는 것 — **이건 진짜로 채보 순간의 성질이다**:
`killOnSuccess`(같은 패턴이라도 이 엔트리에서만 죽인다) / `projectile`.

---

## 0-2. 두 번째 문제 — 임팩트 오프셋이 두 군데 저작된다

**같은 물리량이 필드 두 개로 존재한다.** 둘 다 "Deadline 대비 ±초, 칼이 닿는 순간을 앞뒤로 민다"이고,
소비자 셋이 소스 둘로 갈려 있다:

| 소비자 | 읽는 값 |
|---|---|
| 플레이어 칼 (`CharacterActionPlayer.cs:420`) | `Pattern.SliceTargetImpactOffset` |
| 카메라 큐 (`CameraDirector.cs:117`) | `Pattern.SliceTargetImpactOffset` |
| **적 칼 · 시체 교체 · 투사체** (`EnemyDirector.cs:390`) | `EnemyCue.impactOffset` |

둘 다 기본값 0이라 **지금은 우연히 겹친다.** 한쪽만 저작하면 **칼이 서로 빗나가고 경고도 안 뜬다.**

더 나쁜 것 둘:

1. **플레이어가 의존하는 필드가 삭제 예정이다.** `Guide_EnemyCombat` 8단계 정리 목록에
   `Pattern`의 `sliceTarget*` 3필드 제거가 있다. 그 안에 `sliceTargetImpactOffset`이 들어 있다.
2. **`HumanoidSlice` Step 0에서 맞춘 정렬을 이게 도로 깨뜨릴 수 있다.**
   시체 교체는 cue 오프셋으로, 플레이어 칼은 패턴 오프셋으로 시각을 잡는다 —
   "칼이 지나가는 순간 = 갈라지는 순간"이 저작 실수 하나로 어긋난다.

**`attacker`와 같은 계열의 문제다** — 하나의 값이 두 군데 살고, 어긋나도 조용하다.
그래서 한 플랜에서 같이 정리한다.

---

## 1. 설계 결정

### 1-1. 필드는 4개를 유지하고, **인스펙터가 2개만 그린다**
데이터를 2개(`playerClip` / `enemyClip`)로 합치는 것도 가능하지만 **하지 않는다.**

- 이름이 모호해져 `Tools/Animation Clip Trimmer`의 슬롯 드롭다운이 "이게 공격인지 패링인지"를 못 알려준다.
  지금 슬롯 목록이 읽히는 것이 그 4개 이름 덕이다.
- 필드를 지우면 이미 오서링된 패턴의 배선이 끊긴다. 커스텀 에디터로 숨기면 **저작 표면은 절반, 마이그레이션 위험은 0**이다.

>>> (완료) 확정 (2026-08-01)

### 1-2. 한 패턴은 한 역할만 갖는다
양쪽 역할이 필요하면 **패턴 에셋을 복제**한다. 패턴은 가볍고, 복제 쪽이 의도가 명확하다.
(`attacker`를 채보에서 오버라이드하는 경로는 두지 않는다 — 두면 지금의 "어긋난 조합" 문제가 그대로 남는다.)

>>> (완료) 확정 (2026-08-01)

### 1-3. 기본값은 `Attacker.Player`
현재 `EnemyCue.attacker`의 기본값과 같다. 값이 없던 기존 패턴이 같은 동작으로 남는다.

>>> (완료) 확정 (2026-08-01)

### 1-4. 임팩트 오프셋은 **`Pattern`에 하나만** 둔다
`attacker`와 같은 근거다 — 이 값은 **모션의 타이밍 보정**이고, 모션은 패턴의 것이다.
"이 베기는 칼이 조금 늦게 닿는 것처럼 보인다"는 클립의 성질이지 채보 순간의 성질이 아니다.

- `Pattern.sliceTargetImpactOffset` → **`impactOffset`으로 이름 변경.**
  이제 표적(투사체) 전용이 아니라 **모든 임팩트의 공통 앵커 보정**이므로 이름이 맞아야 한다.
- **`[FormerlySerializedAs("sliceTargetImpactOffset")]`를 반드시 붙인다.** 안 붙이면 이미 저작된 값이 0으로 리셋된다.
- `EnemyCue.impactOffset` 제거.
- 소비자 셋(플레이어 칼 / 카메라 큐 / 적·시체·투사체)이 **전부 이 하나를 읽는다.**

부수 이득: `Guide_EnemyCombat` 8단계에서 `sliceTarget*`를 지울 때 이 필드가 **딸려 지워지지 않는다** —
이름이 갈렸으므로 삭제 대상(`sliceTarget` / `sliceTargetOffset`)과 헷갈릴 여지가 없다.

>>> (완료) 확정 (2026-08-01)

---

## 2. 단계

### Step 0 — 선행: 기존 채보의 `attacker` + `impactOffset`을 패턴으로 이관 (**필드 제거보다 먼저**)

`EnemyCue`의 두 필드를 지우기 전에 값을 옮겨야 한다. 안 그러면 저작된 지시가 조용히 사라진다.

- [x] 에디터 메뉴 `Tools > Migrate Pattern Combat Fields` 신설.
      **`Tools/Pattern` 하위에 두지 않는다** — 그 메뉴는 구 클립 이관 도구를 지우면서 없어졌고,
      일회용 도구 하나를 위해 되살릴 이유가 없다.
- [x] 프로젝트의 모든 `SongChart`를 훑어 각 엔트리의 `enemyCue.attacker` / `enemyCue.impactOffset`을
      `entry.template`에 기입한다.
- [x] **충돌 보고가 이 도구의 핵심이다** — 같은 패턴이 서로 다른 값으로 쓰이고 있으면
      기입하지 말고 **어느 채보의 몇 번 엔트리인지 목록으로 출력**한다.
      결정 1-2에 따라 그 패턴은 사람이 복제해서 갈라야 한다.
- [x] **`impactOffset`은 충돌 판정을 값 일치가 아니라 근사(`Mathf.Approximately`)로 한다** — float이라 정확 비교는 위험하다.
- [x] **오프셋은 이미 패턴에 값이 있으면 덮어쓰지 않는다.** `Pattern.sliceTargetImpactOffset`이 0이 아닌데
      cue에도 값이 있으면 **양쪽을 다 출력하고 사람이 고르게 한다** — 어느 쪽이 진실인지 도구가 알 수 없다.
- [x] 충돌이 없을 때만 기입하고, 기입한 패턴 수와 값 분포를 로그로 남긴다.
- [x] 이 도구는 **1회용**이다. Step 4 이후 제거 대상으로 §5에 적어 둔다.

### Step 1 — `Pattern.attacker` 신설 + `impactOffset` 이름 변경
- [x] `[SerializeField] EnemySpace.Attacker attacker = EnemySpace.Attacker.Player` + `public Attacker Attacker` 게터.
      `Combat Clips` 헤더 **맨 위**에 둔다 — 아래 슬롯들의 표시 여부를 정하는 값이라 위에 있어야 읽힌다.
- [x] Tooltip에 "이 패턴은 한 역할만 갖는다. 양쪽이 필요하면 에셋을 복제하라"를 명시.
- [x] `OnValidate`에서 **쓰이지 않는 슬롯에 클립이 배선돼 있으면 경고**한다
      (예: `attacker == Player`인데 `enemyAttack.Clip != null`). 이관 후 남은 찌꺼기를 찾는 유일한 장치다.
- [x] `sliceTargetImpactOffset` → **`impactOffset`으로 이름 변경**, 게터도 `ImpactOffset`으로.
      ```csharp
      [UnityEngine.Serialization.FormerlySerializedAs("sliceTargetImpactOffset")]
      [SerializeField] private float impactOffset;
      ```
      **`FormerlySerializedAs`를 빠뜨리면 이미 저작된 값이 전부 0이 된다.**
- [x] Tooltip을 "표적이 닿는 순간" → **"칼이 닿는 순간(플레이어·적·표적·카메라 공통)"** 으로 고친다.
      이제 이 값 하나가 네 소비자를 전부 움직인다.
- [x] 구 게터 `SliceTargetImpactOffset`은 **남기지 않는다** — 남기면 어느 쪽을 읽어야 하는지 다시 모호해진다.
      호출부 3곳(Step 3)을 같이 고친다.

### Step 2 — `PatternEditor` 커스텀 인스펙터
- [x] `Assets/02. Scripts/Pattern/Editor/PatternEditor.cs` 신설.
- [x] `attacker` 값에 따라 무관한 슬롯 2개를 **그리지 않는다**:
      - `Enemy` → `enemyAttack`, `playerParry`
      - `Player` → `playerAttack`, `enemyDeath`
- [x] 나머지 필드는 기본 그리기를 그대로 쓴다(`DrawPropertiesExcluding`) — 새 필드가 늘어도 자동으로 따라온다.
- [x] 숨긴 슬롯에 값이 들어 있으면 **접이식으로 펼쳐 볼 수 있게** 한다.
      완전히 감추면 이관 찌꺼기를 지울 방법이 없다.

### Step 3 — 소비자를 전부 패턴으로 돌린다
- [x] `Reservation`은 이미 `template`을 들고 있다(`HumanoidSlice` Step 1에서 추가). 그것을 쓴다.
- [x] `EnemyDirector.CurrentAttacker` — `r.cue.attacker` → `r.template.Attacker` (템플릿 null이면 `Player` 폴백).
- [x] `EnemyDirector.HandlePatternQueued` — 적 클립 선택 조건을 `info.Template.Attacker == Attacker.Enemy`로.
- [x] `EnemyDirector.ResolveReservation` — `opponent.Resolve(playerSucceeded, r.template.Attacker, returnDuration)`.
      `EnemyView.Resolve`의 시그니처는 그대로다(출처만 바뀐다).
- [x] **임팩트 오프셋 호출부 3곳을 `Pattern.ImpactOffset`으로 통일**:
      - `EnemyDirector.cs:390` — `info.Deadline + cue.impactOffset` → `info.Deadline + info.Template.ImpactOffset`
        (템플릿 null이면 0). **여기 하나가 적 칼 · 시체 교체 · 투사체를 전부 움직인다.**
      - `CharacterActionPlayer.cs:420` — `info.Template.SliceTargetImpactOffset` → `info.Template.ImpactOffset`
      - `CameraDirector.cs:117` — 같은 변경
- [x] 세 곳이 **같은 식이 되었는지 눈으로 확인한다** — 이게 이 플랜의 실질적 산출물이다.

### Step 4 — `EnemyCue`에서 이관된 필드 제거
- [x] `EnemyCue`에서 `attacker` / `impactOffset` 삭제.
      `Attacker` **enum 자체는 남긴다**(`EnemySpace`에 그대로 — `EnemyView.Resolve`가 쓴다).
- [x] `killOnSuccess` / `projectile`은 유지 — 이건 진짜 채보 순간의 성질이다.
- [x] 클래스 주석 갱신 — "누가 휘두르는가와 임팩트 보정은 패턴이 정한다"를 명시.

### Step 5 — `PatternChartWindow` 갱신
- [x] `공격 주체` 팝업(`:373`) → **읽기 전용 표시**로. 패턴이 정하므로 여기서 바꿀 수 없다.
- [x] `임팩트 오프셋` 필드(`:380`) → 같은 이유로 **읽기 전용 표시**로.
- [x] 엔트리 요약(`:365`)은 그대로 두되 출처를 `entry.template.Attacker`로 바꾼다.
- [x] `template`이 비었으면 "패턴 미지정"으로 표시(현재는 기본값이 그냥 찍힌다).
- [x] 드래프트 복사(`:32`, `:35`)에서 `attacker` / `impactOffset` 줄 제거.
- [x] 읽기 전용으로 바뀐 두 값 옆에 **"패턴 에셋에서 편집"** 안내를 붙인다 —
      저작자가 여기서 못 바꾸는 이유를 알 수 있어야 한다.

### Step 6 — `Animation Clip Trimmer` 슬롯 필터
- [x] `Target Pattern`이 지정되면 그 패턴의 `attacker`에 맞는 슬롯만 드롭다운에 노출한다.
      (`레거시`는 언제나 노출 — 구 필드 정리용)
- [x] 패턴 미지정이면 전부 노출(지금 동작).

### Step 7 — 슬라이서 휴머노이드 모드 정합성 경고
- [x] 휴머노이드 모드는 `attacker == Player`인 패턴에서만 의미가 있다
      (적이 베어지는 건 플레이어가 공격자일 때뿐).
- [x] `attacker == Enemy`인 패턴을 넣으면 **경고를 띄우고 Bake를 막는다** — 구워도 절대 재생되지 않기 때문.

### Step 8 — 문서 갱신
- [x] `docs/!Guides/Guide_EnemyCombat.md` 3단계 — 슬롯 표에 "패턴의 `attacker`가 노출 슬롯을 정한다" 반영.
- [x] 같은 문서 5단계 — `공격 주체`가 채보가 아니라 패턴에서 정해진다는 것으로 수정.
- [x] `CLAUDE.md` — `EnemyCue` 설명에서 `attacker` 제거, `Pattern` 항목에 추가.

### Step 9 — 검증

**확인 완료**
- [x] 이관 도구 실행 — 채보 2개 / 엔트리 214개 / 패턴 10종, **기입 0 · 충돌 0 · 양쪽 값 0**.
      전투 필드가 아직 저작된 적이 없어 옮길 값 자체가 없었다(가이드 5단계가 미완료 TODO인 것과 일치).
- [x] `FormerlySerializedAs` 동작 — 강제 재직렬화 후 YAML 키가 `sliceTargetImpactOffset` → `impactOffset`으로
      바뀌고 값이 보존됨(전부 0). `attacker: 1`(Player) 기입 확인.
- [x] EditMode 테스트 **64/64 통과**.
- [x] `read_console` 컴파일 에러/경고 **0건**.
- [x] 이관 도구 제거(§5) — 제거된 필드를 읽어 컴파일이 깨지므로 실행 직후 삭제.

**플레이/에디터에서 확인 필요**
- [ ] 인스펙터에 슬롯이 2개만 보이는지, 접이식으로 나머지를 열 수 있는지.
- [ ] 트리머 슬롯 목록이 패턴 역할에 따라 걸러지는지.
- [ ] 슬라이서에 `attacker == Enemy` 패턴을 넣으면 Bake가 막히는지.
- [ ] `Pattern Chart Tool`에서 두 값이 회색으로만 보이는지.
- [ ] `attacker == Enemy` 패턴에서 적 공격 + 패링이 정상 재생되는지 (**클립 오서링 후**).
- [ ] `attacker == Player` 패턴에서 플레이어 공격 + 처치 절단이 정상 재생되는지 (**클립 오서링 후**).

---

## 3. 위험과 대비

| 위험 | 징후 | 대비 |
|---|---|---|
| **이관 전에 필드 제거** | 저작된 공격 주체·오프셋이 통째로 사라짐 | Step 0을 Step 4보다 **반드시 먼저** |
| **`FormerlySerializedAs` 누락** | 저작된 임팩트 오프셋이 전부 0이 됨. **조용히** — 값이 0이어도 동작은 하므로 알아채기 어렵다 | Step 1. 이름 변경 직후 기존 패턴 값을 눈으로 확인 |
| 오프셋이 양쪽에 다 있음 | 어느 쪽이 진실인지 도구가 못 정함 | Step 0 — 덮어쓰지 말고 양쪽 출력 후 사람이 선택 |
| 같은 패턴이 양쪽 역할로 쓰임 | 이관 도구가 충돌 보고 | 패턴 복제 후 재실행 (결정 1-2) |
| 이관 후 죽은 슬롯에 클립이 남음 | 저작자가 헷갈림 | Step 1 `OnValidate` 경고 + Step 2 접이식 |
| 커스텀 에디터가 새 필드를 삼킴 | 나중에 추가한 필드가 인스펙터에 안 보임 | `DrawPropertiesExcluding`으로 화이트리스트가 아니라 **블랙리스트** 방식 (Step 2) |
| 휴머노이드 세트를 Enemy 패턴에 구움 | 절대 재생되지 않는 에셋이 생김 | Step 7 경고 + Bake 차단 |

---

## 4. 이 변경이 주는 것

- 패턴당 저작할 클립 **4 → 2**
- 어긋난 조합(`enemyAttack` 채워놓고 `attacker = Player`)이 **구조적으로 불가능**해짐
- **임팩트 오프셋 소스 2 → 1** — 플레이어 칼 · 적 칼 · 시체 교체 · 투사체 · 카메라가 같은 값을 읽는다
- `HumanoidSlice` Step 0에서 맞춘 "칼이 지나가는 순간 = 갈라지는 순간"이
  **저작 실수로 깨질 수 없게** 된다(값이 하나뿐이라 어긋날 대상이 없다)
- 채보 저작에서 결정 둘이 빠짐 — 패턴을 고르면 역할과 타이밍 보정이 따라온다

## 5. 정리 (전부 넘어간 뒤)

- [x] `Tools > Migrate Pattern Combat Fields` 제거 (1회용 도구)
- [x] `Guide_EnemyCombat` 8단계의 `sliceTarget*` 3필드 제거 시
      **`impactOffset`은 이제 그 그룹이 아니다** — 이름이 갈렸으니 같이 지우지 않도록 목록을 갱신한다

---

## 6. 피드백

>>> 여기에 `>>>`로 의견을 남겨 주세요.
