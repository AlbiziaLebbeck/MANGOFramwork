using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UserVideoView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button fullscreenButton;
    [SerializeField] private UIFader fader;
    [SerializeField] private uint uid;

    [SerializeField] private GameObject overlay;
    [SerializeField] private GameObject micStatus;
    [SerializeField] private GameObject camStatus;
    [SerializeField] private TMP_Text nameText;

    private ShareScreenCanvas screenCanvas;
    [SerializeField] private bool isSharing;

    private void Awake()
    {
        screenCanvas = PersistentCanvas.ShareScreenCanvas;

        EventHandler.UserMicMuteUpdateEvent += EventHandler_UserMicMuteUpdateEvent;
        EventHandler.UserCamMuteUpdateEvent += EventHandler_UserCamMuteUpdateEvent;

        EventHandler.UserShareScreenStartedEvent += EventHandler_UserShareScreenStartedEvent;
        EventHandler.UserShareScreenStoppedEvent += EventHandler_UserShareScreenStoppedEvent;
    }

    private void OnDestroy()
    {
        EventHandler.UserMicMuteUpdateEvent -= EventHandler_UserMicMuteUpdateEvent;
        EventHandler.UserCamMuteUpdateEvent -= EventHandler_UserCamMuteUpdateEvent;

        if(uid != 0)
        {
            EventHandler.UserShareScreenStartedEvent -= EventHandler_UserShareScreenStartedEvent;
            EventHandler.UserShareScreenStoppedEvent -= EventHandler_UserShareScreenStoppedEvent;
        }
    }

    private void EventHandler_UserMicMuteUpdateEvent(uint _uid, bool _mute)
    {
        if (_uid != uid) return;
        micStatus?.SetActive(_mute);
    }

    private void EventHandler_UserCamMuteUpdateEvent(uint _uid, bool _mute)
    {
        if (_uid != uid) return;
        camStatus?.SetActive(_mute);
    }

    private void EventHandler_UserShareScreenStoppedEvent(uint _screenId)
    {
        if (_screenId != uid) return;
        isSharing = false;
        fullscreenButton?.onClick.RemoveAllListeners();
    }

    private void EventHandler_UserShareScreenStartedEvent(uint _screenId)
    {
        if (_screenId != uid) return;
        isSharing = true;
        fullscreenButton?.onClick.RemoveAllListeners();
        fullscreenButton?.onClick.AddListener(delegate { OnClick_FullScreen(); });
    }

    public void RelocateOverlay()
    {
        overlay.transform.SetAsLastSibling();
        fullscreenButton?.transform.SetAsLastSibling();
    }

    public void SetName(string name)
    {
        nameText.transform.parent.gameObject.SetActive(true);
        nameText.text = name;
    }

    public void AssignUid(uint uid)
    {
        this.uid = uid;

        // if uid == 0 means local views
        // then get rid of share event, make sure that it's not get called.

        if(uid == 0)
        {
            EventHandler.UserShareScreenStartedEvent -= EventHandler_UserShareScreenStartedEvent;
            EventHandler.UserShareScreenStoppedEvent -= EventHandler_UserShareScreenStoppedEvent;
        }
    }

    private void OnClick_FullScreen()
    {
        screenCanvas?.OnClick_OpenFullScreen();
        screenCanvas?.SetVideo(uid);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (uid == 0) return;
        if (!isSharing) return;

        fader?.FadeIn();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (uid == 0) return;
        if (!isSharing) return;

        fader?.FadeOut();
    }
}
