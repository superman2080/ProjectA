# Research_ChartGen.md — 오디오 분석 기반 채보(챠트) 자동 생성 시스템

## 목적
음원(AudioClip)의 파형을 분석해 진폭(RMS)이 급변하는 지점("온셋")을 노드 후보로 뽑고, 온셋들을 시간 간격(gap)으로 묶어 그룹(=하나의 Pattern)을 만든 뒤, 기존 프로젝트에 있는 Pattern SO 템플릿 중 노드 개수가 일치하는 것을 랜덤 배정한다. 이 배정 결과(어떤 템플릿을, 곡의 몇 초 지점에서 실제로 어떤 타이밍으로 쓸지)를 하나의 SongChart 에셋으로 저장하고, 재생 시 기존 `PatternHandler`/`Pattern` 파이프라인을 그대로 태워 곡 전체를 채보 재생할 수 있게 한다.

이 작업은 **에디터 전용 오프라인 툴**이다. 런타임 중 실시간 분석은 하지 않는다.

## 기존 코드 분석

### `Pattern.cs` (`Assets/02. Scripts/Pattern/Pattern.cs`) — 2026-07-10 구조 변경 반영 (4차: 노출시간도 제거)
- `ScriptableObject`. `PatternData[] patternDatas`를 직렬화 필드로 가짐. `PatternData`는 이제 **`{ index(0~8) }`만 가진다 — `inputTime`과 `visibleExposureDuration` 모두 템플릿에서 제거되었다.** 템플릿(Pattern SO)은 오직 "모양(index 시퀀스, 즉 어느 노드가 입력 지점인지)"만 정의하는 순수 위치 데이터가 된다. 타이밍(입력 시각)과 노출시간(연출) 둘 다 곡(Song) 쪽에서 결정한다.
- 대신 `Pattern`은 재생 직전 외부에서 주입받는 `private float[] inputTimes`를 갖는다. `SetInputTimes(IReadOnlyList<float> times)`로 주입하며, 개수가 `patternDatas.Length`와 다르면 에러 로그 후 무시한다. `ExpectedTime`은 `inputTimes[index]`를 반환하고, `GetInputTime(int position)`으로 전체 노드의 입력 시각에 접근할 수 있다(낙하 스폰 스케줄링에 필요).
- `Initialize()`는 `SetInputTimes` 호출 **이후**에 호출해야 한다(순서 의존성). `index=0`, `nowPattern=patternDatas[0]`으로 리셋 — 에셋 자체에 재생 상태(`index`, `nowPattern`, `inputTimes`)가 들어있는 구조는 이전과 동일하게 유지된다.
- `Input()` → `OnInput` 이벤트 발행 후 `Next()`로 진행, 마지막이면 `OnExit` 발행.
- `GetNodeType(position)`: 0=Start, 마지막=End, 나머지=Progress.
- **템플릿 재사용 관점에서 변경점**: 이전에는 "템플릿의 `inputTime`을 실제 온셋 시각으로 치환"하는 방식(런타임 복제 후 덮어쓰기)이 필요했지만, 이제 템플릿에는 애초에 `inputTime`이 없으므로 **덮어쓸 필요가 없다.** 같은 템플릿 에셋을 여러 그룹에서 그대로 재사용해도(복제 없이) `SetInputTimes`로 그때그때 다른 타이밍을 주입하면 된다. 다만 `Pattern`이 `index`/`nowPattern`/`inputTimes` 같은 재생 상태를 인스턴스 필드로 갖는 구조이므로, **동시에(겹치는 시간 구간에) 같은 템플릿을 두 번 이상 재생하면 상태가 꼬인다** — `PatternHandler`가 한 번에 하나의 `nowPattern`만 순차 재생하는 현재 구조에서는 문제 없지만, 이 전제가 이번 채보 재생(`ChartPlayer`)에도 그대로 유지되어야 한다.

### `PatternHandler.cs` (`Assets/02. Scripts/UI/PatternHandler.cs`) — 2026-07-10 구조 변경 반영
- `SetPattern(Pattern pattern, IReadOnlyList<float> inputTimes)`: 이전 패턴 정리 후 `nowPattern = pattern; nowPattern.SetInputTimes(inputTimes); nowPattern.Initialize();` 하고, `patternStartTime = Time.time`(월드 시간 기준)로 낙하 노드 스폰을 스케줄링. 낙하 스폰 시각 계산은 `data.inputTime` 대신 `nowPattern.GetInputTime(i)`를 사용하도록 변경됨.
- `AddPattern(index)`가 정오답 판정과 `nowPattern.Input()` 진행을 모두 처리 — 채보 재생 시 이 경로는 변경 불필요.
- 에디터 디버그 테스트(`DebugSetTestPattern`, `[ContextMenu]`)도 이제 `debugInputTimes`(인스펙터에 노출된 `float[]`)를 함께 넘겨야 한다 — 템플릿에 타이밍이 더 이상 없기 때문에 디버그용 타이밍도 별도로 지정해야 한다.
- 현재는 여전히 단일 `Pattern` 하나만 수동 지정해서 테스트하는 구조. **곡 전체에 걸쳐 여러 Pattern을 순차 재생하는 기능이 아직 없다** — 이번 작업에서 "여러 그룹을 오디오 재생 시각에 맞춰 순서대로 `SetPattern(template, inputTimes)` 호출해주는" 별도 컴포넌트가 필요하다.
- `patternStartTime`은 `Time.time` 기준이라 오디오 재생 시각(`AudioSource.time`)과 별개다. 곡 재생과 동기화하려면 오디오 재생 시작 시각을 기준점으로 삼아 각 그룹의 절대 시각과 매핑해야 한다.

### `PatternData` / 격자 좌표 (CLAUDE.md 기준)
- index 0~8, 3x3 배치. 인덱스 시퀀스가 곧 "패턴 모양"(예: L자, 대각선 등).
- `[Range(0,8)] index` 뿐 — 템플릿은 노드 위치(모양)만 정의한다. **입력 시각(`inputTime`)과 노출시간(`visibleExposureDuration`) 모두 더 이상 템플릿(디자인 타임) 데이터가 아니며, 항상 곡(Song/SongChart) 쪽에서 결정·주입되는 값**이다.
- `PatternHandler.ComputeFallDuration(int pointIndex, float exposureDuration)`은 시그니처는 그대로 유지하되, 두 번째 인자(`exposureDuration`)의 출처가 더 이상 `PatternData`가 아니라 **호출부(디버그 테스트는 `PatternHandler`의 별도 필드, 채보 재생은 곡 쪽에서 굽을 때 사용한 값)**로 바뀐다.

### 템플릿 라이브러리 부재
- 프로젝트 전체에서 `Pattern` SO 에셋(`.asset`)이 현재 몇 개 존재하는지 확인 필요 — 존재하더라도 "노드 개수별 폴더/네이밍 규칙"은 아직 없다.
- 이번 작업 범위: `Assets/Patterns/Templates` 폴더(신설)를 스캔해 `patternDatas.Length` 기준으로 캐싱하는 라이브러리 컴포넌트가 필요. 폴더가 비어있거나 특정 개수의 템플릿이 없으면, 해당 그룹은 배정 없이 스킵하고 경고를 남긴다 (요구사항 확정: 폴더 스캔 방식, 부족분은 디자이너가 나중에 채워 넣고 재분석).

### 오디오 분석 관련 기존 코드
- 프로젝트 내 `AudioClip`/`FFT`/`GetSpectrumData`/`GetOutputData` 참조 검색 결과 **기존 오디오 분석 코드 없음** — 완전히 새로 작성해야 한다.
- Unity `AudioClip.GetData(float[] data, int offsetSamples)`로 원본 PCM 샘플을 읽을 수 있다(임포트 설정상 `Decompress On Load` 또는 `Load Type`이 스트리밍이 아니어야 전체 샘플 접근이 쉬움 — 에디터 툴이므로 필요시 자동으로 읽기 가능한 형태인지 체크 필요).

## 확정된 요구사항 (사용자와의 대화 기반)
1. **동작 시점**: 에디터 전용 오프라인 툴. 런타임 실시간 분석 없음.
2. **온셋 감지 방식**: RMS(진폭 에너지) 기반. 윈도우 단위로 에너지를 계산하고 직전 대비 급증(임계값 이상)이면 온셋으로 판정. 주파수 대역 분석(FFT/Spectral Flux)은 하지 않음.
3. **그룹 크기 결정**: 온셋 간 시간 간격(gap)이 임계값보다 좁으면 같은 그룹, 넓으면 새 그룹. 그룹의 온셋 개수 = 필요한 패턴 노드 개수.
4. **템플릿 배정**: `Assets/Patterns/Templates` 아래 기존 Pattern SO를 노드 개수별로 스캔/캐싱, 그룹 노드 개수에 맞는 템플릿 중 랜덤 배정. 해당 개수의 템플릿이 없으면 그 그룹은 스킵 + 경고.
5. **에디터 UI 범위**: 파형 시각화 + 온셋/그룹 경계 미리보기 포함. 슬라이더(RMS 임계값, gap 임계값)로 즉시 재분석. 그룹별 재배정(reroll)/수동 템플릿 교체 가능.
6. **저장 방식 (2026-07-10 2차 변경)**: 그룹마다 새 Pattern SO 에셋을 복제 생성하지 않는다. 새 `SongChart` SO 하나에 그룹 목록을 직렬화하되, **온셋(입력 판정 시각)과 생성(스폰) 시각을 둘 다 저장한다**: 각 항목은 `{ 템플릿 Pattern 참조, onsetTimes(절대시각, 입력 판정 기준 — 곡의 비트에 고정된 값이라 절대 자동으로 안 바뀜), spawnTimes(절대시각, 노드가 실제로 나타나는 시각 — 굽는 시점에 onsetTimes와 그 시점에 정한 노출시간으로 계산해서 미리 구워둠) }`.
   - `spawnTimes`는 "그 순간 계산값을 스냅샷"한 것이라, 나중에 노출시간을 바꾸면 더 이상 실제 노출시간과 안 맞을 수 있다 — 이때는 에디터 툴의 **"재굽기" 버튼**으로 `onsetTimes`는 그대로 두고 `spawnTimes`만 새 노출시간 기준으로 다시 계산해서 덮어쓴다. `onsetTimes`가 계속 남아있어야 재굽기 시 오디오를 다시 분석할 필요가 없다.
   - 재생 시 `PatternHandler.SetPattern`에 `onsetTimes`(→ 입력 판정용, `Pattern.SetInputTimes`로 주입)와 `spawnTimes`(→ 낙하 노드 스폰 스케줄링용)를 함께 전달해야 한다. 현재(Step 4에서 이미 구현됨) `PatternHandler.SetPattern(pattern, inputTimes)`는 내부에서 `ComputeFallDuration()`으로 스폰 시각을 **매번 새로 계산**하는데, 채보 재생에서는 이미 구운 `spawnTimes`를 그대로 써야 하므로 **`SetPattern`에 스폰 시각을 직접 받는 파라미터를 추가**해야 한다(디버그/수동 테스트 경로는 구운 값이 없으니 기존처럼 `ComputeFallDuration` 계산을 그대로 유지 — 즉 파라미터를 선택적으로 만들어 두 경로를 모두 지원).
7. **노출시간 소유권 이전 (2026-07-10 4차 변경)**: `visibleExposureDuration`을 `PatternData`(템플릿)에서 완전히 제거한다. 템플릿(Pattern SO)은 "어느 노드가 입력 지점인지"(index 시퀀스)만 저장하는 순수 위치 데이터가 되고, 노출시간은 **곡(SongChart) 쪽에서 그룹/노드별로 정한다.** 즉 `SongChartEntry`가 `exposureDurations`(노드별 노출시간, 굽는 에디터에서 편집 가능)를 추가로 저장하고, `spawnTimes`는 `onsetTimes − ComputeFallDuration(index, exposureDurations[i])`로 계산해서 굽는다. 같은 템플릿(모양)이라도 어느 곡에서 쓰이느냐에 따라 노출시간(따라서 체감 난이도/속도)이 달라질 수 있다.

## 낙하 소요시간 계산의 실제 의존성 (`ComputeFallDuration`)
- `PatternHandler.ComputeFallDuration(int pointIndex, float exposureDuration)`은 단순히 `visibleExposureDuration` 값만으로 끝나지 않는다 — `patternPoints[pointIndex].transform.position`(씬 상의 실제 Point 위치), `fallingNodeParent`, `fallSpawnPositionY`, `screenTopY`(캔버스 실제 화면 상단 경계) 등 **씬/레이아웃에 종속된 값**을 함께 사용해 "화면 밖 낙하 구간"을 보정한다.
- 즉 굽는 에디터 툴이 `spawnTimes`를 정확하게 미리 계산하려면, 단순히 `onsetTime - visibleExposureDuration`으로는 부족하고 **실제 씬의 `PatternHandler`(또는 동일한 Point 배치를 가진 참조 오브젝트)를 참조해 `ComputeFallDuration`과 동일한 공식을 재현**해야 한다. 이 부분은 굽는 툴 설계 시 별도로 다뤄야 할 의존성이다(Plan Step 6/7에서 처리).

## BPM/비트 그리드 기반 노드 간격 리서치 (2026-07-10 5차 — 웹 리서치 반영)
사용자 피드백: "노드간의 간격은 BPM과 비트를 통한 음악적인 요소를 고려해서 개발 진행해줘." 기존 3~4차 설계는 `gapThreshold`(초 단위)를 레벨에 선형보간하는 방식이었는데, 이는 곡의 실제 박자와 무관한 임의의 시간 값이라 음악적으로 근거가 없다는 지적. 실제 리듬게임 채보 제작 관행을 조사했다.

- **BPM/오프셋은 보통 수동 입력이다**: 상용 채보 에디터(Beat Saber, Spin Rhythm 등)도 BPM이 0.01만 틀려도 곡 전체에 걸쳐 노트가 점점 어긋나기 때문에, 정확한 BPM과 "첫 비트가 시작하는 정확한 밀리초(오프셋)"를 채보자가 직접 지정하고 그리드를 곡에 맞춰 정렬하는 과정을 거친다. 자동 템포 감지(tempo detection)는 가변 BPM 곡이나 정렬이 어긋난 곡에서 신뢰도가 낮고 구현 난이도도 훨씬 높다 — 우리 프로젝트의 단순 RMS 온셋 감지 수준 구현력으로는 자동 BPM 감지를 신뢰성 있게 구현하기 어렵다고 판단, **BPM/오프셋은 굽는 에디터에서 디자이너가 직접 입력**하는 것으로 결정.
- **비트 그리드 스냅(quantize)**: 채보 에디터들은 BPM으로 계산한 "비트 그리드"(예: 4분음표 간격) 위에 노트를 스냅해서 배치한다. 세분화 단위(subdivision)는 보통 1/4, 1/8, 1/16, (트리플릿의 경우 1/12, 1/24) 등이다.
- **난이도 ↔ 비트 분할 단위 매핑 관행**: Easy/Normal은 4분/8분음표 위주(성긴 배치), Hard/Expert는 8분/16분음표, Extreme/Master는 16분 스트림·복합 폴리리듬까지 사용하는 것이 일반적인 관례다. 즉 난이도가 올라갈수록 "더 촘촘한 시간 간격의 초"가 아니라 **"더 잘게 쪼갠 비트 단위"**가 노트 배치에 허용되는 방식으로 밀도가 올라간다.
- **결론(설계 반영)**: 레벨(1~30)을 초 단위 `gapThreshold`에 직접 선형보간하던 기존 방식을 폐기하고, **레벨 → 허용되는 최소 비트 분할 단위(subdivision)** 매핑으로 대체한다. 감지된 온셋은 원시 시각 그대로 쓰지 않고, BPM 그리드의 가장 가까운 분할 지점으로 스냅(quantize)한 뒤 그룹핑한다. 자세한 알고리즘은 Plan의 가정 8(개정판)과 Step 2/6 참고.

Sources:
- [Rhythm Game Charting & Level Design: BPM, Patterns, Difficulty Curves](https://rhythm-games.com/guides/rhythm-game-charting-level-design)
- [Beat Saber — Preparing the Grid Alignment](https://beatsaber.com/documentation/preparing-the-grid-alignment/index.html)
- [Spin Rhythm — Editor Guide](https://www.spinrhythmgame.com/editor-guide)
- [BSMG Wiki — Downmapping](https://bsmg.wiki/mapping/downmapping.html)

## 어셈블리 구성 (구현 중 확정, UnityMCP로 검증됨)
- `OnsetDetector`/`BeatGrid`/`OnsetGrouper`(Unity API 비의존 순수 로직)는 `Assets/02. Scripts/ChartGen/Core/`로 분리하고 `ChartGen.Core.asmdef`(엔진 참조 없음, `noEngineReferences: true`)로 묶었다. 프로젝트의 다른 스크립트(`SongChart`, `ChartPlayer`, `PatternChartWindow`, `PatternTemplateLibrary` 등)는 asmdef 없이 기본 어셈블리(Assembly-CSharp/-Editor)에 남아있는데, Unity의 기본(예약) 어셈블리는 `autoReferenced: true`인 커스텀 asmdef를 자동으로 참조하므로 별도 수정 없이 `ChartGen` 네임스페이스를 그대로 쓸 수 있었다.
- 테스트는 `Assets/02. Scripts/ChartGen/Tests/ChartGen.Tests.asmdef`(Editor 전용, `ChartGen.Core` 참조)로 구성. 처음에 `"Assembly-CSharp"`을 이름으로 직접 참조하는 방식은 컴파일 에러(`CS0246: 'ChartGen'을 찾을 수 없음`)가 발생해 실패했다 — 예약 어셈블리는 이름 문자열로 참조할 수 없고, 커스텀 asmdef끼리만 이름/GUID 참조가 가능하다는 것을 확인.
- 다만 `ChartGen.Tests.asmdef`가 Unity의 EditMode 테스트 템플릿과 동일한 구성(`includePlatforms: ["Editor"]`, `UnityEditor.TestRunner` 참조 등)임에도, Test Runner가 이 테스트들을 EditMode가 아니라 **PlayMode**로 분류했다(원인 미상 — 프로젝트 재시작 후 재확인 필요할 수 있음). PlayMode 러너로는 15개 테스트 전부 정상 통과함을 UnityMCP로 확인함.

## 미결 사항 / 이번 Plan에서 다루는 가정
- `Pattern.SetInputTimes(IReadOnlyList<float> times)` / `Pattern.GetInputTime(int position)` API가 이미 추가되어 있다(2026-07-10 완료). `PatternData`에서 `inputTime` 필드는 제거되었다.
- 채보 결과를 실제로 재생/검증하려면 곡 전체에 걸쳐 순차적으로 `PatternHandler.SetPattern(...)`을 호출해주는 `ChartPlayer` 컴포넌트가 필요 (현재 프로젝트에는 단일 Pattern 디버그 재생만 있음).
- `PatternHandler.SetPattern`에 구운 `spawnTimes`를 직접 받는 선택적 파라미터를 추가해야 한다(미구현, Plan에서 다룸).
- `PatternData`에서 `visibleExposureDuration`도 제거해야 한다(미구현, Plan에서 다룸) — 제거 후 `PatternHandler`의 디버그 테스트 경로(`DebugSetTestPattern`)는 노출시간을 어디선가 별도로 공급받아야 하므로, `debugInputTimes`와 짝을 이루는 `debugExposureDurations` 필드가 추가로 필요하다.
- 온셋 감지/그룹핑 로직은 Unity API(`AudioClip`)에 의존하지 않는 순수 C# 함수로 분리해 EditMode 유닛 테스트가 가능하게 한다.
- 같은 템플릿 Pattern 에셋이 곡 안에서 여러 번(비순차적으로 겹쳐서) 재생될 일은 없다는 전제(`PatternHandler`가 한 번에 하나의 `nowPattern`만 순차 재생) — `ChartPlayer`도 이 전제를 유지해야 한다.
