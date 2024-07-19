using agora_gaming_rtc;
using System.Collections.Generic;
using UnityEngine;

public class ScreenSharingProjector : MonoBehaviour
{
    public static Dictionary<int, ScreenSharingProjector> projectors = new Dictionary<int, ScreenSharingProjector>();
    public static Dictionary<uint, ScreenSharingProjector> usedProjectors = new Dictionary<uint, ScreenSharingProjector>();

    private VideoSurface videoSurface;
    public static int currentProject = 0;

    public bool IsProjected;

    private void Awake()
    {
        projectors[gameObject.GetInstanceID()] = this;
        currentProject = 0;
    }

    private void OnDestroy()
    {
        projectors.Remove(gameObject.GetInstanceID());
    }
}
