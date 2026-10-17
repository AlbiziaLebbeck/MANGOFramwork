using FishNet.Component.Observing;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MANGOsFramework.Experiment;
using StarterAssets;
using System.Collections;
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
    public readonly SyncVar<uint> OnProjector = new SyncVar<uint>(); 

    private bool IsFlyableController => TryGetComponent(out BasicBehaviour _);

    private void Awake()
    {
        PlayerName.OnChange += OnChangePlayerName;
        GLTFLink.OnChange += OnChangeAvatar;

        OnMic.OnChange += OnMic_OnChange;
    }

    #region Client
    public override void OnStartClient()
    {
        base.OnStartClient();

        AvatarLoaderEvent.AvatarLoadedEvent += AvatarLoaderEvent_AvatarLoadedEvent;

        if (!base.IsOwner)
        {
            gameObject.tag = "RemotePlayer";
            DisableLocalMovement();
            return;
        }

        EventHandler.UserMicMuteUpdateEvent += EventHandler_UserMicMuteUpdateEvent;
        
        nameText.gameObject.SetActive(false);
        micStatusIcon.gameObject.SetActive(false);
        videoStatusIcon.gameObject.SetActive(false);

        gameObject.tag = "Player";

        if(UserReferencePersistent.Instance == null)
        {
            GameObject userRef = new GameObject("UserReferencePersistent");
            userRef.AddComponent<UserReferencePersistent>();
        }
        UserReferencePersistent.Instance.AssignPlayerGameObject(gameObject);

        if (IsFlyableController)
        {
            if (TryGetComponent(out BasicBehaviour basicBehaviour))
            {
                basicBehaviour.enabled = true;
                basicBehaviour.CamInit();
            }
            if (TryGetComponent(out MoveBehaviour moveBehaviour)) moveBehaviour.enabled = true;
            if (TryGetComponent(out FlyBehaviour flyBehaviour)) flyBehaviour.enabled = true;
        }
        else
        {
            PlayerCameraHandler.Instance.Initialize();
            if (TryGetComponent(out PlayerMovementHandler movementHandler)) movementHandler.enabled = true;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
            if (TryGetComponent(out PlayerInput playerInput)) playerInput.enabled = true;
#endif
        }

        EventHandler.OnLocalClientCompleteSetup();

        PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
    }

    private void AvatarLoaderEvent_AvatarLoadedEvent(GameObject _model, string _url)
    {
        if(_model == avatarLoader.Model && _url == GLTFLink.Value)
        {
            StartCoroutine(RestartAnimator(avatarLoader.Animator));
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (base.IsOwner)
        {
            EventHandler.UserMicMuteUpdateEvent -= EventHandler_UserMicMuteUpdateEvent;

            if (IsFlyableController &&
                PlayerCameraHandler.Instance != null &&
                PlayerCameraHandler.Instance.MainVirtualCamera != null &&
                PlayerCameraHandler.Instance.MainVirtualCamera.TryGetComponent(out ThirdPersonOrbitCamBasic flyableCamera))
            {
                flyableCamera.player = null;
                flyableCamera.enabled = false;
            }
        }

        AvatarLoaderEvent.AvatarLoadedEvent -= AvatarLoaderEvent_AvatarLoadedEvent;
    }
    #endregion

    #region Callbacks
    private void EventHandler_UserMicMuteUpdateEvent(uint _uid, bool _muted)
    {
        //if 0 means update from local
        if(_uid == 0)
        {
            RPCServerSetMic(_muted);
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

        if (IsOwner) UserReferencePersistent.Instance.SetGLTFLink(next);

        if(AvatarSystem.Instance.IsAvatarCached(next))
        {
            avatarLoader.LoadAvatarFromCache();
        }
        else
        {
            avatarLoader.LoadAvatar();
        }
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
        OnProjector.Value = projectorId;
    }
    [ServerRpc]
    public  void RPCServerSetAvatar(string newLink)
    {
        GLTFLink.Value = newLink;
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
            DisableLocalMovement();

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

    private void DisableLocalMovement()
    {
        if (IsFlyableController)
        {
            if (TryGetComponent(out BasicBehaviour basicBehaviour)) basicBehaviour.enabled = false;
            if (TryGetComponent(out MoveBehaviour moveBehaviour)) moveBehaviour.enabled = false;
            if (TryGetComponent(out FlyBehaviour flyBehaviour)) flyBehaviour.enabled = false;
            return;
        }

#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
        if (TryGetComponent(out PlayerInput playerInput)) playerInput.enabled = false;
#endif
        if (TryGetComponent(out ThirdPersonController thirdPersonController)) thirdPersonController.enabled = false;
        if (TryGetComponent(out StarterAssetsInputs starterAssetsInputs)) starterAssetsInputs.enabled = false;
        if (TryGetComponent(out PlayerMovementHandler movementHandler)) movementHandler.enabled = false;
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

    private IEnumerator RestartAnimator(Animator anim)
    {
        anim.Rebind();
        anim.Update(0);
        yield return new WaitForEndOfFrame();
    }
    #endregion
}
