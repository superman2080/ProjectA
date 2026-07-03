using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PatternSpace
{
    public class Point : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler
    {
        public bool IsBusy => isBusy;
        private bool isBusy;

        [SerializeField] private int index;
        [SerializeField] private PatternHandler handler;

        public event Action<int> OnPointDown;
        public event Action<int> OnPointUp;

        void Start()
        {
            if (int.TryParse(gameObject.name[^1].ToString(), out int result))
                index = result - 1;

            handler ??= FindAnyObjectByType<PatternHandler>();
        }

        void Reset()
        {
            if (int.TryParse(gameObject.name[^1].ToString(), out int result))
                index = result - 1;
            handler ??= FindAnyObjectByType<PatternHandler>();
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
    }
}
