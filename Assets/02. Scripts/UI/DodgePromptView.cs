using System;
using PatternSpace;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 기습 회피의 <b>원형 프롬프트</b>. 적의 월드 위치를 따라다니며, 그 자리에서 포커스 링이 수축한다.
///
/// <para><b>패턴 노드(Point)와 같은 지름이어야 한다</b> — 링의 <c>endSize</c>(90x90)와 어긋나면
/// "링이 노브 크기가 되는 순간이 입력 타이밍"이라는 학습된 단서가 거짓이 된다.</para>
///
/// <para><b>루트 Canvas가 <c>ScreenSpaceOverlay</c>라는 점이 이 클래스의 전제다</b> —
/// 패턴 UI는 카메라와 무관하지만(§7-5) 이 아이콘은 반대로 <b>적을 따라가야 하므로 카메라가 돌면 같이 움직인다.</b>
/// 그래서 갱신은 카메라가 확정된 뒤인 <c>LateUpdate</c>에서 한다 — <c>Update</c>에서 하면 앵글 교체·쉐이크 중에
/// 한 프레임씩 밀려 아이콘이 적에게서 떨어진다.</para>
///
/// <para><b>판정 시각은 여기 없다.</b> 링의 <c>OnArrived</c>도 구독하지 않는다 —
/// 시계를 둘로 만들면 언젠가 어긋난다. 이 뷰는 "보여주고 눌린 것을 알리는" 일만 한다.</para>
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DodgePromptView : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("링이 앉을 자리이자 아이콘 본체. 비우면 이 오브젝트의 RectTransform.")]
    [SerializeField] private RectTransform root;

    [Tooltip("링의 시작 크기 배율. 패턴 링(PatternHandler.focusRingStartScale)과 같은 값이어야 감각이 같다.")]
    [SerializeField] private float ringStartScale = 2f;

    [Tooltip("링 색. 패턴 링 팔레트와 구분되는 색이어야 '이건 다른 입력'이라고 읽힌다.")]
    [SerializeField] private Color ringColor = new Color(1f, 0.35f, 0.25f);

    [Tooltip("월드→화면 투영에 쓸 카메라. 비우면 Camera.main.\n" +
             "⚠ 캔버스의 worldCamera(Overlay면 null)와 다른 값이다 — 그걸 넘기면 투영이 통째로 생략된다.")]
    [SerializeField] private Camera worldCamera;

    /// <summary>프롬프트가 눌린 순간. 판정은 <c>DodgeDirector</c>가 한다.</summary>
    public event Action OnPressed;

    private RectTransform canvasRect;
    private Camera canvasCamera;
    private FocusRingView ring;

    private Transform follow;
    private Vector3 worldOffset;

    void Awake()
    {
        if (root == null) root = (RectTransform)transform;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvasRect = (RectTransform)canvas.transform;
            canvasCamera = canvas.worldCamera; // Overlay면 null이고, 그게 정상 경로다
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// <paramref name="follow"/> 위에 프롬프트를 띄우고 링 수축을 시작한다.
    /// 링은 <b>이 오브젝트의 자식</b>이라 프롬프트만 움직이면 따라온다(좌표 계산이 한 곳에 모인다).
    /// </summary>
    public void Show(Transform follow, Vector3 worldOffset, float duration)
    {
        this.follow = follow;
        this.worldOffset = worldOffset;

        gameObject.SetActive(true);
        SetVisible(true);   // 지난 대여가 카메라 뒤에서 끝났으면 스케일 0으로 남아 있다
        UpdatePosition();

        ReleaseRing();
        ring = Pool.Instance.Get<FocusRingView>(PoolKey.FocusRing, r =>
        {
            r.transform.SetParent(root, false);
            // 부모가 곧 자리다 → 로컬 (0,0). 표시 인덱스는 안 쓰지만(라벨 비활성) 시그니처상 1을 넘긴다.
            r.Initialize(1, ringColor, NodeType.Start, Vector2.zero, ringStartScale, duration);
        });
    }

    /// <summary>프롬프트를 걷는다. 성패 어느 쪽이든 같은 정리를 탄다.</summary>
    public void Hide()
    {
        ReleaseRing();
        follow = null;
        gameObject.SetActive(false);
    }

    public bool IsShowing => follow != null && gameObject.activeSelf;

    public void OnPointerDown(PointerEventData eventData) => OnPressed?.Invoke();

    /// <summary>⚠ <c>LateUpdate</c>다 — 카메라가 확정된 뒤여야 아이콘이 한 프레임 밀리지 않는다.</summary>
    void LateUpdate()
    {
        if (follow == null) return;
        UpdatePosition();
    }

    /// <summary>
    /// <b>⚠ 두 호출이 서로 다른 카메라를 쓴다.</b>
    /// <list type="bullet">
    /// <item>월드 → 화면: <see cref="worldCamera"/>(실제 렌더링 카메라). <b>null을 넘기면 Unity가 투영을 생략하고
    /// <c>(world.x, world.y)</c>를 픽셀 좌표로 돌려줘 아이콘이 화면 좌하단에 박힌다</b>(정정 8).</item>
    /// <item>화면 → 캔버스 로컬: <see cref="canvasCamera"/>. Overlay라 <b>null이 정상</b>이다.</item>
    /// </list>
    /// </summary>
    private void UpdatePosition()
    {
        if (follow == null || canvasRect == null) return;

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null) return;

        Vector3 world = follow.position + worldOffset;

        // 카메라 뒤면 WorldToScreenPoint가 좌우 반전된 좌표를 준다 — 반대편에 유령 아이콘이 뜨는 것을 막는다.
        if (cam.WorldToViewportPoint(world).z <= 0f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, world);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPoint, canvasCamera, out Vector2 local))
            root.anchoredPosition = local;
    }

    /// <summary>
    /// 화면 밖(카메라 뒤) 처리. <b>비활성화가 아니라 스케일 0이다</b> —
    /// <c>SetActive(false)</c>면 <see cref="LateUpdate"/>가 멈춰 다시 앞으로 와도 안 돌아온다.
    /// </summary>
    private void SetVisible(bool visible)
    {
        Vector3 scale = visible ? Vector3.one : Vector3.zero;
        if (root.localScale != scale) root.localScale = scale;
    }

    private void ReleaseRing()
    {
        if (ring == null) return;

        Pool.Instance.Return(PoolKey.FocusRing, ring);
        ring = null;
    }

    void OnDisable()
    {
        // 씬 정리·비활성에서 링이 남지 않도록. 풀 반납은 멱등이다.
        ReleaseRing();
    }
}
