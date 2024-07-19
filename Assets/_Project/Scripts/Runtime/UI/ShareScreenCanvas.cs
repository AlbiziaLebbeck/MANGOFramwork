using agora_gaming_rtc;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ShareScreenCanvas : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject popUpScreenPanel;
    [SerializeField] private GameObject screenVideoSurface;

    [SerializeField] private List<uint> sharingScreenIds = new List<uint>();
    [SerializeField] private int currentScreenIndex;
    
    private bool isPopped;
    private VideoSurface videoSurface;

    private void Awake()
    {
        videoSurface = screenVideoSurface.AddComponent<VideoSurface>();
        videoSurface.SetVideoSurfaceType(AgoraVideoSurfaceType.RawImage);
        videoSurface.EnableFilpTextureApply(false, true);
        videoSurface.SetEnable(false);

        isPopped = false;
        popUpScreenPanel.SetActive(isPopped);
    }

    private void OnEnable()
    {
        EventHandler.UserShareScreenStartedEvent += EventHandler_UserShareScreenStartedEvent;
        EventHandler.UserShareScreenStoppedEvent += EventHandler_UserShareScreenStoppedEvent;
        EventHandler.ScreenRatioUpdateEvent += EventHandler_ScreenResolutionUpdateEvent;
    }

    private void OnDisable()
    {
        EventHandler.UserShareScreenStartedEvent -= EventHandler_UserShareScreenStartedEvent;
        EventHandler.UserShareScreenStoppedEvent -= EventHandler_UserShareScreenStoppedEvent;
        EventHandler.ScreenRatioUpdateEvent -= EventHandler_ScreenResolutionUpdateEvent;
    }

    private void EventHandler_ScreenResolutionUpdateEvent(uint screenId, float aspectRatio)
    {
        if (currentScreenIndex < 0) return;

        if(sharingScreenIds[currentScreenIndex] == screenId)
        {
            if(videoSurface.TryGetComponent(out AspectRatioFitter fitter))
            {
                fitter.aspectRatio = aspectRatio;
            }
        }
    }

    private void EventHandler_UserShareScreenStartedEvent(uint _screenId)
    {
        if(sharingScreenIds.Count < 1)
        {
            SetVideo(_screenId);
        }

        sharingScreenIds.Add(_screenId);
    }
    private void EventHandler_UserShareScreenStoppedEvent(uint _screenId)
    {
        sharingScreenIds.Remove(_screenId);

        if(sharingScreenIds.Count == 0)
        {
            SetVideo(0);
            if(isPopped)
            {
                TogglePopUpScreen();
            }
        }
        else
        {
            OnClick_PreviousScreen();
        }
    }

    public bool IsSharing(uint _uid)
    {
        return sharingScreenIds.Contains(_uid);
    }

    public void TogglePopUpScreen()
    {
        isPopped = !isPopped;
        popUpScreenPanel.SetActive(isPopped);
    }

    public void OnClick_ExitFullScreen()
    {
        isPopped = false;
        popUpScreenPanel.SetActive(false);
    }

    public void OnClick_OpenFullScreen()
    {
        isPopped = true;
        popUpScreenPanel.SetActive(true);
    }

    public void OnClick_NextScreen()
    {
        currentScreenIndex += 1;

        if (currentScreenIndex > sharingScreenIds.Count - 1) currentScreenIndex = 0;

        SetVideo(sharingScreenIds[currentScreenIndex]);
    }

    public void OnClick_PreviousScreen()
    {
        currentScreenIndex -= 1;

        if (currentScreenIndex < 0) currentScreenIndex = sharingScreenIds.Count == 0 ? 0 : sharingScreenIds.Count - 1;

        SetVideo(sharingScreenIds[currentScreenIndex]);
    }

    public void SetVideo(uint _uid)
    {
        if (_uid > 0)
        {
            videoSurface.SetForUser(_uid);
            videoSurface.SetEnable(true);
            currentScreenIndex = sharingScreenIds.IndexOf(_uid);

            if(currentScreenIndex < 0)
            {
                currentScreenIndex = 0;
            }
        }
        else
        {
            videoSurface.SetForUser(0);
            videoSurface.SetEnable(false);
        }
    }
}
