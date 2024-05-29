using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LoadingCanvas : MonoBehaviour
{
    [SerializeField] private TMP_Text infomationDisplayText;
    [SerializeField] private TMP_Text loadingText;

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
}
