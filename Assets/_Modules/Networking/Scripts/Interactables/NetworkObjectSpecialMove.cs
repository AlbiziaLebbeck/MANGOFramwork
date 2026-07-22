using FishNet.Object;
using FishNet.Object.Synchronizing;
using StarterAssets;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class NetworkObjectSpecialMove : NetworkInteractable
{
    public Transform lockPosition;
    public UnityEvent onDoAction;
    public UnityEvent onStopAction;
    public AnimationClip newClip;
    public string targetStateNameToChange;

    private string modifiedStateName;
    private AnimationClip originalClip;
    private AnimatorOverrideController overrideController;

    private readonly SyncVar<bool> IsOverriding = new SyncVar<bool>();

    // ObserversRpc is not presisted when new player joined the world.
    // So I check syncVar whether this one was overriding some animator.
    public override void OnStartClient()
    {
        if (IsOverriding.Value)
        {
            if (CurrentInteractingNetworkObject.Value.TryGetComponent(out Animator animator))
            {
                DoOverride(animator);
            }
        }
    }

    //to cached local player.
    #region Cache Local
    private NetworkObject localPlayer;
    private CharacterController localPlayer_CharacterController;
    private ThirdPersonController localPlayer_ThirdPersonController;
    private PlayerMovementHandler localPlayer_PlayerMovementHandler;
    private BasicBehaviour localPlayer_BasicBehaviour;
    private MoveBehaviour localPlayer_MoveBehaviour;
    private FlyBehaviour localPlayer_FlyBehaviour;
    private LockTarget localPlayer_LockTarget;
    private Animator localPlayer_Animator;
    #endregion

    public override void Client_PerformInteractEnter(NetworkObject player = null)
    {
        #region Local Object
        if (localPlayer == null)
        {
            localPlayer = player;
            localPlayer_CharacterController = player.GetComponent<CharacterController>();
            localPlayer_ThirdPersonController = player.GetComponent<ThirdPersonController>();
            localPlayer_PlayerMovementHandler = player.GetComponent<PlayerMovementHandler>();
            localPlayer_BasicBehaviour = player.GetComponent<BasicBehaviour>();
            localPlayer_MoveBehaviour = player.GetComponent<MoveBehaviour>();
            localPlayer_FlyBehaviour = player.GetComponent<FlyBehaviour>();
            localPlayer_LockTarget = player.GetComponent<LockTarget>();
            localPlayer_Animator = player.GetComponent<Animator>();
        }

        if (localPlayer_BasicBehaviour != null)
        {
            localPlayer_BasicBehaviour.enabled = false;
            if (localPlayer_MoveBehaviour != null) localPlayer_MoveBehaviour.enabled = false;
            if (localPlayer_FlyBehaviour != null) localPlayer_FlyBehaviour.enabled = false;
            if (localPlayer_LockTarget != null) localPlayer_LockTarget.SetLockToPlatformTarget(lockPosition);
        }
        else
        {
            if (localPlayer_CharacterController != null) localPlayer_CharacterController.enabled = false;
            if (localPlayer_ThirdPersonController != null) localPlayer_ThirdPersonController.enabled = false;
            if (localPlayer_PlayerMovementHandler != null) localPlayer_PlayerMovementHandler.SetLockToPlatformTarget(lockPosition);
        }
        if (localPlayer_Animator != null) localPlayer_Animator.SetInteger("Motion", 1);
        #endregion

        onDoAction?.Invoke();
    }

    public override void Client_PerformInteractExit(NetworkObject player = null)
    {
        #region Local Object
        if (localPlayer == null) return;

        if (localPlayer_BasicBehaviour != null)
        {
            localPlayer_BasicBehaviour.enabled = true;
            if (localPlayer_MoveBehaviour != null) localPlayer_MoveBehaviour.enabled = true;
            if (localPlayer_FlyBehaviour != null) localPlayer_FlyBehaviour.enabled = true;
            if (localPlayer_LockTarget != null) localPlayer_LockTarget.SetLockToPlatformTarget(null);
        }
        else
        {
            if (localPlayer_CharacterController != null) localPlayer_CharacterController.enabled = true;
            if (localPlayer_ThirdPersonController != null) localPlayer_ThirdPersonController.enabled = true;
            if (localPlayer_PlayerMovementHandler != null) localPlayer_PlayerMovementHandler.SetLockToPlatformTarget(null);
        }
        if (localPlayer_Animator != null) localPlayer_Animator.SetInteger("Motion", 0);
        #endregion

        onStopAction?.Invoke();
    }

    public override void Server_PerformInteractEnter(NetworkObject player = null)
    {
        ObserverUpdateOverrideAnimation(player, true);
        IsOverriding.Value = true;
    }

    public override void Server_PerformInteractExit(NetworkObject player = null)
    {
        ObserverUpdateOverrideAnimation(player, false);
        IsOverriding.Value = false;
    }

    [ObserversRpc]
    private void ObserverUpdateOverrideAnimation(NetworkObject player, bool isOverride)
    {
        if (isOverride)
        {
            if(player.TryGetComponent(out Animator animator))
            {
                DoOverride(animator);
            }
        }
        else
        {
            DoReset();
        }
    }

    private void DoOverride(Animator animToOverride)
    {
        overrideController = new AnimatorOverrideController(animToOverride.runtimeAnimatorController);
        animToOverride.runtimeAnimatorController = overrideController;

        if (originalClip == null)
        {
            originalClip = GetOriginalAnimationClip(targetStateNameToChange, animToOverride);
            modifiedStateName = targetStateNameToChange;
        }

        if (originalClip != null)
        {
            overrideController[targetStateNameToChange] = newClip;

            if (!newClip.isLooping)
            {
                StartCoroutine(WaitForAnimationToEnd(newClip.length, () =>
                {
                    DoReset();
                }));
            }
        }
    }

    private void DoReset()
    {
        if (originalClip != null && !string.IsNullOrEmpty(modifiedStateName))
        {
            overrideController[modifiedStateName] = originalClip;
            originalClip = null;
            modifiedStateName = null;
        }
    }

    private AnimationClip GetOriginalAnimationClip(string stateName, Animator animator)
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip.name == stateName)
                return clip;
        }
        return null;
    }

    private IEnumerator WaitForAnimationToEnd(float duration, Action onAnimationEnd)
    {
        yield return new WaitForSeconds(duration);
        // Call a function when the animation ends
        onAnimationEnd?.Invoke();
    }
}
