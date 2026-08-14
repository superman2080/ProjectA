using System;
using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 굽기 툴의 평면 프리셋 이름이자 결과물의 라벨. <b>런타임 조회에는 쓰이지 않는다</b> —
    /// 패턴은 <see cref="SliceSet"/>을 직접 참조하므로 이 값으로 무언가를 찾는 코드는 없다.
    /// 따라서 같은 모양을 몇 벌이든 구울 수 있고, <see cref="Custom"/>도 개수 제한이 없다.
    /// 실제 절단 정보는 <see cref="SliceSet.BakedPlanes"/>에 평면으로 들어 있다.
    /// </summary>
    public enum SliceShape
    {
        Horizontal,
        Vertical,
        Diagonal,
        Cross,
        Dice,
        Custom
    }

    /// <summary>
    /// 절단 평면 하나(메쉬 로컬 좌표계). <c>dot(normal, p) + distance</c>가 점 p의 부호 거리다
    /// (UnityEngine.Plane과 같은 규약).
    /// </summary>
    [Serializable]
    public struct SlicePlane
    {
        public Vector3 normal;
        public float distance;

        public SlicePlane(Vector3 normal, float distance)
        {
            this.normal = normal.normalized;
            this.distance = distance;
        }

        /// <summary>법선과 평면이 지나는 점으로 생성한다.</summary>
        public static SlicePlane FromPointNormal(Vector3 point, Vector3 normal)
        {
            Vector3 n = normal.normalized;
            return new SlicePlane { normal = n, distance = -Vector3.Dot(n, point) };
        }

        public readonly float SignedDistance(Vector3 point) => Vector3.Dot(normal, point) + distance;

        /// <summary>평면 위의 임의의 한 점(원점에서 가장 가까운 점).</summary>
        public readonly Vector3 PointOnPlane => normal * -distance;

        /// <summary>캡 UV 투영에 쓸 접선 기저. normal과 직교하는 정규직교 두 축을 만든다.</summary>
        public readonly void GetTangentBasis(out Vector3 tangent, out Vector3 bitangent)
        {
            // normal과 가장 덜 나란한 축을 골라 외적해야 수치적으로 안정적이다.
            Vector3 helper = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
            tangent = Vector3.Normalize(Vector3.Cross(helper, normal));
            bitangent = Vector3.Cross(normal, tangent);
        }
    }

    public static class SliceShapeExtensions
    {
        /// <summary>
        /// Shape의 기본 평면 프리셋. <b>시작점일 뿐</b>이며, 굽기 툴에서 획을 긋거나 지워
        /// 평면을 자유롭게 추가·삭제·조정할 수 있다(평면 장수는 Shape에 묶이지 않는다).
        /// </summary>
        public static SlicePlane[] ToPlanes(this SliceShape shape, Bounds bounds)
        {
            Vector3 c = bounds.center;
            switch (shape)
            {
                case SliceShape.Horizontal:
                    return new[] { SlicePlane.FromPointNormal(c, Vector3.up) };
                case SliceShape.Vertical:
                    return new[] { SlicePlane.FromPointNormal(c, Vector3.right) };
                case SliceShape.Diagonal:
                    return new[] { SlicePlane.FromPointNormal(c, new Vector3(1f, 1f, 0f)) };
                case SliceShape.Cross:
                    return new[]
                    {
                        SlicePlane.FromPointNormal(c, Vector3.right),
                        SlicePlane.FromPointNormal(c, Vector3.up)
                    };
                case SliceShape.Dice:
                    return new[]
                    {
                        SlicePlane.FromPointNormal(c, Vector3.right),
                        SlicePlane.FromPointNormal(c, Vector3.up),
                        SlicePlane.FromPointNormal(c, Vector3.forward)
                    };
                default:
                    return Array.Empty<SlicePlane>(); // Custom — 프리셋 없음
            }
        }
    }
}
