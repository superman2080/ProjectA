using System;
using System.Collections.Generic;
using System.Linq;

namespace ChartGen
{
    /// <summary>박 위치별 가중치. 같은 세기라면 가중치가 큰 자리가 오래 살아남는다.</summary>
    public readonly struct BeatWeights
    {
        public float BarHead { get; }
        public float Beat { get; }
        public float Eighth { get; }
        public float Sixteenth { get; }

        public BeatWeights(float barHead, float beat, float eighth, float sixteenth)
        {
            BarHead = barHead;
            Beat = beat;
            Eighth = eighth;
            Sixteenth = sixteenth;
        }

        public static BeatWeights Default => new BeatWeights(1.3f, 1.0f, 0.7f, 0.5f);
    }

    /// <summary>
    /// 스냅된 온셋을 목표 개수까지 솎아낸다. Unity API 비의존(docs/ChartDensity).
    ///
    /// <para><b>버리는 순서 = 세기 × 박 위치 가중.</b> 같은 점수면 이른 온셋이 남는다 — 결과가 결정적이어야
    /// 재굽기가 같은 채보를 낸다.</para>
    ///
    /// <para><b>⚠ 전역이 아니라 구간별로 솎는다.</b> 세기는 절대 RMS 증가량이라 전역 상위 N개를 고르면
    /// <b>조용한 구간이 통째로 사라진다</b>. 대신 곡 전체 비율 <c>r = 목표 / 후보</c>를 구간마다 똑같이 적용하고
    /// 반올림 오차를 다음 구간으로 넘긴다 — 빽빽한 곳은 빽빽하게, 성긴 곳은 성기게 남는다.</para>
    /// </summary>
    public static class OnsetThinner
    {
        public static float[] Thin(IReadOnlyList<Onset> snapped, BeatGrid grid, int subdivisionsPerBeat, int beatsPerBar,
                                   int targetCount, int segmentBars, BeatWeights weights)
        {
            int n = snapped?.Count ?? 0;
            if (n == 0 || targetCount <= 0) return Array.Empty<float>();
            if (targetCount >= n) return snapped.Select(o => o.Time).OrderBy(t => t).ToArray();

            int sub = Math.Max(subdivisionsPerBeat, 1);
            int stepsPerBar = sub * Math.Max(beatsPerBar, 1);
            int stepsPerSegment = stepsPerBar * Math.Max(segmentBars, 1);

            var items = snapped.Select((o, k) =>
            {
                int index = grid.TimeToGridIndex(o.Time);
                return new Item
                {
                    Time = o.Time,
                    Score = o.Strength * WeightOf(index, sub, stepsPerBar, weights),
                    Segment = FloorDiv(index, stepsPerSegment),
                };
            }).ToList();

            float ratio = (float)targetCount / n;
            var kept = new HashSet<Item>();
            float carry = 0f;

            foreach (var segment in items.GroupBy(it => it.Segment).OrderBy(g => g.Key))
            {
                var ranked = Rank(segment).ToList();
                float exact = ranked.Count * ratio + carry;
                int keep = Math.Max(0, Math.Min(ranked.Count, (int)Math.Floor(exact + 0.5f)));
                carry = exact - keep;

                for (int i = 0; i < keep; i++) kept.Add(ranked[i]);
            }

            // 구간 클램프 때문에 총합이 ±몇 개 어긋날 수 있다 — 전역 점수 순으로 맞춰 정확히 목표에 닿게 한다.
            if (kept.Count < targetCount)
            {
                foreach (var it in Rank(items.Where(it => !kept.Contains(it))).Take(targetCount - kept.Count))
                    kept.Add(it);
            }
            else if (kept.Count > targetCount)
            {
                foreach (var it in Rank(kept).Reverse().Take(kept.Count - targetCount).ToList())
                    kept.Remove(it);
            }

            return kept.Select(it => it.Time).OrderBy(t => t).ToArray();
        }

        /// <summary>격자 인덱스의 박 위치 가중치. 인덱스 0이 <c>beatOffset</c>(첫 박 = 마디 첫 박)이다.</summary>
        public static float WeightOf(int gridIndex, int subdivisionsPerBeat, int stepsPerBar, BeatWeights weights)
        {
            int sub = Math.Max(subdivisionsPerBeat, 1);
            int pos = Mod(gridIndex, Math.Max(stepsPerBar, 1));

            if (pos == 0) return weights.BarHead;
            if (pos % sub == 0) return weights.Beat;
            if (sub <= 2 || pos % (sub / 2) == 0) return weights.Eighth;
            return weights.Sixteenth;
        }

        private static IEnumerable<Item> Rank(IEnumerable<Item> items)
        {
            return items.OrderByDescending(it => it.Score).ThenBy(it => it.Time);
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

        private sealed class Item
        {
            public float Time;
            public float Score;
            public int Segment;
        }
    }
}
