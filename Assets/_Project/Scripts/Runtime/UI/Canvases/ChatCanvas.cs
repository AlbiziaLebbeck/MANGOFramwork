using MANGOsFramework.Voice;
using UnityEngine;
using UnityEngine.UI;

public class ChatCanvas : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button screenSharingButton;
    [SerializeField] private Button videoCameraButton;
    [SerializeField] private Button microphoneButton;
    [SerializeField] private Button textChatButton;

    [Header("Sprite for Toggle")]
    [SerializeField] private Sprite micSpriteOn;
    [SerializeField] private Sprite micSpriteOff;

    [Header("Panels")]
    [SerializeField] private CanvasGroup settingCanvasGroup;
    [SerializeField] private GameObject videoPreview;
    [SerializeField] private CanvasGroup containerPC;
    [SerializeField] private GameObject shareScreenOptionsPanel;

    private void Awake()
    {
        SetUnsupportedControlVisible(settingsButton, false);
        SetUnsupportedControlVisible(screenSharingButton, false);
        SetUnsupportedControlVisible(videoCameraButton, false);
        SetUnsupportedControlVisible(textChatButton, false);

        SetPanelVisible(settingCanvasGroup, false);
        SetPanelVisible(videoPreview, false);
        SetPanelVisible(shareScreenOptionsPanel, false);

        SetMicrophoneVisual(false);
        OnChatReady(false);
        OnLeaveChat();
    }

    private void OnEnable()
    {
        EventHandler.LocalClientCompleteSetupEvent += HandleLocalClientReady;
        if (VoiceCommunicationManager.Instance != null)
        {
            VoiceCommunicationManager.Instance.StateChanged += HandleVoiceStateChanged;
        }
    }

    private void OnDisable()
    {
        EventHandler.LocalClientCompleteSetupEvent -= HandleLocalClientReady;
        if (VoiceCommunicationManager.Instance != null)
        {
            VoiceCommunicationManager.Instance.StateChanged -= HandleVoiceStateChanged;
        }
    }

    public void OnClick_Microphone()
    {
        VoiceCommunicationManager manager = VoiceCommunicationManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("Voice communication is not available.");
            return;
        }

        manager.ToggleVoice();
    }

    public void OnClick_TextChat()
    {
    }

    public void OnClick_Camera()
    {
    }

    public void OnClick_OpenScreenSharingOptionButton()
    {
    }

    public void OnClick_ShareScreen()
    {
    }

    public void OnClick_Settings()
    {
    }

    public void SetShareAudio(bool share)
    {
    }

    public void OnJoinChat()
    {
        if (containerPC != null)
        {
            containerPC.alpha = 1f;
            containerPC.blocksRaycasts = true;
            containerPC.interactable = true;
        }
    }

    public void OnChatReady(bool isReady)
    {
        if (microphoneButton != null)
        {
            microphoneButton.interactable = isReady;
        }
    }

    public void OnLeaveChat()
    {
        if (containerPC != null)
        {
            containerPC.alpha = 0f;
            containerPC.blocksRaycasts = false;
            containerPC.interactable = false;
        }
    }

    private void HandleLocalClientReady()
    {
        OnJoinChat();
        OnChatReady(true);

        VoiceCommunicationManager manager = VoiceCommunicationManager.Instance;
        if (manager != null)
        {
            HandleVoiceStateChanged(manager.State, string.Empty);
        }
    }

    private void HandleVoiceStateChanged(VoiceCommunicationState state, string message)
    {
        bool isBusy = state == VoiceCommunicationState.Connecting ||
                      state == VoiceCommunicationState.Disconnecting;
        OnChatReady(!isBusy && state != VoiceCommunicationState.Unavailable);
        SetMicrophoneVisual(state == VoiceCommunicationState.Connected);

        if (state == VoiceCommunicationState.Failed && !string.IsNullOrWhiteSpace(message))
        {
            Debug.LogWarning($"Voice communication failed: {message}");
        }
    }

    private void SetMicrophoneVisual(bool isActive)
    {
        if (microphoneButton == null ||
            !microphoneButton.TryGetComponent(out Image image))
        {
            return;
        }

        image.sprite = isActive ? micSpriteOn : micSpriteOff;
    }

    private static void SetUnsupportedControlVisible(Button button, bool visible)
    {
        if (button != null)
        {
            button.gameObject.SetActive(visible);
        }
    }

    private static void SetPanelVisible(CanvasGroup panel, bool visible)
    {
        if (panel == null)
        {
            return;
        }

        panel.alpha = visible ? 1f : 0f;
        panel.blocksRaycasts = visible;
        panel.interactable = visible;
        panel.gameObject.SetActive(visible);
    }

    private static void SetPanelVisible(GameObject panel, bool visible)
    {
        if (panel != null)
        {
            panel.SetActive(visible);
        }
    }
}
