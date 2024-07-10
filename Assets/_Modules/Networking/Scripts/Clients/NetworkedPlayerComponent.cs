using FishNet.Component.Observing;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using StarterAssets;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkedPlayerComponent : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;

    [SerializeField] private List<UnityEngine.Object> localObject = new List<UnityEngine.Object>();
    [SerializeField] private AvatarLoader avatarLoader;

    public readonly SyncVar<string> PlayerName = new SyncVar<string>();
    public readonly SyncVar<string> GLTFLink = new SyncVar<string>();

    private void Awake()
    {
        PlayerName.OnChange += OnChangePlayerName;
        GLTFLink.OnChange += OnChangeAvatar;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!base.IsOwner)
        {
            gameObject.tag = "RemotePlayer";
            GetComponent<ThirdPersonController>().enabled = false;
            GetComponent<StarterAssetsInputs>().enabled = false;
            GetComponent<PlayerMovementHandler>().enabled = false;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
            GetComponent<PlayerInput>().enabled = false;
#endif
            return;
        }

        nameText.gameObject.SetActive(false);

        PlayerCameraHandler.Instance.Initialize();
        GetComponent<PlayerMovementHandler>().enabled = true;
        gameObject.tag = "Player";
        UserReferencePersistent.Instance.AssignPlayerGameObject(gameObject);

        PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
    }

    private void OnChangePlayerName(string prev, string next, bool asServer)
    {       
        if(nameText != null) nameText.text = next;
    }

    private void OnChangeAvatar(string prev, string next, bool asServer)
    {
        avatarLoader.GLTFLink = next;
        
        if (asServer) return;

        avatarLoader.LoadAvatar();
    }

    [Server]
    public void ServerSetName(string name)
    {
        this.PlayerName.Value = name;
    }

    [Server]
    public void ServerSetAvatar(string gltf)
    {
        this.GLTFLink.Value = gltf;
    }

    [Server]
    public void ServerSetMatchId(NetworkObject nob, int _matchId)
    {
        if (nob.Owner.IsValid)
        {
            MatchCondition.AddToMatch(_matchId, nob.Owner, replaceMatch: true);
        }
        else
        {
            MatchCondition.AddToMatch(_matchId, nob, replaceMatch: true);
        }
    }

    [TargetRpc]
    public void TargetUpdatePlayerInfo(NetworkConnection conn, string message)
    {
        Debug.Log($"From server: {message}");
    }

    [TargetRpc]
    public void TargetJoinChat(NetworkConnection conn, string channelName)
    {
        if (AgoraManager.Instance.joinedChannel) return;

        AgoraManager.Instance.JoinChannel(channelName, (uint)PlayerName.GetHashCode() % 1000);
    }

    [TargetRpc]
    public void TargetLeaveChat(NetworkConnection conn, string channelName)
    {
        if (AgoraManager.Instance.joinedChannel)
        {
            AgoraManager.Instance.LeaveChannel();
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (IsServerInitialized)
        {
            Debug.Log($"This also runs on server as well ");

#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
            GetComponent<PlayerInput>().enabled = false;
#endif
            GetComponent<ThirdPersonController>().enabled = false;
            GetComponent<StarterAssetsInputs>().enabled = false;

            foreach (var local in localObject)
            {
                Destroy(local);
            }

            var renderers = GetComponentsInChildren<Renderer>();

            foreach (var renderer in renderers)
            {
                renderer.enabled = false;
            }
        }
    }

    #region WorkAround Fix
    private void OnFootstep(AnimationEvent animationEvent)
    {

    }

    private void OnLand(AnimationEvent animationEvent)
    {

    }
    #endregion
}
