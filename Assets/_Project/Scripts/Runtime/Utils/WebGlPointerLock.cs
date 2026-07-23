using System.Runtime.InteropServices;

public static class WebGlPointerLock
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void MangosAcquirePointerLock();

    [DllImport("__Internal")]
    private static extern void MangosReleasePointerLock();
#endif

    public static void Acquire()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        MangosAcquirePointerLock();
#endif
    }

    public static void Release()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        MangosReleasePointerLock();
#endif
    }
}
