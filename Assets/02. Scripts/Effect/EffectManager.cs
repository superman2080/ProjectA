using System.Collections.Generic;
using PatternSpace;
using UnityEngine;

/// <summary>
/// Canvas 이펙트의 유일한 관리 지점. <see cref="PatternHandler"/>의 기존 확장 이벤트만 구독해
/// (판정/라인연결/패턴완성) 카탈로그에서 프리팹을 골라 좌표를 잡고 재생한다. 풀링은 프리팹별 자체 큐로 관리한다.
/// 배경 앰비언트는 상시 루프 인스턴스로 배치하고 <see cref="SetIntensity"/>로 강도를 조절한다.
///
/// PatternHandler는 이펙트를 위해 수정하지 않는다(관심사 분리). 프리팹이 지정되지 않은 트리거는 무연출.
/// </summary>
public class EffectManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;
    [Tooltip("전투 연출 트리거(적 처치 등). 비우면 그 트리거만 무연출.")]
    [SerializeField] private EnemySpace.EnemyDirector enemyDirector;
    [Tooltip("판정/라인연결 이펙트가 재생되는 전경 컨테이너(패턴인풋 앞).")]
    [SerializeField] private RectTransform overlayLayer;
    [Tooltip("배경 앰비언트가 배치되는 후경 컨테이너(패턴인풋 뒤).")]
    [SerializeField] private RectTransform ambientLayer;

    [Header("One-shot Effect Catalog")]
    [SerializeField] private List<EffectEntry> catalog = new List<EffectEntry>();
    [Tooltip("패턴 완성 이펙트가 재생될 위치 기준(월드). 비우면 overlayLayer 중심.")]
    [SerializeField] private RectTransform patternCompleteAnchor;

    [Header("Ambient")]
    [Tooltip("씬 시작 시 ambientLayer에 상시 재생으로 배치할 앰비언트 프리팹들(각각 AmbientEffectController 보유).")]
    [SerializeField] private List<GameObject> ambientPrefabs = new List<GameObject>();

    // 트리거 → 엔트리 조회
    private readonly Dictionary<EffectTrigger, EffectEntry> catalogByTrigger = new Dictionary<EffectTrigger, EffectEntry>();
    // 프리팹별 뷰 풀. PatternEffectDirector와 같은 구현을 공유한다.
    private CanvasEffectPool pool;

    private readonly List<AmbientEffectController> ambients = new List<AmbientEffectController>();

    private Canvas canvas;
    private Camera canvasCamera;

    void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        canvasCamera = canvas != null ? canvas.worldCamera : null;

        pool = new CanvasEffectPool(overlayLayer);
        BuildPools();
        SpawnAmbients();
    }

    void OnEnable()
    {
        if (handler != null)
        {
            handler.OnFocusRingResolved += HandleFocusRingResolved;
            handler.OnNodeConnected += HandleNodeConnected;
            handler.OnPatternComplete += HandlePatternComplete;
        }

        if (enemyDirector != null) enemyDirector.OnEnemyKilled += HandleEnemyKilled;
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnFocusRingResolved -= HandleFocusRingResolved;
            handler.OnNodeConnected -= HandleNodeConnected;
            handler.OnPatternComplete -= HandlePatternComplete;
        }

        if (enemyDirector != null) enemyDirector.OnEnemyKilled -= HandleEnemyKilled;

        // 곡 중단·비활성으로 여기 들어오면 예약이 남는다 — 다음에 켜질 때 뜬금없이 울린다(잔존물 규율).
        pendingImpactSfx = null;
    }

    /// <summary>적이 갈라지는 자리에 처치 이펙트. 카탈로그가 비면 무연출이라 배선 없이도 안전하다.</summary>
    private void HandleEnemyKilled(EnemySpace.EnemyView view)
    {
        if (view == null) return;
        Play(EffectTrigger.EnemyKilled, view.transform.position);
    }

    // ─────────────────────────── 풀 구성 ───────────────────────────

    private void BuildPools()
    {
        foreach (var entry in catalog)
        {
            if (entry.prefab == null) continue;

            catalogByTrigger[entry.trigger] = entry;
            pool.Prewarm(entry.prefab, entry.initialSize, Mathf.Max(entry.maxSize, entry.initialSize));
        }
    }

    // ─────────────────────────── 재생 ───────────────────────────

    /// <summary>트리거에 매핑된 이펙트를 월드 좌표에 재생한다. 매핑이 없으면 아무것도 하지 않는다.</summary>
    private void Play(EffectTrigger trigger, Vector3 worldPosition)
    {
        if (!catalogByTrigger.TryGetValue(trigger, out var entry) || entry.prefab == null)
            return;

        var view = pool.Rent(entry.prefab, Mathf.Max(entry.maxSize, entry.initialSize));
        view.SetLocalPosition(WorldToLocal(overlayLayer, worldPosition));
        view.OnFinished += pool.Return;
        view.OnSpawn();
    }

    private void HandleFocusRingResolved(int index, NodeType nodeType, Vector3 worldPosition, JudgementResult result)
    {
        EffectTrigger trigger = result switch
        {
            JudgementResult.Perfect => EffectTrigger.Perfect,
            JudgementResult.Good => EffectTrigger.Good,
            _ => EffectTrigger.Miss
        };
        Play(trigger, worldPosition);
        SfxManager.Instance.Play(MapToSfxTrigger(result));
    }

    private static SfxTrigger MapToSfxTrigger(JudgementResult result)
    {
        return result switch
        {
            JudgementResult.Perfect => SfxTrigger.Perfect,
            JudgementResult.Good => SfxTrigger.Good,
            _ => SfxTrigger.Miss
        };
    }

    private void HandleNodeConnected(int index, Vector3 worldPosition)
    {
        Play(EffectTrigger.NodeConnected, worldPosition);
    }

    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        Vector3 worldPosition = patternCompleteAnchor != null
            ? patternCompleteAnchor.position
            : (overlayLayer != null ? overlayLayer.position : transform.position);

        Play(info.AllCorrect ? EffectTrigger.PatternCompleteFull : EffectTrigger.PatternComplete, worldPosition);

        // ⚠ 임팩트 효과음은 여기서 바로 울리면 안 된다. 완료 = 마지막 노드 입력이고,
        // 칼이 닿는 것은 거기서 goodWindow(0.1초) + ImpactOffset 뒤다 — 그 시각은 식 하나로 모여 있다.
        //
        // ⚠ 패턴이 자기 소리를 들고 있으면(PatternEffectCue.Sfx) 공용 임팩트음은 물러난다.
        // 안 그러면 둘 다 임팩트 시각이라 한 소리로 뭉쳐 들리고, 저작자는 "내 클립이 이상하다"로 오진한다.
        if (info.AllCorrect && (info.Template == null || !info.Template.HasSfxCue))
            pendingImpactSfx = info.ImpactTime();
    }

    // ─────────────────────────── 임팩트 효과음 ───────────────────────────

    /// <summary>
    /// 임팩트 효과음의 예약 시각(<c>Deadline + ImpactOffset</c>). <b>최대 하나면 된다</b> —
    /// 패턴 완료는 순차적이고 A의 Deadline보다 B의 완료가 최소 0.4초 뒤라 겹치지 않는다
    /// (<c>CameraDirector</c>의 "예약은 최대 하나"와 같은 근거).
    /// </summary>
    private float? pendingImpactSfx;

    /// <summary>
    /// 예약된 임팩트 효과음을 쏜다.
    ///
    /// <para><b>히트스톱과 시계가 안 어긋난다</b>: 예약은 <c>Time.time</c>이고 히트스톱은
    /// <c>Time.timeScale</c>을 쓰지 않는다(Animator Speed Multiplier만 만진다). 정지 창 안에서도
    /// 제때 울리며, 타격감 관점에서도 <b>멈추는 그 순간</b>에 나는 것이 맞다.</para>
    ///
    /// <para>카탈로그에 클립이 없으면 <see cref="SfxManager.Play"/>가 조용히 무시한다 — 배선 전까지 무음.</para>
    /// </summary>
    void Update()
    {
        if (!pendingImpactSfx.HasValue || Time.time < pendingImpactSfx.Value) return;

        pendingImpactSfx = null;
        SfxManager.Instance.Play(SfxTrigger.PatternImpact);
    }

    // ─────────────────────────── 앰비언트 ───────────────────────────

    private void SpawnAmbients()
    {
        if (ambientLayer == null) return;

        foreach (var prefab in ambientPrefabs)
        {
            if (prefab == null) continue;

            var go = Instantiate(prefab, ambientLayer, false);
            var controller = go.GetComponent<AmbientEffectController>();
            if (controller != null)
                ambients.Add(controller);
            else
                Debug.LogWarning($"[EffectManager] 앰비언트 프리팹 '{prefab.name}'에 AmbientEffectController가 없습니다.", prefab);
        }
    }

    /// <summary>
    /// 배경 앰비언트 강도(0~1)를 설정한다. <b>호출자는 <see cref="ScoreDirector"/></b> —
    /// 콤보를 <c>comboIntensityFull</c>로 나눈 값을 넘긴다(docs/ScoreCombo).
    /// 그쪽 배선이 비면 강도는 그냥 안 바뀐다.
    /// </summary>
    public void SetIntensity(float value)
    {
        foreach (var a in ambients)
            a.SetIntensity(value);
    }

    // ─────────────────────────── 좌표 변환 ───────────────────────────

    /// <summary>PatternHandler.WorldToLocal과 동일 로직: 월드 좌표를 target 레이어의 로컬 좌표로 변환.</summary>
    private Vector2 WorldToLocal(RectTransform target, Vector3 worldPosition)
    {
        if (target == null) return Vector2.zero;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvasCamera, worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            target, screenPoint, canvasCamera, out Vector2 localPoint);
        return localPoint;
    }
}
