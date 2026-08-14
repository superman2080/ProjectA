using ChartGen;
using UnityEngine;

/// <summary>씬 간 선택된 SongChart를 전달하는 DontDestroyOnLoad 싱글톤.</summary>
public class GameSession : Singleton<GameSession>
{
    /// <summary>SongSelectScene에서 선택한 채보. BattleScene의 ChartPlayer가 읽어 사용한다.</summary>
    public SongChart SelectedChart { get; set; }

    /// <summary>
    /// 마지막으로 완주한 곡의 채점 결과(<c>ScoreDirector</c>가 곡 종료에 쓴다).
    ///
    /// <para><b>⚠ 아직 읽는 쪽이 없다</b> — 결과 화면이 미구현이며, 그 화면이 붙을 진입점이 여기다.
    /// <c>PlayerHealth.OnDepleted</c>가 구독자 없이 서 있는 것과 같은 상태다(§7-6).</para>
    /// </summary>
    public ScoreSpace.ScoreResult LastResult { get; set; }

    protected override void Awake()
    {
        // 씬을 넘어와 이미 살아있는 인스턴스가 있으면(중복) 자신을 파기한다.
        // Instance 게터가 최초 1회 인스턴스를 캐시하므로, 먼저 깨어난 원본이 유지되고
        // 그 원본의 SelectedChart가 보존된다.
        if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroy = true; // base.Awake가 이 값을 보고 DontDestroyOnLoad 처리
        base.Awake();
    }
}
