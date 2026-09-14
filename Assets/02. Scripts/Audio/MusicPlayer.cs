using UnityEngine;

/// <summary>
/// 배경음 하나를 루프로 재생한다. <b>게임플레이를 모른다</b> — 언제 무엇이 흐를지는 씬 배치가 정한다.
///
/// <para><b>왜 <see cref="ChartGen.ChartPlayer"/>를 쓰지 않는가</b>: 그쪽 오디오는 <b>채보의 시계</b>다
/// (<c>audioSource.time</c>이 패턴 투입 시각을 정한다). 배경음은 판정과 아무 관계가 없으므로
/// 그 클래스에 얹으면 "곡이 아닌 곡"이 생겨 채보 재생 경로에 분기가 는다.</para>
///
/// <para><b>겹치는 것만 <see cref="MusicPlayerBase"/>가 든다</b> — 오디오 소스 · 볼륨 추종 · 템포 질의.
/// 몸통(루프 여부·클립의 출처)은 여기 남는다. 그래서 위 문단은 여전히 유효하다.</para>
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MusicPlayerBase
{
    [Tooltip("재생할 배경음. 비우면 아무것도 안 한다(배선이 비면 조용히 비활성되는 기존 규율).")]
    [SerializeField] private AudioClip clip;

    [Tooltip("이 클립의 템포. 연출(균열 맥박 등)이 읽는다 - 재생에는 쓰이지 않는다.")]
    [Min(1f)]
    [SerializeField] private float bpm = 120f;

    [SerializeField] private bool playOnStart = true;

    public override float Bpm => bpm;

    protected override void Awake()
    {
        base.Awake();

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.clip = clip;
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        if (playOnStart) Play();
    }

    public override void Play()
    {
        if (audioSource == null || audioSource.clip == null || audioSource.isPlaying) return;
        audioSource.Play();
    }

    public override void Stop()
    {
        if (audioSource != null) audioSource.Stop();
    }
}
