using ChartGen;
using NUnit.Framework;

public class BeatGridTests
{
    [Test]
    public void TimeToGridIndex_And_GridIndexToTime_AreInverse()
    {
        var grid = new BeatGrid(bpm: 120f, beatOffset: 0.1f, subdivisionsPerBeat: 2); // gridInterval = 0.25

        int index = grid.TimeToGridIndex(0.6f);
        Assert.AreEqual(2, index);

        float time = grid.GridIndexToTime(index);
        Assert.AreEqual(0.6f, time, 0.0001f);
    }

    [Test]
    public void Snap_SnapsToNearestGridPoint()
    {
        var grid = new BeatGrid(120f, 0f, 1); // gridInterval = 0.5
        var onsets = new[] { 0.05f, 0.48f, 1.02f };

        var snapped = BeatGrid.Snap(onsets, grid);

        Assert.AreEqual(3, snapped.Length);
        Assert.AreEqual(0f, snapped[0], 0.0001f);
        Assert.AreEqual(0.5f, snapped[1], 0.0001f);
        Assert.AreEqual(1.0f, snapped[2], 0.0001f);
    }

    [Test]
    public void Snap_DuplicateGridIndex_IsRemoved()
    {
        var grid = new BeatGrid(120f, 0f, 1); // gridInterval = 0.5
        var onsets = new[] { 0.05f, 0.1f }; // both snap to grid index 0

        var snapped = BeatGrid.Snap(onsets, grid);

        Assert.AreEqual(1, snapped.Length);
    }

    [TestCase(1, 1)]
    [TestCase(10, 1)]
    [TestCase(11, 2)]
    [TestCase(20, 2)]
    [TestCase(21, 4)]
    [TestCase(30, 4)]
    public void LevelToSubdivision_ReturnsExpectedTier(int level, int expectedSubdivision)
    {
        Assert.AreEqual(expectedSubdivision, BeatGrid.LevelToSubdivision(level));
    }
}
