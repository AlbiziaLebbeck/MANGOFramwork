using System;
using System.IO;
using UnityEngine;

public static class MangosSettingsValidation
{
    public static void StartBuildBoth()
    {
        Run();
        MangosBuildPipeline.Start(MangosBuildProfile.Load("../validation/local-build-profile.json"), "Server", "Client");
    }
    public static void Run()
    {
        var profile = new MangosBuildProfile();
        Require(profile.network.serverPort == 7770 && !profile.network.serverWss, "Server defaults");
        Require(profile.network.clientPort == 443 && profile.network.clientWss, "Client defaults");
        profile.network.serverPort = 7788;
        profile.network.clientPort = 4488;
        profile.network.clientWss = false;
        profile.network.clientPath = "/validation/game";
        profile.Validate();
        var handshake = typeof(JamesFrowen.SimpleWeb.SimpleWebServer).Assembly.GetType("JamesFrowen.SimpleWeb.ServerHandshake");
        var getKey = handshake.GetMethod("GetKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        foreach (var header in new[] { "Sec-WebSocket-Key", "sec-websocket-key", "SEC-WEBSOCKET-KEY" })
        {
            var buffer = new byte[60];
            getKey.Invoke(null, new object[] { "GET / HTTP/1.1\r\n" + header + ": dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n", buffer });
            Require(System.Text.Encoding.ASCII.GetString(buffer, 0, 24) == "dGhlIHNhbXBsZSBub25jZQ==", "HTTP header case " + header);
        }
        var copy = JsonUtility.FromJson<MangosBuildProfile>(JsonUtility.ToJson(profile));
        Require(copy.network.serverPort == 7788 && copy.network.clientPort == 4488 && !copy.network.clientWss, "Override persistence");
        foreach (var path in new[] { "../bad", "/game/../other", "/game; injected", "/game?query" })
        {
            copy.network.clientPath = path;
            Require(Rejected(copy), "Reject path " + path);
        }
        copy.network.clientPath = "/validation/game";
        copy.network.serverPort = 0; Require(Rejected(copy), "Reject port zero");
        copy.network.serverPort = 7788; copy.network.clientPort = 65536; Require(Rejected(copy), "Reject large port");
        copy.network.clientPort = 4488; copy.network.serverWss = true; Require(Rejected(copy), "Server certificate required");
        profile.deployment = "validation";
        profile.network.clientHost = "localhost";
        profile.network.serverPort = 7788;
        profile.network.clientPort = 7788;
        profile.network.clientWss = false;
        profile.Validate();
        Directory.CreateDirectory("../validation");
        File.WriteAllText("../validation/local-build-profile.json", JsonUtility.ToJson(profile, true));
        Debug.Log("MANGOS_SETTINGS_TESTS PASSED: defaults, independent overrides, persistence, path/port/TLS validation");
    }
    private static bool Rejected(MangosBuildProfile profile)
    {
        try { profile.Validate(); return false; } catch (ArgumentException) { return true; }
    }
    private static void Require(bool condition, string name) { if (!condition) throw new Exception("Settings validation failed: " + name); }
}
