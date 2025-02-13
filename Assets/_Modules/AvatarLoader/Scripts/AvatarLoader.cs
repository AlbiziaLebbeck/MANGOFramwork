using GLTFast;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.Events;
using System.Collections.Generic;

public class AvatarLoader : MonoBehaviour
{
    [SerializeField] private GameObject avatarModel;
    [SerializeField] private string gltfLink;
    [SerializeField] private Animator animator;

    public UnityEvent OnLoadCompleted;
    public UnityEvent OnLoadFailed;

    public string GLTFLink { get => gltfLink; set {  gltfLink = value; } }

    public async void LoadAvatar()
    {
        if (avatarModel != null)
        {
            var objectToDestroy = avatarModel.gameObject;

            Destroy(objectToDestroy);

            avatarModel = null;
        }

        if (!string.IsNullOrEmpty(GLTFLink)) await LoadAvatarAsync(GLTFLink);
        else Debug.LogError("GLTF Link is null or empty", this.gameObject);
    }

    private async Task LoadAvatarAsync(string _url)
    {
        var gltf = new GltfImport();
        var success = await gltf.Load(_url);

        if (_url.Contains("default")) success = false;

        if (success)
        {
            var instantiator = new GameObjectInstantiator(gltf, this.transform);

            await gltf.InstantiateMainSceneAsync(instantiator);

            avatarModel = instantiator.SceneTransform.gameObject;
            avatarModel.transform.localEulerAngles = new Vector3(0, 0, 0);

            Destroy(avatarModel.GetComponent<Animation>());

            SetupAnimator(avatarModel);

            Debug.Log("Loading glTF successfully.");

            AvatarLoaderEvent.OnAvatarLoaded(avatarModel, _url);

            if(OnLoadCompleted != null) OnLoadCompleted.Invoke();
        }
        else
        {
            Debug.LogError("Loading glTF failed!");

            avatarModel = Instantiate(Resources.Load<GameObject>("AvatarLoader/BaseAvatar"), this.transform);

            avatarModel.transform.localEulerAngles = new Vector3(0, 180, 0);
            avatarModel.name = avatarModel.name + "-Failed";
            SetupAnimator(avatarModel);

            AvatarLoaderEvent.OnAvatarLoaded(avatarModel, _url);

            AvatarLoaderEvent.OnAvatarLoadFailed(_url);

            if (OnLoadFailed != null) OnLoadFailed.Invoke();
        }
    }

    private Dictionary<string, float> floatParameters;
    private Dictionary<string, bool> boolParameters;
    private Dictionary<string, int> intParameters;

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


        floatParameters = new Dictionary<string, float>();
        boolParameters = new Dictionary<string, bool>();
        intParameters = new Dictionary<string, int>();

        foreach (var parameter in animator.parameters)
        {
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float:
                    floatParameters.Add(parameter.name, animator.GetFloat(parameter.nameHash));
                    break;
                case AnimatorControllerParameterType.Int:
                    intParameters.Add(parameter.name, animator.GetInteger(parameter.nameHash));
                    break;
                case AnimatorControllerParameterType.Bool:
                    boolParameters.Add(parameter.name, animator.GetBool(parameter.nameHash));
                    break;
                default:
                    break;
            }
        }

        if (avatarModel.transform.Find("bone_masque0_root/hips/spine.001") || avatarModel.transform.Find("amature_masque0/hips/spine.001"))
            animator.avatar = Resources.Load<Avatar>("AvatarLoader/MasqueAvatar_CU");
        else
            animator.avatar = Resources.Load<Avatar>("AvatarLoader/BaseAvatar");

        if(animator.runtimeAnimatorController == null)
        {
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("AvatarLoader/AvatarController");
        }



        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(parameter.nameHash, floatParameters[parameter.name]);
                    break;
                case AnimatorControllerParameterType.Int:
                    animator.SetInteger(parameter.nameHash, intParameters[parameter.name]);
                    break;
                case AnimatorControllerParameterType.Bool:
                    animator.SetBool(parameter.nameHash, boolParameters[parameter.name]);
                    break;
                default:
                    break;
            }
        }
    }
}
