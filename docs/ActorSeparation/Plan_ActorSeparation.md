# Plan — 액터 관통 (ActorSeparation)

근거: `docs/ActorSeparation/Research_ActorSeparation.md`

## 방침 한 줄

**플레이어는 절대 안 비킨다. 자리가 자유로운 적(`IsIdle`)만 캡슐 밖으로 밀어낸다.** 불가침 영역 = 플레이어 원 ∪ 현재 상대 원 ∪ 둘을 잇는 통로. 매 프레임 `LateUpdate`에서 점↔선분 거리 하나만 본다. 물리·콜라이더·새 상태 없음.

**원은 이 캡슐의 특수해다** — 상대가 없으면 두 끝점이 같아져 그대로 원이 된다. 분기 없음.

---

## Step 1 — 순수 계산을 `EnemyRing`에 둔다

- [x] `Assets/02. Scripts/Enemy/Core/EnemyRing.cs`에 static 함수 추가

```csharp
/// <summary>
/// <paramref name="position"/>이 선분 <paramref name="a"/>–<paramref name="b"/>의 <b>캡슐</b>
/// (두 끝 원 + 잇는 통로) 안에 들어와 있으면 밖으로 밀어낼 <b>평면 변위</b>를 돌려준다.
/// 안 겹치면 <c>Vector3.zero</c>.
///
/// <para><b>원은 특수해다</b> — <c>a == b</c>면 그대로 점 기준 원이 된다. 상대가 없는 구간을
/// 위해 분기를 따로 두지 않는다.</para>
///
/// <para><b>⚠ 밀어내는 방향은 선분에 수직이다.</b> 통로 한가운데 선 적을 축 방향으로 밀면
/// 통로를 <b>따라</b> 미끄러질 뿐 밖으로 안 나간다. 수직이라야 옆으로 비켜선다.</para>
///
/// <para><b>⚠ DuelGap.ResolveAxis를 쓰지 않는다</b> — 그 함수는 부호가 뒤집히면 직전 축을 유지한다
/// (커브 관통 전용 규칙). 이격은 언제나 <b>지금 방향</b>으로 밀어야 하므로 그 규칙이 해롭다.</para>
/// </summary>
public static Vector3 SeparationPush(Vector3 position, Vector3 a, Vector3 b, float radius)
{
    // 전부 평면에서 푼다 — 높이는 이격의 관심사가 아니다.
    position.y = a.y = b.y = 0f;

    Vector3 ab = b - a;
    float lenSq = ab.sqrMagnitude;

    // a == b면 t가 0이 되어 점 기준 원으로 자연히 떨어진다.
    float t = lenSq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / lenSq) : 0f;
    Vector3 closest = a + ab * t;

    Vector3 flat = position - closest;
    float d = flat.magnitude;
    if (d >= radius) return Vector3.zero;

    // 선분 위에 정확히 올라선 경우 밀 방향이 없다 — 선분에 수직인 쪽으로 뺀다.
    // ⚠ 플레이어 forward를 쓰면 안 된다: 그건 선분 방향과 거의 같아 통로를 따라 미끄러진다.
    Vector3 axis = d > 1e-4f
        ? flat / d
        : (lenSq > 1e-6f ? Vector3.Cross(ab.normalized, Vector3.up) : Vector3.right);

    return axis * (radius - d);
}
```

- [x] `Core`는 asmdef라 `UnityEngine` 외 의존이 없다 — 그대로 유지

**왜 여기인가**: 이 폴더가 이미 배치·표적 선택의 순수 계산을 들고 있고 `Enemy/Tests`가 그것을 검증한다. 같은 자리에 두면 Step 5의 테스트가 공짜다.

---

## Step 2 — 디렉터에 노브 하나

- [x] `EnemyDirector`에 필드 추가 (기존 무리 노브 근처, `minSpacing` 아래)

```csharp
[Header("Separation")]
[Tooltip("불가침 캡슐의 반경(m). 플레이어 원 · 현재 상대 원 · 둘을 잇는 통로를 이 두께로 비운다.\n⚠ standoffDistance(3.5)보다 한참 작아야 한다 — 크면 배회 슬롯과 상시 싸워 적이 계속 밀린다.\n0이면 이격 끄기.")]
[SerializeField] private float separationRadius = 1.0f;
```

- [x] `0`이면 기능 전체가 조용히 꺼진다 — 배선 누락 시 해당 층만 죽는 기존 규율과 같은 결
- [x] **반경은 하나로 시작한다.** 몸 겹침(≈1.0m)과 통로 여유(≈1.2~1.5m)의 자연스러운 크기가 다르지만, 캡슐 하나가 둘을 다 덮으므로 노브를 나눌 근거가 아직 없다. 플레이 검증에서 통로가 좁아 보이면 그때 `laneRadius`를 가른다

---

## Step 3 — `LateUpdate`에 이격 틱

- [x] `EnemyDirector.LateUpdate`(현재 `TickPendingKills`만 있음)에 `TickSeparation()` 추가

```csharp
void LateUpdate()
{
    TickPendingKills();
    TickSeparation();   // ⚠ 모든 보간이 끝난 뒤여야 한다(아래 주석)
}
```

```csharp
/// <summary>
/// <b>불가침 캡슐</b>(플레이어 원 · 현재 상대 원 · 둘을 잇는 통로) 안에 들어온
/// <b>자유로운</b> 적을 밖으로 민다.
///
/// <para><b>⚠ LateUpdate여야 한다.</b> Update에 두면 EnemyView·PlayerCombatMover의 보간과
/// 스크립트 실행 순서가 보장되지 않아 밀어낸 값이 같은 프레임 lerp에 덮인다.</para>
///
/// <para><b>⚠ 미는 것은 적뿐이다.</b> 플레이어 위치는 도착 시각과 거리 커브가 소유한다 —
/// 밀면 저작한 임팩트 간격이 어긋나 칼이 빗나간다.</para>
///
/// <para><b>⚠ 현재 상대는 캡슐의 <b>끝점</b>이지 위반자가 아니다.</b> 자기 영역에 자기가 걸릴 수 없다.
/// 게다가 거리 커브의 음수 구간은 <b>관통이 의도</b>다(§11-9).</para>
///
/// <para>상대가 없으면 두 끝점이 같아져 <b>그대로 원</b>이 된다 — 분기를 두지 않는다.</para>
/// </summary>
private void TickSeparation()
{
    // 통로의 반대편 끝. 상대가 없으면 플레이어 자신 = 캡슐이 원으로 무너진다.
    // ⚠ Destination이 아니라 transform.position이다 — 통로는 '지금 화면에 있는 선'이다.
    Vector3 player = PlayerPosition;
    PushOut(player, currentOpponent != null ? currentOpponent.transform.position : player);
}

/// <summary>
/// 선분 <paramref name="a"/>–<paramref name="b"/>의 캡슐에서 자유로운 적을 밀어낸다.
/// <b>이 루프가 이격의 유일한 구현이다</b> — 매 프레임 통로(<see cref="TickSeparation"/>)와
/// 일회성 자리 비우기(<see cref="ClearSpot"/>)가 같은 몸통을 쓴다.
/// </summary>
private void PushOut(Vector3 a, Vector3 b)
{
    if (separationRadius <= 0f) return;

    for (int i = 0; i < ring.Count; i++)
    {
        var view = ring[i];
        if (view == null || view == currentOpponent) continue;

        // IsIdle 하나로 사망중·공격예약·리액션·이동중이 전부 걸린다. 새 판정을 만들지 않는다.
        if (!view.IsIdle) continue;

        Vector3 push = EnemyRing.SeparationPush(view.transform.position, a, b, separationRadius);
        if (push == Vector3.zero) continue;

        view.transform.position = ClampToStage(view.transform.position + push);  // Step 4
    }
}
```

- [x] **통로 길이가 대시 중에 길어진다는 것을 확인**한다. 배정 직후에는 플레이어–상대 거리가 창에 비례해 최대 8m까지 벌어지고(§11-2), 그 구간 전체가 통로다. 이것이 의도다 — 대시 경로를 미리 비우는 것이 이 설계의 목적

- [x] 밀린 값이 **살아남는 이유**를 주석에 남긴다 — idle 적은 `TickWander`의 증분 경로(`EnemyView.cs:478`)나 정지 상태라 아무도 위치를 대입하지 않는다

---

## Step 4 — 무대 밖으로 밀리는 것만 막는다

- [x] `push` 적용 뒤 결과가 `Center` 기준 `stageRadius`를 벗어나면 원 안으로 클램프. **`PushOut` 안에 접어 넣었다** — 밀어내는 곳이 하나뿐이라 클램프도 하나면 된다

```csharp
private Vector3 ClampToStage(Vector3 position)
{
    Vector3 fromCenter = Vector3.ProjectOnPlane(position - Center, Vector3.up);
    if (fromCenter.magnitude <= stageRadius) return position;

    Vector3 clamped = Center + fromCenter.normalized * stageRadius;
    clamped.y = position.y;
    return clamped;
}
```

**왜 이것만**: 무대 밖으로 나간 적은 `TakeTargetForWindow`의 표적 후보 판정과 카메라 프레이밍을 동시에 흔든다. 나머지 표류(집결지에서 조금 벗어남)는 다음 배회·재배치가 흡수하므로 안 막는다.

---

## Step 5 — 회피 구르기 착지점 비우기

**메우는 구멍**: 구르기 중심은 현재 상대라 착지점은 통로 **밖** 원주 위다(`DodgeDirector.RollAway:575`). 거기 서 있던 적은 캡슐에 안 걸려 안 밀리고, 플레이어가 그 위에 착지한다. `ScoreSpot`(`:620`)이 좌/우 중 빈 쪽을 고르지만 **점수일 뿐 보장이 아니다.**

- [x] `EnemyDirector`에 일회성 진입점 추가 — **새 계산 없이 Step 3의 `PushOut`을 그대로 부른다**

```csharp
/// <summary>
/// <paramref name="spot"/> 주위를 <b>한 번</b> 비운다. 캡슐이 점으로 무너진 특수해다(<c>a == b</c>).
///
/// <para><b>왜 착지 시점이 아니라 지금인가</b>: 구르기는 <c>move</c>초에 걸쳐 원호를 돈다.
/// 출발할 때 비우면 <b>플레이어가 도착하기 전에</b> 적이 비켜서 있다 —
/// 도착 후에 밀면 겹친 프레임이 이미 화면에 나온 뒤다.</para>
///
/// <para><b>왜 한 번으로 충분한가</b>: 밀린 적은 idle이라 아무도 그 위치를 대입하지 않는다(Step 3과 같은 근거).</para>
/// </summary>
public void ClearSpot(Vector3 spot) => PushOut(spot, spot);
```

- [x] `DodgeDirector.RollAway`에서 착지점이 확정된 직후(`mover.RollArc` 호출 옆) 한 줄

```csharp
enemyDirector.ClearSpot(end);
```

- [x] **⚠ 현재 상대는 여기서도 제외된다** — `PushOut`이 이미 거른다. 구르기 중심이 곧 상대이고 착지점은 그로부터 `rollRadius`(≈1~1.5m)라, 안 걸렀으면 `separationRadius` 1.0m 안에 들어와 **상대가 밀려 결투 기하가 깨진다.** 몸통을 공유하는 것이 이 함정을 공짜로 막는다
- [x] **기습자에게는 예외 처리가 필요 없다** — 이 시점에 `hasPendingAttack`이 서 있어 `IsIdle`이 false다(`DodgeDirector.Fire:485`의 `AssignAttack`). 자동 제외

---

## Step 6 — 검증 (`Enemy/Tests`)

- [x] `SeparationPush` 유닛테스트 7건 추가
  - 캡슐 밖 → `Vector3.zero`
  - 끝점 원 안 → 밀어낸 뒤 그 끝점까지 거리가 정확히 `radius`
  - **통로 옆구리 안**(선분 중간에 수직으로 가까움) → 밀어낸 뒤 **선분까지** 거리가 `radius`
  - **`a == b`(상대 없음)** → 점 기준 원과 결과가 같다 (원이 특수해임의 증거)
  - **선분 연장선 위, 끝점 밖** → `Vector3.zero` (통로가 무한 직선이 아니라 선분임의 증거)
  - 선분 위에 정확히 올라섬(d=0) → 변위가 **선분에 수직**(`Dot(push, ab) ≈ 0`), 길이 `radius`
  - y 성분 → 언제나 0 (높이를 안 건드린다)

- [ ] 플레이 검증 (에디터) — **미실행. Unity 에디터에서 직접 확인 필요**
  - 무대 횡단 대시 중 배회 무리를 지날 때 적이 갈라지는가
  - **통로에 서 있던 적이 옆으로 비키는가** (앞뒤로 미끄러지면 폴백 축이 잘못 걸린 것)
  - **임팩트 순간 플레이어–상대 사이에 제3의 몸이 없는가**
  - `duelDistanceCurve`가 음수인 패턴에서 **관통이 그대로 남아 있는가** (← 상대 제외가 제대로 걸렸는지의 유일한 증거)
  - **회피 구르기 착지점에 적이 서 있으면 구르기가 시작될 때 비켜서는가** (Step 5)
  - **구르기 도중 현재 상대가 안 밀리는가** — 밀리면 결투 간격이 깨져 다음 칼이 빗나간다
  - **기습자가 사전 접근 중에 안 밀리는가** — `"이동 중"`으로 제외돼야 한다
  - 적이 무대 밖으로 나가지 않는가

- [x] 기즈모 (`EnemyDirector.Gizmos.cs`, `drawGizmos` 켜졌을 때) — 캡슐 윤곽을 그린다. 통로 이격은 **눈으로 봐야 튜닝된다**

---

## Step 7 — 문서

- [x] `CLAUDE.md` §11-2에 한 문단 추가 — "불가침 영역은 원이 아니라 **캡슐**(플레이어·상대·통로) / 이격은 적만 한다 / 현재 상대는 끝점이라 제외(관통이 의도) / 밀어냄은 선분에 수직 / LateUpdate 근거"
- [x] `CLAUDE.md` §11-8에 한 줄 — "회피 착지점은 구르기 **시작 시각**에 `ClearSpot`으로 비운다(캡슐이 점으로 무너진 특수해). 기습자는 `hasPendingAttack`이라 이격에서 자동 제외"
- [x] 이 Plan의 체크박스 갱신

---

## 범위 밖 (의도적으로 안 한다)

| 항목 | 왜 |
|---|---|
| 적↔적 이격 | `minSpacing` 2.5m 배치 + 궤도 슬롯 균등 배분이 이미 잡는다. 실측 겹침이 드묾 |
| 이동 중인 적의 회피 | `Destination`이 결투 계획의 입력이라 밀면 플레이어가 밀린 자리로 달린다(§11-1) |
| 밀어냄의 감쇠·스무딩 | 반경이 1m라 변위가 프레임당 수 cm. 눈에 띄면 그때 붙인다 |
| **이동 궤적(스윕) 캡슐** | 터널링 방지용인데 `cruiseSpeed` 4.5m/s ÷ 60fps = 프레임당 0.075m다. 반경 1m의 1/13이라 한 프레임에 캡슐을 건너뛸 수 없다(Research §5-1) |
| **적↔적 통로** | 적끼리의 통로는 화면에 아무 의미가 없다. 통로가 지키는 것은 **칼이 지나가는 선** 하나뿐 |
| 통로 전용 반경 분리 | 캡슐 하나가 몸 겹침과 통로를 다 덮는다. 좁아 보이면 그때 가른다(Step 2) |
| 콜라이더·Rigidbody | Research §2 — 덮어쓰기 + 도착 시각 계약 + 커브 관통, 셋 다 위배 |
