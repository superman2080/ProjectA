using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 진행도 플래그를 세우고 즉시 끝난다. <b>무대의 해금이 여기서 일어난다</b> —
    /// 어느 무대가 언제 열리는지는 시퀀스가 재생되는 순서가 이미 표현하고 있으므로
    /// 해금 순서표나 의존 그래프를 따로 두지 않는다.
    ///
    /// <para><b>⚠ <see cref="SequenceRunner.SetFlag"/>가 아니라 <see cref="GameProgress"/>다.</b>
    /// 러너의 플래그는 그 시퀀스가 도는 동안만 살아서, 전투 씬을 다녀오면 사라진다.</para>
    ///
    /// <para><b>⚠ 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로, 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.
    /// 옮겨야 하면 <c>[MovedFrom]</c>을 붙인다.</para>
    /// </summary>
    [Serializable]
    public class UnlockStep : SequenceStep
    {
        [Tooltip("세울 진행도 플래그. Encounter의 unlockFlag나 다른 시퀀스의 조건이 이것을 읽는다.")]
        [SerializeField] private string flagName;

        public override void Enter(SequenceContext context)
        {
            if (string.IsNullOrEmpty(flagName))
            {
                Debug.LogError("[UnlockStep] flagName이 비어 있습니다 - 아무것도 해금되지 않습니다.", context.Runner);
                return;
            }

            GameProgress.SetFlag(flagName);
        }

        public override bool IsFinished(SequenceContext context) => true;

        public override string Label => $"Unlock '{flagName}'";
    }
}
