using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public abstract class NetworkInteractable : NetworkBehaviour
{
    public Transform ButtonAnchor;
    public string InteractionPrompt = "Press E to interact";
    public readonly SyncVar<bool> IsInteracted = new SyncVar<bool>();
    public bool CoInteractAllow = false;
    public readonly SyncVar<NetworkConnection> CurrentPlayer = new SyncVar<NetworkConnection>();
    public readonly SyncVar<NetworkObject> CurrentInteractingNetworkObject = new SyncVar<NetworkObject>();

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
        if (CurrentPlayer.Value == leftPlayer.Owner)
        {
            IsInteracted.Value = false;
            CurrentPlayer.Value = null;
            CurrentInteractingNetworkObject.Value = null;
        }
    }

    public virtual void Client_OnFocus()
    {
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

    public virtual void Server_PerformInteractEnter(NetworkObject player = null) { }
    public virtual void Server_PerformInteractExit(NetworkObject player = null) { }
    public virtual void Client_PerformInteractEnter(NetworkObject player = null) { }
    public virtual void Client_PerformInteractExit(NetworkObject player = null) { }

    [ServerRpc(RequireOwnership = false)]
    public void ServerRpcInteractEnter(NetworkObject player)
    {
        if (!IsInteracted.Value)
        {
            IsInteracted.Value = true;

            CurrentPlayer.Value = player.Owner;
            CurrentInteractingNetworkObject.Value = player;

            Server_PerformInteractEnter(player);

            TargetInteractEnter(player.Owner, player);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void ServerRpcInteractExit(NetworkObject player)
    {
        if (CurrentPlayer.Value != player.Owner) return;

        if (IsInteracted.Value)
        {
            IsInteracted.Value = false;

            CurrentPlayer.Value = null;
            CurrentInteractingNetworkObject.Value = null;

            Server_PerformInteractExit(player);

            TargetInteractExit(player.Owner, player);
        }
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
