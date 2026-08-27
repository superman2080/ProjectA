using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>정해진 시간만큼 기다린다. 경과 시간은 러너가 재므로 자기 타이머를 들지 않는다.</summary>
    [Serializable]
    public class WaitStep : SequenceStep
    {
        [Min(0f)]
        [SerializeField] private float duration = 1f;

        public override bool IsFinished(SequenceContext context) => context.ElapsedInStep >= duration;

        public override string Label => $"Wait {duration:0.##}s";
    }
}
