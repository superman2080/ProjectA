namespace ChartGen
{
    /// <summary>
    /// 스테이지가 어떻게 끝나는가. <b>곡의 구조를 가리키는 이름이고, 나머지가 거기서 따라온다</b> —
    /// 종료 조건 · 재시도 활성 · <c>audioSource.loop</c> 셋이 이 값 하나에 묶여 있다.
    ///
    /// <para><b>⚠ 셋을 따로 두면 안 된다.</b> 그중 하나를 이름에 쓰면 나머지 둘이 숨고,
    /// "<see cref="Linear"/>인데 곡이 루프해서 영영 안 끝나는" 조합이 표현 가능해진다.</para>
    ///
    /// <para><b>⚠ <c>SongChart</c>에 두지 않는다.</b> 같은 곡을 두 모드로 플레이하는 것이 요구라
    /// 채보에 박으면 커스텀 모드가 스토리 채보를 못 쓴다. 진실의 원천은 씬의 <c>ChartPlayer</c>이고
    /// <c>GameSession.StageModeOverride</c>가 그것을 덮는다.</para>
    ///
    /// <para><b>⚠ §9의 <c>GameMode</c>(Story/FreePlay)와 합치지 않는다.</b> 합치면
    /// "자유 연주는 반드시 <see cref="Linear"/>"가 코드에 박혀 나중에 못 뗀다 — 둘은 직교한다.</para>
    /// </summary>
    public enum StageMode
    {
        /// <summary>
        /// 곡이 무한 루프하고, 공격 패턴 실패는 <b>재시도</b>하며, <b>모든 엔트리가 소비되면</b> 끝난다.
        /// 스토리 모드의 기본값이다.
        /// </summary>
        Loop = 0,

        /// <summary>곡이 1회 재생되고, 재시도가 없으며, <b>곡이 끝나면</b> 끝난다. 커스텀 모드용.</summary>
        Linear = 1,
    }
}
