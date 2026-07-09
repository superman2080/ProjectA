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
    public void Group_EmptyInput_ReturnsEmptyList()
    {
        var grid = new BeatGrid(120f, 0f, 1);

        var groups = OnsetGrouper.Group(new List<float>(), grid, 1);

        Assert.IsEmpty(groups);
    }
}
