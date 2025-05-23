using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using System;
using UnityEngine;

public class CustomNetworkSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject networkPlayerPrefab;
    [Space(5)]
    [Header("PlayerSpawnerSettings")]
    [SerializeField] private bool addToDefaultScene = true;

    private NetworkManager networkManager;
    private AreaSpawner areaSpawner;

    public event Action<NetworkObject> OnSpawned;

    private void Start()
    {
        if (IsServerOnlyInitialized)
        {
            networkManager = InstanceFinder.NetworkManager;

            networkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
        }
    }

    private void OnDestroy()
    {
        if (IsServerOnlyInitialized)
        {
            networkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
        }
    }

    private void SceneManager_OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
    {
        if (!asServer) return;

        Debug.Log($"{connection} join scene");
        if (networkPlayerPrefab == null)
        {
            Debug.LogWarning($"Player prefab is empty and cannot be spawned for connection {connection.ClientId}.");
            return;
        }

        Vector3 position;
        Quaternion rotation;

        SetSpawn(networkPlayerPrefab.transform, out position, out rotation);

        NetworkObject nob = networkManager.GetPooledInstantiated(networkPlayerPrefab, position, rotation, true);

        networkManager.ServerManager.Spawn(nob, connection);

        if (addToDefaultScene) networkManager.SceneManager.AddOwnerToDefaultScene(nob);

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
}
