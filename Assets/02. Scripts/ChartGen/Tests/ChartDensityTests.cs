using ChartGen;
using NUnit.Framework;

public class ChartDensityTests
{
    [Test]
    public void TargetNps_IncreasesMonotonicallyWithoutSteps()
    {
        float prev = ChartDensity.TargetNps(1, 0.8f, 5.0f);
        float uniformStep = (5.0f - 0.8f) / (ChartDensity.MaxLevel - ChartDensity.MinLevel);

        for (int level = 2; level <= ChartDensity.MaxLevel; level++)
        {
            float nps = ChartDensity.TargetNps(level, 0.8f, 5.0f);
            Assert.Greater(nps, prev, $"level {level}");
            // 10→11·20→21에도 계단이 없다 — 모든 레벨 간격이 같다.
            Assert.AreEqual(uniformStep, nps - prev, 0.0001f, $"level {level}");
            prev = nps;
        }
    }

    [Test]
    public void TargetNps_EndpointsMatchKnobs()
    {
        Assert.AreEqual(0.8f, ChartDensity.TargetNps(1, 0.8f, 5.0f), 0.0001f);
        Assert.AreEqual(5.0f, ChartDensity.TargetNps(30, 0.8f, 5.0f), 0.0001f);
    }

    [Test]
    public void TargetCount_UsesFirstToLastOnsetSpan()
    {
        Assert.AreEqual(20, ChartDensity.TargetCount(2f, 5f, 15f));
        Assert.AreEqual(0, ChartDensity.TargetCount(2f, 5f, 5f));
    }
}
