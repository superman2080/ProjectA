using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 패턴 완료(완주/만료) 순간의 불변 페이로드. 완료 이벤트가 인자를 나열하는 대신 이 구조체 하나를 넘긴다.
    /// 향후 소비자(카메라 연출 등)가 늘거나 필요한 데이터(예: 마지막 노드 월드 좌표, 콤보 수)가 추가돼도
    /// 여기 필드만 더하면 되고 기존 구독자 시그니처는 깨지지 않는다.
    /// </summary>
    public readonly struct PatternCompletionInfo
    {
        /// <summary>전 노드를 Good/Perfect로 완주하고 오답 Point를 건드리지 않았는지.</summary>
        public readonly bool AllCorrect;

        /// <summary>완성된 패턴 템플릿(모양 원본). 런타임 상태 객체가 아니다.
        /// <b>이름이 <c>Template</c>인 이유</b>는 <see cref="JudgeTargetInfo"/>·<see cref="PatternQueuedInfo"/>와
        /// 같은 것을 같은 이름으로 부르기 위해서다.</summary>
        public readonly Pattern Template;

        /// <summary>마지막 노드의 도달 시각(절대). 캐릭터 액션의 contact 타이밍 정렬에 쓴다.</summary>
        public readonly float LastNodeTime;

        /// <summary>다음 대기 패턴의 마지막 노드 도달 시각(절대). 액션 겹침 방지용. 다음 패턴이 없으면 음수.</summary>
        public readonly float NextLastNodeTime;

        /// <summary>
        /// 입력 시한(절대) = <c>LastNodeTime + goodWindow</c>. <b>성패가 확정되는 시각</b>이며
        /// <see cref="JudgeTargetInfo.Deadline"/>·<see cref="PatternQueuedInfo.Deadline"/>과 같은 값이다.
        ///
        /// <para><b>페이로드가 직접 든다.</b> 예전에는 이 필드가 없어서 완료 이벤트 구독자만
        /// <c>handler.GoodWindow</c>를 따로 참조해 식을 손으로 다시 조립했고, 그래서 같은 식이 여러 곳에 복제됐다.</para>
        /// </summary>
        public readonly float Deadline;

        /// <summary>
        /// 이 패턴이 <b>재시도본</b>인가(같은 엔트리를 이미 한 번 겪었다). 채점과 마무리 실루엣이
        /// 이 표식 하나만 보고 물러난다 — "다시 해도 점수는 안 오른다"의 구현 전부다.
        ///
        /// <para><b>⚠ 표시 콤보는 다시 쌓인다.</b> 안 오르는 것은 채점의 누적(<c>comboSum</c>·판정 개수·
        /// <c>MaxCombo</c>)뿐이다 — 재시도 구간 내내 화면 콤보가 0에 박혀 있으면 안 된다.</para>
        /// </summary>
        public readonly bool IsRetry;

        /// <summary>
        /// 이 패턴이 <b>취소</b>됐는가 — 플레이어가 입력할 기회 없이 큐에서 회수됐다.
        ///
        /// <para>재시도로 채보 시계를 되감을 때 이미 큐에 올라간 패턴(언제나 최대 1개)을 걷어내는 경로다.
        /// <b>새 이벤트를 만들지 않고 완료 이벤트에 얹는 이유</b>는 큐 시점에 상태를 만든 구독자
        /// (<c>EnemyDirector</c>·<c>PatternEffectDirector</c>)가 전부 이미 이 이벤트를 구독하고 있어
        /// <b>새 배선이 0개</b>라서다.</para>
        ///
        /// <para><b>⚠ 구독자는 연출을 건너뛰어야 한다.</b> 그냥 실패로 흘리면 <b>오지도 않은 칼에
        /// 적이 패링 모션을 한다.</b> 그래서 발행 쪽도 <c>AllCorrect = false</c>로 못박아 보낸다 —
        /// 손 안 댄 패턴은 <c>AllCorrect</c>가 <b>true</b>라 그대로 두면 취소가 처치로 읽힌다.</para>
        /// </summary>
        public readonly bool Cancelled;

        public PatternCompletionInfo(bool allCorrect, Pattern template, float lastNodeTime, float nextLastNodeTime, float deadline,
                                     bool isRetry = false, bool cancelled = false)
        {
            AllCorrect = allCorrect;
            Template = template;
            LastNodeTime = lastNodeTime;
            NextLastNodeTime = nextLastNodeTime;
            Deadline = deadline;
            IsRetry = isRetry;
            Cancelled = cancelled;
        }
    }
}
