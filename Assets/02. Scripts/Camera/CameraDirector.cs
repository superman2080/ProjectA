using System.Collections;
using System.Collections.Generic;
using PatternSpace;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 연출의 <b>유일한 관리 지점</b>. <see cref="PatternHandler"/>의 기존 확장 이벤트만 구독해
/// 카탈로그에서 큐를 골라 재생한다. 판정 파이프라인에는 개입하지 않는다(순수 연출) —
/// <c>EffectManager</c>·<c>SliceTargetDirector</c>와 같은 위치다.
///
/// <para><b>큐는 셋이고, 각각 화면에서 실제로 사건이 일어나는 순간에 맞춘다.</b>
/// 성공/실패(표적 파괴)는 <b>Deadline</b> — 칼날 임팩트 프레임·표적 절단과 같은 식이다.
/// 반면 피격은 <see cref="PatternHandler.OnJudgeTargetFirstMiss"/> 순간에 <b>즉시</b>다
/// (<c>CharacterActionPlayer</c>가 그때 Hit 클립을 바로 재생하므로). 실패는 사건이 둘이라 큐도 둘이다.</para>
///
/// <para><b>예약은 하나면 충분하다.</b> 패턴 완료는 순차적이고, A의 Deadline은 A 마지막 노드 +goodWindow(0.1초)인데
/// B의 완료는 A보다 최소 0.4초 뒤다 → 동시에 대기 중인 예약은 최대 하나다. 리스트를 두지 않는다.</para>
///
/// <para><b>겹침은 합성되지 않는다.</b> Perlin은 채널이 하나뿐이다. 그리고 마지막 노드에서 미스가 나면
/// <see cref="CameraTrigger.PatternMiss"/>와 <see cref="CameraTrigger.PatternFailure"/>가 0.1초 간격으로 확실히 붙는다.
/// 그래서 새 쉐이크는 타이머를 재시작하되 <b>진폭은 큰 쪽을 취한다</b> — 단순 덮어쓰기면 강한 쉐이크 도중
/// 약한 쉐이크가 들어와 세기가 뚝 떨어진다.</para>
///
/// <para><b>휴지값은 0이 아니다.</b> 씬의 Perlin에 상시 흔들림 값이 들어 있을 수 있어
/// <see cref="Awake"/>에서 현재 값을 캐시해 그리로 복귀한다.</para>
///
/// <para>─────────────────────────────────────────────────────────</para>
///
/// <para>이 클래스가 하는 일은 <b>셋</b>이고, 서로 다른 층에 산다 —
/// <b>쉐이크</b>(노이즈 채널), <b>프레이밍</b>(무엇을 담을지), <b>인트로</b>(어느 vcam을 쓸지).
/// 채널이 겹치지 않아 동시에 돌아도 간섭하지 않는다.</para>
///
/// <para><b>Cinemachine 타입은 세 이음매에만 등장한다</b> — <see cref="ApplyShake"/>,
/// <see cref="ApplyFraming"/>, <see cref="IntroRoutine"/>. 위층(트리거·카탈로그·타이밍·거리 계산)은
/// Cinemachine을 모르므로, 나중에 쉐이크를 Impulse로 갈아끼우거나 프레이밍 방식을 바꿔도 그대로 남는다.</para>
///
/// <para><b>세 기능은 각자 독립적으로 꺼진다.</b> 배선이 빠진 기능만 조용히 비활성되고 나머지는 동작한다 —
/// 새 기능이 기존 씬을 깨지 않게 하는 규율이다.</para>
/// </summary>
public class CameraDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("피격 큐의 시각원. 칼이 실제로 닿는 순간을 알려준다(첫 미스 순간이 아니다).")]
    [SerializeField] private CharacterActionPlayer actionPlayer;

    [Tooltip("적 처치 큐. 비우면 그 트리거만 무연출.")]
    [SerializeField] private EnemySpace.EnemyDirector enemyDirector;

    [Tooltip("흔들 대상. 씬의 CinemachineCamera에 붙어 있는 노이즈 컴포넌트.")]
    [SerializeField] private CinemachineBasicMultiChannelPerlin perlin;

    [Header("Cue Catalog")]
    [Tooltip("트리거별 쉐이크 설정. 연출 추가 = 여기에 한 줄.")]
    [SerializeField] private List<CameraCueEntry> catalog = new List<CameraCueEntry>();

    [Tooltip("모든 큐가 공유하는 흔들림 주파수. 0.2초 남짓 쉐이크에서는 큐별로 나눌 만한 차이가 나지 않는다.")]
    [SerializeField] private float shakeFrequency = 1.6f;

    [Header("Framing")]
    [Tooltip("게임플레이 vcam이 추적하는 타겟 그룹. 비우면 프레이밍 기능만 꺼진다.")]
    [SerializeField] private CinemachineTargetGroup targetGroup;

    [Tooltip("이 거리 이하면 적을 완전히 담는다(가중치 1).")]
    [SerializeField] private float fullFrameDistance = 3f;

    [Tooltip("이 거리 이상이면 적을 담지 않는다(가중치 0) — 화면에는 플레이어만 남는다.")]
    [SerializeField] private float dropoffDistance = 6f;

    [Tooltip("가중치가 목표를 따라가는 시간상수(초). 거리 계산이 튀어도 구도는 부드럽게 따라온다.")]
    [SerializeField] private float weightDamping = 0.35f;

    [Header("Intro")]
    [Tooltip("카운트다운 시각원. 비우면 인트로 기능만 꺼진다.")]
    [SerializeField] private ChartGen.ChartPlayer chartPlayer;

    [Tooltip("인트로용 vcam. 스플라인 위를 달리며 플레이어를 본다.")]
    [SerializeField] private CinemachineCamera introCamera;

    [Tooltip("인트로 vcam의 스플라인 주행 컴포넌트. PositionUnits는 Normalized여야 한다.")]
    [SerializeField] private CinemachineSplineDolly introDolly;

    [Tooltip("Main Camera의 Brain. 마무리 블렌드 시간의 진실의 원천이다.")]
    [SerializeField] private CinemachineBrain brain;

    [Tooltip("인트로 동안 vcam에 줄 우선순위. 게임플레이 vcam보다 높아야 한다.")]
    [SerializeField] private int introPriority = 20;

    [Tooltip("스플라인 진행도의 시간 배분. 경로 '모양'은 씬의 스플라인이, '속도'는 이 곡선이 정한다.")]
    [SerializeField]
    private AnimationCurve introEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private readonly Dictionary<CameraTrigger, CameraCueEntry> catalogByTrigger =
        new Dictionary<CameraTrigger, CameraCueEntry>();

    // Perlin의 휴지값(씬에 설정된 상시 흔들림). 쉐이크가 끝나면 0이 아니라 여기로 되돌아간다.
    private float idleAmplitude;
    private float idleFrequency;

    // 진행 중인 쉐이크.
    private float shakeAmplitude;
    private float shakeStartTime;
    private float shakeDuration;

    // 대기 중인 예약(최대 하나). duration이 0 이하면 예약 없음.
    private bool hasPending;
    private float pendingFireTime;
    private CameraTrigger pendingTrigger;

    // 프레이밍 상태. framingEnabled가 false면 targetGroup을 건드리지 않는다.
    private bool framingEnabled;
    private Transform opponentTransform;
    private float opponentWeight;

    // 인트로 상태.
    private Coroutine introRoutine;
    private int introRestingPriority;

    void Awake()
    {
        if (handler == null)
            Debug.LogError("[CameraDirector] handler가 배선되지 않았습니다 — 카메라 연출이 동작하지 않습니다.", this);

        if (perlin == null)
            Debug.LogError("[CameraDirector] perlin이 배선되지 않았습니다 — CinemachineCamera의 노이즈 컴포넌트를 넣으세요.", this);
        else
        {
            idleAmplitude = perlin.AmplitudeGain;
            idleFrequency = perlin.FrequencyGain;
        }

        foreach (var entry in catalog)
            catalogByTrigger[entry.trigger] = entry;

        SetupFraming();
        SetupIntro();
    }

    /// <summary>
    /// 타겟 그룹을 <b>고정 2칸</b>으로 세운다 — 0번 플레이어(가중치 1), 1번 교전 상대(가중치 0에서 시작).
    ///
    /// <para>멤버를 넣었다 뺐다 하지 않는 이유: 바운드가 계단식으로 튀어 구도가 팝한다.
    /// 가중치만 움직이면 바운드가 연속적으로 변해 카메라가 부드럽게 물러나고 붙는다.
    /// 가중치 0인 멤버는 바운드 계산에서 제외되므로 "적 없음"이 정확히 표현된다.</para>
    /// </summary>
    private void SetupFraming()
    {
        if (targetGroup == null) return; // 프레이밍만 비활성 — 쉐이크·인트로는 그대로 동작한다.

        if (actionPlayer == null)
        {
            Debug.LogError("[CameraDirector] actionPlayer가 없어 프레이밍을 비활성화합니다 — 그룹의 기준이 되는 플레이어를 알 수 없습니다.", this);
            return;
        }

        if (dropoffDistance <= fullFrameDistance)
        {
            Debug.LogError($"[CameraDirector] dropoffDistance({dropoffDistance})가 fullFrameDistance({fullFrameDistance})보다 커야 합니다. " +
                           "프레이밍을 비활성화합니다.", this);
            return;
        }

        targetGroup.Targets.Clear();
        targetGroup.AddMember(actionPlayer.transform, 1f, 1f);
        targetGroup.AddMember(null, 0f, 1f); // 1번 칸은 자리만 잡아 둔다. 대상은 승격될 때 채운다.

        opponentWeight = 0f;
        framingEnabled = true;
    }

    private void SetupIntro()
    {
        if (chartPlayer == null || introCamera == null || introDolly == null || brain == null) return;

        // 씬에 적힌 값을 그대로 휴지값으로 삼는다. 게임플레이 vcam(우선순위 0)보다 낮게 두어야
        // 인트로가 끝난 뒤 동점으로 남지 않는다 — 동점이면 어느 쪽이 이길지 활성화 순서에 달린다.
        introRestingPriority = introCamera.Priority.Value;
        if (introRestingPriority >= introPriority)
            Debug.LogWarning($"[CameraDirector] introCamera의 휴지 우선순위({introRestingPriority})가 " +
                             $"인트로 우선순위({introPriority}) 이상입니다 — 인트로가 끝나도 내려오지 않습니다.", this);

        // Normalized가 아니면 0→1이 '전체 경로'를 뜻하지 않는다 — 경로가 조용히 일부만 재생된다.
        if (introDolly.PositionUnits != UnityEngine.Splines.PathIndexUnit.Normalized)
            Debug.LogWarning("[CameraDirector] introDolly.PositionUnits가 Normalized가 아닙니다 — " +
                             "인트로 경로가 일부만 재생됩니다.", this);
    }

    void OnEnable()
    {
        if (actionPlayer != null) actionPlayer.OnPlayerHit += HandlePlayerHit;

        if (enemyDirector != null)
        {
            enemyDirector.OnEnemyKilled += HandleEnemyKilled;
            enemyDirector.OnOpponentChanged += HandleOpponentChanged;

            // 구독보다 먼저 승격이 끝났을 수 있다 — 이벤트만으로는 지금 상태를 알 수 없어 한 번 읽는다.
            SetOpponent(enemyDirector.CurrentOpponent);
        }

        if (chartPlayer != null) chartPlayer.OnCountdownStarted += HandleCountdownStarted;

        if (handler == null) return;
        handler.OnPatternComplete += HandlePatternComplete;
        handler.OnAllPatternsCleared += HandleAllCleared;
    }

    void OnDisable()
    {
        if (actionPlayer != null) actionPlayer.OnPlayerHit -= HandlePlayerHit;

        if (enemyDirector != null)
        {
            enemyDirector.OnEnemyKilled -= HandleEnemyKilled;
            enemyDirector.OnOpponentChanged -= HandleOpponentChanged;
        }

        if (chartPlayer != null) chartPlayer.OnCountdownStarted -= HandleCountdownStarted;

        if (handler != null)
        {
            handler.OnPatternComplete -= HandlePatternComplete;
            handler.OnAllPatternsCleared -= HandleAllCleared;
        }

        StopShake(); // 꺼진 채 흔들림이 남지 않도록.
        StopIntro(); // 인트로 도중 꺼져도 vcam이 높은 우선순위로 남지 않도록.
    }

    // ── 이벤트 처리 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 성공/실패 큐를 <b>Deadline에 맞춰 예약</b>한다. 표적이 갈라지거나 부딪히는 바로 그 순간이다.
    /// 완주 성공은 Deadline 이전에, 만료 실패는 Deadline 직후에 이 이벤트가 오므로
    /// "시각이 이미 지났으면 즉시 발사"는 예외가 아니라 실패 경로의 정상 동작이다.
    /// </summary>
    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        var trigger = info.AllCorrect ? CameraTrigger.PatternSuccess : CameraTrigger.PatternFailure;

        float offset = info.Pattern != null ? info.Pattern.ImpactOffset : 0f;
        float fireTime = info.LastNodeTime + handler.GoodWindow + offset;

        if (Time.time >= fireTime)
        {
            PlayCue(trigger);
            return;
        }

        hasPending = true;
        pendingFireTime = fireTime;
        pendingTrigger = trigger;
    }

    /// <summary>
    /// 피격 큐는 <b>칼이 실제로 닿는 순간</b>에 터진다. 예전에는 첫 미스 순간에 즉시 냈는데,
    /// 그건 적 칼이 도착하기 전이라 화면 흔들림이 피격보다 먼저 왔다.
    /// <see cref="CharacterActionPlayer.OnPlayerHit"/>이 그 시각에 정확히 발행되므로 예약이 필요 없다.
    /// </summary>
    private void HandlePlayerHit() => PlayCue(CameraTrigger.PatternMiss);

    /// <summary>적 처치 — 히트스톱을 못 쓰는 대신 타격감을 내는 주 큐다.</summary>
    private void HandleEnemyKilled(EnemySpace.EnemyView view) => PlayCue(CameraTrigger.EnemyKilled);

    private void HandleAllCleared()
    {
        hasPending = false;
        StopShake();
    }

    private void HandleOpponentChanged(EnemySpace.EnemyView previous, EnemySpace.EnemyView current) => SetOpponent(current);

    /// <summary>
    /// 슬롯 1의 대상을 갈아끼운다. <b>가중치를 같은 순간에 0으로 떨어뜨린다.</b>
    ///
    /// <para>가중치는 연속적으로 움직이지만 <b>대상 위치는 순간이동한다</b> — 처치 순간 상대가
    /// 결투 위치(≈1m)의 적에서 링(≈6m)의 다음 적으로 바뀐다. 가중치를 이월하면
    /// 그 0.35초 동안 그룹이 <b>6m 밖 한 점을 무겁게 껴안아</b> 바운드가 부풀고, 카메라가 바깥으로 튄다.</para>
    ///
    /// <para>0에서 다시 시작하면 새 상대는 <see cref="dropoffDistance"/> 안으로 들어오는 만큼만
    /// 화면에 자리를 얻는다. 대상이 튄 그 프레임에는 가중치가 0이라 <b>바운드 계산에서 아예 빠진다</b> —
    /// 이게 D-1이 "멤버를 넣었다 뺐다 하지 않는다"로 노렸던 연속성이고, 대상 교체에도 같은 규칙을 적용하는 것이다.</para>
    ///
    /// <para>죽은 적이 풀로 반납되는 것(<c>SwapToCorpse</c>)은 임팩트 시각이라 <b>이보다 늦다</b>.
    /// 그때는 이미 대상이 다음 적을 가리키고 있어 문제가 되지 않는다.</para>
    /// </summary>
    private void SetOpponent(EnemySpace.EnemyView view)
    {
        var next = view != null ? view.transform : null;
        if (next == opponentTransform) return;

        opponentTransform = next;
        opponentWeight = 0f;
    }

    private void HandleCountdownStarted(float duration)
    {
        if (chartPlayer == null || introCamera == null || introDolly == null || brain == null) return;

        StopIntro();
        introRoutine = StartCoroutine(IntroRoutine(duration));
    }

    // ── 루프 ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (hasPending && Time.time >= pendingFireTime)
        {
            hasPending = false;
            PlayCue(pendingTrigger);
        }

        UpdateShake();
        UpdateFraming();
    }

    // ── 프레이밍 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 교전 상대를 구도에 <b>얼마나</b> 담을지 매 프레임 정한다.
    ///
    /// <para><b>"적이 있다/없다"를 이진값으로 두지 않는다.</b> 처치 즉시 다음 상대가 승격되는데
    /// 그 적은 아직 링(6m) 위에 있어서, 이진값이면 카메라가 확 물러났다가 적이 달려오며 다시 붙는다.
    /// 거리의 함수로 두면 멀리서 오는 적이 화면에 서서히 자리를 만들고, 링에 흩어진 배경 적들은
    /// 애초에 구도에 개입하지 않는다. 요구("있으면 함께, 없으면 플레이어만")를 만족하면서
    /// 승격 구간 문제를 같은 식 하나로 흡수한다.</para>
    /// </summary>
    private void UpdateFraming()
    {
        if (!framingEnabled) return;

        float target = 0f;
        if (opponentTransform != null && opponentTransform.gameObject.activeInHierarchy)
        {
            // 높이는 무시한다 — 구도 판단은 평면 거리의 문제다.
            float distance = Vector3.ProjectOnPlane(
                opponentTransform.position - actionPlayer.transform.position, Vector3.up).magnitude;

            target = 1f - Mathf.Clamp01((distance - fullFrameDistance) / (dropoffDistance - fullFrameDistance));
        }

        opponentWeight = weightDamping > 0f
            ? Mathf.Lerp(opponentWeight, target, 1f - Mathf.Exp(-Time.deltaTime / weightDamping))
            : target;

        ApplyFraming(opponentTransform, opponentWeight);
    }

    /// <summary>
    /// 그룹 멤버를 실제로 갱신하는 <b>유일한 지점</b>. <see cref="ApplyShake"/>와 같은 규율이라
    /// 프레이밍 방식을 바꿔도 위층(거리 계산·감쇠)은 그대로 남는다.
    /// </summary>
    private void ApplyFraming(Transform opponent, float weight)
    {
        if (targetGroup == null || targetGroup.Targets.Count < 2) return;

        var slot = targetGroup.Targets[1];
        slot.Object = opponent;
        slot.Weight = weight;
    }

    private void UpdateShake()
    {
        if (shakeDuration <= 0f) return;

        float t = (Time.time - shakeStartTime) / shakeDuration;
        if (t >= 1f)
        {
            StopShake();
            return;
        }

        ApplyShake(shakeAmplitude * Decay(t), shakeFrequency * Decay(t));
    }

    /// <summary>1 → 0 감쇠. 제곱이라 끝에서 부드럽게 잦아든다.</summary>
    private static float Decay(float t)
    {
        float inv = 1f - t;
        return inv * inv;
    }

    // ── 큐 재생 ──────────────────────────────────────────────────────────────

    private void PlayCue(CameraTrigger trigger)
    {
        if (!catalogByTrigger.TryGetValue(trigger, out var cue)) return; // 카탈로그에 없으면 무연출
        if (cue.shakeDuration <= 0f) return;

        // 겹침: 진행 중인 쉐이크의 남은 진폭과 비교해 큰 쪽을 취한다(세기 낙차 방지).
        float remaining = 0f;
        if (shakeDuration > 0f)
        {
            float t = Mathf.Clamp01((Time.time - shakeStartTime) / shakeDuration);
            remaining = shakeAmplitude * Decay(t);
        }

        shakeAmplitude = Mathf.Max(remaining, cue.shakeAmplitude);
        shakeDuration = cue.shakeDuration;
        shakeStartTime = Time.time;
    }

    private void StopShake()
    {
        shakeDuration = 0f;
        shakeAmplitude = 0f;
        ApplyShake(0f, 0f);
    }

    // ── 인트로 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 곡 시작 전 인트로. <b>두 도막</b>이다 — ① 씬의 스플라인을 달리고, ② Brain이 게임플레이 구도로 블렌드한다.
    ///
    /// <para><b>경로는 코드가 모른다.</b> 좌표·높이·곡률·시작 각도는 전부 씬의 <c>SplineContainer</c>가 갖고 있고,
    /// 여기서 미는 것은 진행도 0→1 하나뿐이다(<c>PositionUnits = Normalized</c> 전제).
    /// 그래서 경로를 다시 그려도 이 코드는 바뀌지 않는다. 코드가 주는 것은 <b>속도 배분</b>(<see cref="introEase"/>)뿐이다 —
    /// 모양과 시간 배분은 층이 다르다.</para>
    ///
    /// <para><b>스플라인 끝점을 게임플레이 구도에 정확히 맞출 의무가 없다.</b> 남은 차이는 ②의 블렌드가 흡수한다.</para>
    ///
    /// <para>블렌드 시간은 <see cref="CinemachineBrain.DefaultBlend"/>에서 <b>읽는다</b>. 인스펙터에 같은 값을 두 번 적으면
    /// 언젠가 한쪽만 고쳐져 "곡은 시작됐는데 카메라가 아직 움직이는" 상태가 된다. 진실의 원천은 Brain 하나다.</para>
    /// </summary>
    private IEnumerator IntroRoutine(float duration)
    {
        introCamera.Priority = new PrioritySettings { Enabled = true, Value = introPriority };
        introDolly.CameraPosition = 0f;

        // 곡 시작을 늦출 수는 없으므로, 블렌드가 창을 다 먹으면 인트로가 잘리는 쪽을 택한다.
        float travel = duration - brain.DefaultBlend.BlendTime;
        if (travel <= 0f)
        {
            Debug.LogWarning($"[CameraDirector] 블렌드 시간({brain.DefaultBlend.BlendTime}초)이 카운트다운({duration}초)을 " +
                             "다 써서 스플라인 주행을 생략합니다. Brain의 DefaultBlend를 줄이세요.", this);
        }
        else
        {
            for (float elapsed = 0f; elapsed < travel; elapsed += Time.deltaTime)
            {
                introDolly.CameraPosition = introEase.Evaluate(elapsed / travel);
                yield return null;
            }
        }

        introDolly.CameraPosition = 1f; // 마지막 프레임이 1에 못 미쳐도 끝점에서 출발하게 확정한다.
        introRoutine = null;
        RestoreIntroPriority();
    }

    private void StopIntro()
    {
        if (introRoutine != null)
        {
            StopCoroutine(introRoutine);
            introRoutine = null;
        }

        RestoreIntroPriority();
    }

    private void RestoreIntroPriority()
    {
        if (introCamera != null)
            introCamera.Priority = new PrioritySettings { Enabled = true, Value = introRestingPriority };
    }

    /// <summary>
    /// 쉐이크를 실제로 카메라에 적용하는 <b>유일한 지점</b>. <see cref="ApplyFraming"/>·<see cref="IntroRoutine"/>과 함께
    /// Cinemachine 타입이 등장하는 세 곳 중 하나라, 나중에 Impulse로 갈아끼울 때 위층(트리거·카탈로그·타이밍)은 그대로 둘 수 있다.
    /// </summary>
    private void ApplyShake(float amplitude, float frequency)
    {
        if (perlin == null) return;

        perlin.AmplitudeGain = idleAmplitude + amplitude;
        perlin.FrequencyGain = idleFrequency + frequency;
    }
}
