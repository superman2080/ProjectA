using UnityEngine;

/// <summary>
/// 음악을 내는 것들의 공통부. <b>"이 씬에서 음악을 내는 것"이라는 하나의 타입</b>이 존재하게 하는 것이
/// 이 클래스의 일이고, 그 위에 파생이 둘 있다 — <see cref="ChartGen.ChartPlayer"/>(채보의 시계)와
/// <c>MusicPlayer</c>(루프 배경음).
///
/// <para><b>몸통을 합치지는 않았다.</b> 채보 재생과 배경음 루프는 겹치는 코드가 없다(클립의 출처·루프 여부·
/// 카운트다운·엔트리 투입이 전부 다르다). 여기 모인 것은 <b>두 곳에 글자 단위로 중복돼 있던 것</b>뿐이다 —
/// 오디오 소스 · 볼륨 추종 · 재생 질의.</para>
///
/// <para><b>연출은 파생이 무엇인지 몰라도 된다.</b> 음악에 맞춰 무언가 하는 연출(균열 맥박 등)이
/// 이 타입 <b>한 칸</b>을 배선하면 끝이다. 씬마다 어느 파생이 사는지에 따라 참조 필드를 둘로 나누고
/// 우선순위 규칙을 두던 포크가 여기서 사라진다.</para>
///
/// <para><b>⚠ <see cref="Bpm"/>은 0 이하가 "모른다"는 뜻이다.</b> 소비자가 폴백을 쓸 근거이며,
/// 0으로 주기를 계산하면 연출이 한 값에 굳는다.</para>
///
/// <para><b>⚠ 직렬화 필드 이름을 바꾸지 않는다.</b> <c>audioSource</c>·<c>volumeScale</c>과
/// <see cref="SongSource"/>는 씬 배선과 기존 소비자가 그 이름으로 참조한다(Unity는 상속 사슬을 훑어
/// 필드 <b>이름</b>으로 직렬화하므로, 이름이 같은 동안에만 값이 보존된다).</para>
/// </summary>
public abstract class MusicPlayerBase : MonoBehaviour
{
    [Tooltip("소리가 나오는 AudioSource. 비우면 같은 오브젝트의 것을 쓴다.")]
    [SerializeField] protected AudioSource audioSource;

    [Tooltip("이 클립의 상대 음량. Music 채널 값에 곱해진다.")]
    [Range(0f, 1f)]
    [SerializeField] private float volumeScale = 1f;

    /// <summary>
    /// 음악이 흐르는 오디오 소스. <b>읽기 전용</b>이다 — 연출이 배속·볼륨을 만질 때
    /// <c>GetComponent</c>로 추측하지 않게 하려고 연다(같은 오브젝트에 있는 것은 배선의 우연이다).
    /// </summary>
    public AudioSource SongSource => audioSource;

    /// <summary>지금 소리가 나고 있는가.</summary>
    public bool IsPlaying => audioSource != null && audioSource.isPlaying;

    /// <summary>지금 흐르는 음악의 템포. <b>0 이하면 "모른다"</b>는 뜻이다.</summary>
    public abstract float Bpm { get; }

    public abstract void Play();

    public abstract void Stop();

    // 구독한 대상을 그대로 들고 있다가 그 대상에서 해제한다. OnDisable에서 SoundManager.Instance를 다시 부르면
    // 종료 순서상 SoundManager가 먼저 죽었을 때 게터가 새 인스턴스를 만들어 씬에 미아 오브젝트를 남긴다.
    private SoundManager subscribedSoundManager;

    protected virtual void Awake()
    {
        // 직렬화 참조가 원칙이지만, 같은 오브젝트의 AudioSource로 살아 온 배선(MusicPlayer)이 있어 폴백을 둔다.
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    protected virtual void OnEnable()
    {
        subscribedSoundManager = SoundManager.Instance;

        if (subscribedSoundManager != null)
            subscribedSoundManager.OnVolumeChanged += HandleVolumeChanged;

        ApplyMusicVolume();
    }

    protected virtual void OnDisable()
    {
        if (subscribedSoundManager == null)
            return;

        subscribedSoundManager.OnVolumeChanged -= HandleVolumeChanged;
        subscribedSoundManager = null;
    }

    private void HandleVolumeChanged(VolumeChannel channel)
    {
        if (channel == VolumeChannel.Music || channel == VolumeChannel.Master)
            ApplyMusicVolume();
    }

    /// <summary>
    /// 파생이 곱하는 추가 계수(기본 1). <b>페이드는 <c>audioSource.volume</c>에 직접 대입할 수 없다</b> —
    /// 여기가 볼륨의 주인이라 페이드 도중 <c>OnVolumeChanged</c>가 오면 값이 되돌아간다.
    /// 계수로 두면 채널 볼륨과 페이드가 서로를 덮지 않는다.
    /// </summary>
    protected virtual float VolumeFactor => 1f;

    protected void ApplyMusicVolume()
    {
        if (audioSource == null || SoundManager.Instance == null)
            return;

        audioSource.volume = SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Music) * volumeScale * VolumeFactor;
    }
}
