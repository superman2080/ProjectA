using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PatternData
{
    [Range(0, 8)] public int index;
    [Min(0)] public float nextPatternTime;
}

[CreateAssetMenu(fileName = "Pattern", menuName = "Scriptable Objects/Pattern")]
public class Pattern : ScriptableObject
{
    [SerializeField] private PatternData[] patternDatas;
    public PatternData NowPattern => nowPattern;
    private PatternData nowPattern;

    public int Index => index;
    private int index = 0;

    public event Action OnExit;

    public void Next()
    {
        if (index < patternDatas.Length - 1)
        {
            nowPattern = patternDatas[++index];
        }
        else
            OnExit?.Invoke();
    }
}
