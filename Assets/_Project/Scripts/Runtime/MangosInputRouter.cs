using UnityEngine;

// Shared by the walking controller and the legacy fly controller. Only local input enters here.
public static class MangosInputRouter
{
    public static int DeviceFlags { get; internal set; }
    public static bool Touch => (DeviceFlags & 8) != 0;
    public static bool Mobile => (DeviceFlags & 3) != 0;
    public static bool Portrait => (DeviceFlags & 4) != 0;
    internal static bool BrowserBlocked, NativeBlocked, UiBlocked;
    public static bool Blocked => BrowserBlocked || NativeBlocked || UiBlocked;
    public static Vector2 Move { get; internal set; }
    public static Vector2 Look { get; private set; }
    internal static Vector2 PendingLook;
    internal static bool Jump, Fly, Sprint;
    internal static int JumpVersion, FlyVersion;
    internal static float Vertical;
    private static int jumpConsumed, flyConsumed;
    public static void Reset()
    {
        Move = Look = PendingLook = Vector2.zero;
        Jump = Fly = Sprint = false; Vertical = 0;
        jumpConsumed = JumpVersion; flyConsumed = FlyVersion;
    }
    internal static void BeginFrame() { Look = Blocked ? Vector2.zero : PendingLook; PendingLook = Vector2.zero; }
    public static float Axis(string axis) => Blocked ? 0 : Touch ? (axis == "Horizontal" ? Move.x : Move.y) : Input.GetAxis(axis);
    public static bool SprintHeld(string button) => !Blocked && (Touch ? Sprint : Input.GetButton(button));
    public static float FlyVertical => !Blocked && Touch ? Vertical : 0;
    public static bool JumpDown(string button)
    {
        if (Blocked) return false;
        if (!Touch) return Input.GetButtonDown(button);
        bool pressed = jumpConsumed != JumpVersion; jumpConsumed = JumpVersion; return pressed;
    }
    public static bool FlyDown(string button)
    {
        if (Blocked) return false;
        if (!Touch) return Input.GetButtonDown(button);
        bool pressed = flyConsumed != FlyVersion; flyConsumed = FlyVersion; return pressed;
    }
}
