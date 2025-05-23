using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AvatarCanvas : MonoBehaviour
{
    [SerializeField] private AvatarManager avatarManager;
    [SerializeField] private GameObject avatarIconPrefab;
    public float scale = 1f;
    [SerializeField] private Transform avatarIconContainer;
    private List<GameObject> AvatarIcons = new List<GameObject>();

    private bool isInitialized = false;

    public void Initialize()
    {
        if (isInitialized) return;

        StartCoroutine(LoadAvatarIcon());
    }

    private void OnEnable()
    {
        AvatarLoaderEvent.AvatarLoadedEvent += AvatarLoaderEvent_AvatarLoadedEvent;
    }

    private void AvatarLoaderEvent_AvatarLoadedEvent(GameObject arg1, string arg2)
    {
        var firstAvatar = AvatarIcons[0].GetComponent<AvatarIcon>();

        if(arg2 == firstAvatar.GLTFLink)
        {
            Debug.Log("Click first icon");
            firstAvatar.OnClick_Icon();
        }

        AvatarLoaderEvent.AvatarLoadedEvent -= AvatarLoaderEvent_AvatarLoadedEvent;
    }



    private IEnumerator LoadAvatarIcon()
    {
        for (int i = 0; i < avatarManager.AvatarUrls.Count; i++)
        {
            GameObject newIcon = Instantiate(avatarIconPrefab, avatarIconContainer);

            AvatarIcons.Add(newIcon);

            newIcon.GetComponent<AvatarIcon>().SetIconData(avatarManager.AvatarUrls[i]);

            AvatarCanvasEvent.OnAvatarIconSpawned(newIcon.GetComponent<AvatarIcon>());

            yield return new WaitForEndOfFrame();
        }

        isInitialized = true;

        yield return new WaitForEndOfFrame();
    }
}
