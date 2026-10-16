using System;
using FishNet.Component.Prediction;
using FishNet.Object.Prediction;
using UnityEditor;
using UnityEngine;

public static class MangosUnity6PhysicsValidation
{
    [MenuItem("MANGOs Framework/Validation/Unity 6 Physics")]
    public static void Run()
    {
        var object3D = new GameObject("MangosPhysicsValidation3D") { hideFlags = HideFlags.HideAndDontSave };
        var object2D = new GameObject("MangosPhysicsValidation2D") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var body = object3D.AddComponent<Rigidbody>();
            body.useGravity = false;
            var expected3D = new Vector3(1.25f, -2f, 3.5f);
            body.linearVelocity = expected3D;
            body.angularVelocity = new Vector3(0.1f, 0.2f, 0.3f);
            var state = body.GetState();
            Require(Vector3.Distance(state.Velocity, expected3D) < 0.0001f, "3D state capture");
            body.linearVelocity = Vector3.zero;
            body.SetState(state);
            Require(Vector3.Distance(body.linearVelocity, expected3D) < 0.0001f, "3D state restore");
            Require(Vector3.Distance(body.angularVelocity, state.AngularVelocity) < 0.0001f, "3D angular state restore");

            var prediction = new PredictionRigidbody();
            prediction.Initialize(body);
            prediction.Velocity(-expected3D);
            Require(Vector3.Distance(body.linearVelocity, -expected3D) < 0.0001f, "3D prediction velocity");

            var body2D = object2D.AddComponent<Rigidbody2D>();
            body2D.gravityScale = 0;
            var expected2D = new Vector2(-1.5f, 2.25f);
            body2D.linearVelocity = expected2D;
            body2D.angularVelocity = 15;
            var state2D = body2D.GetState();
            Require(Vector2.Distance(state2D.Velocity, expected2D) < 0.0001f, "2D state capture");
            body2D.linearVelocity = Vector2.zero;
            body2D.SetState(state2D);
            Require(Vector2.Distance(body2D.linearVelocity, expected2D) < 0.0001f, "2D state restore");
            Require(Mathf.Abs(body2D.angularVelocity - state2D.AngularVelocity) < 0.0001f, "2D angular state restore");

            var prediction2D = new PredictionRigidbody2D();
            prediction2D.Initialize(body2D);
            prediction2D.Velocity(-expected2D);
            Require(Vector2.Distance(body2D.linearVelocity, -expected2D) < 0.0001f, "2D prediction velocity");

            Debug.Log("MANGOS_UNITY6_PHYSICS_TESTS PASSED: 3D/2D state capture, restore and prediction velocity");
            var settings = Resources.Load<TMPro.TMP_Settings>("TMP Settings");
            Require(settings != null, "TMP Settings resource exists");
            var serializedSettings = new SerializedObject(settings);
            Require(serializedSettings.FindProperty("assetVersion").stringValue == "2", "Unity 6 TMP Settings schema");
            Require(serializedSettings.FindProperty("m_defaultFontAsset").objectReferenceValue != null, "TMP default font reference");
            Debug.Log("MANGOS_UNITY6_TMP_SETTINGS_TEST PASSED: schema version 2 and default font resource");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(object3D);
            UnityEngine.Object.DestroyImmediate(object2D);
        }
    }

    private static void Require(bool passed, string check)
    {
        if (!passed) throw new InvalidOperationException("Unity 6 physics validation failed: " + check);
    }
}
