using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

[Preserve]
public sealed class MangosBrowserBridge : MonoBehaviour
{
    public static MangosBrowserBridge Instance { get; private set; }
    [Serializable] private sealed class Session { public string roomId; public uint playerId; }
    [Serializable] private sealed class VoiceState { public bool connected; public bool muted; }
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void MangosBrowserSetSession(string json);
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
    }
    private void OnEnable()
    {
        EventHandler.ClientSpawnSuccessEvent += SetRoom;
        EventHandler.ClientDisconnectedEvent += ClearRoom;
    }
    private void OnDisable()
    {
        EventHandler.ClientSpawnSuccessEvent -= SetRoom;
        EventHandler.ClientDisconnectedEvent -= ClearRoom;
        if (Instance == this) Instance = null;
    }
    public static void SetRoom(string roomId, uint playerId = 0)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        MangosBrowserSetSession(JsonUtility.ToJson(new Session { roomId = roomId ?? "", playerId = playerId }));
#endif
    }
    public static void ClearRoom() => SetRoom("");
    [Preserve]
    public void OnVoiceState(string json)
    {
        var state = JsonUtility.FromJson<VoiceState>(json);
        EventHandler.OnUserMicMuteUpdate(0, state == null || !state.connected || state.muted);
    }
}
