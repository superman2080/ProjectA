using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChartGen
{
    /// <summary>SongChart를 오디오 재생 시각에 맞춰 순차적으로 PatternHandler.SetPattern에 흘려보내는 재생 글루.</summary>
    public class ChartPlayer : MonoBehaviour
    {
        [SerializeField] private SongChart debugChart;
        [SerializeField] private AudioSource audioSource;

        /// <summary>
        /// 곡이 흐르는 오디오 소스. <b>읽기 전용</b>이다 — 연출이 배속·볼륨을 만질 때
        /// <c>GetComponent</c>로 추측하지 않게 하려고 연다(같은 오브젝트에 있는 것은 배선의 우연이다).
        /// </summary>
        public AudioSource SongSource => audioSource;
        [SerializeField] private PatternHandler patternHandler;

        [Tooltip("전투 연출. 비우면 적 없이 패턴만 재생된다(기존 동작).")]
        [SerializeField] private EnemySpace.EnemyDirector enemyDirector;

        // 이 구간은 프리웜 창이자 인트로 연출 창이다. 어느 쪽으로 봐도 짧아져서 좋을 게 없어 하한을 타입으로 못박는다.
        [Min(3f)]
        [SerializeField] private float countdownDuration = 3f;
        [SerializeField] private bool playOnStart = false;

        /// <summary>곡이 끝난 순간(마지막 엔트리 소진 + 오디오 종료). 남은 적 소멸·스테이지 종료가 구독한다.</summary>
        public event System.Action OnSongEnded;

        /// <summary>
        /// 카운트다운이 시작된 순간. 인자는 곡이 시작되기까지 남은 시간(초)이다.
        ///
        /// <para>프리웜(<c>PrepareStage</c>)이 <b>끝난 뒤</b> 발행되므로, 구독자는 적이 이미 배치된 무대를 본다.
        /// 이 창 안에서 끝나는 연출(카메라 인트로 등)이 구독한다.</para>
        /// </summary>
        public event System.Action<float> OnCountdownStarted;

        private List<SongChartEntry> pendingEntries;
        private Coroutine playCoroutine;
        private bool songEndRaised;

        /// <summary>
        /// GameSession의 선택된 채보를 우선 사용하고, 없으면 debugChart를 사용한다.
        ///
        /// <para><b>공개인 이유</b>: 채점기(<c>ScoreDirector</c>)가 총 노트 수와 만점을 알려면 곡을 알아야 하는데,
        /// 이 판단(<c>SelectedChart ?? debugChart</c>)을 복제하면 <b>디버그 재생에서만 만점이 어긋난다</b>.
        /// 곡의 진실의 원천은 하나여야 한다.</para>
        /// </summary>
        public SongChart ActiveChart =>
            (GameSession.Instance != null && GameSession.Instance.SelectedChart != null)
                ? GameSession.Instance.SelectedChart
                : debugChart;

        private void Start()
        {
            if (playOnStart)
                Play();
        }

        // 구독한 대상을 그대로 들고 있다가 그 대상에서 해제한다. OnDisable에서 SoundManager.Instance를 다시 부르면
        // 종료 순서상 SoundManager가 먼저 죽었을 때 게터가 새 인스턴스를 만들어 씬에 미아 오브젝트를 남긴다.
        private SoundManager subscribedSoundManager;

        private void OnEnable()
        {
            subscribedSoundManager = SoundManager.Instance;

            if (subscribedSoundManager != null)
                subscribedSoundManager.OnVolumeChanged += HandleVolumeChanged;

            ApplyMusicVolume();
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
            if (channel == VolumeChannel.Music || channel == VolumeChannel.Master)
                ApplyMusicVolume();
        }

        private void ApplyMusicVolume()
        {
            if (audioSource != null)
                audioSource.volume = SoundManager.Instance.GetEffectiveVolume(VolumeChannel.Music);
        }

        [ContextMenu("Play")]
        public void Play()
        {
            if (ActiveChart == null)
            {
                Debug.LogError("[ChartPlayer] 재생할 채보가 없습니다. GameSession.SelectedChart 또는 debugChart를 설정하세요.", this);
                return;
            }
            if (audioSource == null)
            {
                Debug.LogError("[ChartPlayer] AudioSource가 연결되지 않았습니다.", this);
                return;
            }
            if (patternHandler == null)
            {
                Debug.LogError("[ChartPlayer] PatternHandler가 연결되지 않았습니다.", this);
                return;
            }

            if (playCoroutine != null)
                StopCoroutine(playCoroutine);

            playCoroutine = StartCoroutine(PlayRoutine());
        }

        [ContextMenu("Stop")]
        public void Stop()
        {
            if (playCoroutine != null)
            {
                StopCoroutine(playCoroutine);
                playCoroutine = null;
            }

            audioSource.Stop();
            pendingEntries?.Clear();

            if (patternHandler != null)
                patternHandler.ClearAllPatterns();
        }

        private IEnumerator PlayRoutine()
        {
            pendingEntries = ActiveChart.entries
                .Where(e => e.spawnTimes != null && e.spawnTimes.Length > 0)
                .OrderBy(e => e.spawnTimes[0])
                .ToList();

            songEndRaised = false;

            // 카운트다운이 곧 프리웜 창이다 — 곡 도중에는 Instantiate가 한 번도 일어나면 안 된다
            // (스키닝 메쉬 생성 한 프레임이 히치가 되고, 그게 곧 판정 손실이다).
            // 절단 세트는 EnemyDefinition이 소유하므로 rosterPool을 훑는 것만으로 프리웜이 끝난다.
            enemyDirector?.PrepareStage();

            if (countdownDuration > 0f)
            {
                OnCountdownStarted?.Invoke(countdownDuration);
                yield return new WaitForSeconds(countdownDuration);
            }

            audioSource.clip = ActiveChart.song;
            ApplyMusicVolume();
            audioSource.Play();

            playCoroutine = null;
        }

        private void Update()
        {
            if (pendingEntries == null) return;

            if (pendingEntries.Count == 0)
            {
                RaiseSongEndedIfFinished();
                return;
            }

            if (!audioSource.isPlaying) return;

            var next = pendingEntries[0];
            if (audioSource.time < next.spawnTimes[0]) return;

            float baseTime = next.spawnTimes[0];
            var relativeInputTimes = next.onsetTimes.Select(t => t - baseTime).ToArray();
            var relativeSpawnTimes = next.spawnTimes.Select(t => t - baseTime).ToArray();

            // cue를 SetPattern '직전에' 옆으로 밀어 넣는다 — 판정 계층(PatternHandler)은 적을 몰라야 하므로
            // 페이로드에 싣지 않는다. 디렉터는 OnPatternQueued에서 같은 FIFO 순서로 꺼낸다.
            enemyDirector?.EnqueueCue(next.enemyCue);
            patternHandler.SetPattern(next.template, relativeInputTimes, relativeSpawnTimes);

            pendingEntries.RemoveAt(0);
        }

        /// <summary>마지막 엔트리까지 소진되고 오디오도 끝나면 곡 종료를 알린다(1회).</summary>
        private void RaiseSongEndedIfFinished()
        {
            if (songEndRaised || audioSource.isPlaying) return;

            songEndRaised = true;
            enemyDirector?.DissolveAll();
            OnSongEnded?.Invoke();
        }
    }
}
