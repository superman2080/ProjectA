using System;
using PatternSpace;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// 기습 회피의 <b>닷지 포인트</b>. 화면 <b>우하단 고정 자리</b>에서 포커스 링이 수축한다.
/// 패턴인풋의 Point와 같은 성질의 입력 지점이라 이름도 그 계열을 따른다 — "프롬프트"는 UI 안내문으로 읽힌다.
///
/// <para><b>⚠ 적을 따라가지 않는다.</b> 예전에는 기습자의 월드 위치에 붙어 있었고, 그래서 두 문제를 동시에 안고 있었다 —
/// 적이 화면 밖이면 단서가 통째로 사라지고, 적이 중앙에 오면 패턴 링과 겹쳤다. 자리를 고정하면 <b>둘 다 존재하지 않는 문제</b>가 된다.
/// 대신 "어디서 오는가"는 기습자 아웃라인(<c>EnemyView.SetHighlight</c>)이 전담한다 —
/// <b>아웃라인 = 누가·어디서 / 닷지 포인트 = 지금</b>으로 역할이 갈린다.</para>
///
/// <para>그 결과 <b>이 클래스는 카메라를 모른다.</b> 월드→화면 투영도, 카메라 뒤 반전 처리도,
/// 앵글 교체(§7-5)를 따라가기 위한 <c>LateUpdate</c> 규율도 전부 필요 없어졌다.
/// 루트 Canvas가 <c>ScreenSpaceOverlay</c>라 패턴 UI와 똑같이 카메라와 완전히 독립이다.</para>
///
/// <para><b>판정 시각은 여기 없다.</b> 링의 <c>OnArrived</c>도 구독하지 않는다 —
/// 시계를 둘로 만들면 언젠가 어긋난다. 이 뷰는 "보여주고 눌린 것을 알리는" 일만 한다.</para>
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DodgePointView : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("링이 앉을 자리이자 아이콘 본체. 비우면 이 오브젝트의 RectTransform.")]
    [SerializeField] private RectTransform root;

    [Tooltip("링의 시작 크기 배율. 패턴 링(PatternHandler.focusRingStartScale)과 같은 값이어야 감각이 같다.")]
    [SerializeField] private float ringStartScale = 2f;

    [FormerlySerializedAs("promptScale")]
    [Tooltip("닷지 포인트 전체(아이콘 + 링) 배율. 주변시로 읽어야 하므로 크게 띄운다.\n" +
             "⚠ 링의 endSize는 여기서 못 만진다 — FocusRing 프리팹은 패턴 링과 '같은 풀'을 쓰므로\n" +
             "그 값을 키우면 패턴 노드 링까지 같이 커진다. 그래서 배율을 루트에 건다.\n" +
             "아이콘과 링이 함께 커져 '링이 아이콘 크기가 되는 순간이 입력'이라는 단서는 그대로 유지된다.")]
    [SerializeField] private float pointScale = 1.8f;

    [FormerlySerializedAs("promptMargin")]
    [Tooltip("화면 <b>우하단</b> 모서리에서 띄울 여백(px, 캔버스 좌표). 둘 다 양수로 적는다 —\n" +
             "부호는 ApplyAnchor가 붙인다(오른쪽 모서리 기준이라 x는 안쪽 = 음수 방향).\n" +
             "⚠ 패턴인풋 Point_3이 (700,-700)이다 — 그 아래·오른쪽으로 확실히 벗어나야 겹치지 않는다.")]
    [SerializeField] private Vector2 pointMargin = new Vector2(320f, 320f);

    [Tooltip("링 색. 패턴 링 팔레트와 구분되는 색이어야 '이건 다른 입력'이라고 읽힌다.\n" +
             "자리가 달라져 겹치지는 않지만, 색까지 다르면 신호가 더 싸게 선다.")]
    [SerializeField] private Color ringColor = new Color(1f, 0.35f, 0.25f);

    /// <summary>닷지 포인트가 눌린 순간. 판정은 <c>DodgeDirector</c>가 한다.</summary>
    public event Action OnPressed;

    private FocusRingView ring;

    void Awake()
    {
        if (root == null) root = (RectTransform)transform;

        ApplyAnchor();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 우하단 고정 자리를 잡는다. <b>씬 값을 믿지 않고 코드가 정한다</b> —
    /// 앵커와 여백이 따로 놀면 "여백을 키웠는데 안 움직이는" 상태가 되고, 그 조합은 인스펙터만 봐서는 안 보인다.
    /// </summary>
    private void ApplyAnchor()
    {
        var corner = new Vector2(1f, 0f);   // 우하단

        root.anchorMin = corner;
        root.anchorMax = corner;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(-Mathf.Abs(pointMargin.x), Mathf.Abs(pointMargin.y));
    }

#if UNITY_EDITOR
    // 인스펙터에서 여백을 만지면 씬 뷰에 바로 반영된다(플레이 중이 아니어도).
    void OnValidate()
    {
        if (root == null) root = (RectTransform)transform;
        ApplyAnchor();
    }
#endif

    /// <summary>
    /// 닷지 포인트를 띄우고 링 수축을 시작한다. <b>자리는 인자가 아니다</b> — 언제나 같은 곳이다.
    /// 링은 <b>이 오브젝트의 자식</b>이라 포인트만 움직이면 따라온다.
    /// </summary>
    public void Show(float duration)
    {
        gameObject.SetActive(true);
        SetVisible(true);
        ApplyAnchor();   // 여백을 플레이 중에 바꿔도 다음 등장부터 반영된다

        ReleaseRing();
        ring = Pool.Instance.Get<FocusRingView>(PoolKey.FocusRing, r =>
        {
            r.transform.SetParent(root, false);
            // 부모가 곧 자리다 → 로컬 (0,0). 표시 인덱스는 안 쓰지만(라벨 비활성) 시그니처상 1을 넘긴다.
            r.Initialize(1, ringColor, NodeType.Start, Vector2.zero, ringStartScale, duration);
        });
    }

    /// <summary>닷지 포인트를 걷는다. 성패 어느 쪽이든 같은 정리를 탄다.</summary>
    public void Hide()
    {
        ReleaseRing();
        gameObject.SetActive(false);
    }

    public void OnPointerDown(PointerEventData eventData) => OnPressed?.Invoke();

    /// <summary>
    /// 보일 때의 크기는 1이 아니라 <see cref="pointScale"/>다 —
    /// <b>여기가 크기의 유일한 지점</b>이라 배율을 따로 두면 '숨겼다 켜면 크기가 1로 돌아가는' 버그가 난다.
    /// </summary>
    private void SetVisible(bool visible)
    {
        Vector3 scale = visible ? Vector3.one * Mathf.Max(pointScale, 0.01f) : Vector3.zero;
        if (root.localScale != scale) root.localScale = scale;
    }

    private void ReleaseRing()
    {
        if (ring == null) return;

        Pool.Release(PoolKey.FocusRing, ring);
        ring = null;
    }

    void OnDisable()
    {
        // 씬 정리·비활성에서 링이 남지 않도록. 풀 반납은 멱등이다.
        ReleaseRing();
    }
}
