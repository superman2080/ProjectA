using System;
using System.Collections.Generic;

namespace ChartGen
{
    /// <summary>그리드 스냅된 온셋들을 그리드 스텝 간격 기준으로 그룹핑한다. Unity API 비의존.</summary>
    public static class OnsetGrouper
    {
        public static List<List<float>> Group(IReadOnlyList<float> snappedOnsetTimes, BeatGrid grid, int maxGroupGapSteps = 1)
        {
            var groups = new List<List<float>>();
            if (snappedOnsetTimes == null || snappedOnsetTimes.Count == 0)
                return groups;

            var currentGroup = new List<float> { snappedOnsetTimes[0] };
            int previousIndex = grid.TimeToGridIndex(snappedOnsetTimes[0]);

            for (int i = 1; i < snappedOnsetTimes.Count; i++)
            {
                int index = grid.TimeToGridIndex(snappedOnsetTimes[i]);
                if (index - previousIndex <= maxGroupGapSteps)
                {
                    currentGroup.Add(snappedOnsetTimes[i]);
                }
                else
                {
                    groups.Add(currentGroup);
                    currentGroup = new List<float> { snappedOnsetTimes[i] };
                }

                previousIndex = index;
            }

            groups.Add(currentGroup);
            return groups;
        }

        /// <summary>
        /// 박 수를 격자 칸 수로 환산한다. 굽기와 툴의 경계 경고가 <b>같은 값</b>을 쓰도록 여기 하나에 둔다(docs/ChartDensity D9).
        /// </summary>
        public static int GapSteps(int subdivisionsPerBeat, int maxGapBeats)
        {
            return Math.Max(subdivisionsPerBeat, 1) * Math.Max(maxGapBeats, 1);
        }

        /// <summary>
        /// 그룹 기준을 <b>박</b>으로 잰다. 칸 단위면 해상도가 바뀔 때마다(4분 → 8분 → 16분) 같은 음악에 대한
        /// 기준이 절반으로 줄어, 솎은 뒤 단독 온셋이 쏟아진다(Lv11에서 노트가 절반으로 줄었다 — docs/ChartDensity §8).
        /// 4분 격자에서는 1박 = 1칸이라 <see cref="Group"/>과 결과가 같다.
        /// </summary>
        public static List<List<float>> GroupByBeats(IReadOnlyList<float> snappedOnsetTimes, BeatGrid grid,
                                                     int subdivisionsPerBeat, int maxGapBeats = 1)
        {
            return Group(snappedOnsetTimes, grid, GapSteps(subdivisionsPerBeat, maxGapBeats));
        }
    }
}
