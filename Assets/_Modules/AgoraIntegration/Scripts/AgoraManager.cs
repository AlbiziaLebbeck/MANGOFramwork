using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using agora_gaming_rtc;
using System.Linq;

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
    //[SerializeField] private Transform shareScreenContainer;

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
    private Dictionary<uint, GameObject> userVideoViews = new Dictionary<uint, GameObject>();
    private GameObject localVideoView;
    private bool isCameraOn;
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

    private void Start()
    {
        if (!CheckAppId())
        {
            Debug.Log("Your app id is empty.");
            return;
        }

        joinedChannel = false;

        LoadEngine(appId);

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
        UnloadEngine();
    }

    protected override void OnApplicationQuit()
    {
        base.OnApplicationQuit();

        UnloadEngine();
    }

    private void Update()
    {
        if (!joinedChannel) return;

        PermissionHelper.RequestMicrophontPermission();
        PermissionHelper.RequestCameraPermission();

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

    #region Load, Unload Engine
    private void LoadEngine(string appId)
    {
        Debug.Log("initializeEngine");
        if(mRtcEngine != null)
        {
            return;
        }

        mRtcEngine = IRtcEngine.GetEngine(appId);
        // enable log
        mRtcEngine.SetLogFilter(LOG_FILTER.DEBUG | LOG_FILTER.INFO | LOG_FILTER.WARNING | LOG_FILTER.ERROR | LOG_FILTER.CRITICAL);

        //Set Callbacks
        mRtcEngine.OnJoinChannelSuccess = onJoinChannelSuccess;
        mRtcEngine.OnLeaveChannel = OnLeaveChannelHandler;

        mRtcEngine.OnUserJoined += onUserJoined;
        mRtcEngine.OnUserOffline += onUserOffline;

        mRtcEngine.OnUserMutedAudio += OnUserMutedAudio;
        mRtcEngine.OnUserMuteVideo += OnUserMutedVideo;

        mRtcEngine.OnRemoteVideoStateChanged += handleOnUserEnableVideo;

        mRtcEngine.OnCameraChanged += OnCameraChangedHandler;
        mRtcEngine.OnMicrophoneChanged += OnMicrophoneChangedHandler;
        mRtcEngine.OnPlaybackChanged += OnPlaybackChangedHandler;

        //mRtcEngine.OnScreenShareStarted += screenShareStartedHandler;
        //mRtcEngine.OnScreenShareStopped += screenShareStoppedHandler;
        //mRtcEngine.OnScreenShareCanceled += screenShareCanceledHandler;
    }

    private void UnloadEngine()
    {
        if(mRtcEngine != null)
        {
            LeaveChannel();
            mRtcEngine.DisableVideo();
            mRtcEngine.DisableVideoObserver();
            IRtcEngine.Destroy();
            mRtcEngine = null;
        }
    }
    #endregion

    #region Join, Leave Channel
    public void JoinChannel(string _channelName, uint _uid)
    {
        Debug.Log("Calling join (channel = " + _channelName + ")");
        if (mRtcEngine == null)
        {
            LoadEngine(appId);
        }

        this.uid = UID_PREFIX + _uid;

        //Cache devices
        
        videoDropdown.value = 0;
        recordingDropdown.value = 0;
        playbackDropdown.value = 0;

        cacheRecordingDevices();
        cachePlaybackDevices();
        cacheVideoDevices();

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

        DestroyVideoView(0);

        mRtcEngine.LeaveChannel();
        mRtcEngine.DisableVideoObserver();
    }
    #endregion

    #region Callback Handler
    private void onJoinChannelSuccess(string channelName, uint uid, int elapsed)
    {
        Debug.Log($"OnJoinChannelSuccess: {uid}, channel:{channelName}");

        //Some how the engine need to set public option to true before join the channel.
        //That's why we mute it after user join.
        mRtcEngine.MuteLocalAudioStream(true);
        mRtcEngine.MuteLocalVideoStream(true);

        joinedChannel = true;
        
        mRtcEngine.EnableAudioVolumeIndication(1000, 3);

        MakeVideoView(channelName, 0);

        var chatCanvas = FindObjectOfType<ChatCanvas>();

        if(chatCanvas != null)
        {
            chatCanvas.OnJoinChat();
            chatCanvas.OnChatReady(true);
        }
    }
    private void OnLeaveChannelHandler(RtcStats stats)
    {
        Debug.Log($"OnLeaveChannel: {stats}");
        joinedChannel = false;
        mRtcEngine.DisableVideo();

        var chatCanvas = FindObjectOfType<ChatCanvas>();

        if (chatCanvas != null)
        {
            chatCanvas.OnLeaveChat();
        }
    }
    private void onUserOffline(uint uid, USER_OFFLINE_REASON reason)
    {
        Debug.Log($"OnUserOffline: {uid}, with reason: {reason}");
        DestroyVideoView(uid);
    }
    private void onUserJoined(uint uid, int elapsed)
    {
        Debug.Log($"OnUserJoined: {uid}");

        MakeVideoView(ChannelName, uid);
        //when user join sometimes they join just audio, so we turn user video view off. 
        if (userVideoViews.ContainsKey(uid))
        {
            userVideoViews[uid].SetActive(false);
        }

        //will implement screen sharing next commit.
        //if (uid != SCREEN_SHARE_ID)
        //{
        //    MakeVideoView(ChannelName, uid);
        //    //when user join sometimes they join just audio, so we turn user video view off. 
        //    if (userVideoViews.ContainsKey(uid))
        //    {
        //        userVideoViews[uid].SetActive(false);
        //    }
        //}
        //else
        //{

        //}
    }
    private void OnUserMutedAudio(uint uid, bool muted)
    {
        Debug.LogFormat("user {0} muted audio:{1}", uid, muted);
    }
    private void OnUserMutedVideo(uint uid, bool muted)
    {
        Debug.LogFormat("user {0} muted video:{1}", uid, muted);

        if(userVideoViews.ContainsKey(uid))
        {
            userVideoViews[uid].SetActive(!muted);
        }
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
        Debug.Log("remote video state:" + state.ToString());
        if (state == REMOTE_VIDEO_STATE.REMOTE_VIDEO_STATE_STARTING)
        {
            if (!userVideoViews.ContainsKey(uid))
            {
                MakeVideoView(ChannelName, uid);
            }
        }
    }
    
    //private void screenShareStoppedHandler(string channelName, uint id, int elapsed)
    //{
    //    Debug.Log(string.Format("onScreenShareStarted channelId: {0}, uid: {1}, elapsed: {2}", channelName, uid,
    //        elapsed));
    //}
    //private void screenShareStartedHandler(string channelName, uint id, int elapsed)
    //{
    //    Debug.Log(string.Format("onScreenShareStopped channelId: {0}, uid: {1}, elapsed: {2}", channelName, uid,
    //elapsed));
    //}
    //private void screenShareCanceledHandler(string channelName, uint uid, int elapsed)
    //{
    //    Debug.Log(string.Format("onScreenShareCanceled channelId: {0}, uid: {1}, elapsed: {2}", channelName, uid,
    //        elapsed));
    //}

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
    }
    public void OnVideo(bool toggle)
    {
        if (!joinedChannel) return;

        mRtcEngine.MuteLocalVideoStream(!toggle);
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

    #region SharingScreen Not Ready
    //Will uncomment next commit.
//    public void OnShareScreen(bool toggle, bool audioEnabled)
//    {
//        if (!joinedChannel) return;

////        if (toggle)
////        {
////#if UNITY_EDITOR
////            mRtcEngine.StartScreenCaptureByDisplayId(0, default, default);
////            mRtcEngine.MuteLocalVideoStream(false);
////#else
////        mRtcEngine.StartScreenCaptureForWeb(audioEnabled);
////#endif
////        }
////        else
////        {
////            mRtcEngine.StopScreenCapture();
////            mRtcEngine.MuteLocalVideoStream(true);
////        }

//        if (toggle)
//        {
//            updateScreenShareID();
//#if UNITY_EDITOR
//            mRtcEngine.StartScreenCaptureByDisplayId(SCREEN_SHARE_ID, default, default);
//#else
//            mRtcEngine.StartNewScreenCaptureForWeb(SCREEN_SHARE_ID, audioEnabled);
//#endif
//        }
//        else
//        {
//            mRtcEngine.StopNewScreenCaptureForWeb();
//        }
//    }

//    public void updateScreenShareID()
//    {
//        uint.TryParse("testScreenShare", out SCREEN_SHARE_ID);
//    }
#endregion

    #region Video View
    private void MakeVideoView(string channalId, uint uid)
    {
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

        string objName = channalId + "_" + uid.ToString();
        GameObject go = GameObject.Find(objName);
        if(!ReferenceEquals(go, null)) return;

        VideoSurface videoSurface = MakeImageSurface(objName, videoFrame.transform);
        if (!ReferenceEquals(videoSurface, null))
        {
            videoSurface.SetForUser(uid);
            videoSurface.SetEnable(true);
            videoSurface.SetVideoSurfaceType(AgoraVideoSurfaceType.RawImage);
            if(uid == 0)
            {
                localVideoView = videoFrame;
            }

            userVideoViews[uid] = videoFrame;
        }
    }
    private VideoSurface MakeImageSurface(string goName, Transform parent)
    {
        GameObject go = new GameObject();
        if(go == null) return null;

        go.name = goName;
        go.AddComponent<RawImage>();
        go.transform.SetParent(parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.Rotate(0f, 0f, 180f);
        go.transform.localScale = new Vector3(1.6f, .9f, 1);

        VideoSurface videoSurface = go.AddComponent<VideoSurface>();

        return videoSurface;
    }
    private void DestroyVideoView(uint _uid)
    {
        if(userVideoViews.ContainsKey(_uid))
        {
            var view = userVideoViews[_uid];
            userVideoViews.Remove(_uid);
            Destroy(view);
        }
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
