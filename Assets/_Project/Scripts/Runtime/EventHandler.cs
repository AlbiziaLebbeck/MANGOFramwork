using System;

public static class EventHandler
{
    public static event Action<string> LoadSceneCompleteEvent;
    public static event Action ClientConnectedEvent;
    public static event Action ClientDisconnectedEvent;
    public static event Action<string> ClientConntectionFailedEvent;

    public static event Action ClientLoginEvent;
    public static event Action ClientLoginSuccessEvent;
    public static event Action<string> ClientLoginFailedEvent;


    public static event Action<string, uint> ClientSpawnSuccessEvent;
    public static event Action ClientSpawnFailedEvent;
    
    public static event Action ServerStartedEvent;


    //Communication Events
    public static event Action<uint, bool> UserMicMuteUpdateEvent;
    public static event Action<uint, bool> UserCamMuteUpdateEvent;

    public static event Action<uint> UserShareScreenStartedEvent;
    public static event Action<uint> UserShareScreenStoppedEvent;

    public static event Action<uint, float> ScreenRatioUpdateEvent;

    public static event Action<uint> CheckDeviceStatusEvent;

    public static void OnLoadSceneCompleted(string sceneName)
    {
        if(LoadSceneCompleteEvent != null) LoadSceneCompleteEvent(sceneName);
    }

    public static void OnServerStarted()
    {
        if (ServerStartedEvent != null) ServerStartedEvent();
    }

    public static void OnClientConnected()
    {
        if (ClientConnectedEvent != null) ClientConnectedEvent();
    }

    public static void OnClientDisconnected()
    {
        if (ClientDisconnectedEvent != null) ClientDisconnectedEvent();
    }

    public static void OnClientConnectionFailed(string failedReason)
    {
        if (ClientConntectionFailedEvent != null) ClientConntectionFailedEvent(failedReason);
    }

    public static void OnClientLogin()
    {
        if(ClientLoginEvent != null) ClientLoginEvent();
    }

    public static void OnClientLogInSuccess()
    {
        if (ClientLoginSuccessEvent != null) ClientLoginSuccessEvent();
    }

    public static void OnClientLogInFailed(string failedReason)
    {
        if (ClientLoginFailedEvent != null) ClientLoginFailedEvent(failedReason);
    }

    public static void OnClientSpawnSuccess(string channelName, uint uid)
    {
        if(ClientSpawnSuccessEvent != null) ClientSpawnSuccessEvent(channelName, uid);
    }

    public static void OnClientSpawnFailed()
    {
        if(ClientSpawnFailedEvent != null) ClientSpawnFailedEvent();
    }

    #region AgoraEvent
    public static void OnUserMicMuteUpdate(uint userId, bool isMute)
    {
        if(UserMicMuteUpdateEvent != null) UserMicMuteUpdateEvent(userId, isMute);
    }
    public static void OnUserCamMuteUpdate(uint userId, bool isMute)
    {
        if (UserCamMuteUpdateEvent != null) UserCamMuteUpdateEvent(userId, isMute);
    }
    public static void OnUserShareScreenStarted(uint screenId)
    {
        if (UserShareScreenStartedEvent != null) UserShareScreenStartedEvent(screenId);
    }
    public static void OnUserShareScreenStopped(uint screenId)
    {
        if(UserShareScreenStoppedEvent != null) UserShareScreenStoppedEvent(screenId);
    }

    public static void OnScreenResolutionUpdate(uint screenId, float aspectRatio)
    {
        if(ScreenRatioUpdateEvent != null) ScreenRatioUpdateEvent(screenId, aspectRatio);
    }

    public static void OnCheckDeviceStatus(uint uid)
    {
        if(CheckDeviceStatusEvent != null) CheckDeviceStatusEvent(uid);
    }
    #endregion
}
