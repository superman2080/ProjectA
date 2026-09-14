using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 씬을 넘긴다. 덮기·비동기 로드·최소 지속시간·로딩 링은 전부 <see cref="SceneTransition"/>이 든다 —
    /// 이 스텝은 <b>어느 씬인가</b>만 안다.
    ///
    /// <para><b>⚠ 이 스텝은 일부러 끝나지 않는다</b>(<see cref="IsFinished"/>가 언제나 false).
    /// 씬이 바뀌면 러너째 사라지므로 뒤에 올 스텝이 있을 수 없고, 끝났다고 말하면
    /// 로드가 도는 동안 남은 스텝이 실행된다. <b>시퀀스의 마지막에 둔다.</b></para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class LoadSceneStep : SequenceStep
    {
        [Tooltip("넘어갈 씬 이름. Build Settings에 등록돼 있어야 한다.")]
        [SerializeField] private string sceneName;

        public override void Enter(SequenceContext context)
        {
            SceneTransition transition = SceneTransition.Instance;

            if (transition == null)
            {
                Debug.LogError("[LoadSceneStep] SceneTransition을 찾지 못했습니다 - " +
                               "Managers 프리팹이 씬에 있는지 확인하세요.", context.Runner);
                return;
            }

            transition.Load(sceneName);
        }

        // 씬이 넘어가면 러너가 사라진다. 여기서 끝났다고 말할 이유가 없다.
        public override bool IsFinished(SequenceContext context) => false;

        public override string Label => $"LoadScene {sceneName}";
    }
}
