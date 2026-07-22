using System;
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
        private const int CaptureLayer = 31;
        private bool imageGenerationStarted;

        public string GLTFLink { get => gltfLink; }
        public Texture AvatarTexture => avatarImage.texture;
        public event Action<AvatarIcon, Texture> AvatarTextureGenerated;

        private void OnEnable()
        {
            AvatarLoaderEvent.AvatarLoadedEvent += AvatarLoaderEvent_AvatarLoadedEvent;
        }

        private void OnDisable()
        {
            AvatarLoaderEvent.AvatarLoadedEvent -= AvatarLoaderEvent_AvatarLoadedEvent;
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
                GenerateImage(_avatarModel);
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

        public void GenerateFromModel(GameObject model)
        {
            GenerateImage(model);
        }

        private void GenerateImage(GameObject model)
        {
            if (imageGenerationStarted || model == null || avatarImage == null)
            {
                return;
            }

            if (!TryGetRendererBounds(model, out Bounds bounds))
            {
                Debug.LogWarning($"Avatar thumbnail could not be framed because '{gltfLink}' has no renderers.");
                return;
            }

            imageGenerationStarted = true;
            Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
            int[] originalLayers = new int[transforms.Length];
            GameObject cameraObject = null;
            RenderTexture renderTexture = null;

            try
            {
                for (int index = 0; index < transforms.Length; index++)
                {
                    originalLayers[index] = transforms[index].gameObject.layer;
                    transforms[index].gameObject.layer = CaptureLayer;
                }

                renderTexture = new RenderTexture(
                    AvatarImageGenerator.TEXTURE_WIDTH,
                    AvatarImageGenerator.TEXTURE_HEIGHT,
                    16,
                    RenderTextureFormat.ARGB32);
                renderTexture.Create();

                cameraObject = new GameObject("AvatarThumbnailCamera");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                Camera screenshotCamera = cameraObject.AddComponent<Camera>();

                float frameExtent = Mathf.Max(bounds.extents.x, bounds.extents.y);
                float cameraDistance = Mathf.Max(2f, bounds.extents.z + frameExtent + 1f);
                cameraObject.transform.position = bounds.center + Vector3.forward * cameraDistance;
                cameraObject.transform.LookAt(bounds.center, Vector3.up);

                screenshotCamera.orthographic = true;
                screenshotCamera.orthographicSize = Mathf.Max(0.1f, frameExtent * 1.08f);
                screenshotCamera.nearClipPlane = 0.01f;
                screenshotCamera.farClipPlane = cameraDistance + bounds.size.z + 2f;
                screenshotCamera.clearFlags = CameraClearFlags.SolidColor;
                screenshotCamera.backgroundColor = Color.white;
                screenshotCamera.cullingMask = 1 << CaptureLayer;
                screenshotCamera.allowHDR = false;
                screenshotCamera.allowMSAA = false;
                screenshotCamera.targetTexture = renderTexture;

                Texture2D generatedImage = AvatarImageGenerator.TakeScreenshot(screenshotCamera);
                if (generatedImage != null)
                {
                    avatarImage.texture = generatedImage;
                    AvatarTextureGenerated?.Invoke(this, generatedImage);
                }
            }
            catch (Exception exception)
            {
                imageGenerationStarted = false;
                Debug.LogWarning("Avatar thumbnail generation failed: " + exception.Message);
            }
            finally
            {
                for (int index = 0; index < transforms.Length; index++)
                {
                    if (transforms[index] != null)
                    {
                        transforms[index].gameObject.layer = originalLayers[index];
                    }
                }

                if (cameraObject != null)
                {
                    cameraObject.SetActive(false);
                    Destroy(cameraObject);
                }

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    Destroy(renderTexture);
                }
            }
        }

        private static bool TryGetRendererBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                Bounds rendererBounds = renderer.bounds;
                if (!IsFinite(rendererBounds.center) || !IsFinite(rendererBounds.extents))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rendererBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }

            return hasBounds && bounds.size.sqrMagnitude > 0.000001f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

    }
}

