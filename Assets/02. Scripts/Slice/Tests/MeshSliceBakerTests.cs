using System.Collections.Generic;
using NUnit.Framework;
using SliceSpace;
using UnityEngine;

public class MeshSliceBakerTests
{
    private readonly List<Object> spawned = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var o in spawned)
            if (o != null) Object.DestroyImmediate(o);
        spawned.Clear();
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────────────

    /// <summary>한 변 <paramref name="size"/>의 정육면체(중심 원점). 부피 = size³.</summary>
    private Mesh MakeCube(float size = 2f, Vector3 offset = default)
    {
        float h = size * 0.5f;
        var v = new[]
        {
            new Vector3(-h,-h,-h), new Vector3( h,-h,-h), new Vector3( h, h,-h), new Vector3(-h, h,-h),
            new Vector3(-h,-h, h), new Vector3( h,-h, h), new Vector3( h, h, h), new Vector3(-h, h, h)
        };
        for (int i = 0; i < v.Length; i++) v[i] += offset;

        var tris = new[]
        {
            0,2,1, 0,3,2,   // -Z
            4,5,6, 4,6,7,   // +Z
            0,1,5, 0,5,4,   // -Y
            3,7,6, 3,6,2,   // +Y
            0,4,7, 0,7,3,   // -X
            1,2,6, 1,6,5    // +X
        };

        var mesh = new Mesh();
        mesh.SetVertices(new List<Vector3>(v));
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        spawned.Add(mesh);
        return mesh;
    }

    /// <summary>옆면이 y=0에서 이미 두 단으로 나뉜 정육면체 — 절단면 자리에 엣지 루프가 있는 모델.</summary>
    private Mesh MakeRingedCube(float size = 2f)
    {
        float h = size * 0.5f;
        var verts = new List<Vector3>();
        var tris = new List<int>();

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        void Side(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 ab = (a + b) * 0.5f, dc = (d + c) * 0.5f; // y=0 지점에 엣지 루프
            Quad(a, ab, dc, d);
            Quad(ab, b, c, dc);
        }

        Side(new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h), new Vector3(h, -h, -h));
        Side(new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h), new Vector3(-h, -h, h));
        Side(new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h), new Vector3(-h, -h, -h));
        Side(new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(h, -h, h));
        Quad(new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h));
        Quad(new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h), new Vector3(-h, h, -h));

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        spawned.Add(mesh);
        return mesh;
    }

    private void Track(List<SlicedPiece> pieces)
    {
        foreach (var p in pieces) spawned.Add(p.mesh);
    }

    private static float TotalVolume(List<SlicedPiece> pieces)
    {
        float v = 0f;
        foreach (var p in pieces) v += p.volume;
        return v;
    }

    /// <summary>모든 엣지가 정확히 두 삼각형에 공유되면 닫힌 다면체다(교차부에 구멍이 없다는 뜻).</summary>
    private static bool IsClosed(Mesh mesh, float weldEpsilon = 1e-3f)
    {
        var verts = mesh.vertices;
        var map = new Dictionary<Vector3Int, int>();
        var welded = new int[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(verts[i].x / weldEpsilon),
                Mathf.RoundToInt(verts[i].y / weldEpsilon),
                Mathf.RoundToInt(verts[i].z / weldEpsilon));
            if (!map.TryGetValue(key, out int id)) map[key] = id = map.Count;
            welded[i] = id;
        }

        var edgeCount = new Dictionary<(int, int), int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            for (int t = 0; t < tris.Length; t += 3)
            {
                AddEdge(edgeCount, welded[tris[t]], welded[tris[t + 1]]);
                AddEdge(edgeCount, welded[tris[t + 1]], welded[tris[t + 2]]);
                AddEdge(edgeCount, welded[tris[t + 2]], welded[tris[t]]);
            }
        }

        foreach (var kv in edgeCount)
            if (kv.Value != 2) return false;

        return true;
    }

    private static void AddEdge(Dictionary<(int, int), int> counts, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
    }

    /// <summary>
    /// 부호 있는 부피. 모든 면의 감기가 <b>일관되게 바깥</b>을 향할 때만 양수가 된다
    /// (<see cref="IsClosed"/>는 방향을 보지 않으므로 감기 오류를 못 잡는다).
    /// </summary>
    private static float SignedVolume(Mesh mesh)
    {
        var v = mesh.vertices;
        double sum = 0.0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
                sum += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6.0;
        }
        return (float)sum;
    }

    /// <summary>삼각형 (a,b,c)의 앞면 노멀 — Unity 규약은 Cross(b−a, c−a)다.</summary>
    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a);

    // ── 단일 평면 ────────────────────────────────────────────────────────────

    [Test]
    public void 정육면체를_수평면으로_자르면_조각2개_부피절반_캡루프1개()
    {
        var cube = MakeCube(2f);
        var planes = new[] { SlicePlane.FromPointNormal(Vector3.zero, Vector3.up) };

        var pieces = MeshSliceBaker.Slice(cube, planes);
        Track(pieces);

        Assert.AreEqual(2, pieces.Count);
        foreach (var p in pieces)
        {
            Assert.AreEqual(4f, p.volume, 0.01f);  // 8 / 2
            Assert.AreEqual(1, p.capLoopCount);
        }
        Assert.AreEqual(8f, TotalVolume(pieces), 0.01f); // 원본 부피 보존
    }

    [Test]
    public void Dice_3장이면_조각8개()
    {
        var cube = MakeCube(2f);
        var planes = SliceShape.Dice.ToPlanes(cube.bounds);

        var pieces = MeshSliceBaker.Slice(cube, planes);
        Track(pieces);

        Assert.AreEqual(8, pieces.Count);
        Assert.AreEqual(8f, TotalVolume(pieces), 0.05f);
    }

    [Test]
    public void 절단면_기준으로_정점이_한쪽에만_몰린다()
    {
        var cube = MakeCube(2f);
        var plane = SlicePlane.FromPointNormal(Vector3.zero, Vector3.up);

        var pieces = MeshSliceBaker.Slice(cube, new[] { plane });
        Track(pieces);

        foreach (var p in pieces)
        {
            bool anyPositive = false, anyNegative = false;
            foreach (var v in p.mesh.vertices)
            {
                // 피벗이 무게중심으로 옮겨졌으므로 원본 좌표로 되돌려 판정한다.
                float d = plane.SignedDistance(v + p.centroid);
                if (d > 1e-3f) anyPositive = true;
                if (d < -1e-3f) anyNegative = true;
            }
            Assert.IsFalse(anyPositive && anyNegative, "한 조각의 정점이 평면 양쪽에 걸쳐 있습니다(오분류).");
        }
    }

    [Test]
    public void 떨어진_두_덩어리를_자르면_연결요소분해로_4조각()
    {
        // 한 메쉬에 서로 떨어진 정육면체 두 개를 담는다.
        var a = MakeCube(2f, new Vector3(-3f, 0f, 0f));
        var b = MakeCube(2f, new Vector3(3f, 0f, 0f));

        var combined = new Mesh();
        var verts = new List<Vector3>(a.vertices);
        var tris = new List<int>(a.triangles);
        int offset = verts.Count;
        verts.AddRange(b.vertices);
        foreach (int i in b.triangles) tris.Add(i + offset);

        combined.SetVertices(verts);
        combined.SetTriangles(tris, 0);
        combined.RecalculateNormals();
        combined.RecalculateBounds();
        spawned.Add(combined);

        var pieces = MeshSliceBaker.Slice(combined, new[] { SlicePlane.FromPointNormal(Vector3.zero, Vector3.up) });
        Track(pieces);

        Assert.AreEqual(4, pieces.Count);
    }

    [Test]
    public void 서브메쉬_M개면_조각은_M더하기1이고_캡이_마지막()
    {
        // 정육면체를 서브메쉬 2개로 쪼갠다(앞 6삼각형 / 뒤 6삼각형).
        var cube = MakeCube(2f);
        var all = cube.triangles;
        var first = new List<int>();
        var second = new List<int>();
        for (int i = 0; i < all.Length; i += 3)
        {
            var target = i < all.Length / 2 ? first : second;
            target.Add(all[i]); target.Add(all[i + 1]); target.Add(all[i + 2]);
        }

        cube.subMeshCount = 2;
        cube.SetTriangles(first, 0);
        cube.SetTriangles(second, 1);

        var pieces = MeshSliceBaker.Slice(cube, new[] { SlicePlane.FromPointNormal(Vector3.zero, Vector3.up) });
        Track(pieces);

        foreach (var p in pieces)
        {
            Assert.AreEqual(3, p.mesh.subMeshCount, "원본 서브메쉬 2개 + 캡 1개여야 합니다.");
            Assert.AreEqual(2, p.capSubMesh, "캡은 마지막 인덱스여야 합니다.");
            Assert.Greater(p.mesh.GetTriangles(2).Length, 0, "캡 서브메쉬가 비어 있습니다.");
        }
    }

    // ── 교차(다중 획) ───────────────────────────────────────────────────────

    [Test]
    public void 가로세로_2장이면_4조각이고_각_부피는_4분의1()
    {
        var cube = MakeCube(2f);
        var planes = new[]
        {
            SlicePlane.FromPointNormal(Vector3.zero, Vector3.right),
            SlicePlane.FromPointNormal(Vector3.zero, Vector3.up)
        };

        var pieces = MeshSliceBaker.Slice(cube, planes);
        Track(pieces);

        Assert.AreEqual(4, pieces.Count);
        foreach (var p in pieces)
        {
            Assert.AreEqual(2f, p.volume, 0.02f);  // 8 / 4
            Assert.AreEqual(2, p.capLoopCount, "두 절단면을 거쳤으므로 캡 루프가 2개여야 합니다.");
        }
    }

    [Test]
    public void 평면_적용_순서를_바꿔도_결과가_같다()
    {
        var cubeA = MakeCube(2f);
        var cubeB = MakeCube(2f);

        var px = SlicePlane.FromPointNormal(Vector3.zero, Vector3.right);
        var py = SlicePlane.FromPointNormal(Vector3.zero, Vector3.up);

        var forward = MeshSliceBaker.Slice(cubeA, new[] { px, py });
        var reversed = MeshSliceBaker.Slice(cubeB, new[] { py, px });
        Track(forward);
        Track(reversed);

        Assert.AreEqual(forward.Count, reversed.Count);

        var va = forward.ConvertAll(p => p.volume);
        var vb = reversed.ConvertAll(p => p.volume);
        va.Sort();
        vb.Sort();

        for (int i = 0; i < va.Count; i++)
            Assert.AreEqual(va[i], vb[i], 0.02f);
    }

    [Test]
    public void 여러번_잘라도_캡_서브메쉬가_늘어나지_않는다()
    {
        var cube = MakeCube(2f);
        var planes = SliceShape.Dice.ToPlanes(cube.bounds); // 3장

        var pieces = MeshSliceBaker.Slice(cube, planes);
        Track(pieces);

        foreach (var p in pieces)
        {
            // 원본 서브메쉬 1개 + 캡 1개 = 2개. 평면 수(3)만큼 늘어나면 안 된다.
            Assert.AreEqual(2, p.mesh.subMeshCount);
            Assert.AreEqual(1, p.capSubMesh);
        }
    }

    [Test]
    public void 교차지점에_구멍이_없다()
    {
        var cube = MakeCube(2f);
        var planes = new[]
        {
            SlicePlane.FromPointNormal(Vector3.zero, Vector3.right),
            SlicePlane.FromPointNormal(Vector3.zero, Vector3.up)
        };

        var pieces = MeshSliceBaker.Slice(cube, planes);
        Track(pieces);

        foreach (var p in pieces)
            Assert.IsTrue(IsClosed(p.mesh), "조각이 닫힌 다면체가 아닙니다 — 교차부가 뚫렸을 수 있습니다.");
    }

    [Test]
    public void 거의_겹치는_평면의_퇴화조각은_폐기된다()
    {
        var cube = MakeCube(2f);
        var planes = new[]
        {
            SlicePlane.FromPointNormal(Vector3.zero, Vector3.up),
            SlicePlane.FromPointNormal(new Vector3(0f, 0.0005f, 0f), Vector3.up)
        };

        var pieces = MeshSliceBaker.Slice(cube, planes, new SliceOptions(), out int discarded);
        Track(pieces);

        Assert.AreEqual(2, pieces.Count, "두께 0에 가까운 파편은 폐기되고 위/아래 2조각만 남아야 합니다.");
        Assert.GreaterOrEqual(discarded, 1);
    }

    // ── 캡 감기 방향 ────────────────────────────────────────────────────────
    //
    // 이 두 테스트는 "절단면이 투명하게 보인다"(캡이 백페이스 컬링됨) 회귀를 막는다.
    // 기존 테스트로는 잡히지 않았다:
    //  · 부피 검사 — 절단 평면이 원점을 지나면 캡의 부피 기여가 정확히 0이라(dot(캡 중심, 노멀)=0)
    //    감기를 뒤집어도 값이 그대로다. 게다가 Abs()를 씌워 부호도 버렸다.
    //  · IsClosed — 엣지 공유 수만 세므로 방향을 보지 않는다.

    [Test]
    public void 캡_삼각형이_절단면_바깥을_향한다()
    {
        var cube = MakeCube(2f);
        var plane = SlicePlane.FromPointNormal(Vector3.zero, Vector3.up);

        var pieces = MeshSliceBaker.Slice(cube, new[] { plane });
        Track(pieces);

        foreach (var p in pieces)
        {
            // +Y쪽 조각의 캡은 아래(−Y)를, −Y쪽 조각의 캡은 위(+Y)를 향해야 한다.
            Vector3 expected = p.centroid.y > 0f ? Vector3.down : Vector3.up;

            var verts = p.mesh.vertices;
            var capTris = p.mesh.GetTriangles(p.capSubMesh);
            Assert.Greater(capTris.Length, 0, "캡 삼각형이 없습니다.");

            for (int i = 0; i < capTris.Length; i += 3)
            {
                Vector3 n = FaceNormal(verts[capTris[i]], verts[capTris[i + 1]], verts[capTris[i + 2]]);
                Assert.Greater(Vector3.Dot(n.normalized, expected), 0f,
                    "캡 삼각형이 반대쪽을 향합니다 — 렌더 시 백페이스 컬링되어 단면이 투명하게 보입니다.");
            }
        }
    }

    [Test]
    public void 조각의_모든_면이_일관되게_바깥을_향한다()
    {
        // 원점을 지나지 않는 평면으로 잘라야 캡의 부호 기여가 0이 아니게 되어 감기 오류에 민감해진다.
        var cube = MakeCube(2f);
        var planes = new[]
        {
            SlicePlane.FromPointNormal(new Vector3(0f, 0.4f, 0f), Vector3.up),
            SlicePlane.FromPointNormal(new Vector3(0.3f, 0f, 0f), Vector3.right)
        };

        var pieces = MeshSliceBaker.Slice(cube, planes);
        Track(pieces);

        Assert.AreEqual(4, pieces.Count);
        foreach (var p in pieces)
            Assert.Greater(SignedVolume(p.mesh), 0f,
                "부호 있는 부피가 음수 — 면 감기가 뒤집힌 조각이 있습니다.");
    }

    [Test]
    public void 절단면에_이미_엣지루프가_있어도_캡이_생성된다()
    {
        // 옆면이 y=0에서 이미 두 단으로 나뉜 큐브. 실제 프롭에서 흔하고,
        // 프리셋 평면이 bounds 중심을 지나므로 대칭형 모델에서는 오히려 기본 상황이다.
        // 이때는 평면을 '가로지르는' 삼각형이 하나도 없어서, 평면 위에 놓인 엣지를
        // 경계로 인정하지 않으면 캡이 통째로 만들어지지 않는다(= 단면이 뚫려 보인다).
        var cube = MakeRingedCube(2f);

        var pieces = MeshSliceBaker.Slice(cube, new[] { SlicePlane.FromPointNormal(Vector3.zero, Vector3.up) });
        Track(pieces);

        Assert.AreEqual(2, pieces.Count);
        foreach (var p in pieces)
        {
            Assert.Greater(p.mesh.GetTriangles(p.capSubMesh).Length, 0,
                "절단면에 기존 엣지 루프가 있는 모델에서 캡이 생성되지 않았습니다.");
            Assert.AreEqual(1, p.capLoopCount);
            Assert.IsTrue(IsClosed(p.mesh), "캡이 붙었는데도 조각이 닫히지 않았습니다.");
        }
    }

    [Test]
    public void 평면이_모델을_스치기만_하면_캡을_만들지_않는다()
    {
        // 평면이 큐브의 윗면(y=+1)에 정확히 접한다. 갈린 게 아니므로 허공에 판을 만들면 안 된다.
        var cube = MakeCube(2f);

        var pieces = MeshSliceBaker.Slice(cube, new[] { SlicePlane.FromPointNormal(new Vector3(0f, 1f, 0f), Vector3.up) });
        Track(pieces);

        Assert.AreEqual(1, pieces.Count, "접하기만 했는데 조각이 나뉘었습니다.");
        Assert.AreEqual(0, pieces[0].mesh.GetTriangles(pieces[0].capSubMesh).Length,
            "갈리지 않았는데 캡이 생성됐습니다.");
    }

    [Test]
    public void 평면이_없으면_원본_하나가_그대로_나온다()
    {
        var cube = MakeCube(2f);

        var pieces = MeshSliceBaker.Slice(cube, new SlicePlane[0]);
        Track(pieces);

        Assert.AreEqual(1, pieces.Count);
        Assert.AreEqual(8f, pieces[0].volume, 0.01f);
    }
}
