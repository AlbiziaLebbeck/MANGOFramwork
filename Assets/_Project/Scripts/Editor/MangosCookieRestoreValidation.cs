using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

public static class MangosCookieRestoreValidation
{
    [MenuItem("MANGOs Framework/Validation/Cookie Avatar and Controller")]
    public static void Run()
    {
        foreach (var name in new[] { "ArmatureAvatar", "RajPatternAvatar" })
        {
            var avatar = Resources.Load<Avatar>("AvatarLoader/" + name);
            Require(avatar != null && avatar.isValid && avatar.isHuman, "Cookie reference Avatar: " + name);
        }
        Require(Resources.Load<RuntimeAnimatorController>("AvatarLoader/AvatarController") != null, "Cookie Animator Controller");
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Modules/PlayerController/Scripts/InputActions/PlayerActions.inputactions");
        Require(actions != null && actions.FindActionMap("KeyboardMouse") != null && actions.FindActionMap("Touch") != null, "Cookie input maps");
        Require(actions.FindActionMap("KeyboardMouse").FindAction("RightMousePressed") != null, "Legacy view-change action");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Modules/Networking/Prefabs/NetworkPlayer.prefab");
        Require(prefab != null && prefab.GetComponent<PlayerMovementHandler>() != null && prefab.GetComponent<ThirdPersonController>() != null, "Network player movement components");
        var loader = prefab.GetComponentInChildren<AvatarLoader>(true);
        Require(loader != null, "Network player AvatarLoader");
        var animator = new SerializedObject(loader).FindProperty("animator").objectReferenceValue;
        Require(animator != null && animator == prefab.GetComponent<Animator>(), "Cookie loader retains the player-root Animator");
        Debug.Log("MANGOS_COOKIE_RESTORE_TESTS PASSED: reference Avatars, Animator Controller, keyboard/touch maps and network prefab bindings");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
