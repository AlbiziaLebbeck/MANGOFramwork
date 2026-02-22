using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace MANGOsFramework.Experiment
{
    public class UIJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("UI References")]
        public RectTransform background;
        public RectTransform knob;

        [Header("Settings")]
        public float maxRadius = 100f;
        public float sprintExtraRadius = 60f;
        public float sensitivity = 1f;

        [Header("Events")]
        public UnityEvent<Vector2, float> OnJoystickMove;
        public UnityEvent<Vector2, float> OnJoystickRelease;

        private Vector2 _inputVector;
        private Vector2 _startPosition;

        public void OnPointerDown(PointerEventData eventData)
        {
            background.position = eventData.position;
            knob.position = eventData.position;

            _startPosition = eventData.position;

            background.gameObject.SetActive(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 direction = eventData.position - _startPosition;
            float distance = direction.magnitude;
            Vector2 dirNormalized = direction.normalized;

            float maxDistance = maxRadius + sprintExtraRadius;

            Vector2 knobPos = dirNormalized * Mathf.Min(distance, maxRadius);
            knob.position = _startPosition + knobPos;

            float magnitude = Mathf.Clamp01(distance / maxRadius);
            if (distance > maxRadius)
            {
                float sprintFactor = (distance / maxRadius) / sprintExtraRadius;
                magnitude = 1f + Mathf.Clamp01(sprintFactor);
            }

            _inputVector = dirNormalized * sensitivity;
            OnJoystickMove?.Invoke(_inputVector, magnitude);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _inputVector = Vector2.zero;
            knob.position = background.position;
            background.gameObject.SetActive(false);

            OnJoystickRelease?.Invoke(Vector2.zero, 0f);
        }

        public Vector2 GetDirection() => _inputVector;
    }
}

