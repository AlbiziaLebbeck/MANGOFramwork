using agora_gaming_rtc;
using UnityEngine;

public class ScreenSharingProjector : NetworkInteractable
{
    [SerializeField] private Transform screenFrame;
    private VideoSurface videoSurface;

    private void Awake()
    {
        SetupVideoSurface();
    }

    private void SetupVideoSurface()
    {
        videoSurface = screenFrame.gameObject.AddComponent<VideoSurface>();
        SetVideo(0);
    }

    public void SetVideo(uint uid)
    {
        if (uid > 0)
        {
            videoSurface.SetForUser(uid);
            videoSurface.SetEnable(true);
        }
        else
        {
            videoSurface.SetForUser(0);
            videoSurface.SetEnable(false);
        }
    }
}
