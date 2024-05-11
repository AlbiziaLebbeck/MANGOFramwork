using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCameraHandler : Singleton<PlayerCameraHandler>
{
    [SerializeField] private GameObject mainVirtualCamera;
    [SerializeField] private GameObject currentVirtualCameraActive;

    public GameObject MainCamera;
    public CinemachineVirtualCamera MainVirtualCamera { get; private set; }
    public CinemachineFramingTransposer MainCameraFramingTransposer { get; private set; }
    public GameObject CurrentActiveCameraObject { get => currentVirtualCameraActive; }

    protected override void Awake()
    {
        base.Awake();

        if (mainVirtualCamera != null)
        {
            MainVirtualCamera = mainVirtualCamera.GetComponent<CinemachineVirtualCamera>();
            MainCameraFramingTransposer = MainVirtualCamera.GetCinemachineComponent<CinemachineFramingTransposer>();
        }

        var mainCameraList = GameObject.FindGameObjectsWithTag("MainCamera");
        if (mainCameraList.Length > 1)
        {
            foreach (var mainCamera in mainCameraList)
            {
                if (mainCamera != MainCamera)
                {
                    Destroy(mainCamera);
                }
            }
        }
    }

    private void Start()
    {
        if (GameObject.FindGameObjectWithTag("Player") == null) return;

        Initialize();
    }

    public void Initialize()
    {
        if (!UserReferencePersistent.Instance.PlayerCameraRoot) return;
        AssignFollowCamera(MainVirtualCamera, UserReferencePersistent.Instance.PlayerCameraRoot);
        AssignCameraLookAt(MainVirtualCamera, UserReferencePersistent.Instance.PlayerCameraRoot);
    }

    public void AssignFollowCamera(CinemachineVirtualCamera _virtualCam, Transform _cameraRoot)
    {
        _virtualCam.Follow = _cameraRoot;
    }

    public void AssignCameraLookAt(CinemachineVirtualCamera _virtualCamera, Transform _cameraRoot)
    {
        _virtualCamera.LookAt = _cameraRoot;
    }
}
