using System;
using System.Collections;
using Coffee.UIExtensions;
using UnityEngine;

/// <summary>
/// one-shot 파티클 이펙트 하나. 대여 → 위치 지정 → <see cref="OnSpawn"/>으로 재생, 파티클이 모두 소멸하면 스스로
/// <see cref="OnFinished"/>를 발행해 매니저에 반환을 요청한다(<see cref="FocusRingView"/>의 OnArrived와 같은 패턴).
///
/// <para><b>Canvas와 월드를 겸한다.</b> Canvas 경로는 <see cref="UIParticle"/> + <see cref="SetLocalPosition"/>,
/// 월드 경로는 <see cref="SetWorldPose"/>다. 이름이 Canvas인 채로 남은 것은 <b>프리팹의 스크립트 참조가 클래스명에
/// 묶여 있어</b> rename이 기존 이펙트 프리팹을 통째로 깨기 때문이다 — 이름값보다 배선이 무겁다.</para>
/// </summary>
public class CanvasEffectView : MonoBehaviour, IPoolable
{
    [Tooltip("안전장치: 파티클이 어떤 이유로도 살아있다고 계속 보고할 때 이 시간이 지나면 강제 종료(초). 0 이하면 무제한.")]
    [SerializeField] private float maxLifetime = 8f;

    /// <summary>재생이 끝나 반환 가능해졌을 때 발행. 매니저가 구독해 풀로 되돌린다.</summary>
    public event Action<CanvasEffectView> OnFinished;

    /// <summary>이 뷰가 어느 프리팹 풀에서 나왔는지. 매니저가 반환 대상 큐를 찾는 데 쓴다.</summary>
    public GameObject SourcePrefab { get; set; }

    /// <summary>
    /// 지금 걸려 있는 재생 배속. <b>히트스톱이 0으로 얼렸다가 이 값으로 되돌린다</b> —
    /// 큐마다 배속이 다르므로 정지를 거는 쪽이 하나의 값으로 복원하면 안 된다.
    /// </summary>
    public float CurrentSpeed { get; private set; } = 1f;

    private RectTransform rectTransform;
    private UIParticle uiParticle;
    private ParticleSystem[] systems;
    private Coroutine lifeRoutine;

    // 풀 반납 시 되돌릴 기준값. 프리팹마다 다르므로 인스턴스가 스스로 기억한다.
    private Transform poolParent;
    private Vector3 defaultScale = Vector3.one;
    private float defaultSpeed = 1f;
    private bool defaultsCaptured;

    void Awake()
    {
        rectTransform = transform as RectTransform;
        uiParticle = GetComponent<UIParticle>();
        systems = GetComponentsInChildren<ParticleSystem>(true);
        CaptureDefaults();
    }

    private void CaptureDefaults()
    {
        if (defaultsCaptured) return;

        defaultScale = transform.localScale;
        defaultSpeed = systems != null && systems.Length > 0 && systems[0] != null
            ? systems[0].main.simulationSpeed
            : 1f;
        CurrentSpeed = defaultSpeed;
        defaultsCaptured = true;
    }

    /// <summary>풀이 자기 보관 부모를 알려 준다. 따라가기(<c>follow</c>) 이펙트를 회수할 때 여기로 되돌린다.</summary>
    public void SetPoolParent(Transform parent) => poolParent = parent;

    /// <summary>오버레이 레이어 로컬 좌표로 위치를 지정한다. 대여 콜백에서 OnSpawn 전에 호출.</summary>
    public void SetLocalPosition(Vector2 localPosition)
    {
        if (rectTransform == null) rectTransform = transform as RectTransform;
        if (rectTransform != null) rectTransform.anchoredPosition = localPosition;
        else transform.localPosition = localPosition;
    }

    /// <summary>
    /// 월드 앵커 기준으로 포즈를 잡는다.
    ///
    /// <para><paramref name="follow"/>면 앵커의 <b>자식으로 붙어 따라간다</b>(칼날 잔상).
    /// 아니면 발사 순간의 포즈만 복사해 월드에 남는다(스파크) — 이 경우 앵커가 사라져도 이펙트는 살아 있다.</para>
    /// </summary>
    public void SetWorldPose(Transform anchor, bool follow, Vector3 localPosition, Vector3 localEuler, float scale)
    {
        CaptureDefaults();

        if (anchor == null) return;

        transform.SetParent(follow ? anchor : null, false);

        if (follow)
        {
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.Euler(localEuler);
        }
        else
        {
            transform.SetPositionAndRotation(
                anchor.TransformPoint(localPosition),
                anchor.rotation * Quaternion.Euler(localEuler));
        }

        transform.localScale = defaultScale * scale;
    }

    /// <summary>
    /// 파티클 재생 배속. <c>MainModule</c>은 <b>구조체 사본</b>이라 되대입하지 않으면 조용히 무시된다.
    /// 0을 주면 얼어붙는다(히트스톱이 그 값을 쓴다).
    /// </summary>
    public void SetSpeed(float speed)
    {
        CurrentSpeed = speed;
        ApplySpeed(speed);
    }

    /// <summary>정지 창 동안만 배속을 덮는다. <see cref="CurrentSpeed"/>는 건드리지 않아 복귀값이 남는다.</summary>
    public void OverrideSpeed(float speed) => ApplySpeed(speed);

    /// <summary>정지 해제 — 자기 배속으로 되돌아간다.</summary>
    public void RestoreSpeed() => ApplySpeed(CurrentSpeed);

    private void ApplySpeed(float speed)
    {
        if (systems == null) return;

        foreach (var s in systems)
        {
            if (s == null) continue;
            var main = s.main;                 // 구조체 사본
            main.simulationSpeed = speed;      // 되대입해야 반영된다
        }
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

        // 풀에서 재사용되므로 기준값으로 되돌린다 — 안 되돌리면 다음 큐가 이전 큐의 배속·크기를 물려받는다.
        transform.localScale = defaultScale;
        SetSpeed(defaultSpeed);

        // 따라가기 이펙트는 앵커의 자식으로 붙어 있다. 부모를 안 되돌리면 앵커가 죽을 때 같이 파괴된다.
        if (poolParent != null && transform.parent != poolParent)
            transform.SetParent(poolParent, false);

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
