# Plan: 패턴 가이드라인 (낙하 중 패턴 경로 가시성) — v3 (외곽선 반영)

근거 문서: [Research_PatternGuideLine.md](Research_PatternGuideLine.md)

## 확정된 설계 결정 (사용자 확인 완료)

| 항목      | 결정 |
| --------- | --- |
| 잇는 대상 | 패턴이 지나갈 **Point 경로**를 `Pattern.AllData` 순서대로 연결 |
| 생성 시점 | **노드 낙하 전** = `SetPattern()` 시점 (스폰은 `Update()`에서 일어나므로 항상 먼저) |
| 소멸 시점 | **패턴 종료 시** (`HandlePatternComplete`) 페이드아웃 |
| 진행 반영 | **경로 전체 유지** (지나간 구간을 지우지 않음) |
| 형태      | 각 Point 위는 **원**, 원과 원 사이는 **선** → 캡슐(⊂⊃) 실루엣 |
| **외곽선** | **캡슐 전체 실루엣을 감싸는 테두리** 한 줄 (Point별 개별 테두리가 아님) |
| 색        | 하얀색 반투명 채움 + 더 진한 반투명 외곽선 (인스펙터 튜닝) |
| 1노드 패턴 | 선 없이 **원 하나**(외곽선 포함)로 Point를 감싼 형태 |
| 레이어    | 입력 라인 / 낙하 노드 / Point보다 **아래** |
| 입력 방해 | 없음 — `PatternLineRenderer.Awake()`가 `raycastTarget = false`를 강제 |
| 구현 방식 | **새 클래스 없이** `PatternLineRenderer` 인스턴스를 하나 더 사용 (두께/색/캡 옵션만 다르게) |

---

## 핵심 설계: 캡슐을 두 번 그린다 (레이어드)

```
[1] 바깥 캡슐  — 반지름 R = r + outlineWidth, 색 = outlineColor   ← 먼저 그림
[2] 안쪽 캡슐  — 반지름 r,                    색 = fillColor      ← 위에 덮음
결과: 삐져나온 테두리 = 캡슐 전체 실루엣을 감싸는 외곽선 한 줄
```

**왜 이 방식이 반투명에서도 얼룩이 없는가**
안쪽 캡슐은 바깥 캡슐에 **완전히 포함**된다. 따라서 내부 영역은 어디나 "외곽선 1겹 + 채움 1겹"으로 **블렌딩 횟수가 균일**하고, 테두리 영역은 "외곽선 1겹"으로 균일하다. 부분적으로만 겹쳐 알파가 두 번 곱해지는 얼룩이 생기지 않는다.

**단, 각 레이어 내부의 겹침은 없어야 한다.**
원과 선분을 그냥 겹쳐 그리면 그 레이어 안에서 알파가 두 번 곱해진다.

> ⚠️ **구현 중 수정됨 (v3 최초안의 오류)**
> 처음에는 "선분을 양 끝에서 반지름만큼 잘라내 원 경계에 접하게 한다"고 설계했으나, **이는 틀렸다.**
> 잘라낸 지점(`A + R*dir`)은 원의 폭이 0이 되는 접점이라, 원호와 quad 끝면 사이에 **렌즈 모양 빈틈**이 남는다.
> 실제 렌더 결과가 캡슐이 아니라 "구슬 + 네모"로 보였다 (스크린샷으로 확인).

**최종 방식 — 정확한 합집합:**
- 선분 = `A` ~ `B` **중심에서 중심까지 온전히** 그린다 (폭 `2R`).
- 원 = **선분에 덮이지 않는 호(arc)만** 부채꼴로 채운다.
  - 점 P에서 인접 점을 향하는 방향 `u`에 대해, 선분 quad는 P 주위 원의 **`u` 기준 ±90° 반원**을 정확히 덮는다.
  - 따라서 각 선분의 경계각(`θu ± 90°`)을 모아 정렬하고, **덮이지 않은 각도 구간만** 호로 채운다.
  - 인접 선분이 없는 고립된 점(1노드 패턴) = 완전한 원.

→ 겹침 0, 빈틈 0. 끝점은 반원, 코너는 바깥쪽 호로 정확히 메워진다.
→ **이 로직을 반지름·색만 바꿔 두 번 호출하면 외곽선이 완성된다.** (`BuildCapsule` 헬퍼 하나로 재사용)

> Unity UI(`MaskableGraphic`)에는 둥근 캡 토글이 없어 원 지오메트리는 직접 생성해야 한다. 3D `LineRenderer`의 `numCapVertices`는 월드 공간 렌더러라 Screen Space Overlay Canvas 아래에 끼워 넣을 수 없어 사용 불가.

---

## 구현 단계

- [x] **Step 1 — `PatternLineRenderer.cs`: 캡슐 + 외곽선 렌더 옵션 추가**
  - 필드 추가:
    ```csharp
    [Header("Capsule Guide (가이드라인용)")]
    [Tooltip("각 점에 원을 그리고 선분을 원 경계까지만 그려 ⊂⊃ 캡슐 실루엣을 만든다. 얇은 입력 라인은 off 유지.")]
    [SerializeField] private bool capsuleMode = false;
    [SerializeField] private int capSegments = 24;      // 원 분할 수
    [SerializeField] private float outlineWidth = 3f;   // 0이면 외곽선 없음
    [SerializeField] private Color outlineColor = new Color(1f, 1f, 1f, 0.6f);
    ```
  - `OnPopulateMesh()` 수정 — **`capsuleMode == false`인 경로는 기존 코드 그대로**(입력 라인 렌더 결과 불변):
    - `capsuleMode == true`일 때:
      - 점이 **0개면 return**, **1개여도 그린다** (원 하나 = 1노드 패턴).
      - `liveEndPoint`는 가이드에서 쓰지 않으므로 캡슐 경로에서는 무시.
      - `float r = lineWidth * 0.5f;`
      - `outlineWidth > 0`이면 먼저 `BuildCapsule(vh, points, r + outlineWidth, outlineColor * alpha)` 호출.
      - 이어서 `BuildCapsule(vh, points, r, segmentColor)` 호출 (segmentColor = 기존 normal/miss 색 + 페이드 알파).
      - **호출 순서가 곧 렌더 순서**다 (나중에 추가된 삼각형이 위에 그려짐).
  - 헬퍼 추가 (실제 구현):
    ```csharp
    private void BuildCapsule(VertexHelper vh, float radius, Color32 color)
    // 선분: 중심~중심 전체를 AddSegmentQuad(폭 2*radius)
    // 원  : 선분에 덮이지 않은 각도 구간만 AddArc (경계각 θu±90°를 정렬 후 미커버 구간 판정)

    private static void AddArc(VertexHelper vh, Vector2 center, float radius, float startAngle, float endAngle, int fullCircleSegments, Color32 color)
    private static void AddIncidentDir(List<Vector2> dirs, Vector2 delta)
    private static float Normalize(float angle)
    ```
  - 기존 public API(`SetPoints` / `SetLiveEndPoint` / `SetCorrectState` / `FadeOutAndClear`)와 `AddSegmentQuad`는 **변경하지 않는다.**
  - 페이드 알파(`currentAlpha`)는 채움과 외곽선 **양쪽에 동일하게** 적용한다 (페이드아웃 시 함께 사라져야 함).

- [x] **Step 2 — `PatternHandler.cs`: 가이드라인 공급**
  - 필드 추가:
    ```csharp
    [Header("Guide Line")]
    [SerializeField] private PatternLineRenderer guideLineRenderer;
    [SerializeField] private float guideFadeDuration = 0.2f;
    ```
  - `private void ShowGuideLine(Pattern pattern)`:
    - `guideLineRenderer == null`이면 return (참조 미연결이어도 안전).
    - `pattern.AllData`를 순회하며 `WorldToLocal(guideLineRenderer.rectTransform, patternPoints[data.index].transform.position)`로 좌표 리스트를 만들어 `SetPoints()` 호출.
  - `private void HideGuideLine()`:
    - `guideLineRenderer.FadeOutAndClear(guideFadeDuration)` 호출.
  - 호출 지점 (`ApplyHitAreas`와 나란히 배치):
    - `SetPattern()` 검증 실패 분기 → `HideGuideLine()`
    - `SetPattern()` 정상 경로, `ApplyHitAreas(nowPattern)` 옆 → `ShowGuideLine(nowPattern)`
    - `HandlePatternComplete()` → `HideGuideLine()`
  - `EndDrag()` / `TriggerLineFadeOut()`은 **건드리지 않는다** (드래그 종료로 가이드가 사라지면 안 됨 — Research 4절).

- [x] **Step 3 — 씬 구성: `PatternGuideLine` 오브젝트 생성**
  - `Canvas/TileArea/PointBackground` 하위에 `PatternGuideLine` 생성, **첫 번째 자식(sibling index 0)**으로 배치.
    → Point / 낙하 노드 / 입력 라인이 모두 그 위에 그려진다 (Research 5절).
  - RectTransform: 부모에 stretch(앵커 0~1, offset 0) — 기존 `PatternLine`과 동일한 좌표계.
  - `PatternLineRenderer` 컴포넌트 추가 후 값 설정:
    - `lineWidth = 80` → 원 지름 80 (자식 `Visual` 60x60을 감싸고, Point 간격 150보다 작아 원끼리 붙지 않음)
    - `normalColor`(채움) = 흰색 알파 **0.2**
    - `outlineColor` = 흰색 알파 **0.6**, `outlineWidth = 3`
    - `capsuleMode = true`, `capSegments = 24`
  - `PatternHandler.guideLineRenderer`에 이 컴포넌트를 연결.

- [x] **Step 4 — 검증** (Play 모드 실행, 결과 기록)
  - 컴파일 에러 0건.
  - `Pattern_4Node_(6, 3, 0, 1)` 주입 → 가이드 좌표 4개 = `(-150,150) (-150,0) (-150,-150) (0,-150)` (해당 Point 위치와 일치) (완료)
  - 이 시점 **활성 낙하 노드 0개** → 가이드가 노드보다 먼저 생성됨 (완료)
  - `Pattern_1Node_(4)` 주입 → 좌표 1개 `(0,0)`, 원 하나로 렌더 (완료)
  - 패턴 완료 → 페이드 코루틴 완료 후 좌표 클리어 (완료)
  - sibling index 0, `raycastTarget = false` → 입력 방해 없음 (완료)
  - 입력 라인(`PatternLine`)은 `capsuleMode = false` 유지 → 기존 렌더 동작 불변 (완료)
  - **게임 뷰 캡처**: 첫 시도에서 원-선분 사이 노치 발견 → 위 "구현 중 수정됨" 참조. 수정 후 재캡처에서 얼룩·빈틈 없는 캡슐 실루엣 확인 (완료)

- [x] **Step 5 — 문서 갱신**
  - `CLAUDE.md`에 가이드라인 개념과 렌더 순서 규칙(가이드 = 첫 자식 / 입력 라인 = 마지막 자식)을 짧게 추가.

---

## 범위 밖 (이번에 하지 않음)

- 가이드라인에 진행 방향 표시(화살표, 순서 번호)
- 지나간 구간의 색 변화/소거 (사용자가 "경로 전체 유지"로 확정)
- 가이드 등장 시 페이드인 애니메이션 (즉시 표시, 소멸만 페이드아웃)
- Point별 개별 테두리 (사용자가 "캡슐 전체 실루엣 외곽선"으로 확정)

---

## 피드백

> 이 아래에 `>>>` 로 피드백을 남겨주세요. 반영 후 Plan을 다시 작성합니다.
