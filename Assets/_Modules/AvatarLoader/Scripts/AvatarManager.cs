using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class AvatarManager : Singleton<AvatarManager>
{
    [Header("Avatar Urls")]
    public List<string> AvatarUrls = new List<string>();

    [Header("Settings")]
    [SerializeField] private Transform avatarsShowRoomTransform;
    [SerializeField] private Transform avatarCollectionsTransform;
    [SerializeField] private GameObject baseAvatarPrefab;
    [SerializeField] private AvatarCanvas avatarCanvas;

    [Header("Events")]
    public UnityEvent<string> OnSelectedAvatarChange;
    public UnityEvent<Texture> ReturnAvatarTexture;

    [Header("Debug Purpose Only")]
    [SerializeField] private List<GameObject> AvatarCollection = new List<GameObject>();
    [SerializeField] private List<GameObject> AvatarShowroom = new List<GameObject>();
    [SerializeField] private GameObject selectedModel;

    private int showRoomAvatarCount = 0;
    private int collectionAvatarCount = 0;
    private bool _subscribed;

    protected override void Awake()
    {
        base.Awake();

        if(avatarCanvas == null) avatarCanvas = FindObjectOfType<AvatarCanvas>();
    }

    private void Start()
    {
        if(avatarCanvas == null)
        {
            Debug.Log("The login page must start with Avatar Canvas. Please make sure Avatar Canvas is in this scene.");
            return;
        }

        avatarCanvas.Initialize();

        //Set default avatar
        UserReferencePersistent.Instance.SetGLTFLink(AvatarUrls[0]);
    }

    private void OnEnable()
    {
        AvatarCanvasEventSubscribe(true);

        AvatarLoaderEvent.AvatarLoadedEvent += AvatarLoaderEvent_AvatarLoadedEvent;
    }

    private void OnDisable()
    {
        AvatarCanvasEventSubscribe(false);

        AvatarLoaderEvent.AvatarLoadedEvent -= AvatarLoaderEvent_AvatarLoadedEvent;
    }

    private void AddToCollections(AvatarIcon avatarIcon)
    {
        var cloneAvatar = new GameObject();
        cloneAvatar.transform.SetParent(avatarCollectionsTransform, false);
        cloneAvatar.name = $"AvatarHolder{collectionAvatarCount}";
        cloneAvatar.transform.localPosition = new Vector3(2 * collectionAvatarCount, 0f, 0f);

        var loader = cloneAvatar.AddComponent<AvatarLoader>();

        if (CheckIconIsValid(avatarIcon))
        {
            loader.GLTFLink = avatarIcon.GLTFLink;
            loader.LoadAvatar();
        }

        AvatarCollection.Add(cloneAvatar);

        collectionAvatarCount++;
    }

    private bool CheckIconIsValid(AvatarIcon icon)
    {
        bool valid = true;

        if (string.IsNullOrEmpty(icon.GLTFLink))
        {
            valid = false;
            Debug.Log("GLTF Link is empty");
        }
        else
        {
            if (icon.GLTFLink.Contains("Default"))
            {
                valid = false;
                Debug.Log("This icon is for default avatar");
            }
        }

        return valid;
    }

    private void AvatarLoaderEvent_AvatarLoadedEvent(GameObject loadedAvatar, string url)
    {
        AddToShowRoom(loadedAvatar);

        if (showRoomAvatarCount == AvatarUrls.Count)
        {
            PersistentCanvas.LoadingCanvas?.ToggleLoadingScreen(false);

            selectedModel = AvatarCollection[0];
            HighlightAvatar(selectedModel);
        }
    }

    private void AddToShowRoom(GameObject loadedAvatar)
    {
        var cloneToShowRoom = new GameObject();

        cloneToShowRoom.transform.SetParent(avatarsShowRoomTransform, false);
        cloneToShowRoom.name = loadedAvatar.transform.parent.name;
        cloneToShowRoom.transform.localPosition = Vector3.zero;
        cloneToShowRoom.AddComponent<DragRotator>();
        cloneToShowRoom.GetComponent<DragRotator>().SetRotateTarget(cloneToShowRoom.transform);

        Instantiate(loadedAvatar, cloneToShowRoom.transform);

        AvatarShowroom.Add(cloneToShowRoom);
        showRoomAvatarCount++;

        cloneToShowRoom.SetActive(false);
    }

    #region Highlight avatar
    private GameObject previousHighlightModel;

    private void HighlightAvatar(GameObject avatarModel)
    {
        if (previousHighlightModel != null) previousHighlightModel.SetActive(false);

        int avatarIndex = AvatarShowroom.FindIndex(avatar => avatar.name == avatarModel.name);

        if(avatarIndex != -1)
        {
            AvatarShowroom[avatarIndex].gameObject.SetActive(true);
            previousHighlightModel = AvatarShowroom[avatarIndex];
        }
    }
    #endregion

    #region AvatarCanvasEvent
    private void AvatarCanvasEventSubscribe(bool subscribe)
    {
        if (subscribe == _subscribed) return;

        _subscribed = subscribe;
        
        if (subscribe)
        {
            AvatarCanvasEvent.HoverExitIconEvent += OnHoverExitIcon;
            AvatarCanvasEvent.HoverEnterIconEvent += OnHoverEnterIcon;
            AvatarCanvasEvent.OnClickAvatarIconEvent += OnClickIcon;
            AvatarCanvasEvent.AvatarIconSpawnedEvent += OnIconSpawn;
        }
        else
        {
            AvatarCanvasEvent.HoverExitIconEvent -= OnHoverExitIcon;
            AvatarCanvasEvent.HoverEnterIconEvent -= OnHoverEnterIcon;
            AvatarCanvasEvent.OnClickAvatarIconEvent -= OnClickIcon;
            AvatarCanvasEvent.AvatarIconSpawnedEvent -= OnIconSpawn;
        }
    }
    private void OnHoverExitIcon(AvatarIcon icon)
    {
        if (selectedModel == null) return;

        if (icon.AvatarModel != selectedModel)
        {
            HighlightAvatar(selectedModel);
        }
    }
    private void OnHoverEnterIcon(AvatarIcon icon)
    {
        HighlightAvatar(icon.AvatarModel);
    }
    private void OnClickIcon(AvatarIcon icon)
    {
        selectedModel = icon.AvatarModel;
        HighlightAvatar(selectedModel);

        if (OnSelectedAvatarChange != null) OnSelectedAvatarChange.Invoke(icon.GLTFLink);
        if (ReturnAvatarTexture != null) ReturnAvatarTexture.Invoke(icon.AvatarTexture);
    }
    private void OnIconSpawn(AvatarIcon icon)
    {
        AddToCollections(icon);
    }
    #endregion
}
