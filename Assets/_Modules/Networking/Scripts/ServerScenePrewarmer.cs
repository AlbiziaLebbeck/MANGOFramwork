using FishNet;
using FishNet.Managing.Scened;
using FishNet.Object;
using GameKit.Dependencies.Utilities.Types;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ServerScenePrewarmer : NetworkBehaviour
{
    [SerializeField, Scene] private string persistentScene;

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (IsServerOnlyInitialized)
        {
            Debug.Log("Server only");
        }

        if (IsServerInitialized)
        {
            Debug.Log("Host or Server");
        }

        InstanceFinder.SceneManager.LoadGlobalScenes(new(persistentScene));
    }
}
