using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MANGOsFramework.Experiment
{
    public class AvatarIcon : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        [Header("Setup")]
        [SerializeField] private Button buttonRef;
        [SerializeField] private RawImage avatarImage;
        [SerializeField] private Image outline;

        private string gltfLink;

        public string GLTFLink => gltfLink;
        public Texture AvatarTexture => avatarImage != null ? avatarImage.texture : null;

        public void OnSelect(BaseEventData eventData)
        {
            if (outline != null)
            {
                outline.gameObject.SetActive(true);
            }
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (outline != null)
            {
                outline.gameObject.SetActive(false);
            }
        }

        public void OnClick_Icon()
        {
            if (AvatarSystem.Instance != null)
            {
                AvatarSystem.Instance.OnClick_AvatarIcon(gltfLink);
            }
        }

        public void SetIconData(string avatarUrl, AvatarThumbnailService thumbnailService)
        {
            gltfLink = MangosApiClient.ResolveAssetUrl(
                avatarUrl,
                AuthConfig.DefaultApiOrigin);

            if (buttonRef != null)
            {
                buttonRef.interactable = false;
            }

            if (thumbnailService == null)
            {
                return;
            }

            thumbnailService.RequestThumbnail(gltfLink, HandleThumbnailReady);
        }

        private void HandleThumbnailReady(Texture2D texture)
        {
            if (this == null)
            {
                return;
            }

            if (texture != null && avatarImage != null)
            {
                avatarImage.texture = texture;
                avatarImage.color = Color.white;
            }

            if (buttonRef != null)
            {
                buttonRef.interactable = texture != null;
            }

            if (texture != null &&
                UserReferencePersistent.Instance != null &&
                UserReferencePersistent.Instance.GLTF == gltfLink)
            {
                UserReferencePersistent.Instance.SetAvatarImage(texture);
            }
        }
    }
}
