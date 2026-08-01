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
    public void PickSpawnAngle_비어있으면_시야_반대편을_고른다()
    {
        float angle = EnemyRing.PickSpawnAngle(new List<float>(), viewYaw: 0f, minAngleGap: 30f);

        // 0도를 보고 있으면 등 뒤는 180도 근처다.
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, 180f)), Is.LessThan(15f));
    }

    [Test]
    public void PickSpawnAngle_시야가_돌면_등뒤도_따라_돈다()
    {
        float angle = EnemyRing.PickSpawnAngle(new List<float>(), viewYaw: 90f, minAngleGap: 30f);

        Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, 270f)), Is.LessThan(15f));
    }

    [Test]
    public void PickSpawnAngle_점유된_각도와_최소간격을_지킨다()
    {
        var occupied = new List<float> { 180f, 200f, 160f };

        float angle = EnemyRing.PickSpawnAngle(occupied, viewYaw: 0f, minAngleGap: 30f);

        foreach (float other in occupied)
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(other, angle)), Is.GreaterThanOrEqualTo(30f - 0.01f));
    }

    [Test]
    public void PickSpawnAngle_자리가_꽉_차도_실패하지_않는다()
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
    public void PickNextOpponent_한_방향으로만_진행한다()
    {
        var angles = new List<float> { 10f, 100f, 200f, 300f };

        int index = EnemyRing.PickNextOpponent(angles, currentAngle: 0f);

        Assert.AreEqual(0, index); // 10도가 반시계 방향으로 가장 가깝다
    }

    [Test]
    public void PickNextOpponent_한_바퀴_돌면_처음으로_돌아온다()
    {
        var angles = new List<float> { 10f, 100f, 200f };

        int index = EnemyRing.PickNextOpponent(angles, currentAngle: 300f);

        Assert.AreEqual(0, index); // 300 → 10(=+70도)가 가장 가깝다. 뒤로 가지 않는다.
    }

    [Test]
    public void PickNextOpponent_자기_자신은_마지막_순번이_된다()
    {
        var angles = new List<float> { 50f, 120f };

        int index = EnemyRing.PickNextOpponent(angles, currentAngle: 50f);

        Assert.AreEqual(1, index); // 델타 0은 360으로 밀려 뒤로 간다
    }

    [Test]
    public void PickNextOpponent_후보가_없으면_음수()
    {
        Assert.AreEqual(-1, EnemyRing.PickNextOpponent(new List<float>(), 0f));
        Assert.AreEqual(-1, EnemyRing.PickNextOpponent(null, 0f));
    }

    // ── 각도 ↔ 위치 왕복 ───────────────────────────────────────────────────

    [Test]
    public void 각도와_방향은_서로_역함수다()
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
    public void 영도는_플러스Z다()
    {
        Vector3 position = EnemyRing.AngleToPosition(Vector3.zero, 0f, 4f);

        Assert.That(Vector3.Distance(position, new Vector3(0f, 0f, 4f)), Is.LessThan(0.001f));
    }
}
