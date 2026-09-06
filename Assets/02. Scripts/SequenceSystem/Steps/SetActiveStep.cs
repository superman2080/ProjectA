using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 씬 오브젝트 하나를 켜거나 끈다. 검은 화면 사이에 무대를 정리하는 데 쓴다
    /// (균열 이펙트를 끄고, 미리 놓아 둔 파편을 켠다).
    ///
    /// <para><b>켜기와 끄기를 나누지 않는다</b> — 같은 동작이라 <see cref="active"/> 한 칸이면 된다
    /// (<see cref="HighlightStep"/>이 끄는 것도 자기가 하는 것과 같은 결).</para>
    ///
    /// <para><b>기다리지 않는다 — <see cref="IsFinished"/>가 언제나 true다</b>
    /// (<see cref="HighlightStep"/> · <see cref="TutorialCueStep"/>과 같은 관용구).
    /// 무엇을 덮은 채로 바꿀지는 <b>스텝 순서</b>가 정한다 — 앞에 놓인 페이드 스텝이
    /// 완료를 기다리므로 "검은 화면 사이에서만 바뀐다"가 순서만으로 성립한다.</para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class SetActiveStep : SequenceStep
    {
        [SequenceSlot]
        [Tooltip("켜거나 끌 씬 오브젝트 슬롯.")]
        [SerializeField] private string targetSlot;

        [Tooltip("켤 것인가.")]
        [SerializeField] private bool active;

        public string TargetSlot => targetSlot;

        public override void Enter(SequenceContext context)
        {
            Transform target = context.Bindings.ResolveTransform(targetSlot, context.Runner);

            if (target == null)
            {
                // 대상이 없어도 시퀀스는 멎지 않는다(배선이 비면 조용히 비활성되는 기존 규율).
                Debug.LogError($"[SetActiveStep] 대상을 찾지 못했습니다(slot: '{targetSlot}'). " +
                               "이 스텝을 건너뜁니다.", context.Runner);
                return;
            }

            target.gameObject.SetActive(active);
        }

        public override bool IsFinished(SequenceContext context) => true;

        public override string Label => $"SetActive '{targetSlot}' = {active}";
    }
}
