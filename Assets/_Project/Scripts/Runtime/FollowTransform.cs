using UnityEngine;

public class FollowTransform : MonoBehaviour
{
    [SerializeField] private Transform _transform;
    [SerializeField] private Transform fallbackTransform;
    [SerializeField] private Transform searchTarget;
    [SerializeField] private float smooth = 1f;
    [SerializeField] private Vector3 offset;

    private void Update()
    {
        if (_transform == null) return;
        transform.position = Vector3.Lerp(transform.position, _transform.position, smooth * Time.deltaTime);
    }

    public void AssignTransform(Transform target)
    {
        if (target == null) return;
        _transform = target;
    }

    public void SearchForHeadTransform()
    {
        SetTargetToHead(searchTarget.gameObject);
    }

    private void SetTargetToHead(GameObject model)
    {
        foreach (Transform child in model.transform)
        {
            if (child.name.Contains("head") || child.name.Contains("Head"))
            {
                _transform = child;
                break;
            }
            else
            {
                Transform _HasChildren = child.GetComponentInChildren<Transform>();
                if (_HasChildren != null)
                {
                    SetTargetToHead(child.gameObject);
                }
                else
                {
                    _transform = fallbackTransform;
                }
            }
        }
    }
}
