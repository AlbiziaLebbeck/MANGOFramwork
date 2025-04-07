using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class NetworkMultiInteractable : NetworkBehaviour
{
    public Transform ButtonAnchor;
    public string InteractionPrompt = "Press E to interact";
    public int MaximumInteratorCount = 2;
    public readonly SyncList<NetworkConnection> CurrentPlayer = new SyncList<NetworkConnection>();
    public readonly SyncList<NetworkObject> CurrentInteractingNetworkObject = new SyncList<NetworkObject>();

    public override void OnStartServer()
    {
        if (base.IsServerInitialized)
        {
            if (WorldManager.Instance != null)
            {
                WorldManager.Instance.OnClientLeftWorld += WorldManager_OnClientLeftWorld;
            }
        }
    }

    public override void OnStopServer()
    {
        if (base.IsServerInitialized)
        {
            if (WorldManager.Instance != null)
            {
                WorldManager.Instance.OnClientLeftWorld -= WorldManager_OnClientLeftWorld;
            }
        }
    }

    private void WorldManager_OnClientLeftWorld(WorldDetails worldDetails, NetworkObject leftPlayer)
    {
        if (CurrentPlayer.Contains(leftPlayer.Owner))
        {
            HandlePlayerLeftWorld(leftPlayer);

            //Clear player from list
            CurrentPlayer.Remove(leftPlayer.Owner);
            CurrentInteractingNetworkObject.RemoveAll(x => x.Owner == leftPlayer.Owner);
        }
    }

    public virtual void Client_OnFocus()
    {
        if (!CheckValidity()) return;

        PersistentCanvas.InteractionCanvas.SetButtonToTarget(ButtonAnchor);
        PersistentCanvas.InteractionCanvas.AssignInteractionText(InteractionPrompt);
        PersistentCanvas.InteractionCanvas.AddEnterButtonEvent(() =>
        {
            ServerRpcInteractEnter(UserReferencePersistent.Instance.PlayerGameObject.GetComponent<NetworkObject>());

            PersistentCanvas.InteractionCanvas.AddExitButtonEvent(() =>
            {
                ServerRpcInteractExit(UserReferencePersistent.Instance.PlayerGameObject.GetComponent<NetworkObject>());
            });
        });
    }

    public virtual void Client_OnLoseFocus()
    {
        PersistentCanvas.InteractionCanvas.SetButtonToTarget(null);
        PersistentCanvas.InteractionCanvas.RemoveButtonText();
        PersistentCanvas.InteractionCanvas.RemoveButtonEvent();
    }

    public bool CheckValidity()
    {
        return CurrentInteractingNetworkObject.Count < MaximumInteratorCount;
    }

    public virtual void HandlePlayerLeftWorld(NetworkObject leftPlayer) { }
    public virtual void Server_PerformInteractEnter(NetworkObject latestPlayer = null) { }
    public virtual void Server_PerformInteractExit(NetworkObject latestPlayer = null) { }
    public virtual void Client_PerformInteractEnter(NetworkObject latestPlayer = null) { }
    public virtual void Client_PerformInteractExit(NetworkObject latestPlayer = null) { }

    [ServerRpc(RequireOwnership = false)]
    public void ServerRpcInteractEnter(NetworkObject player)
    {
        if (!CheckValidity()) return;

        CurrentPlayer.Add(player.Owner);
        CurrentInteractingNetworkObject.Add(player);

        Server_PerformInteractEnter(player);

        TargetInteractEnter(player.Owner, player);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ServerRpcInteractExit(NetworkObject player)
    {
        if (!CurrentPlayer.Contains(player.Owner)) return;

        CurrentPlayer.Remove(player.Owner);

        CurrentInteractingNetworkObject.Remove(player);

        Server_PerformInteractExit(player);

        TargetInteractExit(player.Owner, player);
    }

    [TargetRpc]
    public void TargetInteractEnter(NetworkConnection conn, NetworkObject player)
    {
        Client_PerformInteractEnter(player);

        if (player.TryGetComponent(out NetworkInteractor interactor))
        {
            interactor.IsInteracting = true;
        }
    }

    [TargetRpc]
    public void TargetInteractExit(NetworkConnection conn, NetworkObject player)
    {
        Client_PerformInteractExit(player);

        if (player.TryGetComponent(out NetworkInteractor interactor))
        {
            interactor.IsInteracting = false;
        }
    }
}
