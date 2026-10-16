// Existing menu commands use the same deployment profile and packaging pipeline.
public static class BuildsScript
{
    [UnityEditor.MenuItem("Game Build Menu/Client Build")]
    public static void PerformClientBuild() => MangosBuildPipeline.Start(MangosBuildProfile.Load(), "Client");
    [UnityEditor.MenuItem("Game Build Menu/Server Build")]
    public static void PerformServerBuild() => MangosBuildPipeline.Start(MangosBuildProfile.Load(), "Server");
}
