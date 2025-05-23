using UnityEngine;

public class FollowTransform : MonoBehaviour
{
    [SerializeField] private Transform _transform;

    [SerializeField] private float smooth = 1f;

    private void Update()
    {
        transform.position = Vector3.Lerp(transform.position, _transform.position + new Vector3(0, 0.45f, 0), smooth * Time.deltaTime);
    }
}
