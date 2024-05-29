using FishNet.Component.Observing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using StarterAssets;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class NetworkedPlayerComponent : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;

    [SerializeField] private List<UnityEngine.Object> localObject = new List<UnityEngine.Object>();
    [SerializeField] private AvatarLoader avatarLoader;

    const string defaultMatchId = "Th1sI5DefaUltM7tch1d";

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

        PlayerCameraHandler.Instance.Initialize();

        GetComponent<PlayerMovementHandler>().enabled = true;

        gameObject.tag = "Player";

        int matchId = defaultMatchId.GetHashCode();

        UserReferencePersistent.Instance.AssignPlayerGameObject(gameObject);

        RPC_ChangeUsername(this, UserReferencePersistent.Instance.Username);

        if (!string.IsNullOrEmpty(UserReferencePersistent.Instance.GLTF))
        {
            RPC_ChangeAvatar(this, UserReferencePersistent.Instance.GLTF);
        }
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

    [ServerRpc]
    public void RPC_ChangeUsername(NetworkedPlayerComponent _player, string _name)
    {
        _player.PlayerName.Value = _name;
    }

    [ServerRpc]
    public void RPC_ChangeAvatar(NetworkedPlayerComponent _player, string _link)
    {
        _player.GLTFLink.Value = _link;
    }


    [ServerRpc]
    public void RPC_ChangeUserMatchId(NetworkedPlayerComponent _player, int _matchId)
    {
        if (_player.Owner.IsValid)
        {
            MatchCondition.AddToMatch(_matchId, _player.Owner, replaceMatch: true);
        }
        else
        {
            MatchCondition.AddToMatch(_matchId, _player.NetworkObject, replaceMatch: true);
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
