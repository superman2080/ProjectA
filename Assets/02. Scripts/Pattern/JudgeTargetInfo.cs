namespace PatternSpace
{
    /// <summary>
    /// 판정 대상이 선두가 되는 순간의 불변 페이로드. 캐릭터 액션이 성공 애니 시작을 예약하는 데 쓴다.
    /// 배속은 소비자가 <see cref="Template"/>.AnimationSpeed로 읽는다.
    /// </summary>
    public readonly struct JudgeTargetInfo
    {
        /// <summary>판정 대상 패턴의 모양 원본(클립·트림·배속 조회용).</summary>
        public readonly Pattern Template;

        /// <summary>첫 노드 입력 절대시각(성공 애니 시작 하한).</summary>
        public readonly float FirstNodeTime;

        /// <summary>마지막 노드 입력 절대시각.</summary>
        public readonly float LastNodeTime;

        /// <summary>
        /// 입력 시한(절대) = <c>LastNodeTime + goodWindow</c>. <b>성패가 확정되는 시각이자 임팩트 정렬 기준</b>이다 —
        /// 표적이 갈라지는 시각(<c>Deadline + Pattern.SliceTargetImpactOffset</c>)과 같은 식을 쓰므로,
        /// 베기 클립의 임팩트 프레임을 여기에 맞추면 칼날이 지나가는 순간과 절단 순간이 구조적으로 일치한다.
        /// </summary>
        public readonly float Deadline;

        public JudgeTargetInfo(Pattern template, float firstNodeTime, float lastNodeTime, float deadline)
        {
            Template = template;
            FirstNodeTime = firstNodeTime;
            LastNodeTime = lastNodeTime;
            Deadline = deadline;
        }
    }
}
