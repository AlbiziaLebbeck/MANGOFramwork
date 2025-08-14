using TMPro;
using UnityEngine;

public class LoadingCanvas : MonoBehaviour
{
    [SerializeField] private TMP_Text infomationDisplayText;
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private GameObject panelGroup;
    [SerializeField] private LoadingSpinner spinner;

    public void SetInformationDisplay(string message)
    {
        if(infomationDisplayText != null)
        {
            infomationDisplayText.text = message;
        }
    }
    public void SetLoadingDisplay(string message)
    {
        if (loadingText != null)
        {
            loadingText.text = message;
        }
    }

    public void ToggleLoadingScreen(bool toggle)
    {
        panelGroup.SetActive(toggle);
    }

    public void ToggleSpinner(bool toggle)
    {
        spinner.Rotation = toggle;
    }
}
