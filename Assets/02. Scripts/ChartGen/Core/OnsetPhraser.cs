using System;
using System.Collections.Generic;
using System.Linq;

namespace ChartGen
{
    /// <summary><see cref="OnsetPhraser.Assemble"/>의 결과.</summary>
    public sealed class PhraseResult
    {
        /// <summary>비지 않은 악구들. 각 악구 안은 시각 오름차순이고 크기는 언제나 <c>minSize</c> 이상이다. 리스트 인덱스가 곧 악구 번호다.</summary>
        public List<List<float>> Groups = new List<List<float>>();

        /// <summary>솎기 전 후보로 만든 악구 수(빈 것 포함).</summary>
        public int CandidatePhrases;

        /// <summary>1개만 남은 악구를 채우려고 되살린 후보 수.</summary>
        public int Added;

        /// <summary><c>minSize</c>를 못 채워 비운 온셋 수(후보가 1개뿐인 악구 포함).</summary>
        public int Removed;
    }

    /// <summary>
    /// 솎은 온셋을 <b>간격이 아니라 소속 악구로</b> 묶는다. Unity API 비의존(docs/ChartDensity 3차).
    ///
    /// <para><b>악구</b> = 솎기 <b>전</b> 후보를 <c>maxGapBeats</c>로 묶은 그룹이다. 음악의 쉼표만 경계가 된다.</para>
    ///
    /// <para><b>⚠ 솎은 뒤에 간격으로 묶으면 안 된다.</b> 낮은 레벨일수록 많이 솎아(Lv1은 3개 중 2개를 버린다)
    /// 남은 온셋 사이가 기준(1박)보다 벌어지고, 그룹 경계가 쉼표가 아니라 <b>솎기가 낸 구멍</b>에서 생긴다 —
    /// 그러면 거의 전부가 단독이 되어 1노트 금지로 버려진다(Lv1 목표 95 → 37).</para>
    ///
    /// <para>솎기(<see cref="OnsetThinner"/>)는 손대지 않는다 — 구간별 비율이 조용한 구간의 몫을 지키고 있어서다.
    /// 이 클래스는 그 <b>뒤</b>에 붙는다.</para>
    /// </summary>
    public static class OnsetPhraser
    {
        public static PhraseResult Assemble(IReadOnlyList<Onset> candidates, IReadOnlyList<float> kept, BeatGrid grid,
                                            int subdivisionsPerBeat, int beatsPerBar, int maxGapBeats, int minSize,
                                            int targetCount, BeatWeights weights)
        {
            var result = new PhraseResult();
            if (candidates == null || candidates.Count == 0) return result;
            minSize = Math.Max(minSize, 1);

            int sub = Math.Max(subdivisionsPerBeat, 1);
            int stepsPerBar = sub * Math.Max(beatsPerBar, 1);

            // ① 악구 — 솎기 전 후보를 박 단위 갭으로 묶는다.
            var phrases = OnsetGrouper.GroupByBeats(candidates.Select(o => o.Time).OrderBy(t => t).ToList(),
                                                    grid, sub, maxGapBeats);
            result.CandidatePhrases = phrases.Count;

            var strength = new Dictionary<int, float>();
            foreach (var o in candidates)
            {
                int idx = grid.TimeToGridIndex(o.Time);
                if (!strength.TryGetValue(idx, out float s) || o.Strength > s) strength[idx] = o.Strength;
            }

            var keptIndices = new HashSet<int>();
            if (kept != null)
                foreach (float t in kept) keptIndices.Add(grid.TimeToGridIndex(t));

            // ③의 판단 기준 — 지금까지 남긴 총수. 처음엔 솎기 결과 전체이고, 보정할 때마다 갱신된다(시간순이라 결정적).
            int total = keptIndices.Count;

            foreach (var phrase in phrases)
            {
                // ② 소속으로 모은다 — 간격을 보지 않는다.
                var inPhrase = phrase.Where(t => keptIndices.Contains(grid.TimeToGridIndex(t))).ToList();

                // ③ minSize 미만으로 남은 악구 보정: 목표 미달이면 같은 악구의 다음 후보로 채우고, 아니면 비운다.
                if (inPhrase.Count > 0 && inPhrase.Count < minSize)
                {
                    int need = minSize - inPhrase.Count;
                    if (phrase.Count >= minSize && total < targetCount)
                    {
                        var extras = phrase.Where(t => !inPhrase.Contains(t))
                            .OrderByDescending(t => Score(t, grid, sub, stepsPerBar, strength, weights))
                            .ThenBy(t => t)
                            .Take(need);
                        inPhrase.AddRange(extras);
                        inPhrase.Sort();
                        total += need;
                        result.Added += need;
                    }
                    else
                    {
                        total -= inPhrase.Count;
                        result.Removed += inPhrase.Count;
                        inPhrase.Clear();
                    }
                }

                if (inPhrase.Count > 0) result.Groups.Add(inPhrase);
            }

            return result;
        }

        private static float Score(float time, BeatGrid grid, int sub, int stepsPerBar, Dictionary<int, float> strength, BeatWeights weights)
        {
            int idx = grid.TimeToGridIndex(time);
            strength.TryGetValue(idx, out float s);
            return s * OnsetThinner.WeightOf(idx, sub, stepsPerBar, weights);
        }
    }
}
