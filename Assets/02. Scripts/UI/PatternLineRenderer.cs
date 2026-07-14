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

    [Header("Capsule Guide (가이드라인용)")]
    [Tooltip("각 점에 원을 그리고 선분을 원 경계까지만 그려 캡슐(⊂⊃) 실루엣을 만든다. 얇은 입력 라인은 off로 둔다.")]
    [SerializeField] private bool capsuleMode = false;
    [Tooltip("원 하나를 이루는 분할 수. 클수록 매끄럽다.")]
    [Range(6, 64)][SerializeField] private int capSegments = 24;
    [Tooltip("캡슐 실루엣을 감싸는 외곽선 두께. 0이면 외곽선 없음.")]
    [SerializeField] private float outlineWidth = 3f;
    [SerializeField] private Color outlineColor = new Color(1f, 1f, 1f, 0.6f);

    [Header("Gradient (capsuleMode 전용)")]
    [Tooltip("켜면 normalColor/outlineColor 대신 아래 그라데이션을 경로 진행 방향(첫 노드 0 → 마지막 노드 1)으로 적용한다.")]
    [SerializeField] private bool useGradient = false;
    [SerializeField] private Gradient fillGradient = new Gradient();
    [SerializeField] private Gradient outlineGradient = new Gradient();

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

        if (capsuleMode)
        {
            PopulateCapsuleMesh(vh);
            return;
        }

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

    /// <summary>
    /// 캡슐(⊂⊃) 실루엣을 반지름/색만 바꿔 두 번 그린다 — 바깥(외곽선) 위에 안쪽(채움)을 덮어,
    /// 삐져나온 테두리가 실루엣 전체를 감싸는 외곽선이 된다.
    /// 안쪽이 바깥에 완전히 포함되므로 반투명이어도 블렌딩 횟수가 균일해 얼룩이 생기지 않는다.
    /// (liveEndPoint는 가이드 용도가 아니므로 무시한다.)
    /// </summary>
    private void PopulateCapsuleMesh(VertexHelper vh)
    {
        if (points.Count == 0)
            return; // 점이 1개면 원 하나만 그린다 (1노드 패턴)

        float radius = lineWidth * 0.5f;

        if (outlineWidth > 0f)
            BuildCapsule(vh, radius + outlineWidth, BuildPointColors(outlineGradient, outlineColor));

        BuildCapsule(vh, radius, BuildPointColors(fillGradient, useMissColor ? missColor : normalColor));
    }

    /// <summary>
    /// 점마다의 색을 만든다. <see cref="useGradient"/>가 켜져 있으면 경로 진행 방향(첫 점 0 → 마지막 점 1)으로
    /// 그라데이션을 평가하고, 꺼져 있으면 단색으로 채운다. 페이드 알파는 어느 쪽이든 곱해진다.
    /// </summary>
    private Color32[] BuildPointColors(Gradient gradient, Color solidColor)
    {
        var colors = new Color32[points.Count];

        for (int i = 0; i < points.Count; i++)
        {
            Color color;
            if (useGradient && gradient != null)
            {
                float t = points.Count > 1 ? i / (float)(points.Count - 1) : 0f;
                color = gradient.Evaluate(t);
            }
            else
            {
                color = solidColor;
            }

            colors[i] = WithFadeAlpha(color);
        }

        return colors;
    }

    private Color32 WithFadeAlpha(Color color)
    {
        Color32 result = color;
        result.a = (byte)Mathf.RoundToInt(result.a * currentAlpha);
        return result;
    }

    /// <summary>
    /// 경로의 합집합(선분 + 원)을 겹침 없이 그린다.
    /// 선분은 중심에서 중심까지 온전히 그리고, 원은 그 선분들에 덮이지 않는 호(arc)만 그린다.
    /// 인접 점 P를 향하는 방향 u에 대해 선분 quad는 P 주위 원의 '(u 기준 ±90°) 반원'을 정확히 덮으므로,
    /// 그 범위를 제외한 각도 구간만 부채꼴로 채우면 겹침도 빈틈도 없다 (반투명에서 알파가 균일하게 유지됨).
    /// </summary>
    private void BuildCapsule(VertexHelper vh, float radius, Color32[] colors)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[i + 1];
            if ((b - a).sqrMagnitude <= Mathf.Epsilon) continue;

            AddSegmentQuad(vh, a, b, radius * 2f, colors[i], colors[i + 1]);
        }

        var incidentDirs = new List<Vector2>(2);
        var boundaryAngles = new List<float>(4);

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 center = points[i];

            incidentDirs.Clear();
            if (i > 0) AddIncidentDir(incidentDirs, points[i - 1] - center);
            if (i < points.Count - 1) AddIncidentDir(incidentDirs, points[i + 1] - center);

            Color32 color = colors[i];

            if (incidentDirs.Count == 0)
            {
                AddArc(vh, center, radius, 0f, Mathf.PI * 2f, capSegments, color); // 고립된 점(1노드 패턴) = 완전한 원
                continue;
            }

            // 각 선분이 덮는 반원의 경계각을 모아 정렬한 뒤, 덮이지 않은 구간만 호로 채운다.
            boundaryAngles.Clear();
            foreach (var dir in incidentDirs)
            {
                float baseAngle = Mathf.Atan2(dir.y, dir.x);
                boundaryAngles.Add(Normalize(baseAngle + Mathf.PI * 0.5f));
                boundaryAngles.Add(Normalize(baseAngle - Mathf.PI * 0.5f));
            }
            boundaryAngles.Sort();

            for (int b = 0; b < boundaryAngles.Count; b++)
            {
                float start = boundaryAngles[b];
                float end = b + 1 < boundaryAngles.Count ? boundaryAngles[b + 1] : boundaryAngles[0] + Mathf.PI * 2f;
                if (end - start <= 0.0001f) continue;

                float mid = (start + end) * 0.5f;
                Vector2 midDir = new Vector2(Mathf.Cos(mid), Mathf.Sin(mid));

                bool covered = false;
                foreach (var dir in incidentDirs)
                    if (Vector2.Dot(midDir, dir) > 0f) { covered = true; break; }

                if (!covered)
                    AddArc(vh, center, radius, start, end, capSegments, color);
            }
        }
    }

    /// <summary>길이 0이거나 이미 담긴 방향과 사실상 같은 방향은 무시한다.</summary>
    private static void AddIncidentDir(List<Vector2> dirs, Vector2 delta)
    {
        if (delta.sqrMagnitude <= Mathf.Epsilon) return;
        dirs.Add(delta.normalized);
    }

    private static float Normalize(float angle)
    {
        while (angle < 0f) angle += Mathf.PI * 2f;
        while (angle >= Mathf.PI * 2f) angle -= Mathf.PI * 2f;
        return angle;
    }

    /// <summary><paramref name="startAngle"/>~<paramref name="endAngle"/> 구간을 부채꼴(삼각형 팬)로 채운다.</summary>
    private static void AddArc(VertexHelper vh, Vector2 center, float radius, float startAngle, float endAngle, int fullCircleSegments, Color32 color)
    {
        float sweep = endAngle - startAngle;
        int segments = Mathf.Max(1, Mathf.CeilToInt(fullCircleSegments * sweep / (Mathf.PI * 2f)));

        int centerIndex = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);

        for (int i = 0; i <= segments; i++)
        {
            float angle = startAngle + sweep * i / segments;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vh.AddVert(center + offset, color, Vector2.zero);
        }

        for (int i = 0; i < segments; i++)
            vh.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 2 + i);
    }

    private static void AddSegmentQuad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 color)
        => AddSegmentQuad(vh, a, b, width, color, color);

    /// <summary>양 끝 색이 다르면 선분을 따라 정점 색이 보간되어 그라데이션이 된다.</summary>
    private static void AddSegmentQuad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 colorA, Color32 colorB)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x) * (width * 0.5f);

        int start = vh.currentVertCount;

        vh.AddVert(a - normal, colorA, Vector2.zero);
        vh.AddVert(a + normal, colorA, Vector2.zero);
        vh.AddVert(b + normal, colorB, Vector2.zero);
        vh.AddVert(b - normal, colorB, Vector2.zero);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
