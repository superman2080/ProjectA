using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// <b>탐색 씬에서 전투로 넘어가는 유일한 지점.</b> 무대에 들어선 사건을 받아
/// 복귀 정보를 <see cref="GameSession"/>에 적고 전투 씬을 로드한다.
///
/// <para><b>새 파이프라인이 아니다</b> — <c>SongSelectManager</c>가 이미
/// <c>SelectedChart</c>를 꽂고 씬을 로드한다. 여기는 그 경로에 "어디로 돌아갈 것인가"만 얹는다.</para>
///
/// <para><b>왜 정적 <see cref="Instance"/>인가</b>: <see cref="Encounter"/>가 트리거 콜백을 이쪽으로
/// 넘겨야 하는데 자리마다 참조를 배선하면 하나 빠뜨렸을 때 그 무대만 조용히 안 열린다.
/// <c>Singleton{T}</c>를 안 쓰는 이유는 <c>DialogUI</c>와 같다 — 없을 때 껍데기를 만들어 내느니 null이 낫다.</para>
/// </summary>
public class EncounterDirector : MonoBehaviour
{
    /// <summary>이 씬의 디렉터. 없으면 <c>null</c>.</summary>
    public static EncounterDirector Instance { get; private set; }

    [Tooltip("플레이어 루트. 복귀 좌표를 여기서 읽는다.")]
    [SerializeField] private Transform player;

    [SerializeField] private PlayerModeDirector modeDirector;
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("전투 씬 이름.")]
    [SerializeField] private string battleSceneName = "BattleScene";

    // 지금 프롬프트를 띄우고 있는 자리(Retry). 없으면 null.
    private Encounter promptTarget;

    // 방금 이 자리에서 돌아왔다. 한 번 벗어나기 전까지는 다시 열지 않는다.
    private Encounter suppressed;

    private bool leaving;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (inputHandler != null) inputHandler.OnInteractPressed += HandleInteract;
    }

    void OnDisable()
    {
        if (inputHandler != null) inputHandler.OnInteractPressed -= HandleInteract;

        // 내리는 경로 둘 중 하나. 빠지면 대사가 화면에 굳는다.
        ClearPrompt();
    }

    /// <summary>
    /// 복귀 직후, 플레이어가 이미 그 트리거 안에 서 있는 자리를 잠근다.
    /// <b>이것이 없으면 되돌아오자마자 같은 곡이 다시 시작되는 무한 루프가 된다.</b>
    /// </summary>
    public void SuppressUntilExit(Encounter encounter) => suppressed = encounter;

    /// <summary><see cref="Encounter"/>의 트리거가 부른다.</summary>
    public void HandleEnter(Encounter encounter)
    {
        if (leaving || encounter == null || encounter == suppressed) return;

        switch (encounter.State)
        {
            case EncounterState.Fresh:
                // 처음은 들어서면 시작된다. 확인 창을 두면 그것이 곧 "스테이지 선언"이 된다.
                StartCoroutine(EnterRoutine(encounter));
                break;

            case EncounterState.Retry:
                promptTarget = encounter;
                DialogUI.Instance?.ShowPrompt(encounter.RetryText);
                break;
        }
    }

    /// <summary><see cref="Encounter"/>의 트리거가 부른다.</summary>
    public void HandleExit(Encounter encounter)
    {
        if (encounter == suppressed) suppressed = null;
        if (encounter == promptTarget) ClearPrompt();
    }

    private void HandleInteract()
    {
        if (leaving || promptTarget == null) return;

        Encounter target = promptTarget;
        ClearPrompt();
        StartCoroutine(EnterRoutine(target));
    }

    private void ClearPrompt()
    {
        if (promptTarget == null) return;

        promptTarget = null;
        DialogUI.Instance?.HidePrompt();
    }

    private IEnumerator EnterRoutine(Encounter encounter)
    {
        if (encounter.Chart == null)
        {
            Debug.LogError($"[EncounterDirector] '{encounter.name}'에 SongChart가 없어 진입할 수 없습니다.", encounter);
            yield break;
        }

        leaving = true;

        // 페이드 동안에는 아무도 플레이어를 움직이면 안 된다.
        modeDirector?.SetMode(PlayerMode.Overlay);

        WriteReturnInfo(encounter);

        ScreenFader fader = ScreenFader.Instance;
        if (fader != null) yield return fader.FadeOut();

        SceneManager.LoadScene(battleSceneName);
    }

    /// <summary>
    /// 복귀 정보를 적는다. <b>⚠ 좌표는 트리거가 아니라 플레이어의 지금 위치다</b> —
    /// 트리거 위치를 쓰면 매번 같은 자리에서 되살아나 "그 자리에 선 채 이어진다"가 깨진다.
    /// </summary>
    private void WriteReturnInfo(Encounter encounter)
    {
        if (GameSession.Instance == null)
        {
            Debug.LogError("[EncounterDirector] GameSession이 없습니다 - Managers 프리팹이 씬에 있는지 확인하세요.", this);
            return;
        }

        GameSession session = GameSession.Instance;

        session.SelectedChart = encounter.Chart;
        session.EncounterId = encounter.EncounterId;
        session.CompleteFlag = encounter.CompleteFlag;
        session.StageIndex = encounter.StageIndex;
        session.ClusterSizeOverride = encounter.ClusterSizeOverride;

        session.ReturnScene = gameObject.scene.name;
        session.ReturnPosition = player != null ? player.position : Vector3.zero;
        session.ReturnYaw = player != null ? player.eulerAngles.y : 0f;
    }
}
