using System;
using FishNet.Transporting.Bayou;
using FishNet.Transporting.Tugboat;
using UnityEngine;

[Serializable]
public sealed class MangosRuntimeConfig
{
    public int serverPort = 7770;
    public bool serverWss;
    public int clientPort = 443;
    public bool clientWss = true;
    public string clientHost = "multiplayer-plus.mangosgo.com";
    public string clientPath = "/systemtester";
    public string roomNamespace = "mangos";
    public string serverCertificatePath = "";
    public string serverCertificatePasswordVariable = "MANGOS_TLS_PASSWORD";
    public bool portalRouting;
    public static MangosRuntimeConfig Load()
    {
        var config = new MangosRuntimeConfig();
        var file = Resources.Load<TextAsset>("MangosRuntimeConfig");
        if (file != null) JsonUtility.FromJsonOverwrite(file.text, config);
#if UNITY_WEBGL && !UNITY_EDITOR
        if (config.portalRouting && TryPortalPath(Application.absoluteURL, out var portalPath))
        {
            config.clientHost = "multiplayer-plus.mangosgo.com";
            config.clientPath = portalPath;
            config.clientPort = 443;
            config.clientWss = true;
            config.roomNamespace = "mangos-ttb-" + portalPath.Trim('/').Replace('/', '-');
        }
#endif
        return config;
    }
    public static bool TryPortalPath(string pageUrl, out string route)
    {
        route = null;
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) || page.Scheme != "https" ||
            page.Host != "plus.mangosgo.com" || !page.IsDefaultPort || !string.IsNullOrEmpty(page.UserInfo)) return false;
        var path = page.AbsolutePath.Trim('/');
        if (path.EndsWith("/index.html", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 11);
        var parts = path.Split('/');
        if (parts.Length < 1 || parts.Length > 2 || Array.Exists(parts, p =>
            !System.Text.RegularExpressions.Regex.IsMatch(p, "^[a-z0-9]+(-[a-z0-9]+)*$"))) return false;
        route = "/" + path;
        return true;
    }
    public void Apply(GameObject manager)
    {
        bool server = false;
#if UNITY_SERVER
        server = true;
#endif
        var bayou = manager.GetComponent<Bayou>();
        if (bayou != null)
        {
            var host = clientHost;
            if (string.IsNullOrWhiteSpace(host) && Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var page)) host = page.Host;
            if (string.IsNullOrWhiteSpace(host)) host = "localhost";
            bayou.SetClientAddress(host);
            bayou.SetClientPath(clientPath);
            bayou.SetPort(CheckedPort(server ? serverPort : clientPort));
            bayou.SetUseWSS(clientWss);
#if UNITY_SERVER || UNITY_EDITOR
            var certificate = Environment.GetEnvironmentVariable("MANGOS_TLS_CERTIFICATE");
            if (string.IsNullOrEmpty(certificate)) certificate = serverCertificatePath;
            if (serverWss && string.IsNullOrEmpty(certificate)) throw new InvalidOperationException("Server WSS needs a certificate path or MANGOS_TLS_CERTIFICATE.");
            bayou.SetServerSslConfiguration(new SslConfiguration(serverWss, certificate,
                Environment.GetEnvironmentVariable(serverCertificatePasswordVariable), System.Security.Authentication.SslProtocols.Tls12));
#endif
            Debug.Log($"MANGOS_NETWORK role={(server ? "server" : "client")} host={host} path={clientPath} port={(server ? serverPort : clientPort)} wss={(server ? serverWss : clientWss)}");
        }
        var tugboat = manager.GetComponent<Tugboat>();
        if (tugboat != null) tugboat.SetPort(CheckedPort(serverPort));
    }
    private static ushort CheckedPort(int port)
    {
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Port must be 1–65535.");
        return (ushort)port;
    }
}
