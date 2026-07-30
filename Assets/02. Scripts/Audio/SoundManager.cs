using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>볼륨 채널 키. 어떤 시스템이 이 채널을 쓰는지 SoundManager는 모른다 — 그냥 값 저장소의 색인.</summary>
public enum VolumeChannel
{
    Master,
    Sfx,
    Music,
}

/// <summary>
/// 채널 하나의 초기 볼륨. "채널에 얼마의 기본값을" 인스펙터에서 지정한다.
/// 목록에 없는 채널은 1.0(무감쇠) 취급.
/// </summary>
[Serializable]
public struct VolumeChannelEntry
{
    public VolumeChannel channel;
    [Range(0f, 1f)] public float volume;
}

/// <summary>
/// 볼륨만 담당하는 전역 저장소. 재생 로직도, 어떤 시스템이 어떤 채널을 쓰는지도 모른다(관심사 분리).
/// 모든 씬에서 동작해야 하므로 Singleton&lt;T&gt;로 DontDestroyOnLoad 유지. 소비자는 <see cref="GetEffectiveVolume"/>
/// 하나만 호출하면 되고, 새 채널이 필요해지면 <see cref="VolumeChannel"/>에 값만 추가하면 된다(소비자 API 불변).
/// </summary>
public class SoundManager : Singleton<SoundManager>
{
    [SerializeField] private List<VolumeChannelEntry> initialVolumes = new List<VolumeChannelEntry>
    {
        new VolumeChannelEntry { channel = VolumeChannel.Master, volume = 1f },
        new VolumeChannelEntry { channel = VolumeChannel.Sfx, volume = 1f },
        new VolumeChannelEntry { channel = VolumeChannel.Music, volume = 1f },
    };

    private readonly Dictionary<VolumeChannel, float> volumes = new Dictionary<VolumeChannel, float>();

    public event Action<VolumeChannel> OnVolumeChanged;

    protected override bool DontDestroy => true;

    protected override void Awake()
    {
        base.Awake();

        foreach (var entry in initialVolumes)
            volumes[entry.channel] = Mathf.Clamp01(entry.volume);
    }

    // 인스펙터에서 initialVolumes 슬라이더를 직접 움직일 때(플레이 모드 라이브 튜닝 포함) 호출된다.
    // SetVolume()을 거치지 않는 경로라 여기서 직접 동기화하고 이벤트를 발행해야 실시간 반영된다.
    private void OnValidate()
    {
        foreach (var entry in initialVolumes)
        {
            float clamped = Mathf.Clamp01(entry.volume);

            if (volumes.TryGetValue(entry.channel, out float current) && Mathf.Approximately(current, clamped))
                continue;

            volumes[entry.channel] = clamped;
            OnVolumeChanged?.Invoke(entry.channel);
        }
    }

    /// <summary>채널 자체의 볼륨(0~1). 목록에 없으면 1.0(무감쇠).</summary>
    public float GetVolume(VolumeChannel channel)
    {
        return volumes.TryGetValue(channel, out float value) ? value : 1f;
    }

    public void SetVolume(VolumeChannel channel, float value)
    {
        volumes[channel] = Mathf.Clamp01(value);
        OnVolumeChanged?.Invoke(channel);
    }

    /// <summary>Master가 곱해진 실제 재생 볼륨. 소비자는 이 메서드 하나만 호출하면 된다.</summary>
    public float GetEffectiveVolume(VolumeChannel channel)
    {
        float master = GetVolume(VolumeChannel.Master);
        return channel == VolumeChannel.Master ? master : GetVolume(channel) * master;
    }
}
