using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UserDataCanvas : MonoBehaviour
{
    [SerializeField] private CanvasGroup container;
    [SerializeField] private TMP_Text userNameText;
    [SerializeField] private RawImage avatarImage;

    private void Awake()
    {
        container.alpha = 0f;
    }

    private void OnEnable()
    {
        EventHandler.ClientLoginSuccessEvent += EventHandler_ClientLoginSuccessEvent;
    }

    private void OnDisable()
    {
        EventHandler.ClientLoginSuccessEvent -= EventHandler_ClientLoginSuccessEvent;
    }

    private void EventHandler_ClientLoginSuccessEvent()
    {
        Initialize();
    }

    public void Initialize()
    {
        OpenCanvas();
    }

    public void SetAvatarImage(Texture newImage)
    {
        avatarImage.texture = newImage;
    }
    public void SetUserNameText(string text)
    {
        userNameText.text = text;
    }

    public void OpenCanvas()
    {
        container.alpha = 1.0f;
    }

    public void CloseCanvas()
    {
        container.alpha = 0f;
    }
}
