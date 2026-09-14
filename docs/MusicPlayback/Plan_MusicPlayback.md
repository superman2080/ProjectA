# Plan — `ChartPlayer` · `MusicPlayer` 통합

목적은 둘이다. ① 볼륨 추종 **중복 25줄**을 없앤다(Research §2). ② 연출이 **"이 씬에서 음악을 내는 것" 하나를 참조**할 수 있게 해 `RiftController`류의 포크를 원천 제거한다(Research §5).

⚠ **몸통은 겹치지 않는다**(채보 시계 vs 루프 BGM). 그래서 "하나의 클래스로 합친다"는 방향은 애초에 성립하지 않고, 선택지는 **공통부를 어디에 두는가**뿐이다.

---

## 방안 넷

### 방안 A — 아무것도 안 한다. `RiftController`가 참조 둘을 든다
- 변경 0줄.
- ⚠ 중복 25줄이 남고, "음악에 맞추는 연출"이 생길 때마다 **참조 둘 + 우선순위 + `OnValidate` 경고**가 복제된다. 지금 rift 하나로 끝날 일이 아니라면 손해로 뒤집힌다.
- 남겨 두는 기준: 음악 연동 연출이 **rift 하나로 끝난다**고 확신할 때.

### 방안 B — 볼륨 추종만 별도 컴포넌트로 뽑는다 (`MusicVolumeFollower`)
- `[RequireComponent(typeof(AudioSource))]` + `volumeScale`. `OnEnable`에 구독, `OnDisable`에 해제, 볼륨 대입. **약 30줄.**
- 두 클래스에서 볼륨 코드 삭제 → **순감 약 20줄.** `ChartPlayer`가 `ApplyMusicVolume()`을 재생 직전에 부르던 것도 필요 없어진다(컴포넌트가 항상 최신값을 들고 있다).
- ⚠ **씬 작업이 는다** — `BattleScene`·`Tutorial`의 두 오디오 오브젝트에 컴포넌트를 하나씩 붙여야 하고, **빠뜨리면 볼륨 슬라이더가 그 소리에 안 닿는다(조용한 실패)**.
- ⚠ **②를 못 푼다.** 두 타입은 여전히 무관하다.

### 방안 C — 공통 베이스 클래스 `MusicPlayerBase` ✅ 추천
```
MusicPlayerBase (abstract)        ← AudioSource · 볼륨 추종 · SongSource · IsPlaying · abstract Bpm
├── ChartPlayer   : 채보 시계  (Bpm => ActiveChart?.bpm)
└── MusicPlayer   : 루프 BGM   (Bpm => 직렬화 bpm 필드)
```
- 중복 25줄이 한 곳으로 모이고, **`ChartPlayer`의 종료 순서 함정 주석도 한 곳에만** 남는다(Research §2).
- **②가 풀린다** — 연출은 `MusicPlayerBase` **한 칸**을 배선한다. 우선순위 규칙·`OnValidate` 경고·`FindObjectOfType` 함정이 전부 사라진다.
- 파생이 둘 다 **실재**한다(투기적 추상화가 아니다). `abstract`라 오브젝트에 직접 붙을 수도 없다.
- ⚠ 직렬화 필드가 베이스로 올라간다 → Unity는 **필드 이름**으로 저장하므로 값이 보존되지만(Research §6) 세 씬에서 확인해야 한다.
- 비용: 신규 파일 1개(약 45줄), 두 클래스에서 각각 약 20줄 삭제. **총량은 거의 그대로이고 개념이 둘 → 하나로 줄어든다.**

### 방안 D — `MusicPlayer`를 `ChartPlayer`에 흡수
- **기각.** `MusicPlayer` 헤더 주석이 이미 검토·기각한 방향이고(Research §4), `ChartPlayer.Play()`는 `patternHandler`가 없으면 에러를 찍고 반환하며 `PlayRoutine`은 `entries`를 전제하고 루프를 지원하지 않는다. 배경음을 넣으려면 **채보 재생 경로에 분기가 늘어난다** — 판정이 걸린 경로다.

---

## 결정: **방안 C** (사용자 확정)

목적 ②를 푸는 것은 C뿐이고, ①은 C가 B보다 더 깨끗하게 푼다(씬에 붙일 것이 늘지 않고 조용한 실패가 없다). 반대로 **"연출이 음악을 참조할 일이 rift 하나뿐"이라면 A가 정답**이다.


---

## 구현 단계 (방안 C 기준)

### Step 1 — `MusicPlayerBase.cs` 신규
`Assets/02. Scripts/Audio/MusicPlayerBase.cs`, 전역 네임스페이스(`MusicPlayer`와 같은 자리 — `ChartGen`이 전역을 참조하는 것은 이미 성립한다).

- [x] `public abstract class MusicPlayerBase : MonoBehaviour`
- [x] `[SerializeField] protected AudioSource audioSource;`
  - ⚠ **필드 이름을 `audioSource`로 유지한다.** `ChartPlayer`의 세 씬 배선이 그 이름으로 직렬화돼 있다.
  - `Awake`에서 `if (audioSource == null) audioSource = GetComponent<AudioSource>();`
    — `MusicPlayer`는 직렬화 참조 없이 `GetComponent`로 살았다(Research §6). 이 한 줄이 씬 수정을 면제한다.
- [x] `[Range(0f,1f)] [SerializeField] private float volumeScale = 1f;`
  - ⚠ **이름을 `volumeScale`로 유지**해야 `Tutorial`의 `TutorialMusic` 값(1)이 산다. `ChartPlayer`에는 없던 필드지만 기본 1이라 **동작이 안 바뀐다.**
- [x] `public AudioSource SongSource => audioSource;`
  - ⚠ **이름을 `SongSource`로 유지**한다. `FinaleSilhouetteDirector`가 그 이름을 쓴다(Research §3).
- [x] `public bool IsPlaying => audioSource != null && audioSource.isPlaying;`
- [x] `public abstract float Bpm { get; }` — **0 이하면 "모른다"**는 뜻으로 통일한다(소비자가 폴백을 쓸 근거).
- [x] 볼륨 추종: `subscribedSoundManager` 필드 + `protected virtual void OnEnable()` / `protected virtual void OnDisable()` + `HandleVolumeChanged` + `protected void ApplyMusicVolume()`.
  - `ChartPlayer`의 종료 순서 주석(구독 대상을 그대로 들고 있다가 그 대상에서 해제한다)을 **여기로 옮긴다.**
  - `ApplyMusicVolume`: `GetEffectiveVolume(Music) * volumeScale`.
- [x] `public abstract void Play();` / `public abstract void Stop();`
  - 둘 다 이미 공개 메서드로 있고 시그니처가 같다. 베이스에 올리면 `BattleSceneBootstrap`류가 베이스 타입으로도 조작할 수 있다.

### Step 2 — `ChartPlayer` 정리
- [x] `: MonoBehaviour` → `: MusicPlayerBase`.
- [x] **삭제**: `audioSource` 필드, `SongSource` 프로퍼티, `subscribedSoundManager`, `OnEnable`, `OnDisable`, `HandleVolumeChanged`, `ApplyMusicVolume`.
- [x] **추가**: `public override float Bpm => ActiveChart != null ? ActiveChart.bpm : 0f;`
- [x] `Play()`·`Stop()`에 `override` 부착. 본문은 한 줄도 안 고친다.
- [x] `PlayRoutine`의 `ApplyMusicVolume()` 호출은 **그대로 둔다**(베이스의 `protected`를 그대로 부른다 — 재생 직전 최신 볼륨 보장이 그 호출의 의도다).

### Step 3 — `MusicPlayer` 정리
- [x] `: MonoBehaviour` → `: MusicPlayerBase`. **클래스명·파일명·네임스페이스를 바꾸지 않는다**(씬이 guid로 참조한다, Research §6).
- [x] **삭제**: `source` 필드, `volumeScale`, `subscribed`, `OnEnable`/`OnDisable`의 볼륨 부분, `HandleVolumeChanged`, `ApplyVolume`, `[RequireComponent]`는 **유지**(여전히 같은 오브젝트의 `AudioSource`를 쓴다).
- [x] `Awake`: `base.Awake()` 호출 후 `audioSource.playOnAwake = false; audioSource.loop = true; audioSource.clip = clip;`
- [x] `protected override void OnEnable()`: `base.OnEnable();` → `if (playOnStart) Play();`
- [x] **추가**: `[Min(1f)] [SerializeField] private float bpm = 120f;` + `public override float Bpm => bpm;`
  - 툴팁: "이 클립의 템포. 연출이 읽는다 — 재생에는 쓰이지 않는다."
  - 이것이 `docs/Rift/`가 요구한 값이고, **템포가 클립의 주인 옆에 적힌다.**
- [x] `Play()`/`Stop()`에 `override` 부착.
- [x] 헤더 주석 갱신 — *"왜 `ChartPlayer`를 쓰지 않는가"*는 **여전히 유효하다**(몸통은 안 합쳤다). 한 줄 덧붙인다: 공통부(오디오 소스·볼륨 추종·템포 질의)는 `MusicPlayerBase`가 든다.

### Step 4 — `RiftController` 쪽 단순화 (`docs/Rift/Plan_Rift.md` 갱신)
- [x] 참조 필드 둘(`musicPlayer`/`chartPlayer`) → **`MusicPlayerBase musicSource` 한 칸**.
- [x] 우선순위 규칙 · `OnValidate` 둘 다 배선 경고 **삭제**.
- [x] `float Bpm => musicSource != null && musicSource.Bpm > 0f ? musicSource.Bpm : fallbackBpm;`
- [x] ⚠ **`FindObjectOfType`은 여전히 금지**다. `Tutorial`에는 재생하지 않는 `ChartPlayer`가 살아 있어(Research §3) 자동 탐색이 **들리지 않는 `Dreamer_Lv10`의 BPM**을 집는다 — 이 위험은 통합으로 사라지지 않는다.

### Step 5 — 씬 확인 (Unity MCP로 수행)
- [x] `Tutorial`의 `ChartPlayer`: 플레이모드에서 `debugChart`·`audioSource`·`patternHandler`·`enemyDirector`·`countdownDuration = 3`·`playOnStart = false` **전부 보존**. 상속된 `volumeScale = 1`이 새로 붙었으나 기본값 1이라 동작 불변(예전에는 암묵적 1이었다).
- [x] `Tutorial`의 `TutorialMusic`: `clip`(`Citadel (Short Loop 1).wav`)·`volumeScale = 1`·`playOnStart = true` 보존, 신규 `bpm = 120`(기본값). **`audioSource` 칸이 씬 YAML에 없는데 런타임에 해결됨** → 베이스 `Awake`의 `GetComponent` 폴백이 의도대로 동작한다.
- [x] `BattleScene`의 `ChartPlayer`: 씬 YAML의 키가 `debugChart`·`audioSource`·`patternHandler`·`enemyDirector`·`countdownDuration`·`playOnStart`로 **그대로**다(이름 기반 역직렬화라 `Tutorial`과 같은 결과). ⚠ **씬을 열어 확인하지는 않았다** — `Tutorial`이 미저장 상태(사용자의 Rift 배치)라 건드리지 않았다.
- [x] `_Recovery/0.unity`는 손대지 않았다.
- [x] 컴파일 에러 0건, 플레이모드 진입/종료 시 `Managed Reference missing`·null 경고 **없음**. (콘솔에 남은 경고 둘은 `Pattern.WarnUnusedSlots`의 기존 저작 경고로 무관하다.)

### Step 6 — 검증
- [x] `Tutorial` 플레이: BGM이 재생되고 `Loop = true`·`m_PlayOnAwake = false`가 런타임에 적용됨(우리 `Awake`가 돈 증거).
- [x] 볼륨 추종 동작: 씬 저장값 `0.6`이 런타임에 **`0.15`**(= `SoundManager`의 Music 실효 볼륨 × `volumeScale` 1)로 바뀌었다 — `ApplyMusicVolume`이 베이스에서 정상 동작한다.
- [x] `ChartPlayer`가 `playOnStart = false`라 재생되지 않는 기존 동작 유지(에러 없음).
- [ ] **`BattleScene` 실제 곡 재생** — 카운트다운 → 패턴 투입 → `OnSongEnded`. (씬을 열지 않아 미검증. `Tutorial`은 채보를 재생하지 않는다.)
- [ ] `FinaleSilhouetteDirector.audioPitchScale`(`SongSource` 사용) 동작.
- [ ] `BattleSceneBootstrap`의 `Play()`/`Stop()` 경로.
- [ ] 볼륨 슬라이더 UI로 `Music` 채널을 실제로 움직여 두 씬 모두 반영되는지.

> 남은 넷은 `BattleScene`을 열어야 한다. `Tutorial`에 미저장 변경(Rift 배치)이 있어 열지 않았다.
