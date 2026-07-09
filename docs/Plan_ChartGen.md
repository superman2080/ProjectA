# Plan_ChartGen.md — 오디오 분석 기반 채보 자동 생성 시스템 구현

> 기반 문서: `Research_ChartGen.md`
> 가정(피드백 필요):
>
> 1. `PatternData`에서 `inputTime`이 완전히 제거되어 템플릿은 애초에 타이밍을 갖지 않는다(완료). 템플릿 원본(.asset)을 그대로 재사용하며 `Instantiate()` 런타임 복제는 필요 없다.
> 2. **(2026-07-10 4차 변경)** `visibleExposureDuration`도 `PatternData`에서 제거한다. 템플릿(Pattern SO)은 **오직 "어느 노드가 입력 지점인지"(index 시퀀스, 모양)만** 저장하는 순수 위치 데이터가 된다. 노출시간은 더 이상 패턴이 아니라 **곡(SongChart)이 그룹/노드별로 결정**한다 — 같은 모양의 템플릿이라도 어느 곡에서 쓰이느냐에 따라 노출시간(체감 속도/난이도)이 달라질 수 있다.
> 3. 템플릿 폴더는 `Assets/Patterns/Templates` 고정 경로로 스캔한다.
> 4. 채보 재생 검증을 위한 `ChartPlayer`는 이번 Plan 범위에 포함한다 (툴만 만들고 재생 경로가 없으면 결과 검증이 불가능하기 때문).
> 5. 같은 템플릿 Pattern 에셋을 곡 안에서 시간이 겹치지 않게 순차적으로만 재사용한다는 전제(Research 문서 참고)는 `ChartPlayer`에서도 유지한다 — 두 그룹이 동시에 재생 중일 때 같은 템플릿을 공유하면 `nowPattern`/`inputTimes` 상태가 꼬인다.
> 6. **(굽기 모델 확정)** `SongChart`는 온셋(입력 판정 시각), 노출시간(굽는 시점에 정한 값), 생성(스폰) 시각을 모두 저장한다. 계산해서 그때그때 쓰는 게 아니라 에디터 툴("굽는 에디터")이 미리 계산해서 데이터로 굽어(bake) 둔다:
>    - `onsetTimes`: 음원 분석으로 얻은 실제 비트 절대시각 — **입력 판정 기준**. 곡의 성질이라 항상 고정, 어떤 이유로도 자동으로 바뀌지 않는다.
>    - `exposureDurations`: 굽는 에디터에서 노드별로 직접 정하는 노출시간(연출용, 초 단위). 곡이 소유하는 저작 데이터이며, 재굽기 전까지는 자유롭게 편집 가능.
>    - `spawnTimes`: 굽는 시점에 `onsetTimes − ComputeFallDuration(index, exposureDurations[i])`로 계산해서 **미리 구워 저장**한 값. 노드가 실제로 화면에 나타나는(생성되는) 시각.
>    - 굽은 뒤 `exposureDurations` 값을 다시 수정하면, 이미 구운 `spawnTimes`는 새 값과 더 이상 맞지 않게 된다 — 이때는 **자동 갱신이 아니라, 에디터 툴에서 다시 열어 "저장"을 눌러 `spawnTimes`만 다시 계산**한다. `onsetTimes`는 재저장 때도 그대로 유지되므로(오디오 재분석 불필요), 재저장은 즉시 끝난다.
>    - 이 방식을 택한 이유(사용자 결정): 값을 실시간으로 계산하지 않고 에디터에서 명시적으로 구워두면, 나중에 노출시간을 수정했을 때 "저장 버튼 한 번"으로 결과를 확인/수정하기 편하다.
> 7. **(신규 피드백 — 그룹별 개별 교체 가능성)** 곡 하나에 여러 그룹(entry)이 있을 때, 특정 그룹의 패턴(모양)이 마음에 안 들면 **그 그룹만** 다른 템플릿으로 교체할 수 있어야 한다. 전체를 다시 분석/재배정하지 않고, 기존 `SongChart`를 에디터 툴에서 불러와 원하는 그룹만 "재배정"하거나 수동으로 템플릿을 바꾼 뒤 저장하면, 나머지 그룹의 `onsetTimes`/`template`/`exposureDurations`는 전혀 영향받지 않는다. Step 6의 "불러오기 → 그룹별 개별 편집 → 저장" 워크플로우가 이 요구사항을 충족하도록 설계한다.
> 8. **(2026-07-10 5차 변경 — BPM/비트 그리드 기반 밀도로 대체)** 레벨을 초 단위 `gapThreshold`에 직접 선형보간하던 기존 방식은 음악적 근거가 없다는 피드백에 따라 폐기한다. 대신 (Research 문서의 웹 리서치 결과 기반) **BPM/비트 그리드에 온셋을 스냅(quantize)하고, 레벨을 "허용되는 최소 비트 분할 단위(subdivision)"에 매핑**하는 방식으로 대체한다:
>    - `SongChart`에 `bpm`(float)과 `beatOffset`(float, 초 — 첫 박이 시작하는 절대시각)을 저장한다. 둘 다 **굽는 에디터에서 디자이너가 직접 입력**한다 — 자동 템포 감지는 가변 BPM 곡에서 신뢰도가 낮고 우리 프로젝트의 단순 RMS 온셋 감지 수준으로는 구현 난이도가 너무 높아 이번 Plan 범위에서 제외한다(Research 문서 참고).
>    - 4/4 박자를 전제로 `beatDuration = 60f / bpm`(초 단위 4분음표 길이)을 계산한다. 다른 박자(3/4 등)는 이번 Plan 범위 밖(향후 확장 지점으로 남김).
>    - 레벨(1~30) → 허용 비트 분할 단위(subdivisionsPerBeat, 1비트를 몇 등분해서 그리드를 만들지) 매핑을 **구간(tier)으로 나눠 결정**한다(연속적인 선형보간이 아니라, 음악적으로 실재하는 음표 단위끼리 계단식으로 전환): 기본값은 레벨 1~10 → 4분음표(`subdivisionsPerBeat=1`), 레벨 11~20 → 8분음표(`=2`), 레벨 21~30 → 16분음표(`=4`). 구간 경계는 굽는 에디터에서 조정 가능한 설정값으로 노출한다.
>    - `gridInterval = beatDuration / subdivisionsPerBeat` — 이 값이 그리드의 최소 간격(초)이다.
>    - 감지된 원시 온셋 시각을 `snappedTime = beatOffset + Mathf.Round((onsetTime - beatOffset) / gridInterval) * gridInterval`로 그리드에 스냅한다. 같은 그리드 지점으로 스냅된 온셋은 중복 제거한다.
>    - 그룹핑(`OnsetGrouper`)은 더 이상 초 단위 `gapThreshold`가 아니라 **그리드 스텝(정수) 간격**으로 판단한다: 스냅된 두 온셋의 그리드 인덱스 차이가 `maxGroupGapSteps`(기본 1 — 그 사이 빈 그리드 칸이 없어야 같은 그룹) 이하면 같은 그룹, 넘으면 새 그룹. 그리드 해상도(`subdivisionsPerBeat`)가 레벨에 따라 달라지므로, 결과적으로 레벨이 높을수록(그리드가 촘촘할수록) 온셋들이 더 잘게 나뉘어 그룹이 더 조밀해진다 — "레벨이 높을수록 노드 생성 간격이 촘촘해진다"는 요구사항을 음악적으로 의미 있는 방식으로 만족.
>    - 이 매핑 함수(레벨 구간 ↔ subdivision)는 최초 구현 제안이며, 실제로 플레이해보고 체감이 다르면 구간 표만 교체하면 되도록 별도 함수(`LevelToSubdivision(int level)`)로 격리한다.
> 9. **(신규 피드백 — 노드 개수 불일치 시각화)** 채보된 곡은 여러 패턴(그룹)의 집합이다. 온셋 분석으로 감지된 **전체 노드 개수**와, 실제로 그룹들에 배정되어 최종 `SongChartEntry`로 남는 **패턴들의 노드 개수 합**이 다를 수 있다(예: 특정 노드 개수의 템플릿이 없어서 그 그룹이 배정 실패로 스킵된 경우). 이 불일치를 에디터 툴에서 **시각적으로 명확히 표시**한다 — 배정 실패한 그룹을 조용히 버리지 않고 "템플릿 없음" 상태로 목록에 남겨 눈에 띄게 표시하고, 상단에 전체 노드 수 대비 배정된 노드 수 요약(불일치 시 경고 색상)을 보여준다.

## 설계 요약

- **`OnsetDetector`** (순수 C#): `float[] samples, int sampleRate` → RMS 윈도우 에너지 배열 계산 → 직전 대비 급증(임계값 이상)인 윈도우 시각들을 온셋 리스트로 반환.
- **`BeatGrid`** (순수 C#, 신규): `bpm`, `beatOffset`, `subdivisionsPerBeat`로부터 그리드 간격을 계산하고, 원시 온셋 시각을 가장 가까운 그리드 지점으로 스냅(quantize)한다. 레벨(1~30) → `subdivisionsPerBeat` 매핑(`LevelToSubdivision`)도 이 컴포넌트가 담당.
- **`OnsetGrouper`** (순수 C#, 시그니처 변경): 그리드로 스냅된 온셋들의 정수 그리드 인덱스 리스트 + `maxGroupGapSteps`(그리드 스텝 기준 정수 간격) → `List<List<float>>`(그룹별 온셋 시각, 스냅된 값) 반환. 더 이상 초 단위 `gapThreshold`를 쓰지 않는다.
- **`PatternTemplateLibrary`**: `Assets/Patterns/Templates` 하위 `Pattern` 에셋을 `AssetDatabase`로 스캔, `patternDatas.Length` 기준 `Dictionary<int, List<Pattern>>` 캐싱. 개수에 맞는 템플릿이 없으면 `null` 반환(호출부에서 스킵+경고 처리).
- **`Pattern`(템플릿)**: `PatternData[]`는 이제 `index`만 가짐(모양 전용, 순수 위치 데이터). 타이밍(`inputTimes`)은 여전히 재생 직전 `SetInputTimes`로 주입.
- **`SongChart`** (ScriptableObject): `AudioClip song`, `int level`(1~30, 굽는 에디터에서 입력 — 레벨이 높을수록 노드 생성 간격이 촘촘해지도록 굽는 시점의 그룹핑에 반영), `SongChartEntry[] entries`.
  - **`SongChartEntry`** (Serializable): `Pattern template`, `float[] onsetTimes`(입력 판정 절대시각, 고정), `float[] exposureDurations`(굽는 에디터에서 편집하는 노출시간, 곡이 소유), `float[] spawnTimes`(구운 스폰 절대시각, 재굽기 가능).
- **`Pattern`의 타이밍 주입 API**: `SetInputTimes(IReadOnlyList<float>)` / `GetInputTime(int)` — 이미 완료됨(Step 4 참고). 노출시간/스폰 타이밍은 `Pattern`이 전혀 모른다 — `PatternHandler`가 구운 `spawnTimes`를 직접 받아 스케줄링한다.
- **`PatternHandler.SetPattern` 확장**: 구운 `spawnTimes`를 선택적으로 받는 파라미터 추가. 값이 주어지면 그대로 스케줄링에 사용하고, 없으면(디버그/수동 테스트 경로) 별도로 공급되는 노출시간으로 `ComputeFallDuration()`을 그 자리에서 계산.
- **`PatternChartWindow`** (EditorWindow, "굽는 에디터"): AudioClip으로 새로 분석하거나 기존 `SongChart`를 불러와서, 그룹(entry) 단위로 **템플릿 재배정/수동 교체 + 노드별 노출시간 편집**을 자유롭게 하고 "저장" 한 번으로 반영한다. 그룹 하나를 건드려도 다른 그룹의 데이터는 그대로 유지된다(개별 교체 가능성이 핵심 요구사항). 자세한 워크플로우는 Step 6 참고.
- **`ChartPlayer`** (MonoBehaviour): `SongChart` + `AudioSource` + `PatternHandler` 참조. 오디오 재생 후 각 entry의 `spawnTimes[0]`(가장 이른 스폰 시각)이 되면 `onsetTimes`/`spawnTimes`를 그룹 시작 기준 상대시간으로 변환해 `PatternHandler.SetPattern(entry.template, relativeInputTimes, relativeSpawnTimes)` 호출.

### `ComputeFallDuration`의 씬 의존성 처리

`PatternHandler.ComputeFallDuration`은 노출시간뿐 아니라 Point의 실제 화면 위치, `fallSpawnPositionY`, `screenTopY` 등 **씬에 종속된 값**을 함께 사용한다(Research 문서 참고). 굽는 에디터가 정확한 `spawnTimes`를 계산하려면 이 공식을 그대로 재현해야 하므로, `PatternChartWindow`는 씬(또는 지정된 프리팹)의 `PatternHandler` 인스턴스를 참조로 받아 **`PatternHandler`에 노출될 계산 전용 public 메서드**를 호출해서 스폰 시각을 계산한다. 이렇게 하면 런타임과 굽는 시점의 계산식이 항상 동일하게 유지된다.

## 단계별 구현 계획

### Step 1 — 온셋 감지 (`OnsetDetector`)

- [x] `Assets/02. Scripts/ChartGen/OnsetDetector.cs` 생성 (신규 네임스페이스 `ChartGen`)
- [x] `public static float[] Detect(float[] samples, int sampleRate, int windowSize, float rmsThreshold)`
  - 샘플을 `windowSize` 단위로 분할, 각 윈도우 RMS 계산
  - 직전 윈도우 RMS 대비 증가폭이 `rmsThreshold` 이상이면 해당 윈도우 시작 시각을 온셋으로 기록
  - 반환값: 온셋 시각(초) 오름차순 배열 — 이 값은 "노드가 실제로 입력되어야 하는 순간"(판정 기준 시각)이며, 낙하 시작(스폰) 시각이 아니다.
- [x] Unity API 비의존 순수 로직 (스테레오는 채널 평균으로 모노 다운믹스해서 입력받는 전제 — 다운믹스는 호출부인 EditorWindow에서 `AudioClip.GetData` 이후 처리)

### Step 1-1 — 비트 그리드 (`BeatGrid`, 신규)

- [x] `Assets/02. Scripts/ChartGen/BeatGrid.cs` 생성 (순수 C#, `ChartGen` 네임스페이스)
- [x] `public readonly struct BeatGrid { public BeatGrid(float bpm, float beatOffset, int subdivisionsPerBeat); public float GridInterval { get; } public int TimeToGridIndex(float time); public float GridIndexToTime(int index); }`
  - `GridInterval = (60f / bpm) / subdivisionsPerBeat`
  - `TimeToGridIndex(time) = Mathf.RoundToInt((time - beatOffset) / GridInterval)`
  - `GridIndexToTime(index) = beatOffset + index * GridInterval`
- [x] `public static int LevelToSubdivision(int level, int tier1Max = 10, int tier2Max = 20)` — 레벨 구간별 `subdivisionsPerBeat` 반환: `level <= tier1Max` → `1`(4분음표), `level <= tier2Max` → `2`(8분음표), 그 외 → `4`(16분음표). 구간 경계(`tier1Max`, `tier2Max`)는 인자로 받아 굽는 에디터에서 조정 가능
- [x] `public static float[] Snap(IReadOnlyList<float> onsetTimes, BeatGrid grid)` — 각 온셋을 `grid.GridIndexToTime(grid.TimeToGridIndex(onsetTime))`로 스냅하고, 같은 그리드 인덱스로 스냅된 중복 온셋은 제거해서 오름차순 배열로 반환

### Step 2 — 그룹핑 (`OnsetGrouper`, 그리드 기반으로 변경)

- [x] `Assets/02. Scripts/ChartGen/OnsetGrouper.cs` 생성
- [x] `public static List<List<float>> Group(IReadOnlyList<float> snappedOnsetTimes, BeatGrid grid, int maxGroupGapSteps = 1)`
  - 스냅된 온셋들을 그리드 인덱스(`grid.TimeToGridIndex`)로 변환해 순회하며, 직전 온셋과의 그리드 인덱스 차이가 `maxGroupGapSteps` 이하면 같은 그룹에 추가, 초과하면 새 그룹 시작
  - 반환값은 각 그룹의 실제 시각(초) 리스트(그리드 인덱스가 아니라 스냅된 시각) — 이후 파이프라인(온셋 분석 이후 단계)은 시각 값을 그대로 사용
  - 빈 입력이면 빈 리스트 반환
  - 레벨이 높을수록(= `subdivisionsPerBeat`가 커서 `GridInterval`이 작을수록) 같은 원시 온셋이라도 그리드 인덱스 차이가 커지기 쉬워, 결과적으로 더 잘게, 더 촘촘하게 그룹이 나뉜다(가정 8 참고)

### Step 3 — 템플릿 라이브러리 (`PatternTemplateLibrary`)

- [x] `Assets/02. Scripts/ChartGen/Editor/PatternTemplateLibrary.cs` 생성 (Editor 전용 — `AssetDatabase` 의존이라 `Editor` 폴더에 위치)
- [x] `Assets/Patterns/Templates` 폴더가 없으면 자동 생성하지 않고 존재 여부만 확인, 없으면 빈 라이브러리로 동작(경고 로그)
- [x] `AssetDatabase.FindAssets("t:Pattern", new[] { "Assets/Patterns/Templates" })`로 스캔 → 각 Pattern의 `AllData.Count`로 그룹핑해 `Dictionary<int, List<Pattern>>` 구성
- [x] `public Pattern GetRandomTemplate(int nodeCount)` — 해당 개수 리스트에서 랜덤 선택, 없으면 `null`

### Step 4 — `PatternData`/`Pattern` 타이밍 구조 변경 (완료)

- [x] `Assets/02. Scripts/Pattern/Pattern.cs`: `PatternData`에서 `inputTime` 필드 제거
- [x] `Pattern`에 `private float[] inputTimes` 추가 + `public void SetInputTimes(IReadOnlyList<float> times)` 추가
  - 전제: `times.Count == patternDatas.Length` (아니면 `Debug.LogError` 후 아무것도 하지 않고 리턴)
  - `Initialize()` 호출 전에 반드시 먼저 호출해야 함 (순서 의존성)
- [x] `public float GetInputTime(int position) => inputTimes[position];` 추가 — 판정 시각 조회용
- [x] `ExpectedTime` 프로퍼티가 `nowPattern.inputTime` 대신 `inputTimes[index]`를 반환하도록 변경
- [x] `Assets/02. Scripts/UI/PatternHandler.cs` 동반 수정:
  - `SetPattern(Pattern pattern)` → `SetPattern(Pattern pattern, IReadOnlyList<float> inputTimes)`로 시그니처 변경, 내부에서 `pattern.SetInputTimes(inputTimes)` 호출 후 `Initialize()`
  - 낙하 스폰 스케줄링 루프에서 `data.inputTime` → `nowPattern.GetInputTime(i)`로 변경
  - 에디터 디버그 테스트용 `[SerializeField] float[] debugInputTimes` 추가, `DebugSetTestPattern()`이 `SetPattern(debugTestPattern, debugInputTimes)` 호출하도록 변경

### Step 4-1 — `PatternHandler.SetPattern`에 구운 스폰 시각 파라미터 추가 (완료)

- [x] `Assets/02. Scripts/UI/PatternHandler.cs` 수정: `SetPattern(Pattern pattern, IReadOnlyList<float> inputTimes, IReadOnlyList<float> spawnTimes = null, IReadOnlyList<float> exposureDurations = null)`로 시그니처 확장
- [x] 스폰 스케줄링 루프 변경:
  - `spawnTimes`가 주어지면(채보 재생 경로): `float spawnOffset = spawnTimes[i]; float fallDuration = nowPattern.GetInputTime(i) - spawnOffset;` 로 그대로 사용 (구운 값이므로 `ComputeFallDuration` 재계산 불필요, `exposureDurations`도 필요 없음)
  - `spawnTimes`가 `null`이면(디버그/수동 테스트 경로): `exposureDurations[i]`(없으면 상수 기본값)로 `ComputeFallDuration(data.index, exposureDurations[i])`을 그 자리에서 계산
- [x] `ComputeFallDuration`을 굽는 에디터 툴에서도 동일하게 재사용할 수 있도록 `public` 메서드로 노출 (접근제한자만 변경, 로직은 그대로). 추가로 `EnsureLayoutInitialized()`를 신설해 Play 모드 없이 에디터에서 호출해도 `screenTopY`/`canvas` 참조가 정상 계산되도록 보강함(Research 문서의 씬 의존성 이슈 해결)
- [x] `debugInputTimes`만 쓰는 기존 디버그 경로는 그대로 동작해야 함(회귀 없음) — `debugExposureDurations`를 함께 넘기도록 갱신(Step 4-2와 동시 반영)
- [x] **(Play Mode 실측 중 추가 수정)** `inputTimes.Count != pattern.AllData.Count`일 때 스케줄링 루프에 진입하지 않고 에러 로그 후 안전하게 리턴하도록 가드 추가, `spawnTimes`/`exposureDurations` 인덱싱도 길이 체크 후 접근하도록 방어 코드 추가 (기존에는 배열 길이가 안 맞으면 `ArgumentOutOfRangeException`으로 크래시했음 — Play Mode 테스트로 발견)

### Step 4-2 — `PatternData`에서 `visibleExposureDuration` 제거 (완료)

- [x] `Assets/02. Scripts/Pattern/Pattern.cs`: `PatternData`를 `{ [Range(0,8)] public int index; }`만 남도록 수정 — 템플릿은 순수 위치(모양) 데이터가 된다
- [x] `Assets/02. Scripts/UI/PatternHandler.cs`: 지금까지 `data.visibleExposureDuration`을 읽던 모든 지점(스폰 스케줄링 루프)을 Step 4-1에서 추가한 `exposureDurations` 파라미터(디버그 경로) 또는 구운 `spawnTimes`(채보 재생 경로)로 대체
- [x] 에디터 디버그 테스트용 `[SerializeField] private float[] debugExposureDurations;` 추가 (`debugInputTimes`와 짝) — `DebugSetTestPattern()`이 `SetPattern(debugTestPattern, debugInputTimes, null, debugExposureDurations)` 호출하도록 변경
- [x] 기존에 노출시간을 담고 있던 Pattern SO 에셋(`Assets/Patterns/Templates` 등)이 있다면, 필드 제거로 인해 인스펙터 값이 사라짐을 인지 — 이번 Plan 범위에서는 데이터 마이그레이션 스크립트를 만들지 않고, 노출시간은 굽는 에디터에서 새로 입력한다는 전제로 진행(템플릿 자체는 index 시퀀스만 남아 그대로 유효). 기존 `Pattern_Test.asset`에 남아있던 `visibleExposureDuration` YAML 값은 컴파일에 영향 없이 무시됨을 확인

### Step 5 — `SongChart` 데이터 에셋

- [x] `Assets/02. Scripts/ChartGen/SongChart.cs` 생성
  - `[Serializable] public class SongChartEntry { public Pattern template; public float[] onsetTimes; public float[] exposureDurations; public float[] spawnTimes; }`
  - `[CreateAssetMenu(fileName = "SongChart", menuName = "Scriptable Objects/Song Chart")] public class SongChart : ScriptableObject { public AudioClip song; [Range(1, 30)] public int level = 1; public float bpm = 120f; public float beatOffset = 0f; public SongChartEntry[] entries; }`
  - `onsetTimes`: 입력 판정 절대시각(고정, 재굽기해도 안 바뀜) — XML 주석으로 명시
  - `exposureDurations`: 곡이 소유하는 노드별 노출시간(저작 데이터, 굽는 에디터에서 편집) — XML 주석으로 명시
  - `spawnTimes`: 굽는 시점의 계산값 스냅샷(스폰 절대시각) — `exposureDurations`를 바꾸고 재굽기하지 않으면 실제 값과 어긋날 수 있음을 XML 주석으로 명시
  - `level`: 1~30, 굽는 에디터에서 입력. 값 자체는 그냥 저장만 하고, 실제 밀도 반영은 굽는 시점의 `BeatGrid.LevelToSubdivision` 계산에 쓰인다(가정 8 참고)
  - `bpm`/`beatOffset`: 굽는 에디터에서 디자이너가 직접 입력하는 곡의 템포/첫 박 시작 시각(자동 감지 없음, Research 문서 참고). 온셋을 그리드에 스냅하는 데 쓰인다
  - `level`/`bpm`/`beatOffset`을 나중에 인스펙터에서 직접 고쳐도 이미 구운 `onsetTimes`/그룹 구조가 자동으로 바뀌진 않는다(재분석 필요) — XML 주석으로 명시

### Step 6 — 에디터 툴 (`PatternChartWindow`, 굽는 에디터)

핵심 UX 원칙(신규 피드백 반영): **그룹(entry) 하나하나가 언제든 독립적으로 교체 가능해야 한다.** 처음 굽기 전이든, 이미 구운 SongChart를 다시 열었든 동일한 UI로 "이 그룹의 패턴이 마음에 안 들면 다른 걸로 바꾼다"가 가능해야 하므로, "새로 분석"과 "기존 SongChart 불러오기"를 같은 화면/같은 리스트로 다룬다 — 아래는 그 전제로 설계한다.

- [x] `Assets/02. Scripts/ChartGen/Editor/PatternChartWindow.cs` 생성 (`EditorWindow`, 메뉴: `Tools/Pattern Chart Tool`)
- [x] 필드: `AudioClip clip`, `float rmsThreshold`, `int windowSize`, `float defaultExposureDuration`, **`float bpm`**, **`float beatOffset`**(직접 숫자 입력만 지원 — 파형 클릭으로 찍는 기능은 이번 범위 밖), **`int level`**(1~30, 슬라이더), **`int tier1Max = 10, int tier2Max = 20`**, **`int maxGroupGapSteps = 1`**, **`PatternHandler referenceHandler`**(없으면 저장 버튼 비활성화 + 안내 메시지), `SongChart existingChart`
- [x] `level` → `subdivisionsPerBeat` 매핑: `BeatGrid.LevelToSubdivision(level, tier1Max, tier2Max)` 호출 → `new BeatGrid(bpm, beatOffset, subdivisionsPerBeat)` 생성 (가정 8 참고)
- [x] 내부적으로 편집 중인 상태를 `List<ChartEntryDraft>`(private nested class: `template`(null 가능), `onsetTimes`, `exposureDurations`, `spawnTimes` 미리보기)로 통일해서 다룸
- [x] **"분석" 버튼** (새로 만들 때): `clip.GetData()`로 샘플 추출(다채널이면 평균 다운믹스) → `OnsetDetector.Detect` → `BeatGrid.Snap` → `OnsetGrouper.Group` → 그룹별로 `PatternTemplateLibrary.GetRandomTemplate(group.Count)` 호출해 `ChartEntryDraft` 리스트 구성
  - 배정 실패 그룹은 `template = null` 상태로 그대로 목록에 남김(가정 9)
- [x] **"불러오기" 버튼**: `existingChart.entries`를 `ChartEntryDraft` 리스트로 변환, `existingChart.level`/`bpm`/`beatOffset`을 각 필드에 반영
- [x] 그룹별(entry별) 리스트 UI:
  - 배정된 템플릿 이름 + 노드 개수 표시. `template == null`인 항목은 빨간 배경 + "템플릿 없음(N개 노드)"로 강조 표시(가정 9)
  - **"재배정" 버튼**: 같은 노드 개수 템플릿을 라이브러리에서 랜덤 재선택
  - **수동 교체 `ObjectField`**: 노드 개수가 다르면 경고 후 거부
  - **노드별 노출시간 편집 필드**: 값 변경 시 즉시 `spawnTimes` 재계산
  - 각 조작은 해당 그룹에만 영향, 다른 그룹의 데이터는 건드리지 않음
- [x] **노드 개수 불일치 요약 배너**(가정 9): 전체 온셋 노드 수 vs 배정된 노드 수를 표시, 불일치 시 경고색
- [x] `spawnTimes` 계산: `referenceHandler.ComputeFallDuration(node.index, exposureDurations[i])`로 산출, `template == null`인 항목은 제외
- [x] 파형 시각화: `EditorGUI.DrawRect` 기반으로 배정 실패 구간(빨강 블록) + 비트 그리드 세로선 + 온셋 마커(cyan/red) 오버레이 구현
- [x] 슬라이더/입력값 변경은 "분석" 재실행 전까지 기존 draft에 영향 없음(재분석해야 그룹 구조가 다시 만들어짐)
- [x] **"저장" 버튼**: 배정 안 된 draft가 있으면 비활성화 + 경고 문구. `existingChart` 지정 시 덮어쓰기(`Undo.RecordObject` + `EditorUtility.SetDirty`), 미지정 시 `SaveFilePanelInProject` → `AssetDatabase.CreateAsset`로 신규 생성

### Step 7 — 재생 글루 (`ChartPlayer`)

- [x] `Assets/02. Scripts/ChartGen/ChartPlayer.cs` 생성 (`MonoBehaviour`)
- [x] `[SerializeField] SongChart chart; [SerializeField] AudioSource audioSource; [SerializeField] PatternHandler patternHandler;`
- [x] 재생 시작 시 `audioSource.clip = chart.song; audioSource.Play();`, entries를 `spawnTimes[0]`(가장 이른 스폰 시각, `onsetTimes[0]`이 아님 — 노드가 미리 나타나야 하므로) 기준 오름차순으로 정렬해 스케줄 큐 구성
- [x] `Update()`에서 `audioSource.time`이 다음 entry의 `spawnTimes[0]`에 도달하면:
  ```csharp
  float baseTime = entry.spawnTimes[0];
  var relativeInputTimes = entry.onsetTimes.Select(t => t - baseTime).ToArray();
  var relativeSpawnTimes = entry.spawnTimes.Select(t => t - baseTime).ToArray();
  patternHandler.SetPattern(entry.template, relativeInputTimes, relativeSpawnTimes);
  ```
  호출 후 큐에서 제거 (템플릿 복제 없이 원본 에셋을 그대로 넘김, `exposureDurations`는 이미 `spawnTimes`에 반영되어 있으므로 재생 시 전달 불필요)
  - 전제: 같은 템플릿이 겹치는 시간대에 동시 재생되지 않는다 — 그룹은 항상 순차 재생되므로 문제 없음
  - 주의: `PatternHandler.SetPattern`은 내부적으로 `Time.time` 기준 낙하 스폰을 스케줄링(`patternStartTime = Time.time`) — 오디오 시각과 완전히 동기화되지는 않는 기존 한계를 그대로 인지하고 진행(이번 Plan 범위 밖, 필요 시 별도 후속 작업)

### Step 8 — 테스트

- [x] `OnsetDetector`/`OnsetGrouper`에 대한 유닛 테스트 작성 및 **UnityMCP로 실행 확인 완료 (15/15 통과)**: `Assets/02. Scripts/ChartGen/Tests/OnsetDetectorTests.cs`, `OnsetGrouperTests.cs`. 순수 로직 3종(`OnsetDetector`/`BeatGrid`/`OnsetGrouper`)은 `Assets/02. Scripts/ChartGen/Core/`로 분리해 `ChartGen.Core.asmdef`(엔진 참조 없음)로 묶고, `ChartGen.Tests.asmdef`가 이를 참조하도록 구성. 단, Unity Test Framework가 이 테스트들을 EditMode가 아니라 **PlayMode**로 분류함(원인 불명 — asmdef 구성은 Unity 표준 EditMode 템플릿과 동일함에도 그렇게 잡힘) — PlayMode 러너로 실행해 전부 통과 확인함
- [x] **`BeatGrid` 유닛 테스트**: `Assets/02. Scripts/ChartGen/Tests/BeatGridTests.cs` 작성 (역변환, 스냅, `LevelToSubdivision` 구간 검증) — 위와 함께 PlayMode 러너로 실행해 통과 확인
- [ ] 실제 오디오 클립으로 `PatternChartWindow`를 열어 BPM/오프셋을 입력했을 때, 파형 위 비트 그리드 세로선이 실제 곡의 비트와 맞는지 육안 확인 (미실시 — 실제 음원 파일 + GUI 조작 필요, MCP로는 IMGUI 클릭 시뮬레이션이 어려워 남겨둠)
- [x] `ChartPlayer`/`SetPattern`(구운 spawnTimes 경로) 실제 재생 검증: **UnityMCP로 Play Mode에서 직접 확인 완료.** 씬의 실제 `PatternHandler`(`PointBackground`)와 새로 만든 템플릿(`Assets/Patterns/Templates/Pattern_2Node_Diagonal.asset` 등)으로 `SetPattern(template, inputTimes, spawnTimes)`를 호출 → 예외 없이 스폰·낙하·도착까지 전체 라이프사이클이 정상 종료됨(`scheduledSpawns`/`activeFallingNodes` 모두 0으로 정리됨) 확인. **이 과정에서 실제 버그를 발견해 수정함**: 아래 "발견된 버그" 참고
- [x] **재굽기 검증**: `ComputeFallDuration`을 실제 씬 `PatternHandler`로 두 번 호출(노출시간 0.5초 vs 1.0초)해 `spawnTime`만 변하고 `onsetTime`은 그대로 유지됨을 확인(예: onset=[0.5,1.0] 노출0.5→spawn=[-0.459,-0.077], 노출1.0→spawn=[-1.418,-1.154] — onset 불변, spawn만 앞당겨짐)
- [x] **개별 교체 검증**: 서로 다른 두 그룹(entry A: onset=[0.5,1.0], entry B: onset=[3.0,3.5])을 독립적으로 계산 → entry A의 노출시간을 바꿔 재계산해도 entry B의 spawn 값은 완전히 동일하게 유지됨을 확인(그룹 간 데이터 독립성 확인). 단, 굽는 에디터 GUI의 "재배정"/"수동 교체" 버튼 자체의 클릭 동작은 IMGUI라 MCP로 직접 조작하지 못해 미검증 — 데이터 레벨 로직만 확인됨
- [x] 같은 템플릿(모양)을 서로 다른 노출시간으로 사용해도 독립 적용됨을 위 재굽기 검증에서 함께 확인(둘 다 `Pattern_2Node_Diagonal` 템플릿을 공유하지만 서로 다른 결과)
- [x] **레벨 밀도 검증**: 합성 클릭 트랙(0.5초 간격 온셋들)에 대해 `BeatGrid`를 레벨1(4분음표, gridInterval=0.5)과 레벨30(16분음표, gridInterval=0.125)으로 각각 적용 → 동일한 원시 온셋인데도 레벨1에서는 3개 그룹(크기 2,2,1)으로, 레벨30에서는 5개 그룹(전부 크기 1)으로 갈리는 것을 실제 실행으로 확인 — 레벨이 높을수록(그리드가 촘촘할수록) 같은 시간 간격이 "더 멀리 떨어진 것"으로 인식되어 그룹이 더 잘게 나뉨을 검증
- [x] **노드 개수 불일치(템플릿 없음) 검증**: `PatternTemplateLibrary.GetRandomTemplate(5)`(라이브러리에 없는 노드 개수)를 실제로 호출해 `null` 반환 확인, `GetRandomTemplate(2)`는 정상적으로 `Pattern_2Node_Diagonal` 반환 확인. UI의 빨간 배너/강조 표시 자체는 GUI라 미검증(로직만 확인)

### 검증 중 발견된 버그 (수정 완료)
`PatternHandler.SetPattern`에서 `inputTimes`/`exposureDurations`가 노드 개수와 다르면(특히 씬의 `debugInputTimes`/`debugExposureDurations`가 빈 배열인 상태로 3노드 패턴을 재생하려 할 때) `Pattern.SetInputTimes`는 에러를 로그하고 안전하게 리턴하지만, 그 뒤 `PatternHandler.SetPattern`은 검증 없이 그대로 스케줄링 루프에 진입해 `exposureDurations[i]`에서 `ArgumentOutOfRangeException`이 던져지는 실제 크래시가 있었다(Play Mode 테스트 중 재현). `SetPattern` 시작부에 `inputTimes` 개수 검증을 추가해 불일치 시 에러 로그 후 안전하게 리턴하도록 고쳤고, `spawnTimes`/`exposureDurations` 인덱싱도 길이 체크를 추가해 방어적으로 만들었다. 수정 후 Play Mode에서 예외 없이 깨끗한 에러 로그만 남고 정상 진행됨을 재확인함.
- [x] 컴파일 에러 없음 확인, `Tools/Pattern Chart Tool` 메뉴 진입 확인 — **UnityMCP로 실제 확인 완료**: `refresh_unity`(force 컴파일) 후 `read_console`에 에러/경고 0건, `menu-items` 리소스에 `"Tools/Pattern Chart Tool"` 등록 확인

## 진행 상태 표기 규칙

구현 시작 후 각 항목을 완료할 때마다 `- [ ]` → `- [x]`로 갱신하며, 전 단계가 끝날 때까지 중단 없이 진행합니다.

>>> 여기에 피드백을 남겨주세요.
