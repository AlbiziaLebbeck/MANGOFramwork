using MANGOsFramework.Experiment;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class TestApi : MonoBehaviour
{
    public string authHost = "https://authenticate.mangosgo.com/api/v2";
    public string authToAccessRoute = "/o-auth/auth-to-access";
    public string basic = "MGQ1MzdjOGEtNTU0OS00YTc5LTk4NTAtMWEwYzcwYzgzNjAzOjEyMzQ1Njc4";
    [TextArea(2,5)] public string code;

    public Response<AuthToAccessResponse> apiResponse = new Response<AuthToAccessResponse>();

    private void Start()
    {
        //StartCoroutine(CallAPI((response) =>
        //{
        //    apiResponse = JsonUtility.FromJson<Response<AuthToAccessResponse>>(response);
        //}));
    }

    public IEnumerator CallAPI(Action<string> callback)
    {
        string url = authHost + authToAccessRoute;
        string jsonBody = JsonUtility.ToJson(new AuthToAccessForm()
        {
            authCode = code
        });

        Debug.Log($"Sending API to: {url}");

        using(UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"Set header authorization to: Basic{basic}");

            request.SetRequestHeader("authorization", $"Basic {basic}");

            yield return request.SendWebRequest();

            if(request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Success: " + request.downloadHandler.text);
                if(callback != null)
                {
                    callback(request.downloadHandler.text);
                }
            }
            else
            {
                Debug.LogError("Error: " + request.error);

                if (callback != null)
                {
                    callback(null);
                }
            }

        }
    }
}
