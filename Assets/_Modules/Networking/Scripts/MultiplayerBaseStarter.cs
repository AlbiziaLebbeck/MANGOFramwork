using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using GameKit.Dependencies.Utilities.Types;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// For Setup basic object for metaverse system.
/// </summary>
public class MultiplayerBaseStarter : NetworkBehaviour
{
    //SerializeField
    [SerializeField] private GameObject networkPlayerPrefab;
    [SerializeField, Scene] private string persistentScene;
    [Space(5)]
    [Header("PlayerSpawnerSettings")]
    [SerializeField] private bool addToDefaultScene = true;

    //Private
    private NetworkManager networkManager;
    private AreaSpawner areaSpawner;

    //Public
    public event Action<NetworkObject> OnSpawned;

    private void Start()
    {
        AsyncOperation loadSceneAsync = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(persistentScene, LoadSceneMode.Additive);
        loadSceneAsync.completed += LoadSceneAsync_completed;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        Initialize();
    }

    public void Initialize()
    {
        InitializePlayerSpawner();

        if(FindObjectOfType<UserReferencePersistent>() == null)
        {
            GameObject userRef = new GameObject();
            userRef.name = "UserReferencePersistent";
            userRef.AddComponent<UserReferencePersistent>();
        }

    }

    private void InitializePlayerSpawner()
    {
        networkManager = InstanceFinder.NetworkManager;

        if(networkManager == null)
        {
            Debug.LogWarning($"PlayerSpawner on {gameObject.name} cannot work as NetworkManager wasn't found on this object or within parent objects.");
            return;
        }

        networkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

    }

    private void SceneManager_OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
    {
        if (!asServer) return;
        if(networkPlayerPrefab == null)
        {
            Debug.LogWarning($"Player prefab is empty and cannot be spawned for connection {connection.ClientId}.");
            return;
        }

        Vector3 position;
        Quaternion rotation;

        SetSpawn(networkPlayerPrefab.transform, out position, out rotation);

        NetworkObject nob = networkManager.GetPooledInstantiated(networkPlayerPrefab, position, rotation, true);

        networkManager.ServerManager.Spawn(nob, connection);

        if(addToDefaultScene) networkManager.SceneManager.AddOwnerToDefaultScene(nob);

        OnSpawned?.Invoke(nob);
    }

    private void SetSpawn(Transform _prefab, out Vector3 _pos, out Quaternion _rot)
    {
        if (areaSpawner != null)
        {
            _pos = areaSpawner.GetRandomSpawn();
            _rot = Quaternion.identity;
        }
        else
        {
            _pos = _prefab.position;
            _rot = _prefab.rotation;
        }
    }

    private void LoadSceneAsync_completed(AsyncOperation obj)
    {
        Debug.Log("Load Persistent Scene");
    }

}
