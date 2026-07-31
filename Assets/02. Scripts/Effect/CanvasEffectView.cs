using System;
using System.Collections;
using Coffee.UIExtensions;
using UnityEngine;

/// <summary>
/// Canvas 위 one-shot 파티클 이펙트 하나. 루트에 <see cref="UIParticle"/> + 하위 <see cref="ParticleSystem"/>(들)을 가진
/// 프리팹에 붙는다. 대여 → 위치 지정 → <see cref="OnSpawn"/>으로 재생, 파티클이 모두 소멸하면 스스로 <see cref="OnFinished"/>를
/// 발행해 매니저에 반환을 요청한다(<see cref="FocusRingView"/>의 OnArrived와 같은 패턴).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CanvasEffectView : MonoBehaviour, IPoolable
{
    [Tooltip("안전장치: 파티클이 어떤 이유로도 살아있다고 계속 보고할 때 이 시간이 지나면 강제 종료(초). 0 이하면 무제한.")]
    [SerializeField] private float maxLifetime = 8f;

    /// <summary>재생이 끝나 반환 가능해졌을 때 발행. 매니저가 구독해 풀로 되돌린다.</summary>
    public event Action<CanvasEffectView> OnFinished;

    /// <summary>이 뷰가 어느 프리팹 풀에서 나왔는지. 매니저가 반환 대상 큐를 찾는 데 쓴다.</summary>
    public GameObject SourcePrefab { get; set; }

    private RectTransform rectTransform;
    private UIParticle uiParticle;
    private ParticleSystem[] systems;
    private Coroutine lifeRoutine;

    void Awake()
    {
        rectTransform = (RectTransform)transform;
        uiParticle = GetComponent<UIParticle>();
        systems = GetComponentsInChildren<ParticleSystem>(true);
    }

    /// <summary>오버레이 레이어 로컬 좌표로 위치를 지정한다. 대여 콜백에서 OnSpawn 전에 호출.</summary>
    public void SetLocalPosition(Vector2 localPosition)
    {
        if (rectTransform == null) rectTransform = (RectTransform)transform;
        rectTransform.anchoredPosition = localPosition;
    }

    public void OnSpawn()
    {
        gameObject.SetActive(true);

        if (uiParticle != null)
            uiParticle.Play();
        else if (systems != null)
            foreach (var s in systems) s.Play();

        if (lifeRoutine != null)
            StopCoroutine(lifeRoutine);
        lifeRoutine = StartCoroutine(LifeRoutine());
    }

    public void OnDespawn()
    {
        if (lifeRoutine != null)
        {
            StopCoroutine(lifeRoutine);
            lifeRoutine = null;
        }

        if (uiParticle != null)
            uiParticle.Stop();
        if (systems != null)
            foreach (var s in systems)
                s.Clear(true);

        OnFinished = null;
        gameObject.SetActive(false);
    }

    /// <summary>파티클이 전부 소멸할 때까지 대기했다가 반환을 요청한다.</summary>
    private IEnumerator LifeRoutine()
    {
        float elapsed = 0f;
        // 최소 한 프레임은 지나야 방금 시작한 파티클이 IsAlive로 잡힌다.
        yield return null;

        while (AnyAlive())
        {
            if (maxLifetime > 0f)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= maxLifetime) break;
            }
            yield return null;
        }

        lifeRoutine = null;
        OnFinished?.Invoke(this);
    }

    private bool AnyAlive()
    {
        if (systems == null) return false;
        foreach (var s in systems)
            if (s != null && s.IsAlive(true))
                return true;
        return false;
    }
}
