using FishNet.Component.Observing;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using StarterAssets;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class NetworkedPlayerComponent : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image micStatusIcon;
    [SerializeField] private Image videoStatusIcon;

    [SerializeField] private Sprite micOnIcon;
    [SerializeField] private Sprite micOffIcon;
    
    [SerializeField] private Sprite videoOnIcon;
    [SerializeField] private Sprite videoOffIcon;

    [SerializeField] private List<Object> localObject = new List<Object>();
    [SerializeField] private AvatarLoader avatarLoader;

    public readonly SyncVar<string> PlayerName = new SyncVar<string>();
    public readonly SyncVar<string> GLTFLink = new SyncVar<string>();
    public readonly SyncVar<uint> Uid = new SyncVar<uint>();
        
    public readonly SyncVar<bool> OnMic = new SyncVar<bool>();
    public readonly SyncVar<bool> OnVideo = new SyncVar<bool>();
    public readonly SyncVar<uint> OnScreen = new SyncVar<uint>(); 

    private void Awake()
    {
        PlayerName.OnChange += OnChangePlayerName;
        GLTFLink.OnChange += OnChangeAvatar;

        OnMic.OnChange += OnMic_OnChange;
        OnVideo.OnChange += OnVideo_OnChange;
        OnScreen.OnChange += OnScreen_OnChange;

        EventHandler.CheckDeviceStatusEvent += EventHandler_CheckDeviceStatusEvent;
    }
    private void OnDestroy()
    {
        EventHandler.CheckDeviceStatusEvent -= EventHandler_CheckDeviceStatusEvent;
    }

    #region Client
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

        EventHandler.UserCamMuteUpdateEvent += EventHandler_UserCamMuteUpdateEvent;
        EventHandler.UserMicMuteUpdateEvent += EventHandler_UserMicMuteUpdateEvent;
        
        nameText.gameObject.SetActive(false);
        micStatusIcon.gameObject.SetActive(false);
        videoStatusIcon.gameObject.SetActive(false);

        PlayerCameraHandler.Instance.Initialize();

        GetComponent<PlayerMovementHandler>().enabled = true;

        gameObject.tag = "Player";

        UserReferencePersistent.Instance.AssignPlayerGameObject(gameObject);

        PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (base.IsOwner)
        {
            EventHandler.UserCamMuteUpdateEvent -= EventHandler_UserCamMuteUpdateEvent;
            EventHandler.UserMicMuteUpdateEvent -= EventHandler_UserMicMuteUpdateEvent;
        }
    }
    #endregion

    #region Callbacks
    private void EventHandler_CheckDeviceStatusEvent(uint _uid)
    {
        if (_uid == Uid.Value)
        {
            EventHandler.OnUserMicMuteUpdate(_uid, !OnMic.Value);
            EventHandler.OnUserCamMuteUpdate(_uid, !OnVideo.Value);
        }
    }
    private void EventHandler_UserMicMuteUpdateEvent(uint _uid, bool _muted)
    {
        //if 0 means update from local
        if(_uid == 0)
        {
            RPCServerSetMic(_muted);
        }
    }
    private void EventHandler_UserCamMuteUpdateEvent(uint _uid, bool _muted)
    {
        if (_uid == 0)
        {
            RPCServerSetCam(_muted);
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
    private void OnVideo_OnChange(bool prev, bool next, bool asServer)
    {
        if(asServer) return;

        if (next)
        {
            if(videoStatusIcon) videoStatusIcon.sprite = videoOnIcon;
        }
        else
        {
            if (videoStatusIcon) videoStatusIcon.sprite = videoOffIcon;
        }

        EventHandler.OnUserCamMuteUpdate(Uid.Value, !next);
    }
    private void OnMic_OnChange(bool prev, bool next, bool asServer)
    {
        if (asServer) return;

        if (next)
        {
            if (micStatusIcon) micStatusIcon.sprite = micOnIcon;
        }
        else
        {
            if (micStatusIcon) micStatusIcon.sprite = micOffIcon;
        }

        EventHandler.OnUserMicMuteUpdate(Uid.Value, !next);
    }
    private void OnScreen_OnChange(uint prev, uint next, bool asServer)
    {
        //later
    }
    #endregion

    #region RPCs
    [ServerRpc]
    public void RPCServerSetMic(bool _muted)
    {
        OnMic.Value = !_muted;
    }
    [ServerRpc]
    public void RPCServerSetCam(bool _muted)
    {
        OnVideo.Value = !_muted;
    }
    [ServerRpc]
    public void RPCServerSetProjector(uint projectorId)
    {
        OnScreen.Value = projectorId;
    }
    [TargetRpc]
    public void TargetUpdatePlayerInfo(NetworkConnection conn, string message)
    {
        Debug.Log($"From server: {message}");
    }
    [TargetRpc]
    public void TargetSpawnedSuccess(NetworkConnection conn, uint _uid, string _worldId)
    {
        if (!string.IsNullOrEmpty(_worldId))
        {
            EventHandler.OnClientSpawnSuccess(_worldId, _uid);
        }
    }
    #endregion

    #region Server Initialize
    public override void OnStartServer()
    {
        base.OnStartServer();

        if (IsServerInitialized)
        {
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
    #endregion

    #region WorkAround Fix
    private void OnFootstep(AnimationEvent animationEvent)
    {

    }

    private void OnLand(AnimationEvent animationEvent)
    {

    }
    #endregion
}
