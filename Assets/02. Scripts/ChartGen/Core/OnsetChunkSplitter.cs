using System.Collections.Generic;
using System.Linq;

namespace ChartGen
{
    /// <summary>온셋 시퀀스를 사용 가능한 노드 크기로 라운드로빈 순환하며 청크 크기 목록으로 분할한다. Unity API 비의존.</summary>
    public static class OnsetChunkSplitter
    {
        /// <summary>
        /// 총 온셋 개수를 availableSizes를 라운드로빈 순환하며 청크 크기 목록으로 분할한다.
        /// - availableSizes가 null이거나 비어 있으면 [totalCount] 하나짜리 리스트 반환.
        /// - 다음 순환 크기가 남은 개수보다 크면 남은 개수 이하의 최대 사용 가능 크기로 대체.
        ///   대체할 크기도 없으면 남은 개수를 그대로 사용.
        /// </summary>
        public static List<int> Split(int totalCount, IReadOnlyList<int> availableSizes)
        {
            var result = new List<int>();

            if (totalCount <= 0)
                return result;

            if (availableSizes == null || availableSizes.Count == 0)
            {
                result.Add(totalCount);
                return result;
            }

            var sorted = availableSizes.OrderBy(x => x).ToList();
            int remaining = totalCount;
            int cycleIndex = 0;

            while (remaining > 0)
            {
                int desired = sorted[cycleIndex % sorted.Count];
                cycleIndex++;

                if (desired <= remaining)
                {
                    result.Add(desired);
                    remaining -= desired;
                }
                else
                {
                    // 남은 개수보다 작거나 같은 최대 크기로 대체
                    int fallback = sorted.LastOrDefault(s => s <= remaining);
                    result.Add(fallback > 0 ? fallback : remaining);
                    remaining = 0;
                }
            }

            return result;
        }
    }
}
