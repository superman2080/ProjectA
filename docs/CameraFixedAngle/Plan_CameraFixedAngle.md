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
