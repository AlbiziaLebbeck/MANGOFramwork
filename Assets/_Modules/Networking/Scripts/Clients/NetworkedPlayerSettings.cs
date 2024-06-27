using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;
using UnityEngine;

public class NetworkedPlayerSettings : NetworkBehaviour
{
    #region Private
    private readonly SyncVar<string> _userName = new SyncVar<string>();
    private readonly SyncVar<string> _gltfLink = new SyncVar<string>();
    #endregion

    public void SetUserName(string value)
    {
        _userName.Value = value;
    }

    public string GetUserName()
    {
        return _userName.Value;
    }

    public void SetGtfLink(string value)
    {
        _gltfLink.Value = value;
    }

    public string GetGtfLink()
    {
        return _gltfLink.Value;
    }
}
