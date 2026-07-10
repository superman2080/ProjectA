# Plan_PatternSplit.md — 채보 패턴 분할 및 다양 배정 전략

> 기반 문서: `Research_PatternSplit.md`

## 설계 요약

- 그룹 1개 → 패턴 N개 구조로 전환 (청킹)
- `OnsetChunkSplitter`: 라이브러리의 사용 가능한 노드 크기를 라운드로빈 순환하며 청크 크기 시퀀스 생성 (순수 C#)
- `PatternTemplateLibrary`: 라운드로빈 템플릿 배정, `AvailableNodeCounts`/`MaxNodeCount` 노출
- `OnsetGrouper`의 `maxGroupSize`는 제거 — 갭 기반 그룹핑만 담당
- `PatternChartWindow`: `maxGroupSize` UI 제거, 청킹+배정 로직으로 Analyze() 교체

---

## 단계별 구현 계획

### Step 1 — `OnsetGrouper` `maxGroupSize` 제거 (이전 변경 롤백)

- [x] `Assets/02. Scripts/ChartGen/Core/OnsetGrouper.cs`
  - `maxGroupSize` 파라미터 제거, 내부 분할 로직 제거
  - 시그니처를 원래대로 복원: `Group(IReadOnlyList<float>, BeatGrid, int maxGroupGapSteps = 1)`
- [x] `Assets/02. Scripts/ChartGen/Tests/OnsetGrouperTests.cs`
  - `Group_MaxGroupSize_SplitsLargeGroup`, `Group_MaxGroupSize9_Splits10Onsets`, `Group_GapAndMaxGroupSizeBothWork` 3개 테스트 제거

### Step 2 — `OnsetChunkSplitter` 신규 생성 (Core, 순수 C#)

- [x] `Assets/02. Scripts/ChartGen/Core/OnsetChunkSplitter.cs` 생성 (`ChartGen` 네임스페이스)
- [x] `public static List<int> Split(int totalCount, IReadOnlyList<int> availableSizes)`
  - `availableSizes`가 null이거나 비어 있으면 `[totalCount]` 반환 (전체를 하나로)
  - `availableSizes`를 오름차순 정렬하여 내부 사용
  - 순환 인덱스를 0부터 시작해 `availableSizes[i % availableSizes.Count]`로 다음 청크 크기 선택
  - 선택한 크기가 남은 개수보다 크면 **남은 개수 이하의 최대 사용 가능 크기**로 대체 (없으면 남은 개수 그대로 사용)
  - 남은 개수가 0이 될 때까지 반복
  - 반환: 청크 크기의 정수 리스트 (합 = totalCount)

### Step 3 — `OnsetChunkSplitterTests` 신규 작성

- [x] `Assets/02. Scripts/ChartGen/Tests/OnsetChunkSplitterTests.cs` 생성
  - 케이스 1: `availableSizes=[1,2,3,4]`, `totalCount=9` → 합이 9이고 각 크기가 1~4 범위인지 확인
  - 케이스 2: `availableSizes=[2,3,4]`, `totalCount=5` → 합이 5, 마지막 청크가 남은 수에 맞게 조정됨 확인
  - 케이스 3: `availableSizes=[3]`, `totalCount=7` → `[3, 3, 1]` — 단일 크기에서 나머지 처리 확인 (남은 1 < 3이므로 1 그대로)
  - 케이스 4: `totalCount=0` → 빈 리스트 반환
  - 케이스 5: `availableSizes` 빈 경우 → `[totalCount]` 하나짜리 리스트 반환

### Step 4 — `PatternTemplateLibrary` 확장

- [x] `Assets/02. Scripts/ChartGen/Editor/PatternTemplateLibrary.cs` 수정
  - `public IReadOnlyList<int> AvailableNodeCounts` 프로퍼티 추가 (생성자에서 정렬된 키 목록 캐싱)
  - `public int MaxNodeCount` 프로퍼티 추가 (`AvailableNodeCounts.Count > 0 ? AvailableNodeCounts[^1] : 0`)
  - `private Dictionary<int, int> _roundRobinIndex` 필드 추가
  - `public Pattern GetNextTemplate(int nodeCount)` 추가
    - 해당 크기 목록이 없으면 `null`
    - `_roundRobinIndex`의 현재 값으로 `list[idx % list.Count]` 선택 후 인덱스 증가
  - 기존 `GetRandomTemplate`은 유지 (하위호환)

### Step 5 — `PatternChartWindow` Analyze() 재작성

- [x] `Assets/02. Scripts/ChartGen/Editor/PatternChartWindow.cs` 수정
  - 필드 제거: `maxGroupSize` (이전에 추가된 것)
  - `DrawGridFields()`에서 `maxGroupSize` IntField 제거
  - `Analyze()` 변경:
    ```
    groups = OnsetGrouper.Group(snapped, grid, maxGroupGapSteps)  // maxGroupSize 인자 제거
    library = new PatternTemplateLibrary()
    for each group:
        chunkSizes = OnsetChunkSplitter.Split(group.Count, library.AvailableNodeCounts)
        offset = 0
        for each size in chunkSizes:
            chunkOnsets = group.GetRange(offset, size)
            template = library.GetNextTemplate(size)
            draft = { template, onsetTimes = chunkOnsets, exposureDurations = ... }
            RecomputeSpawnTimes(draft)
            drafts.Add(draft)
            offset += size
    ```
  - `OnsetGrouper.Group()` 호출부에서 `maxGroupSize` 인자 제거

---

## 변경 범위 요약

| 파일 | 변경 내용 |
|------|-----------|
| `Core/OnsetGrouper.cs` | `maxGroupSize` 롤백 |
| `Tests/OnsetGrouperTests.cs` | `maxGroupSize` 테스트 3종 제거 |
| `Core/OnsetChunkSplitter.cs` | **신규** |
| `Tests/OnsetChunkSplitterTests.cs` | **신규** |
| `Editor/PatternTemplateLibrary.cs` | `AvailableNodeCounts`, `MaxNodeCount`, `GetNextTemplate` 추가 |
| `Editor/PatternChartWindow.cs` | `maxGroupSize` UI 제거, Analyze() 청킹 로직으로 교체 |

`SongChart`, `ChartPlayer`, `Pattern`, `PatternHandler` 등 **무수정**.

---

>>> 여기에 피드백을 남겨주세요.
