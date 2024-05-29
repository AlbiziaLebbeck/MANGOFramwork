using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using UnityEngine;

public class DestroyAsServer : MonoBehaviour
{
    ConnectionStarter connectionStarter;

    private void Awake()
    {
        connectionStarter = FindObjectOfType<ConnectionStarter>();
    }

    private void OnEnable()
    {
        if(connectionStarter != null) connectionStarter.ConnectionStartedEvent += DestroyAsServer_ConnectionStartedEvent;
    }

    private void OnDisable()
    {
        if (connectionStarter != null) connectionStarter.ConnectionStartedEvent -= DestroyAsServer_ConnectionStartedEvent;
    }

    private void DestroyAsServer_ConnectionStartedEvent(StartType type)
    {
        if(type == StartType.Server)
        {
            Destroy(gameObject);
        }
    }
}
