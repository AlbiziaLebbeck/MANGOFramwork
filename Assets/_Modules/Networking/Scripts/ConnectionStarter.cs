using FishNet.Managing;
using FishNet.Transporting;
using System;
using UnityEngine;

public enum StartType
{
    Disabled,
    Host,
    Server,
    Client
}

public class ConnectionStarter : MonoBehaviour
{
    public StartType StartType = StartType.Disabled;

    private NetworkManager networkManager;

    private LocalConnectionState clientState = LocalConnectionState.Stopped;
    private LocalConnectionState serverState = LocalConnectionState.Stopped;

    public event Action<StartType> ConnectionStartedEvent;

    private void Start()
    {
        networkManager = GetComponent<NetworkManager>();
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager not found, ConnectionStarter need network manager attach to Game Object.");
            return;
        }
        else
        {
            networkManager.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
            networkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
        }

#if UNITY_EDITOR
        if (ParrelSync.ClonesManager.IsClone())
        {
            StartType = StartType.Client;
        }
#endif

#if UNITY_SERVER
            StartType = StartType.Server;
#elif !UNITY_EDITOR
            StartType = StartType.Client;
#endif
        Debug.Log("Start server as:" + StartType.ToString());

        if (StartType == StartType.Host || StartType == StartType.Server)
        {
            if (networkManager == null) return;

            if (serverState != LocalConnectionState.Stopped) networkManager.ServerManager.StopConnection(true);
            else networkManager.ServerManager.StartConnection();
        }

        if (StartType == StartType.Host || StartType == StartType.Client)
        {
            if (networkManager == null) return;
            if (clientState != LocalConnectionState.Stopped) networkManager.ClientManager.StopConnection();
            else networkManager.ClientManager.StartConnection();
        }

        if(ConnectionStartedEvent != null) ConnectionStartedEvent(StartType);
    }

    private void OnDestroy()
    {
        if (networkManager == null)
            return;

        networkManager.ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
        networkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
    }

    private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
    {
        clientState = obj.ConnectionState;

        if(clientState == LocalConnectionState.Started)
        {
            EventHandler.OnClientConnected();
        }

        if (obj.ConnectionState == LocalConnectionState.Stopping)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }

    private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
    {
        serverState = obj.ConnectionState;
    }
}