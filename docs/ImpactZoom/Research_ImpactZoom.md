# Research — ImpactZoom (마지막 노트 임팩트 줌)

## 요구

- 패턴의 **마지막 노트 타격 전**에 화면을 서서히 **줌인**하고, **임팩트 순간**에 **줌아웃**해 타격감을 높인다.
- 노트가 부족한 패턴에서는 연출이 성립하지 않으므로 **노드 3개 이상인 패턴에서만** 동작한다.
- 확정 사항(사용자 선택): **줌아웃 시점 = 임팩트 순간**(`Deadline + Pattern.ImpactOffset`), **줌인 시작 = 임팩트 기준 고정 리드타임**.

## 관련 파일

| 파일 | 역할 |
|---|---|
| `Assets/02. Scripts/Camera/CameraDirector.cs` | 카메라 연출 유일 관리 지점. 쉐이크(Perlin) · 펀치(FOV) · 프레이밍 · 인트로 · 앵글 교체 소유 |
| `Assets/02. Scripts/Camera/CameraCueCatalog.cs` | `CameraTrigger` enum + `CameraCueEntry`(쉐이크·펀치 값) |
| `Assets/02. Scripts/Camera/CameraAngleSwitcher.cs` | 앵글 vcam 교체. `CameraDirector.IsCuePlaying`을 발사 게이트로 씀 |
| `Assets/02. Scripts/Pattern/JudgeTargetInfo.cs` | 판정 대상 승계 페이로드(`Template`, `FirstNodeTime`, `LastNodeTime`, `Deadline`) |
| `Assets/02. Scripts/UI/PatternHandler.cs:369` | `OnJudgeTargetBegan` 발행 지점 |
| `Assets/02. Scripts/Pattern/ActivePattern.cs:25` | `NodeCount => Template.AllData.Count` |
| `Assets/02. Scripts/HitStop/HitStopDirector.cs` | 임팩트에 `CameraDirector.HoldForHitStop(D)` 호출 |

## 현재 구현에서 확인된 사실

### 1. FOV 채널은 이미 하나 있다 — 펀치

`CameraDirector.ApplyPunch(float delta)` (`CameraDirector.cs:667`)가 **렌즈 FOV를 쓰는 유일한 지점**이다.

```csharp
var idle = GetIdle(cam);
var lens = cam.Lens;
lens.FieldOfView = idle.fov + delta;   // ← 휴지값 + 오프셋 하나
cam.Lens = lens;
```

- **덮어쓰기 구조다.** 지금은 FOV 오프셋을 내는 소비자가 펀치 하나뿐이라 성립한다.
  줌이 같은 값을 쓰려면 **오프셋을 합산해서 한 번에 쓰는 형태**로 바꿔야 한다 — 아니면 둘이 서로를 지운다.
- **돌리(`FollowOffset`)에 걸면 안 된다.** `CinemachineGroupFraming`이 매 프레임 돌리를 계산하므로 싸운다.
  기존 펀치가 FOV를 고른 이유가 그것이고, 줌도 같은 이유로 FOV여야 한다(CLAUDE.md §7-3).
- **대상 vcam은 고정이 아니다.** `ResolveEffectCamera()`가 `brain.ActiveVirtualCamera`(live)를 매번 조회하고,
  대상이 바뀌면 `RestoreIdle(previous)`로 이전 vcam을 씬 값으로 되돌린다.
  휴지 FOV는 vcam마다 다를 수 있어 `idleByCamera`에 대상별로 캐시돼 있다 → 줌도 이 경로를 그대로 타야 한다.

### 2. 임팩트 시각을 아는 경로가 이미 둘 있다

- `HandlePatternComplete` (`:322`): `fireTime = info.LastNodeTime + handler.GoodWindow + info.Pattern.ImpactOffset`.
  **단 이건 패턴이 끝난 뒤**라 "마지막 노트 전에 줌인"에는 늦다.
- `HandleJudgeTargetBegan(JudgeTargetInfo)` (`:360`): 패턴이 **판정 대상이 되는 순간**에 온다.
  페이로드에 `Deadline`(= `LastNodeTime + goodWindow`)과 `Template`이 있으므로
  **`impactTime = info.Deadline + info.Template.ImpactOffset`을 미리 계산할 수 있다.**
  `Template.AllData.Count`로 **노드 수 조건(≥3)도 여기서 판정**된다.
  → 줌 스케줄의 진입점은 이 이벤트 하나로 충분하다. 새 이벤트·페이로드 확장 불필요.

### 3. 예약은 최대 하나면 된다

기존 큐 예약(`hasPending`)이 리스트가 아닌 근거가 그대로 적용된다 — 패턴 완료는 순차적이고
연속한 두 패턴의 입력 간격이 최소 0.4초다. 줌 스케줄도 `impactTime` 하나만 들면 된다.

⚠ 단 **줌은 임팩트 이후에도 잔여 구간(줌아웃)이 있다.** 다음 패턴의 스케줄이 그 잔여 구간 중에 들어오면
이전 줌아웃이 잘린다. 간격(≥0.4초) > 줌아웃 시간(0.1~0.2초 예정)이라 실무상 안 겹치지만, 겹치면
**새 스케줄이 이긴다**로 두는 게 단순하고 안전하다(FOV는 시간의 닫힌 함수라 값이 튀지 않는다).

### 4. 히트스톱 잠금과의 상호작용 (⚠ 여기가 유일한 함정)

`Update()` 첫 줄 (`:409`):

```csharp
if (holdUntil > 0f) { if (IsHolding) return; ReleaseHold(); }
```

- 잠금 중에는 `Update` 전체가 서므로 **줌 갱신도 같이 선다.** Brain도 꺼져 카메라가 굳으므로
  화면상으로는 정합한다(FOV가 안 변하는 게 맞다).
- 해제되면 시간이 `D`(≈0.08초)만큼 흘러 있으므로 줌아웃이 그만큼 **건너뛴다.**
  D가 0.05~0.10초이고 줌아웃이 그보다 짧지 않다면 값이 튀지 않고 앞당겨질 뿐이다 — 허용 가능.
- ⚠ **`HoldForHitStop`이 `ApplyPunch(0f)`를 부른다** (`:459`). FOV를 합산 구조로 바꾸지 않으면
  **잠금 진입 순간 줌이 통째로 지워진다** — 히트스톱은 임팩트 순간이므로 매번 걸린다.
  → 잠금은 **펀치만** 0으로 만들고 줌 오프셋은 유지해야 한다.

### 5. 앵글 교체와의 상호작용

- `CameraAngleSwitcher.Tick(IsCuePlaying)`는 `IsCuePlaying`(쉐이크·펀치 진행 중)이 false일 때만 발사한다.
  줌이 도는 동안 앵글이 바뀌면 **블렌드 + FOV 램프가 겹쳐 화면이 뭉개진다.**
  → `IsCuePlaying`에 줌 진행 여부를 **포함시켜야 한다**(한 줄).
- 대상 vcam이 줌 도중 바뀌면 `RestoreIdle`이 이전 vcam FOV를 되돌리고, 새 vcam에 다시 걸린다 —
  기존 펀치와 동일한 경로라 추가 처리 없음.

### 6. 실패·중단 경로

- 줌은 `OnJudgeTargetBegan` 시각에 **시간만으로 스케줄**되므로 패턴 성패와 무관하게 진행·복귀한다.
  성공/실패 분기를 볼 필요가 없다(실패해도 화면은 되돌아와야 한다).
- `OnAllPatternsCleared`(곡 중단)에서는 예약을 접고 FOV를 휴지값으로 되돌려야 한다
  (`HandleAllCleared`가 이미 `StopShake()`를 하는 자리).
- `OnDisable`에서도 같은 정리가 필요하다(`StopShake`와 같은 규율).

## 제약 요약

1. FOV 오프셋 소비자가 둘이 되므로 **덮어쓰기 → 합산**으로 바꿔야 한다.
2. 히트스톱 잠금은 펀치만 지워야 한다.
3. `IsCuePlaying`에 줌을 포함시켜야 앵글 교체가 줌을 안 덮는다.
4. 노드 수 조건은 `JudgeTargetInfo.Template.AllData.Count`로 판정 — 페이로드 확장 불필요.
5. Cinemachine 타입은 기존 네 이음매 밖으로 새지 않는다(줌도 `ApplyPunch`가 있던 그 자리에서만 렌즈를 만진다).
