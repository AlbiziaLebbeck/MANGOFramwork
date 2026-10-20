using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public sealed class MangosBuildProfile
{
    public const string DefaultPath = "ProjectSettings/MangosBuildProfile.json";
    public string deployment = "mangos";
    public MangosRuntimeConfig network = new MangosRuntimeConfig();
    public bool development;
    // None keeps the existing MANGOs/voice build behavior. Commerce must be selected explicitly.
    public string commerceEnvironment = "None";
    public static MangosBuildProfile Load(string path = DefaultPath)
    {
        var profile = new MangosBuildProfile();
        if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), profile);
        return profile;
    }
    public void Save() { Validate(); File.WriteAllText(DefaultPath, JsonUtility.ToJson(this, true)); }
    public void Validate()
    {
        if (commerceEnvironment != "None" && commerceEnvironment != "Local" && commerceEnvironment != "Uat" && commerceEnvironment != "Production")
            throw new ArgumentException("Choose None, Local, Uat or Production for TTB commerce.");
        if (!Regex.IsMatch(deployment ?? "", "^[A-Za-z0-9_-]+$")) throw new ArgumentException("Deployment must contain letters, digits, _ or -.");
        if (network == null) throw new ArgumentException("Network settings are missing.");
        if (network.portalRouting && (commerceEnvironment == "None" || network.serverPort != 7770 || network.serverWss ||
            network.clientPort != 443 || !network.clientWss || network.clientHost != "multiplayer-plus.mangosgo.com"))
            throw new ArgumentException("Portal commerce requires Server WS7770 and Client WSS443 at multiplayer-plus.mangosgo.com.");
        if (network.serverPort < 1 || network.serverPort > 65535 || network.clientPort < 1 || network.clientPort > 65535)
            throw new ArgumentException("Server and Client ports must be 1–65535.");
        if (!Regex.IsMatch(network.clientPath ?? "", "^/[A-Za-z0-9_./-]*$") || network.clientPath.Contains(".."))
            throw new ArgumentException("Client Path must start with / and contain only URL path characters, without .., query or fragment.");
        if (!string.IsNullOrWhiteSpace(network.clientHost) && Uri.CheckHostName(network.clientHost) == UriHostNameType.Unknown)
            throw new ArgumentException("Client Host must be a hostname or IP address, without scheme, port or path.");
        if (network.serverWss && string.IsNullOrWhiteSpace(network.serverCertificatePath))
            throw new ArgumentException("Set the Server certificate path on the deployment machine before enabling Server WSS.");
        if (!Regex.IsMatch(network.serverCertificatePasswordVariable ?? "", "^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new ArgumentException("Certificate password environment variable name is invalid.");
        network.roomNamespace = deployment;
    }
}
