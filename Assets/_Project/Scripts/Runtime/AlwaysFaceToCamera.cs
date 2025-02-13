using UnityEngine;

public class AlwaysFaceToCamera : MonoBehaviour
{
    public enum BillboardType { LookAtCamera, CameraForward };

    [SerializeField] private BillboardType billboardType;

    private Transform cam;

    private void Awake()
    {
        if (Camera.main != null) cam = Camera.main.transform;
    }


    private void LateUpdate()
    {
        if (cam != null)
        {
            if (billboardType == BillboardType.LookAtCamera)
            {
                transform.LookAt(cam, Vector3.up);
                transform.Rotate(Vector3.up * 180);
            }
            else if (billboardType == BillboardType.CameraForward)
            {
                transform.forward = cam.transform.forward;
            }
        }
    }
}
