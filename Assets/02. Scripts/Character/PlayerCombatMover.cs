using EnemySpace;
using UnityEngine;

/// <summary>
/// 플레이어의 결투 이동과 상대 방향 회전. <b>이동은 전부 패턴 사이에서만 일어난다.</b>
///
/// <para><b>플레이어는 목적지를 스스로 정하지 않는다.</b> 둘이 어디서 만날지는 두 위치를 동시에 아는
/// <see cref="EnemyDirector"/>가 정하고(<see cref="EnemyDirector.OnDuelScheduled"/>), 여기서는 자기 몫만 옮긴다.
/// 각자 계산하면 간격이 어긋나고, 간격이 어긋나면 칼이 빗나간다.</para>
///
/// <para><b>왜 도착 시각을 지켜야 하는가</b>: 결투 앵커는 플레이어의 자식이다. 스윙 도중 플레이어가 움직이면
/// 앵커가 따라가 적이 겨냥하던 지점이 늦게 바뀐다 → 칼이 어긋난다.
/// 그래서 이동은 <b>클립 시작 시각까지</b> 끝난다(<c>DuelPlan.ArriveTime</c>).
/// 여유가 모자란 경우는 디렉터가 애초에 "플레이어 제자리" 계획을 준다.</para>
///
/// <para><b>원위치로 돌아가지 않는다.</b> 벤 자리에서 다음 적을 향해 나아가는 것이 무쌍의 흐름이고,
/// 교전마다 중앙으로 끌려가면 왕복하는 그림이 된다. 누적 표류(리시)는 디렉터가 수렴 목표를
/// <c>maxOffset</c>(<b>고정 원점</b> 기준)으로 클램프해 막으므로, 복귀로 리셋할 필요가 없다.</para>
/// </summary>
public class PlayerCombatMover : MonoBehaviour
{
    [SerializeField] private EnemyDirector enemyDirector;

    [Header("Rotation")]
    [Tooltip("교전 상대가 바뀔 때 새 상대 쪽으로 도는 시간(초). 위치는 바뀌지 않는다.")]
    [SerializeField] private float turnDuration = 0.3f;

    private Vector3 moveFrom;
    private Vector3 moveTo;
    private Quaternion turnFrom;
    private Quaternion turnTo;
    private float moveStart;
    private float moveEnd;
    private bool moving;

    void Start()
    {
        turnFrom = turnTo = transform.rotation;
    }

    void OnEnable()
    {
        if (enemyDirector == null) return;

        enemyDirector.OnDuelScheduled += HandleDuelScheduled;
        enemyDirector.OnOpponentChanged += HandleOpponentChanged;
    }

    void OnDisable()
    {
        if (enemyDirector == null) return;

        enemyDirector.OnDuelScheduled -= HandleDuelScheduled;
        enemyDirector.OnOpponentChanged -= HandleOpponentChanged;
    }

    /// <summary>
    /// 디렉터가 정한 자리로 간다. 계획이 "제자리"면(여유 부족) 이동 없이 회전만 맞춘다 —
    /// 그래도 적을 바라보게는 해 둬야 칼이 옆구리로 들어오지 않는다.
    /// </summary>
    private void HandleDuelScheduled(EnemyDirector.DuelPlan plan)
    {
        Vector3 target = plan.PlayerPosition;
        target.y = transform.position.y; // 앵커 높이가 실려 붕 뜨지 않게 — 이동은 평면에서만 한다.

        Vector3 facing = Vector3.ProjectOnPlane(plan.EnemyPosition - target, Vector3.up);
        Quaternion rotation = facing.sqrMagnitude < 1e-6f ? transform.rotation : Quaternion.LookRotation(facing);

        // 도착 시각은 디렉터가 정한다(= 클립 시작). 이미 지났으면 즉시 붙인다.
        float duration = Mathf.Max(plan.ArriveTime - Time.time, 0.01f);
        ScheduleMove(target, rotation, duration);
    }

    /// <summary>
    /// 교전 상대가 바뀌면 <b>선 자리에서 새 상대 쪽으로 돌기만 한다.</b>
    ///
    /// <para>중앙으로 되돌리지 않는다 — 벤 자리에서 다음 적을 향해 나아가는 것이 무쌍의 흐름이고,
    /// 매번 원점으로 끌려가면 교전마다 왕복하는 그림이 된다.
    /// 표류는 <see cref="EnemyDirector"/>가 수렴 목표를 <c>maxOffset</c>(고정 원점 기준)으로
    /// 클램프해 막으므로, 복귀로 리셋할 필요가 없다.</para>
    /// </summary>
    private void HandleOpponentChanged(EnemyView previous, EnemyView next)
    {
        if (next == null) return;

        // 링 슬롯이 아니라 '지금 서 있는 자리'를 본다 — 승격된 적은 이미 대기석에 나와 있어
        // 링 좌표를 보면 엉뚱한 쪽으로 돌아선다.
        Vector3 direction = Vector3.ProjectOnPlane(next.transform.position - transform.position, Vector3.up);
        if (direction.sqrMagnitude < 1e-6f) return;

        ScheduleMove(transform.position, Quaternion.LookRotation(direction), turnDuration);
    }

    private void ScheduleMove(Vector3 target, Quaternion rotation, float duration)
    {
        moveFrom = transform.position;
        moveTo = target;
        turnFrom = transform.rotation;
        turnTo = rotation;
        moveStart = Time.time;
        moveEnd = Time.time + Mathf.Max(duration, 0.01f);
        moving = true;
    }

    void Update()
    {
        if (!moving) return;

        float t = Mathf.Clamp01((Time.time - moveStart) / (moveEnd - moveStart));
        float eased = Mathf.SmoothStep(0f, 1f, t);

        transform.position = Vector3.Lerp(moveFrom, moveTo, eased);
        transform.rotation = Quaternion.Slerp(turnFrom, turnTo, eased);

        if (t >= 1f) moving = false;
    }
}
