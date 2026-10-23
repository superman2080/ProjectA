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
        /// - <paramref name="minSize"/> 미만 청크는 만들지 않는다(docs/NoSingleNotePattern).
        ///   그 크기는 순환에서 빠지고, 청크를 떼고 나면 <c>minSize</c> 미만 자투리가 남는 선택은 건너뛴다
        ///   — 미리 피하므로 뒤에서 빌려 올 일이 없다. totalCount 자체가 <c>minSize</c> 미만이면 빈 리스트(호출부가 버린다).
        /// </summary>
        public static List<int> Split(int totalCount, IReadOnlyList<int> availableSizes, int minSize = 1)
        {
            var result = new List<int>();
            if (minSize < 1) minSize = 1;

            if (totalCount <= 0 || totalCount < minSize)
                return result;

            var sorted = availableSizes == null
                ? new List<int>()
                : availableSizes.Where(s => s >= minSize).Distinct().OrderBy(x => x).ToList();

            if (sorted.Count == 0)
            {
                result.Add(totalCount);
                return result;
            }

            int remaining = totalCount;
            int cycleIndex = 0;

            while (remaining > 0)
            {
                int desired = sorted[cycleIndex % sorted.Count];
                cycleIndex++;

                if (Fits(desired, remaining, minSize))
                {
                    result.Add(desired);
                    remaining -= desired;
                    continue;
                }

                // 남은 개수 이하이면서 minSize 미만 자투리를 남기지 않는 최대 크기로 대체
                int fallback = sorted.LastOrDefault(s => Fits(s, remaining, minSize));
                if (fallback > 0)
                {
                    result.Add(fallback);
                    remaining -= fallback;
                }
                else
                {
                    // 맞는 크기가 없다 — 남은 개수 그대로(템플릿 미배정으로 남아 호출부가 경고한다).
                    result.Add(remaining);
                    remaining = 0;
                }
            }

            return result;
        }

        private static bool Fits(int size, int remaining, int minSize)
        {
            if (size > remaining) return false;
            int left = remaining - size;
            return left == 0 || left >= minSize;
        }
    }
}
