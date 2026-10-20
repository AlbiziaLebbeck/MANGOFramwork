using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mangos.Ttb.Commerce.Editor
{
    // Dedicated entry points: no UAT flags are written to the user's project or Local binaries.
    public static class TtbUatWorldBuild
    {
        public static void BuildServer() => Build(true);
        public static void BuildClient() => Build(false);

        private static void Build(bool server)
        {
            if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Replace('\\', '/').EndsWith("/Builds/TTB/BuildWorkspace/Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use the isolated UAT build script.");
            ValidateGameProfiles();
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, "-ttbPreparedWorldFingerprint");
            if (index < 0 || index + 1 >= arguments.Length)
                throw new InvalidOperationException("The reviewed prepared scene fingerprint is required for BOTH targets.");
            var prepared = TtbBaseWorldExhibition.ReuseBuildScene(arguments[index + 1]);
            var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            if (scenes.Length < 3 || !scenes[0].EndsWith("/Bootstrap.unity")) throw new InvalidOperationException("Missing original metaverse scenes.");
            var original = scenes.Single(path => path.EndsWith("/BaseWorld.unity"));
            scenes = scenes.Select(path => path == original ? prepared : path).ToArray();
            const string managerPath = "Assets/_Modules/Networking/Prefabs/WorldManagerPrefab.prefab";
            var manager = AssetDatabase.LoadAssetAtPath<GameObject>(managerPath)?.GetComponent<global::WorldManager>();
            if (manager == null) throw new InvalidOperationException("Missing WorldManager.");
            var serialized = new SerializedObject(manager);
            var scene = serialized.FindProperty("baseWorldScene");
            if (scene == null || scene.propertyType != SerializedPropertyType.String || (scene.stringValue != original && scene.stringValue != prepared))
                throw new InvalidOperationException("Unexpected isolated WorldManager scene reference.");
            scene.stringValue = prepared; serialized.ApplyModifiedPropertiesWithoutUndo();
            var config = new MangosRuntimeConfig
            {
                clientHost = "ttb-api-test.mangosgo.com", clientPort = 443, clientWss = true,
                clientPath = "/ttb-game-uat", serverPort = 7780, serverWss = false, roomNamespace = "mangos-ttb-uat"
            };
            const string runtimePath = "Assets/Resources/MangosRuntimeConfig.json";
            File.WriteAllText(runtimePath, JsonUtility.ToJson(config));
            AssetDatabase.ImportAsset(runtimePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets(); MangosCompatibility.PrepareRendering();
            var output = Path.GetFullPath("Builds/TTB/UatWorld/" + (server ? "Server" : "Client"));
            Directory.CreateDirectory(output);
            if (server) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x);
            else { PlayerSettings.WebGL.template = "PROJECT:MANGOsTTB"; PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, target = server ? BuildTarget.StandaloneLinux64 : BuildTarget.WebGL,
                subtarget = server ? (int)StandaloneBuildSubtarget.Server : 0,
                locationPathName = server ? Path.Combine(output, "TtbUatWorld.x86_64") : output, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new InvalidOperationException("UAT build failed: " + report.summary.result + "; errors=" + report.summary.totalErrors);
            Debug.Log("TTB_UAT_WORLD_BUILD_PASSED: " + (server ? "Server" : "Client") + "; pinned UAT WSS profile; paired reviewed BaseWorld; no bank credentials");
        }

        private static void ValidateGameProfiles()
        {
            const string privateUat = "http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/ttb-commerce-test/v1";
            if (!TtbGameApiProfile.TryResolve(privateUat, false, "Uat", out _) ||
                !TtbGameApiProfile.TryResolve("http://127.0.0.1:5286/ttb-commerce/v1", true, null, out _) ||
                !TtbGameApiProfile.TryResolve("https://ttb-api.mangosgo.com/ttb-commerce/v1", false, "Production", out _))
                throw new InvalidOperationException("A supported server API profile was rejected.");
            foreach (var rejected in new[]
            {
                "http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/ttb-commerce/v1",
                privateUat + "?redirect=1", privateUat + "#fragment", privateUat + "/",
                privateUat.Replace(":8080", ":8081"), privateUat.Replace("ttb-uat", "ttb-prod"),
                privateUat.Replace("http://", "http://user:password@"), "http://127.0.0.1:5286/ttb-commerce/v1",
                "https://ttb-api.mangosgo.com/ttb-commerce/v1", "https://ttb-api-test.mangosgo.com/ttb-commerce/v1"
            })
                if (TtbGameApiProfile.TryResolve(rejected, false, "Uat", out _)) throw new InvalidOperationException("An unrelated UAT API profile was accepted.");
            if (TtbGameApiProfile.TryResolve(privateUat, false, "Production", out _) ||
                TtbGameApiProfile.TryResolve(privateUat, false, null, out _) ||
                TtbGameApiProfile.TryResolve(privateUat, true, "Uat", out _) ||
                TtbGameApiProfile.TryResolve("https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1", false, "Production", out _))
                throw new InvalidOperationException("Game API environment isolation failed.");
            Debug.Log("TTB_UAT_GAME_PROFILE_CHECKS_PASSED: 17 cases; no HTTP request");
        }
    }
}
