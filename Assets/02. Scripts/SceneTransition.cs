using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 씬 전환의 유일 관리 지점. <b>검은 화면 사이에서만 씬이 바뀐다</b>는 규칙이 여기 한 곳에 있다 —
/// 덮기(<see cref="ScreenFader"/>) · 비동기 로드 · 최소 지속시간 · 로딩 링이 전부 한 코루틴이다.
///
/// <para><b>왜 <see cref="ScreenFader"/>에 넣지 않았나</b>: 그 클래스의 규율이 "여기는 알파만 민다"이기 때문.
/// 무엇을 덮을지는 부르는 쪽이 정하고, 그 <i>부르는 쪽</i>이 이 클래스다.</para>
///
/// <para><b>⚠ 페이드 인을 하지 않는다.</b> 걷어내는 것은 받는 씬의 부트스트랩이 든다
/// (<c>CoverInstantly</c> → 준비 → <c>FadeIn</c>). 여기서 같이 걷으면 아직 준비가 안 끝난 씬이 새어 보인다.</para>
///
/// <para><b>⚠ 시계는 전부 unscaled다.</b> 마무리 실루엣(CLAUDE.md §14)이 <c>Time.timeScale = 0.1</c>을
/// 걸어 둔 채로 전환에 들어올 수 있다.</para>
/// </summary>
public class SceneTransition : Singleton<SceneTransition>
{
    [Tooltip("검은 화면 우하단에서 도는 로딩 링. 비어도 전환은 성립한다(아이콘만 안 뜬다).")]
    [SerializeField] private Image loadingRing;

    [Tooltip("화면이 완전히 검어진 뒤 새 씬을 켤 때까지의 하한(초).")]
    [Min(0f)]
    [SerializeField] private float minBlackDuration = 0.5f;

    [Tooltip("링이 한 바퀴 차고 한 바퀴 비는 데 걸리는 시간(초).")]
    [Min(0.1f)]
    [SerializeField] private float ringCycleDuration = 1.2f;

    /// <summary>지금 전환 중인가.</summary>
    public bool IsLoading { get; private set; }

    protected override void Awake()
    {
        DontDestroy = true;
        base.Awake();

        ShowRing(false);
    }

    /// <summary>
    /// 덮고, 비동기로 읽고, 최소 지속시간을 채운 뒤 넘긴다.
    /// <b>이미 덮여 있으면 페이드가 즉시 끝나므로</b> 먼저 덮어 둔 경로(시퀀스의 FadeOut)와 겹쳐도 깜빡이지 않는다.
    /// </summary>
    public void Load(string sceneName)
    {
        // 두 번 부르면 LoadSceneAsync가 둘 뜬다.
        if (IsLoading) return;

        if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneTransition] 씬 '{sceneName}'을 읽을 수 없습니다 - " +
                           "Build Settings에 등록됐는지 확인하세요. 전환하지 않습니다.", this);
            return;
        }

        IsLoading = true;
        StartCoroutine(LoadRoutine(sceneName));
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        ScreenFader fader = ScreenFader.Instance;
        if (fader != null) yield return fader.FadeOut();

        float blackAt = Time.unscaledTime;
        ShowRing(true);

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        // allowSceneActivation이 false면 progress는 0.9에서 멈춘다 - 그게 "다 읽었다"의 값이다.
        while (op.progress < 0.9f || Time.unscaledTime - blackAt < minBlackDuration)
        {
            TickRing();
            yield return null;
        }

        op.allowSceneActivation = true;

        while (!op.isDone)
        {
            TickRing();
            yield return null;
        }

        ShowRing(false);
        IsLoading = false;
    }

    /// <summary>
    /// 하단에서 시계방향으로 채우고, 다시 하단에서부터 시계방향으로 비운다.
    ///
    /// <para>비우는 단계가 <c>fillClockwise = false</c>인 것이 핵심이다 — 반시계로 잰 호의 길이가 줄면
    /// <b>사라지는 쪽이 하단부터 시계방향</b>이 된다. 두 단계가 같은 <c>fillOrigin</c>(Bottom)을 쓰므로
    /// 그 값은 프리팹 저작값 그대로 두고 건드리지 않는다.</para>
    /// </summary>
    private void TickRing()
    {
        if (loadingRing == null) return;

        float t = Mathf.Repeat(Time.unscaledTime, ringCycleDuration) / ringCycleDuration;

        loadingRing.fillClockwise = t < 0.5f;
        loadingRing.fillAmount = t < 0.5f ? t * 2f : 2f - t * 2f;
    }

    private void ShowRing(bool visible)
    {
        if (loadingRing == null) return;

        loadingRing.fillAmount = 0f;
        loadingRing.gameObject.SetActive(visible);
    }
}
