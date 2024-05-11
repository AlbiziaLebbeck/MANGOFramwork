using UnityEngine;

public class FollowTransform : MonoBehaviour
{
    [SerializeField] private Transform _transform;

    [SerializeField] private float smooth = 1f;

    private void Update()
    {
        transform.position = Vector3.Lerp(transform.position, _transform.position, smooth * Time.deltaTime);
    }
}
