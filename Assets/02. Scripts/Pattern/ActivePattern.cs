using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 재생 중인 패턴 하나의 런타임 상태. <see cref="Pattern"/> 에셋은 '모양 원본'일 뿐이며 진행 상태를 갖지 않는다.
    /// 같은 템플릿 에셋을 쓰는 두 패턴이 동시에 살아 있어도 서로의 상태를 덮어쓰지 않도록 분리한 것이다.
    /// </summary>
    public class ActivePattern
    {
        public Pattern Template { get; }

        /// <summary>패턴이 큐에 투입된 시각(= SetPattern 호출 시각). inputTimes의 기준점이며, 판정 대상으로 승계될 때 다시 잡지 않는다.</summary>
        public float StartTime { get; }

        /// <summary>노드별 입력 시각(StartTime 기준 상대시간, 초).</summary>
        private readonly float[] inputTimes;

        /// <summary>전체 정답 여부(보너스 판정용). 오답·Miss가 한 번이라도 나오면 false.</summary>
        public bool AllCorrect { get; private set; } = true;

        /// <summary>현재 입력받을 노드의 위치(0 ~ NodeCount-1). NodeCount에 도달하면 완료.</summary>
        public int CurrentPosition { get; private set; }

        public int NodeCount => Template.AllData.Count;

        /// <summary>
        /// 이 패턴이 끝났는가. <b>연타에서는 뜻이 갈린다</b> — 노드를 다 소비했는가가 아니라
        /// <b>목표 타수를 채웠는가</b>다.
        ///
        /// <para>그 덕에 <c>ExpireOverduePatterns</c>의 <c>if (!IsComplete) MarkIncorrect()</c>가
        /// 그대로 "타수 미달 = 실패"가 된다 — <b>성패 판정을 새로 쓰지 않는다</b>.</para>
        ///
        /// <para>⚠ 반대로 <b>완료 처리의 트리거로는 쓸 수 없다</b>. 연타는 타수를 채워도 창 끝까지
        /// 판정 대상으로 남아야 하며(초과 타격이 다음 패턴으로 새면 그 패턴이 오염된다),
        /// 그 가드는 <c>PatternHandler.AddPattern</c>이 든다.</para>
        /// </summary>
        public bool IsComplete => IsMash ? MashReached : CurrentPosition >= NodeCount;

        // ─────────────────────────── 연타 ───────────────────────────

        /// <summary>연타 패턴인가. 그렇다면 아래 셋이 진행 상태의 전부고 <see cref="CurrentPosition"/>은 쓰이지 않는다.</summary>
        public bool IsMash => Template.IsMash;

        /// <summary>지금까지 들어온 타격 수(초과분 포함).</summary>
        public int MashHits { get; private set; }

        /// <summary>목표 타수를 채웠는가 = 이 연타의 성공 여부.</summary>
        public bool MashReached => MashHits >= Template.MashTargetHits;

        /// <summary>
        /// 연타 입력이 닫히는 절대 시각. <see cref="Deadline"/>보다 <b>마무리 일격의 와인드업만큼 이르다</b>
        /// (<see cref="Pattern.MashInputDeadlineLead"/>).
        ///
        /// <para><b>마감 시각을 아는 곳이 여기 하나여야 한다</b> — 입력 가드와 링 수축 시간이 같은 값을 봐야
        /// "아직 줄고 있는데 입력이 안 먹는" 거짓말이 안 생긴다.</para>
        /// </summary>
        public float MashInputDeadline => Deadline - Template.MashInputDeadlineLead;

        /// <summary>
        /// 타격 하나를 소비한다. <b>목표 이내면 true</b>(= 점수 대상), 초과면 false(= 연출만).
        ///
        /// <para>"그 이상 타격하면 애니메이션만 변경되고 점수는 안 오른다"가 이 반환값 하나로 표현된다 —
        /// 호출부는 true일 때만 <c>OnJudged</c>를 쏜다.</para>
        /// </summary>
        public bool ConsumeMashHit()
        {
            MashHits++;
            return MashHits <= Template.MashTargetHits;
        }

        public int ExpectedPointIndex => Template.AllData[CurrentPosition].index;

        /// <summary>
        /// 현재 노드의 입력 절대시각.
        ///
        /// <para><b>⚠ 연타에서는 '다음 타격이 와야 할 시각'이다.</b> 연타에는 노드 진행이 없어
        /// <see cref="CurrentPosition"/>이 0에 고정되므로, 이 값이 <c>inputTimes[0]</c>에 머무르면
        /// "도달했는가"라는 물음이 창이 열린 뒤 <b>영원히 참</b>이 된다.</para>
        ///
        /// <para>그 상태로 오토플레이(디버그)를 켜면 <b>매 프레임 한 타씩</b> 들어가 초당 60타가 되고,
        /// 두 배우 모두 자기 클립을 매 프레임 처음부터 다시 시작해 <b>첫 프레임에 얼어붙는다</b>
        /// (실제로 "적과 플레이어가 가만히 서 있는" 증상이 이것이었다).</para>
        ///
        /// <para>그래서 연타는 <see cref="MashHits"/>로 이 값을 민다 — 일반 패턴에서 <see cref="Advance"/>가
        /// 하던 일과 <b>정확히 같은 자기 제어</b>이고, 그 결과 오토플레이가 목표 타수를 창에 고르게 펴서
        /// 친다(= 완벽한 플레이의 재현). 새 상태 필드가 없다.</para>
        /// </summary>
        public float ExpectedTime => IsMash
            ? FirstNodeTime + MashHits * MashHitInterval
            : StartTime + inputTimes[CurrentPosition];

        /// <summary>
        /// 연타에서 타격 하나가 차지하는 시간. 창을 목표 타수로 나눈 값이다.
        /// 창이 0이면(잘못 저작된 엔트리) 0을 돌려주지 않고 아주 작은 값으로 막는다 — 0이면 다시 매 프레임이 된다.
        /// </summary>
        public float MashHitInterval =>
            Mathf.Max((LastNodeTime - FirstNodeTime) / Mathf.Max(Template.MashTargetHits, 1), 0.01f);

        /// <summary>마지막 노드의 입력 시한(절대시각). 이 시각을 넘기면 만료 처리한다.</summary>
        public float Deadline { get; }

        /// <summary>마지막 노드의 이상적 도달 시각(절대). ExpectedTime은 CurrentPosition 기준이라 완료 후엔 범위를 벗어나므로 별도로 제공한다.</summary>
        public float LastNodeTime => StartTime + inputTimes[inputTimes.Length - 1];

        /// <summary>첫 노드의 입력 절대시각. 성공 애니 조기 시작 예약 시 시작 하한(첫 노드보다 앞서지 않도록)으로 쓴다.</summary>
        public float FirstNodeTime => StartTime + inputTimes[0];

        public ActivePattern(Pattern template, System.Collections.Generic.IReadOnlyList<float> inputTimes, float startTime, float goodWindow)
        {
            Template = template;
            StartTime = startTime;

            this.inputTimes = new float[inputTimes.Count];
            for (int i = 0; i < inputTimes.Count; i++)
                this.inputTimes[i] = inputTimes[i];

            Deadline = startTime + this.inputTimes[this.inputTimes.Length - 1] + goodWindow;
        }

        public float GetInputTime(int position) => inputTimes[position];

        /// <summary>노드별 입력 절대시각의 사본. 이벤트 페이로드로 나가므로 내부 배열을 그대로 넘기지 않는다.</summary>
        public float[] BuildNodeTimes()
        {
            var times = new float[inputTimes.Length];
            for (int i = 0; i < inputTimes.Length; i++)
                times[i] = StartTime + inputTimes[i];
            return times;
        }

        public NodeType GetNodeType(int position) => Template.GetNodeType(position);

        public int GetPointIndex(int position) => Template.AllData[position].index;

        public void MarkIncorrect() => AllCorrect = false;

        /// <summary>현재 노드를 소비하고 다음 노드로 진행한다.</summary>
        public void Advance() => CurrentPosition++;
    }
}
