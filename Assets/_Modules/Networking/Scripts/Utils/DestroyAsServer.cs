using FishNet;
using UnityEngine;

public class DestroyAsServer : MonoBehaviour
{
    ConnectionStarter connectionStarter;

    private void Awake()
    {
        if(InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.IsServerStarted)
        {
            if (!InstanceFinder.IsClientStarted)
            {
                DestroyImmediate(gameObject);
                return;
            }
        }
        
        connectionStarter = FindObjectOfType<ConnectionStarter>();

        if(connectionStarter != null)
        {
            connectionStarter.ConnectionStartedEvent += OnConnectionStarted;
        }
    }

    private void OnConnectionStarted(StartType type)
    {
        if(type == StartType.Server)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if(connectionStarter != null)
        {
            connectionStarter.ConnectionStartedEvent -= OnConnectionStarted;
        }
    }
}
