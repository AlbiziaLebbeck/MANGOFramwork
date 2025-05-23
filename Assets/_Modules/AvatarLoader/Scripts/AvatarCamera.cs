using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class AvatarCamera : MonoBehaviour
{
    private RenderTexture rt;
    private Camera avatarCamera;
    private RawImage avatarImage;
    private Vector3 offset = new Vector3(0.063f, 0.064f, 1.842f);

    [SerializeField] private bool setOnAwake;
    [SerializeField] private GameObject defaultModel;

    private void Awake()
    {
        if (avatarImage == null) avatarImage = GetComponent<RawImage>();

        if (setOnAwake) StartCoroutine(SetupCamera(defaultModel));
    }

    public IEnumerator SetupCamera(GameObject _model)
    {
        rt = new RenderTexture(AvatarImageGenerator.TEXTURE_WIDTH, AvatarImageGenerator.TEXTURE_HEIGHT, 16, RenderTextureFormat.ARGB32);

        rt.Create();

        var newCam = new GameObject();
        newCam.name = "AvatarCamera";

        SetCameraToHead(_model, newCam);

        newCam.transform.localPosition += offset;

        newCam.transform.localEulerAngles = new Vector3(0, 180, 0);
        newCam.AddComponent<Camera>();
        avatarCamera = newCam.GetComponent<Camera>();
        avatarCamera.fieldOfView = 10f;
        avatarCamera.farClipPlane = 3f;
        avatarCamera.clearFlags = CameraClearFlags.SolidColor;
        avatarCamera.backgroundColor = Color.white;
        avatarCamera.targetTexture = rt;
        avatarImage.texture = rt;

        // Release the hardware resources used by the render texture 
        rt.Release();
        
        yield return null;
    }

    private void SetCameraToHead(GameObject model, GameObject camera)
    {
        foreach (Transform child in model.transform)
        {
            if (child.name.Contains("head") || child.name.Contains("Head"))
            {
                camera.transform.SetParent(child.transform, false);
                break;
            }
            else
            {
                Transform _HasChildren = child.GetComponentInChildren<Transform>();
                if (_HasChildren != null)
                {
                    SetCameraToHead(child.gameObject, camera);
                }
            }
        }
    }

}
