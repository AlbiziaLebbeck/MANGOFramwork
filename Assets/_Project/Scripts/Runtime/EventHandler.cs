using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class EventHandler
{
    public static event Action<string> LoadSceneCompleteEvent;
    public static event Action ClientConnectedEvent;
    public static event Action ClientConntectionFailedEvent;
    public static event Action JoinWorldRequest;

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

    public static void OnClientConnectionFailed()
    {
        if (ClientConntectionFailedEvent != null) ClientConntectionFailedEvent();
    }

    public static void RequestJoinWorld()
    {
        if(JoinWorldRequest != null) JoinWorldRequest();
    }
}
