using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;

namespace MANGOsFramework.Experiment
{
    public class ApiService
    {
        public static void PostJson<T>(
            MonoBehaviour runner,
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
        {
            runner.StartCoroutine(PostCoroutine(url, jsonBody, headers, onSuccess, onError));
        }

        public static IEnumerator PostCoroutine<T>(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
        {
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                if (headers != null)
                {
                    foreach (var pair in headers)
                    {
                        request.SetRequestHeader(pair.Key, pair.Value);
                        yield return null;
                    }
                }

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseJson = request.downloadHandler.text;
                    try
                    {
                        T result = JsonUtility.FromJson<T>(responseJson);
                        onSuccess?.Invoke(result);
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke("Deserialization failed: " + ex.Message);
                    }
                }
                else
                {
                    onError?.Invoke($"Error {request.responseCode}: {request.error}");
                }
            }
        }

        public static void GetJson<T>(
            MonoBehaviour runner,
            string url,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
        {
            runner.StartCoroutine(GetCoroutine(url, headers, onSuccess, onError));
        }

        public static IEnumerator GetCoroutine<T>(
            string url,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Content-Type", "application/json");

                if(headers != null)
                {
                    foreach (var pair in headers)
                    {
                        request.SetRequestHeader(pair.Key, pair.Value);
                        yield return null;
                    }
                }

                yield return request.SendWebRequest();

                if(request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        T result = JsonUtility.FromJson<T>(request.downloadHandler.text);
                        onSuccess?.Invoke(result);
                    }catch (Exception ex)
                    {
                        onError?.Invoke("JSON parse error: " + ex.Message);
                    }
                }
                else
                {
                    onError?.Invoke($"Error: {request.responseCode} - {request.error}");
                }
            }
        }
    }

    [Serializable]
    public class Response<T> where T : class
    {
        public string time;
        public T data;
    }

    #region USER_GET_ONE
    [Serializable]
    public class UserGetOneResponse
    {
        public UserData User;

        [Serializable]
        public class UserData
        {
            public int id;
            public string serialNumber;
            public string firstName;
            public string lastName;
            public string email;
            public string isEmailVerified;
            public string createdAt;
            public string updatedAt;
            public AvatarsData[] Avatars;
            public GeneralInformationData[] GeneralInformation;
        }

        [Serializable]
        public class AvatarsData
        {
            public int id;
            public string url;
            public string userId;
            public string isDefault;
            public string createdAt;
            public string updatedAt;
        }

        [Serializable]
        public class GeneralInformationData
        {
            public int id;
            public string position;
            public string country;
            public string organization;
            public string brithDate;
            public string createdAt;
            public string updatedAt;
        }
    }
    #endregion

    #region Get AuthCode
    [Serializable]
    public class GetAuthCodeReponse
    {
        public string redirectUrl;
        public string metaverseClientId;
        public string scope;
        public string authCode;
        public string state;
        public string authCodeExpiresAt;
    }
    #endregion

    #region AUTHTOACCESS
    [Serializable]
    public class AuthToAccessForm
    {
        public string authCode;
    }

    [Serializable]
    public class AuthToAccessResponse
    {
        public string accessToken;
        public string refreshToken;
        public string accessTokenExpiresAt;
        public string refreshTokenExpiresAt;
        public string userId;
        public string type;
    }
    #endregion

    #region Get Basic
    [Serializable]
    public class GetBasicForm
    {
        public string metaverseClientId;
        public string secret;
    }

    [Serializable]
    public class GetBasicResponse
    {
        public string message;
    }
    #endregion

    [Serializable]
    public class APIForm<T>
    {
        public T form;
    }
}


