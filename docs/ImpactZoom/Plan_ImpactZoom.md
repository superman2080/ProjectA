# Plan — ImpactZoom (마지막 노트 임팩트 줌)

근거: `docs/ImpactZoom/Research_ImpactZoom.md`

## 설계 요약

**한 문장**: `CameraDirector`에 **FOV 오프셋을 내는 두 번째 소비자**(줌)를 붙이고, 기존 펀치와 **합산**해 렌즈에 쓴다.

```
impactTime = JudgeTargetInfo.Deadline + Template.ImpactOffset      ← §7-3·§11과 같은 앵커
줌인  : [impactTime - zoomLeadTime, impactTime)  →  0 → zoomFovDelta   (zoomEase 배분)
줌아웃: [impactTime, impactTime + zoomReleaseDuration) → zoomFovDelta → 0  (선형)
조건  : Template.AllData.Count >= minNodeCount(3)
```

- **새 파일 없음. 새 이벤트 없음. 카탈로그 변경 없음.** 전부 `CameraDirector` 안이다.
  줌은 트리거 키로 갈리는 큐가 아니라 **패턴마다 항상 같은 램프**라 카탈로그 행이 아니다.
- **성패를 보지 않는다.** 시각만으로 도는 닫힌 함수라 실패·중단에서도 화면이 반드시 되돌아온다.
- **왜 `OnJudgeTargetBegan`인가**: 임팩트 시각과 노드 수를 **미리** 알 수 있는 유일한 이벤트다.
  `OnPatternComplete`는 마지막 노트 뒤라 줌인 구간이 이미 지나갔다.
- **`zoomFovDelta`는 음수**(FOV 감소 = 줌인)가 기본이다. `CameraCueEntry.punchFovDelta`와 부호 규약이 같다.

## 단계

- [x] **Step 1 — FOV를 합산 구조로 바꾼다** (`CameraDirector.cs`)
  - `ApplyPunch(float delta)` → `ApplyLens()`로 대체. 필드 `punchOffset`, `zoomOffset`을 두고
    `lens.FieldOfView = idle.fov + punchOffset + zoomOffset` 한 번만 쓴다.
  - `UpdatePunch()`는 `punchOffset`만 갱신하고 `ApplyLens()` 호출.
  - ⚠ `HoldForHitStop`의 `ApplyPunch(0f)` 자리는 **`punchOffset = 0f; ApplyLens();`** — 줌은 살린다.
  - 이 단계만으로는 동작 변화 없음(줌 오프셋이 항상 0). 회귀 확인 지점.

- [x] **Step 2 — 인스펙터 노브 추가** (`CameraDirector.cs`, `[Header("Impact Zoom")]`)
  | 필드 | 기본값 | 설명 |
  |---|---|---|
  | `zoomEnabled` | true | 끄면 이 층만 죽는다(기존 토글 규율) |
  | `zoomFovDelta` | −8 | 줌인 최대 FOV 변화(도). 음수 = 조여든다 |
  | `zoomLeadTime` | 0.30 | 임팩트 기준 몇 초 전부터 조일지 (0.45는 §겹침-2 때문에 못 쓴다) |
  | `zoomReleaseDuration` | 0.12 | 임팩트에서 휴지값으로 되돌아오는 시간 |
  | `minNodeCount` | 3 | 이 미만인 패턴은 줌하지 않는다 |
  | `zoomEase` | EaseInOut(0,0,1,1) | 줌인 구간의 시간 배분(`introEase` 선례) |

- [x] **Step 3 — 스케줄** (`HandleJudgeTargetBegan`)
  - 기존 한 줄(`angleSwitcher.OnPatternBoundary()`)에 줌 스케줄을 더한다.
  - `zoomEnabled && info.Template != null && info.Template.AllData.Count >= minNodeCount`일 때만
    `impact = info.Deadline + info.Template.ImpactOffset` 예약.
  - 조건 미달이면 **예약을 새로 잡지 않는다**(진행 중인 줌은 자기 시각대로 마저 끝난다).
  - ⚠ **덮어쓰면 안 된다** — 아래 §겹침-2. 진행 중인 줌이 있으면 **대기 슬롯**(`pendingZoomImpact`)에 넣고,
    현재 줌이 끝나는 프레임에 승계한다. 슬롯은 하나면 된다(패턴 완료가 순차적 — 기존 큐 예약과 같은 근거).
    승계 시점에 `impact - zoomLeadTime`이 이미 지났으면 **램프를 그 자리에서 시작**해 압축한다
    (건너뛰지 않는다 — 값이 튀는 것보다 짧은 램프가 낫다). `impact`마저 지났으면 그 패턴은 줌을 버린다.

- [x] **Step 4 — 갱신** (`UpdateZoom()`, `Update()`에서 `UpdatePunch()` 뒤에 호출)
  ```
  t = Time.time
  t <  impact - lead                  → 아직. zoomOffset 0 유지
  t <  impact                         → zoomFovDelta * zoomEase.Evaluate((t-(impact-lead))/lead)
  t <  impact + release               → zoomFovDelta * (1 - (t-impact)/release)
  else                                → zoomOffset 0, hasZoom = false
  ```
  - 매 프레임 `ApplyLens()` 호출은 값이 바뀔 때만(= `hasZoom`인 동안)이면 충분하다.
  - 히트스톱 잠금 중에는 `Update`가 조기 반환하므로 자연히 선다. **다만 그것만으로는 부족하다** — §겹침-1.

---

## ⚠ 히트스톱 · 앵글 교체와의 겹침 (검토 결과)

### 겹침-1 — 히트스톱 잠금과 줌아웃은 **시각이 정확히 같다**

`HitStopDirector`는 `Deadline + Pattern.ImpactOffset`에 발사하고(`HitStopDirector.cs:91`),
`hitStopDuration` 기본값은 **0.1초**(`:52`). 줌아웃 시작도 같은 `impact`, 길이 0.12초.
→ **줌아웃 0.12초 중 0.1초가 카메라가 얼어 있는 동안 지나간다.**

- `Update`가 조기 반환하니 `zoomOffset`은 안 변한다. 좋다.
- 하지만 해제되는 프레임에 `Time.time`은 이미 `impact + 0.1`이라
  램프가 **한 프레임에 delta → 0.17·delta로 점프**한다. 8도짜리면 **6.7도가 한 프레임에 풀린다.**
  "멈췄다가 부드럽게 풀린다"가 아니라 **툭 끊긴다** — `HoldForHitStop`이 쉐이크를 먼저 끄는 이유와 같은 문제다.
- ⚠ 게다가 잠금 = `brain.enabled = false`라 **그동안 vcam Lens에 쓴 값은 화면에 도달하지도 않는다.**
  얼어 있는 동안 램프를 굴리는 건 의미가 없다.

**해결 — 줌아웃도 '밀기'다.** 플레이어 `actionEndTime`, 적 `burstTime`이 정지 창만큼 밀리는 것과 같은 규칙
(CLAUDE.md §7-3 "두 배우 모두 밀기"). 카메라 줌이 **세 번째 배우**다.

- [x] **Step 4b** — `HoldForHitStop(duration)` 끝에 한 줄:
  `if (hasZoom) zoomImpactTime = Mathf.Max(zoomImpactTime, holdUntil);`
  - `Max`인 이유: `CameraDirector.Update`와 `HitStopDirector.Update`의 **실행 순서는 보장되지 않는다**(기존 지연 큐와 같은 상황).
    어느 쪽이 먼저 돌든 줌아웃은 **정확히 잠금 해제 순간부터** 시작한다.
  - 이러면 요구가 그대로 성립한다 — 조인 채로 얼었다가, 풀리는 순간부터 0.12초에 걸쳐 펴진다.
    "멈춘 다음에 연출"이라는 지연 큐 규율과 같은 결.
  - `hitStopEnabled = false`거나 실패한 패턴(히트스톱 없음)이면 이 줄이 안 타므로 예전대로 임팩트에 바로 풀린다.

### 겹침-2 — 스케줄 덮어쓰기가 **자기 임팩트 0.1초 전에 줌을 죽인다** (원안의 버그)

`OnJudgeTargetBegan`은 이전 패턴이 **완료되는 순간**(= 마지막 노드 입력, `L_N`)에 온다.
그런데 패턴 N의 임팩트는 `L_N + goodWindow(0.1)`이다 → **다음 패턴 스케줄이 N의 임팩트보다 먼저 도착한다.**

원안대로 덮어쓰면 `L_N` 시점에:
- N의 줌은 **최대로 조여 있는 상태**(임팩트 0.1초 전)
- 새 `impact`는 `L_{N+1} + 0.1 ≥ L_N + 0.5`, 램프 시작은 `L_N + 0.05`
- → `L_N`에는 아직 램프 밖이라 `zoomOffset = 0` → **한 프레임에 완전 줌인이 풀린다.**

즉 **줌아웃이 임팩트가 아니라 마지막 노드 입력에, 그것도 즉발로 일어난다.** 요구 정반대다.
Research §3의 "새 스케줄이 이긴다"는 틀렸다 — 그건 예약이 **겹치지 않는다**는 전제였고, 실제로는
스케줄 도착이 이전 임팩트보다 0.1초 빠르므로 **구조적으로 항상 겹친다**.

**해결**: Step 3의 대기 슬롯. 진행 중인 줌은 자기 임팩트까지 끝까지 살고, 다음 것은 그 뒤에 승계된다.

- ⚠ 램프가 잘릴 수 있다: 최소 간격 0.4초 − 줌아웃 0.12초 = 다음 램프에 남는 시간 **0.28초** < `zoomLeadTime` 0.45초.
  촘촘한 구간에서는 램프가 압축된다(위 Step 3의 "그 자리에서 시작"). 히트스톱 밀기까지 겹치면 더 짧아진다.
  → **`zoomLeadTime` 기본값을 0.3초로 낮춰 잡는다.** 0.45초는 대부분의 패턴에서 어차피 못 쓴다.

### 겹침-3 — 앵글 교체 (Step 5에서 처리)

블렌드(0.4초)가 줌 램프와 겹치면 화면이 뭉개진다 → `IsCuePlaying`에 포함.
단 `hasZoom` 그대로 쓰면 창이 없어져 교체가 영구 봉쇄되므로 **램프 시작 이후**만 게이트한다(Step 5 참조).

### 겹치지 않는 것

- **`RestoreIdle` / vcam 교체**: 기존 펀치와 같은 경로다. 앵글이 바뀌면 이전 vcam FOV가 씬 값으로 돌아가고
  새 vcam에 다시 걸린다 — 줌 오프셋은 `zoomOffset` 필드에 있지 vcam에 있지 않으므로 추가 처리 없음.
- **프레이밍 · 인트로**: 채널이 다르다(그룹 회전 / vcam 우선순위). FOV를 안 만진다.
- **쉐이크**: Perlin 채널. FOV와 무관.

---

- [x] **Step 5 — 앵글 교체 게이트에 줌 포함**
  - `IsCuePlaying => shakeDuration > 0f || punchDuration > 0f || hasZoom;`
  - 이유: 줌 램프 중 앵글 블렌드가 겹치면 화면이 뭉개진다.
  - ⚠ 부작용 점검: 줌이 패턴마다 상시로 돌면 `Tick`이 발사할 창이 좁아진다.
    창은 `impactTime + release` 이후 ~ 다음 `impactTime - lead` 사이 = 최소 0.4 − 0.12 − 0.45 < 0 이 될 수 있다.
    **→ 이 경우 앵글 교체가 영구히 막힌다.** 해결: 게이트는 `hasZoom`이 아니라
    **줌아웃 구간(`t >= impact`)에만** 걸지 않고, **줌인 램프가 시작되기 전이면 발사를 허용**한다.
    구현상 `IsCuePlaying`에는 `IsZoomActive`(= `hasZoom && Time.time >= impact - lead`)를 쓴다.

- [x] **Step 6 — 정리 경로**
  - `HandleAllCleared()`: `StopZoom()` 추가(예약 접기 + `zoomOffset = 0` + `ApplyLens()`).
  - `OnDisable()`: `StopShake()` 옆에 `StopZoom()` 추가 — 꺼진 채 좁혀진 FOV가 씬에 남지 않게.
  - `RestoreIdle`은 이미 FOV를 씬 값으로 되돌리므로 vcam 교체 경로는 추가 처리 불필요.

- [ ] **Step 7 — 씬 값 조정 (Unity, 코드 아님)**
  - `BattleScene`의 `CameraDirector`에 Step 2 기본값 세팅.
  - **`PatternSuccess` 큐의 `punchFovDelta` 재검토**: 임팩트에 펀치(줌인)와 줌아웃이 반대 방향으로 겹친다.
    줌아웃이 주 연출이면 이 값을 0으로 내리거나 부호를 뒤집는다(카탈로그 값 조정, 코드 무관).

## 검증

- [ ] **Step 8 — 확인**
  - 노드 3개 이상 패턴: 마지막 노트 전 화면이 조여들고 임팩트에 확 풀리는지.
  - 노드 2개 패턴: 줌이 아예 안 걸리는지(FOV 씬 값 그대로).
  - `zoomEnabled = false`: 쉐이크·펀치·프레이밍·인트로·앵글 교체 전부 정상.
  - 히트스톱 구간: 잠금 진입 순간 FOV가 휴지값으로 튀지 않는지(Step 1의 ⚠ 회귀 지점).
  - **히트스톱 해제 순간 줌아웃이 점프하지 않는지**(§겹침-1 / Step 4b). `hitStopEnabled` 끈 상태와 켠 상태 둘 다.
  - **연속 패턴에서 마지막 노드 입력에 줌이 풀리지 않는지**(§겹침-2). 촘촘한 구간(간격 0.4초)이 관찰 지점.
  - 앵글 교체가 여전히 `switchInterval` 주기로 일어나는지(Step 5의 창 봉쇄 여부).

## 하지 않는 것

- 줌을 `CameraCueEntry`로 카탈로그화 — 트리거별로 다를 값이 아니다. 필요해지면 그때.
- 노드별 시각을 `JudgeTargetInfo`에 추가 — 고정 리드타임이면 필요 없다.
- 돌리(`FollowOffset`) 기반 줌 — `GroupFraming`과 싸운다(CLAUDE.md §7-3).
