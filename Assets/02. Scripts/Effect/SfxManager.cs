using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 효과음 재생 전담. 카탈로그(트리거→클립)와 동시 재생용 AudioSource 풀만 갖는다.
/// <see cref="PatternHandler"/> 등 특정 게임플레이 시스템을 전혀 모른다 — 어느 씬에 놓아도 그대로 동작해야
/// 하기 때문(다른 씬에서도 재사용). 요청은 항상 외부(EffectManager 등)가 <see cref="Play"/>를 호출해서 온다.
/// 모든 씬에서 동작해야 하므로 Singleton&lt;T&gt;로 DontDestroyOnLoad 유지.
/// </summary>
public class SfxManager : Singleton<SfxManager>
{
    [SerializeField] private List<SfxEntry> catalog = new List<SfxEntry>();
    [Tooltip("동시에 겹쳐 재생 가능한 보이스 수. 콤보 등으로 효과음이 겹칠 때 서로 끊기지 않게 한다.")]
    [SerializeField] private int voiceCount = 8;

    private readonly Dictionary<SfxTrigger, SfxEntry> catalogByTrigger = new Dictionary<SfxTrigger, SfxEntry>();
    private readonly List<AudioSource> voices = new List<AudioSource>();
    // 각 보이스가 마지막으로 재생을 시작한 엔트리의 카탈로그 볼륨(SoundManager 배율 적용 전).
    // 재생 도중 SoundManager 볼륨이 바뀌어도 이 값을 기준으로 다시 곱해 실시간 반영한다.
    private readonly List<float> baseVolumes = new List<float>();

    protected override bool DontDestroy => true;

    protected override void Awake()
    {
        base.Awake();

        foreach (var entry in catalog)
        {
            if (entry == null) continue;   // class라 리스트에 빈 칸이 생길 수 있다
            catalogByTrigger[entry.trigger] = entry;
        }

        EnsureVoices();
    }

    /// <summary>
    /// 보이스 풀을 채운다. <b>Awake가 아니라 사용 시점에도 부른다</b> —
    /// 플레이 도중 스크립트가 리컴파일되면 직렬화 대상이 아닌 이 리스트만 비워진 채
    /// 오브젝트가 살아남고 <c>Awake</c>는 다시 돌지 않는다. 그 상태로 재생하면
    /// <c>voices[index]</c>가 범위를 벗어난다(에디터에서만 나는 증상이라 더 늦게 발견된다).
    /// </summary>
    private void EnsureVoices()
    {
        if (voices.Count > 0) return;

        for (int i = 0; i < voiceCount; i++)
        {
            var go = new GameObject($"Voice {i + 1}");
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            voices.Add(source);
            baseVolumes.Add(0f);
        }
    }

    // 구독한 대상을 그대로 들고 있다가 그 대상에서 해제한다. OnDisable에서 SoundManager.Instance를 다시 부르면
    // 종료 순서상 SoundManager가 먼저 죽었을 때 게터가 새 인스턴스를 만들어 씬에 미아 오브젝트를 남긴다.
    private SoundManager subscribedSoundManager;

    private void OnEnable()
    {
        subscribedSoundManager = SoundManager.Instance;

        if (subscribedSoundManager != null)
            subscribedSoundManager.OnVolumeChanged += HandleVolumeChanged;
    }

    private void OnDisable()
    {
        if (subscribedSoundManager == null)
            return;

        subscribedSoundManager.OnVolumeChanged -= HandleVolumeChanged;
        subscribedSoundManager = null;
    }

    private void HandleVolumeChanged(VolumeChannel channel)
    {
        if (channel != VolumeChannel.Sfx && channel != VolumeChannel.Master)
            return;

        float effective = SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Sfx);

        for (int i = 0; i < voices.Count; i++)
            voices[i].volume = baseVolumes[i] * effective;
    }

    /// <summary>트리거에 매핑된 클립을 재생한다. 매핑이 없거나 clip이 비어 있으면 아무것도 하지 않는다.</summary>
    public void Play(SfxTrigger trigger)
    {
        if (!catalogByTrigger.TryGetValue(trigger, out var entry) || entry == null)
            return;

        // ⚠ entry.pitch가 아니라 Pitch다 — 0으로 저장된 옛 행을 1로 구제한다(피치 0 = 무음).
        PlayClip(entry.clip, entry.volume, entry.Pitch);
    }

    /// <summary>
    /// 카탈로그를 거치지 않고 클립을 직접 재생한다. <b>패턴이 소유한 소리</b>가 이 경로로 온다
    /// (<c>PatternEffectCue.Sfx</c>) — 키를 코드가 정하는 enum으로는 패턴 수만큼 늘릴 수 없기 때문
    /// (<c>SliceSet</c>이 enum 카탈로그를 안 두는 것과 같은 지점).
    /// </summary>
    public void Play(AudioClip clip, float volume, float pitch) => PlayClip(clip, volume, pitch);

    private void PlayClip(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;

        EnsureVoices();
        if (voices.Count == 0) return;

        int index = GetFreeVoiceIndex();
        AudioSource voice = voices[index];

        // ⚠ baseVolumes에 기록해야 재생 도중 SoundManager 볼륨 변경이 반영된다(HandleVolumeChanged).
        baseVolumes[index] = volume;
        voice.clip = clip;
        voice.pitch = pitch;
        voice.volume = volume * SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Sfx);
        voice.Play();
    }

    private int GetFreeVoiceIndex()
    {
        int oldestIndex = 0;

        for (int i = 0; i < voices.Count; i++)
        {
            if (!voices[i].isPlaying)
                return i;

            if (voices[i].time > voices[oldestIndex].time)
                oldestIndex = i;
        }

        // 유휴 보이스가 없으면 가장 오래 재생된(=끝나가는) 보이스를 가로챈다.
        return oldestIndex;
    }
}
