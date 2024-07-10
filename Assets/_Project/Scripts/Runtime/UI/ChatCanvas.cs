using UnityEngine;
using UnityEngine.UI;

public class ChatCanvas : MonoBehaviour
{
    #region SerializeField
    [Header("Engine Handler")]
    [SerializeField] private AgoraManager agoraManager;

    [Header("Buttons")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button screenSharingButton;
    [SerializeField] private Button videoCameraButton;
    [SerializeField] private Button microphoneButton;
    [SerializeField] private Button textChatButton;
    [SerializeField] private Button previewButton;
    [SerializeField] private Button cameraViewsTabButton;

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
    [SerializeField] private CanvasGroup maximizedCameraViewTab;
    [SerializeField] private CanvasGroup minimizedCameraViewTab;
    [SerializeField] private GameObject videoPreview;
    [SerializeField] private CanvasGroup containerPC;
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

    private bool isMicOn;
    private bool isVideoOn;
    private bool isScreenShareOn;
    private bool isSettings;
    private bool isShareAudio;
    private bool isPreview;
    private bool isCameraViewTabOn;

    private void Awake()
    {
        isMicOn = false;
        isVideoOn = false;
        isScreenShareOn = false;
        isSettings = false;
        isPreview = false;
        isCameraViewTabOn = true;

        SetShareAudio(false);

        settingCanvasGroup.alpha = 0f;
        maximizedCameraViewTab.alpha = 1f;
        minimizedCameraViewTab.alpha = 0f;
        videoPreview.GetComponent<CanvasGroup>().alpha = 0;

        SwitchSprite(screenSharingButton.gameObject, SpriteType.ScreenOn);
        SwitchSprite(videoCameraButton.gameObject, SpriteType.VideoOff);
        SwitchSprite(microphoneButton.gameObject, SpriteType.MicOff);

        OnChatReady(false);
        OnLeaveChat();
    }

    private void Update()
    {
        if(videoViewContent.childCount == 0)
        {
            cameraViewsTabButton.interactable = false;
        }
        else
        {
            cameraViewsTabButton.interactable = true;
        }
    }

    public void OnClick_TextChat()
    {
        Debug.Log("OnClick_TextChat");
    }

    public void OnClick_Microphone()
    {
        isMicOn = !isMicOn;

        agoraManager.OnMic(isMicOn);

        SwitchSprite(microphoneButton.gameObject, isMicOn ? SpriteType.MicOn : SpriteType.MicOff);
    }

    public void OnClick_Camera()
    {
        isVideoOn = !isVideoOn;

        agoraManager.OnVideo(isVideoOn);

        videoPreview.GetComponent<CanvasGroup>().alpha = isVideoOn? 1f : 0f;

        SwitchSprite(videoCameraButton.gameObject, isVideoOn ? SpriteType.VideoOn : SpriteType.VideoOff);
    }

    public void OnClick_ScreenSharing()
    {
        isScreenShareOn = !isScreenShareOn;

        //agoraManager.OnShareScreen(isScreenShareOn, isShareAudio);

        SwitchSprite(screenSharingButton.gameObject, isScreenShareOn ? SpriteType.ScreenOff : SpriteType.ScreenOn);
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

    public void OnClick_PreviewVideo()
    {
        isPreview = !isPreview;
        videoPreview.GetComponent<CanvasGroup>().alpha = 1.0f;

        if (isPreview)
        {
            if(!agoraManager.previewing) agoraManager.StartPreview();
        }
        else
        {
            if (agoraManager.previewing) agoraManager.StopPreview();
        }
    }

    public void OnClick_CameraViewTabButton()
    {
        isCameraViewTabOn = !isCameraViewTabOn;

        maximizedCameraViewTab.alpha = isCameraViewTabOn? 1.0f : 0.0f;
        minimizedCameraViewTab.alpha = isCameraViewTabOn? 0.0f : 1.0f;

        agoraManager.OnMuteRemoteVideo(!isCameraViewTabOn);

        SwitchSprite(cameraViewsTabButton.gameObject, isCameraViewTabOn ? SpriteType.CameraViewsTabOn : SpriteType.CameraViewsTabOff);
    }

    public void SetShareAudio(bool share)
    {
        isShareAudio = share;
    }

    public void SetPreviewVideo(GameObject videoView)
    {
        videoView.transform.SetParent(videoPreview.transform.GetChild(0));
        videoView.transform.position = Vector3.zero;
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
