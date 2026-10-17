using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OrientationManager : MonoBehaviour
{
    [SerializeField] private GameObject portraitOverlay;
    [SerializeField] private bool forceLandscape = true;

    private void Start()
    {
        CheckOrientation();
    }

    private void Update()
    {
        CheckOrientation();
    }

    private void CheckOrientation()
    {
        bool isMobile = CheckMobile.CheckIsMobile();

        bool isCurrentlyPortrait = Screen.height > Screen.width;
        bool isCurrentlyLandscape = Screen.width >= Screen.height;

        bool showOverlay = false;

        if (forceLandscape)
        {
            if (isCurrentlyPortrait)
            {
                showOverlay = true;
            }
        }
        else
        {
            if (isCurrentlyLandscape)
            {
                showOverlay = true;
            }
        }

        if (showOverlay)
        {
            if (!portraitOverlay.activeSelf)
            {
                portraitOverlay.SetActive(true);
            }
        }
        else
        {
            if (portraitOverlay.activeSelf)
            {
                portraitOverlay.SetActive(false);
            }
        }
    }
}
