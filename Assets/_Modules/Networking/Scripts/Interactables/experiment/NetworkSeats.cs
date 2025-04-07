using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using StarterAssets;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class NetworkSeats : NetworkMultiInteractable
{
    public List<Transform> sitPoints = new();

    public readonly SyncDictionary<int, NetworkObject> takenSeat = new();

    //to cached local player.
    private NetworkObject currentPlayer;
    private CharacterController currentPlayer_CharacterController;
    private ThirdPersonController currentPlayer_ThirdPersonController;
    private PlayerMovementHandler currentPlayer_PlayerMovementHandler;
    private Animator currentPlayer_Animator;

    public override void HandlePlayerLeftWorld(NetworkObject leftPlayer)
    {
        int seatIndex = takenSeat.FirstOrDefault(x => x.Value == leftPlayer).Key;

        if (takenSeat.ContainsKey(seatIndex))
        {
            takenSeat.Remove(seatIndex);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RPCRequestAddSeat(NetworkObject player, int takingSeat)
    {
        if(takenSeat.ContainsKey(takingSeat))
        {
            RPCResponseToSeatTaker(player.Owner, false);
        }
        else
        {
            takenSeat.Add(takingSeat, player);
            RPCResponseToSeatTaker(player.Owner, true);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RPCRequestRemoveSeat(NetworkObject player, int seatTaken)
    {
        if (takenSeat.ContainsKey(seatTaken))
        {
            takenSeat.Remove(seatTaken);
        }
    }

    [TargetRpc]
    private void RPCResponseToSeatTaker(NetworkConnection conn, bool IsAccept)
    {
        if(IsAccept)
        {
            Debug.Log("Seat Taking Allow.");
            currentPlayer_CharacterController.enabled = false;
            currentPlayer_ThirdPersonController.enabled = false;
            currentPlayer_PlayerMovementHandler.SetLockToPlatformTarget(sitPoints[currentTaking]);
            currentPlayer_Animator.SetInteger("Motion", 1);
        }
        else
        {
            Debug.Log("Seat was Taken.");
        }
    }

    private int currentTaking;

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

        int nextAvailableIndex = sitPoints
           .Select((t, i) => new { Index = i, Distance = Vector3.Distance(currentPlayer.transform.position, t.position) }) // Store index & distance
           .Where(x => !takenSeat.ContainsKey(x.Index)) // Exclude taken indices
           .OrderBy(x => x.Distance) // Sort by distance
           .Select(x => x.Index) // Select index
           .FirstOrDefault(); // Get the first available index

        currentTaking = nextAvailableIndex;

        RPCRequestAddSeat(player, currentTaking);
    }

    public override void Client_PerformInteractExit(NetworkObject player = null)
    {
        if (currentPlayer == null) return;

        currentPlayer_CharacterController.enabled = true;
        currentPlayer_ThirdPersonController.enabled = true;
        currentPlayer_PlayerMovementHandler.SetLockToPlatformTarget(null);
        currentPlayer_Animator.SetInteger("Motion", 0);

        RPCRequestRemoveSeat(player, currentTaking);
    }
}
