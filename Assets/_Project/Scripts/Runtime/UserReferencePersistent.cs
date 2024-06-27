using UnityEngine;

public class UserReferencePersistent : SingletonPersistent<UserReferencePersistent>
{
    [SerializeField] private string username;
    [SerializeField] private string gltf;
    [SerializeField] private GameObject playerGameObject;
    [SerializeField] private Transform playerCameraRoot;

    public string Username { get { return username; } }
    public string GLTF { get { return gltf; } }
    public GameObject PlayerGameObject { get {  return playerGameObject; } }
    public Transform PlayerCameraRoot { get { return playerCameraRoot; } }

    public void SetUserName(string _name)
    {
        Debug.Log("Set Username to " + _name);
        username = _name;
    }

    public void SetGLTFLink(string _link)
    {
        gltf = _link;
    }

    public void AssignPlayerGameObject(GameObject _gameObject)
    {
        playerGameObject = _gameObject;
        playerCameraRoot = _gameObject.transform.Find("PlayerCameraRoot");
    }
}
