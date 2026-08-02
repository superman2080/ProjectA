# Research — CameraSwitch

요구: **플레이어 주변에 vcam을 여러 대 배치하고, 일정 시간이 지난 뒤 첫 패턴 전환 타이밍에 랜덤하게 EaseOut으로 카메라를 교체한다.**

---

## 1. 이 게임은 카메라를 마음껏 써도 된다 — 결정적 근거

**씬의 루트 Canvas는 `ScreenSpaceOverlay`이고 `worldCamera`가 없다.**

패턴인풋 9칸 · 포커스 링 · 가이드라인 · 판정 색상이 **전부 화면 공간**이라 카메라와 완전히 독립이다. 카메라를 갈아끼워도 **플레이어가 봐야 할 것은 1픽셀도 움직이지 않는다.**

보통 3D 액션에서 카메라를 과감히 못 쓰는 이유가 조작 가독성인데, 이 게임에는 그 제약이 **없다.** 여러 대 랜덤 교체가 이 게임에 특별히 잘 맞는 구조적 이유다.

---

## 2. 씬 실측 (BattleScene)

### 2-1. `CinemachineBrain` — 'Main Camera'

| 항목 | 값 |
|---|---|
| `DefaultBlend` | **EaseInOut, 1.0초** |
| `CustomBlends` | 없음 |
| UpdateMethod / BlendUpdateMethod | SmartUpdate / LateUpdate |

### 2-2. vcam 둘

| vcam | Priority | FOV | Follow / LookAt |
|---|---|---|---|
| `CinemachineCamera` | 0 | 40 | `CameraTargetGroup` / 동일 |
| `IntroCamera` | −10 | 25 | — / 플레이어 직접 |

게임플레이 vcam 구성 — **새 vcam이 그대로 베낄 원본**:
- `CinemachineFollow` offset (0, 2.5, −7), **`BindingMode = LockToTarget`**, `PositionDamping (1,1,1)`
- `CinemachineRotationComposer` `TargetOffset (0, 1.5, 0)`
- `CinemachineGroupFraming` size 0.6, `CenterOffset (0, −1.0)`, `DollyRange (−2, +4)`, `DollyOnly`
- `CinemachineBasicMultiChannelPerlin` (AmplitudeGain 0.1 휴지값)

**`LockToTarget` + 그룹 `RotationMode = Manual`이라 `FollowOffset`이 그룹 로컬 축으로 해석된다**(§7-2). `CameraDirector`가 그룹 yaw를 플레이어 방향으로 몰고 있으므로, **새 vcam은 `FollowOffset`만 다르게 주면 자동으로 "플레이어 기준" 앵글이 된다.** 앵글 정의가 벡터 하나로 끝난다.

### 2-3. 채보 타이밍 (Lv10, 엔트리 87개)

| 항목 | 평균 | 최소 | 최대 |
|---|---|---|---|
| 패턴 내부 길이(첫→마지막 노드) | 0.90s | 0.00s | 1.60s |
| 패턴 사이 공백 | 0.48s | 0.40s | 1.60s |
| **패턴 주기** | **≈1.38s** | | |

교체 간격을 초로 주면 실제 교체는 그 이후 **첫 패턴 경계**에서 일어나므로, 실측 지연은 **평균 +0.7초, 최대 +1.4초**다(경계 대기).

---

## 3. ⚠ 이 변경이 깨뜨리는 것 — 진짜 작업량은 여기다

### 3-1. 쉐이크와 펀치가 **특정 vcam에 하드와이어**돼 있다

`CameraDirector` 실측:

| 기능 | 필드 | 가리키는 대상 |
|---|---|---|
| 쉐이크 | `perlin` | `CinemachineCamera`의 Perlin 컴포넌트 |
| FOV 펀치 | `gameplayCamera` | `CinemachineCamera` |

**다른 vcam이 live가 되는 순간 둘 다 화면에서 사라진다.** 타격감 연출(§7-1·§7-3)이 통째로 죽는다는 뜻이다. 여러 대를 도입하려면 **반드시 같이 고쳐야 한다.**

해결 후보:

| 안 | 방식 | 비용 | 비고 |
|---|---|---|---|
| **A** | `brain.ActiveVirtualCamera`로 **live vcam을 조회**해 그 Perlin/Lens에 건다 | 작음 | 기존 카탈로그 의미(진폭·지속·겹침 규칙·휴지값)를 그대로 유지. 펀치는 **어차피 이 조회가 필요**하므로 메커니즘이 하나로 합쳐진다 |
| B | 쉐이크를 `CinemachineImpulse`로 교체 | 중간 | Cinemachine의 정석. `ApplyShake` 주석이 *"나중에 Impulse로 갈아끼울 때"*라고 예고해 둔 자리다. 다만 Impulse는 자체 엔벨로프를 쓰므로 카탈로그의 진폭/지속 의미와 겹침 규칙("큰 쪽을 취한다")을 다시 정의해야 한다. **FOV 펀치는 Impulse로 해결 안 된다** — 결국 A의 조회가 또 필요 |

⚠ **블렌드 도중에는 vcam이 둘 다 live다.** A는 그중 하나(Active)만 흔들므로 블렌드 구간의 쉐이크가 가중치만큼 약해진다. 교체 간격이 수 초 단위면 임팩트가 블렌드와 겹칠 확률이 낮아 실사용에서 문제되기 어렵다.

`CinemachineImpulseSource`/`Listener` 둘 다 프로젝트에 **설치돼 있음(확인 완료)** — B로 가는 길은 열려 있다.

### 3-2. 히트스톱이 임팩트에 Brain을 끈다

`CameraDirector.HoldForHitStop`이 `brain.enabled = false`로 0.1초 잠근다(§7-3). 교체가 패턴 경계에서 일어나면 **그 직후 임팩트가 와서 블렌드가 0.1초 얼었다 이어진다.**

이건 §7-3에서 이미 "의도"로 확정한 성질과 같은 것이다(멈췄다가 튕겨 나감). 새로 조율할 코드는 없지만 **문서에 못박아야** "왜 블렌드가 끊기지"로 헤매지 않는다.

### 3-3. 인트로와 우선순위

`IntroRoutine`은 인트로 vcam을 `introPriority`(20)로 올렸다가 휴지값(−10)으로 되돌린다. 교체 시스템이 쓰는 우선순위가 20 이상이면 인트로를 이긴다 → **교체용 활성 우선순위는 20 미만**이어야 한다.

또 `IntroRoutine`은 `travel = 카운트다운 − Brain.DefaultBlend.BlendTime`으로 창을 계산한다. **`DefaultBlend`를 줄이면 인트로 주행 시간이 늘어난다**(개선 방향이라 무해). 규율은 "두 군데 적지 마라"지 "바꾸지 마라"가 아니다.

### 3-4. 교체 시각을 알려 주는 이벤트

`PatternHandler.OnJudgeTargetBegan(JudgeTargetInfo)` — **판정 대상이 선두가 되는 순간**(최초 투입/승계). §3에 따르면 선두 패턴이 완료/만료되면 **같은 프레임에** 다음 패턴이 승계되며 이 이벤트가 발행된다.

= **"다음 패턴으로 넘어가는 타이밍" 그 자체다.** 새 이벤트도, 시각 계산도 필요 없다.

`OnAllPatternsCleared`로 곡 중단 시 정리.

---

## 4. 정리 — 필요한 변경

| # | 대상 | 성격 |
|---|---|---|
| 1 | 앵글 vcam N대 (기존 vcam 복제, `FollowOffset`만 다름) | 씬 |
| 2 | 쿨다운 + 패턴 경계 교체 선택기 | 신규 코드(작음) |
| 3 | **쉐이크·펀치를 live vcam으로** (3-1 A안) | 기존 수정 — **필수** |
| 4 | `Brain.DefaultBlend`를 EaseOut·짧게 | 씬 |
| 5 | 히트스톱 잠금과의 관계를 의도로 문서화 | 문서 |
