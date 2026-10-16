using System;
using System.IO;
using System.Linq;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class MangosBuildPipeline
{
    private const string JobPath = "Library/MangosBuildJob.json";
    private const string ConfigPath = "Assets/Resources/MangosRuntimeConfig.json";
    private static bool executing;
    public static bool IsRunning => File.Exists(JobPath);
    public static string LastResult => SessionState.GetString("MangosBuildResult", "");
    [Serializable] private sealed class Job
    {
        public MangosBuildProfile profile;
        public string[] targets;
        public int index;
        public string phase = "next", id, output;
        public bool cancel, hadConfig;
        public string originalConfig, originalTemplate;
        public int originalTarget, originalSubtarget, originalBackend, originalCompression, originalTexture;
    }
    [Serializable] private sealed class Manifest
    {
        public string buildId, editor, createdUtc, status, error;
        public MangosBuildProfile profile;
        public string[] completedTargets;
    }
    static MangosBuildPipeline() => EditorApplication.delayCall += Resume;
    public static void RequestCancel()
    {
        if (!IsRunning) return;
        var job = ReadJob(); job.cancel = true; SaveJob(job);
    }
    public static void Start(MangosBuildProfile profile, params string[] targets)
    {
        if (IsRunning) throw new InvalidOperationException("A build job is already active.");
        Preflight(profile, targets);
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var job = new Job {
            profile = JsonUtility.FromJson<MangosBuildProfile>(JsonUtility.ToJson(profile)), targets = targets.Distinct().ToArray(), id = id,
            output = Path.GetFullPath(Path.Combine("Builds", profile.deployment, id + ".staging")),
            hadConfig = File.Exists(ConfigPath), originalConfig = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : "",
            originalTarget = (int)EditorUserBuildSettings.activeBuildTarget,
            originalSubtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget,
            originalBackend = (int)PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server),
            originalCompression = (int)PlayerSettings.WebGL.compressionFormat,
            originalTexture = (int)EditorUserBuildSettings.webGLBuildSubtarget,
            originalTemplate = PlayerSettings.WebGL.template
        };
        Directory.CreateDirectory(job.output);
        SaveJob(job);
        EditorApplication.delayCall += Resume;
    }
    private static Job ReadJob() => JsonUtility.FromJson<Job>(File.ReadAllText(JobPath));
    private static void SaveJob(Job job) { Directory.CreateDirectory("Library"); File.WriteAllText(JobPath, JsonUtility.ToJson(job, true)); }
    private static void Resume()
    {
        var exitCode = SessionState.GetInt("MangosBuildExitCode", -1);
        if (exitCode >= 0)
        {
            SessionState.EraseInt("MangosBuildExitCode");
            EditorApplication.Exit(exitCode);
            return;
        }
        if (executing || !IsRunning) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Resume; return; }
        var job = ReadJob();
        try
        {
            if (job.cancel) { Finish(job, "Cancelled", "Cancelled by user"); return; }
            if (job.phase == "building") { Finish(job, "Failed", "The Editor stopped during a build. Partial output is retained."); return; }
            if (job.index >= job.targets.Length) { Finish(job, "Succeeded", ""); return; }
            var name = job.targets[job.index];
            var target = name == "Server" ? BuildTarget.StandaloneLinux64 : BuildTarget.WebGL;
            if (!Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != target)
            {
                job.phase = "switching"; SaveJob(job);
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target))
                    throw new InvalidOperationException("Cannot switch to " + target);
                EditorApplication.delayCall += Resume;
                return;
            }
            executing = true;
            job.phase = "building"; SaveJob(job);
            BuildOne(job.profile, name, Path.Combine(job.output, name), job.id);
            job.cancel = IsRunning && ReadJob().cancel;
            job.index++; job.phase = "next"; SaveJob(job);
            EditorApplication.delayCall += Resume;
        }
        catch (Exception error) { Debug.LogException(error); Finish(job, "Failed", error.Message); }
        finally { executing = false; EditorUtility.ClearProgressBar(); }
    }
    public static void Preflight(MangosBuildProfile profile, string[] targets)
    {
        MangosAvatarSetup.Configure();
        profile.Validate();
        if (targets.Length == 0 || targets.Any(t => t != "Server" && t != "Client")) throw new ArgumentException("Choose Server, Client or Both.");
        foreach (var target in targets)
        {
            var platform = target == "Server" ? BuildTarget.StandaloneLinux64 : BuildTarget.WebGL;
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(platform), platform))
                throw new InvalidOperationException("Install Unity build support for " + platform);
        }
        var scenes = MangosCompatibility.Scenes();
        if (scenes.Length == 0 || !scenes[0].EndsWith("/Bootstrap.unity", StringComparison.Ordinal))
            throw new InvalidOperationException("The first enabled Build Settings scene must be Bootstrap.unity.");
        if (!File.Exists("Assets/WebGLTemplates/MANGOsPWA/voice-overlay/app.js")) throw new FileNotFoundException("Voice overlay is missing from the template.");
    }
    public static BuildReport BuildOne(MangosBuildProfile profile, string name, string output, string buildId)
    {
        profile.Validate();
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
        var config = JsonUtility.FromJson<MangosRuntimeConfig>(JsonUtility.ToJson(profile.network));
        if (name == "Client") config.serverCertificatePath = "";
        File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, true));
        AssetDatabase.ImportAsset(ConfigPath, ImportAssetOptions.ForceSynchronousImport);
        MangosCompatibility.PrepareRendering();
        var server = name == "Server";
        if (server) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
        else
        {
            PlayerSettings.WebGL.template = "PROJECT:MANGOsPWA";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.Generic;
        }
        if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("MANGOs Build", name + ": " + buildId, 0.5f);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = MangosCompatibility.Scenes(), target = server ? BuildTarget.StandaloneLinux64 : BuildTarget.WebGL,
            subtarget = server ? (int)StandaloneBuildSubtarget.Server : 0,
            locationPathName = server ? Path.Combine(output, "MANGOsServer.x86_64") : output,
            options = profile.development ? BuildOptions.Development : BuildOptions.None
        });
        var messages = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content);
        File.WriteAllLines(Path.Combine(output, "build-diagnostics.txt"), messages);
        Debug.Log($"MANGOS_BUILD {name}: {report.summary.result}, errors={report.summary.totalErrors}, output={output}");
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(name + " build failed. See build-diagnostics.txt.");
        if (server)
        {
            if (!File.Exists(Path.Combine(output, "MANGOsServer.x86_64")) || !File.Exists(Path.Combine(output, "GameAssembly.so"))) throw new IOException("Incomplete Linux IL2CPP package.");
            File.WriteAllText(Path.Combine(output, "start-server.sh"), "#!/bin/sh\nset -eu\ncd -- \"$(dirname -- \"$0\")\"\nchmod +x ./MANGOsServer.x86_64\nexec ./MANGOsServer.x86_64 -batchmode -nographics -logFile - \"$@\"\n");
        }
        else
        {
            foreach (var asset in new[] { "index.html", "voice-app.js", "voice-overlay/app.js", "voice-overlay/config.js", "TemplateData/style.css" })
                if (!File.Exists(Path.Combine(output, asset))) throw new IOException("WebGL output is missing " + asset);
            var publicVoice = new { requireGameRoom = true, roomId = "", roomNamespace = profile.deployment };
            File.WriteAllText(Path.Combine(output, "mangos-config.js"), "window.MANGOS_BUILD_ID=" + Newtonsoft.Json.JsonConvert.SerializeObject(buildId) + ";\nwindow.VOICE_APP_CONFIG=" + Newtonsoft.Json.JsonConvert.SerializeObject(publicVoice) + ";\n");
        }
        return report;
    }
    private static void Finish(Job job, string status, string error)
    {
        try
        {
            var publicProfile = JsonUtility.FromJson<MangosBuildProfile>(JsonUtility.ToJson(job.profile));
            publicProfile.network.serverCertificatePath = "";
            var manifest = new Manifest { buildId = job.id, editor = Application.unityVersion, createdUtc = DateTime.UtcNow.ToString("O"), status = status, error = error,
                profile = publicProfile, completedTargets = job.targets.Take(job.index).ToArray() };
            File.WriteAllText(Path.Combine(job.output, "build-manifest.json"), JsonUtility.ToJson(manifest, true));
            WriteDeployment(job);
            if (status == "Succeeded")
            {
                var complete = job.output.Substring(0, job.output.Length - ".staging".Length);
                Directory.Move(job.output, complete);
                job.output = complete;
                File.WriteAllText("Builds/last-successful.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { output = complete, buildId = job.id }));
            }
        }
        finally
        {
            if (job.hadConfig) { File.WriteAllText(ConfigPath, job.originalConfig); AssetDatabase.ImportAsset(ConfigPath); }
            else AssetDatabase.DeleteAsset(ConfigPath);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, (ScriptingImplementation)job.originalBackend);
            PlayerSettings.WebGL.compressionFormat = (WebGLCompressionFormat)job.originalCompression;
            EditorUserBuildSettings.webGLBuildSubtarget = (WebGLTextureSubtarget)job.originalTexture;
            PlayerSettings.WebGL.template = job.originalTemplate;
            EditorUserBuildSettings.standaloneBuildSubtarget = (StandaloneBuildSubtarget)job.originalSubtarget;
            File.Delete(JobPath);
            var result = status + ": " + job.output;
            SessionState.SetString("MangosBuildResult", result); Debug.Log("MANGOS_BUILD_JOB " + result);
            if (Environment.GetCommandLineArgs().Contains("-mangosExitWhenDone")) SessionState.SetInt("MangosBuildExitCode", status == "Succeeded" ? 0 : 1);
            var original = (BuildTarget)job.originalTarget;
            if (!Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != original)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(original), original);
            EditorApplication.delayCall += Resume;
        }
    }
    private static void WriteDeployment(Job job)
    {
        var folder = Path.Combine(job.output, "Deploy"); Directory.CreateDirectory(folder);
        var n = job.profile.network;
        var host = string.IsNullOrWhiteSpace(n.clientHost) ? "YOUR_GAME_HOST" : n.clientHost;
        var tls = n.clientWss ? " ssl" : "";
        var certificates = n.clientWss ? "  ssl_certificate /etc/ssl/game/fullchain.pem;\n  ssl_certificate_key /etc/ssl/game/privkey.pem;\n" : "";
        var text = $"# Example: configure host/certificates/root before deployment.\nserver {{\n  listen {n.clientPort}{tls};\n  server_name {host};\n{certificates}  root /usr/share/nginx/html;\n  index index.html;\n  location {n.clientPath} {{\n    proxy_pass {(n.serverWss ? "https" : "http")}://127.0.0.1:{n.serverPort};\n    proxy_http_version 1.1;\n    proxy_set_header Upgrade $http_upgrade;\n    proxy_set_header Connection \"upgrade\";\n    proxy_read_timeout 3600s;\n  }}\n  location / {{ try_files $uri $uri/ =404; }}\n}}\n";
        File.WriteAllText(Path.Combine(folder, "nginx-game.conf.example"), text);
        File.WriteAllText(Path.Combine(folder, "README.txt"), "Serve the Client folder with correct wasm/JavaScript MIME types. Start the entire Server package with start-server.sh.\nThe proxy file is a deployment example and is not applied automatically.\nServer certificates/passwords are provided on the deployment host, never in WebGL.\n");
    }
    public static void BuildServerBatch() => BuildBatch("Server");
    public static void BuildClientBatch() => BuildBatch("Client");
    private static void BuildBatch(string target)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "-mangosProfile");
        var profile = MangosBuildProfile.Load(index >= 0 && index + 1 < args.Length ? args[index + 1] : MangosBuildProfile.DefaultPath);
        Preflight(profile, new[] { target });
        var expected = target == "Server" ? BuildTarget.StandaloneLinux64 : BuildTarget.WebGL;
        if (EditorUserBuildSettings.activeBuildTarget != expected) throw new InvalidOperationException("Pass -buildTarget Linux64 or WebGL to batchmode.");
        var hadConfig = File.Exists(ConfigPath); var original = hadConfig ? File.ReadAllText(ConfigPath) : null;
        var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);
        var compression = PlayerSettings.WebGL.compressionFormat;
        var texture = EditorUserBuildSettings.webGLBuildSubtarget;
        var template = PlayerSettings.WebGL.template;
        try { BuildOne(profile, target, Path.GetFullPath("Builds/Validation/" + target), "validation"); }
        finally
        {
            if (hadConfig) { File.WriteAllText(ConfigPath, original); AssetDatabase.ImportAsset(ConfigPath); }
            else AssetDatabase.DeleteAsset(ConfigPath);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, backend);
            PlayerSettings.WebGL.compressionFormat = compression;
            EditorUserBuildSettings.webGLBuildSubtarget = texture;
            PlayerSettings.WebGL.template = template;
            EditorUtility.ClearProgressBar();
        }
    }
}
