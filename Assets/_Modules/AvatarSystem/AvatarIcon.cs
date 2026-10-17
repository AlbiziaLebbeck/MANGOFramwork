using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;

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
        public bool IsSelectable => buttonRef != null && buttonRef.interactable;
        private bool thumbnailRequested;

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
                buttonRef.interactable = !string.IsNullOrWhiteSpace(gltfLink);
            }

            var labelObject = new GameObject("AvatarName", typeof(RectTransform));
            labelObject.transform.SetParent(transform, false);
            var rect = (RectTransform)labelObject.transform;
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0); rect.sizeDelta = new Vector2(-8, 28);
            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = Uri.TryCreate(gltfLink, UriKind.Absolute, out var uri)
                ? Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(uri.AbsolutePath)) : "Avatar";
            label.fontSize = 14; label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.12f, 0.18f, 0.25f); label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            EnsureThumbnail(thumbnailService);
        }

        public void EnsureThumbnail(AvatarThumbnailService service)
        {
            if (thumbnailRequested || service == null || string.IsNullOrWhiteSpace(gltfLink)) return;
            thumbnailRequested = true;
            service.RequestThumbnail(gltfLink, HandleThumbnailReady);
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
                buttonRef.interactable = !string.IsNullOrWhiteSpace(gltfLink);
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
