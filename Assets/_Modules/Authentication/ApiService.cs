using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace MANGOsFramework.Experiment
{
    public enum ApiErrorKind
    {
        Http,
        Network,
        Timeout,
        Cancelled,
        InvalidJson,
        MissingData,
        UnexpectedEnvelope,
        InvalidAuthentication,
        UnsupportedAuthenticationMode,
        SecurityConfiguration
    }

    [Serializable]
    public sealed class ApiError
    {
        public ApiErrorKind kind;
        public long statusCode;
        public string message;

        public ApiError(ApiErrorKind kind, string message, long statusCode = 0)
        {
            this.kind = kind;
            this.message = message;
            this.statusCode = statusCode;
        }

        public string ToSafeMessage()
        {
            return statusCode > 0 ? $"HTTP {statusCode}: {message}" : message;
        }
    }

    public sealed class ApiRequestOptions
    {
        public int TimeoutSeconds = 20;
        public CancellationToken CancellationToken = CancellationToken.None;
    }

    public interface IApiEnvelope
    {
        string Status { get; }
        bool HasData { get; }
    }

    public static class ApiService
    {
        public static void PostJson<T>(
            MonoBehaviour runner,
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
            where T : class
        {
            runner.StartCoroutine(PostCoroutine(url, jsonBody, headers, onSuccess, onError));
        }

        public static IEnumerator PostCoroutine<T>(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
            where T : class
        {
            yield return SendCoroutine(
                UnityWebRequest.kHttpVerbPOST,
                url,
                jsonBody,
                headers,
                new ApiRequestOptions(),
                onSuccess,
                error => onError?.Invoke(error.ToSafeMessage()));
        }

        public static void GetJson<T>(
            MonoBehaviour runner,
            string url,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
            where T : class
        {
            runner.StartCoroutine(GetCoroutine(url, headers, onSuccess, onError));
        }

        public static IEnumerator GetCoroutine<T>(
            string url,
            Dictionary<string, string> headers,
            Action<T> onSuccess,
            Action<string> onError)
            where T : class
        {
            yield return SendCoroutine(
                UnityWebRequest.kHttpVerbGET,
                url,
                null,
                headers,
                new ApiRequestOptions(),
                onSuccess,
                error => onError?.Invoke(error.ToSafeMessage()));
        }

        public static IEnumerator SendCoroutine<T>(
            string method,
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            ApiRequestOptions options,
            Action<T> onSuccess,
            Action<ApiError> onError)
            where T : class
        {
            options = options ?? new ApiRequestOptions();

            using (UnityWebRequest request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, options.TimeoutSeconds);
                request.SetRequestHeader("Accept", "application/json");

                if (jsonBody != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> pair in headers)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                        {
                            request.SetRequestHeader(pair.Key, pair.Value);
                        }
                    }
                }

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (options.CancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        onError?.Invoke(new ApiError(ApiErrorKind.Cancelled, "The request was cancelled."));
                        yield break;
                    }

                    yield return null;
                }

                if (options.CancellationToken.IsCancellationRequested)
                {
                    onError?.Invoke(new ApiError(ApiErrorKind.Cancelled, "The request was cancelled."));
                    yield break;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(CreateRequestError(request));
                    yield break;
                }

                if (!TryDeserialize(request.downloadHandler.text, out T result, out ApiError parseError))
                {
                    onError?.Invoke(parseError);
                    yield break;
                }

                onSuccess?.Invoke(result);
            }
        }

        public static bool TryDeserialize<T>(string json, out T result, out ApiError error)
            where T : class
        {
            result = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = new ApiError(ApiErrorKind.MissingData, "The API returned an empty response.");
                return false;
            }

            try
            {
                result = JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                error = new ApiError(ApiErrorKind.InvalidJson, "The API returned invalid JSON.");
                return false;
            }

            if (result == null)
            {
                error = new ApiError(ApiErrorKind.InvalidJson, "The API response could not be parsed.");
                return false;
            }

            if (result is IApiEnvelope envelope)
            {
                if (string.IsNullOrWhiteSpace(envelope.Status))
                {
                    error = new ApiError(ApiErrorKind.UnexpectedEnvelope, "The API response is missing its status field.");
                    return false;
                }

                if (!string.Equals(envelope.Status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    error = new ApiError(ApiErrorKind.Http, "The API rejected the request.");
                    return false;
                }

                if (!envelope.HasData)
                {
                    error = new ApiError(ApiErrorKind.MissingData, "The API response is missing its data field.");
                    return false;
                }
            }

            return true;
        }

        public static ApiError CreateHttpError(long statusCode, string transportMessage = null)
        {
            ApiErrorKind kind = statusCode == 401
                ? ApiErrorKind.InvalidAuthentication
                : ApiErrorKind.Http;

            string message;
            switch (statusCode)
            {
                case 400:
                    message = "The request was invalid.";
                    break;
                case 401:
                    message = "The authentication state is missing, invalid, or expired.";
                    break;
                case 403:
                    message = "The authenticated user is not allowed to perform this action.";
                    break;
                case 404:
                    message = "The requested resource was not found.";
                    break;
                default:
                    message = string.IsNullOrWhiteSpace(transportMessage)
                        ? "The API request failed."
                        : transportMessage;
                    break;
            }

            return new ApiError(kind, message, statusCode);
        }

        private static ApiError CreateRequestError(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                return CreateHttpError(request.responseCode);
            }

            if (string.Equals(request.error, "Request timeout", StringComparison.OrdinalIgnoreCase) ||
                request.error?.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ApiError(ApiErrorKind.Timeout, "The API request timed out.");
            }

            return new ApiError(ApiErrorKind.Network, "The API could not be reached.");
        }
    }

    [Serializable]
    public class Response<T> : IApiEnvelope where T : class
    {
        public string status;
        public string time;
        public T data;

        public string Status => status;
        public bool HasData => data != null;
    }

    [Serializable]
    public class UserGetOneResponse
    {
        public UserData User;

        [Serializable]
        public class UserData
        {
            public string id;
            public string firstName;
            public string lastName;
            public string email;
            public string emailVerifiedAt;
            public string emailVerificationSentAt;
            public string affiliation;
            public string birthdate;
            public string gender;
            public string nationality;
            public string avatarImageUrl;
            public string createdAt;
            public string updatedAt;
            public LoginMethodsData loginMethods;
            public RoleData[] Roles;
        }

        [Serializable]
        public class LoginMethodsData
        {
            public LoginMethodData email;
            public LoginMethodData line;
        }

        [Serializable]
        public class LoginMethodData
        {
            public bool configured;
            public bool verified;
            public bool connected;
        }

        [Serializable]
        public class RoleData
        {
            public string id;
            public string name;
        }
    }

    [Serializable]
    public class AvatarListResponse
    {
        public int Count;
        public AvatarData[] Avatars;
    }

    [Serializable]
    public class AvatarData
    {
        public int id;
        public string url;
        public string userId;
        public string avatarType;
        public bool isDefault;
        public string createdAt;
        public string updatedAt;
    }

    [Serializable]
    public class GetAuthCodeForm
    {
        public string email;
        public string password;
    }

    [Serializable]
    public class GetAuthCodeResponse
    {
        public string redirectUrl;
        public string metaverseClientId;
        public string authCode;
        public string state;
        public string authCodeExpiresAt;
    }

    [Obsolete("Use GetAuthCodeResponse.")]
    [Serializable]
    public class GetAuthCodeReponse : GetAuthCodeResponse
    {
    }

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

    [Serializable]
    public class GetBasicForm
    {
        public string id;
        public string secret;
    }

    [Serializable]
    public class GetBasicResponse
    {
        public string Basic;
    }

    [Serializable]
    public class MangosGoldResponse
    {
        public WalletData UserMgoGoldWallet;

        [Serializable]
        public class WalletData
        {
            public int id;
            public string userId;
            public int amount;
            public string createdAt;
            public string updatedAt;
        }
    }

    [Serializable]
    public class FriendListResponse
    {
        public FriendData[] Friends;
    }

    [Serializable]
    public class FriendRequestListResponse
    {
        public FriendData[] FriendRequests;
    }

    [Serializable]
    public class RequestedFriendListResponse
    {
        public FriendData[] RequestedFriends;
    }

    [Serializable]
    public class FriendData
    {
        public int id;
        public string requesterId;
        public string addresseeId;
        public string status;
        public string createdAt;
        public string updatedAt;
        public UserGetOneResponse.UserData Requester;
        public UserGetOneResponse.UserData Addressee;
    }

    [Serializable]
    public class FriendProfileResponse
    {
        public UserGetOneResponse.UserData User;
        public int Count;
        public AvatarData[] Avatars;
    }

    [Serializable]
    public class MarketplaceListResponse
    {
        public int Count;
        public MarketplaceItemData[] MarketplaceItems;
    }

    [Serializable]
    public class MarketplaceItemResponse
    {
        public MarketplaceItemData MarketplaceItem;
    }

    [Serializable]
    public class MarketplaceItemData
    {
        public int id;
        public string name;
        public string description;
        public string category;
        public int price;
        public string modelUrl;
        public string sellerId;
        public string receiverId;
        public bool isActive;
        public string createdAt;
        public string updatedAt;
        public UserGetOneResponse.UserData Seller;
        public UserGetOneResponse.UserData Receiver;
    }

    [Serializable]
    public class MarketplaceBuyForm
    {
        public int itemId;
    }

    [Serializable]
    public class MarketplacePurchaseResponse
    {
        public ItemPurchaseHistoryData ItemPurchaseHistory;
        public MangosGoldResponse.WalletData UserMgoGoldWallet;
    }

    [Serializable]
    public class ItemPurchaseHistoryData
    {
        public int id;
        public int itemId;
        public string userId;
        public string sellerId;
        public string receiverId;
        public int pricePaid;
        public int buyerBalanceBefore;
        public int buyerBalanceAfter;
        public int receiverBalanceBefore;
        public int receiverBalanceAfter;
        public string createdAt;
        public string updatedAt;
        public MarketplaceItemData Item;
    }

    [Serializable]
    public class APIForm<T>
    {
        public T form;
    }
}
