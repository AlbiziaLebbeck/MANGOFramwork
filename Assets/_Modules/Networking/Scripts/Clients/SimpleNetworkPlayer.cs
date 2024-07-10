using FishNet.Object;
using TMPro;
using UnityEngine;

public class SimpleNetworkPlayer : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.IsOwner)
        {
            nameText.text = UserReferencePersistent.Instance.Username;
            RPC_ChangeUsername(UserReferencePersistent.Instance.Username);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RPC_ChangeUsername(string _name)
    {
        OB_ChangeUsername(_name);
    }

    [ObserversRpc]
    public void OB_ChangeUsername(string _name)
    {
        nameText.text = _name;
    }
}
