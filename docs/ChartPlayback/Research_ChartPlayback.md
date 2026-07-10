# Research_ChartPlayback.md — 채보 테스트 재생 시스템

## 피드백 요약 (2차)

1. **씬 간 곡 선택**: 곡 선택 씬(SongSelect)에서 선택한 `SongChart`를 게임플레이 씬(DefaultScene)으로 전달
2. **에디터 디버그**: 씬 선택 없이 에디터에서 직접 곡을 지정해 플레이 가능
3. **재생 전 카운트다운**: 곡이 시작하기 전 일정 시간 대기 후 재생

---

## 현재 상태

- `ChartPlayer.cs`: 자동 재생만, Play/Stop 없음, 씬 연결 없음
- 씬: `DefaultScene` 1개만 존재 (`Assets/01. Scenes/`)
- 채보 에셋: `Assets/04. Datas/Song/Dreamer_Lv10.asset` 1개

---

## 씬 간 데이터 전달 방법 검토

| 방법 | 장점 | 단점 |
|------|------|------|
| `DontDestroyOnLoad` 싱글톤 | 단순, 런타임 전용 | 씬 직접 진입 시 없음 |
| ScriptableObject 런타임 변수 | 에디터·런타임 동일 참조 | 씬 언로드 후 값 남음 |
| `PlayerPrefs` | 영속 | SongChart 직렬화 불가 |

**채택: `DontDestroyOnLoad` 싱글톤 (`GameSession`)** + 에디터 디버그용 fallback 필드 조합
- `GameSession.SelectedChart`가 null이면 `ChartPlayer`의 `[SerializeField] SongChart debugChart`를 대신 사용
- 에디터에서 SongSelect 씬 없이 직접 플레이할 때 `debugChart`로 동작

---

## 씬 구성

```
SongSelectScene (신규)
  └─ SongSelectManager: availableCharts[] 목록 표시
       → 선택 시 GameSession.SelectedChart 설정 + DefaultScene 로드

DefaultScene (기존)
  └─ GameSession GameObject (DontDestroyOnLoad)
  └─ ChartPlayer:
       · GameSession.SelectedChart 또는 debugChart로 곡 결정
       · countdownDuration 대기 후 오디오 재생
```

---

## 카운트다운 설계

- `ChartPlayer.Play()` 호출 시 코루틴 시작
- `countdownDuration`(초, 기본 3f) 대기
- 대기 중에도 `pendingEntries` 큐는 미리 구성해두되, `audioSource.Play()` + `isPlaying = true` 플래그는 대기 후 설정
- `spawnTimes`는 오디오 시각(0부터) 기준 절대값이므로, 오디오 재생 시작 직전에 큐를 구성하면 타이밍이 맞음

---

## 영향 범위

| 대상 | 변경 내용 |
|------|-----------|
| `GameSession.cs` | **신규** — DontDestroyOnLoad 싱글톤, SelectedChart 보유 |
| `ChartPlayer.cs` | Play/Stop, 카운트다운, GameSession 연동, debugChart fallback |
| `SongSelectScene.unity` | **신규** 씬 |
| `SongSelectManager.cs` | **신규** — 곡 목록 UI, 씬 전환 |
| `DefaultScene.unity` | GameSession + ChartPlayer GameObject 추가 |
