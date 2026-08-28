using ChartGen;
using ScoreSpace;
using UnityEngine;

/// <summary>무대 하나가 지금 어떤 상태인가(CLAUDE.md 개편안 §0-6).</summary>
public enum EncounterState
{
    /// <summary>아직 해금되지 않았다. 들어서도 아무 일이 없고 일렁이지도 않는다.</summary>
    Locked,

    /// <summary>해금됐고 아직 한 번도 완곡하지 않았다. <b>들어서면 곧바로 시작된다.</b></summary>
    Fresh,

    /// <summary>완곡했지만 최고 등급이 아니다. 상호작용하면 다시 시작할 수 있다.</summary>
    Retry,

    /// <summary>최고 등급으로 끝냈다. 조용하다.</summary>
    Done,
}

/// <summary>
/// <b>곡이 딸린 자리.</b> 곡은 고르는 것이 아니라 장소에 딸린다 — 무대에 들어서면 그 자리에서 시작된다.
///
/// <para><b>상태를 저장하지 않는다</b>(<see cref="State"/>는 게터다). <see cref="GameProgress"/>의
/// 플래그·등급에서 매번 파생시키므로 <b>등급과 어긋난 상태가 표현 불가능</b>하다.</para>
///
/// <para><b>⚠ 프롬프트는 <see cref="EncounterState.Retry"/>에서만 뜬다.</b> 처음 도전에도 뜨면
/// "아직 남았다"는 말이 뜻을 잃는다. 그리고 이것이 CLAUDE.md §13 프롬프트 금지의
/// <b>유일한 예외</b>다 — 배경 도상과 「닫힌 문」에는 여전히 아무것도 안 붙는다.</para>
/// </summary>
[RequireComponent(typeof(Collider))]
public class Encounter : MonoBehaviour
{
    [Header("Chart")]
    [Tooltip("이 자리에서 시작되는 곡.")]
    [SerializeField] private SongChart chart;

    [Tooltip("이 자리의 식별자. 등급 기록의 키다 - 비우면 아무것도 기록되지 않는다(자유 연주와 같은 취급).")]
    [SerializeField] private string encounterId;

    [Tooltip("배경 씬(StageBackground_Stage{N}) 선택에 쓴다.")]
    [SerializeField] private int stageIndex = 1;

    [Tooltip("이 무대의 적 수. 0이면 EnemyDirector의 씬 값을 그대로 쓴다. 튜토리얼과 4-b가 쓴다.")]
    [SerializeField] private int clusterSizeOverride;

    [Header("Progress")]
    [Tooltip("이 플래그가 서야 열린다. 비우면 처음부터 열려 있다(튜토리얼·자유 연주).")]
    [SerializeField] private string unlockFlag;

    [Tooltip("완곡하면 서는 플래그. 다음 무대의 unlockFlag나 시퀀스의 WaitFlagStep이 이것을 읽는다.")]
    [SerializeField] private string completeFlag;

    [Header("Presentation")]
    [Tooltip("Retry 상태에서 다가서면 뜨는 한 줄. 기능 안내가 아니라 대사여야 한다.")]
    [TextArea(2, 4)]
    [SerializeField] private string retryText = "아직 전부 처리하지 않았어";

    [Tooltip("아직 볼일이 남은 자리의 일렁임. Fresh와 Retry에서만 켜진다.")]
    [SerializeField] private GameObject shimmerVfx;

    public SongChart Chart => chart;
    public string EncounterId => encounterId;
    public int StageIndex => stageIndex;
    public int ClusterSizeOverride => clusterSizeOverride;
    public string CompleteFlag => completeFlag;
    public string RetryText => retryText;

    /// <summary>
    /// 지금 상태. <b>해금 → 완곡 → 최고 등급</b> 순서로만 갈린다.
    ///
    /// <para><b>⚠ <c>Fresh</c>와 <c>Retry</c>를 가르는 것은 등급이 아니라 "한 번이라도 완곡했는가"다.</b>
    /// 목숨이 0이 되어 중단된 곡은 아무것도 기록하지 않으므로 그 무대는 <c>Fresh</c>로 남는다.</para>
    /// </summary>
    public EncounterState State
    {
        get
        {
            if (!GameProgress.HasFlag(unlockFlag)) return EncounterState.Locked;
            if (!GameProgress.TryGetGrade(encounterId, out ScoreGrade grade)) return EncounterState.Fresh;

            return grade >= ScoreGrade.SSS ? EncounterState.Done : EncounterState.Retry;
        }
    }

    /// <summary>아직 볼일이 남았는가. 일렁임과 프롬프트가 이것을 본다.</summary>
    public bool HasBusiness
    {
        get
        {
            EncounterState state = State;
            return state == EncounterState.Fresh || state == EncounterState.Retry;
        }
    }

    private void Start()
    {
        if (chart == null)
            Debug.LogError($"[Encounter] '{name}'에 SongChart가 없습니다.", this);

        ApplyShimmer();
    }

    /// <summary>완곡 뒤 되돌아왔을 때 <c>EncounterDirector</c>가 부른다.</summary>
    public void ApplyShimmer()
    {
        if (shimmerVfx != null) shimmerVfx.SetActive(HasBusiness);
    }

    // 트리거 판정은 컴포넌트로 한다. 태그를 새로 만들면 프리팹마다 설정이 하나 늘고,
    // 빠뜨렸을 때 증상이 "그 무대만 안 열린다"라 원인을 짚기 어렵다.
    private static bool IsPlayer(Collider other)
        => other.GetComponentInParent<PlayerExploreMover>() != null;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        EncounterDirector.Instance?.HandleEnter(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        EncounterDirector.Instance?.HandleExit(this);
    }

    private void Reset()
    {
        var collider = GetComponent<Collider>();
        if (collider != null) collider.isTrigger = true;
    }
}
