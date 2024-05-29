using System.Collections;
using System.Collections.Generic;
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
        }
    }

    public void OnClick_LoginButton()
    {
        // do some logic;
        EventHandler.RequestJoinWorld();
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
