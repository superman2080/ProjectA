namespace PatternSpace
{
    /// <summary>
    /// 패턴이 <b>큐에 투입되는 순간</b>의 불변 페이로드. 판정 대상이 되는 시점(<see cref="JudgeTargetInfo"/>)보다
    /// 훨씬 이르기 때문에, 등장에 시간이 걸리는 연출(베이는 표적 등)이 준비할 여유를 얻는다.
    /// </summary>
    public readonly struct PatternQueuedInfo
    {
        /// <summary>투입된 패턴 템플릿(모양 원본).</summary>
        public readonly Pattern Template;

        /// <summary>큐 투입 시각(절대) = <b>첫 노드가 낙하를 시작하는 시각</b>. 연출이 쓸 수 있는 시간의 하한선이다.</summary>
        public readonly float StartTime;

        /// <summary>첫 노드의 도달 시각(절대).</summary>
        public readonly float FirstNodeTime;

        /// <summary>마지막 노드의 도달 시각(절대).</summary>
        public readonly float LastNodeTime;

        /// <summary>
        /// 입력 시한(절대) = <c>LastNodeTime + goodWindow</c>. <b>이 시점 이전에는 성패가 아직 열려 있다</b> —
        /// 마지막 노드를 goodWindow 안에 늦게 눌러도 Good 성공이기 때문이다.
        /// 성패 확정을 전제로 하는 연출은 이 시각을 기준으로 잡아야 한다.
        /// </summary>
        public readonly float Deadline;

        /// <summary>
        /// 노드별 입력 예정 시각(절대), 패턴 순서 그대로. <b>이 한 필드 덕분에 패턴 진행 중 임의의 노드에
        /// 연출을 걸 수 있고</b>, 그 대가로 소비자가 이벤트를 더 구독하지 않아도 된다 —
        /// 모든 기준 시각이 큐 투입 순간에 확정된다.
        ///
        /// <para>⚠ <b>'예정'이지 '실제'가 아니다.</b> 플레이어가 늦게 눌러도 이 값은 밀리지 않는다.
        /// 입력 순간에 정확히 붙어야 하는 연출은 판정 이벤트(<c>OnFocusRingResolved</c>)를 쓴다.</para>
        /// </summary>
        public readonly float[] NodeTimes;

        public PatternQueuedInfo(Pattern template, float startTime, float firstNodeTime, float lastNodeTime, float deadline, float[] nodeTimes = null)
        {
            Template = template;
            StartTime = startTime;
            FirstNodeTime = firstNodeTime;
            LastNodeTime = lastNodeTime;
            Deadline = deadline;
            NodeTimes = nodeTimes;
        }
    }
}
