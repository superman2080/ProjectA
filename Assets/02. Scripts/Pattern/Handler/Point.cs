using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PatternSpace
{
    /// <summary>
    /// 개별 포인트. 포인터/키보드 입력을 <see cref="OnPointDown"/>·<see cref="OnPointUp"/>으로 알리기만 하고,
    /// "이미 입력된 Point인가"라는 중복 판정은 하지 않는다 — 그 상태는 <see cref="PatternHandler"/>가
    /// 패턴 단위로 관리한다(진실의 원천 일원화).
    /// </summary>
    public class Point : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler
    {
        [SerializeField] private int index;
        [SerializeField] private PatternHandler handler;
        [SerializeField] private Image image;

        /// <summary>레이캐스트(판정)를 받는 본체 Image. <see cref="image"/>는 색상 표시용 자식 Visual이라 서로 다른 대상이다.</summary>
        [SerializeField] private Image hitGraphic;

        /// <summary>
        /// 노브(Visual)의 표시/숨김 전용 CanvasGroup. <see cref="image"/>의 알파를 쓰지 않는 이유는
        /// <see cref="SetJudgementColor"/>·<see cref="ResetColor"/>가 알파를 포함한 색을 통째로 대입하기 때문 —
        /// 알파로 페이드하면 판정 색 대입이 페이드를 덮어쓴다. 이 CanvasGroup은 Visual 서브트리에만 걸리므로
        /// 부모 본체의 레이캐스트(<see cref="hitGraphic"/>)에는 영향이 없다 — 노브를 감춰도 판정 영역은 살아 있다.
        /// </summary>
        [SerializeField] private CanvasGroup visualGroup;

        [Header("Judgement Colors")]
        [SerializeField] private Color perfectColor = Color.blue;
        [SerializeField] private Color goodColor = Color.green;
        [SerializeField] private Color missColor = Color.red;

        private Color defaultColor;
        private Coroutine knobFadeRoutine;

        public event Action<int> OnPointDown;
        public event Action<int> OnPointUp;

        /// <summary>
        /// <see cref="PatternHandler"/>가 <c>patternPoints</c> 배열 순서대로 인덱스와 자기 참조를 주입한다.
        /// 배열 순서가 인덱스의 진실의 원천이다 — 예전처럼 게임오브젝트 이름을 파싱하지 않는다.
        /// </summary>
        public void Initialize(int pointIndex, PatternHandler owner)
        {
            index = pointIndex;
            handler = owner;

            image ??= GetComponent<Image>();
            hitGraphic ??= GetComponent<Image>();
            visualGroup ??= image != null ? image.GetComponent<CanvasGroup>() : null;
            defaultColor = image != null ? image.color : Color.white;
        }

        void Reset()
        {
            image ??= GetComponent<Image>();
            hitGraphic ??= GetComponent<Image>();
            visualGroup ??= image != null ? image.GetComponent<CanvasGroup>() : null;
        }

        public void OnPointerDown(PointerEventData eventData) => Down();
        public void OnPointerUp(PointerEventData eventData) => Up();

        public void OnPointerEnter(PointerEventData eventData)
        {
            // 마우스로 끌고 지나갈 때만 입력으로 인정한다.
            // IsDragging은 키보드 스트로크 중에도 true이므로, 그 값을 보면 마우스를 올리기만 해도 입력돼 버린다.
            if (handler != null && handler.IsMouseDragging)
                Down();
        }

        private void Down() => OnPointDown?.Invoke(index);

        private void Up() => OnPointUp?.Invoke(index);

        /// <summary>통과 노드 자동 인식·키보드 입력용 — 실제 포인터 이벤트 없이 Down을 발생시킨다.</summary>
        public void ForceDown() => Down();

        public void SetJudgementColor(JudgementResult result)
        {
            if (image == null) return;

            image.color = result switch
            {
                JudgementResult.Perfect => perfectColor,
                JudgementResult.Good => goodColor,
                _ => missColor
            };
        }

        public void ResetColor()
        {
            if (image != null)
                image.color = defaultColor;
        }

        /// <summary>판정(레이캐스트) 영역을 RectTransform 크기 대비 <paramref name="ratio"/> 비율로 줄인다. 1이면 원래 크기.</summary>
        public void SetHitAreaRatio(float ratio)
        {
            if (hitGraphic == null) return;

            ratio = Mathf.Clamp01(ratio);
            Vector2 size = ((RectTransform)transform).rect.size;
            float padX = size.x * (1f - ratio) * 0.5f;
            float padY = size.y * (1f - ratio) * 0.5f;
            hitGraphic.raycastPadding = new Vector4(padX, padY, padX, padY);
        }

        public void ResetHitArea() => SetHitAreaRatio(1f);

        /// <summary>
        /// 노브(Visual)를 페이드로 표시/숨김한다. 판정 색과는 독립이다(<see cref="visualGroup"/> 참고).
        /// <paramref name="duration"/>이 0 이하면 즉시 반영한다.
        /// </summary>
        public void SetKnobVisible(bool visible, float duration)
        {
            if (visualGroup == null) return; // 연출이라 배선이 없어도 게임이 멈추면 안 된다

            if (knobFadeRoutine != null)
            {
                StopCoroutine(knobFadeRoutine);
                knobFadeRoutine = null;
            }

            float target = visible ? 1f : 0f;

            if (duration <= 0f)
            {
                visualGroup.alpha = target;
                return;
            }

            knobFadeRoutine = StartCoroutine(FadeKnobRoutine(target, duration));
        }

        /// <summary>현재 알파에서 이어서 보간한다 — 페이드 도중 반대로 뒤집혀도 값이 튀지 않는다.</summary>
        private IEnumerator FadeKnobRoutine(float target, float duration)
        {
            float start = visualGroup.alpha;
            float t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                visualGroup.alpha = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }

            visualGroup.alpha = target;
            knobFadeRoutine = null;
        }
    }
}
