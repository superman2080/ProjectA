using ChartGen;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>곡 선택 씬에서 SongChart를 선택하고 게임플레이 씬으로 전환한다.</summary>
public class SongSelectManager : MonoBehaviour
{
    [SerializeField] private SongChart[] availableCharts;
    [SerializeField] private string gameplaySceneName = "BattleScene";

    /// <summary>버튼 OnClick 이벤트에서 호출. 선택한 채보를 GameSession에 등록하고 게임플레이 씬으로 전환한다.</summary>
    public void SelectChart(SongChart chart)
    {
        // SongSelectScene에서 직접 진입 시 GameSession이 없을 수 있으므로 안전하게 생성
        if (GameSession.Instance == null)
        {
            var go = new GameObject("GameSession");
            go.AddComponent<GameSession>();
        }

        GameSession.Instance.SelectedChart = chart;
        SceneManager.LoadScene(gameplaySceneName);
    }
}
