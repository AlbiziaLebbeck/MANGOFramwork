using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using System;
using UnityEngine;
using Scene = UnityEngine.SceneManagement.Scene;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

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

    [SerializeField] private NetworkObject serverScenePrewarmerPrefab;
    [SerializeField] private NetworkObject worldManagerPrefab;

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

        PersistentCanvas.LoadingCanvas?.SetInformationDisplay("Starting connection.");
        PersistentCanvas.LoadingCanvas?.ToggleSpinner(true);

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

            PersistentCanvas.LoadingCanvas?.SetInformationDisplay("Connected To Server.");
        }

        if (obj.ConnectionState == LocalConnectionState.Stopping)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
            PersistentCanvas.LoadingCanvas?.SetInformationDisplay("Cannot connect to server. Please try again.");
        }
    }

    private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
    {
        serverState = obj.ConnectionState;

        if (obj.ConnectionState != LocalConnectionState.Started) return;
#if UNITY_WEBGL
         if (!networkManager.ServerManager.OneServerStarted()) return;
#endif
        Scene scene = UnitySceneManager.GetSceneByName("NetworkBoostrapScene");

        NetworkObject serverPrewarmer = Instantiate(serverScenePrewarmerPrefab);
        UnitySceneManager.MoveGameObjectToScene(serverPrewarmer.gameObject, scene);
        networkManager.ServerManager.Spawn(serverPrewarmer.gameObject);

        NetworkObject worldManager = Instantiate(worldManagerPrefab);
        UnitySceneManager.MoveGameObjectToScene(worldManager.gameObject, scene);
        networkManager.ServerManager.Spawn(worldManager.gameObject);
    }
}