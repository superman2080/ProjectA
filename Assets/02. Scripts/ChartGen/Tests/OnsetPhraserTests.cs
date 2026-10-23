using System.Collections.Generic;
using System.Linq;
using ChartGen;
using NUnit.Framework;

public class OnsetPhraserTests
{
    // BPM 120, 박당 2칸 → 격자 0.25초, 1박 = 2칸 = 0.5초.
    private static readonly BeatGrid Grid = new BeatGrid(120f, 0f, 2);

    private static List<Onset> Candidates(IEnumerable<int> indices, System.Func<int, float> strength = null)
    {
        return indices.Select(i => new Onset(Grid.GridIndexToTime(i), strength?.Invoke(i) ?? 1f)).ToList();
    }

    private static List<float> Times(params int[] indices) => indices.Select(Grid.GridIndexToTime).ToList();

    private static PhraseResult Run(List<Onset> candidates, List<float> kept, int target)
    {
        return OnsetPhraser.Assemble(candidates, kept, Grid, 2, 4, 1, 2, target, BeatWeights.Default);
    }

    [Test]
    public void Assemble_ThinnedPhrase_StaysOneGroupDespiteWideGaps()
    {
        // 한 악구(0~7칸)에서 솎기가 2박 간격(0, 4칸)으로 남겼다 — 간격으로 묶으면 둘 다 단독이 됐다.
        var candidates = Candidates(Enumerable.Range(0, 8));
        var kept = Times(0, 4);

        Assert.AreEqual(2, OnsetGrouper.GroupByBeats(kept, Grid, 2, 1).Count);

        var result = Run(candidates, kept, 2);

        Assert.AreEqual(1, result.Groups.Count);
        CollectionAssert.AreEqual(kept, result.Groups[0]);
    }

    [Test]
    public void Assemble_RestSeparatedPhrases_DoNotMix()
    {
        // 0~3칸과 12~15칸 사이가 1박(2칸)보다 넓다 — 쉼표다.
        var candidates = Candidates(Enumerable.Range(0, 4).Concat(Enumerable.Range(12, 4)));
        var kept = Times(0, 2, 12, 14);

        var result = Run(candidates, kept, 4);

        Assert.AreEqual(2, result.Groups.Count);
        CollectionAssert.AreEqual(Times(0, 2), result.Groups[0]);
        CollectionAssert.AreEqual(Times(12, 14), result.Groups[1]);
    }

    [Test]
    public void Assemble_SingleLeftUnderTarget_FillsWithBestCandidate()
    {
        // 1칸(8분 엇박, 0.7)보다 2칸(정박, 1.0)이 점수가 높다.
        var strengths = new Dictionary<int, float> { [0] = 1f, [1] = 0.9f, [2] = 0.9f, [3] = 0.1f };
        var candidates = Candidates(Enumerable.Range(0, 4), i => strengths[i]);

        var result = Run(candidates, Times(0), 5);

        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(1, result.Groups.Count);
        CollectionAssert.AreEqual(Times(0, 2), result.Groups[0]);
    }

    [Test]
    public void Assemble_SingleLeftAtTarget_IsRemoved()
    {
        var candidates = Candidates(Enumerable.Range(0, 4));

        var result = Run(candidates, Times(0), 1);

        Assert.AreEqual(1, result.Removed);
        Assert.IsEmpty(result.Groups);
    }

    [Test]
    public void Assemble_IsolatedSingleCandidate_IsAlwaysRemoved()
    {
        // 후보가 1개뿐인 악구는 목표가 남아도 채울 것이 없다.
        var candidates = Candidates(new[] { 0, 20 });

        var result = Run(candidates, Times(0, 20), 10);

        Assert.AreEqual(2, result.Removed);
        Assert.IsEmpty(result.Groups);
    }

    [Test]
    public void Assemble_IsDeterministic()
    {
        var candidates = Candidates(Enumerable.Range(0, 40), i => (i % 3) * 0.2f + 0.1f);
        var kept = Times(0, 5, 9, 16, 23, 31, 38);

        var a = Run(candidates, kept, 9);
        var b = Run(candidates, kept, 9);

        Assert.AreEqual(a.Groups.Count, b.Groups.Count);
        for (int i = 0; i < a.Groups.Count; i++) CollectionAssert.AreEqual(a.Groups[i], b.Groups[i]);
    }
}
