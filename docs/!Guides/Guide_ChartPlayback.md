# Guide_ChartPlayback.md — 채보 재생 시스템 가이드

## 시스템 구성 요약

```
GameSession (DontDestroyOnLoad)
  └─ SelectedChart: SongChart  ← 씬 간 선택한 곡 전달

SongSelectScene
  └─ SongSelectManager
       └─ 버튼 클릭 → GameSession.SelectedChart 설정 → BattleScene 로드

BattleScene
  └─ ChartPlayer (AudioSource 포함)
       └─ ActiveChart = GameSession.SelectedChart ?? debugChart
       └─ countdownDuration 대기 후 오디오 재생
       └─ pendingEntries 큐 → PatternHandler.SetPattern 순차 호출
```

---

## 관련 파일

| 파일 | 역할 |
|------|------|
| `Assets/02. Scripts/GameSession.cs` | 씬 간 선택 곡 보유 싱글톤 |
| `Assets/02. Scripts/ChartGen/ChartPlayer.cs` | 채보 재생 글루 (카운트다운 + 패턴 피드) |
| `Assets/02. Scripts/SongSelectManager.cs` | 곡 선택 UI 로직 |
| `Assets/01. Scenes/SongSelectScene.unity` | 곡 선택 씬 |
| `Assets/01. Scenes/BattleScene.unity` | 게임플레이 씬 |
| `Assets/04. Datas/Song/` | SongChart 에셋 저장 위치 |

---

## 사용 방법

### A. 에디터 디버그 (SongSelect 생략)

1. Unity에서 **BattleScene** 열기
2. **Play Mode** 진입
3. Hierarchy에서 `ChartPlayer` 선택
4. Inspector 우상단 점 세 개(⋮) 또는 컴포넌트 우클릭 → **Play** 클릭
5. `Countdown Duration`(기본 3초) 대기 후 곡 재생 시작
6. 중단하려면 동일 메뉴에서 **Stop** 클릭

> `debugChart` 필드에 원하는 SongChart를 직접 지정하면 다른 곡으로 테스트 가능.

---

### B. 정식 플로우 (씬 선택 → 게임플레이)

1. **SongSelectScene** 열기 (또는 Build Settings 기준 첫 씬으로 실행)
2. Play Mode 진입
3. 화면에 표시된 곡 버튼 클릭 (예: `Dreamer_Lv10`)
4. BattleScene이 자동 로드됨
5. `ChartPlayer`의 `playOnStart = true` 설정 시 자동 재생, 아니면 Inspector에서 **Play** 클릭

---

### C. 새 곡 추가 방법

1. `Tools/Pattern Chart Tool`에서 새 SongChart 에셋 생성 및 저장 (`Assets/04. Datas/Song/`)
2. **SongSelectScene** 열기 → Hierarchy의 `Canvas` 선택
3. `SongSelectManager` 컴포넌트의 `Available Charts` 배열에 새 에셋 추가
4. 새 버튼 GameObject를 복제(Ctrl+D)해 텍스트 수정, `OnClick` → `SongSelectManager.SelectChart(새 차트)` 연결

---

## ChartPlayer 인스펙터 필드 설명

| 필드 | 설명 |
|------|------|
| `Debug Chart` | SongSelect 없이 에디터 직접 테스트할 때 사용할 SongChart |
| `Audio Source` | 곡을 재생할 AudioSource 컴포넌트 (같은 오브젝트) |
| `Pattern Handler` | 패턴을 수신할 PatternHandler (씬의 PointBackground) |
| `Countdown Duration` | 재생 전 대기 시간(초). 0이면 즉시 재생 |
| `Play On Start` | true 설정 시 씬 로드 직후 자동 재생 |

---

## 씬 구성 (Build Settings)

| 순서 | 씬 | 용도 |
|------|----|------|
| 0 | `SongSelectScene` | 곡 선택 |
| 1 | `BattleScene` | 게임플레이 |
