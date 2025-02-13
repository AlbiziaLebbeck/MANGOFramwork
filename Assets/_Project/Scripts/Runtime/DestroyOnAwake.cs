using UnityEngine;

public class DestroyOnAwake : MonoBehaviour
{
    private void Awake() => Destroy(this.gameObject);

}
