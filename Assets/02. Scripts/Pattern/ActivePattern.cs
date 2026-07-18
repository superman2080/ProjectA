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
        public bool IsComplete => CurrentPosition >= NodeCount;

        public int ExpectedPointIndex => Template.AllData[CurrentPosition].index;

        /// <summary>현재 노드의 입력 절대시각.</summary>
        public float ExpectedTime => StartTime + inputTimes[CurrentPosition];

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

        public NodeType GetNodeType(int position) => Template.GetNodeType(position);

        public int GetPointIndex(int position) => Template.AllData[position].index;

        public void MarkIncorrect() => AllCorrect = false;

        /// <summary>현재 노드를 소비하고 다음 노드로 진행한다.</summary>
        public void Advance() => CurrentPosition++;
    }
}
