using System.Collections.Generic;
using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 절단 평면끼리의 근접도. <b>"이 스윙이 만드는 절단면과 가장 비슷하게 구워진 세트는 어느 것인가"</b>
    /// 하나만 답한다.
    ///
    /// <para><b>⚠ 절단 평면에는 부호가 없다.</b> <c>n</c>과 <c>−n</c>은 같은 절단면이다 —
    /// 유도 방향은 칼의 진행 방향(<c>Cross(장축, travel)</c>)이 정하므로 같은 각도의 베기라도
    /// 좌우 방향이 반대면 법선이 뒤집혀 나온다. 부호를 보면 그 둘이 180° 차이로 읽혀
    /// <b>재사용이 통째로 실패한다</b>(같은 각도인데 매번 새로 구워야 하는 상태).</para>
    ///
    /// <para>비교는 <b>같은 좌표계에서만</b> 성립한다. 런타임이 쓰는 값은 전부
    /// <b>적 루트 로컬</b>(표준 결투 배치에서 뽑은 canonical 평면)이며, 굽기에 쓰는
    /// 메쉬 로컬 평면(<see cref="SliceSet.BakedPlanes"/>)과 섞으면 안 된다.</para>
    /// </summary>
    public static class SliceMatch
    {
        /// <summary>후보가 하나도 없을 때 <see cref="Pick"/>이 돌려주는 값.</summary>
        public const int None = -1;

        /// <summary>
        /// 두 평면의 거리. <b>작을수록 비슷하다</b>(0 = 같은 절단면).
        ///
        /// <para>점수 = 각도(도) + <paramref name="positionWeight"/> × 거리차(m).</para>
        ///
        /// <para><c>ponytail:</c> 각도와 높이를 한 스칼라로 더한다. 두 축에 서로 다른 문턱이
        /// 필요할 만큼 갈리면 그때 <c>(angle, offset)</c> 쌍을 돌려주게 나눈다.</para>
        /// </summary>
        public static float Score(SlicePlane a, SlicePlane b, float positionWeight)
        {
            Compare(a, b, out float angle, out float offset);
            return angle + Mathf.Max(0f, positionWeight) * offset;
        }

        /// <summary>
        /// 두 평면의 차이를 <b>두 축으로 갈라서</b> 돌려준다 — <paramref name="angle"/>은 기울기 차(도),
        /// <paramref name="offset"/>은 같은 방향으로 맞춘 뒤의 거리 차(m, 절단 높이).
        /// 저작 툴이 "몇 도 · 몇 cm 차이인가"를 사람에게 보여 주려면 합산 점수로는 부족하다.
        /// </summary>
        public static void Compare(SlicePlane a, SlicePlane b, out float angle, out float offset)
        {
            Vector3 na = a.normal.normalized;
            Vector3 nb = b.normal.normalized;

            // 부호 없는 비교 — 뒤집힌 쪽이 가까우면 그쪽으로 맞춰 거리도 함께 뒤집는다.
            float dot = Vector3.Dot(na, nb);
            bool flipped = dot < 0f;

            angle = Mathf.Acos(Mathf.Clamp(flipped ? -dot : dot, -1f, 1f)) * Mathf.Rad2Deg;
            offset = Mathf.Abs(a.distance - (flipped ? -b.distance : b.distance));
        }

        /// <summary>
        /// <paramref name="want"/>에 가장 가까운 후보의 인덱스. 후보가 없으면 <see cref="None"/>.
        /// <b>동점이면 앞선 인덱스</b>가 이긴다 — 배열 순서가 곧 저작 의도다.
        /// </summary>
        public static int Pick(SlicePlane want, IReadOnlyList<SlicePlane> candidates, float positionWeight)
        {
            if (candidates == null || candidates.Count == 0) return None;

            int best = None;
            float bestScore = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                float score = Score(want, candidates[i], positionWeight);
                if (score >= bestScore) continue;

                bestScore = score;
                best = i;
            }

            return best;
        }
    }
}
