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
    }
}
