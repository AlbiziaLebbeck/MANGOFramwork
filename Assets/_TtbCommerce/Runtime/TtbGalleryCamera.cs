using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

namespace Mangos.Ttb.Commerce
{
    // Isolated gallery demonstrator only. The actual metaverse continues to use its existing Cookie controller.
    public sealed class TtbGalleryCamera : MonoBehaviour
    {
        private Vector2 previous;
        private bool dragging;
        private int previousTouchCount;
        private float previousPinch;
        private readonly Vector3 target = new Vector3(0, 1.8f, 2.8f);
        private float yaw, pitch = 12, distance = 10;
        private void Start() => Position();
        private void Update()
        {
            if (MangoTtbCommerceBridge.IsOverlayOpen) { dragging = false; return; }
            var touches = Touchscreen.current?.touches.Where(x => x.press.isPressed).ToArray();
            if (touches != null && touches.Length > 0)
            {
                var point = touches[0].position.ReadValue();
                if (touches.Length == 1 && previousTouchCount == 1)
                { var delta = point - previous; yaw = Mathf.Clamp(yaw + delta.x * .15f, -55, 55); pitch = Mathf.Clamp(pitch - delta.y * .15f, -5, 40); Position(); }
                if (touches.Length >= 2)
                {
                    var pinch = Vector2.Distance(point, touches[1].position.ReadValue());
                    if (previousTouchCount >= 2 && pinch > 1 && previousPinch > 1) { distance = Mathf.Clamp(distance * previousPinch / pinch, 5, 15); Position(); }
                    previousPinch = pinch;
                }
                previousTouchCount = touches.Length; previous = point; return;
            }
            previousTouchCount = 0;
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.wasPressedThisFrame) { dragging = true; previous = mouse.position.ReadValue(); }
            if (mouse.rightButton.wasReleasedThisFrame) dragging = false;
            if (dragging)
            {
                var point = mouse.position.ReadValue(); var delta = point - previous; previous = point;
                yaw = Mathf.Clamp(yaw + delta.x * .15f, -55, 55); pitch = Mathf.Clamp(pitch - delta.y * .15f, -5, 40); Position();
            }
            var wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > .01f) { distance = Mathf.Clamp(distance - Mathf.Sign(wheel) * .4f, 5, 15); Position(); }
        }
        private void Position() { transform.position = target + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -distance); transform.LookAt(target); }
    }
}
