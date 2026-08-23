namespace PatternSpace
{
    /// <summary>
    /// 연타 타격 하나의 불변 페이로드. 인자를 나열하지 않고 구조체로 넘기는 것은
    /// <see cref="PatternCompletionInfo"/>·<see cref="PatternQueuedInfo"/>·<see cref="JudgeTargetInfo"/>와
    /// 같은 관례다 — <b>구독자가 다섯이라</b>(캐릭터·히트스톱·게이지·적·이펙트) 시그니처가 흔들리면 전부 깨진다.
    ///
    /// <para><b>⚠ <see cref="Template"/>이 실려 있는 것이 이 구조체의 존재 이유다.</b> 없으면
    /// <c>EnemyDirector</c>(리액션 클립)와 <c>PatternEffectDirector</c>(큐 목록)가 각자
    /// <c>pendingTokens.Peek()</c>으로 "지금 판정 대상"을 <b>다시 유도</b>해야 하고, 그 유도들이 어긋나는
    /// 순간을 디버깅하게 된다(§11-1이 상대 배정에서 이미 겪은 부류다).</para>
    /// </summary>
    public readonly struct MashHitInfo
    {
        /// <summary>이 타격이 속한 패턴(모양 원본). 타격 클립·적 리액션·이펙트 큐를 전부 여기서 꺼낸다.</summary>
        public readonly Pattern Template;

        /// <summary>실제로 눌린 Point 인덱스(0~8). 연타는 어느 인덱스든 유효타라 <b>판정에는 안 쓰인다</b> — 연출용이다.</summary>
        public readonly int PointIndex;

        /// <summary>
        /// 목표 타수 이내인가 = <b>점수가 올랐는가</b>. false면 초과 타격이라 연출만 나온다.
        /// <b>연출 구독자는 이 값을 보지 않는다</b> — 초과 타격에도 모션·이펙트·적 반응이 나와야 한다.
        /// </summary>
        public readonly bool Scored;

        /// <summary>이번 타격까지의 누적 타수(초과분 포함).</summary>
        public readonly int Hits;

        /// <summary>목표 타수. 게이지가 '현재/목표'를 그리는 데 쓴다.</summary>
        public readonly int Target;

        public MashHitInfo(Pattern template, int pointIndex, bool scored, int hits, int target)
        {
            Template = template;
            PointIndex = pointIndex;
            Scored = scored;
            Hits = hits;
            Target = target;
        }
    }
}
