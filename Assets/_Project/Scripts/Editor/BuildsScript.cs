using UnityEditor;
using System.IO;
using UnityEditor.Build;
using UnityEngine;

public class BuildsScript
{
    private static string[] GetScenes()
    {
        DirectoryInfo dir = new DirectoryInfo("Assets/_Project/Scenes");
        FileInfo[] info = dir.GetFiles("*.unity");

        string[] scenes = new string[info.Length];

        for (int i = 0; i < scenes.Length; i++)
        {
            int assetPathIndex = info[i].ToString().IndexOf("Assets");
            string localPath = info[i].ToString().Substring(assetPathIndex);

            scenes[i] = localPath;
        }

        return scenes;
    }

    [MenuItem("Game Build Menu/Client Build")]
    public static void PerformClientBuild()
    {
        string clientBuildPath = Path.Combine("Builds", "ClientBuild");

        EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ASTC;

        BuildPipeline.BuildPlayer(GetScenes(), clientBuildPath, BuildTarget.WebGL, BuildOptions.None);
    }


    [MenuItem("Game Build Menu/Server Build")]
    public static void PerformServerBuild()
    {
        string serverBuildPath = Path.Combine($"Builds/ServerBuild", "ServerBuild");

        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);

        BuildPipeline.BuildPlayer(GetScenes(), serverBuildPath, BuildTarget.StandaloneLinux64, BuildOptions.None);
    }
}
