using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UserDataCanvas : MonoBehaviour
{
    [SerializeField] private CanvasGroup container;
    [SerializeField] private TMP_Text userNameText;
    [SerializeField] private RawImage avatarImage;
    [SerializeField] private TMP_Text mangosGoldText;

    public int MangosGold { get; private set; }

    private void Awake()
    {
        container.alpha = 0f;
        container.gameObject.SetActive(false);
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
        if (avatarImage == null)
        {
            Debug.LogError("UserDataCanvas avatar image reference is missing.", this);
            return;
        }

        avatarImage.texture = newImage;
    }
    public void SetUserNameText(string text)
    {
        userNameText.text = text;
    }

    public void SetMangosGold(int amount)
    {
        MangosGold = amount;
        if (mangosGoldText != null)
        {
            mangosGoldText.text = amount.ToString();
        }
    }

    public void OpenCanvas()
    {
        container.alpha = 1.0f;
        container.gameObject.SetActive(true);
    }

    public void CloseCanvas()
    {
        container.alpha = 0f;
        container.gameObject.SetActive(false);
    }
}
