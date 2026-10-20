using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mangos.Ttb.Commerce.Editor
{
    public static class TtbWorldBuild
    {
        public static void BuildServerLocal() => Build(true);
        public static void BuildClientLocal() => Build(false);
        private static void Build(bool server)
        {
            if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Replace('\\', '/').Contains("/Builds/TTB/BuildWorkspace/"))
                throw new InvalidOperationException("Run through the isolated TTB build script.");
            var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            if (scenes.Length < 3 || !scenes[0].EndsWith("/Bootstrap.unity") || !scenes.Any(x => x.EndsWith("/BaseWorld.unity"))) throw new InvalidOperationException("Missing original metaverse scenes.");
            var configPath = "Assets/Resources/MangosRuntimeConfig.json"; Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            var config = new MangosRuntimeConfig { clientHost = "127.0.0.1", clientPort = 7778, serverPort = 7778, clientWss = false, serverWss = false, clientPath = "/ttb-game-local", roomNamespace = "mangos-ttb-local" };
            File.WriteAllText(configPath, JsonUtility.ToJson(config)); AssetDatabase.ImportAsset(configPath, ImportAssetOptions.ForceSynchronousImport);
            MangosCompatibility.PrepareRendering();
            // Keep the original Bootstrap/Launcher and user build settings; author only the owned BaseWorld copy.
            var baseWorld = scenes.Single(path => path.EndsWith("/BaseWorld.unity"));
            var arguments = Environment.GetCommandLineArgs();
            var pairedIndex = Array.IndexOf(arguments, "-ttbPreparedWorldFingerprint");
            if (pairedIndex >= 0 && (server || pairedIndex + 1 >= arguments.Length))
                throw new InvalidOperationException("Invalid paired BaseWorld build arguments.");
            var preparedWorld = pairedIndex >= 0
                ? TtbBaseWorldExhibition.ReuseBuildScene(arguments[pairedIndex + 1])
                : TtbBaseWorldExhibition.PrepareBuildScene(baseWorld);
            PrepareWorldManagerScenePath(baseWorld, preparedWorld);
            scenes = scenes.Select(path => path == baseWorld ? preparedWorld : path).ToArray();
            var output = Path.GetFullPath("Builds/TTB/LocalWorld/" + (server ? "Server" : "Client")); Directory.CreateDirectory(output);
            if (server) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x);
            else { PlayerSettings.WebGL.template = "PROJECT:MANGOsTTB"; PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, target = server ? BuildTarget.StandaloneWindows64 : BuildTarget.WebGL,
                subtarget = server ? (int)StandaloneBuildSubtarget.Server : 0, locationPathName = server ? Path.Combine(output, "TtbLocalWorld.exe") : output, options = BuildOptions.None });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new InvalidOperationException("TTB metaverse build failed: result=" + report.summary.result + "; errors=" + report.summary.totalErrors);
            Debug.Log("TTB_WORLD_BUILD_PASSED: " + (server ? "Server" : "Client") + "; original Bootstrap/Launcher and prepared BaseWorld exhibition; loopback Local+Mock profile");
        }

        private static void PrepareWorldManagerScenePath(string originalPath, string preparedPath)
        {
            const string path = "Assets/_Modules/Networking/Prefabs/WorldManagerPrefab.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var manager = prefab != null ? prefab.GetComponent<global::WorldManager>() : null;
            if (manager == null) throw new InvalidOperationException("The existing world manager prefab is required.");
            var serialized = new SerializedObject(manager); var scene = serialized.FindProperty("baseWorldScene");
            if (scene == null || scene.propertyType != SerializedPropertyType.String ||
                (scene.stringValue != originalPath && scene.stringValue != preparedPath))
                throw new InvalidOperationException("The isolated world manager has an unexpected BaseWorld reference.");
            scene.stringValue = preparedPath; serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            Debug.Log("TTB_WORLD_SCENE_REFERENCE_PREPARED: isolated WorldManager prefab targets " + preparedPath);
        }
    }
}
