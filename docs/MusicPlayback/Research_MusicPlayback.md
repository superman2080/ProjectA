# Research — 음악 재생 두 클래스(`ChartPlayer` · `MusicPlayer`)

## 1. 두 클래스가 실제로 하는 일

### `ChartGen.ChartPlayer` (`02. Scripts/ChartGen/ChartPlayer.cs`, 약 190줄)
1. **곡 선택** — `ActiveChart => GameSession.SelectedChart ?? debugChart`. 공개인 이유가 주석에 못박혀 있다(복제하면 디버그 재생에서만 만점이 어긋난다).
2. **카운트다운 = 프리웜 창 + 인트로 창** — `enemyDirector.PrepareStage()` 후 `OnCountdownStarted(duration)` 발행, `WaitForSeconds`.
3. **오디오 재생** — `audioSource.clip = ActiveChart.song` → `Play()`. **루프하지 않는다.**
4. **채보 시계** — `Update`에서 `audioSource.time >= spawnTimes[0]`이면 `enemyDirector.EnqueueCue` → `patternHandler.SetPattern`.
5. **곡 종료** — 엔트리 소진 + `!audioSource.isPlaying` → `DissolveAll()` + `OnSongEnded`.
6. **볼륨 추종** — `SoundManager.OnVolumeChanged` 구독 → `audioSource.volume = GetEffectiveVolume(Music)`.

### `MusicPlayer` (`02. Scripts/Audio/MusicPlayer.cs`, 약 75줄)
1. **클립 하나를 루프 재생** — `source.loop = true`, `playOnStart`면 `OnEnable`에서 `Play()`.
2. **볼륨 추종** — `SoundManager.OnVolumeChanged` 구독 → `GetEffectiveVolume(Music) * volumeScale`.
3. 그 외 없음. 게임플레이를 모른다.

## 2. 겹치는 것은 정확히 한 덩어리다 — 볼륨 추종

두 클래스에서 **의미가 같은 코드**는 이것뿐이다:

| 조각 | `ChartPlayer` | `MusicPlayer` |
|---|---|---|
| 직렬화 `AudioSource` | `audioSource`(직렬화 참조) | `GetComponent`(+`[RequireComponent]`) |
| 구독 대상 캐시 | `subscribedSoundManager` | `subscribed` |
| `OnEnable` 구독 + 즉시 적용 | 있음 | 있음 |
| `OnDisable` 해제 | 있음 | 있음 |
| `HandleVolumeChanged(channel)` | `Music \|\| Master` | `Music \|\| Master` |
| 볼륨 대입 | `GetEffectiveVolume(Music)` | `GetEffectiveVolume(Music) * volumeScale` |
| `Play()` / `Stop()` | 있음(채보 코루틴 포함) | 있음(단순) |

합쳐 **약 25줄이 두 곳에 거의 글자 단위로 중복**돼 있다. `ChartPlayer` 쪽에는 그 중복에 딸린 함정 주석까지 붙어 있다:

> `OnDisable`에서 `SoundManager.Instance`를 다시 부르면 종료 순서상 `SoundManager`가 먼저 죽었을 때 게터가 새 인스턴스를 만들어 씬에 미아 오브젝트를 남긴다.

**이 지식이 한쪽에만 적혀 있고 다른 쪽은 같은 구조를 우연히 맞게 구현해 둔 상태**다 — 중복의 전형적인 비용이다.

겹치지 **않는** 것: 채보 선택 · 카운트다운 · 프리웜 · 엔트리 투입 · 이벤트 둘 · 루프 여부 · 클립의 출처. 즉 **몸통은 전혀 겹치지 않는다.**

## 3. 소비자 목록 (합칠 때 깨질 수 있는 지점)

### `MusicPlayer`
- **코드 소비자 0개.** `grep`으로 `MusicPlayer`를 참조하는 `.cs`가 하나도 없다.
- 씬 참조: **`Tutorial.unity` 한 곳**(`TutorialMusic` 오브젝트).
- ⚠ 다만 **스크립트 guid로 직렬화**돼 있다(`b18239cf…`) → **파일명/클래스명을 바꾸면 그 씬 참조가 끊긴다.**

### `ChartPlayer`
직렬화 참조로 들고 있는 소비자 넷 — 전부 "기존 이벤트만 구독하는 순수 소비자" 관례다.

| 소비자 | 쓰는 것 |
|---|---|
| `CameraDirector` | `OnCountdownStarted`(인트로 창) |
| `ScoreDirector` | `ActiveChart`(총 노트·만점), 곡 시작/종료 |
| `FinaleSilhouetteDirector` | `ActiveChart.entries.Length`, `OnCountdownStarted`, **`SongSource`**(피치) |
| `BattleSceneBootstrap` | `Play()` / `Stop()` |

`SongSource`를 쓰는 곳은 `FinaleSilhouetteDirector` **한 곳**뿐이다.

씬 참조: `BattleScene.unity` · `Tutorial.unity`(+ `_Recovery/0.unity`).

⚠ **`Tutorial`의 `ChartPlayer`는 `m_Enabled: 1`이지만 `playOnStart: 0`이다** — 살아만 있고 재생을 안 한다. `debugChart`에는 `Dreamer_Lv10`이 꽂혀 있다. `TutorialDirector`의 주석이 그 상태를 *"`ChartPlayer`가 꺼져 있고"*로 부른다.

## 4. 합치면 안 되는 방향이 문서화돼 있다

`MusicPlayer` 헤더 주석:

> **왜 `ChartPlayer`를 쓰지 않는가**: 그쪽 오디오는 **채보의 시계**다(`audioSource.time`이 패턴 투입 시각을 정한다). 배경음은 판정과 아무 관계가 없으므로 그 클래스에 얹으면 **"곡이 아닌 곡"이 생겨 채보 재생 경로에 분기가 는다.**

즉 **"`MusicPlayer`를 `ChartPlayer`에 흡수"는 이미 검토되고 기각된 방향**이다. 기술적으로도 `ChartPlayer.Play()`는 `patternHandler` null 검사에서 **에러를 찍고 반환**하므로 배경음만 틀 수 없고, `PlayRoutine`은 `ActiveChart.entries`를 전제하며 루프를 지원하지 않는다.

## 5. 왜 지금 이 질문이 나왔는가 — `RiftController`

`docs/Rift/Research_Rift.md` §8: 튜토리얼에서 **들리는 소리는 `MusicPlayer`**, 재생하지 않는 `ChartPlayer`는 `Dreamer_Lv10`을 물고 있다. 연출(`RiftController`)이 "현재 곡의 BPM"을 알려면 **"이 씬에서 음악을 내는 것"이라는 하나의 타입**을 참조하고 싶은데, 지금은 **무관한 두 타입**이라 참조 필드가 둘로 갈라진다(`musicPlayer` / `chartPlayer` + 우선순위 규칙 + `OnValidate` 경고).

**합치는 것의 실익은 중복 25줄 제거보다 이쪽이 크다** — 앞으로 "음악에 맞춰 무언가 한다"는 연출이 생길 때마다 같은 포크가 반복된다.

## 6. 제약 정리

- **파일명 = 클래스명**이 Unity 규칙이고 씬은 **스크립트 guid**로 참조한다 → `MusicPlayer`·`ChartPlayer` **둘 다 이름을 유지해야** 세 씬의 배선이 산다.
- 직렬화 필드를 **같은 오브젝트의 베이스 클래스로 올리는 것은 값이 보존된다**(Unity는 상속 사슬을 훑어 **필드 이름**으로 저장한다). 이름·타입을 바꾸지 않는 한 안전하다. ⚠ 그래도 세 씬에서 실제로 확인할 항목이다.
- `MusicPlayer`의 `AudioSource`는 **직렬화 참조가 아니라 `GetComponent`**다 → 베이스가 직렬화 참조만 받으면 `Tutorial`에서 그 칸이 비어 무음이 된다. **`null`이면 `GetComponent` 폴백** 한 줄로 막는다.
- `MusicPlayer`는 `OnEnable`에서 재생하고 `ChartPlayer`는 `Start`에서 재생한다. 베이스가 `OnEnable`을 가지면 **`protected virtual`**이어야 파생이 덧붙일 수 있다.
