public static class MangosIntegrationValidation
{
    public static void BuildBoth()
    {
        MangosInputValidation.Run();
        MangosAvatarValidation.Run();
        MangosSettingsValidation.StartBuildBoth();
    }
}
