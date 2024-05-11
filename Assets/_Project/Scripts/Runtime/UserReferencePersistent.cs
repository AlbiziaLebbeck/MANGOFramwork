using UnityEngine;

public class UserReferencePersistent : Singleton<UserReferencePersistent>
{
    [SerializeField] private string username;
    [SerializeField] private GameObject playerGameObject;
    [SerializeField] private Transform playerCameraRoot;

    public string Username { get { return username; } }
    public GameObject PlayerGameObject { get {  return playerGameObject; } }
    public Transform PlayerCameraRoot { get { return playerCameraRoot; } }
 
    public void SetUserName(string _name)
    {
        username = _name;
    }

    public void AssignPlayerGameObject(GameObject _gameObject)
    {
        playerGameObject = _gameObject;
        playerCameraRoot = _gameObject.transform.Find("PlayerCameraRoot");
    }
}
