using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using GameKit.Dependencies.Utilities.Types;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkLobbyManager : SingletonNetworkBehaviour<NetworkLobbyManager>
{
    [SerializeField, Scene] private string GLOBAL_SCENE;
    [SerializeField] private List<NetworkConnection> clients = new List<NetworkConnection>();
    [SerializeField] private List<int> clientsId = new List<int>();

    [Server]
    public override void OnStartServer()
    {
        base.OnStartServer();
        ServerPrewarmScene();
        InstanceFinder.ServerManager.OnRemoteConnectionState += RemoteConnectionStateChanged;
    }

    [Server]
    private void RemoteConnectionStateChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            clients.Add(connection);
            clientsId.Add(connection.ClientId);
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            clients.Remove(connection);
            clientsId.Remove(connection.ClientId);
        }
    }

    [Server]
    private void ServerPrewarmScene()
    {
        SceneLookupData globalSceneLookup = new SceneLookupData()
        {
            Handle = 0,
            Name = GLOBAL_SCENE
        };

        SceneLoadData sceneLoadData = new SceneLoadData()
        {
            PreferredActiveScene = new PreferredScene(globalSceneLookup),
            SceneLookupDatas = new SceneLookupData[] { globalSceneLookup, },
            ReplaceScenes = ReplaceOption.None,
            Options = new LoadOptions()
            {
                AutomaticallyUnload = false,
                AllowStacking = false,
                LocalPhysics = LocalPhysicsMode.Physics3D
            }
        };

        InstanceFinder.SceneManager.LoadConnectionScenes(sceneLoadData);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestToJoinWorld(NetworkConnection _client = null)
    {
        LoadSceneForClient(GLOBAL_SCENE, _client);
    }

    [Server]
    private void LoadSceneForClient(string _scene, NetworkConnection client)
    {
        SceneLoadData sld = new SceneLoadData(_scene);
        sld.ReplaceScenes = ReplaceOption.All;
        sld.Options.LocalPhysics = LocalPhysicsMode.Physics3D;
        sld.MovedNetworkObjects = new NetworkObject[] { client.FirstObject };
        sld.PreferredActiveScene = new PreferredScene(new SceneLookupData(_scene));
        InstanceFinder.SceneManager.LoadConnectionScenes(client, sld);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        EventHandler.JoinWorldRequest += EventHandler_JoinWorldRequest;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        EventHandler.JoinWorldRequest -= EventHandler_JoinWorldRequest;
    }

    private void EventHandler_JoinWorldRequest()
    {
        RequestToJoinWorld();
    }
}
