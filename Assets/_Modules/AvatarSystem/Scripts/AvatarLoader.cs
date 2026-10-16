using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using MANGOsFramework.Experiment;

public class AvatarLoader : MonoBehaviour
{
    [SerializeField] private GameObject avatarModel;
    [SerializeField] private string gltfLink;
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject basicAvatar;
    [SerializeField] private AvatarRigProfile rigProfile;
    [SerializeField] private bool normalizeHeight = true;
    [SerializeField, Min(0.2f)] private float targetHeight = 1.7f;
    public UnityEvent OnLoadCompleted;
    public UnityEvent OnLoadFailed;
    public bool LoadOnStart;
    public string GLTFLink { get => gltfLink; set => gltfLink = value; }
    public GameObject Model => avatarModel;
    public Animator Animator => animator;
    public string LastLoadError { get; private set; }
    private GameObject owner;
    private CancellationTokenSource cancellation;
    private int generation;

    private void Awake() => owner = animator != null ? animator.gameObject : gameObject;
    private void Start() { if (LoadOnStart) LoadAvatar(); }
    public void LoadAvatarFromCache() => LoadAvatar();
    public async void LoadAvatar()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        int request = ++generation;
        GameObject candidate = null;
        var url = MangosApiClient.ResolveAssetUrl(GLTFLink, AuthConfig.DefaultApiOrigin);
        try
        {
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Avatar URL is empty or unsupported.");
            gltfLink = url;
            candidate = await AvatarModelImporter.Load(url, token);
            token.ThrowIfCancellationRequested();
            if (request != generation || this == null) return;
            foreach (var legacy in candidate.GetComponentsInChildren<Animation>(true)) legacy.enabled = false;
            var next = candidate.GetComponent<Animator>();
            if (next == null) next = candidate.AddComponent<Animator>();
            if (next.avatar == null || !next.avatar.isValid || !next.avatar.isHuman)
            {
                var profile = rigProfile;
                if (profile == null)
                    foreach (var configured in Resources.LoadAll<AvatarRigProfile>("AvatarRigProfiles"))
                        if (!string.IsNullOrWhiteSpace(configured.urlPrefix) && url.StartsWith(configured.urlPrefix, StringComparison.OrdinalIgnoreCase)) { profile = configured; break; }
                var ownAvatar = HumanoidRigBuilder.Build(candidate, profile);
                var lifetime = candidate.GetComponent<AvatarModelLifetime>();
                if (lifetime == null) lifetime = candidate.AddComponent<AvatarModelLifetime>();
                lifetime.GeneratedAvatar = ownAvatar;
                next.avatar = ownAvatar;
            }
            var previous = avatarModel;
            candidate.name = "AvatarModel";
            candidate.transform.SetParent(transform, false);
            candidate.transform.localPosition = Vector3.zero;
            candidate.transform.localRotation = Quaternion.identity;
            if (owner == null) owner = gameObject;
            var runtime = owner.GetComponent<MangosAvatarRuntime>();
            if (runtime == null) runtime = owner.AddComponent<MangosAvatarRuntime>();
            if (MangosAvatarRuntime.ResolveAnimator(owner)?.runtimeAnimatorController == null)
                next.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("AvatarLoader/AvatarController");
            runtime.Bind(next);
            FitVisual(candidate);
            candidate.AddComponent<AvatarAnimationEvents>().Owner = owner;
            avatarModel = candidate;
            animator = next;
            candidate = null;
            if (basicAvatar != null) basicAvatar.SetActive(false);
            if (previous != null && previous != avatarModel) Destroy(previous);
            LastLoadError = null;
            AvatarLoaderEvent.OnAvatarLoaded(avatarModel, url);
            OnLoadCompleted?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request != generation || this == null) return;
            LastLoadError = error.Message;
            Debug.LogWarning("Avatar rejected: " + error.Message, this);
            if (avatarModel == null && basicAvatar != null) basicAvatar.SetActive(true);
            OnLoadFailed?.Invoke();
            // Invalid rigs need a mapping/source fix; retrying the same file cannot repair them.
        }
        finally { if (candidate != null) Destroy(candidate); }
    }
    private void FitVisual(GameObject model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        if (normalizeHeight && bounds.size.y > 0.001f)
        {
            model.transform.localScale *= targetHeight / bounds.size.y;
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        }
        model.transform.position += Vector3.up * (transform.position.y - bounds.min.y);
        var height = normalizeHeight ? targetHeight : bounds.size.y;
        var cameraTarget = owner.transform.Find("PlayerCameraRoot");
        if (cameraTarget != null) { var position = cameraTarget.localPosition; position.y = height * 0.9f; cameraTarget.localPosition = position; }
        var headPanel = owner.transform.Find("HeadPanel");
        if (headPanel != null) { var position = headPanel.localPosition; position.y = height + 0.2f; headPanel.localPosition = position; }
    }
    public void AssignAnimatorController(RuntimeAnimatorController controller)
    {
        if (animator != null) animator.runtimeAnimatorController = controller;
    }
    private void OnDestroy() { generation++; cancellation?.Cancel(); cancellation?.Dispose(); }
}
