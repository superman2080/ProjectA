using System.Collections.Generic;
using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 한 패턴이 상대하는 적들이 <b>어디에 서는가</b>의 순수 계산.
    /// <b>런타임(<c>EnemyDirector</c>)과 저작 툴(짝 에디터)이 전부 이 함수 하나를 부른다</b> —
    /// 식이 두 곳에 적히면 프리뷰와 게임이 조용히 갈라진다(<see cref="DuelGap"/>과 같은 규율).
    ///
    /// <para><b>좌표계</b>: 오프셋은 <b>임팩트 시점의 플레이어 위치</b>를 원점으로 한
    /// <b>결투 축 로컬 평면</b> 좌표(m)다 — <c>y</c>가 축 방향(앞), <c>x</c>가 그 오른쪽.
    /// 적은 고정이고 플레이어만 움직이므로(CLAUDE.md 11-9) 임팩트 순간이 둘의 유일한 공통 기준이다.</para>
    ///
    /// <para><b>비율이 아니라 절대 미터다</b> — 리치는 모션의 성질이라 씬 값(<c>DuelBaseDistance</c>)에
    /// 따라 흔들리면 안 된다.</para>
    ///
    /// <para>계산이 <c>Pattern</c> 에셋 밖에 사는 이유는 하나다: 여기가 asmdef 안이라 테스트할 수 있다.</para>
    /// </summary>
    public static class EngageSlots
    {
        /// <summary>
        /// 적 <paramref name="index"/>가 설 월드 자리.
        ///
        /// <para><b>오프셋이 비면 예전 경로다</b> — 결투 축 위 <paramref name="fallbackGap"/>만큼 앞,
        /// 즉 <b>마주보고 한 명</b>이다. 그래서 기존 템플릿의 마이그레이션이 0이다.</para>
        /// </summary>
        /// <param name="offsets">패턴이 든 배치(<c>Pattern.EngageOffsets</c>). null·빈 리스트면 폴백.</param>
        /// <param name="index">몇 번째 적인가. 범위를 벗어나면 폴백 자리를 돌려준다.</param>
        /// <param name="playerPosition">임팩트 시점의 플레이어 위치(원점).</param>
        /// <param name="axis">결투 축(<see cref="DuelGap.ResolveAxis"/>가 정한 단위벡터). 평면으로 정규화해 쓴다.</param>
        /// <param name="fallbackGap">오프셋이 없을 때 쓸 간격(m) — 씬 앵커 + 패턴 보정.</param>
        public static Vector3 Resolve(
            IReadOnlyList<Vector2> offsets, int index,
            Vector3 playerPosition, Vector3 axis, float fallbackGap)
        {
            Vector3 forward = Flatten(axis);
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            if (offsets == null || offsets.Count == 0 || index < 0 || index >= offsets.Count)
                return playerPosition + forward * fallbackGap;

            Vector2 offset = offsets[index];
            return playerPosition + right * offset.x + forward * offset.y;
        }

        /// <summary>
        /// 이 패턴이 상대하는 적 수. <b>배치 리스트의 길이가 곧 인원이다</b> —
        /// 인원을 따로 들면 "인원 3인데 오프셋 2개"가 표현 가능해진다.
        /// </summary>
        public static int CountOf(IReadOnlyList<Vector2> offsets) =>
            offsets == null || offsets.Count == 0 ? 1 : offsets.Count;

        /// <summary>
        /// 오프셋이 비었을 때 저작 화면이 보여 줄 <b>기본 자리</b>(마주보기).
        /// 저작을 시작하는 순간 이 값이 첫 원소로 들어간다 — 그래서 "추가"가 자리를 바꾸지 않는다.
        /// </summary>
        public static Vector2 DefaultOffset(float fallbackGap) => new Vector2(0f, fallbackGap);

        /// <summary>
        /// 이 자리가 칼날 선분(<paramref name="bladeFrom"/> ~ <paramref name="bladeTo"/>)에서
        /// 얼마나 떨어져 있는가(m, 평면). <b>"칼 한 번이 셋에 다 닿는가"를 툴이 묻는 식이다.</b>
        ///
        /// <para>선분까지의 거리라 손잡이·칼끝 어느 쪽에 가까운지와 무관하게 "스치는가"만 본다.
        /// 판단(닿았다/아니다)은 부르는 쪽이 자기 임계값으로 한다 — 스치는 연출이 의도일 수 있다.</para>
        /// </summary>
        public static float BladeDistance(Vector3 slot, Vector3 bladeFrom, Vector3 bladeTo)
        {
            // ⚠ Flatten을 쓰면 안 된다 — 그쪽은 <b>정규화</b>까지 해서 길이를 버린다(축 전용이다).
            //   거리를 재는 벡터는 높이만 떨어뜨리고 길이를 지켜야 한다.
            Vector3 a = Planar(bladeFrom - slot);
            Vector3 segment = Planar(bladeTo - bladeFrom);

            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr < 1e-8f) return a.magnitude;

            // 선분 위 최근접점의 매개변수. 구간 밖이면 끝점으로 잘린다.
            float t = Mathf.Clamp01(Vector3.Dot(-a, segment) / lengthSqr);
            return (a + segment * t).magnitude;
        }

        /// <summary>높이만 떨어뜨린다(길이 보존). 거리 계산용 — <see cref="Flatten"/>과 혼동하면 안 된다.</summary>
        private static Vector3 Planar(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>평면 <b>단위</b>벡터로 만든다(축 전용). 축이 0이면 +Z로 폴백한다 — 방향이 없으면 자리도 정의되지 않는다.</summary>
        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-8f ? Vector3.forward : v.normalized;
        }
    }
}
