using System.Collections.Generic;
using NUnit.Framework;
using SliceSpace;
using UnityEngine;

public class SliceMatchTests
{
    private const float Weight = 60f;

    private static SlicePlane Plane(Vector3 normal, float distance) =>
        new SlicePlane(normal, distance);

    [Test]
    public void SamePlaneScoresZero()
    {
        var p = Plane(Vector3.up, -1.2f);
        Assert.AreEqual(0f, SliceMatch.Score(p, p, Weight), 1e-4f);
    }

    /// <summary>
    /// 이 테스트가 <see cref="SliceMatch"/>의 존재 이유다 — 같은 절단면을 반대 방향에서
    /// 유도하면 법선이 뒤집혀 나온다. 부호를 보면 180°로 읽혀 재사용이 전부 실패한다.
    /// </summary>
    [Test]
    public void FlippedNormalIsTheSamePlane()
    {
        var a = Plane(Vector3.up, -1.2f);
        var b = Plane(Vector3.down, 1.2f);

        Assert.AreEqual(0f, SliceMatch.Score(a, b, Weight), 1e-4f);
    }

    [Test]
    public void PerpendicularNormalsScoreNinety()
    {
        var a = Plane(Vector3.up, 0f);
        var b = Plane(Vector3.right, 0f);

        Assert.AreEqual(90f, SliceMatch.Score(a, b, Weight), 1e-3f);
    }

    [Test]
    public void HeightDifferenceCostsPositionWeight()
    {
        var neck = Plane(Vector3.up, -1.5f);
        var waist = Plane(Vector3.up, -1.0f);

        // 각도차 0, 거리차 0.5m → 0.5 × Weight.
        Assert.AreEqual(0.5f * Weight, SliceMatch.Score(neck, waist, Weight), 1e-3f);
    }

    [Test]
    public void PicksNearestOfSameAngleByHeight()
    {
        var want = Plane(Vector3.up, -1.4f);
        var candidates = new List<SlicePlane>
        {
            Plane(Vector3.up, -0.6f),   // 허리
            Plane(Vector3.up, -1.5f),   // 목 — 이쪽이 가깝다
            Plane(Vector3.right, -1.4f) // 각도가 다르다
        };

        Assert.AreEqual(1, SliceMatch.Pick(want, candidates, Weight));
    }

    [Test]
    public void PicksFlippedCandidate()
    {
        var want = Plane(Vector3.up, -1.5f);
        var candidates = new List<SlicePlane>
        {
            Plane(Vector3.right, 0f),
            Plane(Vector3.down, 1.5f) // 같은 절단면, 뒤집힌 법선
        };

        Assert.AreEqual(1, SliceMatch.Pick(want, candidates, Weight));
    }

    [Test]
    public void EmptyCandidatesReturnNone()
    {
        var want = Plane(Vector3.up, 0f);

        Assert.AreEqual(SliceMatch.None, SliceMatch.Pick(want, new List<SlicePlane>(), Weight));
        Assert.AreEqual(SliceMatch.None, SliceMatch.Pick(want, null, Weight));
    }

    [Test]
    public void TieGoesToEarlierIndex()
    {
        var want = Plane(Vector3.up, 0f);
        var candidates = new List<SlicePlane> { Plane(Vector3.up, 0f), Plane(Vector3.up, 0f) };

        Assert.AreEqual(0, SliceMatch.Pick(want, candidates, Weight));
    }
}
