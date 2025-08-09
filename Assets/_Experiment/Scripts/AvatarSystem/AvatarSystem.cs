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
        [SerializeField] private Transform avatarButtonHolder;
        [SerializeField] private Button AvatarButtonPrefab;
        [SerializeField] private Transform cachedAvatarHolder;
        [SerializeField] private Transform avatarCollectionsTransform;
        [SerializeField] private Button resetAnimatorButton;

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

        protected override void Awake()
        {
            base.Awake();

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

            yield return new WaitUntil(() => cachedAvatarModel.Count == AvatarUrls.Count + userAvatarUrls.Count);

            Destroy(avatarCollectionsTransform.gameObject, 15);

            var matchIcon = avatarButtons.Find(icon => icon.GLTFLink == UserReferencePersistent.Instance.GLTF);

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

            PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
        }

        private void OnAvatarLoaded(GameObject _modelToCache, string _url)
        {
            if (!cachedAvatarModel.ContainsKey(_url))
            {
                var clonedAvatar = Instantiate(_modelToCache, cachedAvatarHolder);
                cachedAvatarModel.Add(_url, clonedAvatar);
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
            return cachedAvatarModel.ContainsKey(_url);
        }

        public Texture GetAvatarTexture(string _url)
        {
            var matchIcon = avatarButtons.Find(icon => icon.GLTFLink == _url);

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
            if(cachedAvatarModel.TryGetValue(_url, out GameObject loadedAvatar))
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
            var totalAvatarCount = AvatarUrls.Count + userAvatarUrls.Count;
            if (totalAvatarCount != avatarButtons.Count)
            {
                for (int i = 0; i < totalAvatarCount; i++)
                {
                    var newButton = Instantiate(AvatarButtonPrefab, avatarButtonHolder);

                    var icon = newButton.GetComponent<AvatarIcon>();

                    string url = i >= AvatarUrls.Count ? userAvatarUrls[i - AvatarUrls.Count] : AvatarUrls[i];

                    icon.SetIconData(url);

                    int count = i;
                    
                    LoadAvatar(url, count);
                   
                    avatarButtons.Add(icon);

                    yield return null;
                }
            }


            if (onComplete != null) onComplete();
        }

        private void LoadAvatar(string url, int avatarCount)
        {
            var cloneAvatar = new GameObject();
            cloneAvatar.transform.SetParent(avatarCollectionsTransform, false);
            cloneAvatar.name = $"AvatarHolder{avatarCount}";
            cloneAvatar.transform.localPosition = new Vector3(2 * avatarCount, 0f, 0f);

            var loader = cloneAvatar.AddComponent<AvatarLoader>();

            loader.GLTFLink = url;
            loader.LoadAvatar();
        }

        public void AddNewUserAvatar(string avatarUrl)
        {
            userAvatarUrls.Add(avatarUrl);
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
            currentSelectAvatar = url;
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

