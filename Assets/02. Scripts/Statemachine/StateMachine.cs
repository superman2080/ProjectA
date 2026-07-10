using System;
using System.Collections.Generic;
using UnityEngine;

// [FSM / HFSM] 스테이트 머신
// 상태 등록, 전환 조건 관리, 매 프레임 상태 업데이트를 담당한다.
// - Enum 키 또는 StateBase 인스턴스로 상태를 직접 전환할 수 있다.
// - 조건 기반 자동 전환 (RegisterCondition) 과 AnyState 전환을 지원한다.
// - HFSM에서 CompositeStateBase가 서브 머신으로 사용할 수 있도록 Enter/Exit/SetInitialState를 제공한다.

public class StateMachine<T> where T : class
{
    // 현재 활성 상태
    public StateBase<T> CurState { get => state; }

    // 상태 전환 시 발생하는 이벤트 (이전 상태, 다음 상태)
    public event Action<StateBase<T>, StateBase<T>> OnStateChanged;

    private StateBase<T> state;
    private T caster;

    // Enum 키로 상태 인스턴스를 조회하는 딕셔너리
    private Dictionary<Enum, StateBase<T>> stateDict = new();

    // 특정 상태에서 다음 상태로의 조건부 전환 목록
    private Dictionary<StateBase<T>, List<(Func<bool> condition, StateBase<T> next)>> transitionDict = new();

    // 현재 상태에 관계없이 조건이 충족되면 전환되는 AnyState 전환 목록
    private List<(Func<bool> condition, StateBase<T> next)> anyStateTransitions = new();

    // 초기 상태 없이 생성 (나중에 SetInitialState 또는 ChangeState로 진입)
    public StateMachine(T caster)
    {
        this.caster = caster;
    }

    // 초기 상태와 함께 생성 (생성 즉시 Enter 호출)
    public StateMachine(T caster, StateBase<T> state)
    {
        this.caster = caster;
        this.state = state;
        this.state.Enter();
    }

    // 현재 상태를 종료하고 새 상태로 즉시 전환 (인스턴스 직접 지정)
    public void ChangeState(StateBase<T> newState)
    {
        if (newState == null) return;

        var prevState = state;
        state?.Exit();
        state = newState;
        state?.Enter();

        OnStateChanged?.Invoke(prevState, state);
    }

    // 현재 상태를 종료하고 새 상태로 즉시 전환 (Enum 키로 조회)
    public void ChangeState(Enum newState)
    {
        if (!stateDict.TryGetValue(newState, out var toState))
            return;

        ChangeState(toState);
    }

    // HFSM용: 부모 상태 Enter 시점에 서브 머신을 외부에서 시작
    public void Enter()
    {
        state?.Enter();
    }

    // HFSM용: 부모 상태 Exit 시점에 서브 머신을 외부에서 종료
    public void Exit()
    {
        state?.Exit();
        state = null;
    }

    // HFSM용: Enter 없이 초기 상태만 지정 (생성 시점과 진입 시점 분리)
    public void SetInitialState(Enum type)
    {
        if (stateDict.TryGetValue(type, out var initState))
            state = initState;
    }

    // 매 프레임: 현재 상태 Execute 후 전환 조건 검사
    public void Update()
    {
        state?.Execute();
        CheckNextStateCondition();
    }

    // 고정 프레임: 현재 상태 FixedExecute 호출
    public void FixedUpdate()
    {
        state?.FixedExecute();
    }

    #region State Registration

    // 상태를 Enum 키와 함께 등록
    public void RegisterState(Enum type, StateBase<T> state)
    {
        stateDict[type] = state;
    }

    // 등록된 상태를 Enum 키로 제거
    public void UnregisterState(Enum type)
    {
        stateDict.Remove(type);
    }

    // Enum 키로 등록된 상태 인스턴스 반환 (없으면 null)
    public StateBase<T> GetState(Enum type)
    {
        return stateDict.TryGetValue(type, out var state) ? state : null;
    }

    #endregion

    #region State Comparison

    // 두 상태가 같은 타입인지 비교
    public bool CompareState(StateBase<T> s1, StateBase<T> s2)
    {
        if (s1 == null || s2 == null) return false;
        return s1.GetType() == s2.GetType();
    }

    // 상태의 타입 이름이 문자열과 일치하는지 비교
    public bool CompareState(StateBase<T> s1, string s2)
    {
        return s1 != null && s2 != null && s1.GetType().Name.Equals(s2);
    }

    public bool CompareState(StateBase<T> s1, Enum e) => s1 == stateDict[e];

    #endregion

    #region Transition Conditions

    // 특정 상태(from)에서 특정 상태(to)로의 조건부 전환 등록 (인스턴스 직접 지정)
    public void RegisterCondition(StateBase<T> from, StateBase<T> to, Func<bool> condition)
    {
        if (from == null || to == null || condition == null) return;

        if (!transitionDict.ContainsKey(from))
            transitionDict[from] = new List<(Func<bool>, StateBase<T>)>();

        transitionDict[from].Add((condition, to));
    }

    // 특정 상태(from)에서 특정 상태(to)로의 조건부 전환 등록 (Enum 키로 조회)
    public void RegisterCondition(Enum from, Enum to, Func<bool> condition)
    {
        RegisterCondition(GetState(from), GetState(to), condition);
    }

    public void RegisterCondition(Enum from, StateBase<T> to, Func<bool> condition)
    {
        RegisterCondition(GetState(from), to, condition);
    }

    // 특정 from → to 전환 조건 제거 (인스턴스 직접 지정)
    public void UnregisterCondition(StateBase<T> from, StateBase<T> to)
    {
        if (from == null || to == null) return;

        if (transitionDict.TryGetValue(from, out var transitions))
            transitions.RemoveAll(tran => tran.next == to);
    }

    // 특정 from → to 전환 조건 제거 (Enum 키로 조회)
    public void UnregisterCondition(Enum from, Enum to)
    {
        UnregisterCondition(GetState(from), GetState(to));
    }

    #endregion

    #region Any State Transitions

    // 현재 상태와 무관하게 조건 충족 시 전환되는 AnyState 전환 등록 (인스턴스 직접 지정)
    public void RegisterAnyStateTransition(StateBase<T> to, Func<bool> condition)
    {
        if (to == null || condition == null) return;

        anyStateTransitions.Add((condition, to));
    }

    // AnyState 전환 등록 (Enum 키로 조회)
    public void RegisterAnyStateTransition(Enum to, Func<bool> condition)
    {
        RegisterAnyStateTransition(GetState(to), condition);
    }

    // 특정 목적지로의 AnyState 전환 제거 (인스턴스 직접 지정)
    public void UnregisterAnyStateTransition(StateBase<T> to)
    {
        if (to == null) return;

        anyStateTransitions.RemoveAll(t => t.next == to);
    }

    // 특정 목적지로의 AnyState 전환 제거 (Enum 키로 조회)
    public void UnregisterAnyStateTransition(Enum to)
    {
        UnregisterAnyStateTransition(GetState(to));
    }

    // 등록된 모든 AnyState 전환 제거
    public void ClearAnyStateTransitions()
    {
        anyStateTransitions.Clear();
    }

    #endregion

    #region Transition Check

    // AnyState → 일반 전환 순서로 조건 검사 (AnyState가 우선)
    private void CheckNextStateCondition()
    {
        if (state == null) return;

        if (CheckAnyStateTransitions())
            return;

        CheckNormalTransitions();
    }

    // AnyState 전환 조건 검사: 조건 충족 시 전환 후 true 반환
    private bool CheckAnyStateTransitions()
    {
        foreach (var (condition, next) in anyStateTransitions)
        {
            // 이미 목적지 상태에 있으면 자기 자신으로 전환 방지
            if (state == next) continue;

            if (condition())
            {
                ChangeState(next);
                return true;
            }
        }
        return false;
    }

    // 현재 상태에 등록된 일반 전환 조건 순서대로 검사, 첫 충족 조건으로 전환
    private void CheckNormalTransitions()
    {
        if (!transitionDict.TryGetValue(state, out var transitions))
            return;

        foreach (var (condition, next) in transitions)
        {
            if (condition())
            {
                ChangeState(next);
                break;
            }
        }
    }

    #endregion
}
