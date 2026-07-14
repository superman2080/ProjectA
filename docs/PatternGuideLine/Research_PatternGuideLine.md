# Research: 패턴 가이드라인 (낙하 중 패턴 경로 가시성)

## 목표 요약
노드가 낙하하는 동안 "이번 패턴이 어떤 모양인지" 미리 보이지 않는 문제를 해결한다.
패턴이 지나갈 Point들을 **순서대로 잇는 반투명 경로 가이드라인**을 노드 낙하 전에 생성하고, 패턴이 끝나면 사라지게 한다.

## 사용자 확정 요구사항
- 가이드라인은 **패턴의 Point 경로**를 잇는다 (낙하 노드끼리 잇는 선이 아님).
- **노드 낙하 전**에 경로를 생성하고, **패턴이 끝나면** 보이지 않게 한다.
- 진행과 무관하게 **경로 전체를 유지**한다 (지나간 구간을 지우지 않음).
- 가이드라인은 **Point를 감싸는 반투명 라인** — 말 그대로 "경로 가이드". **사용자 입력(실제 패턴 라인)에 방해가 되어서는 안 된다.**

---

## 1. 관련 파일

| 파일 | 역할 |
| --- | --- |
| `Assets/02. Scripts/UI/PatternLineRenderer.cs` | `MaskableGraphic` 상속. 좌표 리스트를 받아 선분 메시를 직접 생성 |
| `Assets/02. Scripts/UI/PatternHandler.cs` | 패턴 수명주기 + 라인 좌표 공급 |
| `Assets/02. Scripts/Pattern/Pattern.cs` | `AllData` = 패턴 경로 순서 (`PatternData.index`, 0~8) |
| `Assets/02. Scripts/UI/FallingNodeView.cs` | 낙하 노드 (가이드라인과 직접 관련 없음 — 참고용) |
| `Assets/01. Scenes/DefaultScene.unity` | `Canvas/TileArea/PointBackground` 하위 계층 |

## 2. `PatternLineRenderer` 현재 구현

```csharp
public class PatternLineRenderer : MaskableGraphic
{
    [SerializeField] private float lineWidth = 8f;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color missColor = Color.red;

    public void SetPoints(IReadOnlyList<Vector2> pts)   // 로컬 좌표 리스트
    public void SetLiveEndPoint(Vector2 pt)             // 마우스 커서까지 잇는 임시 끝점
    public void ClearLiveEndPoint()
    public void SetCorrectState(bool allCorrect)        // normalColor / missColor 전환
    public void FadeOutAndClear(float duration)         // 알파 페이드 후 좌표 클리어
}
```

- `Awake()`에서 **`raycastTarget = false`**를 강제한다. 주석에 그 이유가 명시돼 있다: Point들 위에 그려지므로 레이캐스트를 켜면 아래 Point의 포인터 입력을 가로채 드래그가 아예 동작하지 않는다.
  → **가이드라인도 같은 컴포넌트를 쓰면 입력 방해 문제가 구조적으로 자동 해결된다.** (사용자 요구사항 "입력에 방해되면 안 됨"과 직결)
- `OnPopulateMesh()`는 인접 점을 잇는 **사각 quad**만 만든다. 캡(cap)이나 조인트(joint) 처리가 없다.
  → **선이 두꺼워지면 꺾이는 지점(코너) 바깥쪽에 삼각형 틈이 생긴다.** 8px에서는 눈에 안 띄지만, Point(100x100)를 "감싸는" 두께(30~50px)로 키우면 확실히 보인다.
  → 또한 `liveCount < 2`면 아무것도 그리지 않으므로 **1노드 패턴(`Pattern_1Node_(4)` 등 실제 존재)은 가이드가 전혀 안 보인다.**

## 3. `PatternHandler`의 라인 공급 방식 (재사용 가능한 패턴)

```csharp
private void RefreshLinePoints()
{
    var localPoints = new List<Vector2>(connectedIndices.Count);
    foreach (var idx in connectedIndices)
        localPoints.Add(WorldToLocal(lineRenderer.rectTransform, patternPoints[idx].transform.position));
    lineRenderer.SetPoints(localPoints);
}
```

- Point의 월드 좌표 → 대상 렌더러의 로컬 좌표로 변환해 넘긴다. 가이드라인도 **동일한 `WorldToLocal` 헬퍼를 그대로 쓸 수 있다.**
- `WorldToLocal`은 `canvasCamera`를 쓰며 `EnsureLayoutInitialized()`가 이를 준비한다. `SetPattern()`은 이미 `EnsureLayoutInitialized()` 경로를 타므로 추가 초기화가 필요 없다.

## 4. 패턴 수명주기 훅 (이전 작업에서 이미 정리됨)

| 시점 | 위치 | 가이드라인 동작 |
| --- | --- | --- |
| 패턴 시작 | `SetPattern()` — `nowPattern.Initialize()` / `ApplyHitAreas(nowPattern)` 부근 | **경로 생성** (이 시점은 첫 노드 스폰보다 앞선다 → "낙하 전" 요구 충족) |
| 패턴 설정 실패 | `SetPattern()` 검증 실패 분기 | 경로 클리어 |
| 패턴 종료 | `HandlePatternComplete()` — `nowPattern = null` 직후 | **경로 페이드아웃/클리어** |

- **중요**: `SetPattern()`은 `scheduledSpawns`에 스폰 예약만 하고, 실제 스폰은 `Update()`의 `ProcessFallingNodeSpawns()`에서 일어난다. 따라서 `SetPattern()`에서 가이드를 그리면 **어떤 노드보다도 먼저** 경로가 표시된다.
- `EndDrag()` / `TriggerLineFadeOut()`은 **입력 라인**(`lineRenderer`)만 정리한다. 가이드라인을 여기에 엮으면 드래그가 끝날 때마다 가이드가 사라져 요구사항("패턴 끝날 때까지 유지")과 어긋난다.

## 5. 씬 계층과 렌더 순서

```
Canvas/TileArea/PointBackground   (Image, PatternHandler, InputHandler)
├── Point_1 ~ Point_9             (판정 100x100 투명 / 자식 Visual 60x60)
├── FallingNodeParent
└── PatternLine                   (PatternLineRenderer — 실제 입력 라인)
```

- Unity UI는 **계층 순서 = 렌더 순서**(먼저 오는 자식이 아래에 그려짐)다.
- 현재 `PatternLine`은 **마지막 자식** → Point와 낙하 노드 **위**에 그려진다.
- 가이드라인은 "Point를 감싸는 반투명 배경"이어야 하므로 **Point_1보다 앞선 첫 번째 자식**으로 넣어야 한다. 그러면 Point / 낙하 노드 / 입력 라인이 모두 가이드 **위**에 그려져 시각적 방해가 없다.

## 6. 제약 및 결론

1. **새 렌더러 클래스는 필요 없다.** `PatternLineRenderer`를 가이드용 인스턴스로 하나 더 두고, 인스펙터에서 두께/색(반투명)만 다르게 주면 된다. `raycastTarget = false`도 자동 적용되어 입력 방해가 없다.
2. 다만 **두꺼운 선을 위한 코너 조인트 처리는 추가해야 한다** (2절). 옵션 플래그로 넣어 기존 입력 라인(8px)의 렌더 결과는 바뀌지 않게 한다.
3. **1노드 패턴 대응**도 조인트 캡을 그리면 자연스럽게 해결된다(점 하나에 캡만 표시).
4. 가이드 색상/두께는 인스펙터 노출값이므로 튜닝은 코드 수정 없이 가능하다.
