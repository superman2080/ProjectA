using System.Collections.Generic;
using ChartGen;
using NUnit.Framework;

public class OnsetGrouperTests
{
    [Test]
    public void Group_CloseOnsets_FormOneGroup()
    {
        var grid = new BeatGrid(120f, 0f, 1); // gridInterval = 0.5
        var onsets = new List<float> { 0f, 0.5f, 1.0f };

        var groups = OnsetGrouper.Group(onsets, grid, 1);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(3, groups[0].Count);
    }

    [Test]
    public void Group_FarOnsets_FormSeparateGroups()
    {
        var grid = new BeatGrid(120f, 0f, 1); // gridInterval = 0.5
        var onsets = new List<float> { 0f, 0.5f, 5.0f };

        var groups = OnsetGrouper.Group(onsets, grid, 1);

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual(2, groups[0].Count);
        Assert.AreEqual(1, groups[1].Count);
    }

    [Test]
    public void GroupByBeats_OnBeatsOnEighthGrid_StayInOneGroup()
    {
        // 8분 격자에서 정박만 남은 온셋은 2칸 간격이다 — 칸 1개 기준이면 전부 갈라졌다(docs/ChartDensity §8의 회귀).
        var grid = new BeatGrid(120f, 0f, 2); // gridInterval = 0.25, 1박 = 0.5
        var onsets = new List<float> { 0f, 0.5f, 1.0f, 1.5f };

        Assert.AreEqual(4, OnsetGrouper.Group(onsets, grid, 1).Count);

        var groups = OnsetGrouper.GroupByBeats(onsets, grid, 2, 1);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(4, groups[0].Count);
    }

    [Test]
    public void GroupByBeats_QuarterGrid_MatchesStepGrouping()
    {
        // 4분 격자에서는 1박 = 1칸 — Lv1~10 결과가 바뀌지 않는다.
        var grid = new BeatGrid(120f, 0f, 1);
        var onsets = new List<float> { 0f, 0.5f, 1.5f, 2.0f, 4.0f };

        var byBeats = OnsetGrouper.GroupByBeats(onsets, grid, 1, 1);
        var bySteps = OnsetGrouper.Group(onsets, grid, 1);

        Assert.AreEqual(bySteps.Count, byBeats.Count);
        for (int i = 0; i < bySteps.Count; i++)
            CollectionAssert.AreEqual(bySteps[i], byBeats[i]);
    }

    [TestCase(1, 1, 1)]
    [TestCase(2, 1, 2)]
    [TestCase(4, 1, 4)]
    [TestCase(2, 2, 4)]
    public void GapSteps_ConvertsBeatsToSteps(int sub, int beats, int expected)
    {
        Assert.AreEqual(expected, OnsetGrouper.GapSteps(sub, beats));
    }

    [Test]
    public void Group_EmptyInput_ReturnsEmptyList()
    {
        var grid = new BeatGrid(120f, 0f, 1);

        var groups = OnsetGrouper.Group(new List<float>(), grid, 1);

        Assert.IsEmpty(groups);
    }
}
