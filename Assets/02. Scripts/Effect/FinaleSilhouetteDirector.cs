using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ChartGen;
using EnemySpace;
using PatternSpace;

/// <summary>
/// 곡의 <b>마지막 패턴을 성공으로 끝낸 순간</b> 화면을 뒤집는 마무리 연출 —
/// 배경은 빨갛게, 배우들만 검은 실루엣으로. 그리고 그 순간을 슬로우모션으로 늘인다.
///
/// <para>관례는 <see cref="CameraDirector"/>·<see cref="HitStopDirector"/>와 같다 —
/// <b>기존 이벤트만 구독하는 순수 소비자</b>이고 판정에 개입하지 않으며, 배선이 비면 조용히 비활성된다.
/// <c>PatternHandler</c>·<c>ChartPlayer</c>·<c>ScoreDirector</c>는 이 연출을 위해 한 줄도 안 고친다.</para>
///
/// <para><b>⚠ 이 클래스만이 <see cref="Time.timeScale"/>을 건드린다.</b> §7-3은 그것을 금지하는데,
/// 근거는 판정·클립 정렬이 <c>Time.time</c>인 반면 채보는 <c>audioSource.time</c>으로 돌고
/// <b>오디오는 timeScale 밖이라 차이가 영구 누적된다</b>는 것이다. 그 근거가 여기서만 성립하지 않는다 —
/// 마지막 엔트리의 마지막 노드가 이미 입력됐고 <b>판정할 패턴이 남아 있지 않아</b> 누적될 곳이 없다.
/// 즉 예외가 아니라 규칙의 경계다:</para>
///
/// <para><b><c>Time.timeScale</c>은 판정이 남아 있는 동안 못 쓴다. 곡의 마지막 판정이 끝난 뒤에는
/// 쓸 수 있고, 되돌리는 책임만 남는다.</b> 그래서 트리거 조건(마지막 엔트리 + <c>AllCorrect</c>)이
/// 곧 안전 조건이며, 그 밖에서는 절대 건드리지 않는다.</para>
///
/// <para>상세: <c>docs/FinaleSilhouette/</c></para>
/// </summary>
public class FinaleSilhouetteDirector : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("채보를 읽어 '마지막 엔트리인가'를 판단한다. ChartPlayer를 수정하지는 않는다.")]
    [SerializeField] private ChartPlayer chartPlayer;

    [Tooltip("살아 있는 적·시체·조각의 유일한 소유자. 실루엣 대상을 여기서 받아 온다.")]
    [SerializeField] private EnemyDirector enemyDirector;

    [Tooltip("플레이어 루트(서브트리 전체가 실루엣이 된다). 비면 플레이어만 실루엣에서 빠진다.")]
    [SerializeField] private Transform playerRoot;

    [Tooltip("마지막 일격에서 히트스톱을 억제한다. 비우면 억제하지 않는다 —\n" +
             "⚠ 그러면 슬로우가 아니라 정지 컷이 된다(아래 hitStopSuppression 툴팁).")]
    [SerializeField] private HitStopDirector hitStopDirector;

    [Tooltip("마지막 순간에 감출 점수 HUD. 비우면 HUD가 실루엣 위에 그대로 남는다.")]
    [SerializeField] private ScoreHudView scoreHud;

    [Header("Renderer Features")]
    [Tooltip("실루엣을 그리는 렌더러(보통 PC_Renderer). 품질 등급이 여럿이면 전부 넣는다 —\n" +
             "안 넣은 등급에서만 연출이 조용히 빠진다.")]
    [SerializeField] private UniversalRendererData[] renderers;

    [Tooltip("배경을 빨갛게 칠하는 RenderObjects 피처의 이름.")]
    [SerializeField] private string backgroundFeatureName = "FinaleBackground";

    [Tooltip("배우를 검게 칠하는 RenderObjects 피처의 이름.")]
    [SerializeField] private string actorFeatureName = "FinaleActors";

    [Tooltip("배우가 올라갈 레이어. RenderObjects 피처 둘의 LayerMask가 이 값을 기준으로 갈린다.")]
    [SerializeField] private string silhouetteLayerName = "Silhouette";

    [Header("Feel")]
    [Tooltip("연출 전체 토글. 끄면 이 층만 죽고 나머지는 그대로 돈다(§7-3의 연출 토글 규율).")]
    [SerializeField] private bool silhouetteEnabled = true;

    [Tooltip("노출 시간(초). ⚠ 실시간(unscaled)이다 — 0.1배속이어도 이 값만큼만 보인다.")]
    [SerializeField] private float holdDuration = 1f;

    [Tooltip("슬로우모션 배속. 1이면 시계를 안 건드린다.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float slowTimeScale = 0.1f;

    [Tooltip("곡 오디오의 배속. 1이면 음악은 정상 속도로 끝난다(기본).\n" +
             "⚠ 내리면 ChartPlayer가 audioSource.isPlaying을 보므로 곡 종료가 그만큼 늦어진다.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float audioPitchScale = 1f;

    [Tooltip("마지막 일격의 히트스톱을 억제한다.\n" +
             "⚠ 끄면 정지 창이 timeScale만큼 늘어나(0.1초 → 실시간 1초) 노출 내내 화면이 얼어붙는다 —\n" +
             "슬로우모션이 아니라 정지 컷이 된다.")]
    [SerializeField] private bool suppressHitStop = true;

    /// <summary>실루엣이 터진 순간. 결과 화면·서사 연출이 붙을 자리다.</summary>
    public event System.Action OnFinaleBegan;

    /// <summary>실루엣이 걷힌 순간.</summary>
    public event System.Action OnFinaleEnded;

    private int completedPatterns;
    private int totalPatterns;

    private bool armed;          // 마지막 패턴 성공을 확인했고 임팩트를 기다리는 중
    private float fireTime;
    private bool active;         // 실루엣이 화면에 올라가 있는 중
    private float holdRemaining; // 실시간 잔여

    private int silhouetteLayer = -1;
    private float restoreTimeScale = 1f;
    private float restoreAudioPitch = 1f;
    private AudioSource songSource;

    // 바꾸기 전 레이어를 오브젝트별로 기록한다. ⚠ 전부 0으로 되돌리면 AmbushOutline(9번)에
    // 올라가 있던 기습자가 아웃라인을 잃는다(§11-8) — 원복은 언제나 이 기록으로만 한다.
    private readonly List<Transform> swapped = new List<Transform>();
    private readonly List<int> swappedLayers = new List<int>();
    private readonly List<Transform> actorScratch = new List<Transform>();

    void OnEnable()
    {
        silhouetteLayer = LayerMask.NameToLayer(silhouetteLayerName);
        if (silhouetteLayer < 0)
        {
            Debug.LogWarning(
                $"[FinaleSilhouette] '{silhouetteLayerName}' 레이어가 없습니다. " +
                "ProjectSettings의 빈 슬롯에 추가하세요 — 그때까지 이 연출은 비활성입니다.", this);
        }

        if (handler == null) return;

        handler.OnPatternComplete += HandlePatternComplete;
        handler.OnAllPatternsCleared += HandleAllCleared;

        // 곡 시작이 카운터의 리셋 지점이다 — 재도전에서도 같은 자리에서 다시 센다
        // (ScoreDirector가 총량을 잡는 시점과 같다). ChartPlayer는 수정하지 않는다.
        if (chartPlayer != null) chartPlayer.OnCountdownStarted += HandleCountdownStarted;

        ResetRun();
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnPatternComplete -= HandlePatternComplete;
            handler.OnAllPatternsCleared -= HandleAllCleared;
        }

        if (chartPlayer != null) chartPlayer.OnCountdownStarted -= HandleCountdownStarted;

        // ⚠ 반드시 되돌린다. 안 그러면 화면이 빨간 채로, 게임이 0.1배속으로 굳는다
        // (CameraDirector가 OnDisable에서 Brain을 되살리는 것과 같은 규율).
        armed = false;
        Restore();
    }

    /// <summary>
    /// 마지막 엔트리를 성공으로 끝냈는가. <b>둘 다여야 한다.</b>
    ///
    /// <para>"마지막"은 채보를 직접 읽어 센다 — <see cref="ScoreDirector"/>가 총량을 잡는 것과 같은 방식이라
    /// <c>ChartPlayer</c>에 이벤트를 새로 뚫지 않는다. <c>ChartPlayer.OnSongEnded</c>는 쓸 수 없다:
    /// 그것은 <b>오디오가 멈춘 뒤에</b> 나오므로 아웃트로 길이만큼 늦다.</para>
    /// </summary>
    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        completedPatterns++;

        if (!silhouetteEnabled || silhouetteLayer < 0) return;
        if (totalPatterns <= 0 || completedPatterns < totalPatterns) return;

        // ⚠ 실패로 끝나면 아무 일도 없다 — 시계도 안 건드린다.
        if (!info.AllCorrect) return;

        // 화면에서 사건이 일어나는 시각. §6·§7-1·§7-3과 같은 확장 메서드를 쓴다 —
        // 식을 손으로 다시 조립하면 언젠가 갈라진다.
        fireTime = info.ImpactTime();
        armed = true;

        // 히트스톱은 같은 프레임에 발사되므로 예약보다 먼저 막아 둔다.
        if (suppressHitStop && hitStopDirector != null) hitStopDirector.SuppressNextMainImpact();
    }

    private void HandleAllCleared()
    {
        armed = false;
        Restore();
    }

    private void HandleCountdownStarted(float _) => ResetRun();

    private void ResetRun()
    {
        completedPatterns = 0;
        totalPatterns = ResolveTotalPatterns();
    }

    void Update()
    {
        if (armed && Time.time >= fireTime)
        {
            armed = false;
            Fire();
        }

        if (!active) return;

        // ⚠ 반드시 unscaled다. Time.deltaTime으로 재면 0.1배속에서 10배로 늘어난다.
        holdRemaining -= Time.unscaledDeltaTime;
        if (holdRemaining <= 0f) Restore();
    }

    private void Fire()
    {
        if (active) return;

        SwapActorsToSilhouette();
        SetFeatures(true);

        restoreTimeScale = Time.timeScale;
        if (slowTimeScale < 1f) Time.timeScale = slowTimeScale;

        if (audioPitchScale < 1f)
        {
            songSource = chartPlayer != null ? chartPlayer.SongSource : null;
            if (songSource != null)
            {
                restoreAudioPitch = songSource.pitch;
                songSource.pitch = restoreAudioPitch * audioPitchScale;
            }
        }

        scoreHud?.SetHidden(true);

        active = true;
        holdRemaining = Mathf.Max(holdDuration, 0f);
        OnFinaleBegan?.Invoke();
    }

    /// <summary>
    /// 원복. <b>세 경로에서 불린다</b> — 노출 종료 · 곡 중단(<c>OnAllPatternsCleared</c>) · <c>OnDisable</c>.
    /// 하나라도 빠지면 화면이 빨간 채로, 게임이 0.1배속으로 굳는다. 여러 번 불려도 안전해야 한다.
    /// </summary>
    private void Restore()
    {
        if (!active)
        {
            // 발사 전에 불려도 레이어 기록은 비어 있으므로 아무 일도 안 일어난다.
            RestoreLayers();
            return;
        }

        active = false;

        SetFeatures(false);
        RestoreLayers();

        Time.timeScale = restoreTimeScale;
        if (songSource != null)
        {
            songSource.pitch = restoreAudioPitch;
            songSource = null;
        }

        scoreHud?.SetHidden(false);

        OnFinaleEnded?.Invoke();
    }

    // ── 레이어 스왑 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 배우 서브트리를 통째로 실루엣 레이어에 올린다.
    ///
    /// <para><b>⚠ 원래 레이어를 오브젝트별로 기록한다.</b> 적은 풀에서 나오므로(§10) 원복이 어긋나면
    /// <b>다음 대여가 실루엣인 채로 나온다</b> — §11-8의 기습 아웃라인이 정확히 이 함정을 밟아
    /// 세 곳에서 끄고 있다. 게다가 기습자는 <c>AmbushOutline</c> 레이어에 올라가 있어
    /// 일괄로 0을 되돌리면 그 강조가 사라진다.</para>
    /// </summary>
    private void SwapActorsToSilhouette()
    {
        actorScratch.Clear();
        if (playerRoot != null) actorScratch.Add(playerRoot);
        enemyDirector?.CollectActorRoots(actorScratch);

        foreach (var root in actorScratch)
        {
            if (root == null) continue;
            RecordAndSetLayer(root);
        }
    }

    private void RecordAndSetLayer(Transform target)
    {
        swapped.Add(target);
        swappedLayers.Add(target.gameObject.layer);
        target.gameObject.layer = silhouetteLayer;

        for (int i = 0; i < target.childCount; i++)
            RecordAndSetLayer(target.GetChild(i));
    }

    private void RestoreLayers()
    {
        for (int i = 0; i < swapped.Count; i++)
        {
            if (swapped[i] == null) continue;   // 풀 반납으로 파괴됐을 수 있다
            swapped[i].gameObject.layer = swappedLayers[i];
        }

        swapped.Clear();
        swappedLayers.Clear();
    }

    // ── 렌더러 피처 ─────────────────────────────────────────────────────────

    /// <summary>
    /// <b>⚠ 이것은 렌더러 <i>에셋</i>의 상태를 바꾼다.</b> 플레이 모드에서 켠 채로 끝나면
    /// 에디터 세션에 빨간 화면이 그대로 남는다 — <see cref="Restore"/>가 세 경로에서 불리는 이유다.
    /// </summary>
    private void SetFeatures(bool on)
    {
        if (renderers == null) return;

        foreach (var data in renderers)
        {
            if (data == null) continue;

            foreach (var feature in data.rendererFeatures)
            {
                if (feature == null) continue;
                if (feature.name != backgroundFeatureName && feature.name != actorFeatureName) continue;

                feature.SetActive(on);
            }
        }
    }

    // ── 채보 ────────────────────────────────────────────────────────────────

    private int ResolveTotalPatterns()
    {
        var chart = chartPlayer != null ? chartPlayer.ActiveChart : null;
        return chart?.entries != null ? chart.entries.Length : 0;
    }
}
