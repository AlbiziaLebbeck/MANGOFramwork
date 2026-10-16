using UnityEngine;
using UnityEngine.UI;

// Preserve serialized component and UnityEvent entry points on existing prefabs.
// The browser overlay owns communication controls.
public class ChatCanvas : MonoBehaviour
{
    [SerializeField] private Button settingsButton, screenSharingButton, videoCameraButton, microphoneButton, textChatButton;
    [SerializeField] private CanvasGroup settingCanvasGroup, containerPC;
    [SerializeField] private GameObject videoPreview, shareScreenOptionsPanel;
    private void Awake() => HideLegacyControls();
    private void HideLegacyControls()
    {
        foreach (var button in new[] { settingsButton, screenSharingButton, videoCameraButton, microphoneButton, textChatButton })
            if (button != null) button.gameObject.SetActive(false);
        foreach (var group in new[] { settingCanvasGroup, containerPC })
            if (group != null) { group.alpha = 0; group.interactable = false; group.blocksRaycasts = false; }
        if (videoPreview != null) videoPreview.SetActive(false);
        if (shareScreenOptionsPanel != null) shareScreenOptionsPanel.SetActive(false);
    }
    public void OnJoinChat() => HideLegacyControls();
    public void OnLeaveChat() => HideLegacyControls();
    public void OnChatReady(bool ready) { }
    public void OnClick_Microphone() { }
    public void OnClick_TextChat() { }
    public void OnClick_Camera() { }
    public void OnClick_OpenScreenSharingOptionButton() { }
    public void OnClick_ShareScreen() { }
    public void OnClick_Settings() { }
    public void SetShareAudio(bool share) { }
}
