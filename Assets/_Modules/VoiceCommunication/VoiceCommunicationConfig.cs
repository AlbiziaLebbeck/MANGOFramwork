using UnityEngine;

namespace MANGOsFramework.Voice
{
    [CreateAssetMenu(
        fileName = "VoiceCommunicationConfig",
        menuName = "MANGOs Framework/Voice Communication Config")]
    public sealed class VoiceCommunicationConfig : ScriptableObject
    {
        public const string ResourceName = "VoiceCommunicationConfig";
        public const string DefaultBackendBaseUrl = "https://apischool.mangosgo.com/video-streaming";
        public const string DefaultSocketUrl = "https://apischool.mangosgo.com";
        public const string DefaultSocketPath = "/video-streaming/socket.io";

        [SerializeField] private string backendBaseUrl = DefaultBackendBaseUrl;
        [SerializeField] private string socketUrl = DefaultSocketUrl;
        [SerializeField] private string socketPath = DefaultSocketPath;
        [SerializeField, Min(1000)] private int requestTimeoutMilliseconds = 15000;
        [SerializeField, Min(1000)] private int iceGatheringTimeoutMilliseconds = 5000;
        [SerializeField, Range(1, 10)] private int subscribeRetryCount = 5;

        public string BackendBaseUrl => NormalizeUrl(backendBaseUrl, DefaultBackendBaseUrl);
        public string SocketUrl => NormalizeUrl(socketUrl, DefaultSocketUrl);
        public string SocketPath => string.IsNullOrWhiteSpace(socketPath)
            ? DefaultSocketPath
            : socketPath.Trim();
        public int RequestTimeoutMilliseconds => Mathf.Max(1000, requestTimeoutMilliseconds);
        public int IceGatheringTimeoutMilliseconds => Mathf.Max(1000, iceGatheringTimeoutMilliseconds);
        public int SubscribeRetryCount => Mathf.Clamp(subscribeRetryCount, 1, 10);

        private static string NormalizeUrl(string value, string fallback)
        {
            string selected = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return selected.TrimEnd('/');
        }
    }
}
