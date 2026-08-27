using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 씬 쪽이 <see cref="SequenceRunner.SetFlag"/>를 부를 때까지 기다린다.
    /// 트리거 볼륨에 들어가기, 버튼 누르기처럼 <b>스텝이 직접 콜백을 받을 수 없는 조건</b>의 답이다.
    ///
    /// <para>폴링이라 새 개념이 아니다 — 구 <c>SequenceStateBase.FinishSequenceCondition</c>도 폴링이었다.</para>
    /// </summary>
    [Serializable]
    public class WaitFlagStep : SequenceStep
    {
        [Tooltip("씬 쪽에서 SequenceRunner.SetFlag(이 이름)을 부르면 통과한다.")]
        [SerializeField] private string flagName;

        public override void Enter(SequenceContext context)
        {
            if (string.IsNullOrEmpty(flagName))
            {
                Debug.LogError("[WaitFlagStep] flagName이 비어 있습니다 - 아무도 이 스텝을 통과시킬 수 없습니다.",
                               context.Runner);
                return;
            }

            // 앞서 세워진 값이 남아 있으면 즉시 통과해 버린다.
            context.Runner.ClearFlag(flagName);
        }

        public override bool IsFinished(SequenceContext context)
        {
            if (string.IsNullOrEmpty(flagName)) return true;
            return context.Runner.HasFlag(flagName);
        }

        public override string Label => $"WaitFlag '{flagName}'";
    }
}
