using System;
using FishNet.Component.Animating;
using UnityEngine;

public sealed class MangosAvatarRuntime : MonoBehaviour
{
    public Animator ActiveAnimator { get; private set; }
    public static Animator ResolveAnimator(GameObject player)
    {
        if (player == null) return null;
        var runtime = player.GetComponent<MangosAvatarRuntime>();
        return runtime != null && runtime.ActiveAnimator != null ? runtime.ActiveAnimator : player.GetComponent<Animator>();
    }
    public void Bind(Animator next)
    {
        var previous = ResolveAnimator(gameObject);
        if (previous != null && previous != next)
        {
            if (previous.runtimeAnimatorController != null) next.runtimeAnimatorController = previous.runtimeAnimatorController;
            next.applyRootMotion = false;
            next.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            next.Rebind();
            next.Update(0);
            foreach (var parameter in previous.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Float) next.SetFloat(parameter.nameHash, previous.GetFloat(parameter.nameHash));
                else if (parameter.type == AnimatorControllerParameterType.Int) next.SetInteger(parameter.nameHash, previous.GetInteger(parameter.nameHash));
                else if (parameter.type == AnimatorControllerParameterType.Bool) next.SetBool(parameter.nameHash, previous.GetBool(parameter.nameHash));
            }
            for (int layer = 0; layer < Math.Min(previous.layerCount, next.layerCount); layer++)
            {
                var state = previous.GetCurrentAnimatorStateInfo(layer);
                if (next.HasState(layer, state.fullPathHash)) next.Play(state.fullPathHash, layer, state.normalizedTime);
                next.SetLayerWeight(layer, previous.GetLayerWeight(layer));
            }
            previous.enabled = false;
        }
        ActiveAnimator = next;
        if (TryGetComponent<NetworkAnimator>(out var networkAnimator)) networkAnimator.SetAnimator(next);
        if (TryGetComponent<PlayerMovementHandler>(out var movement)) movement.SetAnimator(next);
        if (TryGetComponent<BasicBehaviour>(out var fly)) fly.SetAnimator(next);
        if (TryGetComponent<StarterAssets.ThirdPersonController>(out var starter)) starter.AnimatorOverride = next;
    }
}
