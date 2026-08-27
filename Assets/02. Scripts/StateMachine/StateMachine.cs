using System;
using System.Collections.Generic;
using UnityEngine;

// [FSM] 스테이트 머신
// 상태 등록, 전환 조건 관리, 매 프레임 상태 업데이트를 담당한다.
// - Enum 키 또는 StateBase 인스턴스로 상태를 직접 전환할 수 있다.
// - 조건 기반 자동 전환 (RegisterCondition) 과 AnyState 전환을 지원한다.
// - 시작은 ChangeState, 정지는 Stop 하나뿐이다. 계층(서브 머신)은 지원하지 않는다.
//
// ⚠ 전이 구성은 '실행 전에' 확정한다.
//   머신을 돌리기 시작하기 전에 RegisterState / RegisterCondition / RegisterAnyStateTransition을
//   전부 끝내고, 그 뒤로는 구성을 바꾸지 않는다. 그래서 해제 API(UnregisterState / UnregisterCondition /
//   UnregisterAnyStateTransition / ClearAnyStateTransitions)를 두지 않는다 — 런타임에 전이를
//   추가·삭제하는 것은 기능이 아니라 오용이다.
//   이 규칙이 두 가지를 원천 봉쇄한다:
//     (1) 조건 검사 중 컬렉션이 변경되어 순회가 깨지는 것
//     (2) 상태를 해제했는데 전이 목록에 남아 되살아나는 것 (전이 목록의 키가 인스턴스라서 생기던 문제)

public class StateMachine<T> where T : class
{
    // 현재 활성 상태
    public StateBase<T> CurState { get => state; }

    // 상태 전환 시 발생하는 이벤트 (이전 상태, 다음 상태)
    public event Action<StateBase<T>, StateBase<T>> OnStateChanged;

    private StateBase<T> state;
    private T caster;

    // ChangeState 실행 중 플래그. Enter/Exit 안에서의 재진입 호출을 잡는다.
    private bool isTransitioning;

    // Enum 키로 상태 인스턴스를 조회하는 딕셔너리
    private Dictionary<Enum, StateBase<T>> stateDict = new();

    // 특정 상태에서 다음 상태로의 조건부 전환 목록
    private Dictionary<StateBase<T>, List<(Func<bool> condition, StateBase<T> next)>> transitionDict = new();

    // 현재 상태에 관계없이 조건이 충족되면 전환되는 AnyState 전환 목록
    private List<(Func<bool> condition, StateBase<T> next)> anyStateTransitions = new();

    // 초기 상태 없이 생성 (나중에 ChangeState로 진입)
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

        // 전이 도중의 재진입은 거부한다. 허용하면 안쪽 전이가 끝난 뒤 바깥 전이가
        // state를 덮어써 안쪽 전이가 통째로 사라지고(OnStateChanged의 prevState도 어긋난다),
        // 증상이 "전이를 호출했는데 조용히 안 바뀐다"라 원인을 못 짚는다.
        // 상태 전환은 조건 평가로만 일어나야 한다 - Enter/Exit에서는 플래그만 세우고
        // 다음 Update의 조건 검사가 집어가게 한다.
        if (isTransitioning)
        {
            Debug.LogError($"[StateMachine<{typeof(T).Name}>] 전이 도중에 ChangeState가 다시 호출됐습니다 " +
                           $"({state?.GetType().Name} -> {newState.GetType().Name}). 이 호출은 무시됩니다. " +
                           "Enter/Exit 안에서 상태를 바꾸지 말고 전이 조건으로 표현하세요.");
            return;
        }

        isTransitioning = true;
        var prevState = state;
        try
        {
            state?.Exit();
            state = newState;
            state?.Enter();
        }
        finally
        {
            // Enter/Exit가 예외를 던져도 플래그가 갇히지 않게 한다 - 갇히면 이후 모든 전이가 막힌다.
            isTransitioning = false;
        }

        OnStateChanged?.Invoke(prevState, state);
    }

    // 현재 상태를 종료하고 새 상태로 즉시 전환 (Enum 키로 조회)
    public void ChangeState(Enum newState)
    {
        if (!stateDict.TryGetValue(newState, out var toState))
            return;

        ChangeState(toState);
    }

    // 현재 상태를 종료하고 머신을 멈춘다. 이후 Update는 아무것도 하지 않으며,
    // 다시 돌리려면 ChangeState로 진입한다. (소유자의 OnDisable 등에서 사용)
    public void Stop()
    {
        // ChangeState와 같은 가드 - 여기서 막지 않으면 Exit 안의 ChangeState가
        // 방금 멈춘 머신을 되살려 Stop이 아무 효과도 없게 된다.
        isTransitioning = true;
        try
        {
            state?.Exit();
        }
        finally
        {
            isTransitioning = false;
        }

        state = null;
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

    // 상태가 해당 Enum 키로 등록된 바로 그 인스턴스인지 비교.
    // 위 두 오버로드는 '같은 종류인가'(타입)를 묻지만 이쪽은 '바로 그 상태인가'(인스턴스)를 묻는다 —
    // Enum 키는 인스턴스 하나를 정확히 가리키므로 타입 비교보다 좁고 정확하다.
    // 미등록 키는 false. (예전에는 stateDict[e]를 직접 인덱싱해 KeyNotFoundException을 던졌다.)
    public bool CompareState(StateBase<T> s1, Enum e)
    {
        if (s1 == null || e == null) return false;
        return stateDict.TryGetValue(e, out var target) && s1 == target;
    }

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
