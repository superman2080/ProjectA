# Plan — CameraFraming

근거: `docs/CameraFraming/Research_CameraFraming.md`

## 목표

1. **프레이밍** — 교전 상대가 있으면 플레이어와 함께 한 화면에 담고, 없으면 플레이어만 비춘다. (D-1 ~ D-6)
2. **인트로** — 곡 시작 전, 플레이어를 로우앵글부터 훑고 올라가 게임플레이 구도로 고정된다. (D-7 ~ D-9)

둘은 **서로 다른 층에서 산다.** 인트로는 vcam을 갈아타는 일이고, 프레이밍은 게임플레이 vcam **안에서** 무엇을 담을지 정하는 일이다. 그래서 인트로 vcam은 TargetGroup을 쓰지 않고 겹칠 일도 없다.

## 설계 결정

### D-1. `CinemachineTargetGroup` **한 대**, 멤버는 **고정 2칸**

vcam의 `TrackingTarget`을 플레이어에서 그룹으로 바꾼다. 멤버는 **항상 두 칸**이다.

| 슬롯 | 대상 | 가중치 |
|---|---|---|
| 0 | 플레이어 | 1 고정 |
| 1 | 현재 교전 상대 | 0 ↔ 1 (블렌드) |

**멤버를 넣었다 뺐다 하지 않는다.** 그러면 바운드가 계단식으로 튀어 구도가 팝한다. 가중치만 움직이면 바운드가 연속적으로 변해 카메라가 부드럽게 물러나고 붙는다. 가중치 0은 바운드 계산에서 제외되므로 "적 없음"이 정확히 표현된다.

**확정.**

### D-2. 승격 직후 멀리 있는 적은 **거리로 가중치를 깎는다**

Research 4-1의 문제 — 처치 즉시 승격된 적은 링(6m) 위에 있다. 그대로 담으면 카메라가 확 물러난다.

**거리 기반 가중치**로 푼다:

```
weight = 1 - saturate((distance - fullFrameDistance) / (dropoffDistance - fullFrameDistance))
```

- `distance` ≤ `fullFrameDistance`(기본 3m) → 가중치 1, 완전히 담는다
- `distance` ≥ `dropoffDistance`(기본 6m) → 가중치 0, 없는 것과 같다 = **플레이어만**
- 사이는 선형

**"적이 있다/없다"가 이진값이 아니라 거리의 함수가 된다.** 멀리서 달려오는 적은 화면에 서서히 자리를 만들어 주고, 링에 흩어진 배경 적들은 구도에 개입하지 않는다. 요구("있으면 함께, 없으면 플레이어만")를 만족하면서 4-1을 같은 식 하나로 흡수한다.

**확정.**

### D-3. `CinemachineGroupFraming`은 `SizeAdjustment = DollyOnly`

FOV 25는 좁은 망원 구도라 `Zoom`으로 흔들면 원근이 눈에 띄게 왜곡된다. 거리를 움직이는 `Dolly`가 이 구도를 보존한다.

`CinemachineFollow`의 `FollowOffset`(0, 4, −10)이 기준 거리이고 `GroupFraming`이 그 위에서 앞뒤로 민다.

**확정.**

### D-4. `BindingMode`를 `LockToTarget` → `WorldSpace`로 — ~~확정~~ **폐기**

> **⚠ 폐기됨 (`docs/CameraOverShoulder/`).** 근거가 틀렸고, 이제는 정반대 요구가 됐다.
>
> "그룹 회전은 멤버 배치에서 파생된다"는 `RotationMode = GroupAverage`일 때만 참인데 실제 그룹은 **`Manual`**이다 — 그룹 회전은 그 GameObject의 `transform.rotation`이고 당시엔 아무도 쓰지 않아 identity였다. 그래서 **처방이 씬에 적용되지 않았는데도(`BindingMode`는 계속 `LockToTarget`) 아무 증상이 없었다.**
>
> 지금은 `CameraDirector`가 그 그룹 회전을 **플레이어 yaw로 몰아** 카메라를 등 뒤에 세운다. `LockToTarget`이 그 전제이므로 **유지**한다. 대신 `RotationMode`를 `GroupAverage`로 바꾸는 것이 새 금기다.

~~Research 4-2. 그룹 회전은 멤버 배치에서 파생되므로, 지금 값(`LockToTarget`)을 유지하면 적이 링을 돌 때마다 **구도가 통째로 회전한다**. 카메라 각도는 씬이 정한 고정값이어야 한다.~~

### D-5. 소유자는 `CameraDirector`

CLAUDE.md가 "카메라 연출의 유일 관리 지점"으로 규정했고, 필요한 참조(`actionPlayer` = 플레이어 Transform, `enemyDirector`)를 **이미 전부 들고 있다.** 새 컴포넌트를 만들지 않는다.

봉인 규율은 유지하되 문구를 갱신한다 — **Cinemachine 타입은 `ApplyShake`·`ApplyFraming`·`IntroRoutine` 세 이음매에만 등장한다.** 위층(트리거·거리 계산)은 Cinemachine을 모른다.

**확정.**

### D-6. 상대는 **이벤트로 받고 매 프레임 거리만 잰다**

`EnemyDirector.OnOpponentChanged`를 구독해 상대 `Transform`을 갈아끼운다(폴링 없음). 거리는 `Update`에서 계산한다 — 적이 계속 움직이므로 이건 프레임 단위일 수밖에 없다.

`EnemyDirector`에 **`public EnemyView CurrentOpponent` 프로퍼티를 추가**한다. 이벤트만으로는 `OnEnable` 시점의 초기 상태를 알 수 없다(구독 전에 이미 승격됐을 수 있다).

**확정.**

---

### D-7. 인트로 경로는 **씬의 스플라인**이고, 코드는 진행도 하나만 민다

인트로 vcam(`IntroCamera`)에 `CinemachineSplineDolly`를 붙이고 `SplineContainer`를 씬 오브젝트로 둔다. **경로는 씬 뷰에서 노트를 찍어 저작한다** — 로우앵글에서 시작해 훑고 올라가는 모양을 직접 그린다.

코드가 만지는 것은 **`CameraPosition` 하나**다. 카운트다운 동안 0 → 1로 민다.

- **`PositionUnits`는 `Normalized`.** 스플라인 길이·노트 수와 무관하게 0→1이 항상 전체 경로다. **경로를 다시 그려도 코드는 그대로다** — 이게 저작 자유의 실체다. `Distance`면 경로를 늘릴 때마다 코드/인스펙터 값을 따라 고쳐야 한다.
- 시작·끝 지점, 높이, 곡률, 카메라가 무엇을 보는지 전부 씬이 정한다. **코드에는 좌표가 하나도 없다.**
- **속도 곡선만 코드가 준다** — `AnimationCurve introEase`(기본 EaseInOut). 경로는 모양이고 이징은 시간 배분이라 층이 다르다. 등속으로 훑으면 기계적으로 보인다.

>>> 

### D-8. 인트로는 **두 도막**이고, 곡 시작에 정확히 맞물린다

Research 4-5 — `countdownDuration`이 이미 인트로 창이다. 새 대기 구간을 만들지 않는다.

`ChartPlayer` 변경 둘:
- `countdownDuration`에 **`[Min(3f)]`** — 인트로가 담길 최소 길이를 타입으로 보장한다. 프리웜 창이기도 하므로 짧아져서 좋을 게 없다.
- `public event Action<float> OnCountdownStarted` 추가. `PrepareStage()` 직후·`WaitForSeconds` **직전**에 `countdownDuration`을 실어 발행.

`CameraDirector`가 받아 두 도막으로 나눈다:

```
t=0                     인트로 vcam 우선순위 ↑ (컷 — 아직 아무것도 시작 안 했으므로 컷이 맞다)
t=0 ─────────────────→  ① 스플라인 주행: CameraPosition 0 → 1  (introEase 적용)
t=duration − blendTime  주행 완료, 우선순위 ↓ → ② Brain 블렌드 시작
t=duration              블렌드 완료 = 곡 시작   ← 정확히 맞물린다
```

**주행이 블렌드 시작 시점에 끝난다.** 스플라인 끝점이 게임플레이 구도와 비슷하게 놓이면 블렌드가 짧게 마무리하고, 달라도 블렌드가 그 차이를 흡수한다 — **끝점을 정확히 맞출 의무가 없다.** 저작이 편해지는 지점이다.

**`blendTime`은 `CinemachineBrain.DefaultBlend.Time`에서 읽는다.** 인스펙터에 같은 값을 두 번 적으면 언젠가 한쪽만 고쳐져 *곡은 시작됐는데 카메라가 아직 움직이는* 상태가 된다. 진실의 원천은 Brain 하나다.

`blendTime ≥ duration`이면 주행 없이 즉시 우선순위를 내리고 **경고를 찍는다** — 곡 시작을 늦출 수는 없으니 인트로가 잘리는 쪽을 택한다. `[Min(3f)]`가 있어 블렌드를 3초 넘게 잡지 않는 한 일어나지 않는다.

>>> 

### D-9. 인트로 vcam은 **플레이어만** 본다

TargetGroup을 쓰지 않고 플레이어 Transform을 `LookAt`으로 직접 추적한다(위치는 스플라인이, 방향은 플레이어가 정한다).

- 인트로의 주인공은 플레이어다. 적이 구도를 흔들면 곡마다 인트로가 달라진다.
- 그룹을 공유하면 D-2의 가중치가 인트로 구도까지 바꿔 **두 기능이 한 값으로 얽힌다.**

노이즈(`perlin`)도 인트로 vcam에는 붙이지 않는다 — 쉐이크는 게임플레이 vcam 것이고, `CameraDirector`가 캐시하는 휴지값도 그쪽 것이다.

>>> 

---

## 단계

### A. 프레이밍

- [x] **Step 1 — `EnemyDirector.CurrentOpponent` 노출**
  `public EnemyView CurrentOpponent => currentOpponent;` 한 줄. 초기 상태 동기화용(D-6).

- [x] **Step 2 — `CameraDirector`에 프레이밍 필드 추가**
  `[Header("Framing")]`: `targetGroup`(`CinemachineTargetGroup`), `fullFrameDistance`(3), `dropoffDistance`(6), `weightDamping`(0.35). 전부 `SerializeField`.
  `targetGroup`이 비면 **프레이밍 기능만 조용히 꺼진다**(쉐이크는 계속 동작). 새 기능이 기존 씬을 깨지 않게 한다.

- [x] **Step 3 — 그룹 슬롯 초기화**
  `Awake`에서 그룹 멤버를 2칸으로 세운다: 0번 = `actionPlayer.transform`(가중치 1), 1번 = 없음(가중치 0). 반경은 인스펙터 값 그대로 둔다.
  `actionPlayer`가 없으면 에러 로그 후 프레이밍 비활성.

- [x] **Step 4 — 상대 추적**
  `OnEnable`에서 `enemyDirector.OnOpponentChanged += HandleOpponentChanged`, `OnDisable`에서 해제. 핸들러는 `opponentTransform`만 갈아끼운다.
  `OnEnable` 말미에 `CurrentOpponent`로 초기 동기화(D-6).

- [x] **Step 5 — 가중치 계산과 적용 (`ApplyFraming`)**
  `Update`에서 D-2 식으로 목표 가중치를 구하고, `weightDamping`으로 현재 가중치를 지수 보간한 뒤 `targetGroup`의 1번 슬롯에 쓴다.
  상대가 null이거나 비활성이면 목표 가중치 0.
  **Cinemachine 타입이 등장하는 두 번째 지점이다**(D-5).

- [x] **Step 6 — 씬 배선**
  - `CinemachineCamera` 아래에 `CameraTargetGroup` 오브젝트 신설, `CinemachineTargetGroup` 부착
  - vcam `TrackingTarget` → 이 그룹
  - vcam에 `CinemachineGroupFraming` 익스텐션 추가, `SizeAdjustment = DollyOnly` (D-3)
  - `CinemachineFollow.BindingMode` → `WorldSpace` (D-4)
  - `CameraDirector`에 `targetGroup` 배선 (`GroupFraming`은 씬 익스텐션으로 알아서 동작 — 코드가 참조를 들 이유가 없다)
  ※ 가능한 만큼 내가 직접 한다. 손댈 수 없는 항목만 남겨 보고한다.

### B. 인트로

- [x] **Step 7 — `ChartPlayer` 변경 둘**
  - `countdownDuration`에 `[Min(3f)]` 부착 (D-8)
  - `public event System.Action<float> OnCountdownStarted;` 추가. `PlayRoutine`에서 `PrepareStage()` 다음, `WaitForSeconds` **직전**에 `OnCountdownStarted?.Invoke(countdownDuration)`

- [x] **Step 8 — `CameraDirector`에 인트로 필드·구독 추가**
  `[Header("Intro")]`: `chartPlayer`(`ChartGen.ChartPlayer`), `introCamera`(`CinemachineCamera`), `introDolly`(`CinemachineSplineDolly`), `brain`(`CinemachineBrain`), `introPriority`(20), `introEase`(`AnimationCurve`, 기본 EaseInOut).
  `OnEnable`/`OnDisable`에서 `OnCountdownStarted` 구독·해제.
  필수 참조가 하나라도 비면 **인트로만 조용히 꺼진다**(쉐이크·프레이밍은 계속). Step 2와 같은 규율.
  `Awake`에서 `introDolly.PositionUnits != Normalized`면 경고 — 0→1 전제가 깨지면 경로가 일부만 재생된다(D-7).

- [x] **Step 9 — 인트로 재생 (`ApplyIntro`)**
  코루틴 하나:
  1. 우선순위 ↑
  2. `travel = duration − brain.DefaultBlend.Time` 동안 매 프레임 `introDolly.CameraPosition = introEase.Evaluate(t / travel)`
  3. `CameraPosition = 1`로 확정 후 우선순위 ↓ → Brain이 블렌드
  `travel <= 0`이면 주행 생략 + 경고(D-8).
  `OnDisable`에서 코루틴을 멈추고 우선순위를 원복한다 — 인트로 도중 씬을 나가도 vcam이 높은 우선순위로 남지 않게.
  **Cinemachine 타입이 등장하는 세 번째이자 마지막 지점**(D-5의 봉인 규율 문구를 여기까지로 갱신).

- [x] **Step 10 — 인트로 씬 배선**
  - `IntroSpline` 오브젝트 신설 + `SplineContainer`. 노트 2~3개로 **로우앵글 → 상승** 초안 경로를 깔아 둔다(사용자가 다듬을 출발점)
  - `IntroCamera` 오브젝트 신설 + `CinemachineCamera`
    - `LookAt` = 플레이어 Transform (그룹 아님, D-9)
    - `CinemachineSplineDolly` 부착 → `Spline` = `IntroSpline`, **`PositionUnits = Normalized`**, `AutomaticDolly` 끔(코드가 민다)
    - `CinemachineRotationComposer` 부착. 노이즈는 붙이지 않는다(D-9)
  - `Main Camera`의 `CinemachineBrain.DefaultBlend` → `EaseInOut`, 시간 설정(권장 1.0초 — 카운트다운 3초 중 2초가 주행에 남는다)
  - `CameraDirector`에 `chartPlayer` / `introCamera` / `introDolly` / `brain` 배선
  ※ **경로 모양은 사용자가 저작한다.** 나는 배선과 초안 경로까지 하고 남긴다.

### C. 검증

- [ ] **Step 11 — 검증(플레이)**
  **프레이밍**
  - 적 없음(링 소진) → 플레이어만, 지금과 같은 구도
  - 교전 중 → 둘 다 화면 안, 잘리지 않음
  - 처치 직후 → 다음 적이 다가오며 **부드럽게** 화면에 들어옴(팝 없음)
  - 쉐이크가 프레이밍과 간섭하지 않음
  - 구도가 적 위치에 따라 회전하지 않음 (D-4 확인)

  **인트로**
  - 카운트다운 시작과 동시에 로우앵글로 **컷**, 곡 시작 순간에 게임플레이 구도로 **정확히 도착**
  - 스플라인 **전체**가 재생됨(끝이 잘리거나 중간에서 멈추지 않음) — `PositionUnits = Normalized` 확인
  - 경로 노트를 옮겨도 코드 수정 없이 그대로 재생됨 (D-7의 저작 자유 확인)
  - 곡이 시작됐는데 카메라가 아직 움직이지 않음 (D-8 타이밍 확인)
  - 인트로 중 적이 링에 서 있음(`PrepareStage`가 먼저라 정상)
  - 인트로 도중 `Stop()` → vcam 우선순위가 원복됨

## 범위 밖

- 인트로 중 UI(곡명·난이도) 표시
- 곡 종료 시의 아웃트로 연출
- 적 처치 순간의 클로즈업 같은 별도 연출 큐
- `CameraCenter` 죽은 오브젝트 정리 (Research 2 — 별건)

---

## 구현 중 확정된 사항 (Plan 대비 차이)

전부 실제 API를 리플렉션으로 확인한 결과다.

- **`CinemachineGroupFraming`의 Dolly/Zoom 선택은 `FramingMode`가 아니라 `SizeAdjustment`다.** `FramingMode`는 프레이밍할 **축**(`Horizontal` / `Vertical` / `HorizontalAndVertical`)이고, Dolly/Zoom은 `SizeAdjustmentModes`(`ZoomOnly` / `DollyOnly` / `DollyThenZoom`)다. D-3의 의도대로 `SizeAdjustment = DollyOnly`로 배선했다.
- **`groupFraming` 참조를 코드에 두지 않았다.** 씬 익스텐션이 스스로 동작하므로 들고 있을 이유가 없다. 인스펙터 필드에서도 뺐다.
- **블렌드 시간은 `DefaultBlend.Time`이 아니라 `DefaultBlend.BlendTime`을 읽는다.** `BlendTime`은 `Style`이 `Cut`일 때 0을 돌려주는 **실효값**이다. 원시 필드를 읽으면 Cut으로 바꿔 놓고도 주행 시간을 그만큼 깎게 된다.
- **인트로 vcam의 휴지 우선순위를 −10으로 뒀다.** 0으로 두면 게임플레이 vcam(0)과 **동점**이라, 인트로가 끝난 뒤 어느 쪽이 이길지 활성화 순서에 달린다. `Awake`에서 휴지값이 `introPriority` 이상이면 경고한다.
- **`CameraDirector`가 플레이어를 다시 찾지 않는다.** 배선 시 vcam의 기존 `TrackingTarget`을 그대로 그룹 0번으로 옮겼다 — 지금 화면에 잡히던 대상이 곧 플레이어이므로, 이름으로 다시 찾는 것보다 어긋날 여지가 없다.

---

## 사후 수정 — 처치 순간 카메라가 바깥으로 튀는 문제

**증상**: 적을 처치하는 순간 카메라가 살짝 바깥으로 튄다.

**원인은 풀 반환이 아니다.** 풀 반환(`SwapToCorpse` → `ReleaseEnemy`)은 임팩트 시각에 일어나 이보다 늦고, 그때는 이미 대상이 다음 적을 가리킨다.

**진짜 원인은 D-1의 사각지대다.** D-1은 *"멤버를 넣었다 뺐다 하지 않는다 — 가중치만 움직이면 바운드가 연속적으로 변한다"*로 팝을 막았지만, **슬롯 1의 대상 자체가 바뀔 때는 위치가 순간이동한다**는 걸 놓쳤다.

```
처치 순간   슬롯1 대상: A(결투 위치 ≈1m) → B(링 ≈6m)   ← 순간이동
            슬롯1 가중치: ≈1 그대로 이월, weightDamping(0.35초) 동안 감쇠
            → 그 0.35초 동안 그룹이 6m 밖 한 점을 무겁게 껴안는다 → 바운드 부풀음 → 카메라가 물러남
```

**수정**: `SetOpponent`에서 대상을 갈아끼울 때 **가중치를 같은 순간에 0으로 떨어뜨린다.**

대상이 튀는 그 프레임에 가중치가 0이면 **바운드 계산에서 아예 빠진다**. 새 상대는 `dropoffDistance` 안으로 들어오는 만큼만 다시 자리를 얻는다. D-1이 노렸던 연속성을 **대상 교체에도 적용**하는 것이다.

> `ponytail:` 나가는 상대의 가중치가 자기 마지막 위치에서 서서히 빠지는 게 이상적이지만, 그러려면 풀로 반납될 적의 위치를 붙잡아 둘 별도 앵커 Transform이 필요하다. 지금은 플레이어 단독 구도로 즉시 수렴하고 `GroupFraming.Damping`(1.0)이 카메라 반응을 매끄럽게 한다 — 적이 1m 거리였으므로 바운드 변화가 작아 눈에 띄지 않는다. 처치 순간을 더 붙잡고 싶어지면 그때 앵커를 둔다.
