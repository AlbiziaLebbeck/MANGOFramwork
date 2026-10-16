using UnityEngine;

public sealed class AvatarAnimationEvents : MonoBehaviour
{
    [System.NonSerialized] public GameObject Owner;
    private void OnFootstep(AnimationEvent value) { if (Owner != null) Owner.SendMessage("OnFootstep", value, SendMessageOptions.DontRequireReceiver); }
    private void OnLand(AnimationEvent value) { if (Owner != null) Owner.SendMessage("OnLand", value, SendMessageOptions.DontRequireReceiver); }
}
