using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using GameKit.Dependencies.Utilities.Types;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WorldManager : SingletonNetworkBehaviour<WorldManager>
{
    #region Public
    public Dictionary<NetworkConnection, string> LoggedInUsernames = new Dictionary<NetworkConnection, string>();
    public List<WorldDetails> CreatedWorlds = new List<WorldDetails>();
    public Dictionary<NetworkConnection, WorldDetails> ConnectionWorlds = new Dictionary<NetworkConnection, WorldDetails>();

    #region Worlds
    /// <summary>
    /// Called after a member has joined your world.
    /// </summary>
    public static event Action<NetworkObject> OnMemberJoined;
    /// <summary>
    /// Called after a member has left your world.
    /// </summary>
    public static event Action<NetworkObject> OnMemberLeft;
    

    public WorldDetails currentWorld { get; private set; } = null;
    /// <summary>
    /// Current room of local client.
    /// </summary>
    public static WorldDetails CurrentWorld
    {
        get { return Instance.currentWorld; }
        private set { Instance.currentWorld = value; }
    }
    #endregion

    public event Action<WorldDetails, NetworkObject> OnClientJoinedWorld;
    public event Action<WorldDetails, NetworkObject> OnClientLeftWorld;
    public event Action<NetworkObject> OnClientLoggedIn;
    #endregion

    #region Serialized
    [SerializeField, Scene] private string boostrapScene;
    [SerializeField, Scene] private string baseWorldScene;

    // for local client to load local asset scene
    // implement later 
    [SerializeField] private List<WorldSceneConfigurations> worldConfigs = new();
    #endregion

    #region Const.
    private const int MAX_PLAYER = 1000;
    private const int MIN_PLAYER = 0;
    #endregion

    #region Initialization.
    protected override void Awake()
    {
        base.Awake();

        EventHandler.ClientLoginEvent += EventHandler_ClientLoginEvent;

        InstanceFinder.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;

        InstanceFinder.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
        InstanceFinder.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

        InstanceFinder.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
    }

    private void OnDestroy()
    {
        EventHandler.ClientLoginEvent -= EventHandler_ClientLoginEvent;

        if (InstanceFinder.ClientManager) InstanceFinder.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;

        if (InstanceFinder.ServerManager) InstanceFinder.ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
        if (InstanceFinder.ServerManager) InstanceFinder.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;

        if (InstanceFinder.SceneManager) InstanceFinder.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
    }

    private void EventHandler_ClientLoginEvent()
    {
        TrySignIn();
    }

    private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
    {
        ServerReset();
    }
    private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
    {
        if (obj.ConnectionState == LocalConnectionState.Started) return;

        if (obj.ConnectionState == LocalConnectionState.Stopped)
        {
            EventHandler.OnClientDisconnected();
        }

        PersistentCanvas.LoadingCanvas?.SetInformationDisplay($"Client State: {obj.ConnectionState}");
        PersistentCanvas.LoadingCanvas?.ToggleLoadingScreen(true);
    }
    private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (asServer)
        {
            SendExistingWorld(conn);
        }
    }
    private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs state)
    {
        if(state.ConnectionState == RemoteConnectionState.Stopped)
        {
            ClientDisconnected(conn);
        }
    }
    private void ChangeSubscriptions(bool subscribe)
    {
        if (base.NetworkManager == null) return;

        if (subscribe)
        {
            base.NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
            base.NetworkManager.SceneManager.OnClientPresenceChangeEnd += SceneManager_OnClientPresenceChangeEnd;
        }
        else
        {
            base.NetworkManager.SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
            base.NetworkManager.SceneManager.OnClientPresenceChangeEnd -= SceneManager_OnClientPresenceChangeEnd;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        ServerPreWarmScene();
        ChangeSubscriptions(true);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        ChangeSubscriptions(false);
    }

    [Server]
    private void ServerPreWarmScene()
    {
        SceneLookupData boostrapsceneLookup = new SceneLookupData()
        {
            Handle = 0,
            Name = boostrapScene
        };


        SceneLoadData boostrap = new SceneLoadData()
        {
            SceneLookupDatas = new SceneLookupData[] { boostrapsceneLookup },
            ReplaceScenes = ReplaceOption.None,
            Options = new LoadOptions()
            {
                AutomaticallyUnload = false,
                AllowStacking = false,
            }
        };

        InstanceFinder.SceneManager.LoadGlobalScenes(boostrap);

        SceneLookupData baseWorldLookup = new SceneLookupData()
        {
            Handle = 0,
            Name = baseWorldScene
        };

        SceneLoadData baseWorld = new SceneLoadData()
        {
            SceneLookupDatas = new SceneLookupData[] { baseWorldLookup },
            ReplaceScenes = ReplaceOption.None,
            Options = new LoadOptions()
            {
                AutomaticallyUnload = false,
                AllowStacking = false,
                LocalPhysics = LocalPhysicsMode.Physics3D
            },
            //PreferredActiveScene = new PreferredScene(baseWorldLookup)
        };

        InstanceFinder.SceneManager.LoadConnectionScenes(baseWorld);
    }
    #endregion

    #region SceneManager Callbacks
    private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs obj)
    {
        if (!obj.QueueData.AsServer) return;

        foreach (Scene s in obj.LoadedScenes)
        {
            GameObject[] gos = s.GetRootGameObjects();

            for (int i = 0; i < gos.Length; i++)
            {
                if (gos[i].TryGetComponent(out NetworkedPlayerSpawner spawner))
                {
                    //Initialize NetworkPlayerSpawner In the scene.
                    spawner.FirstInitialize(this);
                    break;
                }
            }
        }
    }
    private void SceneManager_OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs obj)
    {
        if (obj.Added) Debug.Log("Client is added to the scene");
    }
    #endregion

    #region NetworkManager Callbacks
    private void ClientDisconnected(NetworkConnection obj)
    {
        ClientLeftServer(obj);
        LoggedInUsernames.Remove(obj);
        ConnectionWorlds.Remove(obj);
    }
    private void ServerReset()
    {
        CreatedWorlds.Clear();
        LoggedInUsernames.Clear();
    }
    #endregion

    #region Sign In
    [Client]
    private void TrySignIn()
    {
        var name = UserReferencePersistent.Instance.Username;
        var link = UserReferencePersistent.Instance.GLTF;

        RPC_SignIn(name, link);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RPC_SignIn(string username, string gltf, NetworkConnection sender = null)
    {
        ClientInstance ci;
        if (!FindClientInstance(sender, out ci)) return;

        string failedReason = string.Empty;

        bool success = OnSignIn(ref username, ref failedReason);
        if (success)
        {
            //Add to usernames on server.
            LoggedInUsernames[ci.Owner] = username;

            ci.PlayerSettings.SetUserName(username);
            ci.PlayerSettings.SetGtfLink(gltf);
            OnClientLoggedIn?.Invoke(ci.NetworkObject);
            TargetSignInSuccess(ci.Owner, username);
        }
        else
        {
            TargetSignInFailed(ci.Owner, failedReason);
        }
    }
    private bool OnSignIn(ref string username, ref string failedReason)
    {
        if (!SanitizeUsername(ref username, ref failedReason))
            return false;

        foreach (KeyValuePair<NetworkConnection, string> item in LoggedInUsernames)
        {
            if(item.Value.ToLower() == username.ToLower())
            {
                failedReason = "Username is already taken.";
                return false;
            }
        }

        return true;
    }
    
    [TargetRpc]
    private void TargetSignInSuccess(NetworkConnection conn, string username)
    {
        Debug.Log($"Welcome {username}!, you're now logged in!");

        EventHandler.OnClientLogInSuccess();

        JoinWorld("BaseWorld");
    }
    [TargetRpc]
    private void TargetSignInFailed(NetworkConnection conn, string failedReason)
    {
        if (failedReason == string.Empty)
            failedReason = "Sign in failed.";
        EventHandler.OnClientLogInFailed(failedReason);
    }
    #endregion

    #region Create World
    [Server]
    private void CreateWorld(string worldName)
    {
        string failedReason = string.Empty;
        bool success = OnCreateWorld(ref worldName, ref failedReason);

        if (success)
        {
            var worldId = worldName.GetHashCode();
            WorldDetails worldDetails = new WorldDetails(worldName, worldId, MAX_PLAYER);
            CreatedWorlds.Add(worldDetails);       
        }
    }

    private bool OnCreateWorld(ref string worldName, ref string failedReason)
    {
        if (InstanceFinder.IsServerStarted)
        {
            WorldDetails worldDetails = ReturnWorldDetails(worldName);
            if(worldDetails != null)
            {
                failedReason = "World already exist.";
                return false;
            }
        }
        return true;
    }

    #endregion

    #region Join World
    [Client]
    public static void JoinWorld(string worldName)
    {
        Instance.JoinWorldInternal(worldName);
    }
    private void JoinWorldInternal(string worldName)
    {
        RPC_JoinWorld(worldName);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RPC_JoinWorld(string worldName, NetworkConnection sender = null)
    {
        ClientInstance ci;
        if (!FindClientInstance(sender, out ci)) return;

        JoinWorld_Logic(worldName, sender);
    }

    private void JoinWorld_Logic(string worldName, NetworkConnection sender = null)
    {
        ClientInstance ci;
        if (!FindClientInstance(sender, out ci)) return;

        string failedReason = string.Empty;
        WorldDetails worldDetails = null;

        bool success = OnJoinRoom(worldName, ci.NetworkObject, ref failedReason, ref worldDetails);

        if (success)
        {
            worldDetails.AddMember(ci.NetworkObject);
            ConnectionWorlds[ci.Owner] = worldDetails;
            TargetJoinRoomSuccess(ci.Owner, worldDetails);
            OnClientJoinedWorld?.Invoke(worldDetails, ci.NetworkObject);

            //Update FriendList
            //Tell everyone that you joined their world
            foreach(NetworkObject item in worldDetails.MemberIds)
            {
                TargetMemberJoined(item.Owner, ci.NetworkObject, worldDetails.ID);
            }
        }
        else
        {
            TargetJoinRoomFailed(ci.Owner, failedReason);
        }
    }

    private bool OnJoinRoom(string worldName, NetworkObject joiner, ref string failedReason, ref WorldDetails worldDetails)
    {
        if (ReturnWorldDetails(joiner) != null)
        {
            failedReason = "You are already in a world";

            //Maybe we can let player leave world then let them join new world.
            
            if(ReturnWorldDetails(joiner) == ReturnWorldDetails(worldName))
            {
                return false;
            }
            else
            {
                TryLeaveRoom(joiner);
            }
        }

        worldDetails = ReturnWorldDetails(worldName);

        if(worldDetails == null)
        {
            failedReason = "World does not exist.";

            Debug.Log($"{failedReason}... Instead of saying can't join, we should create new world for client.");

            CreateWorld(worldName);

            // Get Created World.
            worldDetails = ReturnWorldDetails(worldName);
        }

        return true;
    }

    [TargetRpc]
    private void TargetMemberJoined(NetworkConnection conn, NetworkObject member, int worldId)
    {
        if (CurrentWorld == null || CurrentWorld.ID != worldId)
            return;

        MemberJoined(member);
    }

    private void MemberJoined(NetworkObject member)
    {
        CurrentWorld.AddMember(member);
        OnMemberJoined?.Invoke(member);

        if(FindClientInstance(member.Owner, out ClientInstance ci))
        {
            Debug.Log($"{ci.PlayerSettings.GetUserName()} join your world!!");
        }
    }

    [TargetRpc]
    private void TargetJoinRoomSuccess(NetworkConnection conn, WorldDetails worldDetails)
    {
        CurrentWorld = worldDetails;
        Debug.Log($"You are now joined {CurrentWorld.Name} !!");
        // you can use this to update canvas about you joining the world.
    }
    [TargetRpc]
    private void TargetJoinRoomFailed(NetworkConnection conn, string failedReason)
    {
        CurrentWorld = null;
        Debug.Log($"Join world failed for a reason: {failedReason} !!");

    }
    #endregion

    #region Load Client To World
    /// <summary>
    /// Load base world Scene, will we use match id for manage player instead.
    /// </summary>
    /// <param name="worldId"></param>
    /// <param name="player"></param>
    [Server]
    public void LoadSceneForClient(NetworkObject player)
    {
        ClientInstance ci;
        if (!FindClientInstance(player.Owner, out ci))
            return;

        SceneLoadData sld = new SceneLoadData(baseWorldScene);

        LoadOptions loadOptions = new LoadOptions
        {
            LocalPhysics = LocalPhysicsMode.Physics3D,
            AllowStacking = false,
        };

        sld.MovedNetworkObjects = new NetworkObject[] { player };
        //sld.PreferredActiveScene = new PreferredScene(new SceneLookupData(baseWorldScene));
        sld.Options = loadOptions;

        InstanceFinder.SceneManager.LoadConnectionScenes(player.Owner, sld);
    }
    #endregion

    #region Leave World
    [Server]
    private void ClientLeftServer(NetworkConnection conn)
    {
        if(FindClientInstance(conn, out ClientInstance ci))
        {
            //Remove From world
            RemoveFromWorld(ci.NetworkObject, true);
        }
    }

    [Client]
    public static void LeaveWorld()
    {
        Instance.LeaveWorldInternal();
    }

    private void LeaveWorldInternal()
    {
        if(CurrentWorld != null)
        {
            RPC_LeaveRoom();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RPC_LeaveRoom(NetworkConnection sender = null)
    {
        ClientInstance ci;

        if (!FindClientInstance(sender, out ci)) return;

        TryLeaveRoom(ci.NetworkObject);
    }

    [Server]
    public void TryLeaveRoom(NetworkObject clientId)
    {
        WorldDetails worldDetails = RemoveFromWorld(clientId, false);
        bool success = (worldDetails != null);

        if (success)
        {
            TargetLeaveWorldSuccess(clientId.Owner);
        }
        else
        {
            TargetLeaveRoomFailed(clientId.Owner);
        }
    }

    [TargetRpc]
    private void TargetLeaveWorldSuccess(NetworkConnection conn)
    {
        Debug.Log("You are leave the current world");
    }

    [TargetRpc]
    private void TargetLeaveRoomFailed(NetworkConnection conn)
    {
        Debug.Log("You cannot leave the current world some how...");
    }

    [TargetRpc]
    private void TargetMemberLeft(NetworkConnection conn, NetworkObject member)
    {
        if (CurrentWorld == null) return;

        CurrentWorld.RemoveMember(member);
        OnMemberLeft?.Invoke(member);
    }
    #endregion

    #region World Manage
    [Server]
    private WorldDetails RemoveFromWorld(NetworkObject clientId, bool clientDisconnected)
    {
        WorldDetails worldDetails = ReturnWorldDetails(clientId);

        if(worldDetails != null)
        {
            foreach ( NetworkObject item in worldDetails.MemberIds)
            {
                if (clientDisconnected && item == clientId) continue;
                //Let members know someone left.
                TargetMemberLeft(item.Owner, item);
            }

            worldDetails.RemoveMember(clientId);
            ConnectionWorlds.Remove(clientId.Owner);

            OnClientLeftWorld?.Invoke(worldDetails, clientId);

            //If not disconnecting then tell client to unload currentworld assets.
            //if (!clientDisconnected)
            //{

            //}
        }

        return worldDetails;
    }

    private void SendExistingWorld(NetworkConnection conn)
    {
        // Send current worlds to new client.
        List<WorldDetails> worlds = new List<WorldDetails>();

        for (int i = 0; i < CreatedWorlds.Count; i++)
        {
            worlds.Add(CreatedWorlds[i]);
        }

        //Send remaining rooms.
        if (worlds.Count > 0) TargetInitialRooms(conn, worlds.ToArray());
    }

    [TargetRpc]
    public void TargetInitialRooms(NetworkConnection conn, WorldDetails[] worldDetails)
    {
        Debug.Log($"From server: Update existing world. Count = {worldDetails.Length} ");
    }
    #endregion

    #region Helper
    public WorldSceneConfigurations ReturnWorldConfig(string worldName)
    {
        for (int i = 0; i < worldConfigs.Count; i++)
        {
            if (worldConfigs[i].WorldName.Equals(worldName, StringComparison.CurrentCultureIgnoreCase))
            {
                return worldConfigs[i];
            }
        }

        return null;
    }
    private WorldDetails ReturnWorldDetails(string worldName)
    {
        for(int i = 0; i < CreatedWorlds.Count; i++)
        {
            if (CreatedWorlds[i].Name.Equals(worldName, StringComparison.CurrentCultureIgnoreCase)) return CreatedWorlds[i];
        }

        return null;
    }
    private WorldDetails ReturnWorldDetails(NetworkObject clientId)
    {
        for(int i = 0;i < CreatedWorlds.Count;i++)
        {
            for(int j = 0; j < CreatedWorlds[i].MemberIds.Count; j++)
            {
                if (CreatedWorlds[i].MemberIds[j] == clientId) return CreatedWorlds[i];
            }
        }

        return null;
    }
    private bool FindClientInstance(NetworkConnection conn, out ClientInstance ci)
    {
        ci = null;
        if (conn == null)
        {
            Debug.Log("Connection is null.");
            return false;
        }
        ci = ClientInstance.ReturnClientInstance(conn);
        if (ci == null)
        {
            Debug.LogWarning("ClientInstance not found for connection.");
            return false;
        }

        return true;
    }
    public static void SanitizeTextMeshProString(ref string value)
    {
        if (value.Length == 0)
            return;
        /* Textmesh pro seems to add on an unknown char at the end.
        * If last char is an invalid ascii then remove it. */
        if ((int)value[value.Length - 1] > 255)
            value = value.Substring(0, value.Length - 1);
    }
    protected virtual bool OnSanitizeUsername(ref string value, ref string failedReason)
    {
        value = value.Trim();
        SanitizeTextMeshProString(ref value);
        //Length check.
        if (value.Length < 3)
        {
            failedReason = "Username must be at least 3 letters long.";
            return false;
        }
        if (value.Length > 15)
        {
            failedReason = "Username must be at most 15 letters long.";
            return false;
        }

        return true;
    }
    public static bool SanitizeUsername(ref string value, ref string failedReason)
    {
        return Instance.OnSanitizeUsername(ref value, ref failedReason);
    }
    public static bool SanitizePlayerCount(int count, ref string failedReason)
    {
        return Instance.OnSanitizePlayerCount(count, ref failedReason);
    }
    private bool OnSanitizePlayerCount(int count, ref string failedReason)
    {
        if(count < MIN_PLAYER || count > MAX_PLAYER)
        {
            failedReason = "Invalid player count.";
            return false;
        }

        return true;
    }
    #endregion
}
