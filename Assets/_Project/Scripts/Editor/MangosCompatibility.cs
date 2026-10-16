using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MangosCompatibility
{
    public static string[] Scenes() => EditorBuildSettings.scenes
        .Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();

    public static void BuildServer()
    {
        PrepareRendering();
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
        Build(new BuildPlayerOptions {
            scenes = Scenes(), target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            locationPathName = "../validation/compatibility/Server/MANGOsServer.x86_64"
        });
    }

    public static void BuildClient()
    {
        PrepareRendering();
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        Build(new BuildPlayerOptions {
            scenes = Scenes(), target = BuildTarget.WebGL,
            locationPathName = "../validation/compatibility/Client"
        });
    }

    private static void Build(BuildPlayerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.locationPathName)));
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"MANGOS_COMPATIBILITY {options.target}: {report.summary.result}, errors={report.summary.totalErrors}");
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception($"Compatibility build failed for {options.target}");
    }

    public static void PrepareRendering()
    {
#if UNITY_6000_3_OR_NEWER
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineGlobalSettings"))
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var serialized = new SerializedObject(asset);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.name == "m_EnableRenderCompatibilityMode" && property.propertyType == SerializedPropertyType.Boolean)
                    property.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        AssetDatabase.SaveAssets();
#endif
    }
}
