using FishNet.Object;
using FishNet.Object.Synchronizing;

public class NetworkedPlayerSettings : NetworkBehaviour
{
    #region Private
    private readonly SyncVar<string> _userName = new SyncVar<string>();
    private readonly SyncVar<string> _gltfLink = new SyncVar<string>();
    private readonly SyncVar<uint> _uid = new SyncVar<uint>();
    private const uint UID_PREFIX = 1;
    #endregion

    public void SetUserName(string value)
    {
        _userName.Value = value;

        //generate _uid add prefix '1' to uid
        var userId = uint.Parse($"{UID_PREFIX}{(uint)value.GetHashCode() % 1000}");
        _uid.Value = userId;
    }
    public void SetGtfLink(string value)
    {
        _gltfLink.Value = value;
    }
    public string GetUserName()
    {
        return _userName.Value;
    }
    public string GetGtfLink()
    {
        return _gltfLink.Value;
    }

    public uint GetUid()
    {
        return _uid.Value;
    }
}
