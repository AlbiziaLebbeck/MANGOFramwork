using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace MANGOsFramework.Experiment
{
    public class Launcher : MonoBehaviour
    {
        [Header("Setup")]
        public string WorldSceneName;

        [Header("References")]
        [SerializeField] private AuthenticationCanvas authCanvas;

        [Header("Flags")]
        public bool HasLogedIn;

        [Header("Default Settings")]
        public string defaultAvatarLink;
        public string defaultName;

        [Header("Authentication Settings")]
        [SerializeField] private AuthConfig config;

        public Response<AuthToAccessResponse> accessResponse = new Response<AuthToAccessResponse>();
        public Response<UserGetOneResponse> userResponse = new Response<UserGetOneResponse>();
        public Response<AvatarListResponse> avatarResponse = new Response<AvatarListResponse>();
        public Response<MangosGoldResponse> goldResponse = new Response<MangosGoldResponse>();

        public AuthenticationMode ResolvedAuthenticationMode =>
            apiClient != null ? apiClient.ResolvedMode : AuthenticationMode.ThirdPartyOAuth;

        public MangosApiClient ApiClient => apiClient;

        private MangosApiClient apiClient;
        private CancellationTokenSource lifetimeCancellation;

        private void Awake()
        {
            apiClient = GetComponent<MangosApiClient>();
            if (apiClient == null)
            {
                apiClient = gameObject.AddComponent<MangosApiClient>();
            }

            apiClient.Configure(config);
            lifetimeCancellation = new CancellationTokenSource();
        }

        private void Start()
        {
            if (PersistentCanvas.LoadingCanvas != null)
            {
                PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
            }

            if (authCanvas == null)
            {
                Debug.LogError("AuthenticationCanvas is not assigned to Launcher.");
                return;
            }

            authCanvas.Init(false);
            StartCoroutine(CheckCurrentSession());
        }

        private IEnumerator CheckCurrentSession()
        {
            UpdateLoadingText("Checking MANGOs session...");

            if (apiClient.ResolvedMode == AuthenticationMode.ThirdPartyOAuth &&
                !apiClient.HasValidOAuthAccessToken)
            {
                SetAnonymousState("Connect to MANGOs when you need an authenticated feature.");
                yield break;
            }

            Response<UserGetOneResponse> currentUserResponse = null;
            ApiError sessionError = null;
            yield return apiClient.GetCurrentUser(
                response => currentUserResponse = response,
                error => sessionError = error,
                lifetimeCancellation.Token);

            if (sessionError != null)
            {
                if (sessionError.kind == ApiErrorKind.InvalidAuthentication || sessionError.statusCode == 401)
                {
                    apiClient.ClearLocalAuthenticationState();
                    SetAnonymousState("No active MANGOs session. You can continue as a guest.");
                }
                else
                {
                    SetAnonymousState(sessionError.ToSafeMessage());
                }
                yield break;
            }

            userResponse = currentUserResponse;
            HasLogedIn = true;
            yield return LoadAuthenticatedUserResources();

            string firstName = userResponse.data.User.firstName;
            authCanvas.DisplayUsername(string.IsNullOrWhiteSpace(firstName) ? "MANGOs user" : firstName);
            authCanvas.Init(true);
            UpdateLoadingText("MANGOs session is ready.");
        }

        private IEnumerator LoadAuthenticatedUserResources()
        {
            ApiError avatarError = null;
            yield return apiClient.GetAvatars(
                response => avatarResponse = response,
                error => avatarError = error,
                lifetimeCancellation.Token);

            if (avatarError != null)
            {
                avatarResponse = new Response<AvatarListResponse>();
                Debug.LogWarning("MANGOs avatars could not be loaded: " + avatarError.ToSafeMessage());
            }

            ApiError walletError = null;
            yield return apiClient.GetWallet(
                response => goldResponse = response,
                error => walletError = error,
                lifetimeCancellation.Token);

            if (walletError != null)
            {
                goldResponse = new Response<MangosGoldResponse>();
                Debug.LogWarning("MANGOs Gold wallet could not be loaded: " + walletError.ToSafeMessage());
            }
        }

        public void TryLogin()
        {
            if (apiClient.ResolvedMode == AuthenticationMode.FirstPartyCookieSso)
            {
                UpdateLoadingText("Redirecting to MANGOs login...");
                apiClient.RedirectToFirstPartyLogin();
                return;
            }

            CommonErrorFallback(
                "Third-party OAuth requires a trusted code-exchange backend. " +
                "Apply its documented token response with ApplyOAuthTokens after the secure exchange completes.");
        }

        public bool ApplyOAuthTokens(AuthToAccessResponse tokens)
        {
            if (!apiClient.ApplyOAuthTokens(tokens, out ApiError error))
            {
                CommonErrorFallback(error.ToSafeMessage());
                return false;
            }

            accessResponse = new Response<AuthToAccessResponse>
            {
                status = "success",
                data = tokens
            };

            StopAllCoroutines();
            StartCoroutine(CheckCurrentSession());
            return true;
        }

        public void DisconnectLocal()
        {
            apiClient.ClearLocalAuthenticationState();
            HasLogedIn = false;
            userResponse = new Response<UserGetOneResponse>();
            avatarResponse = new Response<AvatarListResponse>();
            goldResponse = new Response<MangosGoldResponse>();
            authCanvas.ResetCanvas();
            UpdateLoadingText("Disconnected from this application.");
        }

        public void LogoutFromMangosGlobally()
        {
            StartCoroutine(apiClient.GlobalLogout(
                () =>
                {
                    DisconnectLocal();
                    UpdateLoadingText("Logged out from MANGOs.");
                },
                error => CommonErrorFallback(error.ToSafeMessage())));
        }

        [Obsolete("The legacy popup flow used obsolete endpoints. Use a trusted OAuth exchange backend and ApplyOAuthTokens.")]
        public void OnAuthSuccess(string authCode)
        {
            CommonErrorFallback(
                "An authorization code was received, but secure OAuth exchange is not configured. " +
                "Do not embed the client secret in this Unity build.");
        }

        private void ApplyAuthenticatedUserToRuntime()
        {
            UserGetOneResponse.UserData user = userResponse?.data?.User;
            if (user == null || UserReferencePersistent.Instance == null)
            {
                CommonErrorFallback("Authenticated user data is unavailable.");
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(user.firstName)
                ? (string.IsNullOrWhiteSpace(user.email) ? defaultName : user.email)
                : user.firstName;
            UserReferencePersistent.Instance.SetUserName(displayName);

            AvatarData selectedAvatar = null;
            AvatarData[] avatars = avatarResponse?.data?.Avatars;
            if (avatars != null)
            {
                for (int index = 0; index < avatars.Length; index++)
                {
                    AvatarData avatar = avatars[index];
                    if (avatar == null || string.IsNullOrWhiteSpace(avatar.url))
                    {
                        continue;
                    }

                    avatar.url = apiClient.ResolveAssetUrl(avatar.url);
                    if (AvatarSystem.Instance != null)
                    {
                        AvatarSystem.Instance.AddNewUserAvatar(avatar.url);
                    }

                    if (selectedAvatar == null || avatar.isDefault)
                    {
                        selectedAvatar = avatar;
                    }
                }
            }

            string avatarUrl = selectedAvatar != null
                ? selectedAvatar.url
                : apiClient.ResolveAssetUrl(defaultAvatarLink);
            UserReferencePersistent.Instance.SetGLTFLink(
                string.IsNullOrWhiteSpace(avatarUrl) ? "default" : avatarUrl);

            if (goldResponse?.data?.UserMgoGoldWallet != null)
            {
                UserReferencePersistent.Instance.SetMangosGold(goldResponse.data.UserMgoGoldWallet.amount);
            }

            EventHandler.OnClientLogin();
        }

        private void SetAnonymousState(string message)
        {
            HasLogedIn = false;
            authCanvas.Init(false);
            UpdateLoadingText(message);
        }

        private void CommonErrorFallback(string errorMessage, Action fallback = null)
        {
            Debug.LogError("MANGOs authentication error: " + errorMessage);
            authCanvas.ResetCanvas();
            UpdateLoadingText(errorMessage);
            fallback?.Invoke();
        }

        private void OnEnable()
        {
            if (authCanvas == null)
            {
                return;
            }

            authCanvas.OnLogin += AuthCanvas_OnLogin;
            authCanvas.OnLoginAsGuest += AuthCanvas_OnLoginAsGuest;
            authCanvas.OnContinueAsUser += AuthCanvas_OnContinueAsUser;
            authCanvas.OnLoginAsNewUser += AuthCanvas_OnLoginAsNewUser;
        }

        private void OnDisable()
        {
            if (authCanvas == null)
            {
                return;
            }

            authCanvas.OnLogin -= AuthCanvas_OnLogin;
            authCanvas.OnLoginAsGuest -= AuthCanvas_OnLoginAsGuest;
            authCanvas.OnContinueAsUser -= AuthCanvas_OnContinueAsUser;
            authCanvas.OnLoginAsNewUser -= AuthCanvas_OnLoginAsNewUser;
        }

        private void AuthCanvas_OnLoginAsNewUser()
        {
            DisconnectLocal();
        }

        private void AuthCanvas_OnContinueAsUser()
        {
            ApplyAuthenticatedUserToRuntime();
        }

        private void AuthCanvas_OnLoginAsGuest(string guestName)
        {
            apiClient.ClearLocalAuthenticationState();
            HasLogedIn = false;
            UpdateLoadingText("Joining as a guest...");

            if (UserReferencePersistent.Instance == null)
            {
                CommonErrorFallback("UserReferencePersistent is unavailable.");
                return;
            }

            UserReferencePersistent.Instance.SetUserName(
                string.IsNullOrWhiteSpace(guestName) ? defaultName : guestName);

            if (AvatarSystem.Instance != null && AvatarSystem.Instance.AvatarUrls.Count > 0)
            {
                UserReferencePersistent.Instance.SetGLTFLink(AvatarSystem.Instance.AvatarUrls[0]);
            }
            else
            {
                string fallbackAvatar = apiClient.ResolveAssetUrl(defaultAvatarLink);
                UserReferencePersistent.Instance.SetGLTFLink(
                    string.IsNullOrWhiteSpace(fallbackAvatar) ? "default" : fallbackAvatar);
            }

            EventHandler.OnClientLogin();
        }

        private void AuthCanvas_OnLogin()
        {
            TryLogin();
        }

        private void UpdateLoadingText(string message)
        {
            authCanvas?.UpdateFooterMessage(message);
        }

        private void OnDestroy()
        {
            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
        }
    }
}
