using FishNet.Object;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

[System.Serializable]
public class WorldDetails
{
    public WorldDetails() { }

    public WorldDetails(string name, int worldId, int maxPlayers)
    {
        Name = name;
        ID = worldId;
        MaxPlayers = maxPlayers;
    }

    /// <summary>
    /// Name of this world
    /// </summary>
    public string Name;
    public int ID;
    /// <summary>
    /// Maximum players which may join this world.
    /// </summary>
    public int MaxPlayers;
    /// <summary>
    /// Scenes loaded for this world. Only available on server.
    /// </summary>
    [System.NonSerialized]
    public HashSet<Scene> Scenes = new HashSet<Scene>();
    /// <summary>
    /// Members in this room.
    /// </summary>
    public List<NetworkObject> MemberIds = new List<NetworkObject>();

    /// <summary>
    /// Adds to Members.
    /// </summary>
    /// <param name="clientId"></param>
    internal void AddMember(NetworkObject clientId)
    {
        if(!MemberIds.Contains(clientId)) MemberIds.Add(clientId);
    }
    /// <summary>
    /// Removes from Members.
    /// </summary>
    /// <param name="clientId"></param>
    internal bool RemoveMember(NetworkObject clientId)
    {
        int index = MemberIds.IndexOf(clientId);
        if(index != -1)
        {
            MemberIds.RemoveAt(index);
            return true;
        }
        else
        {
            return false;
        }
    }
    /// <summary>
    /// Clears MemberIds.
    /// </summary>
    internal void ClearMembers()
    {
        MemberIds.Clear();
    }

}
