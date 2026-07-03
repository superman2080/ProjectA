using System;
using UnityEngine;

namespace PatternSpace
{
    [Serializable]
    public class PatternData
    {
        [Range(0, 8)] public int index;
        [Min(0)] public float inputTime;
    }

    [CreateAssetMenu(fileName = "Pattern", menuName = "Scriptable Objects/Pattern")]
    public class Pattern : ScriptableObject
    {
        [SerializeField] private PatternData[] patternDatas;

        public PatternData NowPattern => nowPattern;
        private PatternData nowPattern;

        public int ExpectedPointIndex => nowPattern.index;
        public float ExpectedTime => nowPattern.inputTime;

        private int index;

        public event Action OnInput;
        public event Action OnExit;

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
