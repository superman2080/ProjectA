using System.Collections.Generic;
using System.Linq;
using ChartGen;
using NUnit.Framework;

public class OnsetChunkSplitterTests
{
    [Test]
    public void Split_AvailableSizes1234_TotalCount9_SumsTo9AndAllInRange()
    {
        var sizes = new List<int> { 1, 2, 3, 4 };

        var chunks = OnsetChunkSplitter.Split(9, sizes);

        Assert.AreEqual(9, chunks.Sum());
        Assert.IsTrue(chunks.All(c => c >= 1 && c <= 4));
    }

    [Test]
    public void Split_RemainderSmallerThanNextCycleSize_FallsBackToLargestFit()
    {
        // availableSizes=[2,3,4], totalCount=5
        // 순환: 2→5에서 2 뺌(남은 3), 3→3에서 3 뺌(남은 0)
        var sizes = new List<int> { 2, 3, 4 };

        var chunks = OnsetChunkSplitter.Split(5, sizes);

        Assert.AreEqual(5, chunks.Sum());
        Assert.IsTrue(chunks.All(c => c >= 2 && c <= 4));
    }

    [Test]
    public void Split_SingleAvailableSize_RemainderUsedDirectly()
    {
        // availableSizes=[3], totalCount=7 → [3, 3, 1]
        // 남은 1 < 3이므로 fallback → 3 이하 최대 = 없음(3보다 작은 값 없음) → 남은 값 1 그대로
        var sizes = new List<int> { 3 };

        var chunks = OnsetChunkSplitter.Split(7, sizes);

        Assert.AreEqual(7, chunks.Sum());
        Assert.AreEqual(3, chunks[0]);
        Assert.AreEqual(3, chunks[1]);
        Assert.AreEqual(1, chunks[2]);
    }

    [Test]
    public void Split_TotalCountZero_ReturnsEmptyList()
    {
        var chunks = OnsetChunkSplitter.Split(0, new List<int> { 1, 2, 3 });

        Assert.IsEmpty(chunks);
    }

    [Test]
    public void Split_MinSize2_NeverProducesSingleChunk()
    {
        // 1이 사용 가능 크기에 있어도 순환에서 빠진다(docs/NoSingleNotePattern).
        var sizes = new List<int> { 1, 2, 3, 4, 5 };

        for (int total = 2; total <= 40; total++)
        {
            var chunks = OnsetChunkSplitter.Split(total, sizes, 2);

            Assert.AreEqual(total, chunks.Sum(), $"total={total}");
            Assert.IsTrue(chunks.All(c => c >= 2), $"total={total}: [{string.Join(",", chunks)}]");
        }
    }

    [Test]
    public void Split_MinSize2_RemainderOfOneIsAvoided()
    {
        // 예전 순환이면 [2,3,2,...] 끝에 1이 남을 자리 — 자투리를 미리 피해 모두 2 이상으로 끝난다.
        var sizes = new List<int> { 2, 3 };

        var chunks = OnsetChunkSplitter.Split(6, sizes, 2);

        Assert.AreEqual(6, chunks.Sum());
        Assert.IsTrue(chunks.All(c => c >= 2));
    }

    [Test]
    public void Split_MinSize2_TotalBelowMin_ReturnsEmptyList()
    {
        var chunks = OnsetChunkSplitter.Split(1, new List<int> { 2, 3 }, 2);

        Assert.IsEmpty(chunks);
    }

    [Test]
    public void Split_MinSize2_OnlySizeOneAvailable_ReturnsWholeGroup()
    {
        // 쓸 수 있는 크기가 없으면 그룹 통째로 하나(템플릿 미배정으로 남는다) — 1로 쪼개지 않는다.
        var chunks = OnsetChunkSplitter.Split(3, new List<int> { 1 }, 2);

        Assert.AreEqual(1, chunks.Count);
        Assert.AreEqual(3, chunks[0]);
    }

    [Test]
    public void Split_EmptyAvailableSizes_ReturnsSingleChunk()
    {
        var chunks = OnsetChunkSplitter.Split(5, new List<int>());

        Assert.AreEqual(1, chunks.Count);
        Assert.AreEqual(5, chunks[0]);
    }
}
