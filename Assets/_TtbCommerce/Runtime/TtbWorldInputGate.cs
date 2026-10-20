using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mangos.Ttb.Commerce
{
    // Adapter to the existing Cookie player. Do not rewrite its input maps or player movement source.
    public sealed class TtbWorldInputGate : MonoBehaviour
    {
        private readonly Dictionary<Behaviour, bool> previous = new Dictionary<Behaviour, bool>();
        private NetworkObject player;
        private void OnEnable()
        {
            MangoTtbCommerceBridge.OverlayChanged += Changed;
            LocalPlayerAnnouncer.OnLocalPlayerUpdated += PlayerChanged;
            global::EventHandler.ClientDisconnectedEvent += Disconnected;
            global::EventHandler.ClientLoginSuccessEvent += LoginChanged;
            if (ClientInstance.Instance != null) PlayerChanged(ClientInstance.Instance.NetworkObject);
        }
        private void OnDisable()
        {
            MangoTtbCommerceBridge.OverlayChanged -= Changed;
            LocalPlayerAnnouncer.OnLocalPlayerUpdated -= PlayerChanged;
            global::EventHandler.ClientDisconnectedEvent -= Disconnected;
            global::EventHandler.ClientLoginSuccessEvent -= LoginChanged;
            Restore();
        }
        private void Disconnected() => MangoTtbCommerceBridge.Instance?.NotifySessionEnded();
        private void LoginChanged() => MangoTtbCommerceBridge.Instance?.NotifySessionChanged();
        private void PlayerChanged(NetworkObject next) { Restore(); player = next; }
        private void LateUpdate() { if (MangoTtbCommerceBridge.IsOverlayOpen) Suspend(); }
        private void Changed(bool open) { if (open) Suspend(); else Restore(); }
        private void Suspend()
        {
            // HTML owns the entire commerce surface, including its native 3D preview viewport.
            // Legacy HUD and orientation canvases must not render over that viewport.
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (canvas.renderMode == RenderMode.WorldSpace) continue;
                if (!previous.ContainsKey(canvas)) previous.Add(canvas, canvas.enabled);
                canvas.enabled = false;
            }
            if (player == null) return;
            foreach (var behaviour in player.GetComponentsInChildren<Behaviour>(true))
            {
                var name = behaviour.GetType().Name;
                if (behaviour is PlayerMovementHandler || behaviour is PlayerInput || name == "ThirdPersonController" || name == "StarterAssetsInputs" || name == "BasicBehaviour" || name == "MoveBehaviour" || name == "FlyBehaviour")
                {
                    if (!previous.ContainsKey(behaviour)) previous.Add(behaviour, behaviour.enabled);
                    behaviour.enabled = false;
                }
            }
            if (PlayerCameraHandler.Instance != null && PlayerCameraHandler.Instance.MainVirtualCamera != null)
                foreach (var behaviour in PlayerCameraHandler.Instance.MainVirtualCamera.GetComponentsInChildren<Behaviour>(true))
                    if (behaviour.GetType().Name == "CinemachineInputProvider" || behaviour.GetType().Name == "ThirdPersonOrbitCamBasic")
                    { if (!previous.ContainsKey(behaviour)) previous.Add(behaviour, behaviour.enabled); behaviour.enabled = false; }
        }
        private void Restore() { foreach (var item in previous) if (item.Key != null) item.Key.enabled = item.Value; previous.Clear(); }
    }
}
