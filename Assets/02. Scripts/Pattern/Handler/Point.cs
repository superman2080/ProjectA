using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PatternSpace
{
    public class Point : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler
    {
        public bool IsBusy => isBusy;
        private bool isBusy;

        [SerializeField] private int index;
        [SerializeField] private PatternHandler handler;
        [SerializeField] private Image image;

        [Header("Judgement Colors")]
        [SerializeField] private Color perfectColor = Color.blue;
        [SerializeField] private Color goodColor = Color.green;
        [SerializeField] private Color missColor = Color.red;

        private Color defaultColor;

        public event Action<int> OnPointDown;
        public event Action<int> OnPointUp;

        void Start()
        {
            if (int.TryParse(gameObject.name[^1].ToString(), out int result))
                index = result - 1;

            handler ??= FindAnyObjectByType<PatternHandler>();
            image ??= GetComponent<Image>();
            defaultColor = image != null ? image.color : Color.white;
        }

        void Reset()
        {
            if (int.TryParse(gameObject.name[^1].ToString(), out int result))
                index = result - 1;
            handler ??= FindAnyObjectByType<PatternHandler>();
            image ??= GetComponent<Image>();
        }

        public void OnPointerDown(PointerEventData eventData) => Down();
        public void OnPointerUp(PointerEventData eventData) => Up();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (handler.IsDragging && !isBusy)
                Down();
        }

        private void Down()
        {
            isBusy = true;
            OnPointDown?.Invoke(index);
        }

        private void Up()
        {
            isBusy = false;
            OnPointUp?.Invoke(index);
        }

        public void ResetBusy() => isBusy = false;

        /// <summary>통과 노드 자동 인식용 — 실제 포인터 이벤트 없이 Down() 상태를 강제한다.</summary>
        public void ForceDown()
        {
            if (!isBusy)
                Down();
        }

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
    }
}
