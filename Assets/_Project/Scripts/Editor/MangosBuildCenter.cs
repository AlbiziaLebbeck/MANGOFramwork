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
    private void OnEnable() => profile = MangosBuildProfile.Load();
    private void OnGUI()
    {
        profile ??= MangosBuildProfile.Load();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("MANGOs Build Center", EditorStyles.boldLabel);
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
    private void StartBuild(params string[] targets) => Run(() => { profile.Save(); MangosBuildPipeline.Start(profile, targets); });
    private void Run(Action action)
    {
        try { action(); message = "Settings saved. " + MangosBuildPipeline.LastResult; }
        catch (Exception error) { message = error.Message; Debug.LogException(error); }
    }
}
