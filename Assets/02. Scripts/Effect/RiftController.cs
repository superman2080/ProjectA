using UnityEngine;

/// <summary>
/// 균열 하나의 연출. <b>Cracks 셰이더의 두 값만 민다</b> — 노이즈 좌표(무늬가 흐른다)와
/// 발광 세기(음악의 템포로 맥박한다).
///
/// <para><b>순수 연출이다</b>(<c>CameraDirector</c>·<c>FinaleSilhouetteDirector</c>와 같은 관례).
/// 판정에 개입하지 않고, 배선이 비면 조용히 비활성된다.</para>
///
/// <para><b>켜고 끄는 일을 하지 않는다.</b> 균열이 보일지 말지는 <c>Encounter.shimmerVfx</c>가
/// <c>SetActive</c>로 정한다(CLAUDE.md 9의 상태 표 — <c>Locked</c>/<c>Done</c>에서 조용해진다).
/// 자식 파티클도 부모가 꺼지면 같이 꺼지므로 <b>이 클래스는 파티클 참조조차 들지 않는다</b>.</para>
///
/// <para><b>⚠ 머티리얼 인스턴스를 쓴다.</b> <c>Cracks Material</c>은 에셋 하나를 공유하므로
/// <c>sharedMaterial</c>을 만지면 <b>에디터에서 그 에셋이 실제로 수정돼 저장된다</b>
/// (CLAUDE.md 12의 "프로파일을 코드가 수정하지 않는다"와 같은 함정).</para>
///
/// <para><b>한 오브젝트에 하나만 붙인다.</b> 앞면·뒷면처럼 렌더러가 여럿이면
/// <c>additionalRenderers</c>에 간다 — 그들은 <b>머티리얼 인스턴스 하나를 공유</b>하므로
/// 무늬와 발광이 어긋날 여지가 없다. 렌더러마다 이 컴포넌트를 붙이면
/// 인스턴스가 둘이 되어 값이 갈릴 수 있다.</para>
///
/// <para><b>⚠ 맥박의 시계는 <c>Time.time</c>이고, 오디오 시각이 아니다.</b>
/// <see cref="Mathf.PingPong"/>으로 만드는 맥박에는 기준점(다운비트)이 없어 화면에서 관측 가능한
/// 사실이 <b>주기</b>뿐이다 — 오디오 시각을 써서 얻을 것이 없고, 루프 배경음에서는
/// <c>AudioSource.time</c>이 한 바퀴마다 0으로 되돌아가 <b>발광이 루프 경계마다 튄다</b>.</para>
/// </summary>
public class RiftController : MonoBehaviour
{
    private static readonly int NoiseOffsetId = Shader.PropertyToID("_NoiseOffset");
    private static readonly int EmissionId = Shader.PropertyToID("_Emission");

    [Tooltip("Cracks 머티리얼이 붙은 렌더러. 비우면 같은 오브젝트의 것을 쓴다.")]
    [SerializeField] private MeshRenderer targetRenderer;

    [Tooltip("같은 균열을 함께 보여 주는 다른 렌더러(뒷면 등). 여기 꽂힌 렌더러는 " +
             "targetRenderer와 머티리얼 인스턴스를 공유하므로 자기 RiftController가 필요 없다.")]
    [SerializeField] private MeshRenderer[] additionalRenderers;

    [Header("Noise")]
    [Tooltip("노이즈 좌표(_NoiseOffset)의 초당 증가량. 무늬가 흐르는 속도다.")]
    [SerializeField] private Vector2 noiseScrollSpeed = new Vector2(0.05f, 0.05f);

    [Header("Emission")]
    [Tooltip("발광 세기(_Emission)가 가장 밝을 때의 값. 0(무발광)과 이 값 사이를 왕복한다.")]
    [Range(0f, 4f)]
    [SerializeField] private float maxEmission = 2f;

    [Tooltip("몇 비트에 한 번 왕복하는가(어두움 -> 밝음 -> 어두움). 1이면 매 비트.")]
    [Min(0.01f)]
    [SerializeField] private float beatsPerPulse = 1f;

    [Header("Tempo")]
    [Tooltip("템포를 읽어 올 음악. 씬의 ChartPlayer든 MusicPlayer든 이 칸에 들어간다. " +
             "비우면 fallbackBpm으로 돈다.\n" +
             "주의: 자동으로 찾지 않는다 - 재생하지 않는 ChartPlayer가 살아 있는 씬에서는 " +
             "들리지 않는 곡의 BPM을 집게 된다.")]
    [SerializeField] private MusicPlayerBase musicSource;

    [Tooltip("musicSource가 비었거나 템포를 모를 때(Bpm이 0 이하) 쓸 값.")]
    [Min(1f)]
    [SerializeField] private float fallbackBpm = 120f;

    private Material material;
    private Vector2 noiseOffset;

    /// <summary>지금 맥박의 기준이 되는 템포. <see cref="MusicPlayerBase.Bpm"/>의 0은 "모른다"는 뜻이다.</summary>
    private float Bpm => musicSource != null && musicSource.Bpm > 0f ? musicSource.Bpm : fallbackBpm;

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<MeshRenderer>();

        if (targetRenderer == null)
        {
            Debug.LogError("[RiftController] MeshRenderer가 없습니다. targetRenderer를 배선하세요.", this);
            enabled = false;
            return;
        }

        // 공유 에셋이 아니라 인스턴스를 쓴다(위 주석 참조).
        material = targetRenderer.material;

        // 같은 인스턴스를 다른 렌더러에도 물려 준다. 인스턴스가 하나뿐이라
        // 무늬와 발광이 갈릴 수가 없다 - 뒷면에 RiftController를 하나 더 붙이면
        // 컨트롤러마다 인스턴스가 따로 생겨 값이 어긋날 여지가 생긴다.
        if (additionalRenderers == null)
            return;

        foreach (MeshRenderer extra in additionalRenderers)
        {
            if (extra != null)
                extra.material = material;
        }
    }

    private void Update()
    {
        noiseOffset += noiseScrollSpeed * Time.deltaTime;
        material.SetVector(NoiseOffsetId, noiseOffset);

        // PingPong(x, 1)의 주기는 x로 2다. 그래서 k = 2 x 초당비트 / 왕복당비트면
        // 실시간 주기가 정확히 (beatsPerPulse x 한 비트)가 된다.
        float k = 2f * (Bpm / 60f) / beatsPerPulse;

        material.SetFloat(EmissionId, Mathf.PingPong(Time.time * k, 1f) * maxEmission);
    }

    private void OnDisable()
    {
        // 꺼진 순간의 밝기로 굳지 않게 되돌린다(ComboPostFxView의 규율과 같은 결).
        if (material != null)
            material.SetFloat(EmissionId, 0f);
    }
}
