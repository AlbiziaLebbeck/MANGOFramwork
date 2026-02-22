using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LocomotionCanvas : MonoBehaviour
{
    [SerializeField] private LocomotionManager _manager;
    [SerializeField] private GameObject containerPC;
    [SerializeField] private GameObject containerMobile;
    [SerializeField] private GameObject mobileControllerPanel;

    [SerializeField] private Button switchControllerButton;
    [SerializeField] private Image iconImage;
    [SerializeField] private List<Sprite> PCControllerIcon = new();
    [SerializeField] private List<Sprite> mobileControllerIcon = new();

    private int currentIconIndex;
    private bool isTouch;

    private void Awake()
    {
        switchControllerButton.onClick.RemoveAllListeners();
        switchControllerButton.onClick.AddListener(() =>
        {
            ToggleController();
        });
    }

    private void Start()
    {
        containerPC.SetActive(!LocomotionManager.IsMobile);
        containerMobile.SetActive(LocomotionManager.IsMobile);

        currentIconIndex = 0;
        isTouch = false;

        iconImage.sprite = LocomotionManager.IsMobile ? mobileControllerIcon[currentIconIndex] : PCControllerIcon[currentIconIndex];
        _manager.OnToggleController(isTouch);
        mobileControllerPanel.SetActive(!isTouch && LocomotionManager.IsMobile);
    }

    private void ToggleController()
    {
        currentIconIndex++;
        currentIconIndex %= LocomotionManager.IsMobile ? mobileControllerIcon.Count : PCControllerIcon.Count;
        iconImage.sprite = LocomotionManager.IsMobile ? mobileControllerIcon[currentIconIndex] : PCControllerIcon[currentIconIndex];

        isTouch = !isTouch;
        _manager.OnToggleController(isTouch);
        mobileControllerPanel.SetActive(!isTouch && LocomotionManager.IsMobile);
    }
}


