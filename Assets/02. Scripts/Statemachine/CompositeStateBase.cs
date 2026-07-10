using UnityEngine;

// [HFSM] 복합 상태 베이스 클래스
// 내부에 서브 스테이트 머신을 보유하여 계층적 상태 구조를 구성한다.
// 부모 상태는 공통 로직(중력, HP 체크 등)을 담당하고,
// 자식 상태는 세부 행동을 담당한다.
//
// 사용 방법:
//   1. 이 클래스를 상속하여 복합 상태를 구현한다.
//   2. 생성자에서 SubStateMachine에 자식 상태들을 등록한다.
//   3. SetInitialState로 초기 자식 상태를 지정한다.
//   4. 공통 로직은 Execute 등을 override하고 base.Execute()를 반드시 호출한다.
public abstract class CompositeStateBase<T> : StateBase<T> where T : class
{
    // 자식 상태들을 관리하는 서브 스테이트 머신
    public StateMachine<T> SubStateMachine { get; protected set; }

    public CompositeStateBase(T caster): base(caster)
    {
        SubStateMachine = new StateMachine<T>(caster);
    }

    // 진입: 부모 로직 실행 후 서브 머신의 초기 자식 상태 Enter 호출
    public override void Enter()
    {
        base.Enter();
        SubStateMachine.Enter();
    }

    // 업데이트: 부모 공통 로직 실행 후 서브 머신 Update 위임
    public override void Execute()
    {
        base.Execute();
        SubStateMachine.Update();
    }

    // 물리 업데이트: 부모 공통 로직 실행 후 서브 머신 FixedUpdate 위임
    public override void FixedExecute()
    {
        base.FixedExecute();
        SubStateMachine.FixedUpdate();
    }

    // 종료: 자식 상태 Exit 먼저 호출 후 부모 정리 (역순 보장)
    public override void Exit()
    {
        SubStateMachine.Exit();
        base.Exit();
    }
}
