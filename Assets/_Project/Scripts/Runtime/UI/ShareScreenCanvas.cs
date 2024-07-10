using agora_gaming_rtc;
using System.Collections.Generic;
using UnityEngine;

public class ShareScreenCanvas : MonoBehaviour
{
    [Header("Engine Handler")]
    [SerializeField] private AgoraManager agoraManager;

    [Header("References")]
    [SerializeField] private GameObject popUpScreenPanel;
    [SerializeField] private GameObject screenVideoSurface;

    [SerializeField] private List<uint> uids = new List<uint>();
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
        //onprojectorOpen;
        //onprojectorClose;
    }

    private void OnDisable()
    {
        //onprojectorOpen;
        //onprojectorClose;
    }

    private void OnProjectorOpen(uint _uid)
    {
        if (uids.Count < 1)
        {
            SetVideo(_uid);
        }

        uids.Add(_uid);
    }

    public bool IsSharing(uint _uid)
    {
        return uids.Contains(_uid);
    }

    private void OnProjectorClose(uint _uid)
    {
        uids.Remove(_uid);

        if (uids.Count == 0)
        {
            SetVideo(0);

            if (isPopped)
            {
                TogglePopUpScreen();
            }
        }
        else
        {
            OnClick_PreviousScreen();
        }
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

        if (currentScreenIndex > uids.Count - 1) currentScreenIndex = 0;

        SetVideo(uids[currentScreenIndex]);
    }

    public void OnClick_PreviousScreen()
    {
        currentScreenIndex -= 1;

        if (currentScreenIndex < 0) currentScreenIndex = uids.Count == 0 ? 0 : uids.Count - 1;

        SetVideo(uids[currentScreenIndex]);
    }

    public void SetVideo(uint _uid)
    {
        if (_uid > 0)
        {
            videoSurface.SetForUser(_uid);
            videoSurface.SetEnable(true);

            currentScreenIndex = uids.FindIndex(x => uids.Equals(_uid));
        }
        else
        {
            videoSurface.SetForUser(0);
            videoSurface.SetEnable(false);
        }
    }
}
