using NUnit.Framework;
using PatternSpace;
using UnityEngine;

/// <summary>
/// <see cref="DuelGap"/> 검증.
///
/// <para>간격은 <b>화면에 그대로 보이는 값</b>이지만 두 경로가 조용히 갈린다 —
/// 커브가 없으면 상수(하한 있음), 있으면 커브값(하한 <b>없음</b>: 음수가 목적이다).
/// 여기가 뒤집히면 "지나치기"가 통째로 사라지거나 둘이 겹쳐 선다.</para>
/// </summary>
public class DuelGapTests
{
    private const float Tolerance = 1e-4f;

    /// <summary>커브가 없으면 기준 거리 + 패턴 보정(예전 상수 경로).</summary>
    [Test]
    public void ConstantPathAddsOffsetToBase()
    {
        Assert.AreEqual(1.6f, DuelGap.At(null, 0f, 1f, 0.6f), Tolerance);
        Assert.AreEqual(1.6f, DuelGap.At(new AnimationCurve(), 12.3f, 1f, 0.6f), Tolerance);
    }

    /// <summary>보정이 기준을 통째로 깎아도 둘이 겹쳐 서지는 않는다.</summary>
    [Test]
    public void ConstantPathClampsToMinimum()
    {
        Assert.AreEqual(DuelGap.MinConstantGap, DuelGap.At(null, 0f, 1f, -5f), Tolerance);
    }

    [Test]
    public void CurveInterpolatesOverTime()
    {
        var curve = AnimationCurve.Linear(-0.5f, 1.2f, 0f, -0.8f);

        Assert.AreEqual(1.2f, DuelGap.At(curve, -0.5f, 1f, 0f), Tolerance);
        Assert.AreEqual(0.2f, DuelGap.At(curve, -0.25f, 1f, 0f), Tolerance);
        Assert.AreEqual(-0.8f, DuelGap.At(curve, 0f, 1f, 0f), Tolerance);
    }

    /// <summary>구간 밖은 끝 키 값으로 홀드된다 — 홀드 코드를 따로 쓰지 않는 근거다.</summary>
    [Test]
    public void CurveHoldsEndKeysOutsideRange()
    {
        var curve = AnimationCurve.Linear(-0.5f, 1.2f, 0f, -0.8f);

        Assert.AreEqual(1.2f, DuelGap.At(curve, -5f, 1f, 0f), Tolerance);
        Assert.AreEqual(-0.8f, DuelGap.At(curve, 5f, 1f, 0f), Tolerance);
    }

    /// <summary>상수 경로의 하한이 커브에 끼어들면 '지나치기'가 통째로 사라진다.</summary>
    [Test]
    public void CurveKeepsNegativeGap()
    {
        var curve = AnimationCurve.Constant(-1f, 1f, -0.9f);

        Assert.AreEqual(-0.9f, DuelGap.At(curve, 0f, 1f, 3f), Tolerance);
    }

    /// <summary>
    /// 관통해 적 뒤에 선 자리에서 다시 재면 방향이 뒤집힌다 — 그때는 직전 축이 이겨야 한다.
    /// 여기가 뒤집히면 다음 패턴부터 커브가 거울로 돌아 물러날 때 적을 뚫고 건너간다.
    /// </summary>
    [Test]
    public void ResolveAxisKeepsPreviousWhenFlipped()
    {
        Vector3 previous = Vector3.forward;

        Assert.AreEqual(previous, DuelGap.ResolveAxis(new Vector3(0f, 0f, -3f), previous));
    }

    /// <summary>상대가 바뀌면(직전 축 없음) 그 자리에서 새로 잡는다 — 유지가 새 교전을 막지 않는다.</summary>
    [Test]
    public void ResolveAxisTakesNewDirectionWhenNotFlipped()
    {
        Assert.AreEqual(Vector3.right, DuelGap.ResolveAxis(new Vector3(4f, 0f, 0f), Vector3.zero));
        Assert.AreEqual(Vector3.right, DuelGap.ResolveAxis(new Vector3(4f, 0f, 0f), Vector3.forward));
    }

    /// <summary>겹쳐 서서 방향을 잴 수 없으면 직전 축, 그것도 없으면 forward.</summary>
    [Test]
    public void ResolveAxisFallsBackWhenDegenerate()
    {
        Assert.AreEqual(Vector3.right, DuelGap.ResolveAxis(Vector3.zero, new Vector3(9f, 0f, 0f)));
        Assert.AreEqual(Vector3.forward, DuelGap.ResolveAxis(Vector3.zero, Vector3.zero));
    }

    /// <summary>
    /// 첫 키 시각 = 결투 배치의 기준 시각. 여기가 틀리면 접근이 끝난 자리와 커브 첫 값이 달라
    /// 커브가 열리는 순간 그 차이만큼 <b>순간이동</b>한다.
    /// </summary>
    [Test]
    public void StartTimeIsFirstKeyTime()
    {
        Assert.AreEqual(0f, DuelGap.StartTime(null), Tolerance);
        Assert.AreEqual(0f, DuelGap.StartTime(new AnimationCurve()), Tolerance);
        Assert.AreEqual(-0.5f, DuelGap.StartTime(AnimationCurve.Linear(-0.5f, 1.2f, 0.35f, -1.5f)), Tolerance);
    }

    /// <summary>
    /// 마지막 키 시각 = 이 패턴이 위치를 소유하는 끝. 0을 돌려주면 임팩트 이후 구간이
    /// 다음 계획에 덮여 <b>한 번도 재생되지 않는다</b>.
    /// </summary>
    [Test]
    public void EndTimeIsLastKeyTime()
    {
        Assert.AreEqual(0f, DuelGap.EndTime(null), Tolerance);
        Assert.AreEqual(0f, DuelGap.EndTime(new AnimationCurve()), Tolerance);
        Assert.AreEqual(0.35f, DuelGap.EndTime(AnimationCurve.Linear(-0.5f, 1.2f, 0.35f, -1.5f)), Tolerance);
    }

    [Test]
    public void HasChecksKeyCount()
    {
        Assert.IsFalse(DuelGap.Has(null));
        Assert.IsFalse(DuelGap.Has(new AnimationCurve()));
        Assert.IsTrue(DuelGap.Has(AnimationCurve.Constant(0f, 1f, 1f)));
    }
}
