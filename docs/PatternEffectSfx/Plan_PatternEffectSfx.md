# Plan — 패턴별 이펙트 사운드 (PatternEffectSfx)

근거: `docs/PatternEffectSfx/Research_PatternEffectSfx.md`

## 방침 한 줄

**소리는 이펙트 큐가 든다.** `PatternEffectCue`에 `AudioClip` 슬롯을 얹으면 시각(`EffectTiming` + `timeOffset`)·조건(`Success`/`Parry`/`Evade`)·개수·툴 타임라인이 **전부 공짜로 따라온다**. 새 시계도, 새 저작 화면도, 새 조건 판정도 만들지 않는다.

**`SfxTrigger.PatternImpact`는 폴백으로 남는다** — 큐를 저작하지 않은 패턴은 지금 그대로 공용 소리. 기존 채보 회귀 0.

---

## Step 1 — 큐에 소리 필드

- [x] `Pattern/Core/PatternEffectCue.cs`에 필드 3개 + 프로퍼티

```csharp
[Header("Sound")]
[Tooltip("이 큐가 낼 효과음. 비우면 무음.\n" +
         "⚠ 2D로 재생된다(앵커와 무관) — 임팩트음이 좌우로 흔들리면 판정 단서가 흐려진다.")]
[SerializeField] private AudioClip sfx;

[Range(0f, 1f)]
[SerializeField] private float sfxVolume = 1f;

[Min(0.01f)]
[SerializeField] private float sfxPitch = 1f;

public AudioClip Sfx => sfx;
public float SfxVolume => sfxVolume;
public float SfxPitch => Mathf.Max(sfxPitch, 0.01f);
```

- [x] **`IsUsable`을 넓힌다 — 이 한 줄이 이번 변경의 축이다**

```csharp
/// <summary>
/// 재생할 것이 있는가. 비면 예약 자체를 만들지 않는다(= "뒷구르기는 무연출"의 구현 전부).
///
/// <para><b>프리팹과 소리 중 하나만 있어도 성립한다</b> — 소리 전용 큐(앵커 없음)와
/// 그림 전용 큐가 같은 게이트를 통과한다. 이 값을 보는 곳이 예약·프리웜·툴 목록·툴 경고 전부라
/// <b>여기만 넓히면 나머지가 따라온다</b>.</para>
/// </summary>
public bool IsUsable => prefab != null || sfx != null;
```

- [x] `Label`도 폴백을 넓힌다 — 프리팹이 없으면 클립 이름을 쓴다(툴 목록에 "(비어 있음)"으로 뜨면 소리 큐를 못 고른다)

- [x] ⚠ **`Pattern`에 `impactSfx` 슬롯을 만들지 않는다.** 저작 지점이 둘로 갈리면 "이 패턴의 임팩트에 뭐가 나는가"를 두 화면에서 봐야 한다(Research §2-1)

---

## Step 2 — `SfxManager`에 클립 재생 경로

- [x] `Play(SfxTrigger)`의 몸통을 `PlayClip`으로 뽑고 오버로드를 얹는다. **트리거 경로의 동작은 1비트도 안 바뀐다**

```csharp
public void Play(SfxTrigger trigger)
{
    if (!catalogByTrigger.TryGetValue(trigger, out var entry)) return;
    PlayClip(entry.clip, entry.volume, entry.pitch);
}

/// <summary>
/// 카탈로그를 거치지 않고 클립을 직접 재생한다. <b>패턴이 소유한 소리</b>가 이 경로로 온다
/// (<c>PatternEffectCue</c>) — 키를 코드가 정하는 enum으로는 패턴 수만큼 늘릴 수 없기 때문.
/// </summary>
public void Play(AudioClip clip, float volume, float pitch) => PlayClip(clip, volume, pitch);

private void PlayClip(AudioClip clip, float volume, float pitch)
{
    if (clip == null) return;

    int index = GetFreeVoiceIndex();
    AudioSource voice = voices[index];

    // ⚠ baseVolumes에 기록해야 재생 도중 SoundManager 볼륨 변경이 반영된다(HandleVolumeChanged).
    baseVolumes[index] = volume;
    voice.clip = clip;
    voice.pitch = pitch;
    voice.volume = volume * SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Sfx);
    voice.Play();
}
```

- [x] `spatialBlend`는 안 건드린다(2D 유지, Research §5)

---

## Step 3 — `PatternEffectDirector.Fire` 순서 뒤집기

- [x] **⚠ 소리를 먼저 재생하고 그 다음에 앵커를 본다.** 지금은 `ResolveAnchor`가 null이면 즉시 리턴해서(`:296`) **소리 전용 큐가 통째로 죽는다**

```csharp
private void Fire(PatternEffectCue cue)
{
    // ⚠ 소리가 먼저다. 소리는 2D라 앵커가 필요 없는데, 앵커 가드를 앞에 두면
    // 배선이 비었을 때(또는 소리 전용 큐일 때) 소리까지 같이 죽는다.
    // ⚠ 히트스톱은 소리를 얼리지 않는다 — 오디오는 원래 timeScale의 지배를 안 받고(§7-3),
    // 타격감으로도 '멈추는 그 순간'에 울리는 것이 맞다. 재생 중 AudioSource를 멈추면
    // "정지"가 아니라 "소리가 끊겼다"로 읽힌다.
    if (cue.Sfx != null) SfxManager.Instance.Play(cue.Sfx, cue.SfxVolume, cue.SfxPitch);

    if (cue.Prefab == null) return;   // 소리 전용 큐는 여기서 끝

    Transform anchor = ResolveAnchor(cue.Anchor);
    if (anchor == null) return;       // 배선이 비면 그림만 조용히 빠진다

    // ... 이하 기존 그대로 ...
}
```

- [x] **⚠ 프리웜에 null 가드**(`:377~379`). `IsUsable`을 넓혔으므로 이제 `cue.Prefab == null`이 여기 도달한다 — **이번 변경의 유일한 회귀 지점이다**

```csharp
if (cue == null || !cue.IsUsable) continue;
if (cue.Prefab == null) continue;   // 소리는 풀이 필요 없다(SfxManager 보이스 풀이 감당)
```

---

## Step 4 — 폴백 이중 재생 막기

**막지 않으면**: 패턴이 임팩트 소리 큐를 저작해도 `EffectManager`의 `SfxTrigger.PatternImpact`가 같은 시각에 같이 울린다. 둘 다 임팩트 시각이라 **한 소리로 뭉쳐 들리고**, 저작자는 "내 클립이 이상하다"로 오진한다.

- [x] `Pattern`에 질의 하나

```csharp
/// <summary>
/// 이 패턴이 자기 소리를 들고 있는가. <c>EffectManager</c>의 공용 임팩트음(<c>SfxTrigger.PatternImpact</c>)이
/// <b>겹치지 않게</b> 물러나는 근거다 — 폴백은 "소리를 저작하지 않은 패턴"만 위한 것이다.
/// </summary>
public bool HasSfxCue
{
    get
    {
        if (effectCues == null) return false;
        foreach (var cue in effectCues)
            if (cue != null && cue.Sfx != null) return true;
        return false;
    }
}
```

- [x] `EffectManager.HandlePatternComplete`의 예약 조건에 한 줄

```csharp
// 패턴이 자기 소리를 들고 있으면 공용 임팩트음은 물러난다 — 안 그러면 같은 시각에 둘이 겹친다.
if (info.AllCorrect && (info.Template == null || !info.Template.HasSfxCue))
    pendingImpactSfx = info.ImpactTime();
```

- [x] ⚠ 조건이 `Impact` 타이밍인지까지는 안 본다. `PatternStart`에 칼 뽑는 소리만 저작해도 폴백이 죽지만, **"소리를 직접 설계한 패턴"이라는 사실 하나로 물러나는 것이 규칙으로 단순하다**. 임팩트음이 필요하면 그 큐를 만들면 된다

---

## Step 5 — 툴(`Tools/Pattern Effect Tool`)

- [x] `EstimateDuration`(`PatternEffectWindow.cs:273`) — 프리팹이 없으면 **클립 길이**를 쓴다. 지금은 0을 돌려줘 소리 전용 큐가 타임라인에 길이 0 막대로 뜬다

```csharp
if (cue.Prefab == null) return cue.Sfx != null ? cue.Sfx.length : 0f;
```

- [x] 큐 상세에 `Sound` 섹션(클립·볼륨·피치)을 "재생" 위에 그린다
- [x] 목록 행에 소리 뱃지(`♪`) — 그림 없는 큐가 목록에서 빈칸으로 보이면 안 된다
- [x] **⚠ `:521`의 "프리팹에 ParticleSystem이 없습니다" 경고를 소리 전용 큐에서 끈다**(오탐). 반대로 **프리팹도 소리도 없는 큐**는 기존 `:505` 경고가 그대로 잡는다
- [x] 프리뷰에서 소리는 **재생하지 않는다** — `ParticleSystem.Simulate`는 되감기가 되지만 오디오는 안 된다. 스크럽할 때마다 소리가 튀면 저작을 방해한다(타임라인 막대로 시각만 보여 준다)

---

## Step 6 — 검증

- [x] `Pattern/Tests/PatternEffectCueTests.cs`에 테스트 **5건**
  - 프리팹만 → `true` (기존 동작 유지)
  - **클립만 → `true`** (이번 변경의 핵심)
  - 둘 다 없음 → `false`
  - 소리 전용 큐의 `Label`이 클립 이름으로 폴백 (툴 목록에서 고를 수 있는가)
  - `SfxPitch`가 0으로 무너지지 않음
- [ ] ~~`HasSfxCue` 테스트 2건~~ — **불가.** `Pattern.Tests` asmdef는 `Pattern.Core`만 참조하는데 `Pattern`(ScriptableObject)은 Assembly-CSharp에 있어 asmdef가 참조할 수 없다. 테스트만을 위해 어셈블리 구조를 바꾸지 않는다 — `HasSfxCue`는 위에서 검증된 `Sfx` 프로퍼티를 도는 4줄 루프다
- [ ] 플레이 검증 — **미실행. Unity 에디터에서 직접 확인 필요**
  - 소리 전용 큐(앵커 미배선)가 **울리는가** — Step 3 순서 뒤집기의 유일한 증거
  - 같은 패턴에서 **소리가 두 번 안 울리는가** (Step 4)
  - 히트스톱이 걸린 임팩트에서 **소리가 정지 순간에 울리는가**(끊기지 않는가)
  - `Parry` 조건 소리가 **적이 막을 때만** 울리는가 / `Evade`(큐 없음)에서 무음인가
  - 소리 큐가 없는 기존 패턴이 예전 공용 임팩트음 그대로인가 (회귀)

---

## Step 7 — 문서

- [x] `CLAUDE.md` §7-4에 문단 추가 — "큐가 그림뿐 아니라 **소리**도 든다 / `IsUsable`이 둘 중 하나 / `Fire`는 소리가 먼저(앵커 가드보다 앞) / 히트스톱은 소리를 안 얼린다 / 소리 큐가 있으면 §7의 공용 `PatternImpact` 폴백이 물러난다"
- [x] `docs/!Guides/Guide_PatternEffectTool.md`에 소리 큐 저작 절 추가
- [x] 이 Plan의 체크박스 갱신

---

## 범위 밖 (의도적으로 안 한다)

| 항목 | 왜 |
|---|---|
| 3D(위치) 사운드 | 임팩트음이 앵커 따라 좌우로 흔들리면 판정 단서가 흐려진다. 리듬게임에서는 손해 |
| 소리 전용 풀 | `SfxManager`의 보이스 8개가 이미 동시 재생을 감당한다 |
| 히트스톱 중 소리 정지 | Research §4-4 — 오디오는 timeScale 밖이고, 끊으면 "정지"가 아니라 "글리치"로 읽힌다 |
| 툴 프리뷰의 소리 재생 | 스크럽 되감기가 오디오에는 없다. 매 프레임 소리가 튀면 저작을 방해한다 |
| `Pattern.impactSfx` 별도 슬롯 | 저작 지점이 둘로 갈린다(Research §2-1) |
| `SfxTrigger.PatternImpact` 제거 | 큐 없는 패턴의 폴백. 지우면 기존 채보가 무음이 된다 |
