#if UNITY_WEBGL && !UNITY_EDITOR
    using System.Runtime.InteropServices;
#endif

using System;
using System.Collections;
using System.Collections.Generic;
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

        [Header("DefaultSettings")]
        public string defaultAvatarLink;
        public string defaultName;

        [Header("OAuth Settings")]
        [SerializeField] private AuthConfig config;
        [SerializeField] private string mockAuthCode;
        private const string AUTH_TO_ACCESS_ROUTE = "/o-auth/auth-to-access";
        private string _basic;

        public Response<AuthToAccessResponse> accessResponse = new();
        public Response<UserGetOneResponse> userResponse = new();

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void OpenOAuthPopup(string gameObject, string callbackMethod, string authUrl, string redirUrl);

        [DllImport("__Internal")]
        private static extern int HasLoggedIn();

        [DllImport("__Internal")]
        private static extern void Logout();

        [DllImport("__Internal")]
        private static extern void SaveToLocalStorage(string key, string value);

        [DllImport("__Internal")]
        private static extern string GetLocalStorage(string key);
#endif

        private void Start()
        {
            if(PersistentCanvas.LoadingCanvas != null) PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
            UpdateLoadingText("Check Login Session ...");

            HasLogedIn = CheckHasLoggedIn();

            if(HasLogedIn)
            {
                string _accessToken = string.Empty;
                string _userId = string.Empty;

#if UNITY_WEBGL && !UNITY_EDITOR
                _accessToken = GetLocalStorage("metaauth_accessToken");
                _userId = GetLocalStorage("metaauth_userId");
#endif

                if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_userId))
                {
                    CommonErrorFallback("Failed, cannot get accessToken or userId from local storage.");
                    authCanvas.Init(false);
                    return;
                }

                StartCoroutine(ApiService.GetCoroutine<Response<UserGetOneResponse>>(
                    url: config.HOST + "/users/get-one/" + _userId,
                    headers: new Dictionary<string, string>()
                    {
                                { "Authorization", "Bearer " + _accessToken }
                    },
                    onSuccess: (response) =>
                    {
                        userResponse = response;
                        UpdateLoadingText($"Welcome {userResponse.data.User.firstName} !");
                        authCanvas.DisplayUsername(userResponse.data.User.firstName);
                        authCanvas.Init(true);

                    },
                    onError: (error) =>
                    {
                        CommonErrorFallback(error);
                    }
                    ));

            }
            else
            {
                authCanvas.Init(false);
            }
        }

        public void TryLogin()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string redir = "http://localhost:8000";
            string authUrl = $"https://auth.mangosgo.com/oauth-login?metaverseClientId={config.METAVERSE_CLIENT_ID}&scope=users_get-one";
            OpenOAuthPopup(gameObject.name, "OnAuthSuccess", authUrl, redir);
#else
            Debug.Log("OAuth login only works in WebGL builds.");
            UpdateLoadingText("OAuth login only works in WebGL builds. Runs Mock Login");
            StartCoroutine(RunMockLogin());
#endif
        }

        private bool CheckHasLoggedIn()
        {
            // Do Check login logic
#if UNITY_WEBGL && !UNITY_EDITOR
            return HasLoggedIn() == 1;
#else
            return false;
#endif
        }

        private void CommonErrorFallback(string errorMessage, Action fallback = null)
        {
            Debug.LogError("Error: " + errorMessage);

            authCanvas.ResetCanvas();

            fallback?.Invoke();
        }

        #region Mock

#if UNITY_EDITOR
        private IEnumerator RunMockLogin()
        {
            bool success = false;

            yield return StartCoroutine(ApiService.PostCoroutine<Response<GetBasicResponse>>(
                 url: config.HOST + "/public/generate-basic",
                 jsonBody: JsonUtility.ToJson(new GetBasicForm()
                 {
                     metaverseClientId = config.METAVERSE_CLIENT_ID,
                     secret = config.SECRET
                 }),
                 headers: null,
                 onSuccess: (response) =>
                 {
                     _basic = response.data.message;
                     success = true;
                 },
                 onError: (error) =>
                 {
                     CommonErrorFallback(error);
                     success = false;
                 }));


            if (!success) yield break;
            
            yield return StartCoroutine(ApiService.PostCoroutine<Response<AuthToAccessResponse>>(
                url: config.AUTH_HOST + AUTH_TO_ACCESS_ROUTE,
                jsonBody: JsonUtility.ToJson(new AuthToAccessForm()
                {
                    authCode = mockAuthCode
                }),
                headers: new Dictionary<string, string>()
                {
                    {"authorization", $"Basic {_basic}"}
                },
                onSuccess: (response) =>
                {
                    accessResponse = response;
                    UpdateLoadingText("Get accessToken success!");
                    success = true;
                },
                onError: (error) =>
                {
                    CommonErrorFallback(error);
                    success = false;
                }));

            if (!success) yield break;

            yield return StartCoroutine(ApiService.GetCoroutine<Response<UserGetOneResponse>>(
                url: config.HOST + "/users/get-one/" + accessResponse.data.userId,
                headers: new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + accessResponse.data.accessToken }
                },
                onSuccess: (response) =>
                {
                    userResponse = response;
                    UpdateLoadingText($"Welcome {userResponse.data.User.firstName} !");
                    success = true;
                },
                onError: (error) =>
                {
                    CommonErrorFallback(error);
                    success = false;
                }
                ));

            if (success)
            {
                if(UserReferencePersistent.Instance != null)
                {
                    UserReferencePersistent.Instance.SetUserName(userResponse.data.User.firstName);
                    UserReferencePersistent.Instance.SetGLTFLink(userResponse.data.User.Avatars[0].url);

                    for (int i = 0; i < userResponse.data.User.Avatars.Length; i++)
                    {
                        AvatarSystem.Instance.AddNewUserAvatar(userResponse.data.User.Avatars[i].url);
                        yield return null;
                    }

                    EventHandler.OnClientLogin();
                }
            }
        }
#endif
        #endregion

        #region Login Canvas

        private void OnEnable()
        {
            authCanvas.OnLogin += AuthCanvas_OnLogin;
            authCanvas.OnLoginAsGuest += AuthCanvas_OnLoginAsGuest;

            authCanvas.OnContinueAsUser += AuthCanvas_OnContinueAsUser;
            authCanvas.OnLoginAsNewUser += AuthCanvas_OnLoginAsNewUser;
        }

        private void OnDisable()
        {
            authCanvas.OnLogin -= AuthCanvas_OnLogin;
            authCanvas.OnLoginAsGuest -= AuthCanvas_OnLoginAsGuest;

            authCanvas.OnContinueAsUser -= AuthCanvas_OnContinueAsUser;
            authCanvas.OnLoginAsNewUser -= AuthCanvas_OnLoginAsNewUser;
        }

        private void AuthCanvas_OnLoginAsNewUser()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Logout();
#endif
            authCanvas.ResetCanvas();
        }

        private void AuthCanvas_OnContinueAsUser()
        {
 
            if(userResponse != null)
            {
                if (UserReferencePersistent.Instance != null)
                {
                    UserReferencePersistent.Instance.SetUserName(userResponse.data.User.firstName);
                    UserReferencePersistent.Instance.SetGLTFLink(userResponse.data.User.Avatars[0].url);

                    for (int i = 0; i < userResponse.data.User.Avatars.Length; i++)
                    {
                        AvatarSystem.Instance.AddNewUserAvatar(userResponse.data.User.Avatars[i].url);
                    }

                    EventHandler.OnClientLogin();
                }
            }
        }

        private void AuthCanvas_OnLoginAsGuest(string guestName)
        {
            Debug.Log("User will join as Guest...");
            UpdateLoadingText("User will join as Guest...");
            if (UserReferencePersistent.Instance != null)
            {
                if (!string.IsNullOrEmpty(guestName))
                {
                    UserReferencePersistent.Instance.SetUserName(guestName);
                }
                else
                {
                    UserReferencePersistent.Instance.SetUserName(defaultName);
                }

                if(AvatarSystem.Instance.AvatarUrls.Count > 0)
                {
                    UserReferencePersistent.Instance.SetGLTFLink(AvatarSystem.Instance.AvatarUrls[0]);
                }
                else
                {
                    if(!string.IsNullOrEmpty(defaultAvatarLink))
                    {
                        UserReferencePersistent.Instance.SetGLTFLink(defaultAvatarLink);
                    }
                    else
                    {
                        UserReferencePersistent.Instance.SetGLTFLink("default");
                    }
                }


                EventHandler.OnClientLogin();
            }
        }

        private void AuthCanvas_OnLogin()
        {
            Debug.Log("Try login... connecting to MetaAuth...");
            UpdateLoadingText("Try login... connecting to MetaAuth...");

            TryLogin();
        }

        private void UpdateLoadingText(string _loadingString)
        {
            authCanvas.UpdateFooterMessage(_loadingString);
        }
        #endregion

        #region Auth Callback
        public void OnAuthSuccess(string resultJson)
        {

            if(!string.IsNullOrEmpty(resultJson) && resultJson != "ERROR_NO_AUTHCODE")
            {
                UpdateLoadingText("Get AuthCode success! trying to get accessToken");   
                StartCoroutine(RunFullAuthenticationStep(resultJson));
            }
            else
            {
                UpdateLoadingText("Get AuthCode failed! please try again...");

                authCanvas.ResetCanvas();
            }
        }

        private IEnumerator RunFullAuthenticationStep(string _authCode)
        {
            bool success = false;

            yield return StartCoroutine(ApiService.PostCoroutine<Response<GetBasicResponse>>(
                 url: config.HOST + "/public/generate-basic",
                 jsonBody: JsonUtility.ToJson(new GetBasicForm()
                 {
                     metaverseClientId = config.METAVERSE_CLIENT_ID,
                     secret = config.SECRET
                 }),
                 headers: null,
                 onSuccess: (response) =>
                 {
                     _basic = response.data.message;
                     success = true;
                 },
                 onError: (error) =>
                 {
                     CommonErrorFallback(error);
                     success = false;
                 }));


            if (!success) yield break;

            yield return StartCoroutine(ApiService.PostCoroutine<Response<AuthToAccessResponse>>(
                 url: config.AUTH_HOST + AUTH_TO_ACCESS_ROUTE,
                 jsonBody: JsonUtility.ToJson(new AuthToAccessForm()
                 {
                     authCode = _authCode
                 }),
                 headers: new Dictionary<string, string>()
                 {
                        {"authorization", $"Basic {_basic}"}
                 },
                 onSuccess: (response) =>
                 {
                     accessResponse = response;
                     UpdateLoadingText("Get accessToken success!");
                     Debug.Log($"success Check userID: {accessResponse.data.userId}");
                     success = true;
                 },
                 onError: (error) =>
                 {
                     CommonErrorFallback(error);
                     success = false;
                 }));

            if (!success) yield break;

            yield return StartCoroutine(ApiService.GetCoroutine<Response<UserGetOneResponse>>(
                url: config.HOST + "/users/get-one/" + accessResponse.data.userId,
                headers: new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + accessResponse.data.accessToken }
                },
                onSuccess: (response) =>
                {
                    userResponse = response;
                    UpdateLoadingText($"Welcome {userResponse.data.User.firstName} !");
                    success = true;
                },
                onError: (error) =>
                {
                    CommonErrorFallback(error);
                    success = false;
                }
                ));

            if (success)
            {
                // Do Check login logic
#if UNITY_WEBGL && !UNITY_EDITOR
                SaveToLocalStorage("metaauth_accessToken", accessResponse.data.accessToken);
                SaveToLocalStorage("metaauth_accessTokenExpiresAt", accessResponse.data.accessTokenExpiresAt);
                SaveToLocalStorage("metaauth_userId", accessResponse.data.userId);
#endif
                if (UserReferencePersistent.Instance != null)
                {
                    UserReferencePersistent.Instance.SetUserName(userResponse.data.User.firstName);
                    UserReferencePersistent.Instance.SetGLTFLink(userResponse.data.User.Avatars[0].url);

                    for(int i = 0; i < userResponse.data.User.Avatars.Length; i++)
                    {
                        AvatarSystem.Instance.AddNewUserAvatar(userResponse.data.User.Avatars[i].url);
                        yield return null;
                    }

                    EventHandler.OnClientLogin();
                }
            }
        }
        #endregion

    }
}

