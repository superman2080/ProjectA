using System.Collections.Generic;
using EnemySpace;
using PatternSpace;
using UnityEngine;

/// <summary>
/// 패턴이 소유한 <b>월드 이펙트의 유일한 관리 지점</b>. <see cref="PatternHandler"/>와 <see cref="EnemyDirector"/>의
/// 기존 이벤트만 구독하는 순수 연출이라 판정 파이프라인에는 개입하지 않는다
/// (<c>EffectManager</c>·<c>CameraDirector</c>·<c>HitStopDirector</c>와 같은 자리).
///
/// <para><b><c>EffectManager</c>에 얹지 않는 이유</b>: 그쪽은 Canvas 좌표계·<c>UIParticle</c>·<c>EffectTrigger</c> 키가
/// 전제다. 여기는 월드 Transform에 붙고 키가 패턴이다.</para>
///
/// <para><b>예약은 큐 투입 한 번으로 끝난다.</b> 모든 기준 시각이 <see cref="PatternQueuedInfo"/>에 이미 들어 있으므로
/// (<c>NodeTimes</c> 포함) 이벤트를 더 구독하지 않고 예약 목록 하나만 돌린다.
/// <b>조건만 나중에 채워진다</b> — 성패는 마지막 노드에서, 반응은 그 직후에 정해지기 때문이다.</para>
///
/// <para><b><c>HitStopDirector</c>의 "예약 최대 하나"를 쓸 수 없다.</b> 그 근거는 시각이 임팩트로 고정이라는 것인데,
/// 여기 큐는 <c>PatternStart</c>까지 앞당겨질 수 있어 <b>앞 패턴의 임팩트 큐와 다음 패턴의 시작 큐가 겹친다</b>
/// (리드타임 0.5초 &gt; 엔트리 간격 0.4초).</para>
///
/// <para><b>배선이 비면 그 부분만 조용히 빠진다</b> — 기존 규율 그대로다.</para>
/// </summary>
public class PatternEffectDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("실패 반응(패링/회피)을 알려 주는 디렉터. 비우면 결과 조건 큐 중 Parry/Evade만 빠진다.")]
    [SerializeField] private EnemyDirector enemyDirector;

    [Header("Anchors")]
    [Tooltip("칼이 지나가는 월드 지점. SliceTargetDirector의 impactAnchor와 같은 자리를 쓴다.")]
    [SerializeField] private Transform impactAnchor;

    [Tooltip("플레이어 루트.")]
    [SerializeField] private Transform playerRoot;

    [Tooltip("칼날 노드. ⚠ 같은 이름 노드가 2단이라 '안쪽'(메쉬가 달린) 노드를 잡아야 한다\n" +
             "(root/add_weapon_r/Weapon_Katana_01_Blade/Weapon_Katana_01_Blade).")]
    [SerializeField] private Transform playerWeapon;

    [Header("Tuning")]
    [Tooltip("전체 On/Off. 끄면 예약도 잡지 않는다.")]
    [SerializeField] private bool effectsEnabled = true;

    [Tooltip("씬 시작 시 미리 인스턴스를 확보해 둘 패턴들. 곡 도중 Instantiate는 히치(=판정 손실)다.")]
    [SerializeField] private Pattern[] prewarmPatterns;

    /// <summary>패턴 인스턴스 하나의 결과. 예약과 조건이 <b>다른 시점에</b> 정해지므로 따로 든다.</summary>
    private sealed class PatternRun
    {
        public int token;
        public bool hasOutcome;
        public bool success;
        public bool hasReaction;
        public EnemyDirector.EnemyReaction reaction;
        public int pending;      // 아직 발사도 폐기도 안 된 예약 수
    }

    private sealed class Reservation
    {
        public PatternRun run;
        public PatternEffectCue cue;
        public float fireTime;
        public float lastNodeTime;
    }

    /// <summary>따라가며 칼 진행 방향으로 도는 이펙트. <c>alignToBlade</c>를 쓸 때만 존재한다.</summary>
    private sealed class AlignedEffect
    {
        public CanvasEffectView view;
        public Vector3 lastPosition;
        public Vector3 lastForward;
    }

    private readonly List<PatternRun> runs = new List<PatternRun>();
    private readonly List<Reservation> reservations = new List<Reservation>();
    private readonly List<AlignedEffect> aligned = new List<AlignedEffect>();
    private readonly List<CanvasEffectView> active = new List<CanvasEffectView>();

    // 프리팹별 뷰 풀. EffectManager와 같은 구현을 공유한다.
    private CanvasEffectPool pool;
    private int nextToken;

    // 연타 큐 순환. 매 타격 재할당을 피하려고 스크래치 리스트를 하나 든다.
    // ⚠ 커서는 들지 않는다 — 순환 위치를 누적 타수에서 유도해야 클립·리액션과 같은 박자로 돈다.
    private readonly List<PatternEffectCue> mashCueScratch = new List<PatternEffectCue>();

    // 히트스톱 창. 창 안에서 새로 뜨는 이펙트도 얼려야 그놈만 혼자 흐르지 않는다.
    private bool frozen;
    private float freezeUntil;

    private BladePath bladePath;

    void Awake()
    {
        var rootGo = new GameObject("[PatternEffectPool]");
        rootGo.transform.SetParent(transform, false);
        rootGo.SetActive(false);
        // 월드 이펙트는 앵커를 따라가느라 부모가 바뀌므로, 뷰가 돌아올 자리를 스스로 알아야 한다.
        pool = new CanvasEffectPool(rootGo.transform, assignPoolParent: true);

        bladePath = new BladePath(playerWeapon);
    }

    void Start()
    {
        Prewarm();
    }

    void OnEnable()
    {
        if (handler != null)
        {
            handler.OnPatternQueued += HandlePatternQueued;
            handler.OnPatternComplete += HandlePatternComplete;
            handler.OnAllPatternsCleared += HandleAllCleared;
            handler.OnMashHit += HandleMashHit;
        }
        else
        {
            Debug.LogError("[PatternEffectDirector] handler가 배선되지 않았습니다 — 이펙트가 뜨지 않습니다.", this);
        }

        if (enemyDirector != null) enemyDirector.OnEnemyReacted += HandleEnemyReacted;
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnPatternQueued -= HandlePatternQueued;
            handler.OnPatternComplete -= HandlePatternComplete;
            handler.OnAllPatternsCleared -= HandleAllCleared;
            handler.OnMashHit -= HandleMashHit;
        }

        if (enemyDirector != null) enemyDirector.OnEnemyReacted -= HandleEnemyReacted;

        // 잠금 도중 꺼져도 다음 활성화에서 얼어 있지 않도록.
        frozen = false;
    }

    // ── 예약 ────────────────────────────────────────────────────────────────

    private void HandlePatternQueued(PatternQueuedInfo info)
    {
        if (!effectsEnabled || info.Template == null) return;

        var cues = info.Template.EffectCues;
        if (cues == null || cues.Count == 0) return;

        var run = new PatternRun { token = nextToken++ };

        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            if (cue == null || !cue.IsUsable) continue;   // 비면 예약 자체를 안 만든다

            // ⚠ 사건에 붙는 큐(연타 타격)는 예약하지 않는다 — 시각이 없으므로 ResolveTime이
            // 뜻 없는 숫자를 돌려주고, 그대로 두면 타격과 무관한 때에 한 번 더 뜬다.
            if (cue.IsEventDriven) continue;

            float fireTime = cue.ResolveTime(
                info.StartTime, info.FirstNodeTime, info.LastNodeTime, info.Deadline,
                info.NodeTimes, info.Template.ImpactOffset);

            // 재생될 수 없는 조합은 여기서 거른다(툴이 저장 전에 이미 경고한 상태).
            if (!cue.IsTimingValid(fireTime, info.LastNodeTime)) continue;

            reservations.Add(new Reservation
            {
                run = run,
                cue = cue,
                fireTime = fireTime,
                lastNodeTime = info.LastNodeTime
            });
            run.pending++;
        }

        if (run.pending > 0) runs.Add(run);
    }

    /// <summary>성패를 채운다. 패턴 완료는 순차적이므로 <b>결과가 아직 없는 가장 오래된 런</b>이 그 주인이다.</summary>
    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        foreach (var run in runs)
        {
            if (run.hasOutcome) continue;

            run.hasOutcome = true;
            run.success = info.AllCorrect;
            return;
        }
    }

    /// <summary>
    /// 반응을 채운다. <b>완료 이벤트와 도착 순서에 기대지 않는다</b> — 두 칸이 각자 채워지고,
    /// 성공으로 확정된 런은 반응을 받지 않으므로 건너뛴다.
    /// </summary>
    private void HandleEnemyReacted(Pattern template, EnemyDirector.EnemyReaction reaction, float impactTime)
    {
        foreach (var run in runs)
        {
            if (run.hasReaction) continue;
            if (run.hasOutcome && run.success) continue;   // 성공엔 반응이 없다

            run.hasReaction = true;
            run.reaction = reaction;
            return;
        }
    }

    /// <summary>
    /// 연타 타격 하나 — <b>예약하지 않고 그 자리에서 발사한다</b>.
    ///
    /// <para><b>예약할 수 없는 것과 발사할 수 없는 것은 다르다.</b> <see cref="Fire"/>는 큐 하나만 있으면
    /// 되는 독립 메서드다(앵커 조회·풀 대여·포즈·배속·소리·정지 동결이 전부 그 안에 있다) —
    /// 시각이 없다는 것은 <c>ResolveTime</c>을 못 쓴다는 뜻일 뿐이다.</para>
    ///
    /// <para><b>공짜로 따라오는 것들</b>: <c>EffectAnchor.Opponent</c>가 원래 <b>발사 순간에</b> 조회되므로
    /// 현재 상대에 정확히 붙고, <c>PlayerWeapon</c> + <c>bladeT</c>가 휘두르는 칼날을 따라가며,
    /// <c>Sfx</c>가 실려 있으면 타격음도 같이 난다. <see cref="Prewarm"/>도 <c>Timing</c>을 안 보므로
    /// 풀 예열이 이미 돼 있다.</para>
    ///
    /// <para><b>⚠ <c>info.Scored</c>를 보지 않는다</b> — 초과 타격에도 이펙트가 뜬다(적 반응과 같은 판단).
    /// <b>⚠ 조건은 <c>Always</c>만 유효하다</b> — 타격 순간에는 성패가 아직 안 정해졌다.</para>
    ///
    /// <para><b>⚠ 풀 크기가 이 경로의 유일한 실무 함정이다.</b> 초당 8타 × 이펙트 수명이 동시 인스턴스 수라,
    /// <c>poolSize</c>가 모자라면 곡 도중 <c>Instantiate</c>가 나고 그 히치가 그대로 판정 손실이다.</para>
    /// </summary>
    private void HandleMashHit(MashHitInfo info)
    {
        if (!effectsEnabled || info.Template == null) return;

        var cues = info.Template.EffectCues;
        if (cues == null) return;

        // ⚠ 연타 큐는 '전부'가 아니라 '하나씩 번갈아' 돈다 — mashHitClips와 같은 규율이다.
        //
        // 전부 쏘면 같은 순간에 같은 소리가 겹쳐 한 덩어리로 들리고, "타격마다 다른 소리"를
        // 데이터로 표현할 방법이 사라진다(큐 하나에 소리도 하나다). 번갈아 돌면 큐가 하나뿐일 때는
        // 매 타격 그 하나가 나가므로 '전부 쏘기'와 결과가 같고, 둘 이상일 때만 갈린다.
        //
        // 같은 그림을 매 타격 보여 주고 소리만 바꾸고 싶다면 큐 둘에 같은 프리팹을 넣으면 된다.
        mashCueScratch.Clear();
        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            if (cue == null || !cue.IsUsable) continue;
            if (!cue.IsEventDriven || cue.NeedsOutcome) continue;

            mashCueScratch.Add(cue);
        }

        if (mashCueScratch.Count == 0) return;

        // 순환 위치를 누적 타수에서 유도한다 — 커서를 따로 들면 클립·리액션과 박자가 어긋나
        // "1타에 07 소리인데 모션은 3번" 같은 상태가 조용히 만들어진다(Pattern.MashStrikeFor와 같은 규율).
        int index = (Mathf.Max(info.Hits, 1) - 1) % mashCueScratch.Count;
        Fire(mashCueScratch[index]);
    }

    private void HandleAllCleared()
    {
        reservations.Clear();
        runs.Clear();

        for (int i = active.Count - 1; i >= 0; i--)
            ReturnView(active[i]);
    }

    // ── 루프 ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (frozen && Time.time >= freezeUntil) Unfreeze();

        float now = Time.time;

        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            var r = reservations[i];
            if (now < r.fireTime) continue;

            reservations.RemoveAt(i);
            r.run.pending--;

            if (ShouldFire(r)) Fire(r.cue);
        }

        // 다 쓴 런은 버린다 — 안 그러면 결과 칸을 찾는 루프가 죽은 런에 걸린다.
        for (int i = runs.Count - 1; i >= 0; i--)
            if (runs[i].pending <= 0) runs.RemoveAt(i);
    }

    void LateUpdate()
    {
        if (aligned.Count == 0) return;

        for (int i = aligned.Count - 1; i >= 0; i--)
        {
            var a = aligned[i];
            if (a.view == null || !a.view.gameObject.activeInHierarchy)
            {
                aligned.RemoveAt(i);
                continue;
            }

            Vector3 position = a.view.transform.position;
            Vector3 travel = position - a.lastPosition;
            a.lastPosition = position;

            // 정지 프레임에는 직전 방향을 유지한다(칼이 멈춘 순간 이펙트가 홱 도는 것을 막는다).
            if (travel.sqrMagnitude > 1e-8f) a.lastForward = travel.normalized;

            Vector3 bladeAxis = bladePath.WorldAxis();
            if (a.lastForward.sqrMagnitude > 1e-8f && bladeAxis.sqrMagnitude > 1e-8f)
                a.view.transform.rotation = Quaternion.LookRotation(a.lastForward, bladeAxis);
        }
    }

    /// <summary>
    /// 이 예약이 지금 뜰 자격이 있는가. <b>결과가 아직 없으면 폐기한다</b> —
    /// 성패는 마지막 노드에서 정해지고 결과 조건 큐는 그 뒤에만 예약되므로, 여기서 비었다는 것은
    /// 알려 줄 사람이 배선되지 않았다는 뜻이다.
    /// </summary>
    private bool ShouldFire(Reservation r)
    {
        var cue = r.cue;
        if (!cue.NeedsOutcome) return true;
        if (!r.run.hasOutcome) return false;

        switch (cue.Condition)
        {
            case EffectCondition.Success:
                return r.run.success;

            case EffectCondition.Parry:
                return !r.run.success && r.run.hasReaction && r.run.reaction == EnemyDirector.EnemyReaction.Parry;

            case EffectCondition.Evade:
                return !r.run.success && r.run.hasReaction && r.run.reaction == EnemyDirector.EnemyReaction.Evade;

            default:
                return true;
        }
    }

    // ── 발사 ────────────────────────────────────────────────────────────────

    private void Fire(PatternEffectCue cue)
    {
        // ⚠ 소리가 먼저다. 소리는 2D라 앵커가 필요 없는데, 앵커 가드를 앞에 두면
        // 소리 전용 큐(또는 앵커 배선이 빈 경우)에서 소리까지 같이 죽는다.
        //
        // ⚠ 히트스톱은 소리를 얼리지 않는다. 오디오는 원래 timeScale의 지배를 안 받고(§7-3),
        // 타격감으로도 '멈추는 그 순간'에 울리는 것이 맞다 — 재생 중 AudioSource를 멈추면
        // "정지"가 아니라 "소리가 끊겼다"로 읽힌다(카메라를 즉시 얼리는 규율과 같은 결).
        if (cue.Sfx != null) SfxManager.Instance.Play(cue.Sfx, cue.SfxVolume, cue.SfxPitch);

        if (cue.Prefab == null) return;   // 소리 전용 큐는 여기서 끝

        Transform anchor = ResolveAnchor(cue.Anchor);
        if (anchor == null) return;   // 배선이 비면 그림만 조용히 빠진다

        var view = pool.Rent(cue.Prefab, cue.PoolSize);
        if (view == null) return;

        Vector3 localPosition = cue.PositionOffset;
        if (cue.UsesBlade && anchor == playerWeapon)
            localPosition += bladePath.LocalPoint(cue.BladeT);

        view.SetWorldPose(anchor, cue.Follow, localPosition, cue.RotationOffset, cue.Scale);
        view.SetSpeed(cue.Speed);
        if (frozen) view.OverrideSpeed(0f);   // 창 안에서 태어난 것도 같이 언다

        view.OnFinished += ReturnView;
        view.OnSpawn();
        active.Add(view);

        if (cue.AlignToBlade && cue.UsesBlade)
        {
            aligned.Add(new AlignedEffect
            {
                view = view,
                lastPosition = view.transform.position,
                lastForward = view.transform.forward
            });
        }
    }

    private Transform ResolveAnchor(EffectAnchor anchor)
    {
        switch (anchor)
        {
            case EffectAnchor.Player: return playerRoot;
            case EffectAnchor.PlayerWeapon: return playerWeapon;
            case EffectAnchor.Opponent:
                var opponent = enemyDirector != null ? enemyDirector.CurrentOpponent : null;
                return opponent != null ? opponent.transform : null;
            default: return impactAnchor;
        }
    }

    // ── 히트스톱 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 정지 창 동안 파티클을 얼린다. <b><c>Time.timeScale</c>은 쓰지 않는다</b> —
    /// 멈추는 것은 파티클 시스템의 자기 배속뿐이라 판정·오디오·포커스 링은 그대로 흐른다
    /// (Animator Speed Multiplier만 멈추는 <see cref="HitStopDirector"/>의 모델과 같은 성질).
    ///
    /// <para>복귀값은 <b>뷰가 안다</b> — 큐마다 배속이 다르므로 하나의 값으로 되돌리면 안 된다.</para>
    /// </summary>
    public void ApplyHitStop(float duration)
    {
        if (duration <= 0f) return;

        frozen = true;
        freezeUntil = Mathf.Max(freezeUntil, Time.time + duration);

        foreach (var view in active)
            if (view != null) view.OverrideSpeed(0f);
    }

    private void Unfreeze()
    {
        frozen = false;

        foreach (var view in active)
            if (view != null) view.RestoreSpeed();
    }

    // ── 프리팹별 풀 ──────────────────────────────────────────────────────────

    private void Prewarm()
    {
        if (prewarmPatterns == null) return;

        foreach (var pattern in prewarmPatterns)
        {
            if (pattern == null || pattern.EffectCues == null) continue;

            foreach (var cue in pattern.EffectCues)
            {
                if (cue == null || !cue.IsUsable) continue;

                // ⚠ IsUsable이 '소리만 있어도 true'로 넓어졌으므로 여기 null이 도달할 수 있다.
                // 소리는 풀이 필요 없다 — SfxManager의 보이스 풀이 동시 재생을 감당한다.
                if (cue.Prefab == null) continue;

                pool.Prewarm(cue.Prefab, cue.PoolSize, cue.PoolSize);
            }
        }
    }

    /// <summary>뷰를 회수한다. <b>활성 목록에서 먼저 뺀다</b> — 히트스톱이 그 목록을 훑기 때문.</summary>
    private void ReturnView(CanvasEffectView view)
    {
        if (view == null) return;

        active.Remove(view);
        pool.Return(view);
    }

}
