using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace MANGOsFramework.Experiment
{
    public sealed class MangosApiClient : MonoBehaviour
    {
        public const string GetAuthorizationCodeRoute = "/o-auth/get-auth-code";
        public const string GetBasicRoute = "/o-auth/get-basic";
        public const string ExchangeAuthorizationCodeRoute = "/o-auth/auth-to-access";
        public const string CurrentUserRoute = "/users/me";
        public const string AvatarsRoute = "/avatars";
        public const string FriendsRoute = "/friends/get-my-friends";
        public const string IncomingFriendRequestsRoute = "/friends/get-friend-requests";
        public const string OutgoingFriendRequestsRoute = "/friends/get-requests-of-me";
        public const string WalletRoute = "/mgo-gold/get-my-wallet";
        public const string MarketplaceItemsRoute = "/marketplace/get-items";
        public const string MarketplaceBuyRoute = "/marketplace/buy-item";
        public const string GlobalLogoutRoute = "/auth/logout";

        private const string FirstPartyRootHost = "mangosgo.com";

        private readonly Dictionary<string, BrowserPendingRequest> browserRequests =
            new Dictionary<string, BrowserPendingRequest>();

        private AuthConfig config;
        private AuthToAccessResponse oauthTokens;
        private DateTimeOffset oauthAccessTokenExpiresAt;
        private DateTimeOffset oauthRefreshTokenExpiresAt;

        public AuthenticationMode ResolvedMode { get; private set; } = AuthenticationMode.ThirdPartyOAuth;
        public UserGetOneResponse.UserData CurrentUser { get; private set; }
        public MangosGoldResponse.WalletData CurrentWallet { get; private set; }
        public AuthToAccessResponse OAuthTokens => oauthTokens;
        public bool HasValidOAuthAccessToken =>
            oauthTokens != null &&
            !string.IsNullOrWhiteSpace(oauthTokens.accessToken) &&
            oauthAccessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MangosFetchWithCredentials(
            string gameObjectName,
            string callbackMethod,
            string requestId,
            string url,
            string method,
            string jsonBody,
            int timeoutMilliseconds);

        [DllImport("__Internal")]
        private static extern void MangosCancelBrowserRequest(string requestId);

        [DllImport("__Internal")]
        private static extern void MangosRedirectToLogin(string webBaseUrl);
#endif

        public void Configure(AuthConfig authConfig)
        {
            config = authConfig;
            ResolvedMode = ResolveAuthenticationMode(authConfig != null ? authConfig.Mode : AuthenticationMode.Auto);
        }

        public AuthenticationMode ResolveAuthenticationMode(AuthenticationMode configuredMode)
        {
            if (configuredMode != AuthenticationMode.Auto)
            {
                return configuredMode;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (TryGetHost(Application.absoluteURL, out string host) && IsFirstPartyHost(host))
            {
                return AuthenticationMode.FirstPartyCookieSso;
            }
#endif
            return AuthenticationMode.ThirdPartyOAuth;
        }

        public void RedirectToFirstPartyLogin()
        {
            if (ResolvedMode != AuthenticationMode.FirstPartyCookieSso)
            {
                Debug.LogWarning("MANGOs login redirect is only available in First-party Cookie SSO mode.");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            MangosRedirectToLogin(WebBaseUrl);
#else
            Debug.LogWarning("First-party Cookie SSO login redirect is only available in a WebGL browser build.");
#endif
        }

        public bool ApplyOAuthTokens(AuthToAccessResponse tokens, out ApiError error)
        {
            error = null;
            if (tokens == null ||
                string.IsNullOrWhiteSpace(tokens.accessToken) ||
                string.IsNullOrWhiteSpace(tokens.refreshToken) ||
                !DateTimeOffset.TryParse(tokens.accessTokenExpiresAt, out DateTimeOffset accessExpiry) ||
                !DateTimeOffset.TryParse(tokens.refreshTokenExpiresAt, out DateTimeOffset refreshExpiry))
            {
                error = new ApiError(
                    ApiErrorKind.InvalidAuthentication,
                    "The OAuth token response is incomplete or has invalid expiration metadata.");
                ClearLocalAuthenticationState();
                return false;
            }

            if (accessExpiry <= DateTimeOffset.UtcNow)
            {
                error = new ApiError(ApiErrorKind.InvalidAuthentication, "The OAuth access token is expired.");
                ClearLocalAuthenticationState();
                return false;
            }

            if (refreshExpiry <= DateTimeOffset.UtcNow)
            {
                error = new ApiError(ApiErrorKind.InvalidAuthentication, "The OAuth refresh token is expired.");
                ClearLocalAuthenticationState();
                return false;
            }

            oauthTokens = tokens;
            oauthAccessTokenExpiresAt = accessExpiry;
            oauthRefreshTokenExpiresAt = refreshExpiry;
            ResolvedMode = AuthenticationMode.ThirdPartyOAuth;
            return true;
        }

        public void ClearLocalAuthenticationState()
        {
            oauthTokens = null;
            oauthAccessTokenExpiresAt = default;
            oauthRefreshTokenExpiresAt = default;
            CurrentUser = null;
            CurrentWallet = null;
        }

        public IEnumerator GetCurrentUser(
            Action<Response<UserGetOneResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            yield return SendAuthenticatedGet(
                CurrentUserRoute,
                (Response<UserGetOneResponse> response) =>
                {
                    if (response?.data?.User == null)
                    {
                        onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "The user response is missing data.User."));
                        return;
                    }

                    CurrentUser = response.data.User;
                    onSuccess?.Invoke(response);
                },
                onError,
                cancellationToken);
        }

        public IEnumerator GetAvatars(
            Action<Response<AvatarListResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            yield return SendAuthenticatedGet(
                AvatarsRoute,
                (Response<AvatarListResponse> response) =>
                {
                    if (response?.data?.Avatars == null)
                    {
                        onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "The avatar response is missing data.Avatars."));
                        return;
                    }

                    onSuccess?.Invoke(response);
                },
                onError,
                cancellationToken);
        }

        public IEnumerator GetWallet(
            Action<Response<MangosGoldResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            yield return SendAuthenticatedGet(
                WalletRoute,
                (Response<MangosGoldResponse> response) =>
                {
                    if (response?.data?.UserMgoGoldWallet == null)
                    {
                        onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "The wallet response is missing data.UserMgoGoldWallet."));
                        return;
                    }

                    CurrentWallet = response.data.UserMgoGoldWallet;
                    onSuccess?.Invoke(response);
                },
                onError,
                cancellationToken);
        }

        public IEnumerator GetFriends(
            Action<Response<FriendListResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            yield return SendAuthenticatedGet(
                FriendsRoute,
                (Response<FriendListResponse> response) =>
                {
                    if (response?.data?.Friends == null)
                    {
                        onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "The friend response is missing data.Friends."));
                        return;
                    }

                    onSuccess?.Invoke(response);
                },
                onError,
                cancellationToken);
        }

        public IEnumerator GetIncomingFriendRequests(
            Action<Response<FriendRequestListResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            yield return SendAuthenticatedGet(
                IncomingFriendRequestsRoute,
                (Response<FriendRequestListResponse> response) =>
                {
                    if (response?.data?.FriendRequests == null)
                    {
                        onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "The friend request response is missing data.FriendRequests."));
                        return;
                    }

                    onSuccess?.Invoke(response);
                },
                onError,
                cancellationToken);
        }

        public IEnumerator GetOutgoingFriendRequests(
            Action<Response<RequestedFriendListResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            yield return SendAuthenticatedGet(
                OutgoingFriendRequestsRoute,
                (Response<RequestedFriendListResponse> response) =>
                {
                    if (response?.data?.RequestedFriends == null)
                    {
                        onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "The outgoing friend request response is missing data.RequestedFriends."));
                        return;
                    }

                    onSuccess?.Invoke(response);
                },
                onError,
                cancellationToken);
        }

        public IEnumerator GetFriendProfile(
            string userId,
            Action<Response<FriendProfileResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "A friend user id is required."));
                yield break;
            }

            yield return SendAuthenticatedGet(
                "/friends/" + UnityWebRequest.EscapeURL(userId) + "/profile",
                onSuccess,
                onError,
                cancellationToken);
        }

        public IEnumerator GetMarketplaceItems(
            Action<Response<MarketplaceListResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            if (!EnsureMarketplaceAuthentication(onError))
            {
                yield break;
            }

            yield return SendAuthenticatedGet(MarketplaceItemsRoute, onSuccess, onError, cancellationToken);
        }

        public IEnumerator GetMarketplaceItem(
            int itemId,
            Action<Response<MarketplaceItemResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            if (!EnsureMarketplaceAuthentication(onError))
            {
                yield break;
            }

            yield return SendAuthenticatedGet(
                "/marketplace/get-item/" + itemId,
                onSuccess,
                onError,
                cancellationToken);
        }

        public IEnumerator BuyMarketplaceItem(
            int itemId,
            Action<Response<MarketplacePurchaseResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            if (!EnsureMarketplaceAuthentication(onError))
            {
                yield break;
            }

            yield return SendAuthenticated(
                UnityWebRequest.kHttpVerbPOST,
                MarketplaceBuyRoute,
                JsonUtility.ToJson(new MarketplaceBuyForm { itemId = itemId }),
                onSuccess,
                onError,
                cancellationToken);
        }

        public IEnumerator RequestAuthorizationCode(
            string email,
            string password,
            string clientId,
            string redirectUrl,
            Action<Response<GetAuthCodeResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(clientId))
            {
                onError?.Invoke(new ApiError(ApiErrorKind.MissingData, "Email, password, and OAuth client id are required."));
                yield break;
            }

            string route = GetAuthorizationCodeRoute + "?metaverseClientId=" + UnityWebRequest.EscapeURL(clientId);
            if (!string.IsNullOrWhiteSpace(redirectUrl))
            {
                route += "&redirectUrl=" + UnityWebRequest.EscapeURL(redirectUrl);
            }

            yield return SendUnauthenticated(
                UnityWebRequest.kHttpVerbPOST,
                route,
                JsonUtility.ToJson(new GetAuthCodeForm { email = email, password = password }),
                null,
                onSuccess,
                onError,
                cancellationToken);
        }

        public IEnumerator ExchangeAuthorizationCode(
            string authCode,
            string runtimeBasicCredential,
            Action<Response<AuthToAccessResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            onError?.Invoke(new ApiError(
                ApiErrorKind.SecurityConfiguration,
                "OAuth code exchange is disabled in WebGL. Exchange the code on a trusted backend."));
            yield break;
#else
            if (string.IsNullOrWhiteSpace(authCode) || string.IsNullOrWhiteSpace(runtimeBasicCredential))
            {
                onError?.Invoke(new ApiError(
                    ApiErrorKind.SecurityConfiguration,
                    "A runtime Basic credential from a secure source is required for OAuth code exchange."));
                yield break;
            }

            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "Authorization", "Basic " + runtimeBasicCredential }
            };

            yield return SendUnauthenticated(
                UnityWebRequest.kHttpVerbPOST,
                ExchangeAuthorizationCodeRoute,
                JsonUtility.ToJson(new AuthToAccessForm { authCode = authCode }),
                headers,
                onSuccess,
                onError,
                cancellationToken);
#endif
        }

        public IEnumerator GetBasicCredential(
            string clientId,
            string clientSecret,
            Action<Response<GetBasicResponse>> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken = default)
        {
            onError?.Invoke(new ApiError(
                ApiErrorKind.SecurityConfiguration,
                "POST /o-auth/get-basic must run on a trusted backend; Unity clients must not handle a client secret."));
            yield break;
        }

        public IEnumerator GlobalLogout(Action onSuccess, Action<ApiError> onError)
        {
            if (ResolvedMode != AuthenticationMode.FirstPartyCookieSso)
            {
                onError?.Invoke(new ApiError(
                    ApiErrorKind.UnsupportedAuthenticationMode,
                    "Global MANGOs logout is only documented for First-party Cookie SSO."));
                yield break;
            }

            BrowserResponse response = null;
            ApiError requestError = null;
            yield return SendCookieRaw(
                UnityWebRequest.kHttpVerbPOST,
                GlobalLogoutRoute,
                null,
                value => response = value,
                error => requestError = error,
                CancellationToken.None);

            if (requestError != null)
            {
                onError?.Invoke(requestError);
                yield break;
            }

            if (response == null || response.statusCode < 200 || response.statusCode >= 300)
            {
                onError?.Invoke(ApiService.CreateHttpError(response?.statusCode ?? 0));
                yield break;
            }

            ClearLocalAuthenticationState();
            onSuccess?.Invoke();
        }

        public string ResolveAssetUrl(string assetUrl)
        {
            if (string.IsNullOrWhiteSpace(assetUrl))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(assetUrl, UriKind.Absolute, out Uri absolute))
            {
                return absolute.ToString();
            }

            string path = assetUrl.StartsWith("/", StringComparison.Ordinal) ? assetUrl : "/" + assetUrl;
            return ApiOrigin + path;
        }

        public void OnMangosBrowserResponse(string responseJson)
        {
            BrowserResponse response;
            try
            {
                response = JsonUtility.FromJson<BrowserResponse>(responseJson);
            }
            catch (Exception)
            {
                return;
            }

            if (response == null || string.IsNullOrWhiteSpace(response.requestId))
            {
                return;
            }

            if (browserRequests.TryGetValue(response.requestId, out BrowserPendingRequest pending))
            {
                pending.response = response;
                pending.isDone = true;
            }
        }

        private IEnumerator SendAuthenticatedGet<T>(
            string route,
            Action<T> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken)
            where T : class
        {
            yield return SendAuthenticated(
                UnityWebRequest.kHttpVerbGET,
                route,
                null,
                onSuccess,
                onError,
                cancellationToken);
        }

        private IEnumerator SendAuthenticated<T>(
            string method,
            string route,
            string jsonBody,
            Action<T> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken)
            where T : class
        {
            if (ResolvedMode == AuthenticationMode.FirstPartyCookieSso)
            {
                BrowserResponse response = null;
                ApiError requestError = null;
                yield return SendCookieRaw(method, route, jsonBody, value => response = value, error => requestError = error, cancellationToken);

                if (requestError != null)
                {
                    onError?.Invoke(requestError);
                    yield break;
                }

                if (response.statusCode < 200 || response.statusCode >= 300)
                {
                    ApiError httpError = ApiService.CreateHttpError(response.statusCode);
                    if (response.statusCode == 401)
                    {
                        ClearLocalAuthenticationState();
                    }
                    onError?.Invoke(httpError);
                    yield break;
                }

                if (!ApiService.TryDeserialize(response.body, out T parsed, out ApiError parseError))
                {
                    onError?.Invoke(parseError);
                    yield break;
                }

                onSuccess?.Invoke(parsed);
                yield break;
            }

            if (!HasValidOAuthAccessToken)
            {
                ClearLocalAuthenticationState();
                onError?.Invoke(new ApiError(
                    ApiErrorKind.InvalidAuthentication,
                    "A valid OAuth access token is required."));
                yield break;
            }

            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + oauthTokens.accessToken }
            };

            ApiError oauthError = null;
            yield return SendUnauthenticated(method, route, jsonBody, headers, onSuccess, error => oauthError = error, cancellationToken);
            if (oauthError != null)
            {
                if (oauthError.statusCode == 401)
                {
                    ClearLocalAuthenticationState();
                }
                onError?.Invoke(oauthError);
            }
        }

        private IEnumerator SendUnauthenticated<T>(
            string method,
            string route,
            string jsonBody,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken)
            where T : class
        {
            yield return ApiService.SendCoroutine(
                method,
                BuildApiUrl(route),
                jsonBody,
                headers,
                new ApiRequestOptions
                {
                    TimeoutSeconds = RequestTimeoutSeconds,
                    CancellationToken = cancellationToken
                },
                onSuccess,
                onError);
        }

        private IEnumerator SendCookieRaw(
            string method,
            string route,
            string jsonBody,
            Action<BrowserResponse> onSuccess,
            Action<ApiError> onError,
            CancellationToken cancellationToken)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string requestId = Guid.NewGuid().ToString("N");
            BrowserPendingRequest pending = new BrowserPendingRequest();
            browserRequests.Add(requestId, pending);
            float deadline = Time.realtimeSinceStartup + RequestTimeoutSeconds + 1f;

            MangosFetchWithCredentials(
                gameObject.name,
                nameof(OnMangosBrowserResponse),
                requestId,
                BuildApiUrl(route),
                method,
                jsonBody ?? string.Empty,
                RequestTimeoutSeconds * 1000);

            while (!pending.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    MangosCancelBrowserRequest(requestId);
                    browserRequests.Remove(requestId);
                    onError?.Invoke(new ApiError(ApiErrorKind.Cancelled, "The request was cancelled."));
                    yield break;
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    MangosCancelBrowserRequest(requestId);
                    browserRequests.Remove(requestId);
                    onError?.Invoke(new ApiError(ApiErrorKind.Timeout, "The API request timed out."));
                    yield break;
                }
                yield return null;
            }

            browserRequests.Remove(requestId);
            if (!string.IsNullOrWhiteSpace(pending.response.error))
            {
                ApiErrorKind kind = string.Equals(pending.response.errorKind, "timeout", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorKind.Timeout
                    : ApiErrorKind.Network;
                onError?.Invoke(new ApiError(kind, kind == ApiErrorKind.Timeout
                    ? "The API request timed out."
                    : "The API could not be reached."));
                yield break;
            }

            onSuccess?.Invoke(pending.response);
#else
            onError?.Invoke(new ApiError(
                ApiErrorKind.UnsupportedAuthenticationMode,
                "Credentialed Cookie SSO requests require a WebGL browser build."));
            yield break;
#endif
        }

        private bool EnsureMarketplaceAuthentication(Action<ApiError> onError)
        {
            if (ResolvedMode == AuthenticationMode.FirstPartyCookieSso)
            {
                return true;
            }

            onError?.Invoke(new ApiError(
                ApiErrorKind.UnsupportedAuthenticationMode,
                "Marketplace routes do not currently accept OAuth access tokens."));
            return false;
        }

        private string BuildApiUrl(string route)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                return ApiBaseUrl;
            }

            return ApiBaseUrl + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
        }

        private string ApiBaseUrl => config != null ? config.ApiBaseUrl : AuthConfig.DefaultApiBaseUrl;
        private string ApiOrigin => config != null ? config.ApiOrigin : AuthConfig.DefaultApiOrigin;
        private string WebBaseUrl => config != null ? config.WebBaseUrl : AuthConfig.DefaultWebBaseUrl;
        private int RequestTimeoutSeconds => config != null ? config.RequestTimeoutSeconds : 20;

        private static bool TryGetHost(string absoluteUrl, out string host)
        {
            host = null;
            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            host = uri.Host;
            return !string.IsNullOrWhiteSpace(host);
        }

        private static bool IsFirstPartyHost(string host)
        {
            return string.Equals(host, FirstPartyRootHost, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + FirstPartyRootHost, StringComparison.OrdinalIgnoreCase);
        }

        private void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            foreach (string requestId in browserRequests.Keys)
            {
                MangosCancelBrowserRequest(requestId);
            }
#endif
            browserRequests.Clear();
        }

        [Serializable]
        private sealed class BrowserResponse
        {
            public string requestId;
            public long statusCode;
            public string body;
            public string error;
            public string errorKind;
        }

        private sealed class BrowserPendingRequest
        {
            public bool isDone;
            public BrowserResponse response;
        }
    }
}
