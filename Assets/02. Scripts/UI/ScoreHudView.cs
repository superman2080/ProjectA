using TMPro;
using UnityEngine;

/// <summary>
/// 점수·콤보 표시. <see cref="ScoreDirector"/>의 이벤트만 구독하는 <b>순수 표시</b>라 채점에 개입하지 않는다.
///
/// <para><b>⚠ 자리는 패턴인풋 밖이어야 한다.</b> Point_9가 (700, 700)이라 우상단이 비어 있다.
/// 씬에서 <b>Canvas 직속</b>이어야 하며, <c>PatternHandler</c>(1400x1400)의 자식으로 두면 그 rect 기준 앵커가 되어
/// 캔버스 좌표로는 패턴 영역 한가운데에 온다(<c>DodgePointView</c>가 겪은 것과 같은 함정, §11-8).</para>
///
/// <para>배선이 비면 그 표시만 조용히 빠진다(기존 규율).</para>
/// </summary>
public class ScoreHudView : MonoBehaviour
{
    [SerializeField] private ScoreDirector director;

    [Header("Widgets")]
    [Tooltip("점수 표시. 비우면 점수만 안 뜬다.")]
    [SerializeField] private TMP_Text scoreLabel;
    [Tooltip("콤보 숫자. 비우면 콤보만 안 뜬다.")]
    [SerializeField] private TMP_Text comboLabel;
    [Tooltip("콤보가 0일 때 통째로 숨길 대상(숫자 + 'COMBO' 글자 등). 비우면 comboLabel만 숨긴다.")]
    [SerializeField] private GameObject comboGroup;

    [Header("Feel")]
    [Tooltip("점수가 목표값을 따라가는 시간(초). 즉시 대입하면 큰 가산이 안 읽힌다.")]
    [SerializeField] private float scoreRollDuration = 0.35f;

    [Tooltip("콤보가 오를 때 튀는 배율. 1이면 안 튄다.")]
    [SerializeField] private float comboPunchScale = 1.25f;
    [Tooltip("튄 크기가 1로 돌아오는 시간(초).")]
    [SerializeField] private float comboPunchDuration = 0.12f;

    [Tooltip("점수 표시 형식. N0이면 1,234,567.")]
    [SerializeField] private string scoreFormat = "N0";

    private long targetScore;
    private double shownScore;   // ⚠ double이다 — long에 직접 보간하면 1점 단위에서 굴러가지 않는다
    private float punchUntil;
    private RectTransform comboRect;

    void Awake()
    {
        comboRect = comboLabel != null ? comboLabel.rectTransform : null;
    }

    void OnEnable()
    {
        if (director == null) return;

        director.OnScoreChanged += HandleScoreChanged;
        director.OnComboChanged += HandleComboChanged;

        // 도중에 켜져도 화면이 0으로 뜨지 않게 현재 값을 즉시 반영한다.
        targetScore = director.Score;
        shownScore = targetScore;
        HandleComboChanged(director.Combo);
        Redraw();
    }

    void OnDisable()
    {
        if (director == null) return;

        director.OnScoreChanged -= HandleScoreChanged;
        director.OnComboChanged -= HandleComboChanged;
    }

    private void HandleScoreChanged(long score)
    {
        // ⚠ 리셋(0으로 내려감)은 굴리지 않는다 — 곡을 다시 시작했는데 숫자가 천천히 내려가면 버그로 읽힌다.
        targetScore = score;
        if (score < shownScore) shownScore = score;
    }

    private void HandleComboChanged(int combo)
    {
        if (comboGroup != null) comboGroup.SetActive(combo > 0);
        else if (comboLabel != null) comboLabel.gameObject.SetActive(combo > 0);

        if (comboLabel == null) return;

        comboLabel.text = combo.ToString();
        if (combo > 0) punchUntil = Time.time + Mathf.Max(comboPunchDuration, 0.01f);
    }

    void Update()
    {
        TickScoreRoll();
        TickComboPunch();
    }

    private void TickScoreRoll()
    {
        if (scoreLabel == null) return;

        if (System.Math.Abs(shownScore - targetScore) < 0.5)
        {
            if (shownScore == targetScore) return;

            shownScore = targetScore;
            Redraw();
            return;
        }

        // 지수 감쇠 — 프레임률과 무관하게 같은 속도로 붙는다.
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(scoreRollDuration, 0.01f));
        shownScore += (targetScore - shownScore) * k;
        Redraw();
    }

    private void Redraw()
    {
        if (scoreLabel == null) return;
        scoreLabel.text = ((long)System.Math.Round(shownScore)).ToString(scoreFormat);
    }

    private void TickComboPunch()
    {
        if (comboRect == null) return;

        if (Time.time >= punchUntil)
        {
            if (comboRect.localScale != Vector3.one) comboRect.localScale = Vector3.one;
            return;
        }

        float remain = (punchUntil - Time.time) / Mathf.Max(comboPunchDuration, 0.01f);
        float scale = Mathf.LerpUnclamped(1f, Mathf.Max(comboPunchScale, 1f), remain);
        comboRect.localScale = Vector3.one * scale;
    }
}
