using System.Runtime.InteropServices;

public static class CheckMobile
{
#if UNITY_WEBGL
    [DllImport("__Internal")]
    private static extern bool IsMobile();
#endif

    public static bool CheckIsMobile()
    {
#if !UNITY_EDITOR && UNITY_WEBGL
            return IsMobile();
#else
        return false;
#endif
    }
}

