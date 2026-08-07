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
/// 그래서 이동은 <b>클립 시작 시각까지</b> 끝난다(<c>DuelPlan.PlayerArriveTime</c> — 보통 <c>ArriveTime</c>과 같고,
/// 재접근에서는 그보다 앞당겨진다. <b>늦어지는 일은 없다</b>).
/// 여유가 모자란 경우는 디렉터가 애초에 "플레이어 제자리" 계획을 준다.</para>
///
/// <para><b>원위치로 돌아가지 않는다.</b> 벤 자리에서 다음 적을 향해 나아가는 것이 무쌍의 흐름이고,
/// 교전마다 중앙으로 끌려가면 왕복하는 그림이 된다. 무대가 월드에 고정돼 있고 표적도 그 안에서만
/// 골라지므로 <b>플레이어는 자동으로 무대 안에 남는다</b> — 리시로 붙잡을 이유가 없다.</para>
///
/// <para><b>회전은 이동과 따로 돈다</b>(<c>turnDuration</c>). 한 벌로 묶으면 회전이 이동 시간에 끌려가
/// 무대를 가로지르는 내내 목을 천천히 돌리는 그림이 된다.</para>
/// </summary>
public class PlayerCombatMover : MonoBehaviour
{
    [SerializeField] private EnemyDirector enemyDirector;

    [Header("Rotation")]
    [Tooltip("상대 쪽으로 도는 시간(초). 이동 시간과 무관하게 이 값만큼만 걸린다.")]
    [SerializeField] private float turnDuration = 0.15f;

    private Vector3 moveFrom;
    private Vector3 moveTo;
    private float moveStart;
    private float moveEnd;
    private bool moving;

    // 회전은 이동과 따로 돈다 — 스케줄을 한 벌로 묶으면 회전 시간이 이동 시간에 끌려간다.
    private Quaternion turnFrom;
    private Quaternion turnTo;
    private float turnStart;
    private float turnEnd;
    private bool turning;

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
    /// 디렉터가 정한 자리로 간다. 이동은 도착 시각까지, <b>회전은 <c>turnDuration</c>만큼</b> —
    /// 달려가는 내내 도는 게 아니라 먼저 상대를 보고 그 다음에 달린다.
    /// </summary>
    private void HandleDuelScheduled(EnemyDirector.DuelPlan plan)
    {
        // 결투 도착 시각은 칼이 맞는 시각이라 놓칠 수 없다 — 구르는 중이면 즉시 중단하고 이동에 양보한다.
        rolling = false;

        Vector3 target = plan.PlayerPosition;
        target.y = transform.position.y; // 앵커 높이가 실려 붕 뜨지 않게 — 이동은 평면에서만 한다.

        Vector3 facing = Vector3.ProjectOnPlane(plan.EnemyPosition - target, Vector3.up);
        Quaternion rotation = facing.sqrMagnitude < 1e-6f ? transform.rotation : Quaternion.LookRotation(facing);

        // 도착 시각은 디렉터가 정한다. 보통 클립 시작(ArriveTime)이지만, 재접근에서는 그보다 앞당겨진
        // PlayerArriveTime이 온다 — 거리가 창에 안 맞춰지는 그 경로에서 기어가지 않게. 이미 지났으면 즉시 붙인다.
        ScheduleMove(target, Mathf.Max(plan.PlayerArriveTime - Time.time, 0.01f));
        ScheduleTurn(rotation);
    }

    /// <summary>
    /// 교전 상대가 바뀌면 <b>선 자리에서 새 상대 쪽으로 돌기만 한다.</b>
    ///
    /// <para>중앙으로 되돌리지 않는다 — 벤 자리에서 다음 적을 향해 나아가는 것이 무쌍의 흐름이고,
    /// 매번 원점으로 끌려가면 교전마다 왕복하는 그림이 된다.</para>
    /// </summary>
    private void HandleOpponentChanged(EnemyView previous, EnemyView next)
    {
        if (next == null) return;

        // 무대 위 '지금 서 있는 자리'를 본다.
        Vector3 direction = Vector3.ProjectOnPlane(next.transform.position - transform.position, Vector3.up);
        if (direction.sqrMagnitude < 1e-6f) return;

        ScheduleTurn(Quaternion.LookRotation(direction));
    }

    private void ScheduleMove(Vector3 target, float duration)
    {
        moveFrom = transform.position;
        moveTo = target;
        moveStart = Time.time;
        moveEnd = Time.time + Mathf.Max(duration, 0.01f);
        moving = true;
    }

    /// <summary>
    /// <b>회전은 이동과 별개로 스케줄한다.</b> 한 벌로 묶으면 회전이 이동 시간(평균 1.5초)에 끌려가
    /// 목을 천천히 돌리는 그림이 된다 — 무대를 가로지르는 동안 상대를 안 보고 달리는 셈이다.
    ///
    /// <para><see cref="HandleOpponentChanged"/>의 회전이 같은 프레임의 <see cref="HandleDuelScheduled"/>에
    /// 덮이던 것도 이 분리로 사라진다(둘 다 회전만 갱신하므로 나중 값이 이기는 게 옳다).</para>
    /// </summary>
    private void ScheduleTurn(Quaternion rotation)
    {
        turnFrom = transform.rotation;
        turnTo = rotation;
        turnStart = Time.time;
        turnEnd = Time.time + Mathf.Max(turnDuration, 0.01f);
        turning = true;
    }

    // ── 회피 구르기(원호) ────────────────────────────────────────────────────
    private bool rolling;
    private Vector3 rollCenter;
    private float rollRadius;
    private float rollFromAngle;   // 중심 기준 시작 각(도)
    private float rollDeltaAngle;  // 부호 있는 회전량(도). +면 시계 방향
    private float rollStart;
    private float rollEnd;

    /// <summary>
    /// <paramref name="center"/>를 축으로 <b>원호를 그리며</b> 구른다. 회피 성공이 부른다.
    ///
    /// <para><b>직선 보간이면 안 된다</b> — 반경 1.5m에서 60°를 직선으로 이으면 중간에 13%(≈0.2m) 안쪽으로 파고든다.
    /// 결투 간격이 1m 남짓이라 그만큼 칼이 어긋난다. <b>각도를 보간해야 반경이 보존</b>되고,
    /// 그래야 결투 앵커(ImpactAnchor)가 상대에게서 벗어나지 않는다.</para>
    ///
    /// <para>회전은 <see cref="ScheduleTurn"/>을 쓰지 않고 매 프레임 중심을 바라보게 직접 갱신한다 —
    /// 0.15초짜리 회전 스케줄이 0.8초짜리 구르기와 싸우면 도는 도중에 시선이 멈춘다.</para>
    /// </summary>
    public void RollArc(Vector3 center, float signedDegrees, float duration)
    {
        Vector3 offset = Vector3.ProjectOnPlane(transform.position - center, Vector3.up);
        if (offset.sqrMagnitude < 1e-4f) return; // 중심과 겹쳐 있으면 돌 축이 없다

        rollCenter = center;
        rollRadius = offset.magnitude;
        rollFromAngle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        rollDeltaAngle = signedDegrees;
        rollStart = Time.time;
        rollEnd = Time.time + Mathf.Max(duration, 0.01f);

        rolling = true;
        moving = false;   // 이동과 동시에 돌면 두 주인이 위치를 매 프레임 덮어쓴다
        turning = false;  // 회전도 이 구간에는 구르기가 소유한다
    }

    /// <summary>구른 뒤 서게 될 자리. 방향(좌/우)을 고를 때 "적이 없는 쪽"을 재는 데 쓴다.</summary>
    public Vector3 PredictArcEnd(Vector3 center, float signedDegrees)
    {
        Vector3 offset = Vector3.ProjectOnPlane(transform.position - center, Vector3.up);
        if (offset.sqrMagnitude < 1e-4f) return transform.position;

        Vector3 rotated = Quaternion.AngleAxis(signedDegrees, Vector3.up) * offset;
        Vector3 end = center + rotated;
        end.y = transform.position.y;
        return end;
    }

    private void TickRoll()
    {
        float t = Mathf.Clamp01((Time.time - rollStart) / (rollEnd - rollStart));
        float angle = rollFromAngle + rollDeltaAngle * Mathf.SmoothStep(0f, 1f, t);

        float rad = angle * Mathf.Deg2Rad;
        Vector3 position = rollCenter + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * rollRadius;
        position.y = transform.position.y;
        transform.position = position;

        Vector3 facing = Vector3.ProjectOnPlane(rollCenter - position, Vector3.up);
        if (facing.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(facing);

        if (t >= 1f) rolling = false;
    }

    void Update()
    {
        // 구르는 동안은 위치·회전의 주인이 하나다. 이동/회전 보간이 같이 돌면 서로를 덮어쓴다.
        if (rolling)
        {
            TickRoll();
            return;
        }

        if (turning)
        {
            float t = Mathf.Clamp01((Time.time - turnStart) / (turnEnd - turnStart));
            transform.rotation = Quaternion.Slerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f) turning = false;
        }

        if (!moving) return;

        float m = Mathf.Clamp01((Time.time - moveStart) / (moveEnd - moveStart));
        transform.position = Vector3.Lerp(moveFrom, moveTo, Mathf.SmoothStep(0f, 1f, m));

        if (m >= 1f) moving = false;
    }
}
