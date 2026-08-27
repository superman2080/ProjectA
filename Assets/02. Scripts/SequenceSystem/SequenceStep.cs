using System;

namespace SequenceSpace
{
    /// <summary>
    /// 시퀀스의 한 단계. <b>MonoBehaviour가 아니다</b> — <see cref="SequenceAsset"/> 안에
    /// <c>[SerializeReference]</c> 리스트로 직렬화되어 에셋 하나에 여러 종류가 함께 산다.
    ///
    /// <para><b>왜 <c>IState</c>를 공유하지 않는가</b>: 시퀀스는 선형 큐라 조건 전이도 AnyState도 없다.
    /// 인터페이스를 공유하면 <c>FixedExecute</c> 같은 빈 구현이 스텝마다 하나씩 붙는다.</para>
    ///
    /// <para><b>⚠ 런타임 상태는 값 타입만 둔다.</b> 러너가 <see cref="CreateRuntimeCopy"/>로
    /// 얕은 복사본을 만들어 돌리므로, 값 타입 필드(경과 시간·완료 플래그)는 안전하지만
    /// <b>참조 타입 필드를 수정하면 에셋 원본이 바뀐다</b>(리스트에 Add 하는 등).</para>
    /// </summary>
    [Serializable]
    public abstract class SequenceStep
    {
        /// <summary>이 스텝에 진입했을 때 1회.</summary>
        public virtual void Enter(SequenceContext context) { }

        /// <summary>매 프레임. <see cref="IsFinished"/> 검사 직전에 호출된다.</summary>
        public virtual void Tick(SequenceContext context) { }

        /// <summary>이 스텝을 떠날 때 1회.</summary>
        public virtual void Exit(SequenceContext context) { }

        /// <summary>이 스텝이 끝났는가. 러너가 매 프레임 폴링한다.</summary>
        public abstract bool IsFinished(SequenceContext context);

        /// <summary>인스펙터·씬 뷰에 표시할 짧은 이름.</summary>
        public virtual string Label => GetType().Name;

        /// <summary>
        /// 러너가 재생 시작 시 만드는 실행용 복사본. <b>얕은 복사로 충분하다</b> —
        /// 값 타입 런타임 상태만 갈라지면 되고, 저작 데이터(리스트·에셋 참조)는 읽기 전용으로 공유해도 된다.
        /// 이것이 없으면 재생 중 런타임 상태가 <b>에셋에 그대로 저장된다</b>.
        /// </summary>
        public SequenceStep CreateRuntimeCopy() => (SequenceStep)MemberwiseClone();
    }
}
