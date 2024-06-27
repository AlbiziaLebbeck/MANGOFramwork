using System;

public static class AvatarCanvasEvent
{
    public static event Action<AvatarIcon> AvatarIconSpawnedEvent;
    public static event Action<AvatarIcon> HoverEnterIconEvent;
    public static event Action<AvatarIcon> HoverExitIconEvent;
    public static event Action<AvatarIcon> OnClickAvatarIconEvent;
    public static event Action OnUserLoginEvent;

    public static void OnAvatarIconSpawned(AvatarIcon spawnedIcon)
    {
        if (AvatarIconSpawnedEvent != null) AvatarIconSpawnedEvent(spawnedIcon);
    }
    public static void OnClickedAvatarIcon(AvatarIcon _icon)
    {
        if (OnClickAvatarIconEvent != null) OnClickAvatarIconEvent(_icon);
    }
    public static void OnIconHoverEnter(AvatarIcon icon)
    {
        if (HoverEnterIconEvent != null) HoverEnterIconEvent(icon);
    }
    public static void OnIconHoverExit(AvatarIcon icon)
    {
        if (HoverExitIconEvent != null) HoverExitIconEvent(icon);
    }
    public static void OnLoginWithUser()
    {
        if (OnUserLoginEvent != null) OnUserLoginEvent();
    }
}