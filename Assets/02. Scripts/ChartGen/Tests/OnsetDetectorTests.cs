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
    public void Detect_EmptySamples_ReturnsEmptyArray()
    {
        var onsets = OnsetDetector.Detect(new float[0], 1000, 100, 0.05f);

        Assert.IsEmpty(onsets);
    }
}
