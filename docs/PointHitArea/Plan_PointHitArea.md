# Plan: Point 판정 영역 동적 축소 (조작감 개선)

근거 문서: [Research_PointHitArea.md](Research_PointHitArea.md)

## 확정된 설계 결정 (사용자 확인 완료)

| 항목 | 결정 |
| --- | --- |
| 축소 구현 방식 | Point 본체 Image의 **`raycastPadding`** 조정 (RectTransform·자식 Visual 불변) |
| 축소 강도 | **작게 축소** (완전 비활성화 아님). 기본 100x100 → 50x50 (비율 0.5, 인스펙터 조정 가능) |
| 패턴 없는 구간 | 9개 **전부 원래 크기로 복구** |
| 복구 시점 | 패턴 종료(`HandlePatternComplete`) 및 패턴 설정 실패 시 |
| 시각 표현 | 변경 없음 — 자식 `Visual`(60x60)은 축소/복구와 무관 |

## 동작 정의

- `SetPattern(pattern, ...)` 성공 시: `pattern.AllData`의 인덱스 집합에 **속한** Point는 판정 100%, **속하지 않은** Point는 판정 비율 `inactiveHitAreaRatio`(기본 0.5)로 축소.
- `HandlePatternComplete()` 시: 9개 모두 100% 복구.
- 키보드 입력(`ForceDown`)과 통과 노드 자동 인식은 레이캐스트를 거치지 않으므로 **동작 변화 없음**(의도된 동작).
- 축소된 Point도 정확히 누르면 눌리며, 기존 오답 Miss/보너스 취소 로직은 그대로 유지.

---

## 구현 단계

- [x] **Step 1 — `Point.cs`: 판정 영역 제어 API 추가**
  - `[SerializeField] private Image hitGraphic;` 추가 (Point 본체의 투명 Image. 기존 `image` 필드는 자식 Visual = 색상 표시 전용이므로 그대로 둔다).
  - `Start()`/`Reset()`에 `hitGraphic ??= GetComponent<Image>();` 폴백 추가 (기존 `image ??=` 스타일과 동일).
  - `public void SetHitAreaRatio(float ratio)` 추가:
    - `ratio`를 0~1로 clamp.
    - `Vector2 size = ((RectTransform)transform).rect.size;`
    - `float padX = size.x * (1f - ratio) * 0.5f;` / `padY = size.y * (1f - ratio) * 0.5f;`
    - `hitGraphic.raycastPadding = new Vector4(padX, padY, padX, padY);`
    - `hitGraphic == null`이면 무시(기존 `SetJudgementColor`의 null 가드와 동일한 방어).
  - `public void ResetHitArea()` → `SetHitAreaRatio(1f)` (내부적으로 `raycastPadding = Vector4.zero`).

- [x] **Step 2 — `PatternHandler.cs`: 패턴 기준 축소/복구 적용**
  - 인스펙터 필드 추가:
    ```csharp
    [Header("Hit Area")]
    [Range(0.1f, 1f)][SerializeField] private float inactiveHitAreaRatio = 0.5f;
    ```
  - `private void ApplyHitAreas(Pattern pattern)` 추가:
    - `pattern == null` → 9개 모두 `ResetHitArea()` 후 return.
    - `pattern.AllData`를 순회하며 사용 인덱스 집합(`bool[9]`, 지역 재사용 배열)을 만든 뒤, 각 Point에 `SetHitAreaRatio(used ? 1f : inactiveHitAreaRatio)` 호출.
  - 호출 지점:
    - `SetPattern()` 검증 실패 분기(`nowPattern = null; return;`) 직전에 `ApplyHitAreas(null)`.
    - `SetPattern()` 정상 경로에서 `nowPattern.Initialize()` 이후 `ApplyHitAreas(nowPattern)`.
    - `HandlePatternComplete()`에서 `nowPattern = null` 이후 `ApplyHitAreas(null)`.
  - `EndDrag()` / `ResetPointColors()`는 **건드리지 않는다** (드래그 종료로 축소가 풀리면 안 됨 — Research 6-4).
  - `Start()`에서 초기 상태로 `ApplyHitAreas(null)` 호출(씬에 남아 있을 수 있는 패딩 값 정리).

- [x] **Step 3 — 씬 반영: Point_1~9의 `hitGraphic` 참조 연결**
  - MCP `execute_code`로 9개 Point의 직렬화 필드 `hitGraphic`에 **본체 Image**(자식 Visual이 아님)를 지정하고, `raycastPadding`을 `Vector4.zero`로 초기화한 뒤 씬 저장.
  - `PatternHandler`의 `inactiveHitAreaRatio`는 기본값 0.5로 둔다.

- [x] **Step 4 — 검증** (Play 모드에서 실행, 결과 아래 기록)
  - 컴파일 에러 0건 확인 완료.
  - `Pattern_3Node_(0, 4, 8)` 주입 테스트 결과:
    | 단계 | 결과 |
    | --- | --- |
    | [1] 패턴 없음 (Start 직후) | 9개 모두 `padding = (0,0,0,0)` ✅ |
    | [2] `SetPattern` 후 | Point_1 / Point_5 / Point_9(= 인덱스 0,4,8) `padding = 0`, 나머지 6개 `padding = (25,25,25,25)` ✅ |
    | [3] 패턴 완료 후 | 9개 모두 `padding = (0,0,0,0)`으로 복구 ✅ |

- [x] **Step 5 — 문서 갱신**
  - `CLAUDE.md`의 패턴인풋 좌표 설명을 현재 상태(간격 150, Point 100x100 투명 판정 + 자식 Visual 60x60)로 갱신하고, 판정 영역 축소 규칙을 짧게 추가.

---

## 범위 밖 (이번에 하지 않음)

- `ChartPlayer.Stop()` / 곡 중단 시 복구 경로 (패턴 완료 복구로 충분)
- 축소/복구 트윈 애니메이션 (즉시 전환)
- 축소 상태의 시각적 피드백(비활성 Point 흐리게 등)
- `PointBackground`(700x700) 크기 조정

---

## 피드백

> 이 아래에 `>>>` 로 피드백을 남겨주세요. 반영 후 Plan을 다시 작성합니다.
