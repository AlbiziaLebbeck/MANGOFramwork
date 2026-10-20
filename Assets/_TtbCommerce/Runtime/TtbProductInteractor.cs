using UnityEngine;
using UnityEngine.InputSystem;

namespace Mangos.Ttb.Commerce
{
    public sealed class TtbProductInteractor : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private float maximumDistance = 30;
        private Vector2 pressedAt;
        private bool pressed;
        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // DOM pointerup keeps very quick taps reliable even when both transitions fall between Unity frames.
            return;
#else
            if (MangoTtbCommerceBridge.IsOverlayOpen) { pressed = false; return; }
            var touch = Touchscreen.current != null ? Touchscreen.current.primaryTouch : null;
            if (touch != null && touch.press.wasPressedThisFrame) { pressedAt = touch.position.ReadValue(); pressed = true; }
            if (touch != null && touch.press.wasReleasedThisFrame) { Release(touch.position.ReadValue()); return; }
            if (touch != null && touch.press.isPressed) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.leftButton.wasPressedThisFrame) { pressedAt = mouse.position.ReadValue(); pressed = true; }
            if (mouse.leftButton.wasReleasedThisFrame) Release(mouse.position.ReadValue());
#endif
        }
        private void Release(Vector2 position)
        {
            var clicked = pressed && (position - pressedAt).sqrMagnitude < 64; pressed = false;
            var camera = worldCamera != null ? worldCamera : Camera.main;
            if (!clicked || camera == null || MangoTtbCommerceBridge.Instance == null) return;
            OpenRay(camera.ScreenPointToRay(position));
        }
        public void OpenAtNormalized(float x, float y)
        {
            var camera = worldCamera != null ? worldCamera : Camera.main;
            if (camera == null || MangoTtbCommerceBridge.IsOverlayOpen) return;
            OpenRay(camera.ViewportPointToRay(new Vector3(x, 1-y, 0)));
        }
        private void OpenRay(Ray ray)
        {
            if (Physics.Raycast(ray, out var hit, maximumDistance, ~(1 << 31), QueryTriggerInteraction.Ignore))
            {
                var product = hit.collider.GetComponentInParent<TtbProduct>();
                if (product != null) MangoTtbCommerceBridge.Instance.Open(product);
            }
        }
    }
}
