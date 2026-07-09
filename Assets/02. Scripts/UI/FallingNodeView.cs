using System;
using System.Collections;
using PatternSpace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class FallingNodeView : MonoBehaviour, IPoolable
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text indexLabel;

    public int PointIndex { get; private set; }
    public NodeType Type { get; private set; }

    public event Action<FallingNodeView> OnArrived;

    private RectTransform rectTransform;
    private Coroutine fallRoutine;

    private Vector2 pendingTarget;
    private float pendingSpawnPositionY;
    private float pendingFallDuration;

    void Awake()
    {
        rectTransform = (RectTransform)transform;
    }

    /// <summary>풀에서 대여 직후(비활성 상태에서도) 호출 — 시각 요소만 채우고 낙하는 시작하지 않는다.</summary>
    public void Initialize(int displayIndex, Color color, NodeType type, Vector2 targetLocalPos, float spawnPositionY, float fallDuration)
    {
        PointIndex = displayIndex - 1;
        Type = type;

        if (background != null)
            background.color = color;
        if (indexLabel != null)
            indexLabel.text = displayIndex.ToString();

        pendingTarget = targetLocalPos;
        pendingSpawnPositionY = spawnPositionY;
        pendingFallDuration = fallDuration;
    }

    public void OnSpawn()
    {
        gameObject.SetActive(true);

        if (fallRoutine != null)
            StopCoroutine(fallRoutine);
        fallRoutine = StartCoroutine(FallRoutine(pendingTarget, pendingSpawnPositionY, pendingFallDuration));
    }

    public void OnDespawn()
    {
        StopFalling();
        OnArrived = null;
        gameObject.SetActive(false);
    }

    private IEnumerator FallRoutine(Vector2 targetLocalPos, float spawnPositionY, float fallDuration)
    {
        Vector2 startPos = new Vector2(targetLocalPos.x, spawnPositionY);
        rectTransform.anchoredPosition = startPos;

        float t = 0f;
        while (t < fallDuration)
        {
            t += Time.deltaTime;
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, targetLocalPos, fallDuration <= 0f ? 1f : t / fallDuration);
            yield return null;
        }

        rectTransform.anchoredPosition = targetLocalPos;
        fallRoutine = null;
        OnArrived?.Invoke(this);
    }

    public void StopFalling()
    {
        if (fallRoutine != null)
        {
            StopCoroutine(fallRoutine);
            fallRoutine = null;
        }
    }
}
