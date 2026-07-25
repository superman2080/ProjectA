# Plan — ReleaseRecovery (공격 종료 → 콤보 연계 / Run 복귀 블렌딩)

> 근거: `docs/ReleaseRecovery/Research_ReleaseRecovery.md`
> 피드백은 이 문서에 `>>>`로 남겨주세요. 확정 전까지 구현하지 않습니다.

## 설계 요약

**모든 변경은 `CharacterActionPlayer.cs` 안에서만.** 애니메이터 컨트롤러, `PatternHandler`, 이벤트 시그니처, 패턴 에셋은 건드리지 않는다.

공격이 트림 끝(`actionEndTime`)에 도달한 뒤의 처리를 **연계 유무로 갈라** 두 경로로 만든다.

```
                                 ┌─ 연계 있음(콤보) ─ 웨이트 1 유지 → 다음 공격으로 크로스페이드
 트림 구간 재생 ─→ actionEndTime ─┤
 배속 / 웨이트 1    AttackSpeed=1 └─ 연계 없음 ─ recovery 노출 → blend-out → Run
```

### 경로 1 — 연계 있음 (콤보 공격)
다음 공격이 곧 시작되면 **Attack Layer 웨이트를 1로 유지한 채** 다음 클립으로 넘긴다. 레이어를 내렸다 올리지 않으므로 사이에 Run이 비집고 들어오지 않고, 포즈 블렌딩은 듀얼 슬롯(Attack_A ↔ Attack_B) 사이의 **크로스페이드**가 담당한다.

- 이를 위해 크로스페이드 시간을 현재 `0.05` → `attackCrossFadeDuration`(기본 `0.15`)으로 늘린다. 지금 값은 사실상 컷 전환이라 콤보가 툭툭 끊긴다.
- **임팩트 타이밍은 밀리지 않는다.** `CrossFadeInFixedTime`은 목표 스테이트를 지정 시각부터 즉시 진행시키므로, 크로스페이드가 길어져도 트림 정렬(마지막 노드 = 임팩트)은 그대로다. 길어진 구간에 겹치는 것은 다음 베기의 **윈드업**이고, 이전 베기의 마무리와 섞이는 것이 오히려 자연스럽다.

### 경로 2 — 연계 없음 (Run 복귀)
`actionEndTime`부터 `recoveryHoldDuration` 동안 웨이트 1을 유지해 **버려지던 마무리 동작을 노출**하고(Research §1 — 클립은 이미 계속 재생 중이라 별도 재생 호출이 없다), 이후 `blendOutDuration` 동안 웨이트를 0으로 내려 base Run과 크로스블렌드한다.

### 공통
- **정상 속도 복귀** — `actionEndTime` 시점에 `AttackSpeed = 1`. 마무리 동작이 최대 2.5배속으로 지나가지 않게 한다. 두 경로 모두 적용.
- **연계 판정** — 성공 애니 예약 상태(`hasPending` + `pendingScheduleStart`)를 그대로 쓴다. 이미 `HandleJudgeTargetBegan`에서 계산해 두는 값이라 **새 이벤트 구독이 필요 없다.**
  ```
  linked = hasPending && (pendingScheduleStart - actionEndTime) <= comboLinkWindow
  ```
  기본값 `comboLinkWindow = 1.0`초. 근거는 아래 실측이다.

### 실측 근거 — 연계 간격 분포 (`Dreamer_lv10`, 연결 111개)
`pendingScheduleStart − 직전 actionEndTime`을 채보 에셋과 패턴 트림값으로 계산한 결과:

| 지표 | 값 |
|---|---|
| 최소 / 중앙값 / 최대 | 0.400 / **0.679** / 2.000 |
| ≤ 0.55초 | 51 / 111 (46%) |
| ≤ 0.80초 | 90 / 111 (81%) |
| **≤ 1.00초** | **105 / 111 (95%)** |

0.55 초과 값들은 `0.68·0.70·0.75·0.79·0.84·0.90` 에 밀집한 뒤 **1.2 → 1.6 → 2.0**으로 끊긴다. 즉 **0.4~0.9초 = 연계(95%), 1.2초 이상 = 공백(5%)** 로 분포가 갈리고 1.0초 부근이 깨끗한 경계다.

- **임계값을 `recoveryHoldDuration + blendOutDuration`(0.55)에서 유도하지 않는다.** 그 값은 실제 연계의 54%를 "연계 없음"으로 오판해, 절반가량이 Run으로 내려갔다 다시 올라오는 dip을 만든다. 유도식이 간결해 보였지만 데이터와 맞지 않았다.
- 이 분포가 뜻하는 바: **콤보 경로가 주 경로다**(95%). Run 복귀는 곡당 6회 남짓 발생하는 예외 경로다. 따라서 **튜닝 우선순위는 `attackCrossFadeDuration`**이고, `recoveryHoldDuration`/`blendOutDuration`은 그다음이다.
- 계산 주의: `actionEndTime`을 직전 패턴의 마지막 온셋으로 근사했다. 클립이 입력 구간보다 길어 배속 상한(2.5)에 걸리면 실제 `actionEndTime`이 조금 뒤로 밀려 간격이 그만큼 짧아진다. 경계가 1.0인지 1.2인지를 흔들 정도는 아니지만, Step 8에서 실측 로그로 확인한다.

---

## 튜너블 기본값 (`>>>`로 조정)

| 필드 | 기본값 | 의미 |
|---|---|---|
| `attackCrossFadeDuration` | `0.15` | 공격 간 크로스페이드 시간(초). 기존 `crossFadeDuration`(0.05) 대체. **주 경로(95%)의 품질을 좌우하는 값** — 최우선 튜닝 대상. `>>>` |
| `comboLinkWindow` | `1.0` | 이 시간 안에 다음 공격이 시작되면 연계로 보고 레이어를 내리지 않는다. 실측 분포의 절벽(0.9 ↔ 1.2)에서 잡은 값. `>>>` |
| `recoveryHoldDuration` | `0.25` | 연계 없을 때 마무리 동작을 웨이트 1로 노출하는 시간(초). `>>>` |
| `blendOutDuration` | `0.30` | Run으로 녹아드는 시간(초). 기존 `layerFadeOutDuration`(0.08) 대체. `>>>` |
| `layerBlendInDuration` | `0.08` | 레이어 웨이트를 현재값 → 1로 올리는 시간(초). Step 5 참조. `>>>` |
| blend 커브 | SmoothStep(ease-in-out) | 선형보다 시작/끝이 부드럽다. 필드 없이 코드 고정. `>>>` |

- 연계 없을 때 복귀에 총 0.55초가 쓰인다(hold 0.25 + blend-out 0.30). `comboLinkWindow`와는 **독립된 값**이다.
- `recoveryHoldDuration = 0`으로 두면 "긴 페이드만" 형태가 되어, 튜닝 중 두 효과를 분리해 확인할 수 있다.

---

## 구현 단계

- [x] **Step 1 — 튜너블 교체**
  - `crossFadeDuration`(0.05) → `attackCrossFadeDuration = 0.15f`로 이름·기본값 변경.
  - `layerFadeOutDuration`(0.08) 제거 → `blendOutDuration = 0.30f` 추가.
  - `recoveryHoldDuration = 0.25f`, `layerBlendInDuration = 0.08f` 추가. 모두 `[SerializeField]` + `[Tooltip]`.
  - **주의**: 필드명이 바뀌면 인스펙터 직렬화 값이 끊긴다. 씬의 `CharacterActionPlayer`에서 새 값이 기본값으로 들어왔는지 확인한다.

- [x] **Step 2 — 복귀 스케줄 / 상태 필드 추가**
  - `PlaySlot`에서 `actionEndTime` 계산 직후 `recoveryEndTime = actionEndTime + recoveryHoldDuration` 산출.
  - `bool speedRestored` — `actionEndTime`에서 `AttackSpeed`를 1로 되돌렸는지. `PlaySlot`에서 `false`로 리셋.
  - `bool blendOutLatched` / `float blendOutStartTime` / `float blendOutFromWeight` — blend-out **결정 시점에** 래치한다(Step 4 참조). `PlaySlot`에서 리셋.

- [x] **Step 3 — `actionEndTime`에 정상 속도 복귀**
  - `Update()`에서 `!speedRestored && Time.time >= actionEndTime`이면 `animator.SetFloat(attackSpeedHash, 1f)`, `speedRestored = true`.
  - 기존 "Attack → 비Attack 전이 중 `AttackSpeed` 1 복귀" 블록은 **이 로직에 포섭되므로 제거**한다(같은 일을 더 늦게 하던 코드).

- [x] **Step 4 — 연계 분기 + 웨이트 제어로 교체**
  - `Update()`의 기존 페이드 블록을 아래 순서로 대체한다.
    1. `Time.time < actionEndTime` → 웨이트 1 유지(재생 중).
    2. `linked` 판정(위 설계 요약의 식) → 참이면 웨이트 1 유지하고 종료. **콤보 경로.** blend-out을 아예 시작하지 않으므로 레이어가 중간에 내려갔다 올라오는 일이 없다.
    3. `Time.time < recoveryEndTime` → 웨이트 1 유지. **recovery 노출.**
    4. 그 외 → **blend-out.** 아직 래치 전이면 `blendOutStartTime = Time.time`, `blendOutFromWeight = 현재 웨이트`로 래치하고, 이후 `t = (Time.time - blendOutStartTime) / blendOutDuration`을 0~1로 clamp해 `weight = blendOutFromWeight * (1 - SmoothStep(0,1,t))`.
  - **래치를 `PlaySlot`이 아니라 결정 시점에 하는 이유**: 연계가 있다고 판단해 유지하던 중 그 연계가 취소되면(첫 미스로 예약 취소 등) 이미 `recoveryEndTime`을 한참 지난 상태다. 절대 시각으로 계산하면 웨이트가 0으로 튄다. 결정 시점의 현재 웨이트에서 시작해야 어느 경우든 이어서 부드럽게 내려간다.

- [x] **Step 5 — 레이어 blend-in (진입 측 팝핑)**
  - `PlaySlot`은 지금 웨이트를 **0에서 1로 즉시 점프**시킨다. blend-out이 0.3초로 길어지면 그 도중에 다음 공격이 끼어드는 경우가 잦아지고, 그때마다 `0.4 → 1` 스냅이 눈에 띈다.
  - `PlaySlot`에서 `blendInStartTime = Time.time`, `blendInFromWeight = 현재 웨이트`를 기록하고, `Update()`에서 웨이트 1을 "유지"하는 모든 지점을 **`blendInFromWeight → 1`로 `layerBlendInDuration` 동안 SmoothStep 상승**시키는 것으로 바꾼다. 이미 1이면 사실상 즉시 완료라 콤보 경로에는 영향이 없다.
  - 범위 확대로 보일 수 있으나, **Step 4가 만드는 "긴 blend-out 도중 인터럽트" 상황의 직접적 뒷정리**이므로 함께 처리한다.

- [x] **Step 6 — 크로스페이드 시간 반영**
  - `PlaySlot`의 `CrossFadeInFixedTime(..., crossFadeDuration, ...)`을 `attackCrossFadeDuration`으로 교체.
  - `fixedTimeOffset`의 배속 보정(`startOffset / speed`)은 **그대로 둔다** — 크로스페이드 길이와 무관한 별개 보정이다(현재 주석 유지).

- [x] **Step 7 — 주석/문서 문자열 갱신**
  - 클래스 상단 XML 주석에 두 경로(콤보 / Run 복귀)와 판정 기준을 명시.
  - **트림 끝은 재생 끝이 아니라 blend-out 시작점**이라는 점을 분명히 적는다 — 이 오해가 원래 증상의 원인이었다.

- [~] **Step 8 — MCP 실측 검증** (자동 검증 완료 / 육안·미스 케이스 미검증 — 아래 결과 절 참조)
  - **콤보 경로(주 경로, 95%)**: 연속 베기 구간에서 Attack Layer 웨이트가 **1에서 내려오지 않는지** 프레임 로그로 확인. 중간에 dip이 찍히면 `linked` 판정이 늦게 서는 것이므로 재검토.
  - **실측 간격 재확인**: `pendingScheduleStart - actionEndTime`을 런타임에 로그로 찍어, 위 정적 계산(중앙값 0.679 / 95%가 ≤1.0)과 일치하는지 본다. 배속 상한에 걸리는 케이스에서 벌어질 수 있다(설계 요약의 계산 주의 참조).
  - **복귀 경로**: 곡 공백 구간에서 `actionEndTime`에 `AttackSpeed`가 1로 돌아오고, 웨이트가 `recoveryEndTime`까지 1을 유지하다 `blendOutDuration`에 맞춰 0에 도달하는지.
  - **취소 케이스**: 콤보 유지 중 첫 미스로 예약이 취소될 때 웨이트가 0으로 튀지 않고 이어서 내려가는지(Step 4 래치 검증).
  - 실패(피격) 후에도 동일하게 복귀하는지.
  - 눈으로 A(팝핑) / C(마무리 부재) 해소 확인 후 값 튜닝.
  - **검증용 임시 로그는 전부 제거하고 클린 컴파일 확인.**

- [x] **Step 9 — 문서 갱신**
  - 확정된 튜닝 값을 본 Plan에 반영.
  - `CLAUDE.md`의 캐릭터 액션 절에 두 경로를 한 줄로 추가.

- [x] **Step 10 — Release 완주 후 페이드 (사용자 피드백 반영)**
  - **문제**: 공격 클립에 남은 tail이 없는 패턴은 트림 끝에서 곧바로 `Release`(2.33초 논루프)로 흘러가는데, 복귀 창(0.55초)이 그보다 짧아 **Release 재생 도중 웨이트가 0으로 잘렸다.**
  - **애니메이터 수정**(이번에 한해 범위 확대): `ReleaseSpeed`(float, 기본 1) 파라미터 추가 + `Release` 스테이트의 `m_SpeedParameterActive = 1`, `m_SpeedParameter = ReleaseSpeed`.
  - **코드**: `UpdateReleaseState()`가 Release 진입을 감지해 배속을 걸고, `normalizedTime >= 1`이 될 때까지 웨이트를 유지한 뒤 blend-out으로 넘긴다. 즉 **Release를 압축 재생해 완주시킨 다음 Run으로 페이드**한다.
  - **튜너블은 배속이 아니라 목표 재생 시간**(`releaseDuration`, 기본 0.9초). `배속 = 클립 길이 / releaseDuration`으로 역산한다(2.33초 클립이면 약 2.6배). 배속 값보다 "복귀에 몇 초 쓸 것인가"가 직관적이고, 클립이 바뀌어도 복귀 길이가 유지된다.
    - 클립 길이는 `AnimatorStateInfo.length`로 읽어 **에셋을 따로 배선하지 않는다.** 이 값은 스테이트 배속이 반영된 길이이므로, `PlaySlot`에서 `ReleaseSpeed`를 1로 되돌려 배속이 섞이지 않은 길이를 읽도록 보장한다.
  - 이 경로에서는 `recoveryHoldDuration`이 자연히 무의미해진다(Release 재생이 그 역할을 대신함).

  **구현 중 발견한 함정 — Release 오탐 (실측으로 발견, 2회 수정)**
  - `GetCurrentAnimatorStateInfo`는 **Release에서 빠져나가는 크로스페이드 도중에도 Release를 가리킨다.** 이걸 "Release 재생 중"으로 오인하면 공격이 시작되는 순간 복귀 로직이 붙잡혀, 실측에서 blend-out이 `2.47초 → 2.90초`로 밀렸다.
  - 1차 수정(전이 방향으로 판별)은 **불충분했다.** `CrossFade` 호출 다음 프레임에도 `IsInTransition`이 아직 false일 수 있어 같은 오탐이 재현됐다(`t=1.09`에 동일 로그).
  - 2차 수정(채택): `attackStateObserved` — **이번 액션의 공격 스테이트에 실제로 진입한 것을 관측한 뒤부터만** Release를 마무리로 인정한다. 애니메이터의 프레임 지연에 의존하지 않는다.

- [x] **Step 11 — Release를 트림 끝에 맞춰 시작 (회귀 수정)**
  - **증상**: 트림 끝과 클립 끝이 다른 클립(9개 중 8개)에서 Release가 **뒤늦게** 튀어나온다.
  - **원인 1 — 애니메이터**: `Attack→Release` 전이가 `ExitTime` 기반이라 **클립 전체가 끝나야** 발동한다. 트림 끝 이후 tail이 1.1~3.2초 남아 있어 그만큼 늦다.
  - **원인 2 — 코드(내가 만든 회귀)**: Step 10의 `UpdateReleaseState()`가 스테이트를 폴링해 "Release 재생 중"이면 웨이트를 유지했다. 그런데 그 시점엔 **이미 blend-out이 끝나 웨이트가 0이고 Run이 나오고 있었다.** `ApplyBlendIn()`이 호출되면서 `blendInStartTime`이 한참 과거라 보간이 즉시 완료 → **웨이트가 1로 튀어 Release가 갑자기 등장**했다.
  - **수정**: 스테이트 폴링을 버리고 **트림 끝에서 직접 `CrossFadeInFixedTime(Release)`** 한다. 재생 시간이 `releaseDuration`으로 고정되므로 종료 시각도 시간 계산으로 확정된다(`releaseEndTime = 트리거 시각 + releaseDuration`).
  - 폴링으로 세 번(오탐 2회 + 이번 회귀) 문제가 났다. **애니메이터 상태를 되묻지 않고 우리가 건 시각을 기준으로 삼는 편이 견고하다.**
  - 새 필드: `releaseClip`(길이를 읽어 배속 역산), `releaseCrossFadeDuration`(0.10). 제거: `releaseSpeedApplied`/`attackStateObserved`/`hasPlayedAction`/`UpdateReleaseState()`.
  - `recoveryHoldDuration`은 이제 **"Release로 넘어가기 전 클립 자체의 tail을 얼마나 보여줄지"**를 뜻한다. 트림 끝 즉시 Release를 원하므로 씬 값을 **0**으로 설정.

---

## Step 8 실측 결과 (플레이 모드, `Dreamer_Lv2` = 씬의 `debugChart`, 약 14초 구간)

임시 로그로 `PlaySlot` 시점의 레이어 웨이트와 직전 액션 종료 이후 간격을 수집했다. (로그는 확인 후 제거, 클린 컴파일 확인 완료.)

| 관측 gap(초) | 그때 웨이트 | 결과 |
|---|---|---|
| 0.107 / 0.133 / 0.134 / 0.402 / 0.674 / 0.828 | **1.00** | **콤보 경로 — dip 없이 웨이트 유지** ✅ |
| 1.310 / 1.606 / 3.933 | 0.00 | 연계 없음 → blend-out 후 재진입 ✅ |

- **최우선 확인 항목 통과**: 연계 구간에서 웨이트가 1에서 내려온 적이 없다. `hasPending`이 늦게 서서 생기는 dip(리스크 1번)은 관측되지 않았다.
- **blend-out 시간 일치**: `8.40 → 8.70`, `11.49 → 11.79` — 두 번 모두 **0.29~0.30초**로 `blendOutDuration = 0.30`과 일치.
- **`comboLinkWindow = 1.0` 경계가 다른 곡에서도 유효**: 이 채보의 간격 분포도 `0.828` ↔ `1.310` 사이에서 갈렸다. `Dreamer_lv10`(0.90 ↔ 1.2)과 절벽 위치가 사실상 같다 → 리스크 3번(곡 의존성) 상당 부분 완화.
- **`maxAttackSpeed`는 씬 값이 3.5다**(코드 기본값 2.5가 아님 — 필드명이 안 바뀌어 기존 값 유지). 공백 뒤 첫 공격은 `speed=3.50`으로 상한에 걸린다. 정적 계산의 "배속 상한" 주의사항이 실제로 발생하지만, 경계 판정을 흔들지는 않았다.

### Step 10 검증 상태
- 컴파일 클린, `ReleaseSpeed` 파라미터가 런타임 Animator에 등록됨(`parameterCount 2`) 확인.
- **오탐 2건은 실측 로그로 발견·수정**했으나, **2차 수정 후의 재검증은 하지 못했다.** Unity 창이 포커스를 잃으면 플레이 모드가 진행되지 않아(`is_focused: false`, `playmode_transition` 정체) 로그가 쌓이지 않았다.
- Release가 실제로 도달하는 패턴은 `Pattern(8, 5, 2, 1)`(tail 0.000)이고, 테스트 채보에서 **곡 18.0초**가 첫 등장이다. 확인하려면 그 지점까지 재생해야 한다.

### 미검증 (사용자 확인 필요)
- **육안 확인** — A(팝핑) / C(마무리 부재)가 실제로 해소됐는지, 그리고 `attackCrossFadeDuration = 0.15`가 콤보 감각에 맞는지는 직접 보셔야 한다. 튜닝 1순위.
- **미스 취소 케이스** — 콤보 유지 중 첫 미스로 예약이 취소될 때의 blend-out 래치(Step 4). 자동 재생만으로는 미스가 발생하지 않아 관측하지 못했다. 로직상 결정 시점 래치로 처리되지만 **실측되지 않았다.**
- 실패(피격) 후 복귀도 같은 이유로 미관측.

---

## 리스크 / 주의
- **`linked` 판정이 제때 서야 한다.** `hasPending`은 `HandleJudgeTargetBegan`(다음 패턴이 판정 대상으로 승계되는 시점)에 세워진다. 이는 이전 패턴의 완료/만료와 같은 프레임이므로 대체로 `actionEndTime` 부근이다. 만약 `actionEndTime`보다 늦게 서면 그 사이 blend-out이 시작됐다가 다시 올라가는 dip이 생긴다 → Step 8에서 최우선 확인. dip이 관측되면 `recoveryHoldDuration`이 이를 흡수하는지(hold 구간에는 어차피 웨이트 1) 함께 본다.
- **연계 시 Attack Layer 웨이트가 최대 1.0초까지 1로 유지된다.** 그동안 화면에 나오는 것은 정지 포즈가 아니라 **실제로 재생 중인 마무리 동작(tail 약 1.1초, 정상 속도)**이므로 굳는 느낌은 없어야 한다. 다만 tail이 짧은 클립에서 `ExitTime` 전이로 `Release`까지 흘러가 예상보다 정적으로 보일 수 있다 → Step 8에서 확인.
- **`comboLinkWindow = 1.0`은 `Dreamer_lv10` 한 곡에서 뽑은 값이다.** 다른 채보(특히 저난도, 간격이 넓은 곡)에서는 연계 간격이 더 벌어져 오판이 생길 수 있다. 곡이 추가되면 같은 방식으로 분포를 재확인한다.
- 연계 없을 때 **Attack Layer 활성 시간이 약 0.55초 늘어난다.** 곡당 6회 남짓인 공백 구간에서만 발생한다.
- Attack Layer는 **마스크 없는 전신 Override**다. 복귀가 길어지는 만큼 다리도 오래 공격 포즈를 유지한다. 다리 튐이 새로 눈에 띄면 아바타 마스크(상체) 도입을 별건으로 검토 — **이번 범위 밖.**
- `recoveryHoldDuration`이 남은 tail보다 길면 `ExitTime = 1.0` 전이로 `Release` 클립이 보이기 시작한다. 개정 전 문서가 의도했던 "연계 없을 때 Release 재생"이 **별도 로직 없이 이 경로에 흡수된 것**이다. `Release`가 Run과 이질적이면 hold를 줄여 tail 안에서 끝내는 쪽으로 튜닝한다.
- 크로스페이드를 너무 길게(> 0.25) 잡으면 짧은 트림 구간(예: 0.52초)의 앞부분이 이전 베기와 과하게 섞여 임팩트가 흐려진다. `0.15` 부근에서 시작해 올려본다.
- 튜너블 이름 변경으로 **인스펙터 값이 초기화**된다(Step 1 주의 참조).

---

## 범위 밖(하지 않음) — 개정 1 기준
- 애니메이터 컨트롤러/상태/전이 편집 — 대안안(Attack Layer에 `Run` 스테이트 추가)은 본 안 튜닝 실패 시에만 검토.
- `PatternHandler` 및 이벤트 시그니처 변경, `OnPatternComplete` 구독 추가.
- Run 다리 위상(footphase) 동기화, 아바타 마스크 도입.
- 패턴 에셋의 트림 값 재조정.
- 콤보 전용 클립/콤보 카운터 등 **데이터 측 콤보 시스템** — 여기서 말하는 "콤보"는 기존 연계 베기를 끊김 없이 잇는 **연출 처리**만을 뜻한다.

---
---

# 개정 2 — 연계 사이에 Run 노출 (재설계)

> 근거: `Research_ReleaseRecovery.md` §7~§10
> 상태: **검토 중 — 확정 전까지 구현하지 않음.** 피드백은 이 절에 `>>>`로 남겨주세요.

## 무엇이 바뀌나

개정 1은 **연계 유무로 "웨이트를 내릴지"를 갈랐다(2분기).** 개정 2는 이를 **3분기**로 바꾼다. 판정 축은 두 개다 — ① 다음 공격이 연계 창(`comboLinkWindow`) 안에 오는가, ② 그 사이 Run을 보여줄 만큼 간격(`minRunExposure`)이 있는가.

```
                                  ┌─ 연계 O · 간격 부족 ─ 웨이트 1 유지 → 바로 다음 Attack   (Release 없음, Run 없음)
 트림 구간 재생 ─→ actionEndTime ─┼─ 연계 O · 간격 여유 ─ 웨이트 ↓ → Run 노출 → 다음 Attack서 ↑ (Release 없음)
 배속 / 웨이트 1    AttackSpeed=1  └─ 연계 X            ─ Release 완주 → 웨이트 ↓ → Run
```

- **연계 O · 간격 부족** = `IsLinkedToNextAction()` 참 **이면서** `HasRoomForRunExposure()` 거짓. 개정 1의 콤보 경로 그대로 — 웨이트 1을 유지한 채 다음 공격으로 크로스페이드한다. Run도 Release도 나오지 않는다.
- **연계 O · 간격 여유** = 둘 다 참. 웨이트를 0으로 내려 Run을 노출한 뒤 다음 공격에서 다시 올린다. Release는 생략.
- **연계 X** = `IsLinkedToNextAction()` 거짓. Release를 완주한 뒤 웨이트를 내려 Run으로 복귀(개정 1과 동일).

아래 것들은 **그대로 유지**된다:
- 임팩트 정렬(`시작 = 마지막노드 − 재생시간`, 배속 재보정) — 다음 공격의 시작 시각은 건드리지 않는다.
- `actionEndTime`에서 `AttackSpeed = 1` 복귀.
- Release 경로의 압축 완주(`releaseDuration`)와 blend-out.
- blend-out 래치(결정 시점의 현재 웨이트에서 시작), blend-in(현재 웨이트 → 1).

## 반드시 함께 해야 하는 것 — 애니메이터 `ExitTime` 전이 제거

**이것을 빼면 개정 2를 해도 증상이 남는다.** `Attack_A/B → Release`가 `ExitTime = 1.0`으로 걸려 있어(Research §7), 코드가 무엇을 하든 클립 끝에서 Release가 재생된다. 연계 경로에서 웨이트가 0이면 눈에 보이지는 않지만, **Release 진입 경로를 코드 하나로 통일**하지 않으면 같은 종류의 버그가 계속 재발한다. 개정 1에서 폴링을 세 번 갈아엎은 것과 같은 교훈이다(Step 11).

→ 개정 1의 "범위 밖: 애니메이터 무수정" 제약을 **여기서 해제한다.**

## 새 튜너블

| 필드 | 제안 기본값 | 의미 |
|---|---|---|
| `minRunExposure` | `0.35` | **"연계 O · 간격 부족"과 "연계 O · 간격 여유"를 가르는 문턱.** 다음 공격까지 이 시간 이상 남아 있으면 웨이트를 내려 Run을 노출하고, 미만이면 웨이트 1을 유지한다(개정 1의 콤보 경로 그대로). 실측 간격 0.107/0.133/0.134초에서의 **웨이트 깜빡임 방지**(Research §10). `>>>` |

기존 값 중 재검토가 필요한 것:

| 필드 | 현재 | 제안 | 이유 |
|---|---|---|---|
| `blendOutDuration` | `0.30` | `0.15` `>>>` | 개정 1에서는 곡당 6회뿐인 예외 경로였지만, 이제 **모든 연계마다** 실행된다. 0.30이면 간격 0.40초의 연계에서 내려가는 데만 절반 이상을 쓴다. |
| `layerBlendInDuration` | `0.08` | `0.12` `>>>` | 마찬가지로 모든 공격 진입이 웨이트 0에서 시작하게 된다. 진입 팝핑이 개정 1보다 훨씬 자주 노출된다. |
| `recoveryHoldDuration` | 씬 값 `0` | 유지 `>>>` | 이제 **두 경로 공통**으로 "트림 끝 이후 마무리 동작을 웨이트 1로 노출하는 시간"을 뜻한다. 0이면 트림 끝 즉시 내려간다. |
| `comboLinkWindow` | `1.0` | 유지 `>>>` | 의미가 "웨이트를 유지할 간격"에서 **"Release를 생략할 간격"**으로 바뀐다. 실측 절벽(0.9 ↔ 1.2)은 그대로 유효. |

## `Update()` 최종 형태

```csharp
if (Time.time < actionEndTime) { ApplyBlendIn(); return; }   // 트림 구간 재생 중
if (Time.time < recoveryEndTime) { ApplyBlendIn(); return; } // 마무리 동작 노출(공통)

if (IsLinkedToNextAction())
{
    // 연계 — Release를 건너뛰고 Run을 드러낸다. 단 간격이 너무 짧으면 유지.
    if (HasRoomForRunExposure()) ApplyBlendOut();
    else ApplyBlendIn();
    return;
}

if (!releaseTriggered) TriggerRelease();
if (Time.time < releaseEndTime) { ApplyBlendIn(); return; }  // Release 완주까지 유지
ApplyBlendOut();
```

```csharp
// Run 노출 창(recovery 끝 ~ 다음 공격 시작)이 문턱 이상인가. 고정 간격으로 판정한다.
private bool HasRoomForRunExposure()
    => (pendingScheduleStart - recoveryEndTime) >= minRunExposure;
```

> **구현 중 수정**: 초안은 `pendingScheduleStart - Time.time`이었으나, 이 값은 매 프레임 줄어들어 Run 노출 도중 문턱을 밑돌면 `ApplyBlendOut → ApplyBlendIn`으로 분기가 뒤집힌다. 그 순간 `blendInStartTime`이 과거값이라 보간이 즉시 완료되어 **웨이트가 0→1로 튄다**(Step 11에서 겪은 스냅과 동종). 그래서 **고정 기준(`recoveryEndTime`)** 으로 바꿔 분기가 창 안에서 뒤집히지 않게 했다.

`recoveryHoldDuration`이 연계 판정보다 **앞으로** 이동한 점에 주의한다(개정 1은 연계 판정이 먼저였다). 두 경로가 동일한 마무리 노출을 갖게 하려는 의도다.

## 구현 단계

- [x] **Step 12 — 애니메이터 `ExitTime` 전이 제거**
  - `Attack_A`의 전이 `7410374101304509780`, `Attack_B`의 전이 `7638834329385509203`을 삭제하고 각 스테이트의 `m_Transitions`를 빈 배열로 만들었다.
  - `Release` 스테이트와 `ReleaseSpeed` 파라미터는 **그대로 둔다**(코드가 `CrossFadeInFixedTime`으로 직접 진입).
  - `Release`가 Attack Layer의 `m_DefaultState`인 것도 유지 — 웨이트 0이라 보이지 않는다.
  - 확인: 삭제 후 Attack 클립은 끝에서 마지막 포즈로 정지한다. 연계 경로에서는 이미 웨이트 0이므로 무영향.

- [x] **Step 13 — `minRunExposure` 추가**
  - `[SerializeField] private float minRunExposure = 0.35f;` + `[Tooltip]`.
  - `HasRoomForRunExposure()` 추가(위 코드).

- [x] **Step 14 — `Update()` 분기 재배치**
  - 위 "최종 형태"대로 교체. `recoveryEndTime` 체크를 연계 판정보다 앞으로 옮겼다.
  - 연계 경로에서 `TriggerRelease()`가 **절대 호출되지 않도록** 한다(`releaseTriggered`는 `PlaySlot`에서 리셋되므로 추가 처리 불필요).
  - 새 상태 필드 없음 — 기존 래치/블렌드 로직을 그대로 쓴다.

- [x] **Step 15 — 튜너블 기본값 조정**
  - `blendOutDuration` 0.30 → 0.15, `layerBlendInDuration` 0.08 → 0.12. (위 표의 결정 반영)
  - **필드명은 바꾸지 않으므로 씬의 직렬화 값이 그대로 남는다.** 씬 인스펙터에서 직접 갱신해야 한다 — Step 1의 반대 함정이다. (`minRunExposure`는 새 필드라 기본값 0.35로 들어온다.)

- [x] **Step 16 — 주석/문서 갱신**
  - `CharacterActionPlayer` 클래스 XML 주석을 개정 2(3분기) 기준으로 교체. "웨이트를 1로 유지한 채 크로스페이드로 잇는다"는 서술은 이제 "연계 O · 간격 부족" 경로에만 해당한다. `TriggerRelease` 주석의 ExitTime 근거도 "전이 제거됨" 기준으로 갱신.
  - `CLAUDE.md` §6의 "공격 종료 후 복귀" 문단을 3분기로 교체.

- [ ] **Step 17 — 실측 검증** (사용자 플레이 확인 필요)
  - 연계 경로: 트림 끝 이후 웨이트가 0까지 내려갔다가 다음 공격에서 1로 올라오는지, 그 사이 **Release 스테이트에 진입하지 않는지** 로그로 확인.
  - `minRunExposure` 가드: 간격 0.1~0.15초 구간에서 웨이트가 1을 유지하는지.
  - 비연계 경로: Release 완주 후 blend-out(개정 1과 동일 동작 유지 = 회귀 없음).
  - 육안: 짧은 연계에서 Run이 한 프레임 스치듯 지나가 어색하지 않은지 → `minRunExposure` 튜닝.
  - 임시 로그 제거 + 클린 컴파일 확인.

## 리스크

- **연계마다 전신이 Run으로 갔다가 돌아온다.** Attack Layer는 마스크 없는 전신 Override라, 0.4초 간격의 연계에서도 상체가 Run 포즈를 거친다. 개정 1이 피하려던 바로 그 현상이며, 이번에는 **의도된 연출**이다. 실제로 보고 어색하면 조정 순서는 `minRunExposure` ↑ → `blendOutDuration` ↑ → (그래도 안 되면) 아바타 마스크 검토.
- **`hasPending` 오판의 영향**(Research §8)이 남는다. 다음 패턴에 `SuccessAnimationClip`이 없거나 첫 미스로 예약이 취소되면 연계 없음으로 판정되어 Release가 나온다. 개정 2에서는 "웨이트가 튀는" 문제는 사라지고 "Release가 한 번 더 나온다" 정도로 완화된다.
- **`minRunExposure`는 실측 2곡 기준이다.** 배속 상한(씬 값 3.5)에 걸리는 케이스에서 간격이 크게 줄어드는 것이 관측됐으므로, 곡이 추가되면 분포를 다시 본다.

## 범위 밖 (개정 2)
- 다음 공격 시작 시각 변경 — **임팩트 정렬은 건드리지 않는다.**
- 아바타 마스크(상체) 도입.
- `PatternHandler` / 이벤트 시그니처 변경.
- `Release` 클립 자체의 교체·트리밍.

---
---

# 개정 3 — 경로별 Run 클립 분리 (경로 ②는 Sprint_HS)

> 근거: `Research_ReleaseRecovery.md` §11~§13
> 상태: **검토 중 — 확정 전까지 구현하지 않음.** 피드백은 이 절에 `>>>`로 남겨주세요.

## 무엇이 바뀌나

개정 2는 blend-out으로 드러나는 base 클립이 **경로 ②·③ 공통으로 `Run` 하나**였다. 개정 3은 이를 **경로별로 분리**한다.

```
                                  ┌─ 연계 O · 간격 부족 ─ 웨이트 1 유지 (base 안 보임 — 손대지 않음)
 트림 구간 재생 ─→ actionEndTime ─┼─ 연계 O · 간격 여유 ─ 웨이트 ↓ → base = Sprint_HS 노출
 배속 / 웨이트 1    AttackSpeed=1  └─ 연계 X            ─ Release 완주 → 웨이트 ↓ → base = Run 복귀
```

- base `Running Layer`는 평소 `Run`이 기본. **경로 ②를 탈 때 `Sprint`로 CrossFade**, **경로 ③을 탈 때 `Run`으로 CrossFade**해 되돌린다.
- 경로 ①(간격 부족, 웨이트 유지)은 base가 가려져 안 보이므로 전환하지 않는다.

**변경 범위**: 애니메이터 `Running Layer`(스테이트 1개 추가) + `CharacterActionPlayer.cs`. Attack Layer·`PatternHandler`·이벤트·패턴 에셋은 무수정.

## 새 튜너블

| 필드 | 제안 기본값 | 의미 |
|---|---|---|
| `runningLayerName` | `"Running Layer"` | base 레이어 이름. 인덱스/해시 캐시용. `>>>` |
| `runStateName` | `"Run"` | 경로 ③ 복귀 시 드러낼 base 스테이트. `>>>` |
| `sprintStateName` | `"Sprint"` | 경로 ② 노출 시 드러낼 base 스테이트. `>>>` |
| `baseCrossFadeDuration` | `0.2` | base 레이어 Run↔Sprint 포즈 블렌딩 시간(초). base가 가려진 동안 진행. `>>>` |

## 구현 단계

- [x] **Step 18 — 애니메이터 `Sprint` 스테이트 추가**
  - `Running Layer`에 `Sprint` 스테이트 신규 추가. 모션 = `Sprint_HS.anim`(guid `c6ce09bd2c3f38645b61adeccab81781`, fileID 7400000).
  - `m_Transitions: []`, `m_WriteDefaultValues: 1`(기존 `Run`과 동일), 스피드 파라미터 없음.
  - `Run`은 default 스테이트 그대로 유지.
  - **확인**: `Sprint_HS.anim`의 Loop Time이 켜져 있는지(연속 콤보 대비). 꺼져 있으면 켠다.

- [x] **Step 19 — 코드: base 레이어 캐시 + 전환 프리미티브**
  - 새 `[SerializeField]` 필드 4개(위 표) + `[Tooltip]`.
  - `Awake`에서 `runningLayerIndex = GetLayerIndex(runningLayerName)`, `runStateHash`/`sprintStateHash` 캐시. `currentBaseStateHash = runStateHash`로 초기화(default 스테이트).
  - `SwitchBaseState(int hash)`: `hash == currentBaseStateHash`이면 즉시 반환, 아니면 `CrossFadeInFixedTime(hash, baseCrossFadeDuration, runningLayerIndex, 0f)` 후 `currentBaseStateHash = hash`.
  - 레이어/해시 조회 실패 시 무연출(기존 방어 패턴과 동일하게 로그 후 스킵).

- [x] **Step 20 — 코드: `Update()` 경로별 호출 배선**
  - 경로 ②(`IsLinkedToNextAction() && HasRoomForRunExposure()` → `ApplyBlendOut()`) 직전에 `SwitchBaseState(sprintStateHash)`.
  - 경로 ③(비연계, `Time.time >= releaseEndTime` → `ApplyBlendOut()`) 직전에 `SwitchBaseState(runStateHash)`.
  - 경로 ①(간격 부족, `ApplyBlendIn()`)은 손대지 않는다.
  - 매 프레임 호출돼도 `SwitchBaseState`가 중복 진입을 막으므로 별도 래치 불필요.

- [x] **Step 21 — 주석/문서 갱신**
  - `CharacterActionPlayer` 클래스 XML 주석에 경로 ②=Sprint / 경로 ③=Run 분리를 반영.
  - `CLAUDE.md` §6의 "공격 종료 후 복귀" 문단에 한 줄 추가.

- [x] **Step 22 — 실측 검증** (플레이 확인 완료)
  - 경로 ②: 콤보 사이 노출 클립이 `Sprint_HS`인지 육안 확인. base 레이어가 `Sprint`로 CrossFade됐는지 로그.
  - 경로 ③: 공백 복귀 시 `Run`으로 되돌아오는지.
  - 전환 팝핑: Run↔Sprint 교체가 base 가림막 아래에서 끝나 튀지 않는지.
  - 연속 콤보에서 `Sprint_HS`가 루프로 매끄럽게 이어지는지(Loop Time 확인 겸 — 이미 Loop 켜짐, 길이 0.5초).
  - 임시 로그 제거 + 클린 컴파일.

- [x] **Step 23 — 판별 버그 수정 (실측으로 발견)**
  - **증상**: Release가 완주한 진짜 공백 이후에도 Sprint가 계속 재생됐다.
  - **원인**: `[BASE-DBG]` 프레임 로그로 확정. Release 완주(RELEASE-RUN) → base=Run 전환은 정상인데, **50~70ms 뒤 다음 판정 대상이 시작되며 `hasPending`이 true로 뒤집혀** LINKED-SPRINT 분기가 base를 곧바로 Sprint로 덮었다. Run은 스치듯 지나가 체감상 "Sprint가 계속".
  - **1차 오답**: `releaseTriggered`(Release를 트리거했는가)로 가드 → gap 대부분이 시작 순간 `hasPending=false`라 Release가 트리거되므로 **거의 전부 Run**이 되어 Sprint가 사라짐(과잉 교정, 로그로 확인).
  - **채택**: `releaseCompleted`(Release가 `releaseEndTime`까지 **완주**해 RELEASE-RUN 분기에 도달했는가)로 판별. 다음 연계에 인터럽트돼 완주 못 한 Release는 콤보 gap → Sprint, 완주한 Release는 진짜 공백 → Run. RELEASE-RUN에서 `true`, `PlaySlot`에서 `false` 리셋.
  - **메커니즘은 그대로**: bool 파라미터+전이가 아니라 기존 코드 CrossFade(`SwitchBaseState`) 유지 — 버그는 전환 방식이 아니라 판별 로직이었다.

## 리스크
- **Sprint와 Run의 다리 위상(footphase) 불일치.** 경로 ③에서 Sprint→Run으로 되돌릴 때 발이 어긋날 수 있으나 base 가림막 아래 `baseCrossFadeDuration` 블렌딩으로 대부분 흡수된다. 심하면 `baseCrossFadeDuration`을 늘린다.
- **`Sprint_HS.anim` Loop 미설정 시** 짧은 노출은 괜찮지만 연속 콤보에서 정지 포즈로 굳는다. Step 18에서 확인.
- Attack Layer는 전신 Override라 base 클립이 바뀌어도 공격 중에는 가려진다. 문제 없음.

## 범위 밖 (개정 3)
- Attack Layer / `PatternHandler` / 이벤트 시그니처 변경.
- Sprint·Run 다리 위상 동기화, 아바타 마스크.
- 임팩트 정렬·Release 로직 변경(개정 2 그대로).
