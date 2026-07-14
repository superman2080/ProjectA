# Research: Point 판정 영역 동적 축소 (조작감 개선)

## 목표 요약
현재 재생 중인 패턴에 **포함되지 않은** Point는 판정(레이캐스트) 영역을 축소하여 오입력을 줄이고,
패턴이 끝나면 9개 Point 모두 원래 판정 크기로 복구한다.

---

## 1. 관련 파일

| 파일 | 역할 |
| --- | --- |
| `Assets/02. Scripts/Pattern/Handler/Point.cs` | 개별 포인트. 포인터 이벤트 수신, 판정 색상 표시 |
| `Assets/02. Scripts/UI/PatternHandler.cs` | 9개 Point 소유, 패턴 수명주기 관리, 판정 로직 |
| `Assets/02. Scripts/Pattern/Pattern.cs` | ScriptableObject. `PatternData[]`(인덱스 0~8 나열)과 진행 상태 |
| `Assets/02. Scripts/ChartGen/ChartPlayer.cs` | 채보 재생. 시각에 맞춰 `PatternHandler.SetPattern()` 호출 |
| `Assets/02. Scripts/Input/InputHandler.cs` | 키보드 1~9 → `OnKeyPressed(index)` |
| `Assets/01. Scenes/DefaultScene.unity` | `Canvas/TileArea/PointBackground` 하위 Point_1~9 |

---

## 2. 현재 씬 구조 (이번 세션에서 변경된 최신 상태)

```
Canvas/TileArea/PointBackground   (Image, PatternHandler, InputHandler)  700x700
├── Point_1 ~ Point_9             (RectTransform 100x100, Image, Point)
│   └── Visual                    (Image 60x60, raycastTarget = false)
├── FallingNodeParent
└── PatternLine
```

- Point 간 중심 간격: **150px** (기존 200 → 150으로 축소됨)
- **Point 본체의 Image**: 알파 0(투명), `raycastTarget = true` → **현재 판정 영역은 RectTransform 100x100 전체**
- **자식 `Visual`**: Knob 스프라이트 60x60, `raycastTarget = false` → 시각 표현 전용
- `Point.image` 직렬화 필드는 **자식 `Visual`의 Image**를 가리킨다 (판정 색상 표시용)

> 중요: Point 본체 Image(레이캐스트용)와 `Point.image`(색상 표시용, 자식 Visual)가 **서로 다른 Image**다.
> 판정 영역을 건드리려면 **본체 Image**를 조작해야 하며, `Point.image`를 건드리면 색상만 바뀐다.

---

## 3. 판정 영역이 실제로 결정되는 경로

Unity UI 레이캐스트는 `Graphic.raycastTarget`이 켜진 그래픽의 **RectTransform 사각형**을 대상으로 하며,
`Graphic.raycastPadding`(Vector4: left, bottom, right, top)만큼 **안쪽으로 잘라낸** 영역만 히트로 인정한다.

즉 판정 영역을 줄이는 방법은 두 가지다.

1. **`Image.raycastPadding` 증가** — RectTransform/자식 Visual을 전혀 건드리지 않는다. (채택)
2. RectTransform `sizeDelta` 축소 — Transform을 매 패턴마다 변경하게 되고, 향후 레이아웃/이펙트가 Point 크기에 의존하면 얽힌다. (미채택)

현재 Point의 판정 유효 크기: `100 x 100`, `raycastPadding = (0,0,0,0)`.

## 4. 입력 → 판정 흐름

```
[마우스] Point.OnPointerDown / OnPointerEnter(드래그 중)
            └─ 레이캐스트 통과 필요  ← 여기만 raycastPadding 영향을 받음
[키보드] InputHandler.OnKeyPressed(index)
            └─ PatternHandler.OnKeyboardInput → patternPoints[index].ForceDown()
                                                 ← 레이캐스트를 거치지 않음 (영향 없음)
                    ↓ (공통)
Point.Down() → OnPointDown(index) → PatternHandler.OnPointPressed(index)
                                      ├─ GetPassThroughIndex()로 통과 노드 ForceDown (레이캐스트 무관)
                                      ├─ AppendPointToLine(index)
                                      └─ AddPattern(index)
                                            ├─ index != ExpectedPointIndex → allCorrect=false, Miss 색상
                                            └─ 일치 → 타이밍 판정(Perfect/Good/Miss) → nowPattern.Input()
```

**시사점**
- `raycastPadding`은 **마우스/터치 경로에만** 영향을 준다. 키보드 입력과 통과 노드(`ForceDown`)는 레이캐스트를 거치지 않으므로 영향이 없다.
  → 그래서 "완전 비활성화(raycastTarget off)"가 아니라 "작게 축소"를 택하는 편이 마우스/키보드 동작 일관성 면에서 안전하다. (사용자 확정)
- 축소해도 **의도적으로 정확히 누르면 여전히 눌린다.** 기존 오답 Miss 처리 / 보너스 취소 로직은 그대로 유지된다.

## 5. 패턴 수명주기 (훅 지점)

`PatternHandler`:

| 시점 | 코드 위치 | 상태 |
| --- | --- | --- |
| 패턴 시작 | `SetPattern()` — `nowPattern = pattern; ... Initialize();` (159~167행 부근) | 이 시점에 패턴이 쓰는 인덱스 집합이 확정됨 |
| 패턴 설정 실패 | `SetPattern()` 초반 검증 실패 → `nowPattern = null; return;` (152~157행) | 복구 필요 |
| 패턴 종료 | `HandlePatternComplete()` — `nowPattern = null;` (388~395행) | 복구 지점 |

- 패턴이 사용하는 Point 인덱스 집합은 `pattern.AllData`(`PatternData.index`, 0~8)에서 바로 얻을 수 있다. `Pattern.OnValidate()`가 인덱스 중복을 금지하므로 집합 구성이 단순하다.
- `ChartPlayer.Update()`는 다음 엔트리 시각이 되면 `SetPattern()`을 호출한다. **패턴과 패턴 사이에는 `nowPattern == null`인 대기 구간이 존재**하며, 이때는 전부 원래 크기로 복구한다. (사용자 확정)
- `ChartPlayer.Stop()`은 `PatternHandler`를 건드리지 않는다 → 곡 중단 시 별도 복구 경로는 이번 범위에서 다루지 않는다(패턴 완료 시 복구로 충분).

## 6. 제약 및 주의사항

1. **`Point.image`는 Visual(자식)이다.** 판정 영역용 본체 Image 참조를 Point에 별도 필드로 들고 있어야 한다. `GetComponent<Image>()`로 런타임에 잡을 수도 있지만, 직렬화 필드 + 폴백이 기존 코드 스타일(`image ??= GetComponent<Image>()`)과 일관된다.
2. **자식 Visual은 축소 대상이 아니다.** 시각 크기는 60x60 고정 — 판정만 줄고 보이는 건 그대로여야 한다(사용자 요구). `raycastPadding`은 자식에 영향을 주지 않으므로 자동으로 만족된다.
3. **패딩 값은 Point 크기에 의존한다.** 100x100에서 50x50로 줄이려면 사방 25. 하드코딩 대신 비율(예: 0.5)로 두고 `rect.rect.size`에서 계산하면 Point 크기가 바뀌어도 유지된다.
4. **`EndDrag()`/`ResetPointColors()`는 색상만 리셋**한다. 판정 영역 복구를 여기에 섞으면 드래그가 끝날 때마다 축소가 풀려 의도와 어긋난다 — 복구는 **패턴 종료 시점에만** 걸어야 한다.
5. 씬의 Point_1~9는 프리팹이 아니라 씬 인스턴스다 → 새 직렬화 필드는 9개 오브젝트에 각각 채워야 한다(MCP 스크립트로 일괄 처리 가능).
