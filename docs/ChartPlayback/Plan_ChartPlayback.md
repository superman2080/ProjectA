# Plan_ChartPlayback.md — 채보 테스트 재생 시스템

> 기반 문서: `Research_ChartPlayback.md`

## 설계 요약

- **`GameSession`** (DontDestroyOnLoad 싱글톤): 씬 간 선택된 `SongChart` 전달
- **`ChartPlayer`**: `GameSession.SelectedChart` 우선 → 없으면 `debugChart` fallback. 재생 전 `countdownDuration` 대기
- **`SongSelectScene`**: 곡 목록 → 선택 → DefaultScene 로드
- **에디터 디버그**: `debugChart` 직접 지정 → SongSelect 없이 재생 가능

---

## 단계별 구현 계획

### Step 1 — `GameSession.cs` 신규 생성

- [x] `Assets/02. Scripts/GameSession.cs` 생성
  - `DontDestroyOnLoad` 싱글톤 (`public static GameSession Instance`)
  - `public SongChart SelectedChart { get; set; }` — 씬 간 전달할 채보
  - `Awake()`: 이미 인스턴스 있으면 자신 파괴, 없으면 `DontDestroyOnLoad(gameObject)` 등록

### Step 2 — `ChartPlayer.cs` 전면 리팩터

- [x] 필드 추가:
  - `[SerializeField] private SongChart debugChart` — 에디터 디버그용 직접 지정 (GameSession 없을 때 사용)
  - `[SerializeField] private float countdownDuration = 3f` — 재생 전 대기 시간(초)
  - `[SerializeField] private bool playOnStart = false`
  - `private Coroutine playCoroutine`
- [x] `ActiveChart` 프로퍼티 (private): `GameSession.Instance?.SelectedChart ?? debugChart`
- [x] `Start()`: `playOnStart`가 true면 `Play()` 호출
- [x] `public void Play()`:
  - `ActiveChart` null 체크 (null이면 에러 로그 후 리턴)
  - `audioSource`, `patternHandler` null 체크
  - 기존 `playCoroutine` 있으면 중단
  - `playCoroutine = StartCoroutine(PlayRoutine())`
- [x] `IEnumerator PlayRoutine()`:
  - `pendingEntries` 큐 구성 (`ActiveChart.entries`를 `spawnTimes[0]` 오름차순 정렬)
  - `yield return new WaitForSeconds(countdownDuration)` — 대기
  - `audioSource.clip = ActiveChart.song`
  - `audioSource.Play()`
- [x] `public void Stop()`:
  - `playCoroutine` 있으면 `StopCoroutine` 후 null
  - `audioSource.Stop()`
  - `pendingEntries?.Clear()`
- [x] `Update()`: `pendingEntries != null`이고 비어있지 않을 때만 큐 처리
- [x] `[ContextMenu("Play")]` → `Play()`, `[ContextMenu("Stop")]` → `Stop()`

### Step 3 — `SongSelectManager.cs` 신규 생성

- [x] `Assets/02. Scripts/SongSelectManager.cs` 생성
  - `[SerializeField] private SongChart[] availableCharts` — Inspector에서 곡 목록 등록
  - `[SerializeField] private string gameplaySceneName = "DefaultScene"`
  - `public void SelectChart(SongChart chart)`:
    - `GameSession.Instance`가 없으면 새 `GameObject("GameSession")`에 컴포넌트 추가 (SongSelect씬에서 직접 진입 시 안전 처리)
    - `GameSession.Instance.SelectedChart = chart`
    - `SceneManager.LoadScene(gameplaySceneName)`

### Step 4 — `SongSelectScene` 씬 생성 및 구성

- [x] `Assets/01. Scenes/SongSelectScene.unity` 씬 생성
- [x] 씬 내 구성:
  - `GameSession` 빈 GameObject + `GameSession` 컴포넌트
  - `Canvas` + `SongSelectManager` 컴포넌트
  - `availableCharts` → `Dreamer_Lv10.asset` 등록
  - 각 곡마다 버튼 1개 생성, 버튼 텍스트 = 곡 이름(`chart.song.name`), `OnClick` → `SongSelectManager.SelectChart(chart)`

### Step 5 — `DefaultScene`에 GameSession + ChartPlayer GameObject 구성

- [x] `GameSession` 빈 GameObject + `GameSession` 컴포넌트 추가 (씬 직접 진입 시 인스턴스 보장)
- [x] `ChartPlayer` 빈 GameObject 생성:
  - `AudioSource` 컴포넌트 (`playOnAwake = false`, `loop = false`)
  - `ChartPlayer` 컴포넌트
  - `debugChart` → `Dreamer_Lv10.asset`
  - `audioSource` → 동일 오브젝트 AudioSource
  - `patternHandler` → `PointBackground`

---

## 데이터 흐름

```
[SongSelectScene]
  SongSelectManager.SelectChart(chart)
    → GameSession.SelectedChart = chart
    → LoadScene("DefaultScene")

[DefaultScene]
  ChartPlayer.Play()
    → ActiveChart = GameSession.SelectedChart ?? debugChart
    → WaitForSeconds(countdownDuration)
    → audioSource.Play()
    → Update()에서 pendingEntries 순서대로 SetPattern
```

---

## 사용법 (구현 후)

**에디터 디버그** (SongSelect 생략):
1. Play Mode 진입
2. `ChartPlayer` Inspector 우클릭 → **Play**
3. `countdownDuration` 초 대기 후 곡 재생 시작

**정식 플로우**:
1. `SongSelectScene` 진입 → 곡 버튼 클릭
2. `DefaultScene` 로드 → 자동으로 `Play()` 호출(`playOnStart = true` 설정 시)

---

>>> 여기에 피드백을 남겨주세요.
