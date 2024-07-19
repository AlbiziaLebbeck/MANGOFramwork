using System.Runtime.InteropServices;

public class CheckMobile : SingletonPersistent<CheckMobile>
{
#if UNITY_WEBGL
    [DllImport("__Internal")]
    private static extern bool IsMobile();
#endif

    private void Start()
    {
        DebugCanvas.SetText($"IsMobile : {CheckIsMobile()}");
    }

    public bool CheckIsMobile()
    {
#if !UNITY_EDITOR && UNITY_WEBGL
            return IsMobile();
#else
        return false;
#endif
    }
}

