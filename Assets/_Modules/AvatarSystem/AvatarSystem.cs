using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MANGOsFramework.Experiment
{
    // just to be sure that this won't be messed up the old Avatar Manager so I just clone it and modify a bit.
    public class AvatarSystem : Singleton<AvatarSystem>
    {
        [Header("Default Avatar Urls")]
        public List<string> AvatarUrls = new List<string>();

        [Header("References")]
        [SerializeField] private GameObject AvatarSelectPanel;
        [SerializeField] private Button avatarSubmitButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button avatarWindowButton;
        [SerializeField] private Button AvatarButtonPrefab;
        [SerializeField] private Button resetAnimatorButton;
        [SerializeField] private Transform avatarButtonHolder;
        [SerializeField] private Transform cachedAvatarHolder;

        [Header("Setting")]
        private float avatarChangeCooldown = 5f;

        [Header("Debug")]
        [SerializeField] private List<string> userAvatarUrls = new();
        private List<AvatarIcon> avatarButtons = new();
        private LimitedDictionary<string, GameObject> cachedAvatarModel = new(100);
        [SerializeField] private string currentSelectAvatar;

        private bool inCooldown;
        private float elapse;
        private bool isLoaded;
        private bool isPanelOpen = false;
        [SerializeField] private Animator localPlayerAnim;
        private AvatarThumbnailService thumbnailService;

        protected override void Awake()
        {
            base.Awake();

            thumbnailService = GetComponent<AvatarThumbnailService>();
            if (thumbnailService == null)
            {
                thumbnailService = gameObject.AddComponent<AvatarThumbnailService>();
            }

            if (avatarSubmitButton != null)
            {
                avatarSubmitButton.onClick.AddListener(() =>
                {
                    OnClick_SubmitAvatar(currentSelectAvatar);
                    avatarSubmitButton.interactable = false;
                    elapse = 0;
                    inCooldown = true;
                });
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(() =>
                {
                    OnClick_Cancel();
                    cancelButton.interactable = false;
                });
            }

            if(avatarWindowButton != null)
            {
                avatarWindowButton.gameObject.SetActive(false);

                avatarWindowButton.onClick.AddListener(() =>
                {
                    OnToggleAvatarWindow();
                });
            }

            if(resetAnimatorButton != null)
            {
                resetAnimatorButton.onClick.AddListener(() =>
                {
                    if(localPlayerAnim == null)
                    {
                        localPlayerAnim = UserReferencePersistent.Instance.PlayerGameObject.GetComponent<Animator>();
                    }

                    StartCoroutine(RestartAnimator(localPlayerAnim));
                });
            }
        }

        private IEnumerator RestartAnimator(Animator anim)
        {
            anim.Rebind();
            anim.Update(0);
            yield return new WaitForEndOfFrame();
        }

        private void OnEnable()
        {
            AvatarLoaderEvent.AvatarLoadedEvent += OnAvatarLoaded;
            AvatarLoaderEvent.AvatarLoadFailedEvent += AvatarLoaderEvent_AvatarLoadFailedEvent;
            EventHandler.LocalClientCompleteSetupEvent += EventHandler_LocalClientCompleteSetupEvent;
        }

        Dictionary<string, int> attemptDownloadAfterFailed = new Dictionary<string, int>();

        private void AvatarLoaderEvent_AvatarLoadFailedEvent(AvatarLoader loader, string url)
        {
            //just took out the url that broke

            Debug.Log($"Avatar Load failed from {url}");

            if (attemptDownloadAfterFailed.ContainsKey(url))
            {
                var attampt = attemptDownloadAfterFailed[url];
                Debug.Log($"try load again: attemp {attampt}");

                if(attampt > 2)
                {
                    Debug.Log($"Attemp max stop loading, temporary remove url from system. load again.");

                    attemptDownloadAfterFailed.Remove(url);

                    var buttonToRemove = avatarButtons.Find(button => button.GLTFLink == url);

                    if(buttonToRemove != null)
                    {
                        avatarButtons.Remove(buttonToRemove);
                        Destroy(buttonToRemove.gameObject);
                    }

                    if (AvatarUrls.Contains(url))
                    {
                        AvatarUrls.Remove(url);
                    }

                    if (userAvatarUrls.Contains(url))
                    {
                        userAvatarUrls.Remove(url);
                    }

                    return;
                }

                attampt++;
                attemptDownloadAfterFailed[url] = attampt;
                loader.LoadAvatar();
            }
            else
            {
                attemptDownloadAfterFailed[url] = 1;
                Debug.Log($"try load again: attemp {1}");
                loader.LoadAvatar();
            }
        }

        private void OnDisable()
        {
            AvatarLoaderEvent.AvatarLoadedEvent -= OnAvatarLoaded;
            AvatarLoaderEvent.AvatarLoadFailedEvent -= AvatarLoaderEvent_AvatarLoadFailedEvent;
            EventHandler.LocalClientCompleteSetupEvent -= EventHandler_LocalClientCompleteSetupEvent;
        }

        private void EventHandler_LocalClientCompleteSetupEvent()
        {
            // just simply showing the button. probably there's something more to setup
            StartCoroutine(Init());
        }

        private IEnumerator Init()
        {
            yield return StartCoroutine(LoadAvatarSelection());

            string activeAvatarUrl = NormalizeAvatarUrl(UserReferencePersistent.Instance.GLTF);
            var matchIcon = avatarButtons.Find(icon => icon.GLTFLink == activeAvatarUrl);

            if (matchIcon != null)
            {
                if (matchIcon.TryGetComponent(out AvatarIcon icon))
                {
                    UserReferencePersistent.Instance.SetAvatarImage(icon.AvatarTexture);
                }
            }

            if (avatarWindowButton != null)
            {
                avatarWindowButton.gameObject.SetActive(true);
            }

            isLoaded = true;
        }

        private void OnAvatarLoaded(GameObject _modelToCache, string _url)
        {
            string normalizedUrl = NormalizeAvatarUrl(_url);
            if (!string.IsNullOrWhiteSpace(normalizedUrl) && !cachedAvatarModel.ContainsKey(normalizedUrl))
            {
                var clonedAvatar = Instantiate(_modelToCache, cachedAvatarHolder);
                cachedAvatarModel.Add(normalizedUrl, clonedAvatar);
            }
        }

        private void Update()
        {
            if (inCooldown)
            {
                elapse += Time.deltaTime;

                if (elapse >= avatarChangeCooldown)
                {
                    inCooldown = false;
                    elapse = 0;

                    avatarSubmitButton.interactable = true;
                }
            }
        }

        public bool IsAvatarCached(string _url)
        {
            string normalizedUrl = NormalizeAvatarUrl(_url);
            return !string.IsNullOrWhiteSpace(normalizedUrl) && cachedAvatarModel.ContainsKey(normalizedUrl);
        }

        public Texture GetAvatarTexture(string _url)
        {
            string normalizedUrl = NormalizeAvatarUrl(_url);
            var matchIcon = avatarButtons.Find(icon => icon.GLTFLink == normalizedUrl);

            if (matchIcon != null)
            {
                if (matchIcon.TryGetComponent(out AvatarIcon icon))
                {
                    return icon.AvatarTexture;
                }
            }

            return null;
        }

        public GameObject LoadAvatarFromCached(string _url)
        {
            string normalizedUrl = NormalizeAvatarUrl(_url);
            if(!string.IsNullOrWhiteSpace(normalizedUrl) &&
               cachedAvatarModel.TryGetValue(normalizedUrl, out GameObject loadedAvatar))
            {
                return loadedAvatar;
            }
            else
            {
                return null;
            }
        }

        public IEnumerator LoadAvatarSelection(Action onComplete = null)
        {
            var avatarUrlSnapshot = new List<string>(AvatarUrls.Count + userAvatarUrls.Count);
            AddUniqueAvatarUrls(avatarUrlSnapshot, AvatarUrls);
            AddUniqueAvatarUrls(avatarUrlSnapshot, userAvatarUrls);

            for (int i = 0; i < avatarUrlSnapshot.Count; i++)
            {
                string url = NormalizeAvatarUrl(avatarUrlSnapshot[i]);
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (avatarButtons.Exists(icon => icon != null && icon.GLTFLink == url))
                {
                    continue;
                }

                var newButton = Instantiate(AvatarButtonPrefab, avatarButtonHolder);
                var icon = newButton.GetComponent<AvatarIcon>();
                icon.SetIconData(url, thumbnailService);
                avatarButtons.Add(icon);

                yield return null;
            }

            if (thumbnailService != null)
            {
                yield return thumbnailService.WaitForIdle();
            }

            onComplete?.Invoke();
        }

        private static void AddUniqueAvatarUrls(List<string> destination, List<string> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                string url = source[i];
                if (!string.IsNullOrWhiteSpace(url) && !destination.Contains(url))
                {
                    destination.Add(url);
                }
            }
        }

        public void AddNewUserAvatar(string avatarUrl)
        {
            string normalizedUrl = NormalizeAvatarUrl(avatarUrl);
            if (!string.IsNullOrWhiteSpace(normalizedUrl) && !userAvatarUrls.Contains(normalizedUrl))
            {
                userAvatarUrls.Add(normalizedUrl);
            }
        }

        public void OnClick_SubmitAvatar(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            if (UserReferencePersistent.Instance != null)
            {
                if (UserReferencePersistent.Instance.PlayerGameObject.TryGetComponent(out NetworkedPlayerComponent netCom))
                {
                    netCom.RPCServerSetAvatar(url);
                }
            }

            //OnToggleAvatarWindow();
        }

        public void OnToggleAvatarWindow()
        {
            if (!isLoaded) return;

            var popInEffect = AvatarSelectPanel.GetComponent<UIPopIn>();

            if(isPanelOpen)
            {
                isPanelOpen = false;

                if (popInEffect != null)
                {
                    popInEffect.ClosePanel();
                }
                else
                {
                    AvatarSelectPanel.gameObject.SetActive(false);
                }

                currentSelectAvatar = null;
            }
            else
            {
                isPanelOpen = true;

                if (popInEffect != null)
                {
                    popInEffect.OpenPanel();
                }
                else
                {
                    AvatarSelectPanel.gameObject.SetActive(true);
                }

                cancelButton.interactable = true;
            }
        }

        public void OnClick_AvatarIcon(string url)
        {
            currentSelectAvatar = NormalizeAvatarUrl(url);
        }

        private static string NormalizeAvatarUrl(string url)
        {
            return MangosApiClient.ResolveAssetUrl(url, AuthConfig.DefaultApiOrigin);
        }

        public void OnClick_Cancel()
        {
            currentSelectAvatar = null;

            if (AvatarSelectPanel.TryGetComponent(out UIPopIn popInEffect))
            {
                popInEffect.ClosePanel();
            }
            else
            {
                AvatarSelectPanel.gameObject.SetActive(false);
            }

            isPanelOpen = false;
        }
    }
}

