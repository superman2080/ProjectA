using ChartGen;
using NUnit.Framework;

public class OnsetDetectorTests
{
    [Test]
    public void Detect_SilentSamples_ReturnsNoOnsets()
    {
        var samples = new float[1000];
        var onsets = OnsetDetector.Detect(samples, 1000, 100, 0.05f);

        Assert.IsEmpty(onsets);
    }

    [Test]
    public void Detect_SuddenLoudWindow_ReturnsOnsetAtWindowStartTime()
    {
        const int windowSize = 100;
        const int sampleRate = 1000;
        var samples = new float[windowSize * 4];

        for (int i = windowSize * 2; i < windowSize * 3; i++)
            samples[i] = 1f;

        var onsets = OnsetDetector.Detect(samples, sampleRate, windowSize, 0.1f);

        Assert.AreEqual(1, onsets.Length);
        Assert.AreEqual(0.2f, onsets[0], 0.001f);
    }

    [Test]
    public void DetectWithStrength_StrengthIsRmsRise()
    {
        const int windowSize = 100;
        const int sampleRate = 1000;
        var samples = new float[windowSize * 4];

        // 0 → 0.5 RMS로 오르는 창 하나
        for (int i = windowSize * 2; i < windowSize * 3; i++)
            samples[i] = 0.5f;

        var onsets = OnsetDetector.DetectWithStrength(samples, sampleRate, windowSize, 0.1f);

        Assert.AreEqual(1, onsets.Length);
        Assert.AreEqual(0.2f, onsets[0].Time, 0.001f);
        Assert.AreEqual(0.5f, onsets[0].Strength, 0.001f);
    }

    [Test]
    public void Detect_EmptySamples_ReturnsEmptyArray()
    {
        var onsets = OnsetDetector.Detect(new float[0], 1000, 100, 0.05f);

        Assert.IsEmpty(onsets);
    }
}
