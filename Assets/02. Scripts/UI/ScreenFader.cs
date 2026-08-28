using System.Collections;
using UnityEngine;

/// <summary>
/// 씬 전환을 덮는 검은 화면. <b>로딩 화면이 아니다</b> — 진행바도 문구도 없다.
///
/// <para><b>왜 <c>Managers</c> 안인가</b>: 씬이 언로드되는 동안에도 화면을 덮고 있어야 하므로
/// 씬에 두면 <b>자기가 먼저 사라진다</b>. <c>Singleton{T}</c>의 <c>DontDestroy</c> 경로가
/// <c>transform.root</c>에 걸리므로 <c>Managers</c> 자식으로 두면 루트째 넘어간다(CLAUDE.md §10).</para>
///
/// <para><b>⚠ 이 클래스가 씬을 로드하지 않는다.</b> 무엇을 덮을지는 부르는 쪽이 정한다 —
/// 여기는 알파만 민다.</para>
/// </summary>
public class ScreenFader : Singleton<ScreenFader>
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private float fadeDuration = 0.4f;

    /// <summary>지금 화면이 덮여 있는가.</summary>
    public bool IsCovered => group != null && group.alpha >= 1f;

    protected override void Awake()
    {
        DontDestroy = true;
        base.Awake();

        if (group == null)
        {
            Debug.LogError("[ScreenFader] CanvasGroup이 배선되지 않았습니다.", this);
            return;
        }

        Apply(0f);
    }

    /// <summary>검게 덮는다. 씬을 로드하기 직전에 부른다.</summary>
    public IEnumerator FadeOut() => Fade(1f);

    /// <summary>걷어낸다. 씬이 준비된 뒤에 부른다.</summary>
    public IEnumerator FadeIn() => Fade(0f);

    /// <summary>보간 없이 즉시 덮는다(씬 첫 프레임이 새어 보이는 것을 막는다).</summary>
    public void CoverInstantly() => Apply(1f);

    private IEnumerator Fade(float target)
    {
        if (group == null) yield break;

        float from = group.alpha;
        if (Mathf.Approximately(from, target))
        {
            Apply(target);
            yield break;
        }

        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            Apply(Mathf.Lerp(from, target, t / fadeDuration));
            yield return null;
        }

        Apply(target);
    }

    /// <summary>
    /// 알파와 함께 레이캐스트 차단도 같이 민다. <b>검은 화면에서 입력이 먹으면 안 된다</b> —
    /// 전환 도중의 클릭이 사라지는 씬의 UI로 흘러 들어간다.
    /// </summary>
    private void Apply(float alpha)
    {
        group.alpha = alpha;
        group.blocksRaycasts = alpha > 0.001f;
    }
}
