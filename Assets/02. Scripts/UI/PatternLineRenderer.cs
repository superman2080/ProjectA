using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class PatternLineRenderer : MaskableGraphic
{
    [SerializeField] private float lineWidth = 8f;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color missColor = Color.red;

    private readonly List<Vector2> points = new List<Vector2>();
    private bool useMissColor;
    private Vector2? liveEndPoint;
    private float currentAlpha = 1f;
    private Coroutine fadeRoutine;

    /// <summary>확장 포인트: 새 좌표가 라인에 추가될 때(파티클 스폰 후보 지점) 발행.</summary>
    public event Action<Vector2> OnSegmentPointAdded;
    /// <summary>확장 포인트: 페이드아웃 시작/종료 시점.</summary>
    public event Action OnFadeStarted;
    public event Action OnFadeCompleted;

    protected override void Awake()
    {
        base.Awake();
        // 순수 시각 효과 오브젝트 — Point들 위에 그려지므로 raycastTarget이 켜져 있으면
        // 아래 Point의 포인터 입력을 가로채 드래그 자체가 동작하지 않게 된다.
        raycastTarget = false;
    }

    public void SetPoints(IReadOnlyList<Vector2> pts)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        currentAlpha = 1f;

        points.Clear();
        if (pts != null)
            points.AddRange(pts);

        SetVerticesDirty();

        if (points.Count > 0)
            OnSegmentPointAdded?.Invoke(points[^1]);
    }

    public void SetLiveEndPoint(Vector2 pt)
    {
        liveEndPoint = pt;
        SetVerticesDirty();
    }

    public void ClearLiveEndPoint()
    {
        if (liveEndPoint == null) return;
        liveEndPoint = null;
        SetVerticesDirty();
    }

    public void SetCorrectState(bool allCorrect)
    {
        useMissColor = !allCorrect;
        SetVerticesDirty();
    }

    public void FadeOutAndClear(float duration)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(duration));
    }

    private IEnumerator FadeRoutine(float duration)
    {
        OnFadeStarted?.Invoke();

        float startAlpha = currentAlpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            currentAlpha = Mathf.Lerp(startAlpha, 0f, duration <= 0f ? 1f : t / duration);
            SetVerticesDirty();
            yield return null;
        }

        currentAlpha = 0f;
        points.Clear();
        liveEndPoint = null;
        SetVerticesDirty();

        currentAlpha = 1f;
        fadeRoutine = null;
        OnFadeCompleted?.Invoke();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        int liveCount = points.Count + (liveEndPoint.HasValue ? 1 : 0);
        if (liveCount < 2)
            return;

        Color32 segmentColor = useMissColor ? missColor : normalColor;
        segmentColor.a = (byte)Mathf.RoundToInt(segmentColor.a * currentAlpha);

        Vector2 Get(int i) => i < points.Count ? points[i] : liveEndPoint.Value;

        for (int i = 0; i < liveCount - 1; i++)
        {
            Vector2 a = Get(i);
            Vector2 b = Get(i + 1);
            AddSegmentQuad(vh, a, b, lineWidth, segmentColor);
        }
    }

    private static void AddSegmentQuad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 color)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x) * (width * 0.5f);

        int start = vh.currentVertCount;

        vh.AddVert(a - normal, color, Vector2.zero);
        vh.AddVert(a + normal, color, Vector2.zero);
        vh.AddVert(b + normal, color, Vector2.zero);
        vh.AddVert(b - normal, color, Vector2.zero);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
