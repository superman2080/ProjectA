# Plan — CameraSwitch

근거: `docs/CameraSwitch/Research_CameraSwitch.md`

## 목표

플레이어 주변에 앵글 vcam을 여러 대 두고, **`switchInterval`이 지난 뒤 처음 오는 패턴 경계에서** 랜덤하게 다른 앵글로 EaseOut 교체한다.

기존 여섯 기능(쉐이크·펀치·프레이밍·인트로·히트스톱 락·히트스톱 밀기)은 **동작을 바꾸지 않는다.** 단 쉐이크·펀치는 *어느 카메라에 거는가*만 고친다(D-4, 안 고치면 죽는다).

---

## 설계 결정

### D-1. 교체 시각 = 쿨다운(자격) → **패턴 경계(예약)** → **쉐이크 종료(발사)**

**세 단계로 나눈다.** 경계에서 바로 교체하면 블렌드가 임팩트와 쉐이크를 정면으로 덮기 때문이다.

```
쿨다운 만료        →  교체할 '자격'이 생긴다
OnJudgeTargetBegan →  이번 패턴에 교체를 '예약'(arm)한다
카메라 큐 종료      →  실제로 '발사'(fire)한다
```

**왜 경계에서 바로 쏘면 안 되는가** — 마지막 노드 시각을 0으로 두고 실측값으로 펴면:

| 시각 | 사건 |
|---|---|
| −0.0x | 패턴 N 완료 → **N+1 승계**(`OnJudgeTargetBegan`) ← 경계 |
| +0.10 | 임팩트(`Deadline + ImpactOffset`, `goodWindow` 0.1) |
| +0.10~0.20 | 히트스톱 — Brain 정지(§7-3) |
| +0.20 | 락 해제 → 쉐이크 0.18 / 펀치 0.12 시작 |
| **+0.38** | **쉐이크 끝** |
| +0.48 | 패턴 N+1 첫 노드(공백 평균) |

**승계는 임팩트보다 먼저 온다.** 완료는 마지막 노드 입력 순간이고 임팩트는 거기서 `goodWindow`만큼 뒤라서다. 그래서 경계에서 쏘면 0.4초 블렌드가 임팩트·쉐이크 구간을 **매번** 덮는다 — 드문 일이 아니라 구조적으로 고정된 거리다.

**쉐이크가 끝난 뒤로 미루면** 블렌드가 도는 구간이 `+0.38 ~ +0.78`이 된다. 여기엔 임팩트도 쉐이크도 없고, 다음 임팩트는 `+1.48`쯤이라 여유가 크다. **예약이 패턴에 묶여 있으므로 음악적 착지점도 유지된다** — 단지 그 패턴의 타격이 끝나고 나서 움직일 뿐이다.

**쿨다운·경계를 둘 다 쓰는 이유**: 벽시계만 쓰면 패턴 **한가운데서** 바뀐다(음악과 무관한 순간). 패턴 수만 쓰면 채보 밀도에 따라 간격이 요동친다. 합치면 **페이스는 시간이, 착지점은 음악이** 정한다.

실측 지연: 쿨다운 만료 후 경계까지 평균 +0.7초(최대 +1.4초), 거기서 발사까지 다시 +0.38초.

`switchInterval` 기본 **6초** → 곡당 약 12~14회.

⚠ **`EnemyKilled` 큐는 임팩트가 아니라 `burstTime`(사망 클립 트림 끝)에 터진다** — 최대 1초쯤 뒤다. "모든 큐가 끝날 때까지" 기다리면 발사가 그만큼 밀린다(깨지진 않고 늦어질 뿐). 답답하면 **쉐이크만 보고 펀치는 무시**하도록 조건을 좁힌다. 기본은 둘 다 본다.

**확정.**

### D-2. 앵글은 **`FollowOffset` 벡터 하나**로 정의된다

Research 2-2. 기존 vcam이 `BindingMode = LockToTarget`이고 그룹이 `RotationMode = Manual`이라, `FollowOffset`은 **그룹 로컬 축**으로 해석된다. `CameraDirector`가 그 그룹을 플레이어 yaw로 몰고 있으므로 — **오프셋만 바꾸면 자동으로 "플레이어 기준" 앵글**이 된다.

그래서 새 vcam은 기존 vcam을 **복제하고 `FollowOffset`만 바꾸면 끝**이다. Follow/LookAt(`CameraTargetGroup`)·`RotationComposer`·`GroupFraming`을 그대로 물려받아 **"적과 플레이어를 따라다닌다"가 공짜로 성립**한다.

제안 앵글 4종(씬에서 튜닝):

| 이름 | `FollowOffset` | 성격 |
|---|---|---|
| `Cam_Default` | (0, 2.5, −7) | **기존 vcam 그대로** — 풀의 일원이 된다 |
| `Cam_LowBack` | (0, 0.7, −4.5) | 로우앵글, 등 뒤 가까이 |
| `Cam_Shoulder` | (1.6, 2.0, −4.0) | 어깨 너머 |
| `Cam_SideLow` | (−3.5, 0.9, −1.5) | 측면 로우 |

**⚠ 각 vcam의 `GroupFraming`이 거리를 다시 맞추므로 오프셋의 z는 "기준 거리"지 최종 거리가 아니다**(§7-2). 방향과 높이가 실질이다.

**확정.**

### D-3. 선택은 **직전 것을 제외한 균등 랜덤**

같은 카메라를 다시 뽑으면 교체가 무연출로 낭비된다. 후보에서 현재 것을 빼고 균등 추출한다(한 줄).

**셔플백을 쓰지 않는다** — 4대 기준 균등 랜덤의 편중은 수 초 간격에서 체감되지 않고, 셔플백은 상태를 하나 더 든다. 필요해지면 그때.

**확정.**

### D-4. ⚠ 쉐이크·펀치를 **live vcam**에 건다 — 안 하면 타격감이 죽는다

Research 3-1. 지금 `perlin`·`gameplayCamera`가 특정 vcam을 하드와이어로 잡고 있어, **다른 앵글이 live가 되는 순간 쉐이크와 펀치가 화면에서 사라진다.** 이건 선택이 아니라 **이 기능의 전제 조건**이다.

**A안 채택**: `brain.ActiveVirtualCamera`로 live vcam을 조회해 그 Perlin/Lens에 건다.

- 카탈로그 의미(진폭·지속·겹침 "큰 쪽"·휴지값 복귀)를 **그대로 유지**한다 — 검증된 동작을 안 건드린다.
- **펀치는 어차피 이 조회가 필요**하다(FOV는 Impulse로 해결 안 된다). 하나의 메커니즘으로 합쳐진다.
- 휴지값(Perlin `AmplitudeGain`, 렌즈 FOV)은 vcam마다 다를 수 있으므로 **vcam별로 캐시**한다. 지금은 `Awake`에서 하나만 캐시하고 있다.
- `ApplyShake`/`ApplyPunch` 이음매 안에서만 바뀐다. 위층(트리거·카탈로그·타이밍·겹침)은 그대로다 — **봉인 규율이 여기서 값을 한다.**

**B안(Impulse)은 보류.** Cinemachine 정석이고 `ApplyShake` 주석이 예고해 둔 길이지만, 자체 엔벨로프를 쓰므로 카탈로그의 진폭/지속 의미와 겹침 규칙을 **다시 정의**해야 한다. A로 부족하다고 판명되면(아래 ⚠) 그때 간다.

**블렌드 중 쉐이크가 약해지는 문제는 D-1이 이미 없앴다.** A는 블렌드 도중 Active vcam 하나만 흔들 수 있는데, D-1이 발사를 쉐이크 종료 뒤로 미루므로 **블렌드와 쉐이크가 애초에 겹치지 않는다.** 두 결정이 맞물려야 성립하니 한쪽만 바꾸지 말 것.

**확정.**

### D-5. `Brain.DefaultBlend` = **EaseOut, 0.4초**

요구가 "EaseOut". 현재 값은 EaseInOut 1.0초로, 패턴 주기(1.38초) 대비 73%를 이동에 쓴다.

0.4초면 이동 29% / 정지 71%로 **컷이 앉을 자리가 생긴다.**

⚠ `IntroRoutine`이 이 값을 읽어 `travel = 카운트다운 − BlendTime`을 계산한다(§7-2). **줄이면 인트로 주행 시간이 늘어난다** — 개선 방향이라 무해하다. 규율은 "두 군데 적지 마라"지 "바꾸지 마라"가 아니다.

`CinemachineCore.GetBlendOverride`는 **쓰지 않는다** — 전 전환이 같은 블렌드면 전역 기본값 하나로 충분하다. (앵글별로 다른 블렌드가 필요해지면 그때.)

**확정.**

### D-3′. 시작 카메라는 **`cameras[0]` 고정**

첫 카메라를 랜덤으로 뽑지 않는다. `Setup()`에서 **인덱스 0을 활성**으로 올리고 나머지를 전부 휴지로 내린다.

이유 셋:
- **곡의 시작 구도가 매번 같아야 한다.** 랜덤이면 같은 곡을 다시 켤 때마다 첫인상이 달라지고, 인트로 스플라인의 끝점(§7-2 — "끝점을 게임플레이 구도에 정확히 맞출 의무가 없다, 차이는 블렌드가 흡수한다")이 **어느 구도로 흡수될지 알 수 없게 된다.**
- **리스트 순서가 곧 저작 의도가 된다.** 0번에 기준 앵글을 두는 규칙이라 인스펙터만 봐도 시작 구도를 안다.
- 씬 상태에 의존하지 않는다 — 어느 vcam이 우연히 높은 우선순위로 저장돼 있든 `Setup()`이 덮는다.

그래서 **`cameras[0]`에는 `Cam_Default`(기존 게임플레이 vcam)를 넣는다**(D-2 표의 첫 줄). 지금까지의 구도로 곡이 시작되고, 교체는 그 뒤부터 일어난다.

⚠ 첫 교체 쿨다운도 여기서 시작한다(`lastSwitchTime = Time.time`) — 곡 시작 직후 바로 바뀌지 않게.

**확정.**

### D-6. 우선순위 — 활성 10 / 휴지 0, 인트로(20) 아래

`IntroRoutine`이 인트로 vcam을 20으로 올린다. 교체용 활성 우선순위는 **20 미만**이어야 인트로가 이긴다. 휴지 0 / 활성 **10**.

인트로 휴지값 −10은 그대로 — 모두보다 낮아야 인트로가 끝난 뒤 안 튀어나온다(§7-2).

**확정.**

### D-7. 히트스톱 잠금과 겹치는 것은 **의도**

Research 3-2. 교체는 패턴 경계에서 일어나고, 그 직후(≤ goodWindow + ImpactOffset) 임팩트가 와서 `brain.enabled = false`로 0.1초 잠긴다 → **블렌드가 0.1초 얼었다가 이어진다.**

§7-3에서 이미 "멈췄다가 튕겨 나간다"를 의도로 확정한 것과 같은 성질이다. **새 조율 코드를 넣지 않는다.** 문서에만 못박는다.

**확정.**

### D-8. 소유는 `CameraDirector`, 구현은 별도 파일 + 토글

`CameraDirector`가 "카메라 연출의 유일 관리 지점"이고 **이미 vcam 우선순위를 갈아끼운다**(인트로). 교체는 같은 종류의 일이다.

다만 이 클래스가 맡는 일이 이미 여섯이라, **소유는 유지하되 선택기는 `Camera/CameraAngleSwitcher.cs`(`[Serializable]` 헬퍼)로 뽑고** `CameraDirector`가 필드로 든다. 인스펙터에는 접힌 그룹 하나.

토글 `switchEnabled`(D-8), 배선이 비거나 vcam이 2대 미만이면 조용히 비활성 — 기존 규율.

**확정.**

### D-9. 블렌드가 **직선이 아니라 호를 그리게** — `BlendHint = SphericalPosition`

기본 블렌드는 두 vcam 위치를 **직선 보간**한다. 앵글이 서로 반대편이면 카메라가 **무대를 가로질러(플레이어를 뚫고) 지나가고**, 중간에 거리가 확 줄었다 늘어난다.

**Cinemachine에 내장돼 있다** — `CinemachineCamera.BlendHint`에 `CinemachineCore.BlendHints.SphericalPosition`을 주면 블렌드 동안 위치가 **LookAt 타깃을 중심으로 한 구면 위**를 지난다. 즉 **대상을 축으로 돌아간다.** 코드 0줄, vcam마다 enum 하나.

(설치된 값 전체: `SphericalPosition`, `CylindricalPosition`, `ScreenSpaceAimWhenTargetsDiffer`, `InheritPosition`, `IgnoreTarget`, `FreezeWhenBlendingOut` — 확인 완료.)

**회전 중심은 LookAt 타깃 — 즉 `CameraTargetGroup`이고, 그게 의도한 중심이다.** 그룹 위치는 플레이어와 상대의 가중 중심(`GroupCenter`)이라, 카메라가 **교전 자체를 축으로** 돌아간다. 상대가 멀면 가중치가 0으로 떨어져 중심이 플레이어로 수렴하므로(§7-2), 혼자일 때는 자동으로 플레이어 중심이 된다. **추가 배선이 필요 없다.**

⚠ `SphericalPosition`은 **LookAt이 있어야 성립**한다. 비어 있으면 조용히 직선 보간으로 떨어진다 — 복제한 vcam의 LookAt이 그룹을 가리키는지 확인할 것.

**대안 `CylindricalPosition`**: 수평으로만 호를 그리고 높이는 직선 보간한다. 앵글 간 **높이 차가 클 때**(로우앵글 0.7 ↔ 기본 2.5) 구면보다 자연스러운 경우가 있다. 씬에서 둘 다 찍어 보고 고른다 — 코드는 어느 쪽이든 모른다.

**확정.**

---

## 구현 단계

- [x] **Step 1 — 씬: 앵글 vcam 추가 (D-2)**
  기존 `CinemachineCamera`를 3번 복제 → `Cam_LowBack` / `Cam_Shoulder` / `Cam_SideLow`. `FollowOffset`만 D-2 표대로, 나머지(Follow/LookAt·Composer·GroupFraming·Perlin)는 그대로. 전부 Priority 0.
  ⚠ **Perlin을 지우지 않는다** — D-4가 live vcam의 Perlin에 걸기 때문.
  **4대 전부 `BlendHint = SphericalPosition`**(D-9).

- [x] **Step 2 — 씬: `Brain.DefaultBlend` = EaseOut / 0.4초 (D-5)**

- [x] **Step 3 — `CameraDirector`: 쉐이크·펀치를 live vcam으로 (D-4)**
  `perlin`·`gameplayCamera` 단일 참조를 **live 조회 + vcam별 휴지값 캐시**로 교체.
  `ApplyShake`/`ApplyPunch` **안에서만** 바꾼다. 위층은 손대지 않는다.
  ⚠ 조회 실패(브레인 없음/Active 없음)면 지금처럼 조용히 무연출.

- [x] **Step 4 — `Camera/CameraAngleSwitcher.cs` 신규 (D-1·D-3·D-6)**
  필드: `switchEnabled`, `cameras[]`, `activePriority`(10), `restingPriority`(0), `switchInterval`(6).
  API: `Setup()` / `OnPatternBoundary()` / `Reset()`.
  `Setup()`: **인덱스 0을 활성으로, 나머지 전부 휴지로**(D-3′). `lastSwitchTime = Time.time`으로 쿨다운 시작.
  `OnPatternBoundary()`: 쿨다운 만료면 **예약만 세운다**(`armed = true`). 교체하지 않는다.
  `Tick()`: `armed && 카메라 큐가 없음`이면 **발사** — 현재 제외 랜덤 → 우선순위 스왑 → `armed = false`, `lastSwitchTime` 갱신 (D-1).
  "카메라 큐가 없음"의 판단은 `CameraDirector`가 준다(쉐이크·펀치 진행 여부는 그쪽 상태다).

- [x] **Step 5 — `CameraDirector` 배선**
  `[SerializeField] private CameraAngleSwitcher angleSwitcher;`
  `OnEnable`에서 `handler.OnJudgeTargetBegan += _ => angleSwitcher.OnPatternBoundary();`, `OnAllPatternsCleared`에서 `Reset()`.
  `Update`에서 `angleSwitcher.Tick(cueActive)` — `cueActive`는 `shakeDuration > 0f || punchDuration > 0f`(D-1의 발사 조건). **히트스톱 잠금 중에는 `Update`가 조기 반환하므로 `Tick`도 서고, 교체가 잠금 뒤로 밀린다** — 의도된 동작이다.
  `Awake`에서 `Setup()` — `cameras[0]`을 활성으로 올리고 나머지를 휴지로 내린다(D-3′).

- [x] **Step 6 — 씬 배선 + 초기값**
  `angleSwitcher.cameras`에 4대 등록 — **0번은 반드시 `Cam_Default`**(D-3′). `switchInterval` 6, 우선순위 10/0. 씬 저장.

- [x] **Step 7 — 문서**
  `CLAUDE.md` §7-5 추가 — 교체 규칙(쿨다운→예약→쉐이크 종료 발사와 그 근거: 승계가 임팩트보다 먼저 온다), **`BlendHint = SphericalPosition`으로 호를 그린다·회전 중심은 LookAt이라 그룹 중심이다**(D-9), **앵글 = `FollowOffset` 하나**인 이유(그룹 Manual + LockToTarget), **쉐이크·펀치가 live vcam을 봐야 하는 이유**(안 하면 죽는다), 히트스톱 잠금 겹침은 의도(D-7), 활성 우선순위가 인트로(20) 아래여야 하는 이유.

- [ ] **Step 8 — 검증**
  1. 컴파일 에러 0.
  2. **곡 시작 구도가 항상 `cameras[0]`인지**(여러 번 재시작해 확인) — 인트로 블렌드가 이 구도로 흡수돼야 한다.
  3. 곡 하나 돌려 **교체 횟수**(예상 12~14회)와 간격이 6초 + 경계 대기인지.
  3. **어느 앵글에서든 쉐이크·펀치가 보이는지** — 이게 D-4의 회귀 테스트다.
  3-1. **교체가 쉐이크 끝난 뒤에 시작되는지**(블렌드와 쉐이크가 안 겹치는지) — D-1의 회귀 테스트.
  3-2. **블렌드가 직선이 아니라 호를 그리는지**, 반대편 앵글로 갈 때 카메라가 플레이어를 뚫고 지나가지 않는지(D-9). 높이 차가 큰 쌍에서 `Cylindrical`이 나은지도 같이 본다.
  4. 인트로가 정상 동작하고, 끝난 뒤 교체 카메라가 이긴다.
  5. 등 뒤 추적(프레이밍)이 모든 앵글에서 도는지.
  6. 곡 중단 후 재시작 시 상태가 남지 않는지.

---

## 범위 밖

- 앵글별 개별 블렌드(`GetBlendOverride`). 전역 하나로 충분하다(D-5).
- 셔플백 / 가중 랜덤 (D-3).
- 쉐이크 Impulse 전환 (D-4 B안) — A가 부족하면 그때.
- 앵글별 FOV·노이즈 프로파일 차등. 오프셋만으로 충분히 달라 보이는지 먼저 본다.
