using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 결투 간격(m)의 순수 계산. <b>런타임(<c>PlayerCombatMover</c>)·저작 툴·슬라이서가 전부 이 함수 하나를 부른다</b> —
    /// 식이 두 곳에 적히면 프리뷰와 게임이 조용히 갈라진다(<see cref="DuetTimeline"/>과 같은 규율).
    ///
    /// <para>계산이 <c>Pattern</c> 에셋 밖에 사는 이유는 하나다: 여기가 asmdef 안이라 테스트할 수 있다.</para>
    /// </summary>
    public static class DuelGap
    {
        /// <summary>커브가 없을 때의 하한(m). 상수 경로에서 간격이 0이 되면 둘이 겹쳐 선다.</summary>
        public const float MinConstantGap = 0.1f;

        /// <summary>
        /// <paramref name="relTime"/>(임팩트 기준 상대초, 저작 배속 단위)에서의 간격(m).
        ///
        /// <para>커브가 있으면 <b>그 값이 곧 절대 간격</b>이고 하한을 걸지 않는다 —
        /// <b>음수(플레이어가 적을 지나침)가 커브의 목적</b>이기 때문이다.
        /// 구간 밖은 <see cref="AnimationCurve.Evaluate"/>의 기본 Clamp가 끝 키 값으로 홀드한다.</para>
        ///
        /// <para>커브가 없으면 예전 상수 경로다 — 씬 앵커가 정한 <paramref name="baseDistance"/>에
        /// 패턴의 리치 보정(<paramref name="offset"/>)을 더하고 하한만 건다.</para>
        /// </summary>
        public static float At(AnimationCurve curve, float relTime, float baseDistance, float offset) =>
            Has(curve)
                ? curve.Evaluate(relTime)
                : Mathf.Max(baseDistance + offset, MinConstantGap);

        /// <summary>이 커브가 간격을 시간 함수로 그리는가. 키가 없으면 상수 경로다.</summary>
        public static bool Has(AnimationCurve curve) => curve != null && curve.length > 0;

        /// <summary>
        /// 커브가 그리기를 <b>시작</b>하는 시각(임팩트 기준 상대초). 커브가 없으면 0.
        ///
        /// <para><b>커브는 시작 위치와 시작 시각을 같이 소유한다</b> — 결투 배치는 이 시각의 값으로 자리를 잡고
        /// 이 시각까지 도착한다. 안 그러면 접근은 옛 상수 간격으로 끝나 놓고 커브가 열리는 순간
        /// 첫 키 값으로 <b>순간이동</b>한다.</para>
        /// </summary>
        public static float StartTime(AnimationCurve curve) => Has(curve) ? curve[0].time : 0f;

        /// <summary>
        /// 커브가 그리기를 끝내는 시각(임팩트 기준 상대초). 커브가 없으면 0.
        /// <b>임팩트 이후 구간의 길이를 아는 유일한 출처</b>라 위치 소유권 인수인계가 이 값을 읽는다.
        /// </summary>
        public static float EndTime(AnimationCurve curve) => Has(curve) ? curve[curve.length - 1].time : 0f;

        /// <summary>
        /// 이번 교전의 축(플레이어 → 적, 평면 단위벡터). <b>축은 순간 위치가 아니라 교전의 성질이다.</b>
        ///
        /// <para><b>왜 직전 축을 이기게 두는가</b>: 커브의 목적이 관통(음수 간격)이라
        /// 플레이어는 정상적으로 적 <b>뒤</b>에 서 있을 수 있고, 그 자리에서
        /// <paramref name="toEnemy"/>를 다시 재면 방향이 <b>180° 뒤집힌다</b>. 그대로 쓰면 다음 패턴부터
        /// 커브가 거울로 돌아 — 물러나야 할 때 적을 뚫고 건너가고, 음수인데 관통하지 않는다.
        /// 부호가 뒤집힌 경우에만 직전 축을 유지하므로 <b>상대가 바뀌어 축을 새로 잡는 경로는 안 막는다</b>
        /// (호출자가 상대별로 <paramref name="previousAxis"/>를 비워 준다).</para>
        /// </summary>
        public static Vector3 ResolveAxis(Vector3 toEnemy, Vector3 previousAxis)
        {
            bool hasPrevious = previousAxis.sqrMagnitude > 1e-6f;
            if (toEnemy.sqrMagnitude < 1e-6f) return hasPrevious ? previousAxis.normalized : Vector3.forward;

            Vector3 axis = toEnemy.normalized;

            return hasPrevious && Vector3.Dot(axis, previousAxis) < 0f ? previousAxis.normalized : axis;
        }
    }
}
