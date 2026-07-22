using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace MANGOsFramework.Experiment
{
    public enum AuthenticationMode
    {
        Auto,
        FirstPartyCookieSso,
        ThirdPartyOAuth
    }

    [CreateAssetMenu(fileName = "MangosAuthConfig", menuName = "MANGOsFramework/Authentication Config")]
    public class AuthConfig : ScriptableObject
    {
        public const string DefaultApiBaseUrl = "https://api.mangosgo.com/api/v1";
        public const string DefaultApiOrigin = "https://api.mangosgo.com";
        public const string DefaultWebBaseUrl = "https://mangosgo.com";

        [Header("Authentication")]
        [SerializeField] private AuthenticationMode authenticationMode = AuthenticationMode.Auto;
        [SerializeField, Min(1)] private int requestTimeoutSeconds = 20;

        [Header("MANGOs Endpoints")]
        [SerializeField] private string apiBaseUrl = DefaultApiBaseUrl;
        [SerializeField] private string apiOrigin = DefaultApiOrigin;
        [SerializeField] private string webBaseUrl = DefaultWebBaseUrl;

        [Header("OAuth Public Client Settings")]
        [FormerlySerializedAs("METAVERSE_NAME")]
        [SerializeField] private string metaverseName;
        [FormerlySerializedAs("METAVERSE_CLIENT_ID")]
        [SerializeField] private string oauthClientId;
        [FormerlySerializedAs("REDIRECT_URL")]
        [SerializeField] private string oauthRedirectUrl;

        public AuthenticationMode Mode => authenticationMode;
        public int RequestTimeoutSeconds => Mathf.Max(1, requestTimeoutSeconds);
        public string ApiBaseUrl => NormalizeUrl(apiBaseUrl, DefaultApiBaseUrl);
        public string ApiOrigin => NormalizeUrl(apiOrigin, DefaultApiOrigin);
        public string WebBaseUrl => NormalizeUrl(webBaseUrl, DefaultWebBaseUrl);
        public string MetaverseName => metaverseName;
        public string OAuthClientId => oauthClientId;
        public string OAuthRedirectUrl => oauthRedirectUrl;

        [Obsolete("Use MetaverseName.")]
        public string METAVERSE_NAME => MetaverseName;

        [Obsolete("Use OAuthClientId.")]
        public string METAVERSE_CLIENT_ID => OAuthClientId;

        [Obsolete("Use OAuthRedirectUrl.")]
        public string REDIRECT_URL => OAuthRedirectUrl;

        [Obsolete("Client secrets must not be stored in Unity assets. Use a trusted backend for OAuth code exchange.")]
        public string SECRET => string.Empty;

        private static string NormalizeUrl(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.TrimEnd('/');
        }
    }
}
