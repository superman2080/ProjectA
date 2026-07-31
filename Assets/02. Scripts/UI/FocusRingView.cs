using System;
using System.Collections;
using PatternSpace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 입력해야 할 Point 자리에서 <b>줄어드는 링</b>. 링 크기가 노브(Point의 Visual)와 정확히 같아지는 순간이 입력 타이밍이다.
/// 위치는 고정이고 크기만 변한다 — 예전 낙하 노드처럼 화면 위에서 떨어지지 않으므로 행마다 속도가 갈리는 계산이 필요 없다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FocusRingView : MonoBehaviour, IPoolable
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text indexLabel;

    [Tooltip("수축이 끝났을 때의 크기. 노브(Point/Visual)와 같은 값이어야 '딱 맞았다'가 읽힌다.")]
    [SerializeField] private Vector2 endSize = new Vector2(60f, 60f);

    public int PointIndex { get; private set; }
    public NodeType Type { get; private set; }

    /// <summary>수축이 끝난 순간(= 입력 타이밍). 낙하 노드의 OnArrived와 같은 계약이라 PatternHandler 쪽 회수 구조가 그대로 쓰인다.</summary>
    public event Action<FocusRingView> OnArrived;

    private RectTransform rectTransform;
    private Coroutine shrinkRoutine;

    private Vector2 pendingTarget;
    private float pendingStartScale;
    private float pendingDuration;

    void Awake()
    {
        rectTransform = (RectTransform)transform;
    }

    /// <summary>풀에서 대여 직후(비활성 상태에서도) 호출 — 시각 요소만 채우고 수축은 시작하지 않는다.</summary>
    public void Initialize(int displayIndex, Color color, NodeType type, Vector2 targetLocalPos, float startScale, float duration)
    {
        PointIndex = displayIndex - 1;
        Type = type;

        if (background != null)
            background.color = color;
        if (indexLabel != null)
            indexLabel.text = displayIndex.ToString();

        pendingTarget = targetLocalPos;
        pendingStartScale = startScale;
        pendingDuration = duration;
    }

    public void OnSpawn()
    {
        gameObject.SetActive(true);

        if (shrinkRoutine != null)
            StopCoroutine(shrinkRoutine);
        shrinkRoutine = StartCoroutine(ShrinkRoutine(pendingTarget, pendingStartScale, pendingDuration));
    }

    public void OnDespawn()
    {
        StopShrinking();
        OnArrived = null;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 크기를 <paramref name="startScale"/>배에서 <see cref="endSize"/>로 <b>선형</b>으로 줄인다.
    /// 이징을 넣지 않는 이유: 등속이어야 남은 시간이 크기로 정직하게 읽힌다 — 곡선을 먹이면 타이밍 판단이 왜곡된다.
    /// </summary>
    private IEnumerator ShrinkRoutine(Vector2 targetLocalPos, float startScale, float duration)
    {
        rectTransform.anchoredPosition = targetLocalPos;

        Vector2 startSize = endSize * startScale;
        rectTransform.sizeDelta = startSize;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            rectTransform.sizeDelta = Vector2.Lerp(startSize, endSize, duration <= 0f ? 1f : t / duration);
            yield return null;
        }

        rectTransform.sizeDelta = endSize;
        shrinkRoutine = null;
        OnArrived?.Invoke(this);
    }

    public void StopShrinking()
    {
        if (shrinkRoutine != null)
        {
            StopCoroutine(shrinkRoutine);
            shrinkRoutine = null;
        }
    }
}
