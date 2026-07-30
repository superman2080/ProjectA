# Plan: 노트 판정 효과음 (SFX 매니저)

Research 근거: `docs/Sfx/Research_Sfx.md`

## 설계 개요
2계층 구조로 만든다.

- **`SoundManager`**(상위 개념) — `Singleton<SoundManager>`, `DontDestroy = true`. 볼륨만 담당한다. **`SfxManager`/`ChartPlayer` 같은 구체적 소비자를 전혀 모른다** — `MasterVolume`/`SfxVolume`/`MusicVolume`처럼 이름 박은 프로퍼티 대신, 이 프로젝트에 이미 있는 "enum + 카탈로그" 관례(`EffectTrigger`, `CameraTrigger`)를 그대로 따르는 **`VolumeChannel` enum**(`Master`, `Sfx`, `Music`)으로 일반화한다. 소비자는 `GetVolume(VolumeChannel)` 하나만 알면 되고, `SoundManager`는 어떤 채널이 누구 소유인지 모른다. 새 카테고리(보이스·앰비언트 등) 추가는 enum 값 하나 늘리는 걸로 끝 — 소비자 쪽 API는 안 바뀐다.
- **`SfxManager`**(하위, 실제 재생 담당) — `Singleton<SfxManager>`, `DontDestroy = true`. `SfxCatalog` + AudioSource 풀을 갖고 `Play(trigger)`로 재생하되, 재생 볼륨 = `entry.volume * SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Sfx)`. **`PatternHandler`를 전혀 모른다** — 다른 씬에서도 재사용해야 하므로 게임플레이 시스템과 결합시키지 않는다.
- 두 매니저 다 싱글톤이라 `SfxManager.Instance.Play(...)`/`SoundManager.Instance.SetSfxVolume(...)` 형태로 **어디서든 참조 없이 바로 호출**한다 — `EffectManager`에 `[SerializeField]` 필드로 연결할 필요 없음.
- 판정 시점 요청은 이미 `PatternHandler` 이벤트를 구독 중인 `EffectManager`가 `HandleFallingNodeResolved`에서 `SfxManager.Instance.Play(trigger)`를 호출해 담당한다.

### 싱글톤의 "모든 씬에서 동작" 주의점
`Singleton<T>.Instance`는 씬에 없으면 **빈 GameObject를 새로 만들어 대신 채운다**(`Singleton.cs:16-20`) — 이 경우 인스펙터에서 채워둔 `catalog`(AudioClip들)가 없는 빈 인스턴스가 생성돼버린다. 따라서:
- `SoundManager`/`SfxManager`를 **가장 먼저 로드되는 씬(예: `SongSelectScene`)에 프리팹으로 한 번만 배치**하고 `catalog`를 채워둔다. `Awake()`에서 `DontDestroyOnLoad`로 이후 모든 씬에 그대로 들고 다닌다(`GameSession`과 동일 컨벤션, CLAUDE.md §9-10).
- `DefaultScene`에는 따로 배치하지 않는다(중복 인스턴스 방지 — 이미 `DontDestroyOnLoad`로 살아있는 인스턴스를 `Singleton.Instance`가 `FindAnyObjectByType`으로 찾아 재사용한다).

- 새 enum: `SfxTrigger { Perfect, Good, Miss }` (1차 범위 — 노트 판정 3종만. 다른 트리거는 필요해지면 나중에 추가)
- 새 파일 3개: `Assets/02. Scripts/Effect/SfxCatalog.cs`, `Assets/02. Scripts/Effect/SfxManager.cs`, `Assets/02. Scripts/Audio/SoundManager.cs`
- `PatternHandler` 본체 로직은 그대로 두고 `EffectManager`에 한 줄(SfxManager 호출)만 추가한다.

## 단계

- [x] Step 1: `SfxCatalog.cs` 작성
  - `SfxTrigger` enum (`Perfect`, `Good`, `Miss`)
  - `SfxEntry` struct: `trigger`, `AudioClip clip`, `[Range(0,1)] float volume = 1f`, `float pitch = 1f`
  - `CameraCueCatalog.cs`와 동일하게 전역 네임스페이스, `[Serializable]`

- [x] Step 2: `SoundManager.cs` 작성 (볼륨 전담, 재생 로직도 소비자 지식도 없음)
  - `public enum VolumeChannel { Master, Sfx, Music }` (같은 파일 또는 `SoundChannelCatalog.cs`에 분리 — `EffectTrigger`/`CameraTrigger`와 같은 위치 관례)
  - `[Serializable] public struct VolumeChannelEntry { public VolumeChannel channel; [Range(0,1)] public float volume; }` — `EffectEntry`/`SfxEntry`와 동일한 "트리거+값" 카탈로그 구조
  - `public class SoundManager : Singleton<SoundManager>`, `protected override bool DontDestroy => true;`
  - `[SerializeField] private List<VolumeChannelEntry> initialVolumes` (Master/Sfx/Music 기본값 1.0으로 인스펙터에 노출, 채널별로 안 채워지면 1.0 취급)
  - 내부 `Dictionary<VolumeChannel, float> volumes` — `Awake()`에서 `initialVolumes`로 초기화
  - `public float GetVolume(VolumeChannel channel)` / `public void SetVolume(VolumeChannel channel, float value)`(0~1 클램프, `volumes` 갱신 후 이벤트 발행)
  - `public float GetEffectiveVolume(VolumeChannel channel)` — `channel == Master`면 `GetVolume(Master)` 그대로, 아니면 `GetVolume(channel) * GetVolume(Master)`. **소비자는 이 메서드 하나만 호출하면 됨.**
  - `public event Action<VolumeChannel> OnVolumeChanged;` — `SetVolume`에서 발행(어떤 채널이 바뀌었는지 전달)
  - `SoundManager`는 `Sfx`/`Music`이 각각 누구 소유인지 모른다 — 그냥 채널 값 저장소

- [x] Step 3: `SfxManager.cs` 작성 (PatternHandler 참조 없음, 순수 재생 전용)
  - `public class SfxManager : Singleton<SfxManager>`, `protected override bool DontDestroy => true;`
  - `[SerializeField] private List<SfxEntry> catalog`
  - `[SerializeField] private int voiceCount = 8` (동시 재생 AudioSource 풀 크기)
  - `Awake()`에서 `base.Awake()` 호출 후 `voiceCount`개의 자식 `AudioSource`(2D, `playOnAwake=false`, `spatialBlend=0`)를 코드로 생성해 풀 구성, `catalog`를 `Dictionary<SfxTrigger, SfxEntry>`로 색인
  - `public void Play(SfxTrigger trigger)`: 카탈로그 조회 → 매핑 없거나 clip 없으면 무연출 → 유휴 AudioSource(현재 `isPlaying == false`인 것, 없으면 가장 오래전에 재생 시작한 것 재사용) 골라 `clip`/`pitch` 세팅, `volume = entry.volume * SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Sfx)` 적용 후 `Play()`
  - 이벤트 구독 없음, `PatternHandler` 참조 없음 — 다른 씬에 그대로 드롭인 가능

- [x] Step 4: `EffectManager.cs`에서 SfxManager 요청
  - `HandleFallingNodeResolved`에서 기존 VFX `Play(trigger, worldPosition)` 호출 옆에 `SfxManager.Instance.Play(MapToSfxTrigger(result));` 추가 (VFX의 `EffectTrigger`와 SFX의 `SfxTrigger`는 이름은 겹치지만 서로 다른 enum이라 매핑 함수 하나 필요). 싱글톤이라 필드 연결 없이 바로 호출.

- [x] Step 5: `ChartPlayer.cs`에서 BGM 볼륨을 `SoundManager`에 연동
  - `OnEnable`에서 `SoundManager.Instance.OnVolumeChanged += HandleVolumeChanged;` 구독, `OnDisable`에서 해제 (`ChartPlayer.cs`에 `OnEnable/OnDisable`이 아직 없으므로 새로 추가)
  - `private void HandleVolumeChanged(VolumeChannel channel) { if (channel == VolumeChannel.Music || channel == VolumeChannel.Master) ApplyMusicVolume(); }`
  - `private void ApplyMusicVolume() => audioSource.volume = SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Music);`
  - `PlayRoutine()`에서 `audioSource.Play()` 직전에 `ApplyMusicVolume()` 한 번 호출(곡 시작 시 현재 볼륨 반영) + `OnEnable`에서도 한 번 호출(씬 진입 시 갱신)
  - 결합 방향은 `ChartPlayer → SoundManager` 단방향(기존 `EffectManager → SfxManager`와 동일 패턴). `ChartPlayer`가 아는 건 `VolumeChannel.Music` 값 하나뿐, `SoundManager`는 `ChartPlayer`를 모른다

- [x] Step 6: 씬 배선
  - `SoundManager`, `SfxManager`를 `SongSelectScene`에 배치. `SfxManager.catalog`에 Perfect/Good/Miss 슬롯 3개 채워둠(clip은 비어있음 — 사용자가 AudioClip 임포트 후 직접 채워야 함, 이 Plan 범위 밖).
  - `DefaultScene`에는 배치 안 함(확인 완료, 중복 방지).

- [ ] Step 7: 검증
  - 곡 선택 씬 → `DefaultScene` 전환 후에도 `SoundManager`/`SfxManager` 인스턴스가 파괴되지 않고 유지되는지 확인
  - Play 모드에서 노드 Perfect/Good/Miss 판정 시 각각 지정한 클립이 재생되는지 확인
  - 연속 타격(콤보) 시 효과음이 서로 끊기지 않고 겹쳐 들리는지 확인(풀 크기 8 기준)
  - `SoundManager.Instance.SetVolume(VolumeChannel.Sfx, 0)` / `SetVolume(VolumeChannel.Music, 0)` 등으로 볼륨 조절 시 SFX와 BGM 각각 반영되는지 확인 (BGM은 재생 중에도 `OnVolumeChanged`로 즉시 반영, SFX는 다음 재생부터 반영)

## 확장 여지 (이번 범위 아님, 언급만)
- `NodeConnected`/`PatternCompleteFull`/`PatternComplete` 등 다른 트리거는 필요해지면 `SfxTrigger`에 값 추가 + 카탈로그 엔트리 추가로 확장.
- `CharacterActionPlayer`, `SliceTargetDirector` 등 다른 시스템도 필요해지면 `SfxManager.Instance.Play(trigger)`를 직접 호출해 요청할 수 있다.
- 콤보 수에 따른 피치/볼륨 가변 등은 나중 별도 Plan.
- 볼륨 슬라이더 등 실제 설정 UI는 이번 범위 밖(API만 제공).

---
피드백은 이 문서에 `>>>`로 남겨주세요.
