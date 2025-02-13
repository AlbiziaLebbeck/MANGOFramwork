using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AvatarIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Setup")]
    [SerializeField] private float lerpTime = 0.5f;
    [SerializeField] private RectTransform avatarFrame;
    [SerializeField] private GameObject loadingEffect;
    [SerializeField] private Button buttonReference;
    [SerializeField] private RawImage avatarImage;
    public bool IsFirstSelect;

    [Header("Debug Purpose")]
    [SerializeField] private Camera screenshotCameraReference;
    [SerializeField] private GameObject avatarModel;
    [SerializeField] private string gltfLink;
    [SerializeField] private bool wasDisable = false;

    private RenderTexture rt;
    private Vector3 offset = new Vector3(0, 0.1f, 2f);

    //private float reloadingTime = 5f;
    public string GLTFLink { get => gltfLink; }
    public Texture AvatarTexture { get; private set; }
    public GameObject AvatarModel { get => avatarModel; private set { avatarModel = value; } }

    private void OnEnable()
    {
        AvatarLoaderEvent.AvatarLoadedEvent += OnAvatarLoaded;
    }

    private void OnDisable()
    {
        AvatarLoaderEvent.AvatarLoadedEvent -= OnAvatarLoaded;
    }

    private void OnAvatarLoaded(GameObject _avatarModel, string _url)
    {
        if (_url == gltfLink)
        {
            AvatarModel = _avatarModel.transform.parent.gameObject;

            StartCoroutine(GenerateAvatarImage(_avatarModel));
        }
    }

    public void OnClick_Icon()
    {
        if(avatarModel != null)
        {
            AvatarCanvasEvent.OnClickedAvatarIcon(this);
        }
    }

    public void SetIconData(string _gltfLink)
    {
        gltfLink = _gltfLink;
    }

    public void SetActiveIcon(bool _active)
    {
        buttonReference.enabled = _active;
        loadingEffect.SetActive(!_active);
        wasDisable = !_active;
    }

    private IEnumerator GenerateAvatarImage(GameObject model)
    {
        rt = new RenderTexture(AvatarImageGenerator.TEXTURE_WIDTH, AvatarImageGenerator.TEXTURE_HEIGHT, 16, RenderTextureFormat.ARGB32);

        rt.Create();

        var newCam = new GameObject();
        newCam.name = "ScreenShotCamera";

        SetCameraToHead(model, newCam);

        newCam.transform.localPosition += offset;
        newCam.transform.localEulerAngles = new Vector3(0, 180, 0);
        newCam.AddComponent<Camera>();

        screenshotCameraReference =  newCam.GetComponent<Camera>();

        screenshotCameraReference.fieldOfView = 10f;
        screenshotCameraReference.farClipPlane = 3f;
        screenshotCameraReference.clearFlags = CameraClearFlags.SolidColor;
        screenshotCameraReference.backgroundColor = Color.white;
        screenshotCameraReference.targetTexture = rt;

        var genImage = AvatarImageGenerator.TakeScreenshot(screenshotCameraReference);
        avatarImage.texture = genImage;
        AvatarTexture = genImage;

        yield return new WaitForEndOfFrame();

        rt.Release();

        yield return new WaitForEndOfFrame();

        //Destroy(screenshotCameraReference.gameObject, 2f);
        screenshotCameraReference.gameObject.SetActive(false);
    }

    private void SetCameraToHead(GameObject model, GameObject camera)
    {
        foreach (Transform child in model.transform)
        {
            if (child.name.Contains("head") || child.name.Contains("Head"))
            {
                camera.transform.SetParent(child.transform, false);
                break;
            }
            else
            {
                Transform _HasChildren = child.GetComponentInChildren<Transform>();
                if (_HasChildren != null)
                {
                    SetCameraToHead(child.gameObject, camera);
                }
            }
        }
    }

    #region UI Event
    public void OnPointerEnter(PointerEventData eventData)
    {
        Swooping(true);
        AvatarCanvasEvent.OnIconHoverEnter(this);
        
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        Swooping(false);
        AvatarCanvasEvent.OnIconHoverExit(this);

    }
    private void Swooping(bool up)
    {
        StartCoroutine(DoSwoopCoroutine(up));
    }
    private IEnumerator DoSwoopCoroutine(bool up)
    {
        var t = 0f;

        while (t < 1)
        {
            t += Time.time / lerpTime;

            if (t > 1) t = 1;
            if (up)
            {
                avatarFrame.anchoredPosition = Vector2.Lerp(avatarFrame.anchoredPosition, avatarFrame.anchoredPosition + new Vector2(0f, 1f), t);
            }
            else
            {
                avatarFrame.anchoredPosition = Vector2.Lerp(avatarFrame.anchoredPosition, avatarFrame.anchoredPosition + new Vector2(0f, -1f), t);
            }

            yield return null;
        }
    }
    #endregion

}
