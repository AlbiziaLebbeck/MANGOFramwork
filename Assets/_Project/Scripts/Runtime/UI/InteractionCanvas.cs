using System;
using TMPro;
using UnityEngine;

public class InteractionCanvas : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private RectTransform interactEnterButton;
    [SerializeField] private TMP_Text interactText;
    [SerializeField] private RectTransform interactExitButton;
    private Action buttonEnterAction;
    private Action buttonExitAction;
    private Transform buttonAnchor;
    private bool wereInteracted;

    private void Start()
    {
        if (interactExitButton != null)
        {
            interactExitButton.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (buttonAnchor != null && interactEnterButton != null)
        {
            Vector3 screenPos = mainCamera.WorldToScreenPoint(buttonAnchor.position);

            if (screenPos.z > 0)
            {
                interactEnterButton.position = screenPos;
                interactEnterButton.gameObject.SetActive(true);
            }
            else
            {
                interactEnterButton.gameObject.SetActive(false);
            }
        }
        else
        {
            interactEnterButton.gameObject.SetActive(false);
        }
    }

    public void AddEnterButtonEvent(Action EnterAction)
    {
        buttonEnterAction = EnterAction;
    }

    public void AddExitButtonEvent(Action ExitAction)
    {
        buttonExitAction = ExitAction;
        interactExitButton.gameObject.SetActive(true);
    }

    public void AssignInteractionText(string text)
    {
        interactText.text = text;
    }

    public void SetButtonToTarget(Transform targetButtonAnchor)
    {
        buttonAnchor = targetButtonAnchor;
    }

    public void RemoveButtonEvent()
    {
        buttonEnterAction = null;
    }

    public void ResetInteractionCanvas()
    {
        buttonEnterAction = null;
        buttonExitAction = null;
        interactEnterButton.gameObject.SetActive(false);
        interactExitButton.gameObject.SetActive(false);
    }

    public void RemoveButtonText()
    {
        interactText.text = "Interact";
    }

    public void OnClick_InteractEnterButton()
    {
        if (buttonEnterAction != null)
        {
            buttonEnterAction();
            buttonEnterAction = null;
            interactEnterButton.gameObject.SetActive(false);

        }
    }

    public void OnClick_InteractExitButton()
    {
        if (buttonExitAction != null)
        {
            buttonExitAction();

            buttonExitAction = null;

            interactExitButton.gameObject.SetActive(false);
        }
    }
}