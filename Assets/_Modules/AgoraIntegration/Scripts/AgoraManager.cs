using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using agora_gaming_rtc;
using System.Linq;
using agora_utilities;
using System.Collections;
using static UnityEngine.UI.AspectRatioFitter;

public class AgoraManager : Singleton<AgoraManager>
{
    #region SerializeField
    [Header("SerializeField")]
    [SerializeField] private AppInfoObject appInfo;
    [SerializeField] private string appId;
    [SerializeField] private string channelForTest;
    [SerializeField] private GameObject userVideoViewPrefab;
    [SerializeField] private Transform videoViewContainer;
    [SerializeField] private Transform selfViewContainer;

    [SerializeField] private Dropdown videoDropdown, recordingDropdown, playbackDropdown;

    [SerializeField] private int recordingDeviceIndex = 0;
    [SerializeField] private int playbackDeviceIndex = 0;
    [SerializeField] private int videoDeviceIndex = 0;
    #endregion

    #region Private
    private string ChannelName { get; set; }
    private const uint UID_PREFIX = 1;
    private const uint SCREEN_PREFIX = 9;
    private uint uid;
    private uint screenId;
    private Dictionary<uint, GameObject> videoViews = new Dictionary<uint, GameObject>();
    private bool isCameraOn;
    private bool isSharing;
    #endregion

    #region Device
    private Dictionary<int, string> audioRecordingDeviceDict = new();
    private Dictionary<int, string> audioRecordingDeviceNameDict = new();
    private Dictionary<int, string> audioPlaybackDeviceDict = new();
    private Dictionary<int, string> audioPlaybackDeviceNameDict = new();
    private Dictionary<int, string> videoDeviceManagerDict = new();
    private Dictionary<int, string> videoDeviceManagerNameDict = new();

    private AudioRecordingDeviceManager audioRecordingDeviceManager = null;
    private AudioPlaybackDeviceManager audioPlaybackDeviceManager = null;
    private VideoDeviceManager videoDeviceManager = null;
    #endregion

    #region Public
    [Header("Status")]
    public bool joinedChannel;
    public bool previewing;
    public bool pubAudio;
    public bool subAudio;
    public bool pubVideo;
    public bool subVideo = true;
    public IRtcEngine mRtcEngine { get; set; }
    #endregion

    #region MonoBehaviour
    private void Start()
    {
        if (!CheckAppId())
        {
            Debug.Log("Your app id is empty.");
            return;
        }

        if(videoDropdown != null)
        {
            videoDropdown.onValueChanged.RemoveAllListeners();
            videoDropdown.onValueChanged.AddListener((option) => OnVideoDeviceUpdate(option)); 
        }

        if (recordingDropdown != null)
        {
            recordingDropdown.onValueChanged.RemoveAllListeners();
            recordingDropdown.onValueChanged.AddListener((option) => OnRecordingDeviceUpdate(option));
        }

        if (playbackDropdown != null)
        {
            playbackDropdown.onValueChanged.RemoveAllListeners();
            playbackDropdown.onValueChanged.AddListener((option) => OnPlaybackDeviceUpdate(option));
        }

        joinedChannel = false;

        LoadEngine(appId);

        EventHandler.ClientSpawnSuccessEvent += EventHandler_ClientSpawnSuccessEvent;
        EventHandler.ClientDisconnectedEvent += EventHandler_ClientDisconnectedEvent;
    }

    private bool CheckAppId()
    {
        if (appInfo.appID.Length > 10)
        {
            appId = appInfo.appID;
            return true;
        }

        return appId.Length > 10;
    }
    private void OnDestroy()
    {
        LeaveChannel();
        UnloadEngine();

        EventHandler.ClientSpawnSuccessEvent -= EventHandler_ClientSpawnSuccessEvent;
        EventHandler.ClientDisconnectedEvent -= EventHandler_ClientDisconnectedEvent;
    }
    protected override void OnApplicationQuit()
    {
        base.OnApplicationQuit();
        LeaveChannel();
        UnloadEngine();

        EventHandler.ClientSpawnSuccessEvent -= EventHandler_ClientSpawnSuccessEvent;
        EventHandler.ClientDisconnectedEvent -= EventHandler_ClientDisconnectedEvent;
    }
    private void Update()
    {
        PermissionHelper.RequestMicrophontPermission();
        PermissionHelper.RequestCameraPermission();

        //if (!joinedChannel) return;

        List<MediaDeviceInfo> videoDevices = AgoraWebGLEventHandler.GetCachedCameras();

        int recordingDevices = audioRecordingDeviceDict.Count;
        int playbackDevices = audioPlaybackDeviceDict.Count;

        List<string> videoDeviceLabels = new List<string>();

        if (videoDevices.Count > 0)
        {
            if(videoDeviceManagerNameDict.Count != videoDevices.Count)
            {
                GetVideoDeviceManager();
            }

            foreach (MediaDeviceInfo info in videoDevices)
            {
                bool hasLabel = false;
                foreach (Dropdown.OptionData data in videoDropdown.options)
                {
                    if (data.text == info.label)
                    {
                        hasLabel = true;
                    }
                }
                
                if (!hasLabel)
                {
                    videoDeviceLabels.Add(info.label);
                }
            }

            if (videoDropdown.options.Count == 0)
            {
                videoDropdown.AddOptions(videoDeviceLabels);
            }
        }

        videoDropdown.interactable = videoDevices.Count > 0;
        recordingDropdown.interactable = recordingDevices > 0;
        playbackDropdown.interactable = playbackDevices > 0;
    }
    #endregion

    #region Load, Unload Engine
    private void LoadEngine(string appId)
    {
        Debug.Log("initializeEngine");

        if(mRtcEngine == null)
        {
            mRtcEngine = IRtcEngine.GetEngine(appId);
            mRtcEngine.SetLogFilter(LOG_FILTER.DEBUG | LOG_FILTER.INFO | LOG_FILTER.WARNING | LOG_FILTER.ERROR | LOG_FILTER.CRITICAL);
        }

        mRtcEngine.OnJoinChannelSuccess = onJoinChannelSuccess;
        mRtcEngine.OnUserJoined += onUserJoined;
        mRtcEngine.OnUserOffline += onUserOffline;

        mRtcEngine.OnLeaveChannel += OnLeaveChannelHandler;

        mRtcEngine.OnRemoteVideoStateChanged += handleOnUserEnableVideo;

        mRtcEngine.OnCameraChanged += OnCameraChangedHandler;
        mRtcEngine.OnMicrophoneChanged += OnMicrophoneChangedHandler;
        mRtcEngine.OnPlaybackChanged += OnPlaybackChangedHandler;

        mRtcEngine.OnScreenShareStarted += screenShareStartedHandler;
        mRtcEngine.OnScreenShareStopped += screenShareStoppedHandler;

        mRtcEngine.OnVideoSizeChanged += OnVideoSizeChangedHandler;
    }

    private void UnloadEngine()
    {
        if(mRtcEngine != null)
        {
            IRtcEngine.Destroy();
            mRtcEngine = null;
        }
    }
    #endregion

    #region Join, Leave Channel
    public void JoinChannel(string _channelName, uint _uid)
    {
        Debug.Log("Calling join (channel = " + _channelName + ")");

        this.uid = _uid;

        //Cache devices
        videoDropdown.value = 0;
        recordingDropdown.value = 0;
        playbackDropdown.value = 0;

        cacheRecordingDevices();
        cachePlaybackDevices();
        cacheVideoDevices();

        //Config video
        var _orientationMode = ORIENTATION_MODE.ORIENTATION_MODE_FIXED_LANDSCAPE;

        VideoEncoderConfiguration config = new VideoEncoderConfiguration
        {
            orientationMode = _orientationMode,
            degradationPreference = DEGRADATION_PREFERENCE.MAINTAIN_FRAMERATE,
            mirrorMode = VIDEO_MIRROR_MODE_TYPE.VIDEO_MIRROR_MODE_DISABLED
        };

        mRtcEngine.SetVideoEncoderConfiguration(config);

        //Engine Setup
        mRtcEngine.EnableVideo();
        mRtcEngine.EnableVideoObserver();

        ChannelName = _channelName;

        if (previewing)
        {
            ReleaseVideoDevice();
            previewing = false;
        }

        Invoke(nameof(StartJoiningChannel), 1f);
    }

    private void StartJoiningChannel()
    {
        ChannelMediaOptions options = new ChannelMediaOptions()
        {
            autoSubscribeAudio = subAudio,
            autoSubscribeVideo = subVideo,
            publishLocalAudio = pubAudio,
            publishLocalVideo = pubVideo,
        };
        mRtcEngine.JoinChannel("", ChannelName, "", this.uid, options);
    }

    public void LeaveChannel()
    {
        if(mRtcEngine == null) return;

        recordingDropdown.ClearOptions();
        audioRecordingDeviceDict.Clear();
        audioRecordingDeviceNameDict.Clear();

        playbackDropdown.ClearOptions();
        audioPlaybackDeviceDict.Clear();
        audioPlaybackDeviceNameDict.Clear();

        videoDropdown.ClearOptions();   
        videoDeviceManagerDict.Clear(); 
        videoDeviceManagerNameDict.Clear();

        DestroyVideoView(0);

        foreach (KeyValuePair<uint, GameObject> views in videoViews)
        {
            Destroy(views.Value);
        }

        videoViews.Clear();

        mRtcEngine.LeaveChannel();
        mRtcEngine.DisableVideoObserver();
        joinedChannel = false;
    }
    #endregion

    #region Callback Handler
    private void EventHandler_ClientSpawnSuccessEvent(string channelName, uint uid)
    {
        if (joinedChannel) return;
        JoinChannel(channelName, uid);
    }
    private void EventHandler_ClientDisconnectedEvent()
    {
        //Question : when ever video is mute, Still camera/ camera light is active????
        //Answer : when you mute the video track, it's still capturing the video stream data, which is why the light on the camera stays on.
        //If you want to disable both the video track and the camera, you need to use enableLocalVideo from clientManager.js instead.
        // follow this issues https://github.com/AgoraIO-Community/Agora_Unity_WebGL/issues/285

        LeaveChannel();
        UnloadEngine();
    }
    private void onJoinChannelSuccess(string channelName, uint uid, int elapsed)
    {
        Debug.Log($"OnJoinChannelSuccess: {uid}, channel:{channelName}");

        joinedChannel = true;
        
        mRtcEngine.EnableAudioVolumeIndication(1000, 3);

        MakeVideoView(channelName, 0);

        //Setup Chat canvas
        var chatCanvas = FindObjectOfType<ChatCanvas>();

        if(chatCanvas != null)
        {
            chatCanvas.OnJoinChat();
            chatCanvas.OnChatReady(true);
        }

        //Some how the engine need to set public option to true before join the channel.

        //Let user mute themselves.

        mRtcEngine.MuteLocalVideoStream(true);
        mRtcEngine.MuteLocalAudioStream(true);
    }
    private void OnLeaveChannelHandler(RtcStats stats)
    {
        Debug.Log($"OnLeaveChannel: {stats}");

        var chatCanvas = FindObjectOfType<ChatCanvas>();

        if (chatCanvas != null)
        {
            chatCanvas.OnLeaveChat();
        }
    }
    private void onUserOffline(uint uid, USER_OFFLINE_REASON reason)
    {
        DestroyVideoView(uid);

        uint firstUid = GetPrefix(uid);

        if (firstUid == UID_PREFIX)
        {
            Debug.Log($"OnUserOffline: {uid}, with reason: {reason}");
        }
        else if(firstUid == SCREEN_PREFIX)
        {
            Debug.Log($"OnUserStopShareScreen: screen {uid} is removed");

            EventHandler.OnUserShareScreenStopped(uid);
        }
        else
        {
            Debug.Log($"User with {uid} ,the uid is not valid. whether not from user or screen id.");
        }
    }
    private void onUserJoined(uint uid, int elapsed)
    {
        MakeVideoView(ChannelName, uid);

        uint firstUid = GetPrefix(uid);

        if(firstUid == UID_PREFIX)
        {
            Debug.Log($"OnUserJoined: new user {uid}");
        }
        else if(firstUid == SCREEN_PREFIX)
        {
            Debug.Log($"OnUserShareScreen: new screen {uid}");

            EventHandler.OnUserShareScreenStarted(uid);
        }
        else
        {
            Debug.Log($"UserJoined with {uid} ,the uid is not valid. whether not from user or screen id.");
        }

        mRtcEngine.GetRemoteVideoStats();
    }

    private void OnPlaybackChangedHandler(string state, string device)
    {
        GetAudioPlaybackDevice();
    }
    private void OnMicrophoneChangedHandler(string state, string device)
    {
        GetAudioRecordingDevice();
    }
    private void OnCameraChangedHandler(string state, string device)
    {
        GetVideoDeviceManager();
    }
    private void handleOnUserEnableVideo(uint uid, REMOTE_VIDEO_STATE state, REMOTE_VIDEO_STATE_REASON reason, int elapsed)
    {
        if (state == REMOTE_VIDEO_STATE.REMOTE_VIDEO_STATE_STARTING)
        {
            if (!videoViews.ContainsKey(uid))
            {
                Debug.Log($"[handleOnUserEnableVideo] user {uid} video view not found, creating new one.");
                MakeVideoView(ChannelName, uid);
            }
        }
    }
    private void screenShareStoppedHandler(string channelName, uint id, int elapsed)
    {
        Debug.Log(string.Format("onScreenShareStarted channelId: {0}, uid: {1}, elapsed: {2}", channelName, id,
            elapsed));
        //this send to local only
        EventHandler.OnUserShareScreenStopped(0);
    }
    private void screenShareStartedHandler(string channelName, uint id, int elapsed)
    {
        Debug.Log(string.Format("onScreenShareStopped channelId: {0}, uid: {1}, elapsed: {2}", channelName, id,
    elapsed));
        //this send to local only
        EventHandler.OnUserShareScreenStarted(0);
    }
    #endregion

    #region Tools
    public GameObject RetriveVideoView(uint viewId)
    {
        return videoViews[viewId];
    }
    public uint GetUserUID(uint screenId)
    {
        var screenIdString = screenId.ToString();
        var userid = uint.Parse(UID_PREFIX.ToString() + screenIdString.Substring(1, screenIdString.Length - 1));
        return userid;
    }
    public uint GetScreenID(uint userId)
    {
        //remove user prefix then add new prefix
        var userIdString = userId.ToString();
        var screenId = uint.Parse(SCREEN_PREFIX.ToString() + userIdString.Substring(1, userIdString.Length - 1));
        return screenId;
    }
    private uint GetPrefix(uint uid)
    {
        return uint.Parse($"{uid.ToString()[0]}");
    }
    public bool IsUserView(uint uid)
    {
        return GetPrefix(uid) == UID_PREFIX || uid == 0;
    }
    #endregion

    #region Voice Video Controller
    public void OnRecordingDeviceUpdate(int micIndex)
    {
        recordingDeviceIndex = micIndex;
        SetAndReleaseRecordingDevice(micIndex);
    }
    private void SetAndReleaseRecordingDevice(int deviceIndex = 0)
    {
        audioRecordingDeviceManager.SetAudioRecordingDevice(audioRecordingDeviceDict[deviceIndex]);
        audioRecordingDeviceManager.ReleaseAAudioRecordingDeviceManager();
    }
    public void OnPlaybackDeviceUpdate(int speakerIndex)
    {
        playbackDeviceIndex = speakerIndex;
        SetAndReleasePlaybackDevice(speakerIndex);
    }
    private void SetAndReleasePlaybackDevice(int deviceIndex = 0)
    {
        audioPlaybackDeviceManager.SetAudioPlaybackDevice(audioRecordingDeviceDict[deviceIndex]);
        audioPlaybackDeviceManager.ReleaseAAudioPlaybackDeviceManager();
    }
    public void OnVideoDeviceUpdate(int cameraId)
    {
        videoDeviceIndex = cameraId;
        SetVideoDevice(cameraId);
    }
    private void SetVideoDevice(int cameraId = 0)
    {
        if(cameraId < videoDeviceManagerDict.Count)
        {
            videoDeviceManager.SetVideoDevice(videoDeviceManagerDict[cameraId]);
        }
    }
    private void ReleaseVideoDevice()
    {
        if(videoDeviceManager != null)
        {
            videoDeviceManager.ReleaseAVideoDeviceManager();
        }
    }
    public void OnMic(bool toggle)
    {
        if (!joinedChannel) return;
        mRtcEngine.MuteLocalAudioStream(!toggle);

        EventHandler.OnUserMicMuteUpdate(0, !toggle);
    }
    public void OnVideo(bool toggle)
    {
        if (!joinedChannel) return;
        mRtcEngine.MuteLocalVideoStream(!toggle);

        EventHandler.OnUserCamMuteUpdate(0, !toggle);
    }
    public void StartPreview()
    {
        previewing = true;
        mRtcEngine.StartPreview();
        Invoke(nameof(SetVideoDevice), 3f);
    }
    public void StopPreview()
    {
        previewing = false;
        mRtcEngine.StopPreview();
        ReleaseVideoDevice();
    }
    public void OnMuteRemoteVideo(bool mute)
    {
        mRtcEngine.MuteAllRemoteVideoStreams(mute);
    }
    public void OnMuteRemoteAudio(bool mute)
    {
        mRtcEngine.MuteAllRemoteAudioStreams(mute);
    }

    #endregion

    #region SharingScreen
    public void OnShareScreen(bool toggle, bool audioEnabled)
    {
        if (!joinedChannel) return;

#if UNITY_WEBGL
        if (toggle)
        {
            if (isSharing) return;
            updateScreenShareID();
            mRtcEngine.StartNewScreenCaptureForWeb(this.screenId, audioEnabled);
            isSharing = true;
        }
        else
        {
            if (!isSharing) return;
            mRtcEngine.StopNewScreenCaptureForWeb();
            isSharing = false;
        }
#endif
    }

    public void updateScreenShareID()
    {
        this.screenId = GetScreenID(this.uid);
    }
    #endregion

    #region Video View
    private void MakeVideoView(string channalId, uint uid)
    {
        string objName = channalId + "_" + uid.ToString();

        GameObject videoFrame;

        if (uid == 0)
        {
            videoFrame = Instantiate(userVideoViewPrefab, selfViewContainer);
            videoFrame.transform.localScale = new Vector3(2, 2, 1);
        }
        else
        {
            videoFrame = Instantiate(userVideoViewPrefab, videoViewContainer);
        }

        VideoSurface videoSurface = MakeImageSurface(objName, videoFrame.transform);

        videoSurface.SetForUser(uid);
        videoSurface.SetEnable(true);
        videoSurface.SetVideoSurfaceType(AgoraVideoSurfaceType.RawImage);

        videoViews[uid] = videoFrame;

        videoFrame.GetComponent<UserVideoView>().RelocateOverlay();
        videoFrame.GetComponent<UserVideoView>().AssignUid(uid);

        EventHandler.OnCheckDeviceStatus(uid);

        if (!IsUserView(uid))
        {
            videoFrame.transform.SetAsFirstSibling();
        }

    }
    private VideoSurface MakeImageSurface(string goName, Transform parent)
    {
        GameObject go = new GameObject();

        go.name = goName;
        go.AddComponent<RawImage>();
        go.transform.SetParent(parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.Rotate(0f, 0f, 180f);
        go.transform.localScale = Vector3.one;

        if (!go.TryGetComponent(out AspectRatioFitter aspect))
        {
            aspect = go.AddComponent<AspectRatioFitter>();
        }

        aspect.aspectMode = AspectMode.FitInParent;
        aspect.aspectRatio = 16f / 9f;

        VideoSurface videoSurface = go.AddComponent<VideoSurface>();

        return videoSurface;
    }
    private void DestroyVideoView(uint _uid)
    {
        if (videoViews.ContainsKey(_uid))
        {
            var view = videoViews[_uid];
            videoViews.Remove(_uid);
            Destroy(view);
        }
    }

    float EnforcingViewLength = 360f;
    private void OnVideoSizeChangedHandler(uint uid, int width, int height, int rotation)
    {
        Debug.Log(string.Format("OnVideoSizeChangedHandler, uid:{0}, width:{1}, height:{2}, rotation:{3}", uid, width, height, rotation));

        if(!IsUserView(uid))
        {
            //update aspect ratio for big screen
            var aspectRatio = (float)width / height;
            EventHandler.OnScreenResolutionUpdate(uid, aspectRatio);
        }

        if (videoViews.ContainsKey(uid))
        {
            GameObject go = videoViews[uid].gameObject.GetComponentInChildren<VideoSurface>().gameObject;

            Vector2 v2 = new Vector2(width, height);
            RawImage rawImage = go.GetComponent<RawImage>();

            v2 = AgoraUIUtils.GetScaledDimension(width, height, EnforcingViewLength);

            if (rotation == 90 || rotation == 270)
            {
                v2 = new Vector2(v2.y, v2.x);
            }

            rawImage.rectTransform.sizeDelta = v2;

            // if (0,0) we will get a default dimension. but let's still check for the actual dimension
            if (width == 0 && height == 0)
            {
                go.GetComponent<MonoBehaviour>().StartCoroutine(CoGetVideoResolutionDelayed(1));
            }
        }
    }
    private IEnumerator CoGetVideoResolutionDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        mRtcEngine.GetRemoteVideoStats();
    }
    #endregion

    #region Devices
    public void cacheVideoDevices()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        mRtcEngine.CacheVideoDevices();
        pubVideo = true;
        Invoke("GetVideoDeviceManager", .2f);
#endif
    }
    public void cacheRecordingDevices()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        mRtcEngine.CacheRecordingDevices();
        pubAudio = true;
        Invoke("GetAudioRecordingDevice", .2f);
#endif
    }
    public void cachePlaybackDevices()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        mRtcEngine.CachePlaybackDevices();
        subAudio = true;
        Invoke("GetAudioPlaybackDevice", .2f);
#endif
    }

    private void GetVideoDeviceManager()
    {
        string videoDeviceName = "";
        string videoDeviceId = "";

        mRtcEngine.StartPreview();

        videoDeviceManager = (VideoDeviceManager)mRtcEngine.GetVideoDeviceManager();
        videoDeviceManager.CreateAVideoDeviceManager();

        int count = videoDeviceManager.GetVideoDeviceCount();

        videoDropdown.ClearOptions();
        videoDeviceManagerDict.Clear();
        videoDeviceManagerNameDict.Clear();

        for (int i = 0; i < count; i++)
        {
            videoDeviceManager.GetVideoDevice(i, ref videoDeviceName, ref videoDeviceId);

            if (!videoDeviceManagerDict.ContainsKey(i))
            {
                Debug.Log(videoDeviceName);
                Debug.Log(videoDeviceId);
                videoDeviceManagerDict.Add(i, videoDeviceId);
                videoDeviceManagerNameDict.Add(i, videoDeviceName);
            }
        }

        videoDropdown.AddOptions(videoDeviceManagerNameDict.Values.ToList());
        if (videoDeviceManagerNameDict.Count > 0)
        {
            //videoDropdown.value = 0;
            OnVideoDeviceUpdate(videoDropdown.value);
        }
    }
    private void GetAudioRecordingDevice()
    {
        string audioRecordingDeviceName = "";
        string audioRecordingDeviceId = "";
        
        audioRecordingDeviceManager = (AudioRecordingDeviceManager)mRtcEngine.GetAudioRecordingDeviceManager();
        audioRecordingDeviceManager.CreateAAudioRecordingDeviceManager();
        
        int count = audioRecordingDeviceManager.GetAudioRecordingDeviceCount();
        recordingDropdown.ClearOptions();
        audioRecordingDeviceDict.Clear();
        audioRecordingDeviceNameDict.Clear();

        for (int i = 0; i < count; i++)
        {
            audioRecordingDeviceManager.GetAudioRecordingDevice(i, ref audioRecordingDeviceName, ref audioRecordingDeviceId);
            if (!audioRecordingDeviceDict.ContainsKey(i))
            {
                audioRecordingDeviceDict.Add(i, audioRecordingDeviceId);
                audioRecordingDeviceNameDict.Add(i, audioRecordingDeviceName);
            }
        }

        recordingDropdown.AddOptions(audioRecordingDeviceNameDict.Values.ToList());
        if(audioRecordingDeviceNameDict.Count > 0)
        {
            //recordingDropdown.value = 0;
            OnRecordingDeviceUpdate(recordingDropdown.value);
        }
    }
    private void GetAudioPlaybackDevice()
    {
        string audioPlaybackDeviceName = "";
        string audioPlaybackDeviceId = "";

        audioPlaybackDeviceManager = (AudioPlaybackDeviceManager)mRtcEngine.GetAudioPlaybackDeviceManager();
        audioPlaybackDeviceManager.CreateAAudioPlaybackDeviceManager();

        int count = audioPlaybackDeviceManager.GetAudioPlaybackDeviceCount();
        playbackDropdown.ClearOptions();
        
        audioPlaybackDeviceDict.Clear();
        audioPlaybackDeviceNameDict.Clear();

        for (int i = 0; i < count; i++)
        {
            audioPlaybackDeviceManager.GetAudioPlaybackDevice(i, ref audioPlaybackDeviceName, ref audioPlaybackDeviceId);
            if (!audioPlaybackDeviceDict.ContainsKey(i))
            {
                audioPlaybackDeviceDict.Add(i, audioPlaybackDeviceId);
                audioPlaybackDeviceNameDict.Add(i, audioPlaybackDeviceName);
            }
        }

        playbackDropdown.AddOptions(audioPlaybackDeviceNameDict.Values.ToList());

        if( audioPlaybackDeviceNameDict.Count > 0)
        {
            //playbackDropdown.value = 0;
            OnPlaybackDeviceUpdate(playbackDropdown.value);
        }
    }
    #endregion
}
