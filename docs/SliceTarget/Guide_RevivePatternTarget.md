# Guide — 패턴 소유 표적("날아와서 베이는 표적") 부활 레시피

> 2026-08-10 리팩토링에서 `Pattern.sliceTarget` / `Pattern.sliceTargetOffset`을 **삭제**했다.
> 지운 것은 **배선 한 곳**이고 인프라는 전부 살아 있다. 다시 넣고 싶어질 때 이 문서를 본다.

## 왜 지웠나

표적 시스템이 **패턴 소유 → 채보 소유**로 이사한 뒤 남은 껍데기였다.

- `SliceTargetDirector`는 더 이상 `PatternHandler` 이벤트를 구독하지 않는다. 유일한 호출자는 `EnemyDirector`이고, 필드 이름도 **`projectileDirector`**다.
- 실제로 날아오는 것은 `EnemyCue.projectile`(채보 엔트리 소유)이며, 발사 지점은 **쏘는 적의 위치**(`spawnOverride`)다.
- `Pattern.sliceTarget`을 읽는 코드는 **한 줄도 없었고**, `sliceTargetOffset`은 전 패턴 에셋에서 `{x:0, y:0}`이라 한 번도 저작된 적이 없다.

남겨 두면 인스펙터에 "값을 넣어도 아무 일이 일어나지 않는 스위치"가 두 개 남는다 — 그게 삭제 이유다.

## 지우지 않은 것 (전부 그대로 산다)

| 자산 | 역할 |
|---|---|
| `Slice/SliceTargetDirector.cs` | 예약 · 스폰 · 접근 · 임팩트 판정 · 회수 · 조각 예산 · 기즈모 |
| `Slice/SliceTargetView.cs` · `SlicePiece.cs` | 표적 하나의 −Z 이동/절단, 조각 운동(물리 없이 닫힌 식) |
| `Slice/SliceSet.cs` · `Core/MeshSliceBaker.cs` · `Editor/MeshSliceBakerWindow.cs` | 굽기 산출물과 굽기 툴 |
| `Reserve(..., Vector2 offset, ...)`의 `offset` 인자 | 아래 레시피가 그대로 쓴다 (지금은 `Vector2.zero`가 넘어간다) |

## 부활 절차

### 1. `Pattern.cs`에 필드 둘을 되돌린다

```csharp
[Tooltip("이 패턴에서 등장할 베이는 표적. 비우면 표적 없음.")]
[SerializeField] private SliceSpace.SliceSet sliceTarget;

[Tooltip("임팩트 지점 기준 XY 배치. 칼 궤적 밖으로 벌리지 않는다.")]
[SerializeField] private Vector2 sliceTargetOffset;

public SliceSpace.SliceSet SliceTarget => sliceTarget;
public Vector2 SliceTargetOffset => sliceTargetOffset;
```

### 2. `EnemyDirector.BindReservation`의 투사체 블록 **옆에** 한 블록 더 놓는다

```csharp
// 투사체(채보 엔트리 소유) — 쏘는 적 위치에서 날아온다
if (r.cue.projectile != null && projectileDirector != null)
{
    Vector3 origin = opponent != null ? opponent.RingPosition : Center;
    projectileDirector.Reserve(r.token, r.cue.projectile, r.startTime, r.impactTime, Vector2.zero, origin);
}

// 패턴 소유 표적 — spawnOverride를 주지 않으므로 씬의 spawnAnchor에서 날아온다
if (r.template?.SliceTarget != null && projectileDirector != null)
    projectileDirector.Reserve(r.token, r.template.SliceTarget, r.startTime, r.impactTime,
                               r.template.SliceTargetOffset);
```

끝이다. 3줄.

## 왜 이걸로 끝인가 (구조가 이미 열려 있는 근거)

- **토큰당 다중 예약이 이미 성립한다.** `SetOutcome`이 `reservations`·`active` 양쪽에서 같은 토큰을 **전부 순회**해 확정한다. 투사체와 패턴 표적이 한 패턴에 동시에 떠도 같은 성패로 갈린다.
- **`spawnOverride`를 안 주면 `spawnAnchor`에서 날아온다**(`SpawnBase`). 그게 옛 패턴 표적의 경로 그대로다.
- 접근시간 클램프(`approach = min(approachDuration, impactTime − startTime)`), 프리팹 풀, 조각 예산, 기즈모는 호출부와 무관하게 이미 돈다.
- 임팩트 시각은 `r.impactTime` 하나를 공유하므로 §6 정렬(칼날 임팩트 프레임 = 절단 순간)이 자동으로 맞는다.

## 함정 하나

`Reserve`는 **휴머노이드(스킨드) 세트를 거부한다** — 시체 프리팹이 산출물이라 조각 배열이 비어 있기 때문이다.
표적용 `SliceSet`은 **일반 메쉬 모드로 구워야 한다**. 아니면 경고 한 줄만 찍고 무연출이 된다.

## 관련

- `docs/SliceTarget/` — 표적 시스템 설계
- `docs/Refactoring/Research_Refactoring.md` A-4-1 — 삭제 근거 전문
- `CLAUDE.md` §11
