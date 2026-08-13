using EnemySpace;
using PatternSpace;
using UnityEngine;

/// <summary>
/// 플레이어의 결투 이동과 상대 방향 회전. <b>수렴 이동은 패턴 사이에서만 일어난다.</b>
/// 패턴 <b>안</b>에서 움직이는 경우는 하나뿐이다 — 패턴이 결투 거리 커브를 들고 있을 때
/// (<see cref="Pattern.DuelGapAt"/>). 그때는 적이 고정이고 플레이어가 축 위에서 간격을 그린다.
///
/// <para><b>플레이어는 목적지를 스스로 정하지 않는다.</b> 둘이 어디서 만날지는 두 위치를 동시에 아는
/// <see cref="EnemyDirector"/>가 정하고(<see cref="EnemyDirector.OnDuelScheduled"/>), 여기서는 자기 몫만 옮긴다.
/// 각자 계산하면 간격이 어긋나고, 간격이 어긋나면 칼이 빗나간다.</para>
///
/// <para><b>왜 도착 시각을 지켜야 하는가</b>: 결투 앵커는 플레이어의 자식이다. 스윙 도중 플레이어가 움직이면
/// 앵커가 따라가 적이 겨냥하던 지점이 늦게 바뀐다 → 칼이 어긋난다.
/// (거리 커브는 이 제약을 깨지 않는다 — 적을 고정하고 플레이어만 움직이므로 <b>간격이 곧 커브값</b>이고,
/// 저작자는 임팩트 시점 값으로 리치를 맞춘다. 단 <c>Attacker.Enemy</c> 패턴에서 적 칼은 예약 시점의
/// 자리를 겨냥하므로 그 정렬은 저작자 책임이다.)
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

    [Tooltip("결투 거리 커브의 시간 원점을 제공한다(DuelCurveTime). 비우면 커브 구동이 꺼진다 — 예전 동작.")]
    [SerializeField] private CharacterActionPlayer actionPlayer;

    [Tooltip("결투 거리 커브 진단 로그. 한 구동이 끝날 때 도달한 t·간격 극값을 한 줄로 찍는다.")]
    [SerializeField] private bool logDuelCurve;

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
        // ⚠ 계획은 이번 임팩트보다 먼저 온다(완료 = 마지막 노드 입력, 임팩트는 거기서 goodWindow + offset 뒤).
        // 그래서 여기서 바로 갈아끼우면 지금 커브의 임팩트 이후 구간이 통째로 재생되지 않는다.
        if (ShouldDeferHandover(plan))
        {
            pendingPlan = plan;
            return;
        }

        ApplyPlan(plan);
    }

    /// <summary>
    /// 지금 커브가 <b>임팩트 이후 구간을 아직 그리는 중</b>이면 인수인계를 미룬다.
    /// 미루지 않으면 그 구간(= 관통)이 한 번도 화면에 안 나온다.
    ///
    /// <para><b>다음 도착 시각은 넘기지 않는다</b> — 위치 소유권보다 칼이 맞는 시각이 우선이다.</para>
    /// </summary>
    private bool ShouldDeferHandover(EnemyDirector.DuelPlan plan)
    {
        if (!IsCurveDriving()) return false;

        return Time.time < plan.PlayerArriveTime;
    }

    /// <summary>지금 이 커브가 자기 클립을 타고 구동 중이며 아직 마지막 키에 닿지 않았는가.</summary>
    private bool IsCurveDriving()
    {
        if (curveTemplate == null || actionPlayer == null || rolling) return false;
        if (!Mathf.Approximately(actionPlayer.PlayingImpactAlignTime, curveImpactTime)) return false;

        float relTime = actionPlayer.DuelCurveTime;

        return !float.IsNaN(relTime) && relTime < curveTemplate.DuelCurveEndTime;
    }

    private void ApplyPlan(EnemyDirector.DuelPlan plan)
    {
        DumpCurveLog(pendingPlan.HasValue ? "보류 해제" : "즉시");
        pendingPlan = null;

        // 결투 도착 시각은 칼이 맞는 시각이라 놓칠 수 없다 — 구르는 중이면 즉시 중단하고 이동에 양보한다.
        rolling = false;
        curveSuspended = false; // 새 계획이 커브를 통째로 갈아끼우므로 재워 둔 것을 되살릴 이유가 없다

        Vector3 target = plan.PlayerPosition;
        target.y = transform.position.y; // 앵커 높이가 실려 붕 뜨지 않게 — 이동은 평면에서만 한다.

        Vector3 facing = Vector3.ProjectOnPlane(plan.EnemyPosition - target, Vector3.up);
        Quaternion rotation = facing.sqrMagnitude < 1e-6f ? transform.rotation : Quaternion.LookRotation(facing);

        // 도착 시각은 디렉터가 정한다. 보통 클립 시작(ArriveTime)이지만, 재접근에서는 그보다 앞당겨진
        // PlayerArriveTime이 온다 — 거리가 창에 안 맞춰지는 그 경로에서 기어가지 않게. 이미 지났으면 즉시 붙인다.
        ScheduleMove(target, Mathf.Max(plan.PlayerArriveTime - Time.time, 0.01f));
        ScheduleTurn(rotation);

        BeginCurveDrive(plan, target);
    }

    // ── 결투 거리 커브 ───────────────────────────────────────────────────────
    /// <summary>
    /// 아직 걸지 않은 다음 교전 계획. <b>커브의 임팩트 이후 구간이 끝나기를 기다리는 중</b>이다.
    /// 최대 하나면 된다 — 계획은 순차적이고, 기다리는 구간은 다음 계획이 오기 전에 끝난다.
    /// </summary>
    private EnemyDirector.DuelPlan? pendingPlan;

    private Pattern curveTemplate;   // 커브를 든 패턴. null이면 구동 안 함(= 예전 상수 동작)
    private Vector3 curveEnemyPosition;
    private Vector3 curveAxis;       // 플레이어 → 적 단위벡터. 커브는 이 축 위에서만 민다
    private float curveBaseDistance; // 커브가 비어 있을 때 쓰는 상수 폴백(계획이 잡은 간격)
    private float curveImpactTime;   // 이 커브가 속한 패턴의 임팩트 절대시각(= 주인을 대조하는 신분증)
    private bool curveSuspended;     // 구르는 동안만 재워 둔다. 끝나면 축을 다시 잡고 이어받는다

    /// <summary>
    /// 도착 뒤 간격을 이어받을 커브를 건다. <b>적은 고정이고 플레이어만 움직인다</b> —
    /// 그래서 간격은 파생값이 아니라 <c>enemy − player(t)</c>라는 정의 그 자체다.
    ///
    /// <para>커브가 없는 패턴이면 구동을 아예 안 켠다(도착 뒤 아무도 위치를 안 건드리는 예전 동작).</para>
    /// </summary>
    private void BeginCurveDrive(EnemyDirector.DuelPlan plan, Vector3 playerTarget)
    {
        if (actionPlayer == null || plan.Template == null || !plan.Template.HasDuelDistanceCurve)
        {
            curveTemplate = null;
            return;
        }

        ResetCurveLog();

        if (logDuelCurve)
            Debug.Log($"[DuelCurve] {plan.Template.name} 계획 — 배치간격 " +
                      $"{Vector3.ProjectOnPlane(plan.EnemyPosition - playerTarget, Vector3.up).magnitude:F2}m, " +
                      $"커브 t {plan.Template.DuelCurveStartTime:F3}~{plan.Template.DuelCurveEndTime:F3}, " +
                      $"도착까지 {plan.PlayerArriveTime - Time.time:F3}s, 임팩트까지 {plan.ImpactTime - Time.time:F3}s");

        curveTemplate = plan.Template;
        curveEnemyPosition = plan.EnemyPosition;
        curveAxis = plan.Axis;
        curveImpactTime = plan.ImpactTime;
        curveBaseDistance = Vector3.ProjectOnPlane(plan.EnemyPosition - playerTarget, Vector3.up).magnitude;
    }

    /// <summary>
    /// 커브가 그리는 간격대로 축 위에서 플레이어를 민다. <b>시각은 재생 헤드에서 온다</b>
    /// (<see cref="CharacterActionPlayer.DuelCurveTime"/>) — 월드 시각을 쓰면 히트스톱에 얼지 않고
    /// 배속 압축을 따라가지 못한다.
    ///
    /// <para>재생 중인 액션이 없으면(NaN) 아무것도 하지 않는다 — 마지막 위치가 그대로 남는 것이
    /// 곧 커브 구간 밖의 '홀드'와 같은 결과다.</para>
    /// </summary>
    private void TickCurveDrive()
    {
        // ⚠ 이 커브의 주인이 지금 재생 중인지 먼저 본다. 계획은 클립보다 먼저 도착하므로,
        // 안 보면 '직전 패턴의 헤드'로 새 커브를 읽어 마지막 키 값(예: −5m)으로 튄다 —
        // 화면에서는 적을 관통해 뒤로 순간이동한다. NaN이면 이 비교가 false라 자연히 걸러진다.
        if (!Mathf.Approximately(actionPlayer.PlayingImpactAlignTime, curveImpactTime))
        {
            logBlockedGate++;
            return;
        }

        float relTime = actionPlayer.DuelCurveTime;
        if (float.IsNaN(relTime))
        {
            logBlockedNaN++;
            return;
        }

        float gap = curveTemplate.DuelGapAt(relTime, curveBaseDistance);

        Vector3 position = curveEnemyPosition - curveAxis * gap;
        position.y = transform.position.y;
        transform.position = position;

        logDriveFrames++;
        logMinRel = Mathf.Min(logMinRel, relTime);
        logMaxRel = Mathf.Max(logMaxRel, relTime);
        logMinGap = Mathf.Min(logMinGap, gap);
        logMaxGap = Mathf.Max(logMaxGap, gap);
    }

    // ── 진단 로그 ────────────────────────────────────────────────────────────
    // 커브가 '어디까지 그려졌나'는 프레임마다 찍으면 안 보인다 — 한 구동의 극값만 모아 끝에 한 줄로 낸다.
    private int logDriveFrames, logBlockedGate, logBlockedNaN, logBlockedMoving;
    private float logMinRel, logMaxRel, logMinGap, logMaxGap;

    private void ResetCurveLog()
    {
        logDriveFrames = logBlockedGate = logBlockedNaN = logBlockedMoving = 0;
        logMinRel = logMinGap = float.PositiveInfinity;
        logMaxRel = logMaxGap = float.NegativeInfinity;
    }

    private void DumpCurveLog(string reason)
    {
        if (!logDuelCurve || curveTemplate == null) return;

        Debug.Log($"[DuelCurve] {curveTemplate.name} 인수인계({reason}) — " +
                  $"구동 {logDriveFrames}F, t {logMinRel:F3}~{logMaxRel:F3} (커브 {curveTemplate.DuelCurveStartTime:F3}~{curveTemplate.DuelCurveEndTime:F3}), " +
                  $"gap {logMinGap:F2}~{logMaxGap:F2}m, 차단 gate {logBlockedGate} / NaN {logBlockedNaN} / moving {logBlockedMoving}");
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
        moving = false;        // 이동과 동시에 돌면 두 주인이 위치를 매 프레임 덮어쓴다
        turning = false;       // 회전도 이 구간에는 구르기가 소유한다

        // ⚠ 커브를 <b>버리지 않고 재운다</b>. 기습 회피는 <b>클립이 시작되기 전 공백</b>에서 일어나므로
        // 여기서 버리면 이번 패턴의 커브가 한 번도 안 돈다 — 회피한 패턴만 관통이 사라진다.
        // 구른 뒤 자리는 구르기가 정하고, 그 자리에서 축만 다시 잡아 커브가 이어받는다.
        curveSuspended = curveTemplate != null;
    }

    /// <summary>
    /// 구르기가 끝난 자리에서 커브를 다시 세운다. <b>바뀐 것은 축뿐</b>이다 —
    /// 적도, 커브도, 임팩트도 그대로이므로 간격의 정의(<c>enemy − player(t)</c>)는 유지된다.
    ///
    /// <para>축을 안 다시 잡으면 원호를 돈 만큼 축이 어긋난 채로 밀려 <b>적 옆구리를 향해 지나간다</b>.</para>
    /// </summary>
    private void ResumeCurveAfterRoll()
    {
        curveSuspended = false;
        if (curveTemplate == null) return;

        Vector3 toEnemy = Vector3.ProjectOnPlane(curveEnemyPosition - transform.position, Vector3.up);
        curveAxis = DuelGap.ResolveAxis(toEnemy, curveAxis);

        // 구르기는 중심(기습자)을 보게 만든다. 벨 상대는 그쪽이 아니므로 다시 상대를 본다.
        if (toEnemy.sqrMagnitude > 1e-6f) ScheduleTurn(Quaternion.LookRotation(toEnemy));

        // 구르기가 간격을 흐트러뜨렸다 — 커브가 열리기 전에 시작 위치로 되돌린다.
        // 안 되돌리면 클립이 시작하는 프레임에 그 차이만큼 순간이동한다(Step 10과 같은 이음매 규율).
        float startRel = curveTemplate.DuelCurveStartTime;
        Vector3 target = curveEnemyPosition - curveAxis * curveTemplate.DuelGapAt(startRel, curveBaseDistance);
        target.y = transform.position.y;

        ScheduleMove(target, curveImpactTime + startRel - Time.time);
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

        if (t < 1f) return;

        rolling = false;
        if (curveSuspended) ResumeCurveAfterRoll();
    }

    void Update()
    {
        // 미뤄 둔 계획은 커브가 마지막 키를 지나는 즉시 푼다(도착 마감이 먼저 오면 그때 바로).
        if (pendingPlan.HasValue && !ShouldDeferHandover(pendingPlan.Value))
            ApplyPlan(pendingPlan.Value);

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

        if (!moving)
        {
            // 도착 뒤에는 거리 커브가 위치의 주인이다(커브가 없는 패턴이면 아무도 안 건드린다).
            if (curveTemplate != null) TickCurveDrive();
            return;
        }

        if (curveTemplate != null) logBlockedMoving++;

        float m = Mathf.Clamp01((Time.time - moveStart) / (moveEnd - moveStart));
        transform.position = Vector3.Lerp(moveFrom, moveTo, Mathf.SmoothStep(0f, 1f, m));

        if (m >= 1f) moving = false;
    }
}
