using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using MANGOsFramework.Experiment;

public static class MangosInputValidation
{
    public static void Run()
    {
        var player = new GameObject("InputValidationPlayer");
        var bridgeObject = new GameObject("InputValidationBridge");
        try
        {
            var bridge = bridgeObject.AddComponent<MangosBrowserBridge>();
            typeof(MangosBrowserBridge).GetField("localPlayer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridge, player);
            bridge.OnDeviceChanged("9");
            bridge.OnInput("{\"x\":2,\"y\":2,\"lx\":12,\"ly\":4,\"jump\":true,\"sprint\":true,\"fly\":true,\"vertical\":1,\"lookToken\":1}");
            Require(MangosInputRouter.Move.magnitude <= 1.001f && MangosInputRouter.Axis("Horizontal") > 0, "Move clamping/axis routing");
            Require(MangosInputRouter.JumpDown("Jump") && !MangosInputRouter.JumpDown("Jump"), "Jump must fire once per press");
            Require(MangosInputRouter.FlyDown("Fly") && !MangosInputRouter.FlyDown("Fly"), "Fly must fire once per press");
            Require(MangosInputRouter.SprintHeld("Sprint") && MangosInputRouter.FlyVertical == 1, "Sprint and flight elevation");
            typeof(MangosInputRouter).GetMethod("BeginFrame", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Require(MangosInputRouter.Look == new Vector2(12, 4), "Camera delta routing");
            bridge.OnInput("{\"jump\":true}");
            Require(!MangosInputRouter.JumpDown("Jump"), "Held jump must not repeat");
            bridge.OnInputFocus("1");
            bridge.OnInput("{\"x\":1,\"jump\":true}");
            Require(MangosInputRouter.Move == Vector2.zero && !MangosInputRouter.JumpDown("Jump"), "Typing focus must block and reset controls");
            bridge.OnInputFocus("0");
            bridge.OnInput("{\"x\":1,\"jump\":true}");
            bridge.OnDeviceChanged("13");
            Require(MangosInputRouter.Portrait && MangosInputRouter.Move == Vector2.zero && !MangosInputRouter.JumpDown("Jump"), "Rotation must clear held controls");
            MangosBrowserBridge.SetUiPanel(true);
            bridge.OnInput("{\"x\":1,\"jump\":true}");
            Require(MangosInputRouter.Blocked && MangosInputRouter.Move == Vector2.zero && !MangosInputRouter.JumpDown("Jump"), "Avatar modal must block and clear player input");
            MangosBrowserBridge.SetUiPanel(false);
            Require(!MangosInputRouter.Blocked && MangosInputRouter.Move == Vector2.zero, "Closing modal must not restore stale held input");
            ValidateSelectableAvatar();
            Debug.Log("MANGOS_INPUT_TESTS PASSED: walking/fly input routing, single-press actions, focus, orientation and avatar modal reset");
        }
        finally
        {
            MangosBrowserBridge.SetUiPanel(false);
            MangosInputRouter.Reset();
            typeof(MangosInputRouter).GetField("BrowserBlocked", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
            typeof(MangosInputRouter).GetField("NativeBlocked", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
            typeof(MangosInputRouter).GetProperty("DeviceFlags").SetValue(null, 0);
            UnityEngine.Object.DestroyImmediate(bridgeObject); UnityEngine.Object.DestroyImmediate(player);
        }
    }
    private static void ValidateSelectableAvatar()
    {
        var item = new GameObject("AvatarValidationItem", typeof(RectTransform), typeof(Button));
        try
        {
            var icon = item.AddComponent<AvatarIcon>();
            typeof(AvatarIcon).GetField("buttonRef", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(icon, item.GetComponent<Button>());
            icon.SetIconData("https://api.mangosgo.com/api/v1/avatars/test.glb", null);
            Require(icon.IsSelectable, "Avatar selection must be available before its thumbnail");
            typeof(AvatarIcon).GetMethod("HandleThumbnailReady", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(icon, new object[] { null });
            Require(icon.IsSelectable, "A failed preview must not disable avatar selection");
        }
        finally { UnityEngine.Object.DestroyImmediate(item); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
