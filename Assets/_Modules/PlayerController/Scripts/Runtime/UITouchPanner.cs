using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace MANGOsFramework.Experiment
{
    public class UITouchPanner : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Settings")]
        public float sensitivity = 0.2f;

        [Header("Events")]
        public UnityEvent<Vector2> OnPanDelta;
        public UnityEvent OnPanStart;
        public UnityEvent OnPanEnd;

        private Vector2 _lastPosition;

        public Vector2 Delta { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            _lastPosition = eventData.position;
            OnPanStart?.Invoke();
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 delta = (eventData.position - _lastPosition) * sensitivity;
            _lastPosition = eventData.position;

            // Invert Y-axis
            delta.y = -delta.y;

            Delta = delta;
            OnPanDelta?.Invoke(delta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            OnPanEnd?.Invoke();
            Delta = Vector2.zero;
            OnPanDelta?.Invoke(Vector2.zero);
        }
    }
}

