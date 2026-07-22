using GLTFast;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.Events;
using System.Collections.Generic;
using MANGOsFramework.Experiment;
using System.Collections;

public class AvatarLoader : MonoBehaviour
{
    [SerializeField] private GameObject avatarModel;
    [SerializeField] private string gltfLink;
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject basicAvatar;

    public UnityEvent OnLoadCompleted;
    public UnityEvent OnLoadFailed;

    public string GLTFLink { get => gltfLink; set { gltfLink = value; } }
    public GameObject Model => avatarModel;
    public Animator Animator => animator;

    public bool LoadOnStart;

    private void Start()
    {
        if (LoadOnStart)
        {
            LoadAvatar();
        }
    }
    public async void LoadAvatar()
    {
        string resolvedAvatarUrl = MangosApiClient.ResolveAssetUrl(
            GLTFLink,
            AuthConfig.DefaultApiOrigin);
        if (string.IsNullOrWhiteSpace(resolvedAvatarUrl))
        {
            Debug.LogError("Avatar URL is empty or uses an unsupported scheme.", this.gameObject);
            if (basicAvatar != null)
            {
                basicAvatar.SetActive(true);
            }

            OnLoadFailed?.Invoke();
            return;
        }

        GLTFLink = resolvedAvatarUrl;

        if (basicAvatar != null)
        {
            basicAvatar.SetActive(false);
        }

        if (avatarModel != null)
        {
            var objectToDestroy = avatarModel.gameObject;
            Destroy(objectToDestroy);

            avatarModel = null;
        }

        if (!string.IsNullOrEmpty(GLTFLink)) await LoadAvatarAsync(GLTFLink);
        else Debug.LogError("GLTF Link is null or empty", this.gameObject);
    }

    public void LoadAvatarFromCache()
    {
        var model = AvatarSystem.Instance.LoadAvatarFromCached(this.gltfLink);

        if (model == null)
        {
            LoadAvatar();
            return;
        }

        if (avatarModel != null)
        {
            var objectToDestroy = avatarModel.gameObject;
            Destroy(objectToDestroy);

            avatarModel = null;
        }
        avatarModel = Instantiate(model, this.transform);
        avatarModel.transform.localPosition = Vector3.zero;
        avatarModel.transform.localEulerAngles = new Vector3(0, 0, 0);

        if(avatarModel.TryGetComponent(out Animation animation))
        {
            Destroy(animation);
        }

        SetupAnimator(avatarModel);
        Debug.Log("Loading glTF successfully.");

        AvatarLoaderEvent.OnAvatarLoaded(avatarModel, this.gltfLink);
        if (OnLoadCompleted != null) OnLoadCompleted.Invoke();
    }

    private async Task LoadAvatarAsync(string _url)
    {
        var gltf = new GltfImport();
        var loadSuccess = await gltf.Load(_url);

        if (_url.Contains("default")) loadSuccess = false;

        if (loadSuccess)
        {
            var newModel = new GameObject("AvatarModel");

            newModel.transform.SetParent(this.transform);
            newModel.transform.localPosition = Vector3.zero;

            var instantiateSuccess = await gltf.InstantiateMainSceneAsync(newModel.transform);

            if (instantiateSuccess)
            {
                avatarModel = newModel;

                avatarModel.transform.localEulerAngles = new Vector3(0, 0, 0);

                if (avatarModel.TryGetComponent(out Animation animation))
                {
                    Destroy(animation);
                }

                SetupAnimator(avatarModel);

                Debug.Log("Loading glTF successfully.");

                AvatarLoaderEvent.OnAvatarLoaded(avatarModel, _url);

                if (OnLoadCompleted != null) OnLoadCompleted.Invoke();
            }
        }
        else
        {
            Debug.LogError("Loading glTF failed!");
            AvatarLoaderEvent.OnAvatarLoadFailed(this, _url);
            if (OnLoadFailed != null) OnLoadFailed.Invoke();
        }
    }

    private void SetupAnimator(GameObject avatarModel)
    {
        Animator existAnimator;

        if (avatarModel.TryGetComponent(out existAnimator))
        {
            if (animator != null)
            {
                Destroy(existAnimator);
            }
            else
            {
                animator = existAnimator;
            }
        }

        if (animator == null) animator = avatarModel.AddComponent<Animator>();

        var targetAvatar = "";
        var targetController = "";

        if (avatarModel.transform.Find("Scene/bone_masque0_root/hips/spine.001") || avatarModel.transform.Find("Scene/amature_masque0/hips/spine.001"))
        {
            targetAvatar = "AvatarLoader/MasqueAvatar_CU";
        }
        else if (avatarModel.transform.Find("text_for_unity/Armature/Hips/Spine"))
        {
            targetAvatar = "AvatarLoader/MANGOsAvatar";
        }
        else if (avatarModel.transform.Find("Armature/avaturn_body"))
        {
            targetAvatar = "AvatarLoader/AvaternRig";
        }
        else if (
            (avatarModel.transform.Find("Cloth.001") && avatarModel.transform.Find("avaturn_body.001")) ||
            (avatarModel.transform.Find("Scene/Cloth.001") && avatarModel.transform.Find("Scene/avaturn_body.001")))
        {
            targetAvatar = "AvatarLoader/full_derssAvatar";
        }
        else if (avatarModel.transform.Find("Armature/Wolf3D_Head"))
        {
            targetAvatar = "AvatarLoader/ReadyPlayerMeRig";
        }
        else if (avatarModel.transform.Find("Armature/Outfit"))
        {
            targetAvatar = "AvatarLoader/RajPatternAvatar";
        }
        else if (avatarModel.transform.Find("Armature/Sphere"))
        {
            targetAvatar = "AvatarLoader/FruitAvatar";
        }
        else if (avatarModel.transform.Find("Armature/Hips/UpperLeg L"))
        {
            targetAvatar = "AvatarLoader/ShortBone";
        }
        else if (avatarModel.transform.Find("Armature/Hips/LeftUpLeg"))
        {
            targetAvatar = "AvatarLoader/MonkAvatar";
        }
        else if (avatarModel.transform.Find("Scene/Root/J_Bip_C_Hips/J_Bip_C_Spine"))
        {
            targetAvatar = "AvatarLoader/VrmAvatar";
        }
        else if (avatarModel.transform.Find("Armature/PHHips/UpperLeg L"))
        {
            targetAvatar = "AvatarLoader/PHAvatar";
        }
        else
        {
            targetAvatar = "AvatarLoader/ArmatureAvatar";
        }

        targetController = "AvatarLoader/AvatarController";

        if (setupAnimatorRoutine != null)
        {
            StopCoroutine(setupAnimatorRoutine);
            setupAnimatorRoutine = null;
        }

        StartCoroutine(SetupAnimatorCoroutine(animator, targetAvatar, targetController));
    }

    private Coroutine setupAnimatorRoutine;

    private readonly Dictionary<string, float> floatParameters = new();
    private readonly Dictionary<string, int> intParameters = new();
    private readonly Dictionary<string, bool> boolParameters = new();

    private IEnumerator SetupAnimatorCoroutine(Animator _animator, string _avatarPath, string _controllerPath)
    {
        if (_animator == null)
        {
            Debug.LogError("Animator is null in SetupAnimatorCoroutine");
            yield break;
        }

        floatParameters.Clear();
        intParameters.Clear();
        boolParameters.Clear();

        foreach (var p in _animator.parameters)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:
                    floatParameters[p.name] = _animator.GetFloat(p.nameHash);
                    break;
                case AnimatorControllerParameterType.Int:
                    intParameters[p.name] = _animator.GetInteger(p.nameHash); 
                    break;
                case AnimatorControllerParameterType.Bool:
                    boolParameters[p.name] = _animator.GetBool(p.nameHash); 
                    break;
            }
        }

        var newAvatar = Resources.Load<Avatar>(_avatarPath);
        if (newAvatar != null)
        {
            _animator.avatar = newAvatar;
        }
        else
        {
            Debug.LogWarning($"Avatar not found at {_avatarPath}");
        }

        var newController = animator.runtimeAnimatorController;

        if (newController != null)
        {
            _animator.runtimeAnimatorController = newController;
        }
        else
        {
            newController = Resources.Load<RuntimeAnimatorController>(_controllerPath);
            _animator.runtimeAnimatorController = newController;
        }

        yield return null;
        yield return null;

        if (_animator.runtimeAnimatorController == null)
        {
            Debug.LogError("Animator controller is null after assignment and wait.");
            yield break;
        }

        foreach (var p in _animator.parameters)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:
                    if(floatParameters.TryGetValue(p.name, out var fVal))
                        _animator.SetFloat(p.nameHash, fVal);
                    break;
                case AnimatorControllerParameterType.Int:
                    if(intParameters.TryGetValue(p.name, out var iVal))
                        _animator.SetInteger(p.nameHash, iVal);
                    break;
                case AnimatorControllerParameterType.Bool:
                    if(boolParameters.TryGetValue(p.name, out var bVal))
                        _animator.SetBool(p.nameHash, bVal);
                    break;
            }
        }

        setupAnimatorRoutine = null;
    }

    public void AssignAnimatorController(RuntimeAnimatorController controller)
    {
        if (animator != null)
        {
            animator.runtimeAnimatorController = controller;
        }
    }
}
