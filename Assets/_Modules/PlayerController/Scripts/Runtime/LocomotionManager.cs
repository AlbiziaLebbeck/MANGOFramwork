using System;
using UnityEngine;

public class LocomotionManager : Singleton<LocomotionManager>
{
    public static bool IsMobile { get; private set; }
    public static bool IsTouch { get; private set; }

    public static event Action<bool> OnToggleControllerEvent;

    //Bridge
    public static event Action<Vector2, float> VirtualMoveInputEvent;
    public static event Action<Vector2> VirtualLookInputEvent;
    public static event Action<bool> VirtualJumpInputEvent;
    public static event Action<bool> VirtualSprintInputEvent;

    protected override void Awake()
    {
        IsMobile = CheckMobile.CheckIsMobile();
    }

    public void OnVirtualMoveInput(Vector2 virtualMoveDirection, float magnitude)
    {
        if (VirtualMoveInputEvent != null) VirtualMoveInputEvent(virtualMoveDirection, magnitude);
    }

    public void OnVirtualLookInput(Vector2 virtualLookDirection)
    {
        if (VirtualLookInputEvent != null) VirtualLookInputEvent(virtualLookDirection);
    }

    public void OnVirtualJumpInput(bool virtualJumpState)
    {
        if (VirtualJumpInputEvent != null) VirtualJumpInputEvent(virtualJumpState);
    }

    public void OnVirtualSprintInput(bool virtualSprintState)
    {
        if (VirtualSprintInputEvent != null) VirtualSprintInputEvent(virtualSprintState);
    }

    public void OnToggleController(bool _isTouch)
    {
        IsTouch = _isTouch;

        if (OnToggleControllerEvent != null) OnToggleControllerEvent(_isTouch);
    }
}
