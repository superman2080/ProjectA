using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 일시정지와 <b>포기</b>의 유일 관리 지점. <c>CameraDirector</c>·<c>HitStopDirector</c>와 같은 관례 —
/// 기존 참조만 들고 판정에 개입하지 않으며, 배선이 비면 조용히 비활성된다.
///
/// <para><b>왜 필요한가</b>: <see cref="ChartGen.StageMode.Loop"/>에서는 공격 패턴을 못 깨는 동안
/// 스테이지가 끝나지 않는다. <b>무한 재시도가 설계상 정상</b>이므로 플레이어가 나가는 수단이 필수다 —
/// 없으면 실력이 모자란 플레이어가 그 무대에 갇힌다.</para>
///
/// <para><b>⚠ <c>Time.timeScale = 0</c>과 <c>audioSource.Pause()</c>를 <b>같이</b> 건다.</b>
/// §7-3의 timeScale 금지 근거는 <i>"게임 시계만 느려지고 오디오는 안 느려져 차이가 영구 누적된다"</i>인데,
/// <b>오디오도 같이 멈추면 그 차이가 생기지 않는다</b>. §14가 "판정이 남아 있지 않다"로 예외를 얻은 것과
/// <b>다른 근거로</b> 얻는 예외이며, 그래서 둘 중 하나만 걸면 안 된다.</para>
///
/// <para><b>⚠ 마무리 실루엣 중에는 일시정지를 막는다.</b> §14가 이미 <c>timeScale = 0.1</c>을 걸어 둔
/// 상태라 해제가 그것을 1로 덮는다 — 복구 주체가 둘이 되는 문제를 <b>진입을 막아</b> 없앤다
/// (<c>FinaleSilhouetteDirector.IsBusy</c> 재사용, 새 상태가 0개다).</para>
/// </summary>
public class PauseDirector : MonoBehaviour
{
    [Tooltip("곡을 같이 멈춘다. 비우면 일시정지가 통째로 꺼진다 - 오디오만 흐르는 정지는 판정을 영구히 어긋나게 한다.")]
    [SerializeField] private ChartGen.ChartPlayer chartPlayer;

    [Tooltip("전투 씬 종료의 주인. 포기(Abandon)를 여기로 보낸다. 비우면 포기가 비활성된다.")]
    [SerializeField] private BattleSceneBootstrap bootstrap;

    [Tooltip("마무리 실루엣. 바쁘면 일시정지를 막는다(timeScale의 주인이 겹친다). 비우면 검사를 건너뛴다.")]
    [SerializeField] private FinaleSilhouetteDirector finale;

    [Tooltip("일시정지 중 띄우는 패널. 비우면 화면 표시 없이 정지만 걸린다.")]
    [SerializeField] private GameObject pausePanel;

    [Tooltip("일시정지 토글 키.")]
    [SerializeField] private Key toggleKey = Key.Escape;

    [Tooltip("일시정지 중에 누르면 기록 없이 무대를 떠난다.")]
    [SerializeField] private Key abandonKey = Key.Q;

    /// <summary>일시정지 상태가 바뀐 순간. UI·입력 차단이 구독할 확장 포인트이며 지금은 구독자가 0이다.</summary>
    public event System.Action<bool> OnPauseChanged;

    public bool IsPaused { get; private set; }

    private float restoreTimeScale = 1f;

    void OnDisable()
    {
        // ⚠ 반드시 되돌린다. 정지 중에 꺼지면 게임이 영구히 0배속으로 굳는다
        // (CameraDirector가 OnDisable에서 Brain을 되살리는 것과 같은 규율).
        if (IsPaused) Resume();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (IsPaused && abandonKey != Key.None && keyboard[abandonKey].wasPressedThisFrame)
        {
            Abandon();
            return;
        }

        if (toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame)
            Toggle();
    }

    public void Toggle()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        if (IsPaused || chartPlayer == null) return;

        // 실루엣이 timeScale의 주인인 동안에는 들어가지 않는다 — 해제가 그 값을 덮는다.
        if (finale != null && finale.IsBusy) return;

        IsPaused = true;
        restoreTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        chartPlayer.SongSource?.Pause();

        if (pausePanel != null) pausePanel.SetActive(true);
        OnPauseChanged?.Invoke(true);
    }

    public void Resume()
    {
        if (!IsPaused) return;

        IsPaused = false;
        Time.timeScale = restoreTimeScale;
        chartPlayer?.SongSource?.UnPause();

        if (pausePanel != null) pausePanel.SetActive(false);
        OnPauseChanged?.Invoke(false);
    }

    /// <summary>
    /// 이 무대를 떠난다. <b>아무것도 기록하지 않는다</b> — 목숨이 0일 때와 <b>같은 처리</b>다
    /// (§9: 중단에서 등급이 새어 나가면 <c>Fresh</c>가 <c>Retry</c>로 바뀐다).
    /// </summary>
    public void Abandon()
    {
        Resume();     // ⚠ timeScale을 먼저 되돌린다. 씬 전환이 0배속에서 시작하면 페이드가 멈춘다.
        bootstrap?.Abandon();
    }
}
