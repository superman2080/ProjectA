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

        /// <summary>완성된 패턴 템플릿(모양 원본). 런타임 상태 객체가 아니다.</summary>
        public readonly Pattern Pattern;

        /// <summary>마지막 노드의 도달 시각(절대). 캐릭터 액션의 contact 타이밍 정렬에 쓴다.</summary>
        public readonly float LastNodeTime;

        /// <summary>다음 대기 패턴의 마지막 노드 도달 시각(절대). 액션 겹침 방지용. 다음 패턴이 없으면 음수.</summary>
        public readonly float NextLastNodeTime;

        public PatternCompletionInfo(bool allCorrect, Pattern pattern, float lastNodeTime, float nextLastNodeTime)
        {
            AllCorrect = allCorrect;
            Pattern = pattern;
            LastNodeTime = lastNodeTime;
            NextLastNodeTime = nextLastNodeTime;
        }
    }
}
