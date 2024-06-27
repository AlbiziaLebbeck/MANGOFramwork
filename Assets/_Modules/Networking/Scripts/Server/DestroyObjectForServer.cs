using FishNet;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestroyObjectForServer : MonoBehaviour
{
    ConnectionStarter connectionStarter;

    public List<GameObject> objectsToDestroy = new();

    private void Awake()
    {
        connectionStarter = FindObjectOfType<ConnectionStarter>();

        if (InstanceFinder.NetworkManager == null) return;

        if (InstanceFinder.NetworkManager.IsServerStarted)
        {
            if (connectionStarter != null)
            {
                if (connectionStarter.StartType == StartType.Server)
                {
                    if (objectsToDestroy != null)
                    {
                        StartCoroutine(DestroyGameObject());
                    }
                }
            }
        }
    }

    private void OnEnable()
    {
        if (connectionStarter != null) connectionStarter.ConnectionStartedEvent += DestroyAsServer_ConnectionStartedEvent;
    }

    private void OnDisable()
    {
        if (connectionStarter != null) connectionStarter.ConnectionStartedEvent -= DestroyAsServer_ConnectionStartedEvent;
    }

    private void DestroyAsServer_ConnectionStartedEvent(StartType type)
    {
        if (type == StartType.Server)
        {
            if(objectsToDestroy != null)
            {
                StartCoroutine(DestroyGameObject());
            }
        }
    }

    IEnumerator DestroyGameObject()
    {
        for(int i = 0; i < objectsToDestroy.Count; i++)
        {
            Destroy(objectsToDestroy[i]);

            yield return null;
        }

        objectsToDestroy = null;

        yield return null;
    }
}
