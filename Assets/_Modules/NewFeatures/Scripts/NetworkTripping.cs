using FishNet.Object;
using StarterAssets;
using System;
using System.Collections;
using UnityEngine;

public class NetworkTripping : NetworkBehaviour
{
    #region Cache Local
    private GameObject playerRig;
    private NetworkObject localPlayer;
    private CharacterController localPlayer_CharacterController;
    private ThirdPersonController localPlayer_ThirdPersonController;
    private Animator localPlayer_Animator;
    #endregion

    public AnimationClip newClip;
    public string targetStateNameToChange;

    private string modifiedStateName;
    private AnimationClip originalClip;
    private AnimatorOverrideController overrideController;

    public AreaSpawner Spawner;
    private bool tripping;

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

            tripping = false;

            localPlayer_CharacterController.enabled = true;
            localPlayer_ThirdPersonController.enabled = true;
            localPlayer_Animator.SetInteger("Motion", 0);
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

    [ServerRpc(RequireOwnership = false)]
    private void RPCReportTriping(NetworkObject player)
    {
        ObserverCallTrippingAnimation(player);

        if(Spawner != null)
        {
            gameObject.transform.position = Spawner.GetRandomSpawn();
        }
    }

    [ObserversRpc]
    private void ObserverCallTrippingAnimation(NetworkObject player)
    {
        if (player.TryGetComponent(out Animator animator))
        {
            DoOverride(animator);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (tripping) return;

            if (playerRig == null)
            {
                playerRig = UserReferencePersistent.Instance.PlayerGameObject;

                playerRig.TryGetComponent(out localPlayer);

                if (playerRig != null)
                {
                    localPlayer_CharacterController = playerRig.GetComponent<CharacterController>();
                    localPlayer_ThirdPersonController = playerRig.GetComponent<ThirdPersonController>();
                    localPlayer_Animator = playerRig.GetComponent<Animator>();
                }
            }

            if (playerRig == null) return;

            localPlayer_CharacterController.enabled = false;
            localPlayer_ThirdPersonController.enabled = false;
            localPlayer_Animator.SetInteger("Motion", 1);

            tripping = true;

            RPCReportTriping(localPlayer);
        }
    }
}
