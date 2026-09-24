namespace ChartGen
{
    /// <summary>
    /// 채보를 저작한 방식. <b>굽기 툴이 어느 화면으로 열지를 정하는 값이며 런타임은 읽지 않는다</b>
    /// (<c>SongChart.patternPool</c>과 같은 성격).
    ///
    /// <para><b>⚠ <see cref="StageMode"/>와 혼동하지 않는다</b> — 저쪽은 재생 방식(종료 조건·재시도·루프)이고
    /// 이쪽은 저작 방식이다. 둘은 직교한다: 격자로 저작한 채보를 <c>Linear</c>로 재생할 수도 있다.</para>
    /// </summary>
    public enum ChartAuthoring
    {
        /// <summary>온셋 분석으로 굽는다(현행). 온셋 시각이 음악의 트랜지언트에서 나온다.</summary>
        Linear = 0,

        /// <summary>
        /// 마디 격자 위에 손으로 놓는다. 온셋이 <b>스텝 인덱스에서 파생</b>되므로 BPM을 고쳐도
        /// 배치가 격자를 유지한다 — 재시도 드리프트가 쌓이는 <see cref="StageMode.Loop"/>의 전제다.
        /// </summary>
        Loop = 1,
    }
}
