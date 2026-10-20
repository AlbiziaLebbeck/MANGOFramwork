using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace Mangos.Ttb.Commerce
{
    [Preserve, DisallowMultipleComponent, RequireComponent(typeof(TtbWorldInputGate))]
    public sealed class MangoTtbCommerceBridge : MonoBehaviour
    {
        public static MangoTtbCommerceBridge Instance { get; private set; }
        public static event Action<bool> OverlayChanged;
        public static event Action<string> EntitlementUseRequested;
        public static event Action<string, string, string> GameTicketReceived;
        public static event Action GameProbeRequested, GameSessionCleared;
        public static bool IsOverlayOpen => Instance != null && Instance.overlayOpen;
        [SerializeField] private TtbProductPreview preview;
        private bool overlayOpen;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private string activeRequest;
        private float nextCommandAt;
        private int commandsInWindow;

        [Serializable] private sealed class Command
        {
            public int version; public string type, requestId, productId, modelKey, entitlementId, ticket;
            public bool open; public float x, y, width, height, deltaX, deltaY, delta, scale, initialRotation, minZoom, maxZoom;
        }
        [Serializable] private sealed class Event
        {
            public int version = 1; public string type, requestId, productId, modelKey, serverId, challenge, entitlementId, result; public bool ready;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void MangoTtbEmit(string json);
        [DllImport("__Internal")] private static extern void MangoTtbLogin();
#endif
        private void Awake()
        {
#if UNITY_SERVER
            enabled = false; return;
#else
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; gameObject.name = "MangoTtbCommerceBridge";
            if (preview == null) preview = GetComponent<TtbProductPreview>() ?? gameObject.AddComponent<TtbProductPreview>();
#endif
        }
        private void Start() => Emit(new Event { type = "host.ready" });
        private void OnDestroy()
        {
            if (Instance != this) return;
            SetOverlay(false); Emit(new Event { type = "host.stopped" }); Instance = null;
        }
        public void Open(TtbProduct product)
        {
            if (product == null || !product.HasBinding) return;
            SetOverlay(true);
            Emit(new Event { type = "product.open", productId = string.IsNullOrWhiteSpace(product.ProductId) ? null : product.ProductId, modelKey = product.ModelKey });
        }
        [Preserve] public void OnBrowserMessage(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 8192) return;
            if (Time.unscaledTime >= nextCommandAt) { nextCommandAt = Time.unscaledTime + 1; commandsInWindow = 0; }
            if (++commandsInWindow > 180) return;
            Command command;
            try { command = JsonUtility.FromJson<Command>(json); } catch (ArgumentException) { return; }
            if (command == null || command.version != 1 || command.type == null || command.requestId?.Length > 80) return;
            switch (command.type)
            {
                case "overlay.state": SetOverlay(command.open); if (!command.open) ClosePreview(); break;
                case "world.tap":
                    if (!overlayOpen && Finite(command.x, command.y) && command.x >= 0 && command.x <= 1 && command.y >= 0 && command.y <= 1)
                        GetComponent<TtbProductInteractor>()?.OpenAtNormalized(command.x, command.y);
                    break;
                case "product.inspect": Inspect(command); break;
                case "preview.close": if (string.IsNullOrEmpty(command.requestId) || command.requestId == activeRequest) ClosePreview(); break;
                case "preview.viewport":
                    if (Matches(command) && Finite(command.x, command.y, command.width, command.height) && command.width > 0 && command.height > 0)
                        preview.SetViewport(new Rect(command.x, command.y, command.width, command.height));
                    break;
                case "preview.orbit": if (Matches(command) && Finite(command.deltaX, command.deltaY)) preview.Orbit(Mathf.Clamp(command.deltaX, -100, 100), Mathf.Clamp(command.deltaY, -100, 100)); break;
                case "preview.zoom": if (Matches(command) && Finite(command.delta)) preview.Zoom(Mathf.Clamp(command.delta, -.5f, .5f)); break;
                case "preview.reset": if (Matches(command)) preview.ResetView(); break;
                case "session.login":
#if UNITY_WEBGL && !UNITY_EDITOR
                    MangoTtbLogin();
#endif
                    break;
                case "inventory.use":
                    // The trusted game relay must validate this entitlement for the authenticated connection before equipping anything.
                    if (Guid.TryParse(command.entitlementId, out _)) EntitlementUseRequested?.Invoke(command.entitlementId);
                    break;
                case "game.probe": GameProbeRequested?.Invoke(); break;
                case "game.session.clear": GameSessionCleared?.Invoke(); break;
                case "game.ticket":
                    if (Guid.TryParse(command.entitlementId, out _) && command.ticket?.Length == 64 && command.requestId?.Length == 32)
                        GameTicketReceived?.Invoke(command.requestId, command.entitlementId, command.ticket);
                    break;
            }
        }
        private void Inspect(Command command)
        {
            if (string.IsNullOrEmpty(command.requestId) || !Finite(command.scale, command.initialRotation, command.minZoom, command.maxZoom)) return;
            var products = FindObjectsByType<TtbProduct>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(p => p.HasBinding).ToArray();
            TtbProduct product = null;
            if (Guid.TryParse(command.productId, out var targetId)) product = products.FirstOrDefault(p => Guid.TryParse(p.ProductId, out var candidateId) && candidateId == targetId);
            // Models instantiated from a registry may omit dynamic database GUIDs. Only an unambiguous model binding can fall back here.
            if (product == null && !string.IsNullOrEmpty(command.modelKey))
            {
                var matches = products.Where(p => p.ModelKey == command.modelKey).ToArray();
                if (matches.Length == 1) product = matches[0];
            }
            if (product == null) { Emit(new Event { type = "preview.error", requestId = command.requestId }); return; }
            activeRequest = command.requestId;
            if (!preview.Show(product.PreviewModel, Mathf.Clamp(command.scale, .01f, 100), command.initialRotation,
                Mathf.Clamp(command.minZoom, .1f, 5), Mathf.Clamp(command.maxZoom, .2f, 10)))
            { ClosePreview(); Emit(new Event { type = "preview.error", requestId = command.requestId }); return; }
            SetOverlay(true); Emit(new Event { type = "preview.ready", requestId = activeRequest });
        }
        private bool Matches(Command command) => !string.IsNullOrEmpty(activeRequest) && command.requestId == activeRequest;
        private void ClosePreview() { activeRequest = null; if (preview != null) preview.Hide(); }
        private void SetOverlay(bool value)
        {
            if (overlayOpen == value) return;
            overlayOpen = value;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = !value;
#endif
            if (value) { previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else { Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible; }
            OverlayChanged?.Invoke(value);
        }
        public void NotifySessionChanged() => Emit(new Event { type = "session.changed" });
        public static void NotifyGameReady(bool ready) => Emit(new Event { type = "game.ready", ready = ready });
        public static void RequestGameTicket(string request, string entitlement, string server, string challenge) => Emit(new Event { type = "game.challenge", requestId = request, entitlementId = entitlement, serverId = server, challenge = challenge });
        public static void NotifyGameResult(string result) => Emit(new Event { type = "game.result", result = result });
        public void NotifySessionEnded() { ClosePreview(); SetOverlay(false); Emit(new Event { type = "session.end" }); }
        private static bool Finite(params float[] values) => values.All(v => !float.IsNaN(v) && !float.IsInfinity(v));
        private static void Emit(Event payload)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MangoTtbEmit(JsonUtility.ToJson(payload));
#endif
        }
    }
}
