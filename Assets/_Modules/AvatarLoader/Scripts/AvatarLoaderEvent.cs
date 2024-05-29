using UnityEngine;
using System;

public static class AvatarLoaderEvent
{
    //Avatar
    public static event Action AvatarStartLoadingEvent;
    public static event Action AvatarFinishLoadingEvent;
    public static event Action<GameObject, string> AvatarLoadedEvent;
    public static event Action<string> AvatarLoadFailedEvent;
    public static event Action ReloadAvatarEvent;

    public static void OnAvatarStartLoading()
    {
        if (AvatarStartLoadingEvent != null) AvatarStartLoadingEvent();
    }
    public static void OnAvatarFinishLoading()
    {
        if (AvatarFinishLoadingEvent != null) AvatarFinishLoadingEvent();
    }
    public static void OnAvatarLoaded(GameObject _loadedAvatar, string _url)
    {
        if (AvatarLoadedEvent != null) AvatarLoadedEvent(_loadedAvatar, _url);
    }
    public static void OnAvatarLoadFailed(string _url)
    {
        if (AvatarLoadFailedEvent != null) AvatarLoadFailedEvent(_url);
    }
    public static void ReloadAvatar()
    {
        if (ReloadAvatarEvent != null) ReloadAvatarEvent();
    }
}
