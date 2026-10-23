using System;
using System.Collections.Generic;

namespace ChartGen
{
    /// <summary>BPM/오프셋 기반 비트 그리드. 온셋 시각을 그리드에 스냅(quantize)하고, 레벨을 비트 분할 단위에 매핑한다. Unity API 비의존.</summary>
    public readonly struct BeatGrid
    {
        public float GridInterval { get; }
        private readonly float beatOffset;

        public BeatGrid(float bpm, float beatOffset, int subdivisionsPerBeat)
        {
            this.beatOffset = beatOffset;
            GridInterval = 60f / bpm / subdivisionsPerBeat;
        }

        public int TimeToGridIndex(float time)
        {
            return (int)Math.Round((time - beatOffset) / GridInterval, MidpointRounding.AwayFromZero);
        }

        public float GridIndexToTime(int index)
        {
            return beatOffset + index * GridInterval;
        }

        /// <summary>레벨(1~30)을 허용되는 비트 분할 단위로 매핑한다: 1~tier1Max=4분음표, ~tier2Max=8분음표, 그 이상=16분음표.</summary>
        public static int LevelToSubdivision(int level, int tier1Max = 10, int tier2Max = 20)
        {
            if (level <= tier1Max) return 1;
            if (level <= tier2Max) return 2;
            return 4;
        }

        /// <summary>온셋 시각들을 그리드에 스냅하고, 같은 그리드 지점으로 스냅된 중복을 제거해 오름차순으로 반환한다.</summary>
        public static float[] Snap(IReadOnlyList<float> onsetTimes, BeatGrid grid)
        {
            var seenIndices = new HashSet<int>();
            var result = new List<float>();

            foreach (var onset in onsetTimes)
            {
                int gridIndex = grid.TimeToGridIndex(onset);
                if (seenIndices.Add(gridIndex))
                    result.Add(grid.GridIndexToTime(gridIndex));
            }

            result.Sort();
            return result.ToArray();
        }

        /// <summary>
        /// <see cref="Snap"/>과 같되 칸당 <b>가장 센</b> 온셋을 남기고 세기를 보존한다(<see cref="Snap"/>은 먼저 온 것을 남긴다).
        /// 시각은 격자에 맞춰지고 결과는 오름차순이다. 세기가 같으면 먼저 온 것을 남긴다.
        /// </summary>
        public static Onset[] SnapStrongest(IReadOnlyList<Onset> onsets, BeatGrid grid)
        {
            var best = new Dictionary<int, float>();
            foreach (var onset in onsets)
            {
                int gridIndex = grid.TimeToGridIndex(onset.Time);
                if (!best.TryGetValue(gridIndex, out float strength) || onset.Strength > strength)
                    best[gridIndex] = onset.Strength;
            }

            var indices = new List<int>(best.Keys);
            indices.Sort();

            var result = new Onset[indices.Count];
            for (int i = 0; i < indices.Count; i++)
                result[i] = new Onset(grid.GridIndexToTime(indices[i]), best[indices[i]]);
            return result;
        }
    }
}
