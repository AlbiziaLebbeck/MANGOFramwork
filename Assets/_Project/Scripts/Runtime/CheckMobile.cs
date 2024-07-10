using System.Runtime.InteropServices;

public class CheckMobile : SingletonPersistent<CheckMobile>
{
    [DllImport("__Internal")]
    private static extern bool IsMobile();

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

