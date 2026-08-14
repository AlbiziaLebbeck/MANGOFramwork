using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace MANGOsFramework.Voice
{
    public enum VoiceCommunicationState
    {
        Unavailable,
        Idle,
        Connecting,
        Connected,
        Muted,
        Disconnecting,
        Failed
    }

    [Preserve]
    public sealed class VoiceCommunicationManager : MonoBehaviour
    {
        private const string ManagerObjectName = "VoiceCommunicationManager";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MangosVoiceJoin(
            string gameObjectName,
            string callbackMethod,
            string backendBaseUrl,
            string socketUrl,
            string socketPath,
            string roomId,
            string participantPrefix,
            string displayName,
            int requestTimeoutMilliseconds,
            int iceGatheringTimeoutMilliseconds,
            int subscribeRetryCount);

        [DllImport("__Internal")]
        private static extern void MangosVoiceSetMuted(int muted);

        [DllImport("__Internal")]
        private static extern void MangosVoiceLeave();
#endif

        [Serializable]
        private sealed class BrowserVoiceEvent
        {
            public string state;
            public string message;
            public bool muted;
        }

        public static VoiceCommunicationManager Instance { get; private set; }

        public event Action<VoiceCommunicationState, string> StateChanged;

        public VoiceCommunicationState State { get; private set; } = VoiceCommunicationState.Idle;
        public bool IsMuted { get; private set; }
        public bool IsConnected => State == VoiceCommunicationState.Connected || State == VoiceCommunicationState.Muted;

        private VoiceCommunicationConfig config;
        private string roomId;
        private string participantPrefix;
        private string displayName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureRuntimeInstance()
        {
            if (Instance != null)
            {
                return;
            }

            var managerObject = new GameObject(ManagerObjectName);
            managerObject.AddComponent<VoiceCommunicationManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            gameObject.name = ManagerObjectName;
            DontDestroyOnLoad(gameObject);
            config = Resources.Load<VoiceCommunicationConfig>(VoiceCommunicationConfig.ResourceName);
        }

        private void OnEnable()
        {
            EventHandler.ClientSpawnSuccessEvent += HandleClientSpawnSuccess;
            EventHandler.ClientDisconnectedEvent += HandleClientDisconnected;
        }

        private void OnDisable()
        {
            EventHandler.ClientSpawnSuccessEvent -= HandleClientSpawnSuccess;
            EventHandler.ClientDisconnectedEvent -= HandleClientDisconnected;
        }

        public void ToggleVoice()
        {
            if (State == VoiceCommunicationState.Connecting || State == VoiceCommunicationState.Disconnecting)
            {
                return;
            }

            if (!IsConnected)
            {
                JoinVoice();
                return;
            }

            SetMuted(!IsMuted);
        }

        public void JoinVoice()
        {
            if (IsConnected || State == VoiceCommunicationState.Connecting)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(roomId))
            {
                SetState(VoiceCommunicationState.Failed, "The multiplayer room is not ready.");
                return;
            }

            RefreshDisplayName();
            SetState(VoiceCommunicationState.Connecting, "Connecting voice communication...");

#if UNITY_WEBGL && !UNITY_EDITOR
            VoiceCommunicationConfig activeConfig = config;
            MangosVoiceJoin(
                gameObject.name,
                nameof(OnBrowserVoiceEvent),
                activeConfig != null ? activeConfig.BackendBaseUrl : VoiceCommunicationConfig.DefaultBackendBaseUrl,
                activeConfig != null ? activeConfig.SocketUrl : VoiceCommunicationConfig.DefaultSocketUrl,
                activeConfig != null ? activeConfig.SocketPath : VoiceCommunicationConfig.DefaultSocketPath,
                roomId,
                participantPrefix,
                displayName,
                activeConfig != null ? activeConfig.RequestTimeoutMilliseconds : 15000,
                activeConfig != null ? activeConfig.IceGatheringTimeoutMilliseconds : 5000,
                activeConfig != null ? activeConfig.SubscribeRetryCount : 5);
#else
            SetState(
                VoiceCommunicationState.Unavailable,
                "Browser voice communication is available only in a WebGL build.");
#endif
        }

        public void SetMuted(bool muted)
        {
            if (!IsConnected)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            MangosVoiceSetMuted(muted ? 1 : 0);
#else
            IsMuted = muted;
            SetState(muted ? VoiceCommunicationState.Muted : VoiceCommunicationState.Connected, string.Empty);
#endif
        }

        public void LeaveVoice()
        {
            if (State == VoiceCommunicationState.Idle || State == VoiceCommunicationState.Disconnecting)
            {
                return;
            }

            SetState(VoiceCommunicationState.Disconnecting, "Disconnecting voice communication...");

#if UNITY_WEBGL && !UNITY_EDITOR
            MangosVoiceLeave();
#else
            IsMuted = false;
            SetState(VoiceCommunicationState.Idle, string.Empty);
#endif
        }

        [Preserve]
        public void OnBrowserVoiceEvent(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                SetState(VoiceCommunicationState.Failed, "The browser voice bridge returned an empty event.");
                return;
            }

            BrowserVoiceEvent browserEvent;
            try
            {
                browserEvent = JsonUtility.FromJson<BrowserVoiceEvent>(json);
            }
            catch (Exception)
            {
                SetState(VoiceCommunicationState.Failed, "The browser voice bridge returned invalid data.");
                return;
            }

            if (browserEvent == null)
            {
                SetState(VoiceCommunicationState.Failed, "The browser voice bridge returned invalid data.");
                return;
            }

            IsMuted = browserEvent.muted;
            switch (browserEvent.state)
            {
                case "connecting":
                    SetState(VoiceCommunicationState.Connecting, browserEvent.message);
                    break;
                case "connected":
                    SetState(VoiceCommunicationState.Connected, browserEvent.message);
                    EventHandler.OnUserMicMuteUpdate(0, false);
                    break;
                case "muted":
                    SetState(VoiceCommunicationState.Muted, browserEvent.message);
                    EventHandler.OnUserMicMuteUpdate(0, true);
                    break;
                case "disconnecting":
                    SetState(VoiceCommunicationState.Disconnecting, browserEvent.message);
                    break;
                case "idle":
                    IsMuted = false;
                    SetState(VoiceCommunicationState.Idle, browserEvent.message);
                    EventHandler.OnUserMicMuteUpdate(0, true);
                    break;
                case "failed":
                    IsMuted = false;
                    SetState(VoiceCommunicationState.Failed, browserEvent.message);
                    EventHandler.OnUserMicMuteUpdate(0, true);
                    break;
                default:
                    SetState(VoiceCommunicationState.Failed, "The browser voice bridge returned an unknown state.");
                    break;
            }
        }

        private void HandleClientSpawnSuccess(string channelName, uint uid)
        {
            roomId = string.IsNullOrWhiteSpace(channelName) ? "default" : channelName.Trim();
            participantPrefix = uid > 0 ? $"player-{uid}" : "player";
            RefreshDisplayName();
            if (State == VoiceCommunicationState.Unavailable || State == VoiceCommunicationState.Failed)
            {
                SetState(VoiceCommunicationState.Idle, string.Empty);
            }
        }

        private void HandleClientDisconnected()
        {
            LeaveVoice();
            roomId = null;
            participantPrefix = null;
        }

        private void RefreshDisplayName()
        {
            string currentName = UserReferencePersistent.Instance != null
                ? UserReferencePersistent.Instance.Username
                : null;
            displayName = string.IsNullOrWhiteSpace(currentName) ? "MANGOs Player" : currentName.Trim();
        }

        private void SetState(VoiceCommunicationState nextState, string message)
        {
            State = nextState;
            if (nextState != VoiceCommunicationState.Muted && nextState != VoiceCommunicationState.Connected)
            {
                IsMuted = false;
            }

            StateChanged?.Invoke(nextState, message ?? string.Empty);
        }

        private void OnApplicationQuit()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MangosVoiceLeave();
#endif
        }
    }
}
