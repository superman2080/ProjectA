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

    // ── 무리 배치 (docs/EnemyCluster) ──────────────────────────────────────

    private static float Always() => 0.5f;

    [Test]
    public void PickClusterDirectionKeepsPreviousWhenStillUsable()
    {
        // 방향 유지가 목적지 요동 대책의 핵심이다 — 쓸 만한데 새로 뽑으면 무리가 무대를 가로질러 서성인다.
        Vector3 previous = Vector3.forward;
        Vector3 result = EnemyRing.PickClusterDirection(
            playerPosition: Vector3.zero, previous, stageCenter: Vector3.zero, stageRadius: 8f,
            desiredDistance: 4f, avoidCenter: new Vector3(-6f, 0f, 0f), avoidRadius: 2f, random: Always);

        Assert.That(Vector3.Distance(result, previous), Is.LessThan(0.01f));
    }

    [Test]
    public void PickClusterDirectionRerollsWhenPreviousLeavesStage()
    {
        // 무대 가장자리에 서서 바깥(+Z)을 향하면 그 방향으로는 desired만큼 못 간다.
        Vector3 player = new Vector3(0f, 0f, 7f);
        Vector3 result = EnemyRing.PickClusterDirection(
            player, Vector3.forward, Vector3.zero, stageRadius: 8f,
            desiredDistance: 6f, avoidCenter: Vector3.zero, avoidRadius: 0f, random: Always);

        Vector3 point = player + result.normalized * 6f;
        Assert.That(new Vector2(point.x, point.z).magnitude, Is.LessThanOrEqualTo(8.01f), "left the stage");
    }

    [Test]
    public void PickClusterDirectionAvoidsTheActiveCluster()
    {
        // 다음 무리를 지금 싸우는 무리 위에 겹쳐 놓으면 두 무리가 한 덩어리로 보인다.
        Vector3 avoid = new Vector3(0f, 0f, 4f);
        Vector3 result = EnemyRing.PickClusterDirection(
            Vector3.zero, Vector3.forward, Vector3.zero, stageRadius: 8f,
            desiredDistance: 4f, avoid, avoidRadius: 3f, random: Always);

        Vector3 point = result.normalized * 4f;
        Assert.That(Vector3.Distance(point, avoid), Is.GreaterThanOrEqualTo(2.99f));
    }

    [Test]
    public void PickClusterCenterShortensRatherThanLeavingStage()
    {
        // 무대를 벗어나면 거리를 '줄이는' 쪽으로 자른다 — 늘리면 플레이어가 창 안에 도달하지 못한다.
        Vector3 player = new Vector3(0f, 0f, 6f);
        Vector3 center = EnemyRing.PickClusterCenter(
            player, Vector3.forward, desiredDistance: 10f,
            stageCenter: Vector3.zero, stageRadius: 8f, minPlayerDistance: 2f);

        Assert.That(new Vector2(center.x, center.z).magnitude, Is.LessThanOrEqualTo(8.01f), "left the stage");
        Assert.That(Vector3.Distance(center, player), Is.LessThanOrEqualTo(10.01f), "grew past desired");
    }

    [Test]
    public void PickClusterCenterStaysInsideStageWhenPlayerIsOutsideIt()
    {
        // 튜토리얼 회귀: PrepareStage가 골목(z=-40)에 선 미오를 기준으로 집결지를 정하면
        // TravelInsideCircle이 0을 돌려주는데 그 다음 줄의 minPlayerDistance가 0을 되올려
        // 무대에서 45m 떨어진 복도가 나왔다. 무대 원이 하드 제약이고 minPlayerDistance가 그것을 이길 수 없다.
        Vector3 player = new Vector3(0f, 0f, -40f);
        Vector3 center = EnemyRing.PickClusterCenter(
            player, Vector3.back, desiredDistance: 4f,
            stageCenter: Vector3.zero, stageRadius: 8f, minPlayerDistance: 5f);

        Assert.That(new Vector2(center.x, center.z).magnitude, Is.LessThanOrEqualTo(8.01f), "left the stage");
    }

    [Test]
    public void PickClusterCenterHonoursDesiredDistanceWhenItFits()
    {
        Vector3 center = EnemyRing.PickClusterCenter(
            Vector3.zero, Vector3.forward, desiredDistance: 5f,
            stageCenter: Vector3.zero, stageRadius: 8f, minPlayerDistance: 2f);

        Assert.That(Vector3.Distance(center, Vector3.zero), Is.EqualTo(5f).Within(0.01f));
    }

    [Test]
    public void PlaceInClusterIsDeterministicAndInsideRadius()
    {
        // 결정적이지 않으면 재배치마다 대형이 바뀌어 '무리가 옮겨간 것'이 아니라 '흩어졌다 모인 것'으로 보인다.
        Vector3 center = new Vector3(2f, 0f, -3f);
        const int Count = 4;

        for (int i = 0; i < Count; i++)
        {
            Vector3 a = EnemyRing.PlaceInCluster(center, i, Count, clusterRadius: 2f, minSpacing: 1f);
            Vector3 b = EnemyRing.PlaceInCluster(center, i, Count, clusterRadius: 2f, minSpacing: 1f);

            Assert.That(a, Is.EqualTo(b), $"slot {i} was not deterministic");
            Assert.That(Vector3.Distance(a, center), Is.LessThanOrEqualTo(2.01f), $"slot {i} left the cluster");
        }
    }

    [Test]
    public void PlaceInClusterKeepsSlotsApart()
    {
        const int Count = 4;
        var slots = new List<Vector3>();
        for (int i = 0; i < Count; i++)
            slots.Add(EnemyRing.PlaceInCluster(Vector3.zero, i, Count, clusterRadius: 2f, minSpacing: 1f));

        for (int i = 0; i < Count; i++)
        for (int j = i + 1; j < Count; j++)
            Assert.That(Vector3.Distance(slots[i], slots[j]), Is.GreaterThan(0.5f), $"slots {i},{j} overlap");
    }

    [Test]
    public void PickSpawnNearClusterReturnsSlotWhenAlreadyHidden()
    {
        // 자리가 이미 화면 밖이면 오프셋 0 — 이동 없이 그 자리에 선다. 정상 경로다.
        Vector3 slot = new Vector3(1f, 0f, 1f);
        Vector3 result = EnemyRing.PickSpawnNearCluster(
            slot, Vector3.zero, 8f, Vector3.zero, 2f, Vector3.zero, Vector3.forward,
            isVisible: _ => false, maxOffset: 6f);

        Assert.That(result, Is.EqualTo(slot));
    }

    [Test]
    public void PickSpawnNearClusterPicksNearestHiddenPoint()
    {
        // 자리만 화면 안이면 최소 오프셋으로 비껴 나야 한다 — 무대 가장자리까지 밀려나면 첫 이동이 무대 횡단이 된다.
        Vector3 slot = new Vector3(3f, 0f, 0f);
        Vector3 result = EnemyRing.PickSpawnNearCluster(
            slot, Vector3.zero, 8f, Vector3.zero, 1f, Vector3.zero, Vector3.forward,
            isVisible: p => Vector3.Distance(p, slot) < 0.5f, maxOffset: 6f);

        Assert.That(result, Is.Not.EqualTo(slot));
        Assert.That(Vector3.Distance(result, slot), Is.LessThanOrEqualTo(1.6f), "pushed further than needed");
    }

    // ── 배회 궤도 (docs/EnemyIdleWander) ──────────────────────────────────

    [Test]
    public void PickOrbitSlotSitsAtStandoffDistance()
    {
        Vector3 player = new Vector3(1f, 0f, -2f);
        Vector3 slot = EnemyRing.PickOrbitSlot(
            player, index: 0, count: 4, standoffDistance: 3.5f, phase: 0f,
            stageCenter: Vector3.zero, stageRadius: 8f);

        Assert.That(Vector3.Distance(slot, player), Is.EqualTo(3.5f).Within(0.01f));
    }

    [Test]
    public void PickOrbitSlotSpreadsSlotsEvenly()
    {
        // 균등 분할이 곧 겹침 방지다 — 반발 계산을 두지 않는 이유.
        const int Count = 4;
        var slots = new List<Vector3>();
        for (int i = 0; i < Count; i++)
            slots.Add(EnemyRing.PickOrbitSlot(Vector3.zero, i, Count, 3.5f, 0f, Vector3.zero, 8f));

        for (int i = 0; i < Count; i++)
        for (int j = i + 1; j < Count; j++)
            Assert.That(Vector3.Distance(slots[i], slots[j]), Is.GreaterThan(2f), $"slots {i},{j} too close");
    }

    [Test]
    public void PickOrbitSlotShortensRatherThanLeavingStage()
    {
        // 플레이어가 가장자리에 붙으면 궤도 바깥쪽은 무대 밖이다 — 각도가 아니라 '거리'를 줄여야
        // 자리가 안 뒤섞인다.
        Vector3 player = new Vector3(0f, 0f, 7.5f);
        for (int i = 0; i < 4; i++)
        {
            Vector3 slot = EnemyRing.PickOrbitSlot(player, i, 4, 3.5f, 0f, Vector3.zero, 8f);
            Assert.That(new Vector2(slot.x, slot.z).magnitude, Is.LessThanOrEqualTo(8.01f), $"slot {i} left the stage");
            Assert.That(Vector3.Distance(slot, player), Is.LessThanOrEqualTo(3.51f), $"slot {i} grew past standoff");
        }
    }

    [Test]
    public void PickOrbitSlotIsDeterministic()
    {
        Vector3 a = EnemyRing.PickOrbitSlot(Vector3.zero, 2, 4, 3.5f, 47f, Vector3.zero, 8f, jitter: 9f);
        Vector3 b = EnemyRing.PickOrbitSlot(Vector3.zero, 2, 4, 3.5f, 47f, Vector3.zero, 8f, jitter: 9f);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void PickOrbitSlotRotatesWithPhase()
    {
        Vector3 a = EnemyRing.PickOrbitSlot(Vector3.zero, 0, 4, 3.5f, 0f, Vector3.zero, 8f);
        Vector3 b = EnemyRing.PickOrbitSlot(Vector3.zero, 0, 4, 3.5f, 90f, Vector3.zero, 8f);

        Assert.That(Vector3.Distance(a, b), Is.GreaterThan(1f), "phase did not rotate the orbit");
        Assert.That(Vector3.Distance(b, Vector3.zero), Is.EqualTo(3.5f).Within(0.01f), "rotation changed the radius");
    }

    [Test]
    public void PickSpawnNearClusterFallsBackWhenEverythingIsVisible()
    {
        // 전부 화면 안이어도 실패하지 않는다 — 기존 PickStagePosition과 같은 규율.
        Vector3 slot = new Vector3(3f, 0f, 0f);
        Vector3 result = EnemyRing.PickSpawnNearCluster(
            slot, Vector3.zero, 8f, Vector3.zero, 1f, Vector3.zero, Vector3.forward,
            isVisible: _ => true, maxOffset: 6f);

        Assert.That(new Vector2(result.x, result.z).magnitude, Is.LessThanOrEqualTo(8.01f));
        Assert.That(Vector3.Distance(result, Vector3.zero), Is.GreaterThanOrEqualTo(0.99f), "spawned inside the player guard");
    }

    // ── 불가침 캡슐 (docs/ActorSeparation) ──────────────────────────────────

    /// <summary>점 → 선분 평면 거리. 테스트가 자기 기준을 직접 재도록 둔다(구현을 다시 부르지 않는다).</summary>
    private static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        p.y = a.y = b.y = 0f;

        Vector3 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        float t = lenSq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq) : 0f;

        return Vector3.Distance(p, a + ab * t);
    }

    [Test]
    public void SeparationPushIgnoresPointsOutsideCapsule()
    {
        Vector3 push = EnemyRing.SeparationPush(
            new Vector3(0f, 0f, 5f), Vector3.zero, new Vector3(4f, 0f, 0f), radius: 1f);

        Assert.That(push, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void SeparationPushClearsEndCapToExactlyRadius()
    {
        Vector3 a = Vector3.zero;
        Vector3 b = new Vector3(4f, 0f, 0f);
        Vector3 inside = new Vector3(-0.3f, 0f, 0.2f); // a 쪽 끝 원 안

        Vector3 result = inside + EnemyRing.SeparationPush(inside, a, b, radius: 1f);

        Assert.That(FlatDistanceToSegment(result, a, b), Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void SeparationPushClearsCorridorSideways()
    {
        // 통로 한복판 옆구리 — 원만 봤다면 안 걸렸을 자리다.
        Vector3 a = Vector3.zero;
        Vector3 b = new Vector3(4f, 0f, 0f);
        Vector3 inside = new Vector3(2f, 0f, 0.4f);

        Vector3 push = EnemyRing.SeparationPush(inside, a, b, radius: 1f);
        Vector3 result = inside + push;

        Assert.That(FlatDistanceToSegment(result, a, b), Is.EqualTo(1f).Within(0.001f));
        // 통로를 따라 미끄러지면 안 된다 — 축 성분이 0이어야 옆으로 비켜선다.
        Assert.That(Vector3.Dot(push, (b - a).normalized), Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void SeparationPushDegeneratesToCircleWhenEndsCoincide()
    {
        Vector3 center = new Vector3(1f, 0f, 1f);
        Vector3 inside = new Vector3(1.5f, 0f, 1f);

        Vector3 push = EnemyRing.SeparationPush(inside, center, center, radius: 1f);

        Assert.That(Vector3.Distance(inside + push, center), Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void SeparationPushIgnoresPointsBeyondSegmentEnds()
    {
        // 선분의 <b>연장선</b> 위 — 통로가 무한 직선이면 걸렸을 자리다.
        Vector3 push = EnemyRing.SeparationPush(
            new Vector3(6f, 0f, 0f), Vector3.zero, new Vector3(4f, 0f, 0f), radius: 1f);

        Assert.That(push, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void SeparationPushGoesPerpendicularWhenExactlyOnSegment()
    {
        Vector3 a = Vector3.zero;
        Vector3 b = new Vector3(4f, 0f, 0f);

        Vector3 push = EnemyRing.SeparationPush(new Vector3(2f, 0f, 0f), a, b, radius: 1f);

        Assert.That(push.magnitude, Is.EqualTo(1f).Within(0.001f));
        Assert.That(Vector3.Dot(push, (b - a).normalized), Is.EqualTo(0f).Within(0.001f),
            "밀 방향이 선분과 나란하면 통로를 따라 미끄러진다");
    }

    [Test]
    public void SeparationPushNeverTouchesHeight()
    {
        Vector3 a = new Vector3(0f, 1.2f, 0f);
        Vector3 b = new Vector3(4f, 0f, 0f);

        Vector3 push = EnemyRing.SeparationPush(new Vector3(2f, 3f, 0.4f), a, b, radius: 1f);

        Assert.That(push.y, Is.EqualTo(0f).Within(1e-5f));
    }
}
