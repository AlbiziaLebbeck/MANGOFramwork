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

    public static event Action LocalClientCompleteSetupEvent;
    
    public static event Action ServerStartedEvent;

    // Communication events
    public static event Action<uint, bool> UserMicMuteUpdateEvent;

    //Interactables
    public static event Action InteractionEnterEvent;
    public static event Action InteractionExitEvent;

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

    public static void OnLocalClientCompleteSetup()
    {
        if(LocalClientCompleteSetupEvent != null) LocalClientCompleteSetupEvent();
    }

    public static void OnClientSpawnFailed()
    {
        if(ClientSpawnFailedEvent != null) ClientSpawnFailedEvent();
    }

    #region Communication
    public static void OnUserMicMuteUpdate(uint userId, bool isMute)
    {
        if(UserMicMuteUpdateEvent != null) UserMicMuteUpdateEvent(userId, isMute);
    }
    #endregion

    #region Interactions
    public static void OnInteractionEnter()
    {
        if (InteractionEnterEvent != null) InteractionEnterEvent();
    }
    public static void OnInteractionExit()
    {
        if (InteractionExitEvent != null) InteractionExitEvent();
    }
    #endregion
}
