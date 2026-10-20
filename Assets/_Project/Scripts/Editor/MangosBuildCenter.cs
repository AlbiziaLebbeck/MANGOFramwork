using System;
using UnityEditor;
using UnityEngine;

public sealed class MangosBuildCenter : EditorWindow
{
    private MangosBuildProfile profile;
    private string message;
    private Vector2 scroll;
    [MenuItem("MANGOs Framework/Build Center")]
    public static void Open() => GetWindow<MangosBuildCenter>("MANGOs Build Center");
    [MenuItem("MANGOs Framework/TTB/Build TTB UAT (Server + Client)")]
    public static void BuildTtbUat()
    {
        var window = GetWindow<MangosBuildCenter>("MANGOs Build Center");
        window.profile = MangosBuildProfile.Load();
        SetUatDefaults(window.profile);
        window.StartBuild("Server", "Client");
    }
    private static void SetUatDefaults(MangosBuildProfile selected)
    {
        selected.commerceEnvironment = "Uat"; selected.deployment = "mangos-ttb-uat";
        selected.network = new MangosRuntimeConfig { portalRouting = true, serverPort = 7770, serverWss = false,
            clientHost = "multiplayer-plus.mangosgo.com", clientPort = 443, clientWss = true, clientPath = "/ttb-uat" };
    }
    private void OnEnable() => profile = MangosBuildProfile.Load();
    private void OnGUI()
    {
        profile ??= MangosBuildProfile.Load();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("MANGOs Build Center", EditorStyles.boldLabel);
        var commerceOptions = new[] { "None", "Local", "Uat", "Production" };
        var commerceIndex = Array.IndexOf(commerceOptions, profile.commerceEnvironment);
        profile.commerceEnvironment = commerceOptions[EditorGUILayout.Popup("TTB commerce", Math.Max(0, commerceIndex), commerceOptions)];
        if (profile.commerceEnvironment != "None")
        {
            EditorGUILayout.HelpBox("Includes the editable BaseWorld gallery and HTML commerce overlay. Bank payment availability is controlled by the backend.", MessageType.Info);
            profile.network.portalRouting = EditorGUILayout.Toggle("Deployment Portal slug routing", profile.network.portalRouting);
            if (GUILayout.Button("Use Deployment Portal UAT defaults"))
            {
                SetUatDefaults(profile);
            }
            EditorGUILayout.HelpBox("Build prepares the latest HTML UI automatically, then packages the Server and Client ZIPs. No terminal commands are needed.", MessageType.Info);
            using (new EditorGUI.DisabledScope(MangosBuildPipeline.IsRunning))
                if (profile.commerceEnvironment == "Uat" && GUILayout.Button("Build TTB UAT (Server + Client)", GUILayout.Height(36))) StartBuild("Server", "Client");
            if (profile.network.portalRouting)
                EditorGUILayout.HelpBox("On plus.mangosgo.com, the Client reads slug/version from its page URL. Upload Server and Client with the same slug/version. Client Path is only the fallback outside that host.", MessageType.Info);
        }
        profile.network.clientPath = EditorGUILayout.TextField("Client Path", profile.network.clientPath);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Server — Linux Dedicated", EditorStyles.boldLabel);
        profile.network.serverPort = EditorGUILayout.IntField("Port", profile.network.serverPort);
        profile.network.serverWss = EditorGUILayout.Toggle("WSS", profile.network.serverWss);
        if (profile.network.serverWss)
        {
            profile.network.serverCertificatePath = EditorGUILayout.TextField("Certificate path on Server", profile.network.serverCertificatePath);
            profile.network.serverCertificatePasswordVariable = EditorGUILayout.TextField("Password environment variable", profile.network.serverCertificatePasswordVariable);
            EditorGUILayout.HelpBox("The PFX and password stay on the Server. Build Center does not embed them in WebGL.", MessageType.Info);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Client — WebGL", EditorStyles.boldLabel);
        profile.network.clientPort = EditorGUILayout.IntField("Port", profile.network.clientPort);
        profile.network.clientWss = EditorGUILayout.Toggle("WSS", profile.network.clientWss);
        profile.network.clientHost = EditorGUILayout.TextField("Host (blank = page host)", profile.network.clientHost);
        if (!profile.network.clientWss) EditorGUILayout.HelpBox("Use an HTTP localhost/dev page for WS. An HTTPS page requires WSS.", MessageType.Warning);
        profile.deployment = EditorGUILayout.TextField("Deployment / room namespace", profile.deployment);
        profile.development = EditorGUILayout.Toggle("Development Build", profile.development);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Profile")) Run(profile.Save);
        if (GUILayout.Button("Reset Network Defaults")) profile.network = new MangosRuntimeConfig();
        EditorGUILayout.EndHorizontal();
        using (new EditorGUI.DisabledScope(MangosBuildPipeline.IsRunning))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build Server")) StartBuild("Server");
            if (GUILayout.Button("Build Client")) StartBuild("Client");
            if (GUILayout.Button("Build Both")) StartBuild("Server", "Client");
            EditorGUILayout.EndHorizontal();
        }
        if (MangosBuildPipeline.IsRunning && GUILayout.Button("Cancel after current build")) MangosBuildPipeline.RequestCancel();
        if (GUILayout.Button("Open Builds Folder")) EditorUtility.RevealInFinder(System.IO.Path.GetFullPath("Builds"));
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
        if (!string.IsNullOrEmpty(MangosBuildPipeline.LastResult)) EditorGUILayout.HelpBox(MangosBuildPipeline.LastResult, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }
    private void StartBuild(params string[] targets)
    {
        try
        {
            profile.Save(); MangosBuildPipeline.Start(profile, targets);
            message = MangosBuildPipeline.IsRunning ? "Building " + string.Join(" + ", targets) + ". Progress and output will appear below." : "Build cancelled.";
        }
        catch (Exception error) { message = error.Message; Debug.LogException(error); }
    }
    private void Run(Action action)
    {
        try { action(); message = "Settings saved. " + MangosBuildPipeline.LastResult; }
        catch (Exception error) { message = error.Message; Debug.LogException(error); }
    }
}
