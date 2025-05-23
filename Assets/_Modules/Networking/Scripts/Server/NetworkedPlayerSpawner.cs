using FishNet.Component.Observing;
using FishNet.Connection;
using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;


public class NetworkedPlayerSpawner : NetworkBehaviour
{
    #region Serialized
    [Header("Spawning")]
    [SerializeField] private AreaSpawner spawner;
    [SerializeField] private NetworkObject playerPrefab = null;
    #endregion

    /// <summary>
    /// WorldDetails for this world. Only available on the server.
    /// </summary>
    private WorldManager worldManager = null;

    /// <summary>
    /// Currently spawned player objects. Only exist on the server.
    /// </summary>
    private List<NetworkObject> spawnedPlayerObjects = new();

    private void OnDestroy()
    {
        if (this.worldManager != null)
        {
            this.worldManager.OnClientJoinedWorld -= OnClientJoinedWorld;
            this.worldManager.OnClientLeftWorld -= OnClientLeftWorld;
        }
    }
    public void FirstInitialize(WorldManager worldManager)
    {
        this.worldManager = worldManager;

        this.worldManager.OnClientJoinedWorld += OnClientJoinedWorld;
        this.worldManager.OnClientLeftWorld += OnClientLeftWorld;
    }
    private void OnClientLeftWorld(WorldDetails worldDetails, NetworkObject client)
    {
        for(int i = 0;i < spawnedPlayerObjects.Count;i++)
        {
            NetworkObject entry = spawnedPlayerObjects[i];

            if(entry == null)
            {
                spawnedPlayerObjects.RemoveAt(i);
                i--;
                continue;
            }

            if (spawnedPlayerObjects[i].Owner == client.Owner)
            {
                entry.Despawn();
                spawnedPlayerObjects.RemoveAt(i);
                i--;
            }
        }
    }
    private void OnClientJoinedWorld(WorldDetails worldDetails, NetworkObject client)
    {
        if (client == null || client.Owner == null) return;

        SpawnPlayer(client.Owner, worldDetails);
    }
    private void SpawnPlayer(NetworkConnection conn, WorldDetails worldDetails)
    {
        Vector3 spawnPosition = spawner.GetRandomSpawn();

        NetworkObject nob = Instantiate<NetworkObject>(playerPrefab, spawnPosition, Quaternion.identity);

        UnitySceneManager.MoveGameObjectToScene(nob.gameObject, gameObject.scene);

        spawnedPlayerObjects.Add(nob);

        //Spawn player
        base.Spawn(nob.gameObject, conn);

        //Load Scene for player
        worldManager?.LoadSceneForClient(nob);

        //Set MatchID
        if (nob.Owner.IsValid)
        {
            MatchCondition.AddToMatch(worldDetails.ID, nob.Owner, replaceMatch: true);
        }
        else
        {
            MatchCondition.AddToMatch(worldDetails.ID, nob, replaceMatch: true);
        }

        nob.transform.position = spawnPosition;

        ObserversTeleport(nob, spawnPosition);

        SetupPlayer(nob, worldDetails);

    }
    private void SetupPlayer(NetworkObject nob, WorldDetails worldDetails)
    {
        ClientInstance ci = ClientInstance.ReturnClientInstance(nob.Owner);

        var clientName = ci.PlayerSettings.GetUserName();
        var clientAvatar = ci.PlayerSettings.GetGtfLink();
        var clientUid = ci.PlayerSettings.GetUid();

        if(nob.TryGetComponent(out NetworkedPlayerComponent netCom))
        {
            netCom.PlayerName.Value = clientName;
            netCom.GLTFLink.Value = clientAvatar;
            netCom.Uid.Value = clientUid;

            netCom.TargetUpdatePlayerInfo(nob.Owner, $"Your name is {clientName}, your avatar link is {clientAvatar}");
            netCom.TargetSpawnedSuccess(nob.Owner, clientUid, worldDetails.ID.ToString());
        }
    }

    [ObserversRpc]
    private void ObserversTeleport(NetworkObject ident, Vector3 position)
    {
        ident.transform.position = position;
    }
}
