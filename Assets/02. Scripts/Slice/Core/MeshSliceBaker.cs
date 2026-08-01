using System;
using System.Collections.Generic;
using UnityEngine;

namespace SliceSpace
{
    /// <summary>절단 결과 조각 하나. <see cref="mesh"/>의 피벗은 이미 무게중심으로 정렬되어 있고, 원본 기준 위치가 <see cref="centroid"/>다.</summary>
    public sealed class SlicedPiece
    {
        public Mesh mesh;
        public Vector3 centroid;
        public float volume;
        public int capSubMesh;   // 캡이 들어간 서브메쉬 인덱스(= 원본 서브메쉬 수 M)
        public int capLoopCount; // 이 조각이 가진 단면 폐루프 수(디버그/프리뷰 표시용)
    }

    public sealed class SliceOptions
    {
        /// <summary>정점 용접 허용오차. 캡 폐루프 추출과 연결 요소 분해가 이 값으로 정점을 동일시한다.</summary>
        public float weldEpsilon = 1e-4f;

        /// <summary>원본 부피 대비 이 비율 미만인 조각은 퇴화로 보고 폐기한다(기본 0.1%).</summary>
        public float minPieceVolumeRatio = 0.001f;

        /// <summary>캡 UV 투영값에 곱하는 스케일. 겉면과 단면의 텍셀 밀도를 맞추는 수동 손잡이.</summary>
        public float capUvScale = 1f;
    }

    /// <summary>
    /// 메쉬를 평면 여러 장으로 절단해 조각 N개를 만드는 <b>순수 기하 로직</b>(에디터 API 비의존 — 테스트 가능).
    ///
    /// <para><b>다중 평면(획 여러 개)의 교차</b>는 "이미 잘린 조각을 다시 자른다"는 순차 적용만으로 성립한다.
    /// 평면이 무한하므로 가로 1획 + 세로 1획은 자연히 4조각이 된다. 이때 지켜야 하는 것이 셋 있다:
    /// (1) 앞 평면이 만든 <b>캡도 일반 지오메트리로 취급</b>해 다음 평면이 그대로 자른다(안 그러면 교차부가 뚫린다),
    /// (2) 캡은 몇 번을 자르든 <b>서브메쉬 하나(인덱스 M)에 병합</b>한다(머티리얼 슬롯 규칙 M+1을 지키기 위함),
    /// (3) 교차선의 T-정션을 막기 위해 캡 정점에도 동일한 <c>weldEpsilon</c>을 적용한다.</para>
    ///
    /// <para>평면 절단 결과는 반평면 교집합이라 <b>적용 순서와 무관</b>하다.</para>
    /// </summary>
    public static class MeshSliceBaker
    {
        /// <summary>정적 메쉬 경로의 거부 조건. 통과하면 null, 아니면 사용자에게 보일 사유.</summary>
        public static string ValidateStatic(Mesh source)
        {
            string common = ValidateCommon(source);
            if (common != null) return common;

            if (source.boneWeights != null && source.boneWeights.Length > 0)
                return $"'{source.name}'은 스킨드 메쉬입니다. 휴머노이드 모드를 쓰세요.";

            return null;
        }

        /// <summary>
        /// 스킨드(휴머노이드) 경로의 거부 조건. 가중치가 <b>필수</b>다.
        /// 블렌드셰이프는 계속 거부한다 — 절단 후 델타를 조각별로 나눌 방법이 없다.
        /// </summary>
        public static string ValidateSkinned(Mesh source)
        {
            string common = ValidateCommon(source);
            if (common != null) return common;

            if (source.boneWeights == null || source.boneWeights.Length == 0)
                return $"'{source.name}'에 boneWeights가 없습니다. 스킨드 메쉬가 아닙니다.";

            return null;
        }

        /// <summary>기존 호출자 호환용. 정적 경로 검증으로 위임한다.</summary>
        public static string Validate(Mesh source) => ValidateStatic(source);

        private static string ValidateCommon(Mesh source)
        {
            if (source == null) return "원본 메쉬가 없습니다.";
            if (!source.isReadable) return $"'{source.name}'의 Read/Write가 꺼져 있습니다. 임포트 설정에서 켜 주세요.";
            if (source.blendShapeCount > 0)
                return $"'{source.name}'에 블렌드셰이프가 있습니다. 절단 후 델타를 나눌 수 없어 지원하지 않습니다.";

            for (int i = 0; i < source.subMeshCount; i++)
            {
                if (source.GetTopology(i) != MeshTopology.Triangles)
                    return $"'{source.name}'의 서브메쉬 {i}가 삼각형 토폴로지가 아닙니다({source.GetTopology(i)}).";
            }

            return null;
        }

        /// <summary>
        /// <paramref name="source"/>를 <paramref name="planes"/>로 순차 절단하고, 연결 요소별로 분해해 조각 목록을 돌려준다.
        /// 평면이 비면 원본 하나를 그대로(피벗만 정렬해) 돌려준다.
        /// </summary>
        public static List<SlicedPiece> Slice(Mesh source, IReadOnlyList<SlicePlane> planes, SliceOptions options = null)
            => Slice(source, planes, options, out _);

        /// <summary>퇴화(<see cref="SliceOptions.minPieceVolumeRatio"/> 미만)로 폐기된 조각 수를 함께 돌려주는 오버로드.</summary>
        public static List<SlicedPiece> Slice(Mesh source, IReadOnlyList<SlicePlane> planes, SliceOptions options, out int discardedCount)
        {
            options ??= new SliceOptions();

            var work = WorkMesh.FromMesh(source);
            float sourceVolume = Mathf.Abs(work.ComputeVolume());

            var pieces = new List<WorkMesh> { work };
            if (planes != null)
            {
                foreach (var plane in planes)
                {
                    var next = new List<WorkMesh>(pieces.Count * 2);
                    foreach (var piece in pieces)
                        SplitByPlane(piece, plane, options, next);
                    pieces = next;
                }
            }

            // 연결 요소 분해는 마지막에 한 번만 한다. 절단은 연결성을 보지 않으므로
            // 평면마다 분해하는 것과 결과가 같고, 그쪽이 더 싸다.
            var components = new List<WorkMesh>();
            foreach (var piece in pieces)
                piece.SplitConnectedComponents(options.weldEpsilon, components);

            var result = new List<SlicedPiece>(components.Count);
            float minVolume = sourceVolume * options.minPieceVolumeRatio;
            discardedCount = 0;

            foreach (var comp in components)
            {
                if (comp.TriangleCount == 0) continue;

                float volume = Mathf.Abs(comp.ComputeVolume());
                if (volume < minVolume) // 퇴화 조각(두께 ≈ 0) 폐기
                {
                    discardedCount++;
                    continue;
                }

                Vector3 centroid = comp.ComputeCentroid();
                comp.Translate(-centroid); // 피벗을 무게중심으로

                result.Add(new SlicedPiece
                {
                    mesh = comp.ToMesh(),
                    centroid = centroid,
                    volume = volume,
                    capSubMesh = comp.CapSubMesh,
                    capLoopCount = comp.capLoopCount
                });
            }

            return result;
        }

        // ── 평면 1장 처리 ────────────────────────────────────────────────────────

        private static void SplitByPlane(WorkMesh src, SlicePlane plane, SliceOptions options, List<WorkMesh> output)
        {
            var front = src.CreateEmptyLike();
            var back = src.CreateEmptyLike();

            // 삼각형을 실제로 가로질러 생긴 교차 엣지.
            // <b>Vertex로 들고 다닌다</b> — position만 남기면 캡 정점이 스키닝 가중치를 잃는다.
            var cutSegments = new List<(Vertex a, Vertex b)>();

            // 평면 위에 그대로 놓인 엣지. 절단면 자리에 <b>이미 엣지 루프가 있는 모델</b>에서는
            // 어떤 삼각형도 평면을 가로지르지 않아 cutSegments가 비고, 이것만이 유일한 경계 정보가 된다.
            // (프리셋 평면은 bounds 중심을 지나므로 대칭형 프롭에서 이 상황이 오히려 흔하다.)
            var onPlaneEdges = new List<(Vertex a, Vertex b)>();

            float eps = options.weldEpsilon;

            // 평면 '바깥'(부호가 0이 아닌 쪽)에 지오메트리가 있는가. 캡을 만들지 말지의 유일한 근거다.
            bool hasStrictFront = false;
            bool hasStrictBack = false;

            for (int sub = 0; sub < src.SubMeshCount; sub++)
            {
                var tris = src.subTriangles[sub];
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    float d0 = plane.SignedDistance(src.positions[i0]);
                    float d1 = plane.SignedDistance(src.positions[i1]);
                    float d2 = plane.SignedDistance(src.positions[i2]);

                    int s0 = Sign(d0, eps), s1 = Sign(d1, eps), s2 = Sign(d2, eps);

                    bool anyFront = s0 > 0 || s1 > 0 || s2 > 0;
                    bool anyBack = s0 < 0 || s1 < 0 || s2 < 0;

                    // 평면 '위'(부호 0)가 아니라 '바깥'에 있는 지오메트리만 센다 — 아래 actuallySplit 판정의 근거.
                    hasStrictFront |= anyFront;
                    hasStrictBack |= anyBack;

                    if (!anyBack)
                    {
                        front.AppendTriangle(src, sub, i0, i1, i2);
                        CollectOnPlaneEdge(src, i0, i1, i2, s0, s1, s2, onPlaneEdges);
                        continue;
                    }
                    if (!anyFront)
                    {
                        back.AppendTriangle(src, sub, i0, i1, i2);
                        CollectOnPlaneEdge(src, i0, i1, i2, s0, s1, s2, onPlaneEdges);
                        continue;
                    }

                    // 걸침 — 삼각형을 평면으로 자른다.
                    ClipTriangle(src, sub, i0, i1, i2, d0, d1, d2, eps, front, back, cutSegments);
                }
            }

            // 평면 위 엣지는 '실제로 갈렸을 때'만 경계로 인정한다.
            // 평면이 모델을 스치기만 한 경우(한쪽에만 지오메트리가 있음) 캡을 만들면 허공에 판이 생긴다.
            //
            // 조각의 삼각형 수로 재면 안 된다 — 평면에 딱 얹힌 면(세 꼭짓점 부호가 전부 0)은
            // 어느 한쪽으로 배정되므로, 스치기만 해도 양쪽에 삼각형이 생겨 "갈렸다"로 오판한다.
            // 판정의 근거는 '평면 바깥에 지오메트리가 있는가'여야 한다.
            bool actuallySplit = hasStrictFront && hasStrictBack;
            if (actuallySplit)
                cutSegments.AddRange(onPlaneEdges);

            // 앞/뒤 조각에 각각 캡을 붙인다. 캡은 항상 CapSubMesh(인덱스 M) 하나에 병합된다.
            int loops = 0;
            if (cutSegments.Count > 0)
            {
                var loopList = BuildLoops(cutSegments, eps);
                loops = loopList.Count;

                // 앞 조각의 캡은 평면 뒤쪽(-normal)을 향하고, 뒤 조각의 캡은 앞쪽(+normal)을 향한다.
                foreach (var loop in loopList)
                {
                    AppendCap(front, loop, plane, -plane.normal, options);
                    AppendCap(back, loop, plane, plane.normal, options);
                }
            }

            front.capLoopCount = src.capLoopCount + loops;
            back.capLoopCount = src.capLoopCount + loops;

            if (front.TriangleCount > 0) output.Add(front);
            if (back.TriangleCount > 0) output.Add(back);
        }

        private static int Sign(float d, float eps) => d > eps ? 1 : (d < -eps ? -1 : 0);

        /// <summary>
        /// 평면을 가로지르지 않는 삼각형이라도 <b>두 정점이 평면 위에 놓여 있으면 그 엣지가 절단 경계</b>다.
        /// 이 경로가 없으면 절단면 자리에 이미 엣지 루프가 있는 모델에서 캡이 아예 만들어지지 않는다.
        /// 같은 엣지가 앞/뒤 삼각형 양쪽에서 들어오지만 <see cref="BuildLoops"/>가 중복을 걸러낸다.
        /// </summary>
        private static void CollectOnPlaneEdge(
            WorkMesh src, int i0, int i1, int i2, int s0, int s1, int s2,
            List<(Vertex a, Vertex b)> onPlaneEdges)
        {
            int zeros = (s0 == 0 ? 1 : 0) + (s1 == 0 ? 1 : 0) + (s2 == 0 ? 1 : 0);
            if (zeros != 2) return; // 3개면 평면에 눕는 삼각형 — 경계가 아니다

            if (s0 == 0 && s1 == 0) onPlaneEdges.Add((src.GetVertex(i0), src.GetVertex(i1)));
            else if (s1 == 0 && s2 == 0) onPlaneEdges.Add((src.GetVertex(i1), src.GetVertex(i2)));
            else onPlaneEdges.Add((src.GetVertex(i2), src.GetVertex(i0)));
        }

        /// <summary>걸친 삼각형 하나를 평면으로 잘라 앞/뒤 다각형을 만들고 각각 팬 삼각분할해 편입한다.</summary>
        private static void ClipTriangle(
            WorkMesh src, int sub,
            int i0, int i1, int i2,
            float d0, float d1, float d2,
            float eps,
            WorkMesh front, WorkMesh back,
            List<(Vertex, Vertex)> cutSegments)
        {
            var idx = new[] { i0, i1, i2 };
            var dist = new[] { d0, d1, d2 };

            var frontPoly = new List<Vertex>(4);
            var backPoly = new List<Vertex>(4);
            var onPlane = new List<Vertex>(2);

            for (int e = 0; e < 3; e++)
            {
                int cur = e, nxt = (e + 1) % 3;
                Vertex vc = src.GetVertex(idx[cur]);
                float dc = dist[cur], dn = dist[nxt];

                if (dc >= 0f) frontPoly.Add(vc);
                if (dc <= 0f) backPoly.Add(vc);

                // 평면 위에 놓인 정점은 그 자체가 절단선의 끝점이다(교점을 새로 만들지 않는다).
                if (Mathf.Abs(dc) <= eps)
                {
                    onPlane.Add(vc);
                    continue;
                }

                // 부호가 실제로 교차할 때만 교점을 만든다.
                if ((dc > eps && dn < -eps) || (dc < -eps && dn > eps))
                {
                    float t = dc / (dc - dn);
                    Vertex vi = Vertex.Lerp(vc, src.GetVertex(idx[nxt]), t);
                    frontPoly.Add(vi);
                    backPoly.Add(vi);
                    onPlane.Add(vi);
                }
            }

            AppendFan(front, sub, frontPoly);
            AppendFan(back, sub, backPoly);

            // Vertex를 통째로 넘긴다 — 여기서 position만 남기면 캡이 스키닝 가중치를 잃는다.
            if (onPlane.Count >= 2)
                cutSegments.Add((onPlane[0], onPlane[1]));
        }

        private static void AppendFan(WorkMesh target, int sub, List<Vertex> poly)
        {
            if (poly.Count < 3) return;
            for (int i = 1; i < poly.Count - 1; i++)
                target.AppendTriangle(sub, poly[0], poly[i], poly[i + 1]);
        }

        // ── 캡(단면) 생성 ────────────────────────────────────────────────────────

        /// <summary>
        /// 교차 엣지들을 용접해 <b>모든 폐루프</b>를 추출한다. 도넛은 2개, 다리 4개 형태는 4개가 나온다 —
        /// 루프가 하나라고 가정하지 않는다.
        /// </summary>
        private static List<List<Vertex>> BuildLoops(List<(Vertex a, Vertex b)> segments, float weldEpsilon)
        {
            var weld = new VertexWelder(weldEpsilon);
            var adjacency = new Dictionary<int, List<int>>();
            var edges = new HashSet<long>();

            // 용접 인덱스 → 대표 Vertex. 용접의 정의상 같은 위치면 동일 정점이므로 먼저 온 것을 대표로 쓴다.
            var vertexByIndex = new Dictionary<int, Vertex>();

            foreach (var (a, b) in segments)
            {
                int ia = weld.Add(a.position);
                int ib = weld.Add(b.position);
                if (!vertexByIndex.ContainsKey(ia)) vertexByIndex[ia] = a;
                if (!vertexByIndex.ContainsKey(ib)) vertexByIndex[ib] = b;
                if (ia == ib) continue; // 길이 0 세그먼트

                long key = EdgeKey(ia, ib);
                if (!edges.Add(key)) continue; // 중복 엣지

                if (!adjacency.TryGetValue(ia, out var la)) adjacency[ia] = la = new List<int>();
                if (!adjacency.TryGetValue(ib, out var lb)) adjacency[ib] = lb = new List<int>();
                la.Add(ib);
                lb.Add(ia);
            }

            var loops = new List<List<Vertex>>();
            var visitedEdges = new HashSet<long>();

            foreach (var start in adjacency.Keys)
            {
                foreach (int first in adjacency[start])
                {
                    if (visitedEdges.Contains(EdgeKey(start, first))) continue;

                    var loop = new List<Vertex> { vertexByIndex[start] };
                    int prev = start, cur = first;
                    visitedEdges.Add(EdgeKey(prev, cur));

                    // 폐루프가 닫힐 때까지, 또는 더 갈 곳이 없을 때까지 따라간다.
                    while (cur != start)
                    {
                        loop.Add(vertexByIndex[cur]);

                        int next = -1;
                        if (adjacency.TryGetValue(cur, out var neighbours))
                        {
                            foreach (int n in neighbours)
                            {
                                if (n == prev) continue;
                                if (visitedEdges.Contains(EdgeKey(cur, n))) continue;
                                next = n;
                                break;
                            }
                        }

                        if (next < 0) break; // 열린 사슬 — 캡을 만들지 않는다
                        visitedEdges.Add(EdgeKey(cur, next));
                        prev = cur;
                        cur = next;
                    }

                    if (cur == start && loop.Count >= 3)
                        loops.Add(loop);
                }
            }

            return loops;
        }

        private static long EdgeKey(int a, int b)
        {
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            return ((long)lo << 32) | (uint)hi;
        }

        /// <summary>루프 중심을 추가해 팬 삼각형으로 캡을 만든다. 항상 <see cref="WorkMesh.CapSubMesh"/>에 넣는다.</summary>
        private static void AppendCap(WorkMesh target, List<Vertex> loop, SlicePlane plane, Vector3 capNormal, SliceOptions options)
        {
            if (loop.Count < 3) return;

            Vector3 center = Vector3.zero;
            foreach (var v in loop) center += v.position;
            center /= loop.Count;

            plane.GetTangentBasis(out Vector3 tangent, out Vector3 bitangent);
            float uvScale = options.capUvScale;

            // 가중치는 루프 정점에서 승계하고, UV·노멀·탄젠트만 캡 규칙으로 덮어쓴다.
            Vertex MakeVertex(Vector3 p, BoneWeight weight)
            {
                Vector3 rel = p - plane.PointOnPlane;
                return new Vertex
                {
                    position = p,
                    normal = capNormal,
                    // UV는 평면 접선 기저에 투영해 부여한다(오브젝트 크기에 따라 밀도가 달라지므로 capUvScale로 보정).
                    uv = new Vector2(Vector3.Dot(rel, tangent), Vector3.Dot(rel, bitangent)) * uvScale,
                    tangent = new Vector4(tangent.x, tangent.y, tangent.z, 1f),
                    boneWeight = weight
                };
            }

            // 중심 정점은 원본에 대응하는 정점이 없다 — 림(루프) 가중치의 평균으로 만든다.
            Vertex vc = MakeVertex(center, BoneWeightUtil.Average(loop));

            for (int i = 0; i < loop.Count; i++)
            {
                Vertex l0 = loop[i];
                Vertex l1 = loop[(i + 1) % loop.Count];
                Vector3 p0 = l0.position;
                Vector3 p1 = l1.position;

                Vertex v0 = MakeVertex(p0, l0.boneWeight);
                Vertex v1 = MakeVertex(p1, l1.boneWeight);

                // 감기 방향을 캡 노멀에 맞춘다.
                // 삼각형 (a,b,c)의 앞면 노멀은 Cross(b−a, c−a)다. 여기서 a=중심, b=p0, c=p1이므로
                // 그 값이 capNormal과 같은 쪽을 향할 때 순서를 그대로 두어야 앞면이 캡 바깥을 본다.
                // (뒤집으면 캡이 전부 백페이스 컬링되어 단면이 뚫린 것처럼 보인다.)
                Vector3 faceNormal = Vector3.Cross(p0 - center, p1 - center);
                if (Vector3.Dot(faceNormal, capNormal) >= 0f)
                    target.AppendTriangle(target.CapSubMesh, vc, v0, v1);
                else
                    target.AppendTriangle(target.CapSubMesh, vc, v1, v0);
            }
        }

        // ── 내부 표현 ────────────────────────────────────────────────────────────

        internal struct Vertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector2 uv;
            public Vector4 tangent;

            /// <summary>스키닝 가중치. 정적 메쉬 경로에서는 전부 0으로 남아 아무 영향이 없다.</summary>
            public BoneWeight boneWeight;

            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
            {
                position = Vector3.Lerp(a.position, b.position, t),
                // 선형 보간 후 정규화. Slerp는 두 노멀이 반대에 가까울 때 불안정하고 네이티브 호출이라 순수 로직 원칙에도 어긋난다.
                normal = Vector3.Normalize(Vector3.Lerp(a.normal, b.normal, t)),
                uv = Vector2.Lerp(a.uv, b.uv, t),
                tangent = Vector4.Lerp(a.tangent, b.tangent, t),
                boneWeight = BoneWeightUtil.Lerp(a.boneWeight, b.boneWeight, t)
            };
        }

        /// <summary>
        /// <see cref="BoneWeight"/> 병합 유틸. <b>단순 Lerp가 성립하지 않는 유일한 정점 속성이다</b> —
        /// 두 정점이 최대 8개 본에 영향받는데 결과는 4개만 담을 수 있어, 병합 후 상위 4개를 골라 재정규화해야 한다.
        /// </summary>
        internal static class BoneWeightUtil
        {
            private const int MaxInfluences = 4;

            public static BoneWeight Lerp(BoneWeight a, BoneWeight b, float t)
            {
                // 한쪽만 유효하면 병합 없이 그대로 쓴다 — 정적 메쉬(둘 다 0)도 이 경로로 빠져나간다.
                bool hasA = HasWeight(a);
                bool hasB = HasWeight(b);
                if (!hasA && !hasB) return default;
                if (!hasB) return a;
                if (!hasA) return b;

                Span<int> indices = stackalloc int[MaxInfluences * 2];
                Span<float> weights = stackalloc float[MaxInfluences * 2];
                int count = 0;

                Accumulate(a, 1f - t, ref count, indices, weights);
                Accumulate(b, t, ref count, indices, weights);

                return Finalize(count, indices, weights);
            }

            /// <summary>여러 가중치의 평균. 캡 중심 정점이 루프 정점들에서 가중치를 물려받을 때 쓴다.</summary>
            public static BoneWeight Average(IReadOnlyList<Vertex> loop)
            {
                Span<int> indices = stackalloc int[64];
                Span<float> weights = stackalloc float[64];
                int count = 0;

                float share = loop.Count > 0 ? 1f / loop.Count : 0f;
                foreach (var v in loop)
                {
                    if (!HasWeight(v.boneWeight)) continue;
                    // 버퍼가 차면 더 담지 않는다. 상위 4개만 남길 것이라 통계적으로 손실이 없다.
                    if (count > indices.Length - MaxInfluences) break;
                    Accumulate(v.boneWeight, share, ref count, indices, weights);
                }

                return Finalize(count, indices, weights);
            }

            private static bool HasWeight(BoneWeight w) =>
                w.weight0 > 0f || w.weight1 > 0f || w.weight2 > 0f || w.weight3 > 0f;

            /// <summary>같은 본이 이미 있으면 더하고, 없으면 새 칸에 넣는다.</summary>
            private static void Accumulate(BoneWeight w, float scale, ref int count, Span<int> indices, Span<float> weights)
            {
                Add(w.boneIndex0, w.weight0 * scale, ref count, indices, weights);
                Add(w.boneIndex1, w.weight1 * scale, ref count, indices, weights);
                Add(w.boneIndex2, w.weight2 * scale, ref count, indices, weights);
                Add(w.boneIndex3, w.weight3 * scale, ref count, indices, weights);
            }

            private static void Add(int bone, float weight, ref int count, Span<int> indices, Span<float> weights)
            {
                if (weight <= 0f) return; // 0가중치 항목이 상위 4개 자리를 밀어내면 안 된다

                for (int i = 0; i < count; i++)
                {
                    if (indices[i] != bone) continue;
                    weights[i] += weight;
                    return;
                }

                if (count >= indices.Length) return;
                indices[count] = bone;
                weights[count] = weight;
                count++;
            }

            /// <summary>가중치 상위 4개를 골라 합이 1이 되도록 정규화한다.</summary>
            private static BoneWeight Finalize(int count, Span<int> indices, Span<float> weights)
            {
                if (count == 0) return default;

                // 부분 선택 정렬 — 상위 4개만 필요하므로 전체 정렬은 낭비다.
                int take = Mathf.Min(count, MaxInfluences);
                for (int i = 0; i < take; i++)
                {
                    int best = i;
                    for (int j = i + 1; j < count; j++)
                        if (weights[j] > weights[best]) best = j;

                    if (best == i) continue;
                    (weights[i], weights[best]) = (weights[best], weights[i]);
                    (indices[i], indices[best]) = (indices[best], indices[i]);
                }

                float sum = 0f;
                for (int i = 0; i < take; i++) sum += weights[i];
                if (sum <= 0f) return default;

                float inv = 1f / sum;
                var result = new BoneWeight();
                for (int i = 0; i < MaxInfluences; i++)
                {
                    int bone = i < take ? indices[i] : 0;
                    float weight = i < take ? weights[i] * inv : 0f;

                    switch (i)
                    {
                        case 0: result.boneIndex0 = bone; result.weight0 = weight; break;
                        case 1: result.boneIndex1 = bone; result.weight1 = weight; break;
                        case 2: result.boneIndex2 = bone; result.weight2 = weight; break;
                        case 3: result.boneIndex3 = bone; result.weight3 = weight; break;
                    }
                }

                return result;
            }
        }

        /// <summary>정점을 허용오차로 동일시해 인덱스를 부여하는 해시 격자.</summary>
        internal sealed class VertexWelder
        {
            private readonly float epsilon;
            private readonly Dictionary<(int, int, int), List<int>> buckets = new Dictionary<(int, int, int), List<int>>();

            public readonly List<Vector3> Positions = new List<Vector3>();

            public VertexWelder(float epsilon) => this.epsilon = Mathf.Max(epsilon, 1e-7f);

            public int Add(Vector3 p)
            {
                var cell = Cell(p);
                // 인접 셀까지 훑어야 경계에 걸친 정점이 갈라지지 않는다.
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var key = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);
                    if (!buckets.TryGetValue(key, out var list)) continue;
                    foreach (int i in list)
                        if ((Positions[i] - p).sqrMagnitude <= epsilon * epsilon)
                            return i;
                }

                int index = Positions.Count;
                Positions.Add(p);
                if (!buckets.TryGetValue(cell, out var bucket)) buckets[cell] = bucket = new List<int>();
                bucket.Add(index);
                return index;
            }

            private (int, int, int) Cell(Vector3 p) => (
                Mathf.FloorToInt(p.x / epsilon),
                Mathf.FloorToInt(p.y / epsilon),
                Mathf.FloorToInt(p.z / epsilon));
        }

        /// <summary>절단 중간 표현. 서브메쉬 구조를 보존하며, 마지막 인덱스가 캡 전용이다.</summary>
        internal sealed class WorkMesh
        {
            public List<Vector3> positions = new List<Vector3>();
            public List<Vector3> normals = new List<Vector3>();
            public List<Vector2> uvs = new List<Vector2>();
            public List<Vector4> tangents = new List<Vector4>();

            /// <summary>정적 메쉬면 비어 있다. 비어 있으면 <see cref="ToMesh"/>가 기록하지 않는다.</summary>
            public List<BoneWeight> boneWeights = new List<BoneWeight>();

            /// <summary>본 배열을 조각별로 추리지 않으므로 <b>자르지 않고 통째로 승계</b>한다.</summary>
            public Matrix4x4[] bindposes;

            public List<int>[] subTriangles;
            public int originalSubMeshCount;
            public int capLoopCount;

            /// <summary>캡이 들어가는 서브메쉬 인덱스. 몇 번을 잘라도 캡은 전부 여기 하나에 모인다.</summary>
            public int CapSubMesh => originalSubMeshCount;

            public int SubMeshCount => subTriangles.Length;

            public int TriangleCount
            {
                get
                {
                    int n = 0;
                    foreach (var s in subTriangles) n += s.Count;
                    return n / 3;
                }
            }

            public static WorkMesh FromMesh(Mesh mesh)
            {
                var w = new WorkMesh();
                w.originalSubMeshCount = mesh.subMeshCount;
                w.subTriangles = new List<int>[mesh.subMeshCount + 1]; // +1 = 캡
                for (int i = 0; i < w.subTriangles.Length; i++) w.subTriangles[i] = new List<int>();

                mesh.GetVertices(w.positions);

                var n = new List<Vector3>();
                mesh.GetNormals(n);
                var uv = new List<Vector2>();
                mesh.GetUVs(0, uv);
                var tan = new List<Vector4>();
                mesh.GetTangents(tan);

                // 스키닝은 있을 때만 싣는다 — 없으면 리스트가 비어 있어 출력 단계에서 통째로 건너뛴다.
                var bw = new List<BoneWeight>();
                mesh.GetBoneWeights(bw);
                bool hasSkin = bw.Count == w.positions.Count && bw.Count > 0;
                if (hasSkin) w.bindposes = mesh.bindposes;

                int count = w.positions.Count;
                for (int i = 0; i < count; i++)
                {
                    w.normals.Add(i < n.Count ? n[i] : Vector3.up);
                    w.uvs.Add(i < uv.Count ? uv[i] : Vector2.zero);
                    w.tangents.Add(i < tan.Count ? tan[i] : new Vector4(1f, 0f, 0f, 1f));
                    if (hasSkin) w.boneWeights.Add(bw[i]);
                }

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = new List<int>();
                    mesh.GetTriangles(tris, s);
                    w.subTriangles[s].AddRange(tris);
                }

                return w;
            }

            public WorkMesh CreateEmptyLike()
            {
                var w = new WorkMesh
                {
                    originalSubMeshCount = originalSubMeshCount,
                    subTriangles = new List<int>[subTriangles.Length],
                    capLoopCount = capLoopCount,
                    bindposes = bindposes
                };
                for (int i = 0; i < w.subTriangles.Length; i++) w.subTriangles[i] = new List<int>();
                return w;
            }

            /// <summary>스키닝을 싣고 있는가. 조각으로 갈라져도 이 값은 원본을 따라간다.</summary>
            public bool HasSkin => bindposes != null && bindposes.Length > 0;

            public Vertex GetVertex(int i) => new Vertex
            {
                position = positions[i],
                normal = normals[i],
                uv = uvs[i],
                tangent = tangents[i],
                boneWeight = i < boneWeights.Count ? boneWeights[i] : default
            };

            public int AppendVertex(Vertex v)
            {
                positions.Add(v.position);
                normals.Add(v.normal);
                uvs.Add(v.uv);
                tangents.Add(v.tangent);
                if (HasSkin) boneWeights.Add(v.boneWeight);
                return positions.Count - 1;
            }

            public void AppendTriangle(WorkMesh src, int sub, int i0, int i1, int i2)
            {
                AppendTriangle(sub, src.GetVertex(i0), src.GetVertex(i1), src.GetVertex(i2));
            }

            public void AppendTriangle(int sub, Vertex v0, Vertex v1, Vertex v2)
            {
                var tris = subTriangles[sub];
                tris.Add(AppendVertex(v0));
                tris.Add(AppendVertex(v1));
                tris.Add(AppendVertex(v2));
            }

            /// <summary>
            /// 발산정리 합으로 부피를 잰다. <b>반드시 자기 bounds 중심을 기준으로 잰다.</b>
            ///
            /// <para>이 합은 <b>닫힌 메쉬에서만 평행이동 불변</b>이다. 절단 조각은 캡이 없으면 열린 메쉬이고,
            /// 그때 값은 '원점에서 삼각형까지의 뿔 부피'가 되어 원점이 멀수록 부풀어 오른다.
            /// 원점 기준으로 재면 두께 0인 납작한 조각도 0이 아닌 값을 얻어
            /// <see cref="SliceOptions.minPieceVolumeRatio"/> 퇴화 필터를 그냥 통과해 버린다
            /// (예: y=0.5 평면 위 넓이 1짜리 판 → (1/3)·1·0.5 = 0.167).</para>
            ///
            /// <para>기준점을 자기 bounds 중심으로 옮기면 납작한 조각은 기준점이 그 평면 위에 놓여 정확히 0이 되고,
            /// 닫힌 조각은 평행이동 불변이라 값이 그대로다.</para>
            /// </summary>
            public float ComputeVolume()
            {
                Vector3 pivot = ComputeBoundsCenter();

                double v = 0.0;
                foreach (var tris in subTriangles)
                {
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        Vector3 a = positions[tris[t]] - pivot;
                        Vector3 b = positions[tris[t + 1]] - pivot;
                        Vector3 c = positions[tris[t + 2]] - pivot;
                        v += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                    }
                }
                return (float)v;
            }

            /// <summary>정점 bounds의 중심. 부피 계산의 기준점이라 부피에 의존하지 않는 값이어야 한다.</summary>
            private Vector3 ComputeBoundsCenter()
            {
                if (positions.Count == 0) return Vector3.zero;

                Vector3 min = positions[0];
                Vector3 max = positions[0];

                for (int i = 1; i < positions.Count; i++)
                {
                    min = Vector3.Min(min, positions[i]);
                    max = Vector3.Max(max, positions[i]);
                }

                return (min + max) * 0.5f;
            }

            /// <summary>부피 가중 무게중심. 부피가 0에 가까우면(평면 조각) 정점 평균으로 대체한다.</summary>
            public Vector3 ComputeCentroid()
            {
                double vol = 0.0;
                Vector3 acc = Vector3.zero;

                foreach (var tris in subTriangles)
                {
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        Vector3 a = positions[tris[t]], b = positions[tris[t + 1]], c = positions[tris[t + 2]];
                        float dv = Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
                        vol += dv;
                        acc += (a + b + c) * (dv / 4f);
                    }
                }

                if (Mathf.Abs((float)vol) > 1e-9f)
                    return acc / (float)vol;

                Vector3 sum = Vector3.zero;
                foreach (var p in positions) sum += p;
                return positions.Count > 0 ? sum / positions.Count : Vector3.zero;
            }

            public void Translate(Vector3 delta)
            {
                for (int i = 0; i < positions.Count; i++) positions[i] += delta;
            }

            /// <summary>정점 용접 기준으로 연결 요소를 나눠 <paramref name="output"/>에 담는다.</summary>
            public void SplitConnectedComponents(float weldEpsilon, List<WorkMesh> output)
            {
                int triCount = TriangleCount;
                if (triCount == 0) return;

                // 위치 기준으로 용접해 정점을 통합한 뒤, 삼각형을 union-find로 묶는다.
                var welder = new VertexWelder(weldEpsilon);
                var welded = new int[positions.Count];
                for (int i = 0; i < positions.Count; i++) welded[i] = welder.Add(positions[i]);

                var parent = new int[welder.Positions.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;

                int Find(int x)
                {
                    while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                    return x;
                }
                void Union(int a, int b)
                {
                    int ra = Find(a), rb = Find(b);
                    if (ra != rb) parent[ra] = rb;
                }

                foreach (var tris in subTriangles)
                {
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        int a = welded[tris[t]], b = welded[tris[t + 1]], c = welded[tris[t + 2]];
                        Union(a, b);
                        Union(b, c);
                    }
                }

                var byRoot = new Dictionary<int, WorkMesh>();

                for (int sub = 0; sub < subTriangles.Length; sub++)
                {
                    var tris = subTriangles[sub];
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        int root = Find(welded[tris[t]]);
                        if (!byRoot.TryGetValue(root, out var target))
                            byRoot[root] = target = CreateEmptyLike();

                        target.AppendTriangle(this, sub, tris[t], tris[t + 1], tris[t + 2]);
                    }
                }

                foreach (var comp in byRoot.Values) output.Add(comp);
            }

            /// <summary>
            /// 최종 출력. <b>여기서 정점을 용접한다.</b>
            ///
            /// <para><see cref="AppendTriangle"/>이 삼각형마다 정점 3개를 새로 만들기 때문에
            /// 용접 전 정점 수는 삼각형 수 × 3이다. 정적 프롭에서는 무시할 만했지만
            /// 캐릭터(수천 폴리) + boneWeights(32B/정점)에서는 메모리도, 매 프레임 스키닝 비용도 3배가 된다.</para>
            ///
            /// <para>용접은 <b>출력 단계에서만</b> 한다 — 중간 표현에서 합치면 평면별 캡 병합 로직이 꼬인다.
            /// 병합 조건은 <b>속성 전체 일치</b>다. 위치만 보면 스무딩 경계와 UV 이음매가 뭉개진다.</para>
            /// </summary>
            public Mesh ToMesh()
            {
                Weld();

                var mesh = new Mesh { name = "SlicedPiece" };
                if (positions.Count > ushort.MaxValue)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTangents(tangents);

                mesh.subMeshCount = subTriangles.Length;
                for (int s = 0; s < subTriangles.Length; s++)
                    mesh.SetTriangles(subTriangles[s], s, false);

                if (HasSkin && boneWeights.Count == positions.Count)
                {
                    mesh.boneWeights = boneWeights.ToArray();
                    mesh.bindposes = bindposes;
                }

                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>속성이 완전히 같은 정점을 하나로 합치고 삼각형 인덱스를 재기입한다.</summary>
            private void Weld()
            {
                int original = positions.Count;
                if (original == 0) return;

                var map = new Dictionary<VertexKey, int>(original);
                var remap = new int[original];

                var newPositions = new List<Vector3>(original);
                var newNormals = new List<Vector3>(original);
                var newUvs = new List<Vector2>(original);
                var newTangents = new List<Vector4>(original);
                var newWeights = new List<BoneWeight>(HasSkin ? original : 0);

                for (int i = 0; i < original; i++)
                {
                    var key = new VertexKey(GetVertex(i));
                    if (map.TryGetValue(key, out int existing))
                    {
                        remap[i] = existing;
                        continue;
                    }

                    int index = newPositions.Count;
                    map[key] = index;
                    remap[i] = index;

                    newPositions.Add(positions[i]);
                    newNormals.Add(normals[i]);
                    newUvs.Add(uvs[i]);
                    newTangents.Add(tangents[i]);
                    if (HasSkin) newWeights.Add(boneWeights[i]);
                }

                if (newPositions.Count == original) return; // 합칠 게 없다

                foreach (var tris in subTriangles)
                {
                    for (int t = 0; t < tris.Count; t++) tris[t] = remap[tris[t]];
                }

                positions = newPositions;
                normals = newNormals;
                uvs = newUvs;
                tangents = newTangents;
                boneWeights = newWeights;
            }

            /// <summary>
            /// 용접 판정 키. 부동소수를 그대로 해시하면 절단으로 생긴 동일 정점이 미세 오차로 갈라지므로
            /// 양자화해서 비교한다. 격자는 위치 1e-5, 방향/UV 1e-4 — 절단 오차보다 크고 육안 오차보다 작다.
            /// </summary>
            private readonly struct VertexKey : IEquatable<VertexKey>
            {
                private readonly long px, py, pz;
                private readonly int nx, ny, nz;
                private readonly int u, v;
                private readonly int tx, ty, tz, tw;
                private readonly int b0, b1, b2, b3;
                private readonly int w0, w1, w2, w3;

                public VertexKey(Vertex vertex)
                {
                    px = Quantize(vertex.position.x, 100000.0);
                    py = Quantize(vertex.position.y, 100000.0);
                    pz = Quantize(vertex.position.z, 100000.0);

                    nx = (int)Quantize(vertex.normal.x, 10000.0);
                    ny = (int)Quantize(vertex.normal.y, 10000.0);
                    nz = (int)Quantize(vertex.normal.z, 10000.0);

                    u = (int)Quantize(vertex.uv.x, 10000.0);
                    v = (int)Quantize(vertex.uv.y, 10000.0);

                    tx = (int)Quantize(vertex.tangent.x, 10000.0);
                    ty = (int)Quantize(vertex.tangent.y, 10000.0);
                    tz = (int)Quantize(vertex.tangent.z, 10000.0);
                    tw = (int)Quantize(vertex.tangent.w, 10000.0);

                    var bw = vertex.boneWeight;
                    b0 = bw.boneIndex0; b1 = bw.boneIndex1; b2 = bw.boneIndex2; b3 = bw.boneIndex3;
                    w0 = (int)Quantize(bw.weight0, 10000.0);
                    w1 = (int)Quantize(bw.weight1, 10000.0);
                    w2 = (int)Quantize(bw.weight2, 10000.0);
                    w3 = (int)Quantize(bw.weight3, 10000.0);
                }

                private static long Quantize(float value, double scale) => (long)Math.Round(value * scale);

                public bool Equals(VertexKey o) =>
                    px == o.px && py == o.py && pz == o.pz &&
                    nx == o.nx && ny == o.ny && nz == o.nz &&
                    u == o.u && v == o.v &&
                    tx == o.tx && ty == o.ty && tz == o.tz && tw == o.tw &&
                    b0 == o.b0 && b1 == o.b1 && b2 == o.b2 && b3 == o.b3 &&
                    w0 == o.w0 && w1 == o.w1 && w2 == o.w2 && w3 == o.w3;

                public override bool Equals(object obj) => obj is VertexKey other && Equals(other);

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = 17;
                        hash = hash * 31 + px.GetHashCode();
                        hash = hash * 31 + py.GetHashCode();
                        hash = hash * 31 + pz.GetHashCode();
                        hash = hash * 31 + nx;
                        hash = hash * 31 + ny;
                        hash = hash * 31 + nz;
                        hash = hash * 31 + u;
                        hash = hash * 31 + v;
                        hash = hash * 31 + b0;
                        hash = hash * 31 + w0;
                        return hash;
                    }
                }
            }
        }
    }
}
