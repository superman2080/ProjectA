using Coffee.UIExtensions;
using UnityEngine;

/// <summary>
/// 배경 앰비언트 파티클 한 인스턴스. 씬이 켜져 있는 동안 상시 루프로 재생되며,
/// <see cref="SetIntensity"/>(0~1)로 밀도(emission)와 색을 조정해 "상태 반응형" 연출을 표현한다.
/// intensity를 실제로 계산해 먹이는 주체(콤보/점수 등)는 추후 별도 플랜에서 연결한다 — 지금은 API만.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class AmbientEffectController : MonoBehaviour
{
    [Header("Emission (intensity 0 → 1)")]
    [SerializeField] private float minEmissionRate = 2f;
    [SerializeField] private float maxEmissionRate = 20f;

    [Header("Start Color (intensity 0 → 1)")]
    [SerializeField] private bool lerpColor = true;
    [SerializeField] private Color lowColor = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private Color highColor = new Color(1f, 1f, 1f, 1f);

    private UIParticle uiParticle;
    private ParticleSystem[] systems;

    [Range(0f, 1f)][SerializeField] private float intensity = 0f;

    void Awake()
    {
        uiParticle = GetComponent<UIParticle>();
        systems = GetComponentsInChildren<ParticleSystem>(true);
    }

    void OnEnable()
    {
        if (uiParticle != null)
            uiParticle.Play();
        else if (systems != null)
            foreach (var s in systems) s.Play();

        Apply(intensity);
    }

    /// <summary>앰비언트 강도를 0~1로 설정한다. 밀도(emission rate)와 색을 그에 맞게 보간한다.</summary>
    public void SetIntensity(float value)
    {
        intensity = Mathf.Clamp01(value);
        Apply(intensity);
    }

    void OnValidate()
    {
        if (Application.isPlaying && systems != null)
            Apply(intensity);
    }

    private void Apply(float t)
    {
        if (systems == null) return;

        float rate = Mathf.Lerp(minEmissionRate, maxEmissionRate, t);
        Color color = lerpColor ? Color.Lerp(lowColor, highColor, t) : Color.white;

        foreach (var s in systems)
        {
            if (s == null) continue;

            var emission = s.emission;
            emission.rateOverTime = rate;

            if (lerpColor)
            {
                var main = s.main;
                main.startColor = color;
            }
        }
    }
}
