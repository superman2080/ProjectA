using UnityEngine;

// [FSM] 상태 인터페이스
// 모든 상태가 구현해야 하는 생명주기 메서드를 정의한다.

public interface IState
{

    // 상태 진입 시 1회 호출
    void Enter();

    // 매 프레임 호출 (Update)
    void Execute();

    // 고정 프레임 호출 (FixedUpdate, 물리 연산용)
    void FixedExecute();

    // 상태 종료 시 1회 호출
    void Exit();
}
