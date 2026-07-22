using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MANGOsFramework.Experiment
{
    public class AvatarIcon : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        [Header("Setup")]
        [SerializeField] private RectTransform avatarFrame;
        [SerializeField] private Button buttonRef;
        [SerializeField] private RawImage avatarImage;
        [SerializeField] private Image outline;

        private string gltfLink;
        private RenderTexture rt;
        private Vector3 offset = new Vector3(0, 0.1f, 2f);

        public string GLTFLink { get => gltfLink; }
        public Texture AvatarTexture => avatarImage.texture;

        private void Start()
        {
            AvatarLoaderEvent.AvatarLoadedEvent += AvatarLoaderEvent_AvatarLoadedEvent;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if(outline != null)
            {
                outline.gameObject.gameObject.SetActive(true);
            }
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (outline != null)
            {
                outline.gameObject.gameObject.SetActive(false);
            }
        }

        private void AvatarLoaderEvent_AvatarLoadedEvent(GameObject _avatarModel, string _url)
        {
            if(_url == gltfLink)
            {
                StartCoroutine(GenerateImage(_avatarModel));
            }
        }

        public void OnClick_Icon()
        {
            AvatarSystem.Instance.OnClick_AvatarIcon(gltfLink);
        }

        public void SetIconData(string _gltfLink)
        {
            gltfLink = _gltfLink;
        }

        private IEnumerator GenerateImage(GameObject model)
        {
            rt = new RenderTexture(AvatarImageGenerator.TEXTURE_WIDTH, AvatarImageGenerator.TEXTURE_HEIGHT, 16, RenderTextureFormat.ARGB32);

            rt.Create();

            var newCam = new GameObject();
            newCam.name = "ScreenShotCamera";

            SetCameraToHead(model, newCam);

            newCam.transform.localPosition += offset;
            newCam.transform.localEulerAngles = new Vector3(0, 180, 0);
            newCam.AddComponent<Camera>();

            var screenshotCameraReference = newCam.GetComponent<Camera>();

            screenshotCameraReference.fieldOfView = 10f;
            screenshotCameraReference.farClipPlane = 3f;
            screenshotCameraReference.clearFlags = CameraClearFlags.SolidColor;
            screenshotCameraReference.backgroundColor = Color.white;
            screenshotCameraReference.targetTexture = rt;

            var genImage = AvatarImageGenerator.TakeScreenshot(screenshotCameraReference);

            avatarImage.texture = genImage;

            yield return new WaitForEndOfFrame();

            rt.Release();

            yield return new WaitForEndOfFrame();

            screenshotCameraReference.gameObject.SetActive(false);

            AvatarLoaderEvent.AvatarLoadedEvent -= AvatarLoaderEvent_AvatarLoadedEvent;
        }

        private void SetCameraToHead(GameObject model, GameObject camera)
        {
            foreach (Transform child in model.transform)
            {
                if (child.name.Contains("head") || child.name.Contains("Head") && child.GetComponent<SkinnedMeshRenderer>() == null)
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


    }
}

