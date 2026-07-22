using MANGOsFramework.Experiment;
using System;
using UnityEngine;

public class UserReferencePersistent : SingletonPersistent<UserReferencePersistent>
{
    [SerializeField] private string username;
    [SerializeField] private string gltf;
    [SerializeField] private int mangosGold;
    [SerializeField] private GameObject playerGameObject;
    [SerializeField] private Transform playerCameraRoot;

    public string Username { get { return username; } }
    public string GLTF { get { return gltf; } }
    public int MangosGold { get { return mangosGold; } }
    public Texture AvatarImage { get; private set; }
    public GameObject PlayerGameObject { get {  return playerGameObject; } }
    public Transform PlayerCameraRoot { get { return playerCameraRoot; } }

    public event Action<int> MangosGoldChanged;

    private void OnEnable()
    {
        AvatarLoaderEvent.AvatarLoadedEvent += AvatarLoaderEvent_AvatarLoadedEvent;

    }
    private void OnDisable()
    {
        AvatarLoaderEvent.AvatarLoadedEvent -= AvatarLoaderEvent_AvatarLoadedEvent;
    }

    private void AvatarLoaderEvent_AvatarLoadedEvent(GameObject _avatarModel, string _url)
    {
        if (!string.IsNullOrEmpty(gltf) && _url == gltf)
        {
            var avatarTexture  = AvatarSystem.Instance.GetAvatarTexture(_url);

            if(avatarTexture != null)
            {
                SetAvatarImage(avatarTexture);
            }
        }
    }

    public void SetUserName(string _name)
    {
        this.username = _name;
        PersistentCanvas.UserDataCanvas.SetUserNameText(username);
    }

    public void SetGLTFLink(string _link)
    {
        this.gltf = _link;
    }

    public void SetMangosGold(int amount)
    {
        mangosGold = amount;
        PersistentCanvas.UserDataCanvas?.SetMangosGold(amount);
        MangosGoldChanged?.Invoke(amount);
    }

    public void SetAvatarImage(Texture newImage)
    {
        this.AvatarImage = newImage;
        PersistentCanvas.UserDataCanvas.SetAvatarImage(AvatarImage);
    }

    public void AssignPlayerGameObject(GameObject _gameObject)
    {
        this.playerGameObject = _gameObject;
        this.playerCameraRoot = _gameObject.transform.Find("PlayerCameraRoot");
    }
}
