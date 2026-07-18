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

        /// <summary>마지막 노드 입력 절대시각(성공 애니가 여기서 끝나도록 정렬).</summary>
        public readonly float LastNodeTime;

        public JudgeTargetInfo(Pattern template, float firstNodeTime, float lastNodeTime)
        {
            Template = template;
            FirstNodeTime = firstNodeTime;
            LastNodeTime = lastNodeTime;
        }
    }
}
