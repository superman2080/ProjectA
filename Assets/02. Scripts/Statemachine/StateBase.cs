using UnityEngine;

// [FSM] 단일 상태 베이스 클래스
// IState를 구현하는 모든 상태의 공통 베이스.
// 각 메서드는 virtual이므로 필요한 것만 override하여 사용한다.
public abstract class StateBase<T> : IState where T : class
{
    protected T caster;
    
    protected StateBase(T caster)
    {
        this.caster = caster;
    }

    public virtual void Enter() { }

    public virtual void Execute() { }

    public virtual void FixedExecute() { }

    public virtual void Exit() { }
}
