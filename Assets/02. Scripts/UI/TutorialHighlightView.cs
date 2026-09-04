using TMPro;
using UnityEngine;

/// <summary>
/// 튜토리얼 강조 장치. Canvas의 한 자리를 링으로 지목하고 화면 위쪽에 한 줄로 설명한다.
///
/// <para><b>표시 계층은 게임플레이를 모른다</b> — 무엇을 왜 강조하는지는 부르는 쪽(<c>HighlightStep</c>)이 안다.
/// 여기는 "어디에 링을 놓고 무슨 글자를 띄우나"만 안다.</para>
///
/// <para><b>왜 정적 접근자인가</b>: 스텝은 ScriptableObject에 직렬화되므로 씬 오브젝트를 참조할 수 없다.
/// <c>DialogUI</c>와 같은 관용구이며, <c>Singleton{T}</c>를 안 쓰는 이유도 같다 —
/// 그쪽 게터는 <b>없으면 빈 게임오브젝트를 만들어 내기</b> 때문에, 자식 참조가 없는 껍데기보다 <c>null</c>이 낫다
/// (부르는 쪽이 에러를 찍고 그 스텝만 건너뛴다).</para>
///
/// <para><b>⚠ 링을 대상의 자식으로 붙이지 않는다.</b> Point의 노브(<c>Visual</c>)에는 <c>CanvasGroup</c>이 걸려 있어
/// 자식이 되면 <b>노브가 꺼질 때 링도 같이 사라진다</b>. 위치와 크기만 복사한다 —
/// 루트 Canvas가 <c>ScreenSpaceOverlay</c>라 월드 투영도 필요 없다.</para>
/// </summary>
public class TutorialHighlightView : MonoBehaviour
{
    /// <summary>씬에 배치된 강조 장치. 없으면 <c>null</c>.</summary>
    public static TutorialHighlightView Instance { get; private set; }

    [Tooltip("강조 대상 자리에 맞춰지는 테두리. raycastTarget은 반드시 꺼 둔다.")]
    [SerializeField] private RectTransform ring;

    [Tooltip("화면 위쪽 한 줄 설명. 대사창과 달리 이름표가 없다.")]
    [SerializeField] private TextMeshProUGUI caption;

    [Tooltip("맥동에 쓰는 알파. 링과 캡션을 함께 덮는다.")]
    [SerializeField] private CanvasGroup group;

    [Header("Pulse")]
    [Min(0f)]
    [SerializeField] private float minAlpha = 0.35f;

    [Min(0.01f)]
    [SerializeField] private float pulseSpeed = 1.2f;

    private void Awake()
    {
        // ⚠ 등록은 Awake다(Start가 아니라). Unity는 Start 순서를 보장하지 않으므로
        // SequenceRunner의 playOnStart 경로가 자기 Start에서 조회하면 null을 받을 수 있다.
        Instance = this;

        // ⚠ 이름으로 자동 배선하지 않는다 — 매 실행 인스펙터 배선을 덮어써 하이어라키 이름이 계약이 된다.
        if (ring == null || caption == null || group == null)
        {
            Debug.LogError("[TutorialHighlightView] 참조가 배선되지 않았습니다 - 인스펙터에서 채우세요.", this);
            return;
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (group == null || !group.gameObject.activeInHierarchy) return;

        // ⚠ unscaledTime이다. 마무리 실루엣(CLAUDE.md §14)이 timeScale을 건드리는 유일한 곳이고,
        // 안내가 그 영향을 받을 이유가 없다.
        group.alpha = Mathf.Lerp(minAlpha, 1f, Mathf.PingPong(Time.unscaledTime * pulseSpeed, 1f));
    }

    /// <summary>
    /// 강조를 켠다. <paramref name="target"/>이 비면 링 없이 캡션만 띄운다 —
    /// "화면 어디도 아닌 설명"이 분기 하나로 성립한다.
    /// </summary>
    public void Show(RectTransform target, string text, float padding)
    {
        if (ring == null || caption == null || group == null) return;

        group.gameObject.SetActive(true);
        group.alpha = 1f;

        caption.text = text ?? string.Empty;

        // ⚠ 끄는 대상은 캡션 자신이 아니라 '부모가 있으면 부모'다 — 배경 판이 캡션을 감싸고 있어서,
        // 캡션만 끄면 빈 판이 화면에 덩그러니 남는다. 부모가 없으면(판을 안 쓰면) 예전과 같다.
        GameObject captionRoot = caption.transform.parent != null && caption.transform.parent != group.transform
            ? caption.transform.parent.gameObject
            : caption.gameObject;

        captionRoot.SetActive(!string.IsNullOrEmpty(text));

        if (target == null)
        {
            ring.gameObject.SetActive(false);
            return;
        }

        ring.gameObject.SetActive(true);
        ring.position = target.position;

        // 대상과 링의 스케일이 다를 수 있어(중첩 Canvas 스케일러) 로컬 크기로 환산한다.
        float scale = Mathf.Approximately(ring.lossyScale.x, 0f)
            ? 1f
            : target.lossyScale.x / ring.lossyScale.x;

        ring.sizeDelta = target.rect.size * scale + Vector2.one * (padding * 2f);
    }

    /// <summary>강조를 내린다. <b>여러 번 불려도 안전하다</b> — 복구 경로가 셋이라 그래야 한다.</summary>
    public void Hide()
    {
        if (group == null) return;
        group.gameObject.SetActive(false);
    }
}
