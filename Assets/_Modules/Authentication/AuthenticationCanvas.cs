using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MANGOsFramework.Experiment
{
    public class AuthenticationCanvas : MonoBehaviour
    {
        [Header("MainCanvas")]
        [SerializeField] private TMP_Text messageText;

        [Header("SignIn")]
        [SerializeField] private GameObject signInPanel;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button continueAsGuestButton;

        [Header("UserVerification")]
        [SerializeField] private GameObject userVerificationPanel;
        [SerializeField] private TMP_Text usernameText;
        [SerializeField] private TMP_Text usernameOnButtonText;
        [SerializeField] private Button continueAsUserButton;
        [SerializeField] private Button loginAsNewUserButton;

        [Header("AskGuestName")]
        [SerializeField] private GameObject askGuestNamePanel;
        [SerializeField] private TMP_InputField guestNameInputField;
        [SerializeField] private Button confirmGuestNameButton;
        [SerializeField] private Button backToLoginPageButton;

        public event Action OnLogin;
        public event Action<string> OnLoginAsGuest;
        public event Action OnContinueAsUser;
        public event Action OnLoginAsNewUser;

        private TouchScreenKeyboard keyboard;

        private void Awake()
        {
            signInPanel.SetActive(false);
            userVerificationPanel.SetActive(false);

#if !UNITY_EDITOR
            if (CheckMobile.CheckIsMobile())
            {
                keyboard = null;
                guestNameInputField.onSelect.RemoveAllListeners();
                guestNameInputField.onSelect.AddListener((text) =>
                {
                    TouchScreenKeyboard.hideInput = true;
                    keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default);
                });
            }
#endif
        }

        private void SetupCanvas()
        {
            loginButton.onClick.RemoveAllListeners();
            loginButton.onClick.AddListener(() =>
            {
                OnClick_Login();
                DisableAllButtons();
            });

            continueAsGuestButton.onClick.RemoveAllListeners();
            continueAsGuestButton.onClick.AddListener(() =>
            {
                OnClick_LoginAsGuest();
                DisableAllButtons();
            });

            confirmGuestNameButton.onClick.RemoveAllListeners();
            confirmGuestNameButton.onClick.AddListener(() =>
            {
                OnClick_ConfirmGuestName();
                DisableAllButtons();
            });

            backToLoginPageButton.onClick.RemoveAllListeners();
            backToLoginPageButton.onClick.AddListener(() =>
            {
                ResetCanvas();
            });

            continueAsUserButton.onClick.RemoveAllListeners();
            continueAsUserButton.onClick.AddListener(() =>
            {
                OnClick_ContinueAsUser();
                DisableAllButtons();
            });

            loginAsNewUserButton.onClick.RemoveAllListeners();
            loginAsNewUserButton.onClick.AddListener(() =>
            {
                DisableAllButtons();
                OnClick_LoginWithNewUser();
            });

            guestNameInputField.onValueChanged.AddListener(OnNameChanged);
        }

#if !UNITY_EDITOR
        private void OnGUI()
        {
            if (keyboard != null)
            {
                guestNameInputField.text = keyboard.text;
            }
        }
#endif

        private Coroutine typingCoroutine;
        private float typingDelay = 0.5f;

        void OnNameChanged(string text)
        {
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);

            typingCoroutine = StartCoroutine(WaitAndValidate(text));
        }

        IEnumerator WaitAndValidate(string text)
        {
            yield return new WaitForSeconds(typingDelay);

            if (PlayerNameValidator.IsValidName(text, out string reason))
            {
                confirmGuestNameButton.interactable = true;
                UpdateFooterMessage("Click Confirm when you're ready!");
            }
            else
            {
                confirmGuestNameButton.interactable = false;

                if (!string.IsNullOrEmpty(reason))
                    UpdateFooterMessage(reason);
            }
        }

        public void Init(bool hasLoggedIn)
        {
            SetupCanvas();

            signInPanel.SetActive(!hasLoggedIn);
            userVerificationPanel.SetActive(hasLoggedIn);

            askGuestNamePanel.SetActive(false);
        }

        private void OnAskGuestName()
        {
            signInPanel.SetActive(false);
            askGuestNamePanel.SetActive(true);
        }

        public void ResetCanvas()
        {
            // there's might be different cases. but now just reset them all.
            loginButton.interactable = true;
            continueAsGuestButton.interactable = true;
            confirmGuestNameButton.interactable = true;
            continueAsUserButton.interactable = true;
            loginAsNewUserButton.interactable = true;

            UpdateFooterMessage("");

            Init(false);
        }

        private void DisableAllButtons()
        {
            loginButton.interactable = false;
            continueAsGuestButton.interactable = false;
            confirmGuestNameButton.interactable = false;
            continueAsUserButton.interactable = false;
            loginAsNewUserButton.interactable = false;
        }

        private void OnClick_Login() => OnLogin?.Invoke();
        private void OnClick_LoginAsGuest() => OnAskGuestName();
        private void OnClick_ConfirmGuestName() => OnLoginAsGuest?.Invoke(guestNameInputField.text);
        private void OnClick_ContinueAsUser() => OnContinueAsUser?.Invoke();
        private void OnClick_LoginWithNewUser() => OnLoginAsNewUser?.Invoke();

        public void UpdateFooterMessage(string message)
        {
            if (messageText != null)
            {
                messageText.text = message;
            }
        }

        public void DisplayUsername(string name)
        {
            if (usernameText != null) usernameText.text = name;
            if (usernameOnButtonText != null) usernameOnButtonText.text = $"Yes, Continue as <b>{name}</b>";
        }
    }
}

