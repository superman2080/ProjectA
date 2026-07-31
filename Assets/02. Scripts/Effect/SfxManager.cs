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
            catalogByTrigger[entry.trigger] = entry;

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
        if (!catalogByTrigger.TryGetValue(trigger, out var entry) || entry.clip == null)
            return;

        int index = GetFreeVoiceIndex();
        AudioSource voice = voices[index];

        baseVolumes[index] = entry.volume;
        voice.clip = entry.clip;
        voice.pitch = entry.pitch;
        voice.volume = entry.volume * SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Sfx);
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
