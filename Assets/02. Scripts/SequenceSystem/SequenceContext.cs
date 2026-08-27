using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 스텝이 씬 세계에 닿는 유일한 통로. 스텝은 씬 오브젝트를 직접 들지 않고 전부 여기를 거친다 —
    /// 그래야 스텝이 에셋에 직렬화될 수 있다.
    /// </summary>
    public class SequenceContext
    {
        public SequenceContext(SequenceRunner runner, SequenceBindings bindings,
                               Transform player, PlayerModeDirector playerMode)
        {
            Runner = runner;
            Bindings = bindings;
            Player = player;
            PlayerMode = playerMode;
        }

        /// <summary>실행 중인 러너. 플래그 조회·코루틴 시작에 쓴다.</summary>
        public SequenceRunner Runner { get; }

        public SequenceBindings Bindings { get; }

        /// <summary>슬롯을 비운 스텝의 기본 대상.</summary>
        public Transform Player { get; }

        /// <summary>비어 있을 수 있다(모드 개념이 없는 씬).</summary>
        public PlayerModeDirector PlayerMode { get; }

        /// <summary>코루틴이 필요한 스텝이 쓴다.</summary>
        public MonoBehaviour Owner => Runner;

        /// <summary>현재 스텝에 진입한 뒤 지난 시간(초). 스텝이 자기 타이머를 들지 않아도 되게 한다.</summary>
        public float ElapsedInStep => Runner != null ? Runner.ElapsedInStep : 0f;
    }
}
