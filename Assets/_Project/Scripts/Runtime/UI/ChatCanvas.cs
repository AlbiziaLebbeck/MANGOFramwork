using UnityEngine;
using UnityEngine.UI;

public class ChatCanvas : MonoBehaviour
{
    #region SerializeField
    [Header("Buttons")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button screenSharingButton;
    [SerializeField] private Button videoCameraButton;
    [SerializeField] private Button microphoneButton;
    [SerializeField] private Button textChatButton;

    [Header("Sprite for Toggle")]
    [SerializeField] private Sprite screenShareSpriteOn;
    [SerializeField] private Sprite screenShareSpriteOff;
    [SerializeField] private Sprite videoCamSpriteOn;
    [SerializeField] private Sprite videoCamSpriteOff;
    [SerializeField] private Sprite micSpriteOn;
    [SerializeField] private Sprite micSpriteOff;
    [SerializeField] private Sprite fullscreenEnter;
    [SerializeField] private Sprite fullscreenExit;
    [SerializeField] private Sprite cameraViewsTabOn;
    [SerializeField] private Sprite cameraViewsTabOff;

    [Header("Panels")]
    [SerializeField] private CanvasGroup settingCanvasGroup;
    [SerializeField] private GameObject videoPreview;
    [SerializeField] private CanvasGroup containerPC;
    [SerializeField] private GameObject shareScreenOptionsPanel;
    [Header("Video View Child Count")]
    [SerializeField] private Transform videoViewContent;
    #endregion

    public enum SpriteType
    {
        ScreenOn,
        ScreenOff,
        VideoOn,
        VideoOff,
        MicOn,
        MicOff,
        FullScreenOn,
        FullScreenOff,
        CameraViewsTabOn,
        CameraViewsTabOff
    }

    [SerializeField] private bool isMicOn;
    [SerializeField] private bool isVideoOn;
    [SerializeField] private bool isOpenSharingOption;
    [SerializeField] private bool isSharing;
    [SerializeField] private bool isSettings;
    [SerializeField] private bool isShareAudio;

    private void Awake()
    {
        isMicOn = false;
        isVideoOn = false;
        isOpenSharingOption = false;
        isSharing = false;
        isSettings = false;

        SetShareAudio(false);

        settingCanvasGroup.alpha = 0f;
        settingCanvasGroup.gameObject.SetActive(false);
        videoPreview.GetComponent<CanvasGroup>().alpha = 0;

        SwitchSprite(screenSharingButton.gameObject, SpriteType.ScreenOn);
        SwitchSprite(videoCameraButton.gameObject, SpriteType.VideoOff);
        SwitchSprite(microphoneButton.gameObject, SpriteType.MicOff);

        OnChatReady(false);
        OnLeaveChat();

        if (CheckMobile.Instance.CheckIsMobile())
        {
            screenSharingButton.interactable = false;
        }
    }

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

    private void EventHandler_UserShareScreenStoppedEvent(uint _screenId)
    {
        if (_screenId != 0) return;
        SwitchSprite(screenSharingButton.gameObject, SpriteType.ScreenOn);
        isSharing = false;
    }

    private void EventHandler_UserShareScreenStartedEvent(uint _screenId)
    {
        if (_screenId != 0) return;
        SwitchSprite(screenSharingButton.gameObject, SpriteType.ScreenOff);
        isSharing = true;
    }

    public void OnClick_TextChat()
    {
        Debug.Log("OnClick_TextChat");
    }

    public void OnClick_Microphone()
    {
        isMicOn = !isMicOn;

        AgoraManager.Instance.OnMic(isMicOn);

        SwitchSprite(microphoneButton.gameObject, isMicOn ? SpriteType.MicOn : SpriteType.MicOff);
    }

    public void OnClick_Camera()
    {
        isVideoOn = !isVideoOn;

        AgoraManager.Instance.OnVideo(isVideoOn);

        videoPreview.GetComponent<CanvasGroup>().alpha = isVideoOn? 1f : 0f;

        SwitchSprite(videoCameraButton.gameObject, isVideoOn ? SpriteType.VideoOn : SpriteType.VideoOff);
    }

    public void OnClick_OpenScreenSharingOptionButton()
    {
        isOpenSharingOption = !isOpenSharingOption;

        if (isSharing)
        {
            AgoraManager.Instance.OnShareScreen(false, isShareAudio);

            SwitchSprite(screenSharingButton.gameObject, SpriteType.ScreenOn);
            isSharing = false;
        }
        else
        {
            shareScreenOptionsPanel.SetActive(isOpenSharingOption);
        }
    }

    public void OnClick_ShareScreen()
    {
        isSharing = true;

        AgoraManager.Instance.OnShareScreen(true, isShareAudio);

        SwitchSprite(screenSharingButton.gameObject, SpriteType.ScreenOff);
        shareScreenOptionsPanel.SetActive(false);
        isOpenSharingOption = false;
    }

    public void OnClick_Settings()
    {
        isSettings = !isSettings;

        if (isSettings)
        {
            settingCanvasGroup.alpha = 1.0f;

            settingCanvasGroup.gameObject.SetActive(true);
        }
        else
        {
            settingCanvasGroup.alpha = 0f;

            settingCanvasGroup.gameObject.SetActive(false);
        }
    }

    public void SetShareAudio(bool share)
    {
        isShareAudio = share;
    }

    internal void SwitchSprite(GameObject uiElement, SpriteType type)
    {
        if (!uiElement.TryGetComponent<Image>(out Image imageToChange)) return;

        switch (type)
        {
            case SpriteType.ScreenOn:
                imageToChange.sprite = screenShareSpriteOn;
                break;
            case SpriteType.ScreenOff:
                imageToChange.sprite = screenShareSpriteOff;
                break;
            case SpriteType.VideoOn:
                imageToChange.sprite = videoCamSpriteOn;
                break;
            case SpriteType.VideoOff:
                imageToChange.sprite = videoCamSpriteOff;
                break;
            case SpriteType.MicOn:
                imageToChange.sprite = micSpriteOn;
                break;
            case SpriteType.MicOff:
                imageToChange.sprite = micSpriteOff;
                break;
            case SpriteType.FullScreenOn:
                imageToChange.sprite = fullscreenEnter;
                break;
            case SpriteType.FullScreenOff:
                imageToChange.sprite = fullscreenExit;
                break;
            case SpriteType.CameraViewsTabOn:
                imageToChange.sprite = cameraViewsTabOn;
                break;
            case SpriteType.CameraViewsTabOff:
                imageToChange.sprite = cameraViewsTabOff;
                break;
            default:
                break;
        }
    }

    public void OnJoinChat()
    {
        containerPC.alpha = 1;
    }

    public void OnChatReady(bool isReady)
    {
        microphoneButton.interactable = isReady;
        videoCameraButton.interactable = isReady;
        textChatButton.interactable = isReady;
        settingsButton.interactable = isReady;
        screenSharingButton.interactable = isReady;
    }

    public void OnLeaveChat()
    {
        containerPC.alpha = 0;
    }
}
