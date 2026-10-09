using System.Collections.Generic;
using NUnit.Framework;
using PatternSpace;
using UnityEngine;

public class EngageSlotsTests
{
    private static readonly Vector3 Player = new Vector3(3f, 0f, -2f);
    private static readonly Vector3 Axis = Vector3.forward;

    [Test]
    public void EmptyOffsetsFallBackToFacingOneEnemy()
    {
        // 비우면 예전 경로 — 축 위 fallbackGap만큼 앞에 한 명. 기존 템플릿의 마이그레이션이 0인 근거다.
        Vector3 slot = EngageSlots.Resolve(null, 0, Player, Axis, 1.5f);

        Assert.That(slot, Is.EqualTo(Player + Vector3.forward * 1.5f).Using(Vector3Comparer));
        Assert.That(EngageSlots.CountOf(null), Is.EqualTo(1));
        Assert.That(EngageSlots.CountOf(new List<Vector2>()), Is.EqualTo(1));
    }

    [Test]
    public void OffsetsAreReadInDuelAxisLocalSpace()
    {
        // y = 축 방향(앞), x = 그 오른쪽. 축이 +Z면 오른쪽은 +X다.
        var offsets = new List<Vector2> { new Vector2(0f, 2f), new Vector2(1f, 2f) };

        Assert.That(EngageSlots.Resolve(offsets, 0, Player, Axis, 99f),
                    Is.EqualTo(Player + new Vector3(0f, 0f, 2f)).Using(Vector3Comparer));
        Assert.That(EngageSlots.Resolve(offsets, 1, Player, Axis, 99f),
                    Is.EqualTo(Player + new Vector3(1f, 0f, 2f)).Using(Vector3Comparer));
    }

    [Test]
    public void OffsetsRotateWithTheAxis()
    {
        // 축이 회전하면 배치도 같이 돈다 — 오프셋은 월드가 아니라 결투 축 로컬이다.
        var offsets = new List<Vector2> { new Vector2(1f, 2f) };

        Vector3 slot = EngageSlots.Resolve(offsets, 0, Vector3.zero, Vector3.right, 99f);

        // 축이 +X면 앞 = +X, 오른쪽 = -Z.
        Assert.That(slot, Is.EqualTo(new Vector3(2f, 0f, -1f)).Using(Vector3Comparer));
    }

    [Test]
    public void CountComesFromTheOffsetList()
    {
        // 배치가 곧 인원이다 — 둘을 따로 들면 "인원 3인데 오프셋 2개"가 표현 가능해진다.
        var offsets = new List<Vector2> { Vector2.zero, Vector2.one, new Vector2(2f, 2f) };

        Assert.That(EngageSlots.CountOf(offsets), Is.EqualTo(3));
    }

    [Test]
    public void IndexOutOfRangeFallsBackInsteadOfThrowing()
    {
        // 저작 중에 인덱스가 순간적으로 어긋날 수 있다(추가·삭제). 터지는 대신 폴백 자리를 준다.
        var offsets = new List<Vector2> { new Vector2(1f, 1f) };

        Assert.That(EngageSlots.Resolve(offsets, 5, Player, Axis, 1.5f),
                    Is.EqualTo(Player + Vector3.forward * 1.5f).Using(Vector3Comparer));
    }

    [Test]
    public void DefaultOffsetIsTheFacingSpot()
    {
        // 저작을 시작하는 순간 자리가 바뀌면 안 된다 — 첫 원소는 마주보는 그 자리여야 한다.
        Vector2 offset = EngageSlots.DefaultOffset(1.5f);
        Vector3 slot = EngageSlots.Resolve(new List<Vector2> { offset }, 0, Player, Axis, 1.5f);

        Assert.That(slot, Is.EqualTo(Player + Vector3.forward * 1.5f).Using(Vector3Comparer));
    }

    [Test]
    public void SlotHeightNeverComesFromTheOffsets()
    {
        // 오프셋은 평면이다. 높이는 duelHeightCurve가 드는 독립 축이라 여기서 나오면 안 된다.
        var offsets = new List<Vector2> { new Vector2(3f, 4f) };
        Vector3 origin = new Vector3(0f, 5f, 0f);

        Assert.That(EngageSlots.Resolve(offsets, 0, origin, Axis, 1f).y, Is.EqualTo(5f).Within(1e-4f));
    }

    [Test]
    public void BladeDistanceMeasuresToTheSegmentNotItsEnds()
    {
        // 칼날은 점이 아니라 선분이다 — 선분 중간을 스치는 적은 끝점 거리로는 멀게 나온다.
        Vector3 from = new Vector3(-1f, 1f, 0f);
        Vector3 to = new Vector3(1f, 1f, 0f);

        Assert.That(EngageSlots.BladeDistance(Vector3.zero, from, to), Is.EqualTo(0f).Within(1e-4f));
        Assert.That(EngageSlots.BladeDistance(new Vector3(0f, 0f, 0.5f), from, to), Is.EqualTo(0.5f).Within(1e-4f));
    }

    [Test]
    public void BladeDistanceClampsToTheSegmentEnds()
    {
        // 선분 밖의 적은 끝점까지의 거리다 — 칼이 닿지 않는데 연장선이 지나간다고 닿았다고 하면 안 된다.
        Vector3 from = Vector3.zero;
        Vector3 to = new Vector3(1f, 0f, 0f);

        Assert.That(EngageSlots.BladeDistance(new Vector3(3f, 0f, 0f), from, to), Is.EqualTo(2f).Within(1e-4f));
    }

    [Test]
    public void BladeDistanceIgnoresHeight()
    {
        // 높이는 커브가 따로 든다. 도달 판정에 섞으면 도약 패턴이 전부 "안 닿는다"로 읽힌다.
        Vector3 from = new Vector3(-1f, 2f, 0f);
        Vector3 to = new Vector3(1f, 2f, 0f);

        Assert.That(EngageSlots.BladeDistance(Vector3.zero, from, to), Is.EqualTo(0f).Within(1e-4f));
    }

    private static readonly IEqualityComparer<Vector3> Vector3Comparer = new ApproxVector3();

    private class ApproxVector3 : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;
        public int GetHashCode(Vector3 v) => v.GetHashCode();
    }
}
