# Research_PatternSplit.md — 채보 패턴 분할 및 다양 배정 전략

## 문제 정의

현재 파이프라인의 두 가지 구조적 문제:

1. **maxGroupSize 하드코딩**: `OnsetGrouper`에 `maxGroupSize=9`를 넣었지만, 실제 라이브러리에 있는 템플릿 최대 노드 수는 **4개**다. 따라서 연속 5개 이상 온셋이 묶이면 여전히 "템플릿 없음"이 발생한다.

2. **단조로운 배정**: 한 그룹의 모든 온셋을 하나의 패턴에 몰아넣는 구조라, 항상 최대 크기 패턴으로만 채워진다. 짧은 패턴(1~3노드)은 사실상 쓰이지 않는다. "다양하고 균일하게 배정"이 불가능한 구조다.

---

## 현재 템플릿 라이브러리 현황

| 경로 | 노드 수 |
|------|---------|
| `Pattern_1Node_Center.asset` | 1 |
| `Pattern_2Node_Diagonal.asset` | 2 |
| `Pattern_3Node_LShape.asset` | 3 |
| `Pattern_Node_LeftL4.asset` | 4 |
| `Pattern_Node_RightL4.asset` | 4 |

→ 사용 가능한 노드 수: **[1, 2, 3, 4]**  
→ 최대 노드 수: **4** (9가 아님)

---

## 관련 파일 분석

### `PatternTemplateLibrary` (`Editor/PatternTemplateLibrary.cs`)

```csharp
Dictionary<int, List<Pattern>> templatesByNodeCount
public Pattern GetRandomTemplate(int nodeCount)  // Random.Range — 중복 가능
```

- **`AvailableNodeCounts` 프로퍼티 없음** — 외부에서 어떤 크기가 있는지 알 수 없음
- **`MaxNodeCount` 없음** — 상한을 자동으로 알 방법 없음
- **라운드로빈 없음** — 같은 크기에 항상 랜덤 배정 → 동일 템플릿 반복 가능

### `OnsetGrouper` (`Core/OnsetGrouper.cs`)

지난 Plan에서 추가된 `maxGroupSize` 파라미터:
- 갭 기반 그룹핑 + 크기 제한을 같은 루프에서 처리
- 그러나 "어떤 크기 패턴이 있는지"와 무관하게 동작 → 라이브러리와 연동 없음
- 청킹 전략(균일 분배)을 담기엔 부적합한 위치

### `PatternChartWindow.Analyze()` (`Editor/PatternChartWindow.cs`)

```
groups = OnsetGrouper.Group(snapped, grid, maxGroupGapSteps, maxGroupSize)
for each group:
    template = library.GetRandomTemplate(group.Count)  // 실패 시 null
    draft = { template, onsetTimes = group }
```

- 그룹 크기 = 패턴 크기라는 1:1 전제가 박혀 있음
- 한 그룹을 여러 패턴으로 쪼개는 로직 없음

---

## 설계 방향

### 핵심 전환

**기존**: 그룹 1개 → 패턴 1개 (1:1)  
**변경**: 그룹 1개 → 패턴 N개 (청킹, 1:N)

그룹 내 온셋을 여러 청크로 나누고, 각 청크에 독립 패턴을 배정한다.

### 청킹 전략: 라운드로빈 순환 (균일 배정)

- 입력: 총 온셋 개수, 사용 가능한 노드 크기 목록 (예: [1, 2, 3, 4])
- 출력: 청크별 크기 목록 (합 = 총 개수)
- 알고리즘:
  - 사용 가능한 크기를 오름차순 정렬 후 순환 (`[1,2,3,4,1,2,3,4,...]`)
  - 현재 순환 크기가 남은 개수보다 크면, 남은 개수 이하의 최대 사용 가능 크기로 대체
  - 남은 개수가 소진될 때까지 반복

예시 (available=[1,2,3,4], total=9):
- 순환: 1→2→3→4→1→... → 청크: [1, 2, 3, 3] (총 9, 마지막 순환값 4 → 남은 3이므로 3으로 대체)

### 템플릿 다양 배정: 라운드로빈 인덱스

- `PatternTemplateLibrary` 내부에 `Dictionary<int, int> _roundRobinIndex` 유지
- `GetNextTemplate(int nodeCount)`: 해당 크기 목록의 다음 인덱스를 순환하며 반환

### `maxGroupSize` 위치 변경

- `OnsetGrouper`에서 `maxGroupSize` 제거 (이전 변경 롤백) → 갭 기반 그룹핑만 담당
- 청킹은 이후 단계(`Analyze()` 내 `OnsetChunkSplitter`)가 전담
- UI에서 `maxGroupSize` 필드도 제거 (라이브러리 MaxNodeCount가 자동 상한)

---

## 영향 범위

| 파일 | 변경 내용 |
|------|-----------|
| `Core/OnsetGrouper.cs` | `maxGroupSize` 파라미터 제거 (롤백) |
| `Tests/OnsetGrouperTests.cs` | `maxGroupSize` 테스트 3종 제거 |
| `Editor/PatternTemplateLibrary.cs` | `AvailableNodeCounts`, `MaxNodeCount` 추가; `GetNextTemplate` 추가 |
| `Core/OnsetChunkSplitter.cs` | **신규** — 순수 C# 청킹 알고리즘 |
| `Tests/OnsetChunkSplitterTests.cs` | **신규** — 청킹 유닛 테스트 |
| `Editor/PatternChartWindow.cs` | `maxGroupSize` 필드/UI 제거; Analyze() 청킹 로직으로 교체 |
| `SongChart`, `ChartPlayer`, `Pattern`, `PatternHandler` | **무수정** |
