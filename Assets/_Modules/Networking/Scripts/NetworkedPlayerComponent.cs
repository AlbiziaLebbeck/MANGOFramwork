using FishNet.Component.Observing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using StarterAssets;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class NetworkedPlayerComponent : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;

    [SerializeField] private List<UnityEngine.Object> localObject = new List<UnityEngine.Object>();

    const string defaultMatchId = "Th1sI5DefaUltM7tch1d";

    public readonly SyncVar<string> PlayerName = new SyncVar<string>();

    private void Awake()
    {
        PlayerName.OnChange += OnChangePlayerName;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!base.IsOwner)
        {
            gameObject.tag = "RemotePlayer";

            GetComponent<ThirdPersonController>().enabled = false;
            GetComponent<StarterAssetsInputs>().enabled = false;
            GetComponent<PlayerMovementHandler>().enabled = false;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
            GetComponent<PlayerInput>().enabled = false;
#endif
            return;
        }

        GetComponent<PlayerMovementHandler>().enabled = true;

        gameObject.tag = "Player";

        int matchId = defaultMatchId.GetHashCode();

        UserReferencePersistent.Instance.AssignPlayerGameObject(gameObject);
    }

    private void OnChangePlayerName(string prev, string next, bool asServer)
    {
        if(nameText != null) nameText.text = next;
    }

    [ServerRpc]
    public void RPC_ChangeUsername(NetworkedPlayerComponent _player, string _name)
    {
        _player.PlayerName.Value = _name;
    }

    [ServerRpc]
    public void RPC_ChangeUserMatchId(NetworkedPlayerComponent _player, int _matchId)
    {
        if (_player.Owner.IsValid)
        {
            MatchCondition.AddToMatch(_matchId, _player.Owner, replaceMatch: true);
        }
        else
        {
            MatchCondition.AddToMatch(_matchId, _player.NetworkObject, replaceMatch: true);
        }
    }

    #region WorkAround Fix
    private void OnFootstep(AnimationEvent animationEvent)
    {

    }

    private void OnLand(AnimationEvent animationEvent)
    {

    }
    #endregion
}
