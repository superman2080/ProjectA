using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 콤보 단계에 따라 화면 포스트 이펙트를 강화한다(붉은 외곽 + 약한 색수차).
/// <see cref="ScoreDirector.OnComboTierChanged"/>만 구독하는 순수 연출.
///
/// <para><b>이 클래스가 미는 것은 <c>Volume.weight</c> 하나뿐이다.</b> URP는 프로파일 안의 오버라이드를
/// 전부 같은 weight로 보간하므로 <b>효과를 추가하는 일 = 프로파일에 오버라이드 한 줄 추가</b>다
/// (<c>EffectCatalog</c>·<c>CameraCueCatalog</c>의 "연출 추가 = 카탈로그 한 줄"과 같은 결).
/// 이름이 <c>ComboVignette</c>가 아닌 이유가 이것이다.</para>
///
/// <para><b>⚠ 프로파일을 코드가 수정하지 않는다.</b> <c>sharedProfile</c>을 건드리면 에디터에서
/// <b>에셋에 그대로 저장된다</b>. weight만 밀면 그 부류가 원천 소멸하고,
/// 색·강도·부드러움은 전부 프로파일에서 잡는다.</para>
///
/// <para><b>⚠ 기존 <c>Global Volume</c>을 건드리지 않는다.</b> 우선순위가 높은 이 Volume 쪽으로
/// weight만큼 보간되므로 <c>weight = 0</c>이면 평소 화면이 1픽셀도 안 바뀐다 —
/// 콤보 시스템을 꺼도 원래 룩이 그대로 돌아온다.</para>
/// </summary>
public class ComboPostFxView : MonoBehaviour
{
    [SerializeField] private ScoreDirector director;

    [Tooltip("콤보 전용 Volume. Priority가 Global Volume보다 높아야 하고 Weight는 0으로 시작한다.\n" +
             "비우면 이 층만 조용히 죽는다.")]
    [SerializeField] private Volume volume;

    [Tooltip("단계별 목표 weight. 배열 길이는 ScoreDirector의 콤보 문턱 수 + 1이어야 한다(0단계 포함).\n" +
             "단계가 배열을 넘어가면 마지막 값으로 클램프한다.")]
    [SerializeField] private float[] tierWeights = { 0f, 0.35f, 0.65f, 1f };

    [Tooltip("올라갈 때 목표 weight로 붙는 시간(초).\n" +
             "⚠ 내려갈 때는 이 값을 쓰지 않는다 — 콤보 브레이크는 사건이라 즉발이어야 한다.")]
    [SerializeField] private float riseDamp = 0.25f;

    private float target;

    void OnEnable()
    {
        if (director != null) director.OnComboTierChanged += HandleTierChanged;

        // 도중에 켜져도 현재 단계를 반영한다(꺼진 사이에 콤보가 올랐을 수 있다).
        HandleTierChanged(director != null ? director.ComboTier : 0);
        Apply(target);
    }

    void OnDisable()
    {
        if (director != null) director.OnComboTierChanged -= HandleTierChanged;

        // ⚠ 꺼질 때 반드시 되돌린다. 안 그러면 화면이 붉게 물든 채 굳는다
        // (CameraDirector가 OnDisable에서 Brain을 되살리는 것과 같은 규율).
        Apply(0f);
    }

    private void HandleTierChanged(int tier)
    {
        target = WeightOf(tier);

        // ⚠ 내려가는 방향은 즉시 반영한다. 감쇠로 사라지면 "서서히 식는다"로 읽혀
        // 콤보가 끊겼다는 사실 자체가 화면에서 안 보인다.
        if (volume != null && target < volume.weight) Apply(target);
    }

    private float WeightOf(int tier)
    {
        if (tierWeights == null || tierWeights.Length == 0) return 0f;

        return tierWeights[Mathf.Clamp(tier, 0, tierWeights.Length - 1)];
    }

    private void Apply(float weight)
    {
        if (volume == null) return;
        volume.weight = Mathf.Clamp01(weight);
    }

    void Update()
    {
        if (volume == null || Mathf.Approximately(volume.weight, target)) return;

        // 지수 감쇠 — 프레임률과 무관하게 같은 속도로 붙는다.
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(riseDamp, 0.01f));
        Apply(Mathf.Lerp(volume.weight, target, k));
    }
}
