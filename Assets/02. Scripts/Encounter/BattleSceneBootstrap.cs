using System.Collections;
using ChartGen;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 씬이 열릴 때 배경을 얹고 곡을 시작하며, 끝나면 탐색 씬으로 되돌린다.
///
/// <para><b>기록 분기는 둘뿐이다</b> — 완곡(등급 + 완곡 플래그)과 중단(아무것도 안 남긴다).
/// ⚠ 중단에서 등급이 새어 나가면 <b>죽은 곡의 낮은 등급이 박혀</b> 그 무대가
/// <c>Fresh</c>에서 <c>Retry</c>로 바뀐다 — 다음 진입이 곧바로 시작되지 않고 상호작용을 요구하게 된다.</para>
/// </summary>
public class BattleSceneBootstrap : MonoBehaviour
{
    [SerializeField] private ChartPlayer chartPlayer;
    [SerializeField] private EnemySpace.EnemyDirector enemyDirector;
    [SerializeField] private PlayerHealth playerHealth;

    [Tooltip("비우면 마무리 실루엣 없이 OnSongEnded만 듣는다.")]
    [SerializeField] private FinaleSilhouetteDirector finale;

    [Tooltip("배경 씬 이름 접두사. StageIndex가 뒤에 붙는다.")]
    [SerializeField] private string stageBackgroundPrefix = "StageBackground_Stage";

    [Tooltip("복귀 정보가 없을 때(자유 연주) 돌아갈 씬.")]
    [SerializeField] private string freePlayReturnScene = "SongSelectScene";

    // 완곡과 중단 중 먼저 온 것 하나만 받는다.
    private bool resolved;

    private IEnumerator Start()
    {
        ScreenFader fader = ScreenFader.Instance;
        fader?.CoverInstantly();

        yield return LoadStageBackground();

        ApplyClusterOverride();

        Subscribe();

        // ⚠ 걷어낸 다음에 시작한다. 프리웜(PrepareStage)이 도는 창에 씬 로드를 겹치면
        // 그 히치가 그대로 판정 손실이 된다.
        if (fader != null) yield return fader.FadeIn();

        chartPlayer?.Play();
    }

    void OnDestroy() => Unsubscribe();

    private IEnumerator LoadStageBackground()
    {
        int index = GameSession.Instance != null ? GameSession.Instance.StageIndex : 0;
        if (index <= 0) yield break;

        string sceneName = stageBackgroundPrefix + index;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"[BattleSceneBootstrap] 배경 씬 '{sceneName}'을 찾을 수 없습니다 - " +
                             "Build Settings에 등록됐는지 확인하세요. 배경 없이 진행합니다.", this);
            yield break;
        }

        yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
    }

    private void ApplyClusterOverride()
    {
        if (enemyDirector == null || GameSession.Instance == null) return;

        int size = GameSession.Instance.ClusterSizeOverride;
        if (size > 0) enemyDirector.SetClusterSize(size);
    }

    private void Subscribe()
    {
        if (chartPlayer != null) chartPlayer.OnSongEnded += HandleSongEnded;
        if (finale != null) finale.OnFinaleEnded += HandleSongEnded;
        if (playerHealth != null) playerHealth.OnDepleted += HandleDepleted;
    }

    private void Unsubscribe()
    {
        if (chartPlayer != null) chartPlayer.OnSongEnded -= HandleSongEnded;
        if (finale != null) finale.OnFinaleEnded -= HandleSongEnded;
        if (playerHealth != null) playerHealth.OnDepleted -= HandleDepleted;
    }

    /// <summary>완곡. 등급을 남기고 완곡 플래그를 세운다 — 그 한 줄이 "완곡하면 다음으로 간다"의 전부다.</summary>
    private void HandleSongEnded()
    {
        if (resolved) return;
        resolved = true;

        GameSession session = GameSession.Instance;
        if (session != null && !string.IsNullOrEmpty(session.EncounterId))
        {
            GameProgress.ReportGrade(session.EncounterId, session.LastResult.Grade);
            GameProgress.SetFlag(session.CompleteFlag);
        }

        StartCoroutine(ReturnRoutine());
    }

    /// <summary>목숨이 0. <b>아무것도 기록하지 않는다</b> — 그 무대는 아직 완곡한 적이 없는 자리다.</summary>
    private void HandleDepleted()
    {
        if (resolved) return;
        resolved = true;

        chartPlayer?.Stop();
        StartCoroutine(ReturnRoutine());
    }

    private IEnumerator ReturnRoutine()
    {
        Unsubscribe();

        ScreenFader fader = ScreenFader.Instance;
        if (fader != null) yield return fader.FadeOut();

        GameSession session = GameSession.Instance;
        string scene = session != null && !string.IsNullOrEmpty(session.ReturnScene)
            ? session.ReturnScene
            : freePlayReturnScene;

        SceneManager.LoadScene(scene);
    }
}
