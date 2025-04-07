using agora_gaming_rtc;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class NetworkScreen : NetworkBehaviour
{
    public readonly SyncVar<uint> setScreenId = new SyncVar<uint>();
    public readonly SyncVar<bool> isSharing = new SyncVar<bool>();

    public GameObject panel;

    #region EventHandler
    private void OnEnable()
    {
        EventHandler.UserShareScreenStartedEvent += EventHandler_UserShareScreenStartedEvent;
        EventHandler.UserShareScreenStoppedEvent += EventHandler_UserShareScreenStoppedEvent;
    }

    private void OnDisable()
    {
        EventHandler.UserShareScreenStartedEvent -= EventHandler_UserShareScreenStartedEvent;
        EventHandler.UserShareScreenStoppedEvent -= EventHandler_UserShareScreenStoppedEvent;
    }

    private void EventHandler_UserShareScreenStoppedEvent(uint uid)
    {
        RPCRequestSharescreen(0);
    }

    private void EventHandler_UserShareScreenStartedEvent(uint uid)
    {
        RPCRequestSharescreen(uid);
    }
    #endregion

    [ServerRpc(RequireOwnership = false)]
    public void RPCRequestSharescreen(uint uid)
    {
        if(uid > 0)
        {
            isSharing.Value = true;
        }
        else
        {
            isSharing.Value = false;
        }
        setScreenId.Value = uid;

        ObserverShareScreen(uid);
    }

    [ObserversRpc]
    private void ObserverShareScreen(uint uid)
    {
        Debug.Log($"Observer: sharescreen with {uid}");
        SetVideo(uid);
    }

    public void OnClick_ShareScreen()
    {
        if(isSharing.Value)
        {
            return;
        }

        AgoraManager.Instance.OnShareScreen(true, false);
    }

    public void OnClick_CancelShareScreen()
    {

    }


    private void Awake()
    {
        SetupVideoSurface();
    }

    private void SetupVideoSurface()
    {
        videoSurface = screenFrame.gameObject.AddComponent<VideoSurface>();
        SetVideo(0);
    }

    private VideoSurface videoSurface;
    [SerializeField] private Transform screenFrame;

    public void SetVideo(uint uid)
    {
        if (uid > 0)
        {
            videoSurface.SetForUser(uid);
            videoSurface.SetEnable(true);
        }
        else
        {
            videoSurface.SetForUser(0);
            videoSurface.SetEnable(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isSharing.Value) return;

        if (other.CompareTag("Player"))
        {
            panel.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            panel.SetActive(false);
        }
    }
}
