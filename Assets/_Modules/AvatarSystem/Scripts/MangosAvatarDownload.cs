using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MANGOsFramework.Experiment;
using UnityEngine.Networking;

public static class MangosAvatarDownload
{
#if UNITY_WEBGL && !UNITY_EDITOR
    private delegate void DownloadCallback(int id, IntPtr data, int length, int status);
    private static readonly DownloadCallback callback = Complete;
    private static readonly Dictionary<int, TaskCompletionSource<byte[]>> pending = new();
    private static int nextId;
    [DllImport("__Internal", EntryPoint = "MangosAvatarDownload")]
    private static extern void DownloadInBrowser(int id, string url, int credentials, DownloadCallback complete);
    [DllImport("__Internal")] private static extern void MangosCancelAvatarDownload(int id);

    [AOT.MonoPInvokeCallback(typeof(DownloadCallback))]
    private static void Complete(int id, IntPtr pointer, int length, int status)
    {
        if (!pending.TryGetValue(id, out var completion)) return;
        if (status != 200 || pointer == IntPtr.Zero || length <= 0)
        {
            completion.TrySetException(new InvalidOperationException("Avatar download failed (HTTP/network " + status + ")."));
            return;
        }
        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        completion.TrySetResult(bytes);
    }
#endif

    public static async Task<byte[]> Load(string url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
#if UNITY_WEBGL && !UNITY_EDITOR
        int id = ++nextId;
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Add(id, completion);
        // Cookie SSO is for the configured MANGOs API origin. Public third-party
        // assets retain anonymous CORS instead of inheriting account credentials.
        var uri = new Uri(url);
        var api = new Uri(MangosApiClient.ActiveAssetOrigin);
        bool credentials = Uri.Compare(uri, api, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
        using var registration = token.Register(() => {
            pending.Remove(id);
            MangosCancelAvatarDownload(id);
            completion.TrySetCanceled(token);
        });
        try
        {
            DownloadInBrowser(id, url, credentials ? 1 : 0, callback);
            return await completion.Task;
        }
        finally { pending.Remove(id); }
#else
        using var request = UnityWebRequest.Get(url);
        request.timeout = 45;
        var operation = request.SendWebRequest();
        while (!operation.isDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
        token.ThrowIfCancellationRequested();
        if (request.result != UnityWebRequest.Result.Success)
            throw new InvalidOperationException("Avatar download failed: " + request.error);
        return request.downloadHandler.data;
#endif
    }
}
