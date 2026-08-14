namespace PatternSpace
{
    /// <summary>
    /// 세 페이로드가 공유하는 <b>임팩트 시각</b> 파생값. 새 데이터가 아니라 <c>Deadline + Pattern.ImpactOffset</c>
    /// 한 식을 한 곳에 모은 것이다.
    ///
    /// <para><b>왜 한 곳이어야 하나</b> — 이 시각에 다섯이 동시에 맞춰진다(플레이어 칼 임팩트 프레임 · 적 칼 ·
    /// 시체 교체/절단 · 투사체 도착 · 카메라 큐). 식이 복제되면 그중 하나만 어긋나는 순간이 조용히 생기는데,
    /// 화면에서는 "칼은 지나갔는데 뒤늦게 갈라진다"로만 보여 원인을 짚기 어렵다.</para>
    ///
    /// <para>템플릿이 없으면 보정 0으로 <c>Deadline</c>을 그대로 돌려준다(무연출 패턴도 시각은 정의된다).</para>
    /// </summary>
    public static class PatternInfoExtensions
    {
        public static float ImpactTime(this in PatternCompletionInfo info) =>
            info.Deadline + (info.Template != null ? info.Template.ImpactOffset : 0f);

        public static float ImpactTime(this in JudgeTargetInfo info) =>
            info.Deadline + (info.Template != null ? info.Template.ImpactOffset : 0f);

        public static float ImpactTime(this in PatternQueuedInfo info) =>
            info.Deadline + (info.Template != null ? info.Template.ImpactOffset : 0f);
    }
}
