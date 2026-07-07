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
        [Min(0)] public float inputTime;
        [Min(0)] public float visibleExposureDuration = 0.5f;
    }

    [CreateAssetMenu(fileName = "Pattern", menuName = "Scriptable Objects/Pattern")]
    public class Pattern : ScriptableObject
    {
        [SerializeField] private PatternData[] patternDatas;

        public IReadOnlyList<PatternData> AllData => patternDatas;

        public PatternData NowPattern => nowPattern;
        private PatternData nowPattern;

        public int ExpectedPointIndex => nowPattern.index;
        public float ExpectedTime => nowPattern.inputTime;

        public int CurrentIndex => index;
        private int index;

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
