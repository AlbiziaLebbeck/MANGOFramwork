using System;
using System.IO;
using GLTFast;
using UnityEditor;
using UnityEngine;

public static class MangosRealAvatarValidation
{
    public static async void Run()
    {
        GameObject model = null; Avatar avatar = null;
        using var gltf = new GltfImport(deferAgent: new UninterruptedDeferAgent());
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            int fixtureArgument = Array.IndexOf(arguments, "-mangosAvatarFixture");
            var path = Path.GetFullPath(fixtureArgument >= 0 && fixtureArgument + 1 < arguments.Length ? arguments[fixtureArgument + 1] : "../validation/fixtures/RajPattern_Blue.glb");
            if (!await gltf.LoadGltfBinary(File.ReadAllBytes(path), new Uri(path))) throw new Exception("Cannot load fixture");
            model = new GameObject("RealAvatarQA");
            if (!await gltf.InstantiateMainSceneAsync(model.transform)) throw new Exception("Cannot instantiate fixture");
            foreach (var legacy in model.GetComponentsInChildren<Animation>()) legacy.enabled = false;
            avatar = HumanoidRigBuilder.Build(model);
            var animator = model.AddComponent<Animator>(); animator.avatar = avatar;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Modules/PlayerController/Animations/CustomThirdPersonController.controller");
            animator.fireEvents = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            void Dump(string stage)
            {
                foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftFoot })
                {
                    var t = animator.GetBoneTransform(bone);
                    Debug.Log($"REAL_AVATAR {stage} {bone}={t.name} position={t.position.ToString("F4")} rotation={t.rotation.eulerAngles.ToString("F2")} scale={t.lossyScale}");
                }
            }
            Dump("rest"); animator.Rebind(); animator.SetBool("Grounded", true);
            animator.Update(0); Dump("idle0");
            for (int i=0;i<60;i++) animator.Update(1f/60f);
            Dump("idle60");
            var left = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
            var right = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            if (left.x >= hips.x || right.x <= hips.x) throw new Exception("Idle retarget crossed the hands across the body");
            Debug.Log("REAL_AVATAR_VALIDATION_COMPLETED"); EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally { if (model != null) UnityEngine.Object.DestroyImmediate(model); if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar); }
    }
}
