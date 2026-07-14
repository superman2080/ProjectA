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
        [SerializeField] private PatternHandler patternHandler;

        [SerializeField] private float countdownDuration = 3f;
        [SerializeField] private bool playOnStart = false;

        private List<SongChartEntry> pendingEntries;
        private Coroutine playCoroutine;

        /// <summary>GameSession의 선택된 채보를 우선 사용하고, 없으면 debugChart를 사용한다.</summary>
        private SongChart ActiveChart =>
            (GameSession.Instance != null && GameSession.Instance.SelectedChart != null)
                ? GameSession.Instance.SelectedChart
                : debugChart;

        private void Start()
        {
            if (playOnStart)
                Play();
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

            if (countdownDuration > 0f)
                yield return new WaitForSeconds(countdownDuration);

            audioSource.clip = ActiveChart.song;
            audioSource.Play();

            playCoroutine = null;
        }

        private void Update()
        {
            if (pendingEntries == null || pendingEntries.Count == 0) return;
            if (!audioSource.isPlaying) return;

            var next = pendingEntries[0];
            if (audioSource.time < next.spawnTimes[0]) return;

            float baseTime = next.spawnTimes[0];
            var relativeInputTimes = next.onsetTimes.Select(t => t - baseTime).ToArray();
            var relativeSpawnTimes = next.spawnTimes.Select(t => t - baseTime).ToArray();
            patternHandler.SetPattern(next.template, relativeInputTimes, relativeSpawnTimes);

            pendingEntries.RemoveAt(0);
        }
    }
}
