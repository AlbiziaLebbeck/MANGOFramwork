using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;
using StarterAssets;
using UnityEngine.EventSystems;
using System.Collections.Generic;

[Preserve]
[DefaultExecutionOrder(-200)]
public sealed class MangosBrowserBridge : MonoBehaviour
{
    public static MangosBrowserBridge Instance { get; private set; }
    [Serializable] private sealed class Session { public string roomId; public uint playerId; }
    [Serializable] private sealed class VoiceState { public bool connected; public bool muted; }
    [Serializable] private sealed class BrowserInput
    {
        public float x, y, lx, ly, vertical, px, py;
        public bool jump, sprint, fly;
        public int lookToken;
    }
    private GameObject localPlayer;
    private StarterAssetsInputs inputs;
    private PlayerMovementHandler movement;
    private int appliedFlags = -1, jumpVersion, lookToken;
    private bool appliedBlocked, allowLook;
    public static bool HasBrowserControls {
        get {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void MangosBrowserSetSession(string json);
    [DllImport("__Internal")] private static extern int MangosBrowserDeviceFlags();
    [DllImport("__Internal")] private static extern void MangosBrowserSetPlayer(int ready, int canFly);
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
#if !UNITY_SERVER
        if (Instance == null) new GameObject("MangosBrowserBridge").AddComponent<MangosBrowserBridge>();
#endif
    }
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
#if UNITY_WEBGL && !UNITY_EDITOR
        MangosInputRouter.DeviceFlags = MangosBrowserDeviceFlags();
#else
        MangosInputRouter.DeviceFlags = Application.isMobilePlatform ? 9 : 0;
#endif
    }
    private void OnEnable()
    {
        EventHandler.ClientSpawnSuccessEvent += SetRoom;
        EventHandler.ClientDisconnectedEvent += ClearRoom;
        EventHandler.LocalClientCompleteSetupEvent += PlayerReady;
    }
    private void OnDisable()
    {
        EventHandler.ClientSpawnSuccessEvent -= SetRoom;
        EventHandler.ClientDisconnectedEvent -= ClearRoom;
        EventHandler.LocalClientCompleteSetupEvent -= PlayerReady;
        if (Instance == this) Instance = null;
    }
    public static void SetRoom(string roomId, uint playerId = 0)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        MangosBrowserSetSession(JsonUtility.ToJson(new Session { roomId = roomId ?? "", playerId = playerId }));
#endif
    }
    public static void ClearRoom()
    {
        SetRoom("");
        if (Instance != null) Instance.ResetPlayer();
    }
    private void PlayerReady()
    {
        localPlayer = UserReferencePersistent.Instance != null ? UserReferencePersistent.Instance.PlayerGameObject : null;
        inputs = localPlayer != null ? localPlayer.GetComponent<StarterAssetsInputs>() : null;
        movement = localPlayer != null ? localPlayer.GetComponent<PlayerMovementHandler>() : null;
        appliedFlags = -1;
#if UNITY_WEBGL && !UNITY_EDITOR
        MangosBrowserSetPlayer(localPlayer != null ? 1 : 0, localPlayer != null && localPlayer.GetComponent<FlyBehaviour>() != null ? 1 : 0);
#endif
    }
    private void ResetPlayer()
    {
        MangosInputRouter.NativeBlocked = false;
        MangosInputRouter.Reset(); ClearInputs();
        localPlayer = null; inputs = null; movement = null;
#if UNITY_WEBGL && !UNITY_EDITOR
        MangosBrowserSetPlayer(0, 0);
#endif
    }
    private void ClearInputs()
    {
        if (inputs == null) return;
        inputs.MoveInput(Vector2.zero); inputs.LookInput(Vector2.zero); inputs.JumpInput(false); inputs.SprintInput(false);
    }
    private void Update()
    {
        MangosInputRouter.BeginFrame();
        if (!HasBrowserControls || localPlayer == null) return;
        if (appliedFlags != MangosInputRouter.DeviceFlags || appliedBlocked != MangosInputRouter.Blocked)
        {
            ClearInputs();
            appliedFlags = MangosInputRouter.DeviceFlags; appliedBlocked = MangosInputRouter.Blocked;
            movement?.SetBrowserInputMode(MangosInputRouter.Touch, MangosInputRouter.Blocked);
            if (inputs != null) { inputs.cursorLocked = false; inputs.cursorInputForLook = !MangosInputRouter.Touch && !MangosInputRouter.Blocked; }
            if (MangosInputRouter.Touch) Cursor.lockState = CursorLockMode.None;
        }
        if (!MangosInputRouter.Touch || inputs == null) return;
        inputs.MoveInput(MangosInputRouter.Blocked ? Vector2.zero : MangosInputRouter.Move);
        inputs.LookInput(Vector2.zero); // The walking camera consumes the same per-frame delta as the fly camera.
        inputs.SprintInput(!MangosInputRouter.Blocked && MangosInputRouter.Sprint);
        if (jumpVersion != MangosInputRouter.JumpVersion)
        {
            jumpVersion = MangosInputRouter.JumpVersion;
            inputs.JumpInput(!MangosInputRouter.Blocked);
        }
        else if (!MangosInputRouter.Jump) inputs.JumpInput(false);
    }
    [Preserve] public void OnDeviceChanged(string value)
    {
        if (!int.TryParse(value, out var flags) || flags == MangosInputRouter.DeviceFlags) return;
        MangosInputRouter.Reset(); MangosInputRouter.DeviceFlags = flags;
    }
    [Preserve] public void OnInputFocus(string value)
    {
        MangosInputRouter.BrowserBlocked = value == "1"; MangosInputRouter.Reset(); ClearInputs();
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityEngine.WebGLInput.captureAllKeyboardInput = value != "1";
#endif
    }
    [Preserve] public void OnInput(string json)
    {
        if (!MangosInputRouter.Touch || MangosInputRouter.Blocked || localPlayer == null) return;
        var state = JsonUtility.FromJson<BrowserInput>(json);
        if (state == null) return;
        MangosInputRouter.Move = Vector2.ClampMagnitude(new Vector2(state.x, state.y), 1);
        if (state.lookToken != lookToken)
        {
            lookToken = state.lookToken; allowLook = true;
            if (EventSystem.current != null)
            {
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(state.px * Screen.width, state.py * Screen.height) }, results);
                allowLook = !results.Exists(result => result.module is UnityEngine.UI.GraphicRaycaster);
            }
        }
        if (allowLook) MangosInputRouter.PendingLook += Vector2.ClampMagnitude(new Vector2(state.lx, state.ly), 180);
        if (state.jump && !MangosInputRouter.Jump) MangosInputRouter.JumpVersion++;
        if (state.fly && !MangosInputRouter.Fly) MangosInputRouter.FlyVersion++;
        MangosInputRouter.Jump = state.jump; MangosInputRouter.Fly = state.fly;
        MangosInputRouter.Sprint = state.sprint; MangosInputRouter.Vertical = Mathf.Clamp(state.vertical, -1, 1);
    }
    private void OnApplicationFocus(bool focused) { if (!focused) { MangosInputRouter.Reset(); ClearInputs(); } }
    [Preserve]
    public void OnVoiceState(string json)
    {
        var state = JsonUtility.FromJson<VoiceState>(json);
        EventHandler.OnUserMicMuteUpdate(0, state == null || !state.connected || state.muted);
    }
}
