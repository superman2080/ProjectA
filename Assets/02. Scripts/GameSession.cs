using ChartGen;
using UnityEngine;

/// <summary>씬 간 선택된 SongChart를 전달하는 DontDestroyOnLoad 싱글톤.</summary>
public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    /// <summary>SongSelectScene에서 선택한 채보. DefaultScene의 ChartPlayer가 읽어 사용한다.</summary>
    public SongChart SelectedChart { get; set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
