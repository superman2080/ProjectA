using System;
using UnityEngine;

/// <summary>
/// 플레이어 목숨 카운터. <b>HP 바·수치 밸런싱이 아니다</b> — 리듬게임의 남은 목숨이다.
///
/// <para>감소는 <see cref="CharacterActionPlayer.OnPlayerHit"/>에서만 일어난다. 그 이벤트는
/// <b>적 칼이 실제로 닿는 시각</b>에 발행되고, 플레이어가 공격자였던 패턴에서는 아예 발행되지 않는다 —
/// 즉 "적 공격을 못 막았을 때만 깎인다"는 규칙이 이벤트 하나로 이미 표현되어 있다.</para>
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private CharacterActionPlayer actionPlayer;
    [SerializeField] private int maxHealth = 5;

    /// <summary>남은 목숨.</summary>
    public int Current { get; private set; }
    public int Max => maxHealth;

    /// <summary>피격으로 줄어든 순간. 남은 수치를 전달한다(UI·연출용).</summary>
    public event Action<int> OnDamaged;

    /// <summary>0에 도달한 순간. 곡 클리어 실패.</summary>
    public event Action OnDepleted;

    void Awake()
    {
        Current = Mathf.Max(1, maxHealth);
    }

    void OnEnable()
    {
        if (actionPlayer != null) actionPlayer.OnPlayerHit += HandleHit;
    }

    void OnDisable()
    {
        if (actionPlayer != null) actionPlayer.OnPlayerHit -= HandleHit;
    }

    /// <summary>곡을 다시 시작할 때 되돌린다.</summary>
    public void ResetHealth()
    {
        Current = Mathf.Max(1, maxHealth);
    }

    private void HandleHit()
    {
        if (Current <= 0) return;

        Current--;
        OnDamaged?.Invoke(Current);

        if (Current <= 0) OnDepleted?.Invoke();
    }
}
