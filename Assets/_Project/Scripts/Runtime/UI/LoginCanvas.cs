using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Text.RegularExpressions;

public class LoginCanvas : MonoBehaviour
{
    #region Serialize
    [SerializeField] private TMP_InputField userNameInputField;
    [SerializeField] private Button loginButton;
    [SerializeField] private TMP_Text errorText;
    #endregion

    #region Actions and Events
    [Space(10)]
    [Header("Button Events")]
    public UnityEvent<string> OnClickLoginButton;
    #endregion

    private TouchScreenKeyboard keyboard;
    private const string pattern = @"^[a-zA-Z0-9_.-]+$";

    private void Awake()
    {
        if (loginButton != null)
        {
            loginButton.onClick.RemoveAllListeners();
            loginButton.onClick.AddListener(() =>
            {
                if (!CheckInputField()) return;

                if (OnClickLoginButton != null) OnClickLoginButton.Invoke(userNameInputField.text);

                OnClick_LoginButton();
            });

            if (CheckMobile.Instance.CheckIsMobile())
            {
                keyboard = null;
                userNameInputField.onSelect.RemoveAllListeners();
                userNameInputField.onSelect.AddListener((text) =>
                {
                    Debug.Log(text);
                    TouchScreenKeyboard.hideInput = true;
                    keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default);
                });
            }
        }
    }

    private void OnGUI()
    {
        if (keyboard != null)
        {
            userNameInputField.text = keyboard.text;
        }
    }

    private void OnEnable()
    {
        EventHandler.ClientLoginFailedEvent += EventHandler_ClientLoginFailedEvent;
    }
    private void OnDisable()
    {
        EventHandler.ClientLoginFailedEvent -= EventHandler_ClientLoginFailedEvent;
    }

    private void EventHandler_ClientLoginFailedEvent(string obj)
    {
        ShowErrorMessage(obj);
    }


    public void OnClick_LoginButton()
    {
        EventHandler.OnClientLogin();
    }

    private bool CheckInputField()
    {
        string message = string.Empty;
        if(userNameInputField == null)
        {
            message = "InputField is null.";
        }
        else
        {
            if (string.IsNullOrEmpty(userNameInputField.text))
            {
                message = "Username can't be empty.";
            }
            else
            {
                var result = Regex.IsMatch(userNameInputField.text, pattern);

                if (result)
                {
                    message = string.Empty;
                }
                else
                {
                    message = "Username contains only numbers, letters, underscores (_), dots (.), and dashes (-).";
                }
            }
        }

        if (string.IsNullOrEmpty(message))
        {
            HideErrorMessage();
            return true;
        }
        else
        {
            ShowErrorMessage(message);
            return false;
        }
    }

    private void ShowErrorMessage(string _message)
    {
        errorText.gameObject.SetActive(true);

        errorText.text = _message;
    }

    private void HideErrorMessage()
    {
        if (errorText == null) return;

        errorText.text = string.Empty;

        errorText.gameObject.SetActive(false);
    }
}
