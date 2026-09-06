using UnityEngine;

/// <summary>
/// 배경음 하나를 루프로 재생한다. <b>게임플레이를 모른다</b> — 언제 무엇이 흐를지는 씬 배치가 정한다.
///
/// <para><b>왜 <see cref="ChartPlayer"/>를 쓰지 않는가</b>: 그쪽 오디오는 <b>채보의 시계</b>다
/// (<c>audioSource.time</c>이 패턴 투입 시각을 정한다). 배경음은 판정과 아무 관계가 없으므로
/// 그 클래스에 얹으면 "곡이 아닌 곡"이 생겨 채보 재생 경로에 분기가 는다.</para>
///
/// <para><b>볼륨 규율은 <see cref="ChartPlayer"/>와 같다</b> — <see cref="SoundManager"/>의
/// <c>Music</c> 채널을 구독해 따라간다. 그래야 볼륨 슬라이더가 이 소리에도 닿는다.</para>
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    [Tooltip("재생할 배경음. 비우면 아무것도 안 한다(배선이 비면 조용히 비활성되는 기존 규율).")]
    [SerializeField] private AudioClip clip;

    [Tooltip("이 클립의 상대 음량. Music 채널 값에 곱해진다.")]
    [Range(0f, 1f)]
    [SerializeField] private float volumeScale = 1f;

    [SerializeField] private bool playOnStart = true;

    private AudioSource source;
    private SoundManager subscribed;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = clip;
    }

    void OnEnable()
    {
        subscribed = SoundManager.Instance;
        if (subscribed != null) subscribed.OnVolumeChanged += HandleVolumeChanged;

        ApplyVolume();
        if (playOnStart) Play();
    }

    void OnDisable()
    {
        if (subscribed == null) return;

        subscribed.OnVolumeChanged -= HandleVolumeChanged;
        subscribed = null;
    }

    public void Play()
    {
        if (source == null || source.clip == null || source.isPlaying) return;
        source.Play();
    }

    public void Stop()
    {
        if (source != null) source.Stop();
    }

    private void HandleVolumeChanged(VolumeChannel channel)
    {
        if (channel == VolumeChannel.Music || channel == VolumeChannel.Master) ApplyVolume();
    }

    private void ApplyVolume()
    {
        if (source == null || SoundManager.Instance == null) return;
        source.volume = SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Music) * volumeScale;
    }
}
