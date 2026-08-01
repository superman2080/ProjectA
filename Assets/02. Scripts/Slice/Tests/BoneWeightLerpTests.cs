using NUnit.Framework;
using SliceSpace;
using UnityEngine;

/// <summary>
/// <see cref="MeshSliceBaker.BoneWeightUtil"/> 검증.
///
/// <para>가중치 보간은 <b>단순 Lerp가 성립하지 않는 유일한 정점 속성</b>이라 따로 테스트한다 —
/// 두 정점이 최대 8개 본에 영향받는데 결과는 4개만 담을 수 있어, 병합·선별·재정규화가 들어간다.
/// 여기가 깨지면 절단면 정점이 엉뚱한 본을 따라가는데, 실행해 보기 전에는 알 수 없다.</para>
/// </summary>
public class BoneWeightLerpTests
{
    private const float Tolerance = 1e-4f;

    private static BoneWeight Make(params (int bone, float weight)[] influences)
    {
        var w = new BoneWeight();
        for (int i = 0; i < influences.Length && i < 4; i++)
        {
            switch (i)
            {
                case 0: w.boneIndex0 = influences[i].bone; w.weight0 = influences[i].weight; break;
                case 1: w.boneIndex1 = influences[i].bone; w.weight1 = influences[i].weight; break;
                case 2: w.boneIndex2 = influences[i].bone; w.weight2 = influences[i].weight; break;
                case 3: w.boneIndex3 = influences[i].bone; w.weight3 = influences[i].weight; break;
            }
        }
        return w;
    }

    private static float Sum(BoneWeight w) => w.weight0 + w.weight1 + w.weight2 + w.weight3;

    private static float WeightOf(BoneWeight w, int bone)
    {
        float sum = 0f;
        if (w.weight0 > 0f && w.boneIndex0 == bone) sum += w.weight0;
        if (w.weight1 > 0f && w.boneIndex1 == bone) sum += w.weight1;
        if (w.weight2 > 0f && w.boneIndex2 == bone) sum += w.weight2;
        if (w.weight3 > 0f && w.boneIndex3 == bone) sum += w.weight3;
        return sum;
    }

    private static int InfluenceCount(BoneWeight w)
    {
        int n = 0;
        if (w.weight0 > 0f) n++;
        if (w.weight1 > 0f) n++;
        if (w.weight2 > 0f) n++;
        if (w.weight3 > 0f) n++;
        return n;
    }

    [Test]
    public void SameSingleBone_StaysSingleWithFullWeight()
    {
        var a = Make((7, 1f));
        var b = Make((7, 1f));

        var result = MeshSliceBaker.BoneWeightUtil.Lerp(a, b, 0.5f);

        Assert.AreEqual(1, InfluenceCount(result), "같은 본 하나끼리 섞으면 영향 본도 하나여야 한다.");
        Assert.AreEqual(7, result.boneIndex0);
        Assert.AreEqual(1f, result.weight0, Tolerance);
    }

    [Test]
    public void AtEndpoints_MatchesOriginals()
    {
        var a = Make((1, 0.6f), (2, 0.4f));
        var b = Make((3, 1f));

        var atA = MeshSliceBaker.BoneWeightUtil.Lerp(a, b, 0f);
        Assert.AreEqual(0.6f, WeightOf(atA, 1), Tolerance);
        Assert.AreEqual(0.4f, WeightOf(atA, 2), Tolerance);
        Assert.AreEqual(0f, WeightOf(atA, 3), Tolerance);

        var atB = MeshSliceBaker.BoneWeightUtil.Lerp(a, b, 1f);
        Assert.AreEqual(1f, WeightOf(atB, 3), Tolerance);
        Assert.AreEqual(0f, WeightOf(atB, 1), Tolerance);
    }

    [Test]
    public void Midpoint_SplitsEvenlyAndNormalizes()
    {
        var a = Make((1, 1f));
        var b = Make((2, 1f));

        var result = MeshSliceBaker.BoneWeightUtil.Lerp(a, b, 0.5f);

        Assert.AreEqual(1f, Sum(result), Tolerance, "가중치 합은 언제나 1이어야 한다.");
        Assert.AreEqual(0.5f, WeightOf(result, 1), Tolerance);
        Assert.AreEqual(0.5f, WeightOf(result, 2), Tolerance);
    }

    [Test]
    public void EightInfluences_KeepsTopFourAndRenormalizes()
    {
        // 서로 다른 본 8개 — 결과는 4개로 줄고 합은 1이어야 한다.
        var a = Make((1, 0.4f), (2, 0.3f), (3, 0.2f), (4, 0.1f));
        var b = Make((5, 0.4f), (6, 0.3f), (7, 0.2f), (8, 0.1f));

        var result = MeshSliceBaker.BoneWeightUtil.Lerp(a, b, 0.5f);

        Assert.AreEqual(4, InfluenceCount(result), "영향 본은 4개를 넘을 수 없다.");
        Assert.AreEqual(1f, Sum(result), Tolerance);

        // 가장 큰 두 본(1, 5)은 반드시 살아남아야 한다.
        Assert.Greater(WeightOf(result, 1), 0f);
        Assert.Greater(WeightOf(result, 5), 0f);
    }

    [Test]
    public void ZeroWeightEntries_DoNotDisplaceRealInfluences()
    {
        // 0가중치 슬롯이 상위 4개 자리를 밀어내면 실제 영향 본이 잘려 나간다.
        var a = Make((1, 0.5f), (2, 0.5f), (99, 0f), (98, 0f));
        var b = Make((3, 0.5f), (4, 0.5f), (97, 0f), (96, 0f));

        var result = MeshSliceBaker.BoneWeightUtil.Lerp(a, b, 0.5f);

        Assert.AreEqual(4, InfluenceCount(result));
        Assert.AreEqual(1f, Sum(result), Tolerance);
        Assert.AreEqual(0f, WeightOf(result, 99), Tolerance, "0가중치 본이 결과에 들어오면 안 된다.");
        Assert.AreEqual(0f, WeightOf(result, 96), Tolerance);
    }

    [Test]
    public void EmptyWeights_StayEmpty()
    {
        // 정적 메쉬 경로 — 가중치가 없으면 보간도 없어야 한다(기록 자체를 건너뛰기 위함).
        var result = MeshSliceBaker.BoneWeightUtil.Lerp(default, default, 0.5f);

        Assert.AreEqual(0, InfluenceCount(result));
    }

    [Test]
    public void OneSidedWeights_PassThroughUnchanged()
    {
        var a = Make((5, 1f));

        var fromA = MeshSliceBaker.BoneWeightUtil.Lerp(a, default, 0.5f);
        Assert.AreEqual(1f, WeightOf(fromA, 5), Tolerance);

        var fromB = MeshSliceBaker.BoneWeightUtil.Lerp(default, a, 0.5f);
        Assert.AreEqual(1f, WeightOf(fromB, 5), Tolerance);
    }

    [Test]
    public void Average_ProducesNormalizedWeights()
    {
        // 캡 중심 정점이 림 가중치에서 물려받는 경로.
        var loop = new[]
        {
            new MeshSliceBaker.Vertex { boneWeight = Make((1, 1f)) },
            new MeshSliceBaker.Vertex { boneWeight = Make((2, 1f)) },
            new MeshSliceBaker.Vertex { boneWeight = Make((1, 0.5f), (3, 0.5f)) }
        };

        var result = MeshSliceBaker.BoneWeightUtil.Average(loop);

        Assert.AreEqual(1f, Sum(result), Tolerance);
        Assert.Greater(WeightOf(result, 1), WeightOf(result, 3), "두 번 나온 본이 더 큰 가중치를 가져야 한다.");
    }
}
