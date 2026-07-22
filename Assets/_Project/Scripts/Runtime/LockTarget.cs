using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LockTarget : MonoBehaviour
{
    void Update()
    {
        if (_lockToPlatformTarget != null) return;

        if (isInSpecialAnimation) return;
    }

    private void LateUpdate()
    {
        HandleMovingPlatform();
    }

    #region New Feature
    [SerializeField] private float smoothingFactor = 0.1f;
    private Vector3 smoothedPosition;
    private Quaternion smoothedRotation;
    [SerializeField] private Transform _lockToPlatformTarget;
    private bool isInSpecialAnimation;

    private void HandleMovingPlatform()
    {
        if (_lockToPlatformTarget == null) return;

        smoothedPosition = Vector3.Lerp(smoothedPosition, _lockToPlatformTarget.position, Time.deltaTime * smoothingFactor);
        transform.position = smoothedPosition;

        smoothedRotation = Quaternion.Lerp(transform.rotation, _lockToPlatformTarget.rotation, Time.deltaTime * smoothingFactor);
        transform.rotation = smoothedRotation;
    }

    public void SetLockToPlatformTarget(Transform _LockToPlatformTarget)
    {
        _lockToPlatformTarget = _LockToPlatformTarget;
    }

    public void SetSpecialAnimation(bool specialAnimationOn)
    {
        isInSpecialAnimation = specialAnimationOn;
    }
    #endregion
}
