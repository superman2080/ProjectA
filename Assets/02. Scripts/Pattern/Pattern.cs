using System;
using System.Collections.Generic;
using UnityEngine;

namespace PatternSpace
{
    public enum NodeType
    {
        Start,
        Progress,
        End
    }

    [Serializable]
    public class PatternData
    {
        [Range(0, 8)] public int index;
    }

    [CreateAssetMenu(fileName = "Pattern", menuName = "Scriptable Objects/Pattern")]
    public class Pattern : ScriptableObject
    {
        [SerializeField] private PatternData[] patternDatas;

        public IReadOnlyList<PatternData> AllData => patternDatas;

        public PatternData NowPattern => nowPattern;
        private PatternData nowPattern;

        public int ExpectedPointIndex => nowPattern.index;
        public float ExpectedTime => inputTimes[index];

        public int CurrentIndex => index;
        private int index;

        /// <summary>노드별 실제 입력 시각(초, 패턴 시작 기준 상대시간). 곡 재생 시각에 맞춰 <see cref="SetInputTimes"/>로 주입된다.</summary>
        private float[] inputTimes;

        public event Action OnInput;
        public event Action OnExit;

        public NodeType GetNodeType(int position)
        {
            if (position == 0) return NodeType.Start;
            if (position == patternDatas.Length - 1) return NodeType.End;
            return NodeType.Progress;
        }

        private void OnValidate()
        {
            if (patternDatas == null) return;

            var seen = new HashSet<int>();
            foreach (var data in patternDatas)
            {
                if (!seen.Add(data.index))
                {
                    Debug.LogError($"[Pattern] '{name}'에 중복된 인덱스 {data.index}가 있습니다.", this);
                }
            }
        }

        /// <summary>노드별 실제 입력 시각(패턴 시작 기준 상대시간, 초)을 주입한다. <see cref="Initialize"/> 전에 호출해야 한다.</summary>
        public void SetInputTimes(IReadOnlyList<float> times)
        {
            if (times.Count != patternDatas.Length)
            {
                Debug.LogError($"[Pattern] '{name}' 입력 시각 개수({times.Count})가 노드 개수({patternDatas.Length})와 다릅니다.", this);
                return;
            }

            inputTimes = new float[times.Count];
            for (int i = 0; i < times.Count; i++)
                inputTimes[i] = times[i];
        }

        public float GetInputTime(int position) => inputTimes[position];

        public void Initialize()
        {
            index = 0;
            nowPattern = patternDatas[0];
        }

        public void Input()
        {
            OnInput?.Invoke();
            Next();
        }

        private void Next()
        {
            if (index < patternDatas.Length - 1)
                nowPattern = patternDatas[++index];
            else
                OnExit?.Invoke();
        }
    }
}
