public static class MangosIntegrationValidation
{
    public static void BuildBoth()
    {
        MangosUnity6PhysicsValidation.Run();
        MangosCookieRestoreValidation.Run();
        MangosSettingsValidation.StartBuildBoth();
    }
}
