using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChartGen
{
    /// <summary>SongChart를 오디오 재생 시각에 맞춰 순차적으로 PatternHandler.SetPattern에 흘려보내는 재생 글루.</summary>
    public class ChartPlayer : MonoBehaviour
    {
        [SerializeField] private SongChart chart;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private PatternHandler patternHandler;

        private List<SongChartEntry> pendingEntries;

        private void Start()
        {
            if (chart == null || audioSource == null || patternHandler == null) return;

            audioSource.clip = chart.song;
            audioSource.Play();

            pendingEntries = chart.entries
                .OrderBy(entry => entry.spawnTimes[0])
                .ToList();
        }

        private void Update()
        {
            if (pendingEntries == null || pendingEntries.Count == 0) return;

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
