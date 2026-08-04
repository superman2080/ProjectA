using System.Collections.Generic;
using EnemySpace;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 링 배치·상대 선택의 <b>순수 로직</b> 검증. MonoBehaviour·씬에 의존하지 않는다.
/// 여기서 깨지면 무대가 무너지지만 판정은 멀쩡하다 — 그 경계를 지키는 게 이 테스트의 목적이기도 하다.
/// </summary>
public class EnemyRingTests
{
    // ── 스폰 각도: 시야 반대편 ──────────────────────────────────────────────

    [Test]
    public void PickSpawnAnglePicksBehindViewWhenRingIsEmpty()
    {
        float angle = EnemyRing.PickSpawnAngle(new List<float>(), viewYaw: 0f, minAngleGap: 30f);

        // 0도를 보고 있으면 등 뒤는 180도 근처다.
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, 180f)), Is.LessThan(15f));
    }

    [Test]
    public void PickSpawnAngleFollowsViewYaw()
    {
        float angle = EnemyRing.PickSpawnAngle(new List<float>(), viewYaw: 90f, minAngleGap: 30f);

        Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, 270f)), Is.LessThan(15f));
    }

    [Test]
    public void PickSpawnAngleKeepsMinimumGapFromOccupiedAngles()
    {
        var occupied = new List<float> { 180f, 200f, 160f };

        float angle = EnemyRing.PickSpawnAngle(occupied, viewYaw: 0f, minAngleGap: 30f);

        foreach (float other in occupied)
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(other, angle)), Is.GreaterThanOrEqualTo(30f - 0.01f));
    }

    [Test]
    public void PickSpawnAngleStillReturnsAngleWhenRingIsFull()
    {
        // 원주 전체를 촘촘히 점유 — 간격을 지키는 후보가 하나도 없다.
        var occupied = new List<float>();
        for (float a = 0f; a < 360f; a += 5f) occupied.Add(a);

        float angle = EnemyRing.PickSpawnAngle(occupied, viewYaw: 0f, minAngleGap: 30f);

        // "뒤가 될 때까지 재시도"가 아니라 "가장 뒤인 곳을 고른다" — 그래서 항상 유효한 값이 나온다.
        Assert.That(angle, Is.InRange(0f, 360f));
    }

    // ── 다음 상대: 링 각도 순 ───────────────────────────────────────────────

    [Test]
    public void PickNextOpponentAdvancesInOneDirection()
    {
        var angles = new List<float> { 10f, 100f, 200f, 300f };

        int index = EnemyRing.PickNextOpponent(angles, currentAngle: 0f);

        Assert.AreEqual(0, index); // 10도가 반시계 방향으로 가장 가깝다
    }

    [Test]
    public void PickNextOpponentWrapsAroundAfterFullCircle()
    {
        var angles = new List<float> { 10f, 100f, 200f };

        int index = EnemyRing.PickNextOpponent(angles, currentAngle: 300f);

        Assert.AreEqual(0, index); // 300 → 10(=+70도)가 가장 가깝다. 뒤로 가지 않는다.
    }

    [Test]
    public void PickNextOpponentPutsItselfLast()
    {
        var angles = new List<float> { 50f, 120f };

        int index = EnemyRing.PickNextOpponent(angles, currentAngle: 50f);

        Assert.AreEqual(1, index); // 델타 0은 360으로 밀려 뒤로 간다
    }

    [Test]
    public void PickNextOpponentReturnsNegativeWhenNoCandidates()
    {
        Assert.AreEqual(-1, EnemyRing.PickNextOpponent(new List<float>(), 0f));
        Assert.AreEqual(-1, EnemyRing.PickNextOpponent(null, 0f));
    }

    // ── 각도 ↔ 위치 왕복 ───────────────────────────────────────────────────

    [Test]
    public void AngleAndDirectionAreInverse()
    {
        var center = new Vector3(3f, 0f, -2f);

        foreach (float angle in new[] { 0f, 45f, 137f, 270f, 359f })
        {
            Vector3 position = EnemyRing.AngleToPosition(center, angle, 5f);
            float roundTrip = EnemyRing.DirectionToAngle(position - center);

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, roundTrip)), Is.LessThan(0.01f), $"angle={angle}");
        }
    }

    [Test]
    public void ZeroDegreesPointsTowardPositiveZ()
    {
        Vector3 position = EnemyRing.AngleToPosition(Vector3.zero, 0f, 4f);

        Assert.That(Vector3.Distance(position, new Vector3(0f, 0f, 4f)), Is.LessThan(0.001f));
    }

    // ── 무대 배치(2D): 시야 밖 · 간격 ────────────────────────────────────────

    /// <summary>결정적 난수원 — 후보를 예측 가능한 순서로 뽑아 테스트가 흔들리지 않게 한다.</summary>
    private static System.Func<float> Sequence(params float[] values)
    {
        int i = 0;
        return () => values[i++ % values.Length];
    }

    [Test]
    public void PickStagePositionAvoidsVisibleCandidates()
    {
        // +Z 절반을 화면 안으로 본다.
        System.Func<Vector3, bool> visible = p => p.z > 0f;

        for (int seed = 0; seed < 20; seed++)
        {
            Random.InitState(seed);
            Vector3 pos = EnemyRing.PickStagePosition(
                new List<Vector3>(), Vector3.zero, 8f,
                playerPosition: Vector3.zero, viewPosition: new Vector3(0f, 4f, -10f), viewForward: Vector3.forward,
                minSpacing: 1f, minPlayerDistance: 2f,
                isVisible: visible, random: () => Random.value);

            Assert.That(visible(pos), Is.False, $"seed={seed} 위치={pos}");
        }
    }

    [Test]
    public void PickStagePositionKeepsSpacingFromOtherEnemies()
    {
        var occupied = new List<Vector3> { new Vector3(3f, 0f, -3f), new Vector3(-4f, 0f, -1f) };

        for (int seed = 0; seed < 20; seed++)
        {
            Random.InitState(seed);
            Vector3 pos = EnemyRing.PickStagePosition(
                occupied, Vector3.zero, 8f,
                playerPosition: Vector3.zero, viewPosition: new Vector3(0f, 4f, -10f), viewForward: Vector3.forward,
                minSpacing: 2f, minPlayerDistance: 1f,
                isVisible: null, random: () => Random.value);

            foreach (var o in occupied)
                Assert.That(Vector3.Distance(o, pos), Is.GreaterThanOrEqualTo(2f - 0.001f), $"seed={seed}");
        }
    }

    [Test]
    public void PickStagePositionStillReturnsPositionWhenAllAreVisible()
    {
        Random.InitState(1);
        Vector3 pos = EnemyRing.PickStagePosition(
            new List<Vector3>(), Vector3.zero, 8f,
            playerPosition: Vector3.zero, viewPosition: Vector3.zero, viewForward: Vector3.forward,
            minSpacing: 1f, minPlayerDistance: 0f,
            isVisible: _ => true, random: () => Random.value);

        // 폴백은 카메라 전방과 가장 덜 겹치는 후보 = 뒤쪽(-Z).
        Assert.That(pos.z, Is.LessThan(0f), $"위치={pos}");
    }

    [Test]
    public void PickStagePositionStaysInsideStage()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            Random.InitState(seed);
            Vector3 pos = EnemyRing.PickStagePosition(
                new List<Vector3>(), new Vector3(1f, 0f, 2f), 6f,
                playerPosition: Vector3.zero, viewPosition: Vector3.zero, viewForward: Vector3.forward,
                minSpacing: 0f, minPlayerDistance: 0f,
                isVisible: null, random: () => Random.value);

            Assert.That(Vector3.Distance(new Vector3(1f, 0f, 2f), pos), Is.LessThanOrEqualTo(6f + 0.001f), $"seed={seed}");
        }
    }

    // ── 표적 선택: 목표 거리 ────────────────────────────────────────────────

    [Test]
    public void PickTargetByDistancePicksClosestToDesiredDistance()
    {
        var candidates = new List<Vector3>
        {
            new Vector3(1f, 0f, 0f),   // 1m
            new Vector3(5f, 0f, 0f),   // 5m
            new Vector3(9f, 0f, 0f),   // 9m
        };

        Assert.That(EnemyRing.PickTargetByDistance(candidates, Vector3.zero, 4.5f), Is.EqualTo(1));
        Assert.That(EnemyRing.PickTargetByDistance(candidates, Vector3.zero, 0.5f), Is.EqualTo(0));
        Assert.That(EnemyRing.PickTargetByDistance(candidates, Vector3.zero, 100f), Is.EqualTo(2));
    }

    [Test]
    public void PickTargetByDistanceIgnoresHeight()
    {
        var candidates = new List<Vector3>
        {
            new Vector3(5f, 50f, 0f),  // 평면 5m, 높이 50m
            new Vector3(9f, 0f, 0f),
        };

        Assert.That(EnemyRing.PickTargetByDistance(candidates, Vector3.zero, 5f), Is.EqualTo(0));
    }

    [Test]
    public void PickTargetByDistanceReturnsNegativeWhenNoCandidates()
    {
        Assert.That(EnemyRing.PickTargetByDistance(new List<Vector3>(), Vector3.zero, 5f), Is.EqualTo(-1));
        Assert.That(EnemyRing.PickTargetByDistance(null, Vector3.zero, 5f), Is.EqualTo(-1));
    }

    // ── Retreat: stays inside the stage ─────────────────────────────────────

    [Test]
    public void PickRetreatTargetGoesStraightBackWhenWellInsideStage()
    {
        Vector3 target = EnemyRing.PickRetreatTarget(
            position: Vector3.zero, back: Vector3.back, stageCenter: Vector3.zero, stageRadius: 8f, distance: 1.5f);

        Assert.That(Vector3.Distance(target, new Vector3(0f, 0f, -1.5f)), Is.LessThan(0.01f));
    }

    [Test]
    public void PickRetreatTargetVeersSidewaysButStaysInsideStage()
    {
        // Standing near the rim (radius 8) and trying to back out through it (+Z).
        Vector3 position = new Vector3(0f, 0f, 7.5f);
        Vector3 target = EnemyRing.PickRetreatTarget(
            position, back: Vector3.forward, stageCenter: Vector3.zero, stageRadius: 8f, distance: 1.5f);

        Assert.That(target.magnitude, Is.LessThanOrEqualTo(8.01f), "left the stage");
        Assert.That(Vector3.Distance(target, position), Is.GreaterThan(0.1f), "did not move at all");

        // Candidates are limited to the rear half-circle; going forward would not read as a retreat.
        Assert.That(Vector3.Dot((target - position).normalized, Vector3.forward), Is.GreaterThan(-0.01f));
    }

    [Test]
    public void PickRetreatTargetStaysPutWhenDistanceIsZero()
    {
        Vector3 position = new Vector3(1f, 0f, 2f);

        Assert.That(EnemyRing.PickRetreatTarget(position, Vector3.back, Vector3.zero, 8f, 0f), Is.EqualTo(position));
    }
}
