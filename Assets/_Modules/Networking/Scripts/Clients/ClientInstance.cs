using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

public class ClientInstance : NetworkBehaviour
{
    #region Public
    public static ClientInstance Instance;
    public bool Initialized { get; private set; } = false;
    #endregion

    #region Private
    public NetworkedPlayerSettings PlayerSettings { get; private set; }
    #endregion

    #region Constants
    /// <summary>
    /// Find the way to update version later.
    /// </summary>
    private const int VERSION_CODE = 0;
    #endregion

    private void Awake()
    {
        Initialize();
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        if (base.IsOwner) Instance = this;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if(base.IsOwner) CmdVerifyVersion(VERSION_CODE);
    }

    private void Initialize()
    {
        PlayerSettings = GetComponent<NetworkedPlayerSettings>();
    }

    public static ClientInstance ReturnClientInstance(NetworkConnection conn)
    {
        if (InstanceFinder.IsServerStarted && conn != null)
        {
            NetworkObject nob = conn.FirstObject;
            return (nob == null) ? null : nob.GetComponent<ClientInstance>();
        }
        else
        {
            return Instance;
        }
    }

    #region Version validate
    [ServerRpc]
    private void CmdVerifyVersion(int versionCode)
    {
        bool pass = (versionCode == VERSION_CODE);
        TargetVerifyVersion(base.Owner, pass);

        //If not pass then find offending client and give them the boot.
        if (!pass)
            base.NetworkManager.TransportManager.Transport.StopConnection(base.Owner.ClientId, false);
    }

    [TargetRpc]
    private void TargetVerifyVersion(NetworkConnection conn, bool pass)
    {
        Initialized = pass;
        if (!pass)
        {
            base.NetworkManager.ClientManager.StopConnection();
            Debug.LogError("Your exeutable is out of date. Please update");
        }
    }
    #endregion

}
