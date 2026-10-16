public static class MangosIntegrationValidation
{
    public static void BuildBoth()
    {
        MangosUnity6PhysicsValidation.Run();
        MangosInputValidation.Run();
        MangosAvatarValidation.Run();
        MangosSettingsValidation.StartBuildBoth();
    }
}
