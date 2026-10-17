using FishNet.Object;
using StarterAssets;
using UnityEngine;

public class NetworkSeat : NetworkInteractable
{
    public Transform sitPoint;

    //to cached local player.
    private NetworkObject currentPlayer;
    private CharacterController currentPlayer_CharacterController;
    private ThirdPersonController currentPlayer_ThirdPersonController;
    private PlayerMovementHandler currentPlayer_PlayerMovementHandler;
    private Animator currentPlayer_Animator;

    public override void Client_PerformInteractEnter(NetworkObject player = null)
    {
        if (currentPlayer == null)
        {
            currentPlayer = player;
            currentPlayer_CharacterController = player.GetComponent<CharacterController>();
            currentPlayer_ThirdPersonController = player.GetComponent<ThirdPersonController>();
            currentPlayer_PlayerMovementHandler = player.GetComponent<PlayerMovementHandler>();
            currentPlayer_Animator = player.GetComponent<Animator>();
        }

        currentPlayer_CharacterController.enabled = false;
        currentPlayer_ThirdPersonController.enabled = false;
        currentPlayer_PlayerMovementHandler.SetLockToPlatformTarget(sitPoint);
        currentPlayer_Animator.SetInteger("Motion", 1);
    }

    public override void Client_PerformInteractExit(NetworkObject player = null)
    {
        if (currentPlayer == null) return;

        currentPlayer_CharacterController.enabled = true;
        currentPlayer_ThirdPersonController.enabled = true;
        currentPlayer_PlayerMovementHandler.SetLockToPlatformTarget(null);
        currentPlayer_Animator.SetInteger("Motion", 0);
    }
}
