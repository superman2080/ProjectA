# Plan — 고정 앵글 카메라(CameraFixedAngle)

근거: `docs/CameraFixedAngle/Research_CameraFixedAngle.md`

## 설계 요약

**회전은 활성 카메라가 "활성화되던 순간"에 한 번 정해지고, 그 뒤로는 안 변한다.**

| | 위치 | 방향(회전) |
|---|---|---|
| **활성** 카메라 | 플레이어를 따라 평행이동(중앙 고정) | **활성화 순간 값에 고정** |
| **비활성** 카메라 | 플레이어의 전면/후면 자리를 계속 추종 | 추종 중(활성화되면 그 값이 곧 고정값) |

구현의 전부는 **두 줄의 성격 변경**이다:

1. 그룹 회전을 **플레이어 yaw 추종 → 항등 1회**로. 그러면 `LockToTarget`의 `FollowOffset`이
   그대로 월드 오프셋이 되어 **카메라가 안 돈다**(vcam `BindingMode` 수정 0건).
2. 비활성 vcam의 `FollowOffset`만 매 프레임 `Euler(0, playerYaw, 0) * base`로 써 준다.
   활성 vcam은 **쓰지 않는다** → 그 값에 굳는다. "고정"에 새 상태 필드가 0개다.

플레이어 중앙 고정은 `RotationComposer` + `GroupFraming`이 이미 하므로 **코드 변경 0**이다.

전부 `CameraDirector.lockActiveAngle` **하나로 끌 수 있다**(false = 지금 동작 그대로). 회귀 0이 전제.

**교체의 자격과 전환 방식도 같이 바꾼다**(Step 7·8) — 자격을 **패턴 3개마다**로, 전환을 **블렌드 없는 컷**으로.
고정 앵글과 짝이 맞는 변경이다: 돌지 않는 카메라에서는 "언제 샷이 바뀌는가"가 화면 변화의 전부이고,
그 변화는 **음악의 박**(패턴 수)에 걸려야 읽힌다.

---

## - [x] Step 1 — `CameraAngleSwitcher`가 base 오프셋을 들고 쓴다

`CacheAngles`(`CameraAngleSwitcher.cs:85`)가 이미 vcam마다 `CinemachineFollow`를 집고 있다.
거기에 **참조와 base 오프셋을 같이 캐시**한다(`GetComponent`를 런타임에 돌리지 않는 기존 규율 유지).

```csharp
private CinemachineFollow[] follows;   // null이면 그 vcam은 오프셋 추종에서 빠진다
private Vector3[] baseOffset;          // 씬에 저작된 '플레이어 기준' 오프셋
```

새 public 하나:

```csharp
/// <summary>
/// 비활성 vcam을 플레이어 기준 자리(전면/후면)로 끌어다 둔다. 활성 vcam은 <b>쓰지 않는다</b> —
/// 쓰기를 멈추는 것이 "활성화 순간 회전 고정"의 구현 전부다(새 상태 필드가 없다).
/// </summary>
public void Aim(float playerYaw, bool includeActive = false)
```

- `Quaternion.Euler(0f, playerYaw, 0f) * baseOffset[i]`를 `follows[i].FollowOffset`에 대입.
- `i == currentIndex`는 `includeActive`일 때만 쓴다(= `Setup` 직후 1회).
- `follows[i] == null`이면 건너뛴다(조용히 비활성되는 기존 규율).

**⚠ `baseOffset`을 캐시해야 한다.** `FollowOffset`을 읽어 다시 회전시키면 매 프레임 누적돼 카메라가 궤도를 돈다.

---

## - [x] Step 2 — 활성화 순간 감쇠를 건너뛴다 (⚠ 이게 없으면 "고정"이 깨진다)

`Select(index)`(`:196`) 끝에 한 줄:

```csharp
cameras[index].PreviousStateIsValid = false;
```

**근거**: `Cam_ShoulderBack`의 `PositionDamping`이 (2, 0.4, 2)다. 비활성 추종이 감쇠에 끌려
목표에 못 닿은 상태로 활성화되면, 쓰기를 멈춘 뒤 **남은 오차를 2초 가까이 따라가며 플레이어 주위를
미끄러진다** — 화면에서는 "회전이 고정되지 않는다"로 보인다. 감쇠를 한 번 건너뛰면 활성화 프레임에
정확한 자리로 스냅하고, **전환의 부드러움은 블렌드가 담당한다**(스냅은 블렌드 시작 전이라 안 보인다).

---

## - [x] Step 3 — 블렌드 중에는 오프셋을 쓰지 않는다

`Aim` 호출부에 `brain.IsBlending` 가드.

**근거**: 방금 비활성이 된 카메라가 곧바로 추종을 재개하면 **블렌드의 소스가 움직여** 화면이 휘청인다.
블렌드가 끝난 뒤 그 카메라는 보이지 않는 곳에서 제 자리로 돌아간다(감쇠 2초가 여기서는 오히려 득이다).

---

## - [x] Step 4 — `CameraDirector`: 회전 추종을 끊는다

새 필드:

```csharp
[Header("Framing")]
[Tooltip("활성 카메라의 각도를 활성화 순간에 고정한다(= 카메라가 플레이어 회전을 따라 돌지 않는다).\n" +
         "비활성 카메라만 전면/후면 자리를 계속 추종하고, 활성화되는 순간 그 값에 굳는다.\n" +
         "끄면 예전 동작(그룹 회전이 플레이어 yaw를 상시 추종)이다 — cameraTurnDamping·yawCutThreshold는 그때만 쓰인다.")]
[SerializeField] private bool lockActiveAngle = true;
```

① `SetupFraming`(`:311`) 끝: 고정 모드면 **그룹 회전을 항등으로 한 번** 쓰고 `angleSwitcher.Aim(playerYaw, includeActive: true)`.
   - 항등으로 두는 것이 `LockToTarget`을 월드 좌표로 바꾸는 장치다(Research 참조).
   - `includeActive`가 여기만 true인 이유: 0번 카메라도 **첫 프레임부터** 플레이어 등 뒤에 있어야 한다.
     (씬 base 값을 안 회전시키면 "플레이어 yaw = 0" 가정이 되어 시작 구도가 플레이어 초기 방향에 따라 어긋난다.)

② `UpdateFraming`(`:800`): 고정 모드면 yaw 감쇠·컷 블록(`:814`~`:831`)을 건너뛰고 `Aim(playerYaw)`만 부른다.
   - 가중치 계산(`ResolveWeight`·`Damp`)과 멤버 갱신은 **그대로 돈다** — 요구가 "플레이어 중앙 + 상대도 담기"다.
   - `Aim`은 `optionAngleSwitch`와 **묶지 않는다**. 앵글 교체를 꺼도 "카메라가 안 돈다"는 참이어야 하고,
     교체가 꺼지면 활성 카메라가 안 바뀌어 `Aim`은 비활성 쪽만 건드리므로 **화면에 영향이 없다**.

③ `ApplyFraming`(`:879`): 고정 모드에서는 `targetGroup.transform.rotation` 대입을 **건너뛴다**.
   - 매 프레임 항등을 다시 쓰는 것과 결과는 같지만, "회전을 쓰는 곳이 하나"라는 사실을 코드가 말해야 한다.

④ 히트스톱 잠금은 `Update` 조기 반환 안이라 `Aim`도 같이 선다 — **잠금 중 카메라가 1픽셀도 안 움직인다**는
   기존 보장이 그대로 유지된다(§7-3). 확인만 하고 새 코드는 없다.

---

## - [x] Step 5 — 씬: 후면/측면 vcam 둘, 거리 7m

**전면은 쓰지 않는다**(결정, Step 10 참조). **후면 + 측면**이고 **둘의 거리·높이가 같다** —
그러면 컷이 *샷 크기*가 아니라 **방위만** 바꿔, 전환이 "다른 데서 본다"로 정확히 읽힌다.

- [x] 1. `Cam_ShoulderBack` → `FollowOffset = (0, 3.5, -7)` (등 뒤 **7m**, 높이 3.5).
   - 기존 값 `(0, 5, -10)`에서 거리를 줄인 것이다. 고도각은 유지했다(`atan(5/10) ≈ atan(3.5/7) ≈ 26.6°`).
- [x] 2. **복제하지 않고 `Cam_RightLow`를 재활용했다** — 이름을 **`Cam_SideRight`** 로 바꾸고
   `FollowOffset = (-7, 2, -7)` → **`(7, 3.5, 0)`** (오른쪽 **7m**).
   - 이미 `RotationComposer`(`TargetOffset.y = 1.5`)·`GroupFraming`·`Perlin`·`CinemachineConfiner3D`·
     `Priority 0`을 들고 있어 **복제가 필요 없었다.** 새 fileID가 안 생기므로 씬 diff도 벡터·이름 둘뿐이다.
   - ⚠ `Cam_ShoulderBack`에만 `CinemachineDeoccluder`가 붙어 있다(측면엔 없다) — Step 9에서 볼 것.
- [x] 3. `CameraDirector.angleSwitcher.cameras` = **[`Cam_ShoulderBack`, `Cam_SideRight`]** 두 칸으로.
   `switchEnabled`도 켰다(씬에 `0`으로 저장돼 있어 **교체가 아예 꺼져 있었다**).
   - 0번이 곡 시작 구도다(후면 = 등 뒤가 기본).
   - **`Cam_NearLowRight`·`Cam_FarShoulderRight`는 씬에서 지우지 않는다.**
     배열에서 빠지면 `Priority`가 0에 남아 활성(10)을 절대 못 이긴다 — 되돌리기가 배열 한 줄이다.
- [x] 4. ~~`switchInterval = 0`으로 내린다~~ → **코드로 해결했다**(아래 변경점 1). 씬 값(10)을 안 건드린다.
- [x] 5. **각도 밴드(`minSwitchAngle 30` / `maxSwitchAngle 120`)는 손대지 않았다.**
   - 후면 yaw `Atan2(0, -7) = 180°`, 측면 `Atan2(7, 0) = 90°` → **델타 90°로 밴드 안**이다.
   - **90° 교체는 180도 법칙을 지킨다**(좌우가 뒤집히지 않는다). 전/후면 안을 버린 이유 중 하나가 이것이다.

---

## - [x] Step 7 — 교체 자격을 **패턴 N개마다**로 (기본 3)

**지금**: 자격이 `armOnOpponentChange`(상대 교체) + `switchInterval`(6초 쿨다운)이다
(`CameraAngleSwitcher.cs:119`·`:127`). 상대 교체는 **플레이어가 무대를 가로지르는 구간**에 교체를 묶는
장치였는데, 고정 앵글에서는 그 근거가 사라진다 — 카메라가 안 돌므로 "큰 움직임 둘을 합친다"가 성립하지 않는다.

**바꾼다**: `OnPatternBoundary()`에서 패턴을 세고 **N개마다 예약**한다.

```csharp
[Tooltip("패턴 몇 개마다 앵글을 바꿀지. 0 이하면 이 방식을 끄고 armOnOpponentChange/쿨다운이 자격을 든다(예전 동작).")]
[SerializeField] private int patternsPerSwitch = 3;

private int patternCount;
```

`OnPatternBoundary()`:

```csharp
if (patternsPerSwitch > 0)
{
    if (++patternCount % patternsPerSwitch == 0) Arm();
    return;                    // 셈법이 켜지면 상대 교체·경계 쿨다운 경로는 쓰지 않는다
}
if (armOnOpponentChange) return;
Arm();
```

- **우선순위를 값으로 적는다**: `patternsPerSwitch > 0`이면 그것이 이기고 `armOnOpponentChange`는 무시된다.
  enum을 새로 만들지 않는다 — 셋 중 하나를 고르는 문제가 아니라 **하나가 다른 둘을 대체**하는 문제다.
- `OnOpponentChanged()`도 같은 가드로 물러난다(안 그러면 자격이 둘이 되어 3개 주기가 깨진다).
- `Reset()`에서 `patternCount = 0`(곡 중단 → 다음 곡이 1번부터 센다).

**⚠ `switchInterval`이 6초면 이 기능이 조용히 삼켜진다.** 패턴 주기가 약 1.4초라 3개 = 약 4.2초 < 6초이고,
`Arm()`(`:127`)이 쿨다운을 먼저 본다 → **예약이 매번 버려진다**. 씬에서 `switchInterval = 0`으로 내린다
(필드는 하한으로 남긴다 — 패턴이 촘촘한 채보에서 상한선이 필요해지면 그때 쓴다).

**⚠ 세는 것은 "판정 대상 승계"다.** `OnPatternBoundary`의 출처가 `OnJudgeTargetBegan`이라
**취소된 패턴(§16 재시도)도 한 프레임 판정 대상이 되므로 한 칸 센다.** 재시도 구간에서 주기가
한 칸 밀릴 수 있다 — 샷 전환 주기라 그 오차를 고치는 값은 없다고 보고 둔다.

**발사 규율은 그대로**: 예약(`Arm`) → 카메라 큐 종료 후 발사(`Tick`). 컷이라 블렌드가 없어도
**임팩트 프레임에 샷이 갈리는 것은 여전히 피한다**(쉐이크·펀치·줌이 도는 중에 끊으면 "전환"이 아니라 "글리치"다).

---

## - [x] Step 8 — 전환을 블렌드 없는 **컷**으로

**지금**: `ApplyBlendDuration`(`CameraDirector.cs:353`)이 `Brain.DefaultBlend.Time`에
`angleBlendDuration`(0.4초)을 써 넣고 **Style은 건드리지 않는다**(씬 값 존중).

**바꾼다**: Style까지 코드가 정한다.

```csharp
[Tooltip("앵글 전환을 블렌드 없이 즉시 컷으로 한다. 끄면 angleBlendDuration 초만큼 블렌드한다(예전 동작).")]
[SerializeField] private bool cutBetweenAngles = true;
```

`ApplyBlendDuration`에서 `cutBetweenAngles`면 `blend.Style = CinemachineBlendDefinition.Styles.Cut`.

- **Brain 하나가 런타임 진실의 원천**이라는 기존 규율을 그대로 쓴다(값을 **써 넣고** 이후 아무도 안 읽는다).
  per-vcam 커스텀 블렌드(`CinemachineBlenderSettings`)를 새로 만들지 않는다 — 전환이 전부 컷이면 테이블이 죽은 데이터다.
- **`Select()` 뒤에 `brain.ActiveBlend = null`로 끊는 방식은 쓰지 않는다.** Brain이 우선순위 변화를
  알아채는 프레임이 밀려서(인트로가 실측 3프레임을 겪었다, `IntroCutGuardFrames`) **아직 없는 것을 끊는
  no-op**이 되고 그 뒤에 생긴 블렌드가 그대로 끝까지 돈다. Style로 못박으면 그 부류가 원천 소멸한다.

**⚠ 인트로 마무리도 같이 컷이 된다 — 알고 받는다.** `IntroRoutine`(`:1117`)이
`brain.DefaultBlend.BlendTime`을 읽는데 `Cut`이면 그 게터가 **0을 돌려준다**(실효값). 그래서:
- `travel = duration - 0 = duration` → **스플라인이 카운트다운 전체를 쓴다**(인트로가 길어지는 쪽이다).
- 끝에서 게임플레이 구도로 **블렌드 대신 컷**으로 들어간다. 곡 시작 프레임의 컷이라
  거슬리면 `cutBetweenAngles = false`로 두고 Step 7만 쓰거나, 인트로 전용 블렌드를 따로 두는 별건이 된다.
- 컷 가드 루프(`:1106`)는 블렌드가 아예 생기지 않으므로 **첫 프레임에 빠져나온다** — 동작에 문제 없다.

---

## - [x] Step 10 — 이동 패턴과 무대 경계 (조사 완료 · 코드 0줄)

**질문**: 플레이어를 크게 미는 패턴(`Pattern.duelDistanceCurve`, 무대 횡단 대시)에서 카메라가
무대 밖으로 밀려 나가지 않는가.

**답: 이미 막혀 있다. 새 코드가 0줄이다.**

- 앵글 vcam **넷 전부**에 `CinemachineConfiner3D`가 붙어 있다
  (`BattleScene.unity:1282`·`7793`·`9019`·`10025`). 볼륨은 `CameraBounds` BoxCollider —
  **29.48 × 8 × 29.12, 중심 (0, 5, 0.18)**(`:14587`), `SlowingDistance: 2`.
  무대 원(`arenaCenter` = 월드 원점, 반경 8m)과 **같은 원점**이라 둘이 이미 짝이다.
- `RotationComposer`가 그룹을 조준하므로 **클램프돼도 플레이어는 중앙에 남는다.**
- 플레이어가 카메라 뒤로 빠지는 일은 구조적으로 없다 — 플레이어 최대 8m(무대 반경), 벽 ±14.56~14.74m.

### 그래서 남는 것은 "클램프가 일어날 때"뿐이고, 그것을 **거리로** 없앴다

| | 최악의 경우 카메라가 가려는 거리 | 벽 | 클램프량 |
|---|---|---|---|
| 거리 10m (원안) | 8 + 10 = **18m** | 14.56m | **3.4m** |
| 거리 **7m** (Step 5) | 8 + 7 = **15m** | 14.56m | **0.4m** |

**⚠ 클램프가 "회전 고정"을 깨기 때문에 이 숫자가 중요하다.** 위치가 꺾이면 `RotationComposer`의
조준 방향이 바뀐다 = **화면이 돈다** → Step 1~4가 없애려던 것이 벽 근처에서만 되살아난다.
7m면 그 양이 0.4m(그마저 플레이어가 무대 끝에서 정확히 바깥쪽으로 밀릴 때만)로 줄어 사실상 사라진다.
덤으로 `GroupFraming`(DollyOnly)이 물러나려 하고 confiner가 막는 **둘의 싸움**도 같이 없어진다.

### 전면 앵글을 버린 이유 셋

1. 돌진 방향이 곧 카메라 방향이라 **클램프가 가장 잘 걸린다**(위 표의 최악 경우가 전면에서 난다).
2. **플레이어가 적을 가린다** — 적이 플레이어 뒤에 숨는다. 경계와 무관한 가독성 비용이다.
3. 후면과 180° 차이라 **180도 법칙을 어긴다**(좌우가 뒤집힌다). 측면은 90°라 지킨다.

측면은 그 셋을 동시에 피하고, **이동이 화면 가로 성분이 되어 속도감이 가장 잘 읽힌다**(§11-2가
"속도감의 심장"이라 부른 그 이동이 전면/후면에서는 원근으로만 보인다).

### ⚠ 안 하기로 한 것 — 패턴별 앵글 지정

데이터는 이미 있다(`duelDistanceCurve`가 비었는지 · `Pattern.DuelCurveEndsBehind`). 그런데
**교체 자격이 "패턴 3개마다"라 특정 패턴에 묶이지 않는다** — 둘을 맞추려면 자격 규칙을 다시 짜야 하고
그러면 Step 7이 무의미해진다. 거리 하나로 해결되는 문제에 저작 필드를 만들지 않는다.

---

## - [ ] Step 9 — 검증

- **패턴을 세며 플레이 한다.** 3개마다 샷이 갈리는가. `switchInterval`을 안 내리면 **아무 일도 안 일어난다**(Step 7 ⚠).
- **전환이 즉시인가**(0.4초 미끄러짐이 남아 있으면 Style이 안 먹은 것이다).
- **플레이어가 제자리에서 상대를 바꿔 본다**(사슬 → 원거리 상대). 화면이 **한 픽셀도 돌지 않아야** 한다.
- **대시로 무대를 가로지른다.** 카메라가 평행이동만 하고 플레이어가 중앙에 남는가.
- **앵글 교체 순간**: 블렌드 뒤에 카메라가 미끄러지지 않는가(Step 2가 빠지면 여기서 2초쯤 흐른다).
- **교체 직후 플레이어가 돈다**: 카메라는 그대로 있고 플레이어만 돌아야 한다(= 고정 성립).
- **이동 패턴을 무대 끝에서 바깥쪽으로 민다**(Step 10): 카메라가 벽에 막혀 화면이 **도는지** 본다.
  돌면 거리를 7m에서 더 줄인다(6m면 클램프가 음수가 되어 구조적으로 안 걸린다).
- **컷에서 수직 구도가 튀는지 본다.** 두 vcam의 `RotationComposer.TargetOffset.y`가 다르다
  (`Cam_ShoulderBack` **2.5** vs `Cam_SideRight` **1.5**) → 거리·높이를 맞췄는데도 **조준 높이가 달라**
  컷이 방위뿐 아니라 수직 구도까지 바꾼다. 거슬리면 둘을 같은 값으로. **일부러 안 고쳤다** — 2.5가
  저작 의도일 수 있고, 눈으로 보고 정할 문제다.
- **`Cam_ShoulderBack`에만 `CinemachineDeoccluder`가 붙어 있다.** 가림이 생기면 그것이 카메라를 당기는데,
  confiner 클램프와 **같은 부류로 "회전 고정"을 깬다**(Step 10 ⚠). 후면 샷에서만 화면이 돌면 범인이 이것이다
  — 측면에는 없으므로 둘을 비교해 보면 바로 갈린다.
- 히트스톱 프레임에 카메라 정지 유지(기존 보장 회귀 확인).
- `lockActiveAngle = false`로 되돌려 **예전 동작이 그대로인지** 확인(회귀 0).

---

## 구현 결과 (2026-10-16)

코드 전부 구현(Step 1·2·3·4·7·8). 컴파일 에러·경고 0. 씬은 오프셋 둘 + 이름 하나를 Unity MCP로 반영했다.

### Plan과 달라진 곳 둘

1. **`switchInterval`을 씬에서 내리는 대신 코드가 무시하게 했다.** Step 7의 ⚠가 "씬 값이 크면 교체가
   조용히 전부 삼켜진다"는 **침묵 실패**를 문서로만 막고 있었다. 자격이 둘(패턴 수 · 쿨다운)이면
   어느 쪽이 실제로 주기를 정하는지 알 수 없으므로 `Arm()`이 `patternsPerSwitch > 0`일 때
   쿨다운을 **보지 않는다** — 패턴 수가 곧 간격이다. 씬의 `switchInterval: 10`이 무해해져
   **수동 작업 한 칸이 사라졌다.**
2. **`Cam_SideRight`를 복제가 아니라 `Cam_RightLow` 재활용으로 만들었다.** 그 vcam이 이미 필요한
   컴포넌트 다섯을 들고 있어 복제가 순수한 낭비였고, 새 fileID가 안 생겨 씬 diff가 벡터·이름 둘로 끝났다.

### 중첩 `[Serializable]` 필드는 `Unity_RunCommand`로 썼다

`Unity_ManageGameObject`는 `angleSwitcher.*`에 못 쓴다
(`Could not find property or field 'angleSwitcher'`) — 중첩 직렬화 클래스를 모른다.
`SerializedObject` + `FindPropertyRelative`를 쓰는 **일회성 C# 실행**(`Unity_RunCommand`)으로 설정했고,
**에디터 스크립트 파일을 남기지 않았다.** 같은 일이 또 필요하면 이 경로를 쓴다.

저장된 씬에서 확인한 값(`BattleScene.unity:1613`~`1626`·`:1687`):

```
angleSwitcher:
  switchEnabled: 1          ← 0이었다. 안 켜면 Step 7·8이 전부 무효다.
  cameras: [Cam_ShoulderBack, Cam_SideRight]   (4 → 2)
  switchInterval: 10        ← 그대로 둔다. patternsPerSwitch가 켜져 있으면 Arm()이 안 본다.
  patternsPerSwitch: 3
  minSwitchAngle: 30 / maxSwitchAngle: 120     (손대지 않음 — 델타 90°)
cutBetweenAngles: 1
lockActiveAngle: 1
```

씬을 저장할 때 이미 dirty 상태였지만, 실제 diff는 **13 insertions / 12 deletions 전부 카메라 영역**이었다
(vcam 이름·오프셋 둘, 디렉터 필드 넷, Cinemachine이 다시 계산한 vcam Transform 셋).
무관한 변경이 섞이지 않았다.

---

## 범위 밖 (의도적으로 안 함)

- **전면 카메라** — Step 10에서 **버렸다**(클램프·가림·180° 뒤집힘 셋). 꼭 필요해지면
  거리를 6m 이하로 줄여 `cameras` 배열에 한 칸 더 넣는 것으로 끝난다.
- 활성 카메라에 "느린 복귀"(플레이어가 너무 멀리 돌아보면 천천히 재정렬) — 요구가 "회전하지 않는다"라
  지금은 넣지 않는다. 필요해지면 `Aim`에 활성 포함 + 큰 시간상수로 한 줄.
- `cameraTurnDamping`·`yawCutThreshold` 제거 — 예전 경로가 살아 있어야 회귀 0이다. 분기 밖에 둔다.
- 씬 `RotationComposer.TargetOffset.y` 표류(1.5 / 0 / 2.5) 정리 — 독립 작업.

---

# 2차 — 결투 축 기준 고정 (A안)

근거: `Research_CameraFixedAngle.md`의 "2차" 절.

## 설계 요약

**각도의 기준을 플레이어 yaw에서 결투 축(`DuelPlan.Axis`)으로 바꾼다.** 결투 하나가 이어지는 동안 화면이 돌지 않는다는
1차의 성질은 그대로 두고, **다시 설 시점과 방향을 상대의 위치가 정한다.**

| | 1차 | 2차(A안) |
|---|---|---|
| 각도의 기준 | 활성화 순간의 플레이어 yaw | **결투 축 yaw + `shoulderYaw`** |
| 바뀌는 시점 | 패턴 3개마다 | **축이 `reaimAngle` 넘게 바뀔 때**(상대 교체·관통) |
| 전환 | vcam 둘(후면↔측면) 사이 컷 | **같은 vcam 순간이동**(`PreviousStateIsValid = false`) |
| 발사 규율 | 예약 → 카메라 큐 종료 후 | **같음** |

전부 `CameraDirector.aimAlongDuelAxis` **하나로 끌 수 있다**(false = 1차 동작). `lockActiveAngle`이 여전히 상위 스위치다.

---

## - [x] Step 11 — 실제 거리 늘리기 (선행 · 씬 값)

이미 반영했다(Research 2차 표). **씬 저장은 사용자가 한다** — 반영 전에 씬에 저장 안 된 변경이 있었다.

- `Cam_ShoulderBack.FollowOffset` (0, 3.5, −7) → **(0, 4, −8)** · `Cam_SideRight` (7, 3.5, 0) → **(8, 4, 0)**
- 두 vcam의 `GroupFraming.DollyRange.x` −2 → **0** — 실제 거리 ≈5.8m → **≈8.9m**
- ⚠ 2차에서 `baseOffset`(축 로컬 '정뒤')은 `Cam_ShoulderBack`의 값 그대로다. 거리 노브는 계속 그 vcam의 `FollowOffset` 하나다.

---

## - [x] Step 12 — `CameraAngleSwitcher`: 활성 조준이 곧 컷이 되게

`Aim(float yaw, bool includeActive)`에서 `includeActive`일 때 **활성 vcam의 감쇠를 한 번 건너뛴다**:

```csharp
if (includeActive && currentIndex >= 0 && cameras[currentIndex] != null)
    cameras[currentIndex].PreviousStateIsValid = false;
```

- 이 한 줄이 "같은 vcam의 순간이동 = 컷"의 실체다(`Select`가 이미 쓰는 줄과 같은 근거).
  없으면 `PositionDamping`(2, 0.4, 2)이 새 자리까지 **2초 가까이 플레이어 주위를 미끄러진다.**
- 기존 호출자(`Awake`의 1회 조준)에도 무해하다 — 그때는 원래 첫 프레임이라 감쇠할 이전 상태가 없다.

`ActiveCamera` 게터 하나(`currentIndex`의 vcam, 없으면 null) — Step 13의 "인트로 중인가" 판정용.

새 메서드는 만들지 않는다. 파라미터 이름만 `playerYaw` → `yaw`로 바꾼다(이제 플레이어 yaw만 들어오는 게 아니다).

---

## - [x] Step 13 — `CameraDirector`: 결투 계획을 듣고 조준을 예약한다

새 필드(`[Header("Framing")]`, `lockActiveAngle` 바로 아래):

```csharp
[Tooltip("고정 각도의 기준을 플레이어 yaw가 아니라 결투 축(플레이어 → 상대)으로 잡는다.\n" +
         "축이 reaimAngle 넘게 바뀔 때만(상대 교체·관통) 컷으로 다시 서고, 결투가 이어지는 동안은 움직이지 않는다.\n" +
         "끄면 1차 동작(활성화 순간의 플레이어 yaw에 고정 + 앵글 교체). lockActiveAngle이 꺼져 있으면 무시된다.")]
[SerializeField] private bool aimAlongDuelAxis = true;

[Tooltip("결투 축에서 카메라를 틀어 서는 각(도). 0이면 정뒤라 플레이어가 상대를 가린다.\n+면 카메라가 플레이어 오른쪽 뒤에 선다.")]
[SerializeField] private float shoulderYaw = 30f;

[Tooltip("결투 축이 이 각(도)보다 크게 바뀌어야 다시 조준한다. 작으면 상대가 바뀔 때마다 컷이 나고, 크면 상대가 옆으로 빠져도 안 따라간다.")]
[SerializeField] private float reaimAngle = 45f;
```

상태(값 타입 셋, 새 개념 없음):

```csharp
private float lockedAxisYaw = float.NaN;   // 지금 카메라가 기준으로 삼는 축. NaN = 아직 계획을 못 받았다
private float pendingAxisYaw;
private bool reaimPending;
```

① **구독**: 이미 `enemyDirector.OnOpponentChanged`를 거는 자리(`:430`/`:451`)에 `OnDuelScheduled`를 같이 건다/푼다.

② **`HandleDuelScheduled(DuelPlan plan)`**:

```csharp
if (!lockActiveAngle || !aimAlongDuelAxis || !framingEnabled) return;
if (plan.Axis.sqrMagnitude < 1e-6f) return;                        // 축이 없다(디버그 경로)

float yaw = Mathf.Atan2(plan.Axis.x, plan.Axis.z) * Mathf.Rad2Deg;
if (!float.IsNaN(lockedAxisYaw) && Mathf.Abs(Mathf.DeltaAngle(lockedAxisYaw, yaw)) <= reaimAngle) return;

pendingAxisYaw = yaw;
lockedAxisYaw = yaw;   // ⚠ 예약 시점에 갱신한다 — 발사 전에 같은 축의 계획이 또 와도 두 번 예약하지 않게

if (!IsGameplayCameraLive) ApplyReaim();   // 화면에 없다(인트로 중) → 즉시. 안 보인다.
else reaimPending = true;                 // 화면에 있다 → 예약, 발사는 ③
```

- `IsGameplayCameraLive` = `brain != null && ReferenceEquals(brain.ActiveVirtualCamera, angleSwitcher.ActiveCamera)`.
  brain이 없으면 false → 즉시 조준(배선이 빠지면 조용히 단순해지는 기존 규율).
- **근거**: 첫 계획은 카운트다운 중에 온다(Research 함정 2). 그때 기다리면 인트로가 엉뚱한 구도로 착지한다.

③ **발사**: `Update`의 `angleSwitcher.Tick(IsCuePlaying)` 바로 아래:

```csharp
if (reaimPending && !IsCuePlaying) ApplyReaim();
```

- `optionAngleSwitch`로 묶지 않는다 — 앵글 교체 옵션이 아니라 고정 각도의 일부다.
- 히트스톱 잠금 중에는 `Update`가 조기 반환하므로 발사가 잠금 뒤로 밀린다(1차와 같은 의도된 동작).

④ **`ApplyReaim()`**: `reaimPending = false; angleSwitcher.Aim(pendingAxisYaw + shoulderYaw, includeActive: true);`

⑤ **`UpdateFraming`의 고정 분기**: `aimAlongDuelAxis`면 `Aim(PlayerYaw)`를 부르지 않는다(비활성 추종이 필요 없다).
   `Awake`의 `Aim(PlayerYaw, includeActive: true)`는 **그대로 둔다** — 첫 계획 전의 폴백이다.

⑥ **`HandleAllCleared`**: `reaimPending = false; lockedAxisYaw = float.NaN;` — 다음 곡의 첫 계획이 무조건 조준하게.

---

## - [x] Step 14 — 씬

- `angleSwitcher.switchEnabled` → **0**. A안에서는 교체가 없다(전환은 Step 13의 재조준이 한다).
  `Setup`은 `switchEnabled`를 안 보므로 0번(`Cam_ShoulderBack`)은 여전히 활성으로 선다. `cameras` 배열은 그대로 둔다(B·C안 복귀 경로).
- 새 필드 셋은 기본값 그대로.

---

## - [x] Step 15 — CLAUDE.md §7-2 · §7-5 갱신

- §7-2: "카메라는 언제나 플레이어 등 뒤에 선다" → **결투 축의 비스듬한 뒤에 서고 축이 크게 바뀔 때만 다시 선다.**
  `GroupFraming.DollyRange.x = 0`의 근거(하한에 붙어 실제 거리가 2m 짧았다) 한 줄.
- §7-5: 앵글 교체는 `aimAlongDuelAxis`가 꺼졌을 때만 쓰인다는 한 줄.

---

## - [ ] Step 16 — 검증

- **상대가 무대 반대편으로 바뀐다**: 대시가 시작될 즈음 **한 번** 컷하고, 새 상대가 플레이어 너머에 보이는가.
- **사슬(같은 상대 연속)**: 컷이 **한 번도** 안 나는가.
- **상대가 바뀌었지만 같은 방향**(축 차이 < 45°): 컷이 안 나는가.
- **관통 패턴**(`DuelCurveEndsBehind`): 다음 패턴 앞에서 반대편으로 컷하는가. 좌우가 뒤집히는 게 거슬리는지 본다(Research 함정 4).
- **컷이 임팩트·쉐이크·펀치·줌 위에 안 떨어지는가**(예약 → 큐 종료 후).
- **컷 직후 미끄러짐이 없는가** — 있으면 Step 12의 `PreviousStateIsValid`가 빠진 것이다.
- **인트로 → 첫 패턴**: 인트로가 이미 축 기준 구도로 착지하는가(엉뚱한 방향에서 한 번 더 컷하면 Step 13 ②의 즉시 조준이 안 된 것이다).
- **기습**: 기습자가 화면 밖에서 와도 우하단 닷지 포인트로 회피가 되는가(Research 함정 5).
- **무대 끝에서 바깥쪽 상대**: confiner 클램프로 화면이 도는 양이 견딜 만한가. 아니면 Step 11의 거리를 줄인다.
- `shoulderYaw` 0 / 30 / 45를 바꿔 보며 가림과 속도감을 본다.
- `aimAlongDuelAxis = false`면 1차 동작, `lockActiveAngle = false`면 예전 추종인지(회귀 0).

---

## 2차 범위 밖

- **기습자를 위한 재조준** — 입력 단서가 화면 고정이라 지금은 필요 없다. 필요해지면 `SetAmbusher`에서 같은 예약을 건다.
- **`Cam_SideRight` 활용(B안)** · **교체 주기 조정(C안)** — A안이 거슬리면 돌아갈 경로로 코드·씬 오브젝트를 남겨 둔다.
- **`TargetOffset.y` 2.5 / 1.5 정리** — 같은 vcam만 쓰므로 컷에서 튀지 않는다. 독립 작업으로 남긴다.
- **1차 Step 9 검증** — 2차가 그 상당 부분을 대체한다. 1차 고유 항목(`lockActiveAngle = false` 회귀)은 Step 16에 흡수했다.

---

# 3차 — 플레이어 후면 180° 유지, 넘친 만큼만 회전 (2차 재조준 교체)

근거: `Research_CameraFixedAngle.md`의 "3차" 절.

## 설계 요약

- 매 프레임 `delta = DeltaAngle(플레이어 yaw, 리그 yaw)`. `|delta| <= rearHalfAngle`(90)이면 **아무것도 안 한다.**
- 넘으면 리그 yaw를 `플레이어 yaw + sign(delta) × rearHalfAngle`(**경계**) 쪽으로 `rearFollowSpeed`(도/초) 상한으로 옮긴다 — 넘친 만큼만 회전. 컷이 아니라 연속이고 vcam 감쇠가 한 번 더 부드럽게 한다.
- **인스펙터 노브**: 각도 `rearHalfAngle`(90, `[Range(0,180)]`) · 속도 `rearFollowSpeed`(360°/s, 0 = 제한 없음) · 시작 각 `shoulderYaw`(30).
- 2차의 결투 축 경로(`OnDuelScheduled` 구독 · `reaimAngle` · 예약/발사 · 인트로 즉시 조준)와 Step 12의 컷 줄은 **지운다**.

---

## - [x] Step 17 — `CameraAngleSwitcher`: 2차 Step 12 되돌리기

- `Aim`의 `PreviousStateIsValid = false` 두 줄과 주석 삭제 — 남기면 매 프레임 클램프가 순간이동이 된다.
- `ActiveCamera` 게터 삭제(쓰는 곳이 없어진다).
- 파라미터 이름 `yaw`는 그대로 둔다(여전히 플레이어 yaw만 들어오는 게 아니다).

---

## - [x] Step 18 — `CameraDirector`: 조건 교체

필드:
- `aimAlongDuelAxis` → **`keepRearHemisphere`**. (2차 코드는 씬에 저장된 적이 없어 `FormerlySerializedAs`가 필요 없다 — 2차 변경을 HEAD로 되돌리고 그 위에 얹었다.) 툴팁: "카메라가 플레이어 후면 rearHalfAngle×2 안에 있으면 앵글을 유지하고, 넘으면 넘친 만큼만 따라 돈다. 끄면 1차 동작."
- `reaimAngle` 삭제 → **`rearHalfAngle = 90f`**(툴팁: "플레이어 등 방향에서 이 각(도)까지는 앵글을 유지한다. 90 = 후면 180°").
- `shoulderYaw`(30) 유지 — **시작 구도에만** 쓰인다.
- **`rearFollowSpeed = 360f`**(`[Min(0)]`, 도/초, 0 = 제한 없음) — 함정 1의 스윙 속도 노브. 범위 밖에서 이리로 올렸다.

상태: `lockedAxisYaw`·`pendingAxisYaw`·`reaimPending` 삭제 → **`rigYaw`** 하나(마지막으로 `Aim`에 넘긴 yaw).

① `Awake`의 첫 조준: `keepRearHemisphere`면 `rigYaw = PlayerYaw + shoulderYaw`로 `Aim(rigYaw, includeActive: true)`. 아니면 예전대로 `Aim(PlayerYaw, includeActive: true)`.

② `UpdateFraming`의 고정 분기(`lockActiveAngle`)에서, 2차의 `!aimAlongDuelAxis` 조건 줄을 이렇게 바꾼다:

```csharp
if (keepRearHemisphere)
{
    float delta = Mathf.DeltaAngle(PlayerYaw, rigYaw);
    if (Mathf.Abs(delta) > rearHalfAngle)
    {
        float boundary = PlayerYaw + Mathf.Sign(delta) * rearHalfAngle;   // 경계에 걸린 채 끌려간다
        rigYaw = rearFollowSpeed > 0f
            ? Mathf.MoveTowardsAngle(rigYaw, boundary, rearFollowSpeed * Time.deltaTime)
            : boundary;
        angleSwitcher.Aim(rigYaw, includeActive: true);
    }
}
else if (brain == null || !brain.IsBlending) angleSwitcher.Aim(PlayerYaw);
```

- `UpdateFraming` 안이라 `framingEnabled`·히트스톱 잠금(조기 반환)을 그대로 따른다 — 잠금 중엔 클램프도 언다.
- 블렌드 가드가 필요 없다 — 교체가 꺼져 있어 블렌드가 없고, 인트로 → 게임플레이 블렌드 중에 목적지가 조금 움직이는 것은 블렌드가 흡수한다.

③ 삭제: `HandleDuelScheduled`, `IsGameplayCameraLive`, `ApplyReaim`, `OnDuelScheduled` 구독/해제 두 줄, `Update`의 재조준 발사 줄, `HandleAllCleared`의 리셋 두 줄.
   (`rigYaw`는 화면의 사실이라 곡이 끝나도 리셋할 것이 없다.)

---

## - [x] Step 19 — CLAUDE.md §7-2 갱신

2차에서 넣은 "결투 축의 비스듬한 뒤" 단락을 교체: **후면 180° 안이면 유지, 넘으면 넘친 만큼만 연속으로 끌려간다(컷 없음)**, 감쇠가 부드럽게 만든다, `PreviousStateIsValid`를 걸면 안 되는 이유 한 줄. §7-5 한 줄은 그대로.

---

## - [ ] Step 20 — 검증

- **사슬(같은 상대)**: 카메라가 안 움직인다.
- **상대가 옆으로 바뀜(< 90°)**: 안 움직이고 상대가 화면에 담기는가.
- **상대가 뒤쪽으로 바뀜**: 플레이어가 도는 만큼 카메라가 경계(정측면)까지 끌려가는가. 스윙 속도는 `rearFollowSpeed`로 맞춘다(함정 1).
- **관통 패턴**: 지나치는 동안 유지 → 돌아서면 끌려간다.
- **임팩트 직전 끌림**(함정 2)이 타격감을 해치는가.
- **구르기**: 경계 밖일 때만 같이 돈다.
- **인트로 → 첫 패턴**: 시작 구도(등 뒤 + `shoulderYaw`)로 착지하는가.
- `keepRearHemisphere = false` → 1차 동작, `lockActiveAngle = false` → 예전 추종(회귀 0).

---

## 3차 범위 밖

- **경계에서 안쪽으로 되돌아오기**(정측면에 오래 머무는 것을 피하기) — 요구가 "넘친 만큼만"이라 넣지 않는다.
- 2차 Step 16 검증 — Step 20으로 대체.

---

# 4차 — 현재 상대와 플레이어를 언제나 담고, 둘의 평균 위치를 화면 중앙에

요구: 카메라 안에 현재 상대와 플레이어가 모두 들어오고, 둘의 평균 위치가 화면 가운데.

## 조사 (플레이 모드 실측, 오토플레이)

변경 전 한 샘플: 거리 11.2m 대시 중 플레이어 뷰포트 x = −0.07, 상대 x = 1.65 — **둘 다 화면 밖**, 중점 (0.74, 0.35).
원인 넷:
1. 상대 가중치가 거리식이라 6m 밖이면 0 — 그룹에서 빠진다(이 샘플은 교체 직후 감쇠 중이라 0.66).
2. `RotationComposer` 데드존(0.1) + 감쇠 0.5, `GroupFraming` 감쇠 1 — 빠른 대시에서 조준이 뒤처진다.
3. 높이 오프셋 둘이 따로 논다 — `TargetOffset.y` 2.5, `CenterOffset` (0, −0.75). 멤버가 발바닥이라 둘로 몸 높이를 흉내 내고 있었다.
4. `DollyRange.y` 4 — 넓게 벌어지면 다 못 물러난다.

그룹은 이미 `GroupAverage`(가중 평균)라 **가중치가 둘 다 1이면 그룹 위치가 곧 평균 위치**다. 고칠 것은 위 넷뿐이다.

## - [x] Step 21 — 상대 가중치를 언제나 1로 (`alwaysFrameOpponent`, 기본 켜짐)

`UpdateFraming`에서 상대 칸만 거리식을 건너뛴다. 교체 순간의 0 리셋 + `weightDamping` 감쇠는 그대로 — 새 상대는 0.35초에 걸쳐 들어온다.
기습자 칸은 계속 거리식.

## - [x] Step 22 — 멤버를 골반 본에 (`frameAtHips`, 기본 켜짐)

`FramingAnchor(root)` = 휴머노이드 `Animator.GetBoneTransform(Hips)`, 없으면 루트. 플레이어는 `SetupFraming`, 상대·기습자는 `SetOpponent`·`SetAmbusher`에서 한 번 찾아 캐시한다.
그룹 평균 = 바운드 중심 = 두 몸의 평균이 되어 높이 오프셋이 전부 0이 된다(원인 3이 통째로 사라진다).

## - [x] Step 23 — 씬 값 (`Cam_ShoulderBack`)

| | 전 | 후 |
|---|---|---|
| `RotationComposer.TargetOffset` | (0, 2.5, 0) | **0** |
| `RotationComposer` 데드존 · 감쇠 | 켜짐 0.1 · 0.5 | **꺼짐 · 0.2** |
| `GroupFraming.CenterOffset` | (0, −0.75) | **0** |
| `GroupFraming.Damping` · `DollyRange` | 1 · (0, 4) | **0.3 · (0, 8)** |
| `CinemachineFollow.FollowOffset` | (0, 4, −8) | **(0, 3.1, −8)** — 그룹이 골반 높이(≈0.9m)로 올라간 만큼 내렸다 |

`Cam_SideRight` 등 나머지 vcam은 교체가 꺼져 있어 손대지 않았다(교체를 다시 켜면 같은 값으로 맞춘다).

## - [x] Step 24 — CLAUDE.md §7-2 갱신

## - [ ] Step 25 — 검증

변경 후 실측(오토플레이): 거리 1.1~4.6m 구간 중점 뷰포트 (0.48~0.52, 0.51~0.56), 플레이어·상대 모두 x 0.29~0.70 안.
- 남은 확인: **10m 넘는 대시 구간**에서 둘 다 담기는가(이번 샘플에는 그 구간이 안 잡혔다). 모자라면 `DollyRange.y`를 더 늘리거나 `FramingSize`를 낮춘다.
- 근접 구간에서 카메라 거리가 5m대로 줄어드는 순간이 있다 — `Deoccluder`(Default 레이어 · PullCameraForward)나 `Confiner3D`(무대 가장자리)가 당기는 것으로 보인다. 거슬리면 따로 본다.
- 골반 본의 애니메이션 흔들림이 화면 흔들림으로 보이는가(감쇠 0.2/0.3이 흡수하는지).

---

# 5차 — 게임플레이 카메라 한 대 (교차 폐기)

결정: **게임플레이 vcam은 `Cam_ShoulderBack` 한 대뿐이다.** 앵글 교체(1차)도, 2차 이후 "되돌아갈 경로"로 남겨 둔 측면 카메라·교체 코드도 폐기한다.

## 조사

- 교체 코드는 `CameraAngleSwitcher.cs`(303줄) 하나에 모여 있고, 바깥에서 부르는 곳은 `CameraDirector` 9곳 —
  `Setup`(`Awake`) · `Aim`(`Awake`·`KeepRearHemisphere`·`UpdateFraming`) · `OnPatternBoundary` · `Reset` · `OnOpponentChanged` · `Tick` · `Arm`(예전 추종 경로의 큰 회전 컷).
  그 밖의 참조는 `CameraShotStep.priority` 툴팁의 문구 하나뿐이다.
- 한 대일 때 스위처가 실제로 하는 일은 둘뿐이다 — ① 0번 vcam 우선순위를 10으로 올리기 ② `FollowOffset = Euler(0, yaw, 0) × baseOffset` 쓰기.
  ①은 씬 값 하나로, ②는 `CameraDirector`에 몇 줄로 대체된다. `CameraDirector`에는 이미 `gameplayCamera`(= `Cam_ShoulderBack`) 참조가 있다(쉐이크 폴백용).
- 씬의 게임플레이 vcam은 넷이다: `Cam_ShoulderBack` · `Cam_SideRight`(스위처 1번) · `Cam_NearLowRight` · `Cam_FarShoulderRight`(아무 데서도 안 쓰임). **우선순위가 전부 0**이다 — 스위처의 `Setup`이 런타임에 0번을 10으로 올려 왔다. 스위처를 지우면 그 일을 씬이 해야 한다.
- `keepRearHemisphere = false`(1차 동작 = 활성화 순간에 고정 + 비활성 vcam만 추종)는 **한 대에서는 뜻이 없다** — 비활성 vcam이 없어 "곡 내내 시작 구도 고정"이 된다. 같은 결과를 `rearHalfAngle = 180`이 낸다.
- `angleBlendDuration` · `cutBetweenAngles`는 이름과 달리 **인트로 마무리**가 읽는다(§7-2, `Brain.DefaultBlend`). 지우지 않는다.

## - [x] Step 26 — `CameraDirector`: 스위처를 걷어내고 조준을 흡수한다

추가(값 둘, 메서드 하나):

```csharp
private CinemachineFollow gameplayFollow;   // Awake에서 gameplayCamera로부터 집는다. 없으면 조준만 비활성
private Vector3 gameplayBaseOffset;         // 씬에 저장된 FollowOffset = 리그 yaw 0일 때의 자리

private void AimRig(float yaw)
{
    rigYaw = yaw;
    if (gameplayFollow != null) gameplayFollow.FollowOffset = Quaternion.Euler(0f, yaw, 0f) * gameplayBaseOffset;
}
```

- `Awake`: `angleSwitcher.Setup()` + 첫 `Aim` → `gameplayFollow`·`gameplayBaseOffset` 캐시 후 `AimRig(PlayerYaw + shoulderYaw)`(조건 `lockActiveAngle && framingEnabled` 그대로).
- `KeepRearHemisphere`의 `angleSwitcher.Aim(rigYaw, includeActive: true)` → `AimRig(...)`.
- `UpdateFraming`: `if (keepRearHemisphere) … else Aim(PlayerYaw)` → `KeepRearHemisphere()` 한 줄.

삭제:
- 필드 `angleSwitcher` · `optionAngleSwitch` · `keepRearHemisphere`(한 대에서 뜻이 없다 — 고정하고 싶으면 `rearHalfAngle = 180`) 와 툴팁.
- 호출 `OnPatternBoundary`(`HandleJudgeTargetBegan`) · `Reset`(`HandleAllCleared`) · `OnOpponentChanged`(`HandleOpponentChanged`) · `Tick`(`Update`) · `Arm`(예전 추종 경로).
  ⚠ `Arm`이 빠져도 예전 추종 경로의 큰 회전 컷(`yawCutThreshold`)은 그대로다 — `Arm`은 그 컷에 "다른 앵글로 착지"를 얹던 것뿐이다.
- 각 호출 주변의 스위처 설명 주석.

남기는 것: `angleBlendDuration` · `cutBetweenAngles`(인트로 마무리), `brain.ActiveVirtualCamera` 기반 쉐이크·펀치 대상 선택(인트로·`CameraShotStep` 샷이 live일 수 있다), `rearHalfAngle` · `rearFollowSpeed` · `shoulderYaw`.

## - [x] Step 27 — `CameraAngleSwitcher.cs`(+ `.meta`) 삭제

`CameraShotStep.priority` 툴팁의 "CameraAngleSwitcher의 activePriority 10" → "게임플레이 vcam(10)".

## - [x] Step 28 — 씬

- `Cam_ShoulderBack.Priority` 0 → **10**(스위처 `activePriority`였던 값 — 인트로 20·`CameraShotStep` 15에 지고 나머지는 이긴다). 인트로 휴지값 −10과의 관계는 그대로.
- `Cam_SideRight` · `Cam_NearLowRight` · `Cam_FarShoulderRight` **GameObject 삭제**. 삭제 전에 씬 안 참조(시퀀스 슬롯·다른 컴포넌트)가 없는지 확인한다.
- `CameraDirector`의 `angleSwitcher` 직렬화 데이터는 필드 삭제로 자동 소멸.

## - [x] Step 29 — CLAUDE.md

- **§7-5 삭제** → 그 자리에 "게임플레이 vcam은 한 대(`Cam_ShoulderBack`, 우선순위 10)"와, 남는 두 규칙만 옮긴다:
  ① `ScreenSpaceOverlay`라 카메라가 움직여도 판정 단서가 안 움직인다(§12-1이 참조), ② 쉐이크·펀치는 live vcam에 건다(인트로·샷 vcam이 live일 수 있다).
  §7-2 · §12-1에서 §7-5를 가리키는 참조를 고친다.
- **§7-2**: `keepRearHemisphere` 언급 삭제("끄면 1차 동작" 문장 → `rearHalfAngle = 180`이면 시작 구도 고정), 조준이 `CameraDirector.AimRig`라는 것.
- 폴더 구조 `Camera/`: 변경 없음(스위처가 원래 안 실려 있다).

## - [ ] Step 30 — 검증

- 컴파일 · 곡 시작 시 `Cam_ShoulderBack`이 live인가(인트로 → 게임플레이 착지).
- 4차 Step 25 · 3차 Step 20 항목이 그대로 성립하는가(후면 180° 유지, 넘친 만큼 회전, 둘 다 담기, 중점 중앙).
- 쉐이크·펀치·줌이 여전히 화면에 나는가.
- `lockActiveAngle = false` → 예전 추종(큰 회전에서 컷만 나고 앵글 착지는 없다).
- `CameraShotStep` 샷(우선순위 15)이 게임플레이를 이기는가.

### 구현 메모

- `IsCuePlaying` · `IsZoomActive`는 스위처 발사 판단 전용이라 같이 지웠다(다른 사용처 0).
- `gameplayCamera` 툴팁·`ResolveEffectCamera` 주석에서 앵글 교체 언급을 걷어냈다.
- 씬: 세 vcam은 부모 `Camera`의 자식 목록 외에 참조가 없었다. `Cam_ShoulderBack` 우선순위 10, 4차 값(`FollowOffset` (0, 3.1, −8) 등) 유지 확인.
- **Step 30 미완** — 이번 세션에서 에디터가 플레이 모드 첫 프레임 뒤 진행하지 않아(MCP 자동 틱 문제로 추정) 실행 검증을 못 했다. 컴파일은 통과. 후면 유지 로직 자체는 4차에서 실측했고, 바뀐 것은 오프셋을 쓰는 통로(`angleSwitcher.Aim` → `AimRig`)뿐이다.

## 5차 범위 밖

- `docs/CameraSwitch/`는 지우지 않는다 — 폐기된 설계의 기록이다.
- `angleBlendDuration` · `cutBetweenAngles` 이름 정리(지금은 인트로 전용) — 별개 작업.

---

# 6차 — 결투 사이를 가리는 적을 감춘다 (OccluderCull)

근거: `Research_CameraFixedAngle.md`의 "6차" 절.

## 설계 요약

- 매 프레임 XZ 삼각형 (카메라, 플레이어, 상대)에서 `radius` 안에 든 적의 렌더러를 **`forceRenderingOff`** 로 끈다.
  `enabled`·레이어·`shadowCastingMode`는 이미 주인이 있어 안 쓴다.
- 판정은 `EnemyRing`의 순수 함수(테스트됨), 대상 선정은 `EnemyDirector`, 카메라 위치는 `CameraDirector`, 렌더러는 `EnemyView`.
- 교전 중 · 기습자 · 죽는 중 · 소멸 중 · 균열 등장 중인 적은 **절대 안 감춘다.**
- 깜빡임은 히스테리시스 + 최소 유지 시간으로 막고, 페이드는 하지 않는다(하드 컷).
- `EnemyDirector.viewCullEnabled` **하나로 끌 수 있다**(false = 지금 동작, 회귀 0).

---

## - [ ] Step 31 — `EnemyRing.InViewWedge` (순수 함수 + 테스트)

```csharp
/// 평면(XZ)에서 점 p가 삼각형 (cam, a, b)로부터 radius 이내인가.
/// a == b면 선분 cam–a로 무너진다(분기 없음 — SeparationPush가 원으로 무너지는 것과 같다).
public static bool InViewWedge(Vector3 p, Vector3 cam, Vector3 a, Vector3 b, float radius)
```

- 삼각형 내부면 true, 아니면 세 변까지의 평면 거리 최솟값 ≤ `radius`. Y는 버린다.
- 테스트(`Enemy/Tests`): 내부 / 상대 너머(선분 a–b 바깥) = false / 변에서 `radius` 경계 / `a == b` 퇴화 / 카메라 뒤.

---

## - [ ] Step 32 — `EnemyView.SetViewCulled` / `CanViewCull`

```csharp
private bool viewCulled;
private float viewCullChangedAt;

/// 렌더러를 forceRenderingOff로 감춘다. ⚠ enabled를 쓰지 않는다 — 시체 교체(Kill)·DissolveSwap이 그 채널의 주인이다.
public void SetViewCulled(bool on)

/// 감춰도 되는 상태인가. ⚠ IsIdle이 아니다 — 이동 중인 적이 정확히 감출 대상이다.
public bool CanViewCull =>
    Current != Phase.Dying && !dissolving && !warping && !hasPendingAttack && !highlighted;

public bool IsViewCulled => viewCulled;
public float ViewCullChangedAt => viewCullChangedAt;
```

- `SetViewCulled`는 값이 같으면 return, 바뀌면 `renderers` 전부에 `forceRenderingOff = on` + `viewCullChangedAt = Time.time`.
- **`ResetState`에서 `SetViewCulled(false)`** — 안 하면 다음 대여가 안 보이는 적으로 나온다.
- `Dissolve`·`MarkDying`·워프 시작·`SetHighlight(true)` 진입점에서 직접 풀지 **않는다** — 그 상태가 되면 다음 `LateUpdate`에 `CanViewCull`이 false라 디렉터가 푼다(진실의 원천 하나). 한 프레임 늦는 것은 안 보인다.

---

## - [ ] Step 33 — `EnemyDirector.TickViewCull`

필드:

```csharp
[Header("View Cull (docs/CameraFixedAngle 6차)")]
[Tooltip("카메라에서 볼 때 플레이어와 교전 중인 적 사이(XZ 쐐기)에 선 적을 감춘다. 끄면 예전 동작.")]
[SerializeField] private bool viewCullEnabled = true;
[Tooltip("쐐기에서 이 거리(m) 안이면 감춘다. 적 몸 반경 + 여유.")]
[SerializeField, Min(0f)] private float viewCullRadius = 0.6f;
[Tooltip("다시 보이게 하려면 radius보다 이만큼 더 멀어져야 한다(히스테리시스).")]
[SerializeField, Min(0f)] private float viewCullReleaseMargin = 0.4f;
[Tooltip("한 번 바뀐 상태는 이 시간(초) 동안 유지한다 — 경계를 배회하는 적의 깜빡임 방지.")]
[SerializeField, Min(0f)] private float viewCullMinHold = 0.25f;

private Vector3? viewOrigin;   // CameraDirector가 넘긴다. null = 판정 안 함(전원 해제)
public void SetViewOrigin(Vector3? origin) => viewOrigin = origin;
```

`LateUpdate`: `TickPendingKills(); TickSeparation();` **뒤에** `TickViewCull();` (이격이 민 자리 기준).

`TickViewCull()`:
- `!viewCullEnabled || viewOrigin == null`이면 `ring`의 감춰진 적을 전부 풀고 return.
- `ring`의 각 `view`:
  - `IsEngaged(view) || !view.CanViewCull` → `SetViewCulled(false)`(즉시, 최소 유지 무시 — 안전한 쪽은 기다리지 않는다), continue.
  - `Time.time - view.ViewCullChangedAt < viewCullMinHold` → continue.
  - `r = view.IsViewCulled ? viewCullRadius + viewCullReleaseMargin : viewCullRadius`.
  - 교전 상대마다(`engagedOpponents`, 비면 `currentOpponent`, 그것도 없으면 플레이어 자신) `EnemyRing.InViewWedge(view 위치, origin, PlayerPosition, 상대 위치, r)` 중 하나라도 참이면 감춘다.
    `TickSeparation`의 통로 루프와 **같은 모양**으로 쓴다.
- 기즈모(기존 `drawGizmos` 블록): 쐐기 삼각형 + 감춰진 적 표시.

---

## - [ ] Step 34 — `CameraDirector`: 카메라 위치를 넘긴다

`Update` 끝(프레이밍 뒤)에 한 줄:

```csharp
enemyDirector?.SetViewOrigin(IsGameplayCameraLive ? brain.OutputCamera.transform.position : (Vector3?)null);
```

- `IsGameplayCameraLive` = `brain != null && brain.OutputCamera != null && ReferenceEquals(brain.ActiveVirtualCamera, gameplayCamera)`.
  인트로·`CameraShotStep`이 live면 null → 전원 해제. brain이 없으면 null → 조용히 비활성(기존 규율).
- 히트스톱 잠금 중에는 `Update`가 조기 반환해 **마지막 위치가 유지된다** — 카메라도 그 자리에 굳어 있으므로 맞는 값이다.
- ⚠ `OnDisable`에서 `SetViewOrigin(null)` — 안 하면 카메라 디렉터가 꺼진 뒤에도 마지막 위치로 계속 감춘다.
- Cinemachine 타입은 이미 있는 `brain` 필드로만 닿는다.

---

## - [ ] Step 35 — CLAUDE.md

- §7-2 프레이밍 끝에 한 단락: 결투 쐐기를 가리는 적은 `forceRenderingOff`로 감춘다 · 채널 선택 근거(`enabled`·레이어·그림자 모드는 주인이 있다) · 감추지 않는 대상 · 게임플레이 vcam live일 때만.
- §11-6 "교전 중인 적은 집합이다"의 `IsEngaged` 사용처 목록에 "시야 감춤 면제" 추가.
- §11-2 이격 단락 끝에 한 줄: 월드 통로는 이격이, **카메라 쐐기는 감춤이** 맡는다(밀지 않는 이유).

---

## - [ ] Step 36 — 검증

- **쐐기 안 배회 적**이 사라지고, 벗어나면 다시 나타나는가. 경계에서 깜빡이지 않는가(`releaseMargin`·`minHold` 조정).
- **상대 너머(뒤)** 에 선 적은 그대로 보이는가.
- **현재 상대**는 쐐기 안이어도 절대 안 사라지는가. 처치 → 다음 상대 교체 순간에 새 상대가 즉시 보이는가.
- **기습**: 쐐기 안에서 튀어나온 기습자가 `Fire()` 순간 보이고 아웃라인이 그려지는가.
- **곡 끝 `DissolveAll`**: 감춰져 있던 적이 소멸 머티리얼로 보였다가 사라지는가(맨몸 팝이 없어야 한다).
- **균열 등장**: 쐐기를 가로지르는 워프 줄이 감춰지지 않는가.
- **풀 재사용**: 감춰진 채 죽거나 반납된 적의 다음 대여가 보이는가.
- **인트로·`CameraShotStep`** 동안 전원 보이는가.
- 히트스톱 · 마무리 실루엣에서 이상 없는가.
- 팝이 거슬리는 정도 — 거슬리면 디더 페이드(범위 밖)로.
- `viewCullEnabled = false` → 지금 동작(회귀 0).

---

## 6차 범위 밖

- **디더/알파 페이드** — 툰 머티리얼이 불투명이라 머티리얼 교체가 필요하고 `DissolveSwap`과 충돌한다. 하드 컷이 거슬리면 별건.
- **그림자만 남기기**(`ShadowsOnly`) — `ApplyBackgroundBudget`과 그림자 모드 주인 정리가 먼저다.
- **시체(`CorpseView`)·절단 조각 감춤** — 다른 오브젝트·풀이고 곧 소멸한다.
- **배경 프롭 가림** — 적만 대상이다. 프롭은 §13이 무대 원 밖에 둔다.
- **화면 공간 정밀 판정** — 평면 쐐기로 부족하면 그때 바운즈 투영으로 바꾼다(`InViewWedge` 시그니처 뒤에 숨는다).
