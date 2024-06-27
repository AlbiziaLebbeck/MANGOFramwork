using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class EventHandler
{
    public static event Action<string> LoadSceneCompleteEvent;
    public static event Action ClientConnectedEvent;
    public static event Action<string> ClientConntectionFailedEvent;

    public static event Action ClientLoginEvent;
    public static event Action ClientLoginSuccessEvent;
    public static event Action<string> ClientLoginFailedEvent;
    
    public static event Action ServerStartedEvent;

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
}
