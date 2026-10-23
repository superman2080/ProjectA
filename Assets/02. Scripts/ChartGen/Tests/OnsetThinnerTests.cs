using System.Collections.Generic;
using System.Linq;
using ChartGen;
using NUnit.Framework;

public class OnsetThinnerTests
{
    // BPM 120, 박당 2칸 → 격자 0.25초, 4/4박이면 마디 = 8칸 = 2초.
    private static readonly BeatGrid Grid = new BeatGrid(120f, 0f, 2);

    private static List<Onset> Range(int from, int to, System.Func<int, float> strength)
    {
        return Enumerable.Range(from, to - from).Select(i => new Onset(Grid.GridIndexToTime(i), strength(i))).ToList();
    }

    [Test]
    public void Thin_KeepsExactlyTargetCount()
    {
        var onsets = Range(0, 64, i => 0.1f + (i * 37 % 11) * 0.05f);

        var kept = OnsetThinner.Thin(onsets, Grid, 2, 4, 20, 2, BeatWeights.Default);

        Assert.AreEqual(20, kept.Length);
        CollectionAssert.IsOrdered(kept);
    }

    [Test]
    public void Thin_SameStrength_PrefersOnBeats()
    {
        var onsets = Range(0, 8, _ => 1f);

        var kept = OnsetThinner.Thin(onsets, Grid, 2, 4, 4, 1, BeatWeights.Default);

        CollectionAssert.AreEqual(new[] { 0f, 0.5f, 1.0f, 1.5f }, kept);
    }

    [Test]
    public void Thin_QuietSegmentStillKeepsItsShare()
    {
        // 마디 0은 크고 마디 1은 작다. 전역 상위 N이면 마디 1이 통째로 사라진다.
        var onsets = Range(0, 16, i => i < 8 ? 1f : 0.01f);

        var kept = OnsetThinner.Thin(onsets, Grid, 2, 4, 8, 1, BeatWeights.Default);

        Assert.AreEqual(8, kept.Length);
        Assert.AreEqual(4, kept.Count(t => t >= 2f));
    }

    [Test]
    public void Thin_TargetAtLeastCandidates_ReturnsAll()
    {
        var onsets = Range(0, 10, _ => 1f);

        var kept = OnsetThinner.Thin(onsets, Grid, 2, 4, 50, 2, BeatWeights.Default);

        Assert.AreEqual(10, kept.Length);
    }

    [Test]
    public void Thin_IsDeterministic()
    {
        var onsets = Range(0, 40, i => (i % 3) * 0.2f + 0.1f);

        var a = OnsetThinner.Thin(onsets, Grid, 2, 4, 15, 2, BeatWeights.Default);
        var b = OnsetThinner.Thin(onsets, Grid, 2, 4, 15, 2, BeatWeights.Default);

        CollectionAssert.AreEqual(a, b);
    }

    [TestCase(0, 4, 1.3f)]   // 마디 첫 박
    [TestCase(4, 4, 1.0f)]   // 정박
    [TestCase(2, 4, 0.7f)]   // 8분 엇박
    [TestCase(1, 4, 0.5f)]   // 16분 엇박
    [TestCase(1, 2, 0.7f)]   // 박당 2칸이면 엇박은 8분
    public void WeightOf_ClassifiesBeatPosition(int index, int sub, float expected)
    {
        Assert.AreEqual(expected, OnsetThinner.WeightOf(index, sub, sub * 4, BeatWeights.Default), 0.0001f);
    }
}
