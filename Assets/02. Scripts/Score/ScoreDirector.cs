using System;
using ChartGen;
using PatternSpace;
using ScoreSpace;
using UnityEngine;

/// <summary>
/// 채점의 <b>유일 관리 지점</b>. <see cref="PatternHandler"/>·<see cref="ChartPlayer"/>의 기존 이벤트만 구독하는
/// 순수 소비자라 판정 파이프라인에 개입하지 않는다(<c>EffectManager</c>·<c>CameraDirector</c>와 같은 자리).
/// <b><see cref="PatternHandler"/>는 채점을 위해 한 줄도 고치지 않는다.</b>
///
/// <para><b>⚠ 놓친 노트는 이벤트로 오지 않는다.</b> 판정 이벤트(<c>OnJudged</c>)는 <b>친 노트</b>에서만 나고,
/// 오답 인덱스나 무입력에서는 아예 나지 않는다. 그래서 이 클래스의 핵심은
/// <b>패턴이 끝날 때의 뺄셈</b>이다(<see cref="HandlePatternComplete"/>).</para>
///
/// <para><b>⚠ <c>OnFocusRingMissedArrival</c>을 구독하지 않는다.</b> 이름은 "놓쳤다"지만 아니다 —
/// 링의 수축 완료는 <c>ExpectedTime</c> <b>정각</b>에 발행되는데 판정 창은 그보다 <c>goodWindow</c>만큼 더 열려 있다.
/// 즉 <b>늦은 쪽 Perfect/Good이 전부 이 이벤트를 먼저 발행</b>하므로, 이걸로 콤보를 끊으면
/// 정확히 친 노트의 절반이 콤보를 끊는다(docs/ScoreCombo Research §3).</para>
///
/// <para><b>점수 구조</b>: 만점을 세 풀(노트·패턴·콤보)로 나눈 <b>정규화형</b>이다.
/// 콤보를 노트 점수에 <b>곱하지 않는</b> 이유는 곱하면 같은 Perfect라도 곡 후반이 몇 배 비싸져
/// 초반 실수가 싸게 먹히기 때문이다. 콤보는 자기 풀을 따로 갖는다.</para>
/// </summary>
public class ScoreDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("총 노트 수·만점의 출처이자 곡 시작/종료 신호. 비우면 채점이 통째로 꺼진다.")]
    [SerializeField] private ChartPlayer chartPlayer;

    [Tooltip("콤보로 배경 앰비언트 강도를 올린다. 비우면 그 연결만 빠진다.")]
    [SerializeField] private EffectManager effectManager;

    [Header("Score")]
    [Tooltip("곡이 maxScore를 지정하지 않았을 때(0 이하) 쓰는 폴백. 만점의 진실의 원천은 SongChart다.")]
    [SerializeField] private long defaultMaxScore = 1_000_000;

    [Tooltip("노트 판정이 차지하는 비중. 셋의 합으로 정규화되므로 상대값으로 써도 된다.")]
    [Range(0f, 1f)] [SerializeField] private float noteShare = 0.7f;
    [Tooltip("패턴 완주 성공(AllCorrect)이 차지하는 비중.")]
    [Range(0f, 1f)] [SerializeField] private float patternShare = 0.2f;
    [Tooltip("콤보가 차지하는 비중. ⚠ 노트 점수에 곱하는 배율이 아니라 별도 풀이다.")]
    [Range(0f, 1f)] [SerializeField] private float comboShare = 0.1f;

    [Tooltip("Good 한 개가 Perfect 대비 갖는 가중치. Miss는 언제나 0이다.")]
    [Range(0f, 1f)] [SerializeField] private float goodWeight = 0.6f;

    [Header("Grade")]
    [Tooltip("등급 비율 문턱(내림차순). SS / S / A / B / C의 하한이며 경계는 이상(>=)이다.\n" +
             "⚠ SSS는 여기 없다 — 퍼펙트(Miss·Good 0 · 실패 패턴 0 · 풀콤보)라는 사실로만 나온다.\n" +
             "⚠ 절대 점수가 아니라 비율이다. 만점이 곡마다 다르므로 점수로 가르면 곡 간 비교가 깨진다.")]
    [SerializeField] private float[] gradeThresholds = { 0.95f, 0.90f, 0.85f, 0.77f, 0.60f };

    [Header("Combo")]
    [Tooltip("콤보 단계 문턱. 배열 길이가 곧 단계 수다 — 코드는 3을 모른다.")]
    [SerializeField] private int[] comboTierThresholds = { 10, 30, 60 };

    [Tooltip("앰비언트 강도가 1에 도달하는 콤보. 이 값 이상이면 계속 1이다.")]
    [Min(1)] [SerializeField] private int comboIntensityFull = 50;

    // ── 확장 포인트 ──────────────────────────────────────────────────────────
    public event Action<long> OnScoreChanged;
    /// <summary>콤보가 바뀐 순간. <b>0으로 오면 끊긴 것</b>이다.</summary>
    public event Action<int> OnComboChanged;
    /// <summary>콤보가 끊기기 <b>직전</b>의 값. 연출이 "몇에서 끊겼다"를 말하려면 이 값이 필요하다.</summary>
    public event Action<int> OnComboBroken;
    /// <summary><b>단계가 바뀐 순간만</b> 발행한다 — 매 노트마다 쏘면 구독자가 같은 값을 다시 비교해야 한다.</summary>
    public event Action<int> OnComboTierChanged;
    public event Action<ScoreResult> OnFinalized;

    public long Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int ComboTier { get; private set; }

    // ── 누적 상태 ────────────────────────────────────────────────────────────
    private int totalNotes, totalPatterns;
    private int perfectCount, goodCount, missCount;
    private int successPatterns;
    private long comboSum;          // Σ(각 노트 직후의 콤보 수). 콤보 풀의 분자다
    private long maxScore;

    /// <summary>지금 판정 대상 패턴에서 <c>OnJudged</c>가 몇 번 났는가. 패턴이 끝날 때 뺄셈의 좌변이 된다.</summary>
    private int judgedInPattern;

    void OnEnable()
    {
        if (handler != null)
        {
            handler.OnJudged += HandleJudged;
            handler.OnPatternComplete += HandlePatternComplete;
            handler.OnAllPatternsCleared += HandleAllCleared;
        }

        if (chartPlayer != null)
        {
            chartPlayer.OnCountdownStarted += HandleCountdownStarted;
            chartPlayer.OnSongEnded += HandleSongEnded;
        }
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnJudged -= HandleJudged;
            handler.OnPatternComplete -= HandlePatternComplete;
            handler.OnAllPatternsCleared -= HandleAllCleared;
        }

        if (chartPlayer != null)
        {
            chartPlayer.OnCountdownStarted -= HandleCountdownStarted;
            chartPlayer.OnSongEnded -= HandleSongEnded;
        }
    }

    // ── 곡 경계 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 곡이 시작된다. <b>총 수를 여기서 센다</b> — 채보는 이미 확정돼 있으므로 재생 전에 만점이 정해진다.
    /// </summary>
    private void HandleCountdownStarted(float duration)
    {
        SongChart chart = chartPlayer != null ? chartPlayer.ActiveChart : null;
        CountChart(chart);
        ResetRun();
    }

    /// <summary>
    /// 총 노트/패턴 수와 만점을 곡에서 읽는다.
    /// 노트 수는 <c>entries[i].onsetTimes.Length</c>의 합 — <b>노드별 판정 시각</b>이 곧 노트 하나다.
    /// </summary>
    private void CountChart(SongChart chart)
    {
        totalNotes = 0;
        totalPatterns = 0;
        maxScore = defaultMaxScore;

        if (chart == null) return;

        if (chart.maxScore > 0L) maxScore = chart.maxScore;

        if (chart.entries == null) return;

        totalPatterns = chart.entries.Length;
        foreach (var entry in chart.entries)
        {
            if (entry?.onsetTimes == null) continue;
            totalNotes += entry.onsetTimes.Length;
        }
    }

    private void ResetRun()
    {
        Score = 0;
        Combo = 0;
        MaxCombo = 0;
        perfectCount = goodCount = missCount = 0;
        successPatterns = 0;
        comboSum = 0L;
        judgedInPattern = 0;

        OnScoreChanged?.Invoke(Score);
        OnComboChanged?.Invoke(Combo);
        SetComboTier(0);
        PushAmbient();
    }

    /// <summary>곡이 끝났다. 이 시점의 누적이 곧 최종 결과다.</summary>
    private void HandleSongEnded()
    {
        var result = BuildResult();

        if (GameSession.Instance != null) GameSession.Instance.LastResult = result;
        OnFinalized?.Invoke(result);
    }

    /// <summary>
    /// 중단으로 전부 정리됐다. <b>진행 중 카운터만</b> 되돌린다 —
    /// 누적 점수까지 지우면 곡을 멈춘 순간 화면의 점수가 0으로 튄다.
    /// </summary>
    private void HandleAllCleared() => judgedInPattern = 0;

    // ── 노트 채점 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 노트 하나가 판정됐다. <b>친 노트만</b> 여기로 온다 —
    /// 오답 인덱스는 <c>OnJudged</c>를 발행하지 않고, 무입력은 아예 판정되지 않는다.
    /// </summary>
    private void HandleJudged(JudgementResult result, int index)
    {
        judgedInPattern++;
        ApplyNote(result);
    }

    /// <summary>판정 하나를 누적에 반영한다. <b>놓친 노트도 여기로 들어온다</b>(Miss로).</summary>
    private void ApplyNote(JudgementResult result)
    {
        if (result == JudgementResult.Miss)
        {
            missCount++;
            BreakCombo();
        }
        else
        {
            if (result == JudgementResult.Perfect) perfectCount++;
            else goodCount++;

            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;

            // ⚠ 콤보 '직후' 값을 더한다 — 첫 노트가 1이어야 분모(N(N+1)/2)와 짝이 맞는다.
            comboSum += Combo;

            OnComboChanged?.Invoke(Combo);
            SetComboTier(ScoreMath.ComboTierOf(Combo, comboTierThresholds));
            PushAmbient();
        }

        Recalculate();
    }

    private void BreakCombo()
    {
        if (Combo > 0)
        {
            OnComboBroken?.Invoke(Combo);
            Combo = 0;
            OnComboChanged?.Invoke(Combo);
        }

        SetComboTier(0);
        PushAmbient();
    }

    /// <summary>단계가 <b>실제로 바뀐 경우에만</b> 발행한다.</summary>
    private void SetComboTier(int tier)
    {
        if (tier == ComboTier) return;

        ComboTier = tier;
        OnComboTierChanged?.Invoke(tier);
    }

    private void PushAmbient()
    {
        if (effectManager == null) return;

        effectManager.SetIntensity(Mathf.Clamp01((float)Combo / Mathf.Max(comboIntensityFull, 1)));
    }

    // ── 패턴 경계 — 뺄셈 ─────────────────────────────────────────────────────

    /// <summary>
    /// 패턴이 끝났다(완주/만료). 여기가 이 클래스의 핵심이다.
    ///
    /// <para><b>놓친 노트 = 이 패턴의 노드 수 − 판정된 수</b>. 놓친 노트는 이벤트로 오지 않으므로
    /// 뺄셈이 유일한 셈법이다. 오답 인덱스를 아무리 연타해도 <c>OnJudged</c>가 안 나므로
    /// 그만큼 그대로 여기에 잡힌다.</para>
    ///
    /// <para><b>⚠ 오답 인덱스 자체는 콤보를 끊지 않는다.</b> 끊는 것은 그 결과로 놓치게 된 노트다 —
    /// 즉시 끊으려면 <c>OnJudgeTargetFirstMiss</c>가 이미 있지만, 쓰면 패턴이 정체하는 동안
    /// 콤보가 두 번 끊기는 것으로 읽힌다.</para>
    /// </summary>
    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        if (info.AllCorrect) successPatterns++;

        int nodeCount = info.Template != null && info.Template.AllData != null
            ? info.Template.AllData.Count
            : judgedInPattern;   // 템플릿을 모르면 뺄 것이 없다(놓친 노트 0으로 본다)

        int missed = ScoreMath.MissedNotes(nodeCount, judgedInPattern);
        for (int i = 0; i < missed; i++) ApplyNote(JudgementResult.Miss);

        judgedInPattern = 0;
        Recalculate();
    }

    // ── 집계 ─────────────────────────────────────────────────────────────────

    private float CurrentAchievement() => ScoreMath.Achievement(
        ScoreMath.NoteAchievement(perfectCount, goodCount, totalNotes, goodWeight),
        ScoreMath.PatternAchievement(successPatterns, totalPatterns),
        ScoreMath.ComboAchievement(comboSum, totalNotes),
        noteShare, patternShare, comboShare);

    private void Recalculate()
    {
        long next = ScoreMath.Total(CurrentAchievement(), maxScore);
        if (next == Score) return;

        Score = next;
        OnScoreChanged?.Invoke(Score);
    }

    /// <summary>
    /// 퍼펙트인가. <b>정수 비교 셋의 논리곱</b>이라 오차가 낄 자리가 없다 —
    /// 비율로 판정하면 반올림 하나로 최고 등급이 영영 안 나올 수 있다.
    /// </summary>
    private bool IsPerfect() =>
        totalNotes > 0
        && missCount == 0 && goodCount == 0
        && successPatterns == totalPatterns
        && MaxCombo == totalNotes;

    private ScoreResult BuildResult()
    {
        float ratio = CurrentAchievement();
        bool perfect = IsPerfect();

        return new ScoreResult(
            ScoreMath.Total(ratio, maxScore), maxScore, ratio,
            ScoreMath.GradeOf(ratio, perfect, gradeThresholds),
            perfectCount, goodCount, missCount, totalNotes,
            MaxCombo, successPatterns, totalPatterns, perfect);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        float sum = noteShare + patternShare + comboShare;
        if (sum > 0f && Mathf.Abs(sum - 1f) > 0.001f)
        {
            Debug.LogWarning(
                $"[ScoreDirector] 비중 합이 {sum:0.###}입니다. 정규화되므로 만점은 깨지지 않지만, " +
                "인스펙터 값이 '몇 %인지'와 달라 읽기 어렵습니다.", this);
        }

        for (int i = 1; i < gradeThresholds.Length; i++)
        {
            if (gradeThresholds[i] <= gradeThresholds[i - 1]) continue;

            Debug.LogWarning(
                $"[ScoreDirector] 등급 문턱이 내림차순이 아닙니다({i - 1}번 {gradeThresholds[i - 1]} < {i}번 {gradeThresholds[i]}). " +
                "위 등급부터 검사하므로 중간 등급이 영영 안 나옵니다.", this);
            break;
        }
    }
#endif
}
