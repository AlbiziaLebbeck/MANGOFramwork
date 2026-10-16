using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MangosAvatarValidation
{
    public static void Run()
    {
        MangosAvatarSetup.Configure();
        var small = MakeRig(0.7f); var tall = MakeRig(1.4f);
        Avatar a = null, b = null;
        try
        {
            a = HumanoidRigBuilder.Build(small); b = HumanoidRigBuilder.Build(tall);
            Require(a.isValid && a.isHuman && b.isValid && b.isHuman && a != b, "Each skeleton owns a valid Avatar");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Modules/PlayerController/Animations/CustomThirdPersonController.controller");
            foreach (var pair in new[] { (small, a), (tall, b) })
            {
                var animator = pair.Item1.AddComponent<Animator>();
                animator.avatar = pair.Item2;
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.Rebind();
                var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
                animator.SetBool("Grounded", true); animator.SetFloat("Speed", 2); animator.SetFloat("MotionSpeed", 1);
                var restRotation = upper.localRotation;
                bool animated = false;
                for (int frame = 0; frame < 60; frame++)
                {
                    animator.Update(1f / 60f);
                    animated |= Quaternion.Angle(restRotation, upper.localRotation) > 1;
                }
                Require(animated && animator.GetCurrentAnimatorClipInfo(0).Length > 0, "Animation actually updates the skeleton");
                var movedLength = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
                Require(Mathf.Abs(movedLength - length) < 0.01f, "Retarget preserves limb lengths");
            }
            var bad = new GameObject("Unrigged");
            bool rejected = false;
            try { HumanoidRigBuilder.Build(bad); } catch (InvalidOperationException) { rejected = true; }
            finally { UnityEngine.Object.DestroyImmediate(bad); }
            Require(rejected, "Unsupported model rejected with diagnostic");
            ValidateVrm();
            MangosAnimationAudit.Export();
            Debug.Log("MANGOS_AVATAR_TESTS PASSED: per-model Humanoid, different proportions, 60 animation frames without arm stretching, missing-rig rejection");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(small); UnityEngine.Object.DestroyImmediate(tall);
            if (a != null) UnityEngine.Object.DestroyImmediate(a);
            if (b != null) UnityEngine.Object.DestroyImmediate(b);
        }
    }
    private static void Require(bool valid, string message) { if (!valid) throw new Exception("Avatar test failed: " + message); }
    private static void ValidateVrm()
    {
        var source = MakeRig(1);
        GameObject imported = null;
        try
        {
            var transforms = source.GetComponentsInChildren<Transform>();
            var nodes = new Newtonsoft.Json.Linq.JArray();
            foreach (var node in transforms)
            {
                var children = new Newtonsoft.Json.Linq.JArray();
                foreach (Transform child in node) children.Add(Array.IndexOf(transforms, child));
                nodes.Add(new Newtonsoft.Json.Linq.JObject { ["name"] = node.name,
                    ["translation"] = new Newtonsoft.Json.Linq.JArray(node.localPosition.x, node.localPosition.y, node.localPosition.z), ["children"] = children });
            }
            var human = new Newtonsoft.Json.Linq.JObject();
            foreach (var mapping in new[] { "hips:Hips", "spine:Spine", "chest:Chest", "neck:Neck", "head:Head",
                "leftUpperArm:LeftArm", "leftLowerArm:LeftForeArm", "leftHand:LeftHand", "rightUpperArm:RightArm", "rightLowerArm:RightForeArm", "rightHand:RightHand",
                "leftUpperLeg:LeftUpLeg", "leftLowerLeg:LeftLeg", "leftFoot:LeftFoot", "rightUpperLeg:RightUpLeg", "rightLowerLeg:RightLeg", "rightFoot:RightFoot" })
            {
                var parts = mapping.Split(':');
                human[parts[0]] = new Newtonsoft.Json.Linq.JObject { ["node"] = Array.FindIndex(transforms, t => t.name == "mixamorig:" + parts[1]) };
            }
            var vrm = new Newtonsoft.Json.Linq.JObject {
                ["specVersion"] = "1.0", ["meta"] = new Newtonsoft.Json.Linq.JObject { ["name"] = "MANGOs synthetic rig QA", ["version"] = "1", ["authors"] = new Newtonsoft.Json.Linq.JArray("MANGOs QA"), ["licenseUrl"] = "https://vrm.dev/licenses/1.0/" },
                ["humanoid"] = new Newtonsoft.Json.Linq.JObject { ["humanBones"] = human }
            };
            var gltf = new Newtonsoft.Json.Linq.JObject { ["asset"] = new Newtonsoft.Json.Linq.JObject { ["version"] = "2.0" }, ["scene"] = 0,
                ["scenes"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["nodes"] = new Newtonsoft.Json.Linq.JArray(0) }), ["nodes"] = nodes,
                ["buffers"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["byteLength"] = 4 }),
                ["extensionsUsed"] = new Newtonsoft.Json.Linq.JArray("VRMC_vrm"), ["extensions"] = new Newtonsoft.Json.Linq.JObject { ["VRMC_vrm"] = vrm } };
            var text = gltf.ToString(Newtonsoft.Json.Formatting.None);
            var json = System.Text.Encoding.UTF8.GetBytes(text);
            int padding = (4 - json.Length % 4) % 4;
            using var stream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write(0x46546c67u); writer.Write(2u); writer.Write((uint)(32 + json.Length + padding));
            writer.Write((uint)(json.Length + padding)); writer.Write(0x4e4f534au); writer.Write(json);
            for (int i = 0; i < padding; i++) writer.Write((byte)32);
            writer.Write(4u); writer.Write(0x004e4942u); writer.Write(0u);
            var bytes = stream.ToArray();
            var result = UniVRM10.Vrm10.LoadBytesAsync(bytes, controlRigGenerationOption: UniVRM10.ControlRigGenerationOption.None, awaitCaller: new UniGLTF.ImmediateCaller()).GetAwaiter().GetResult();
            imported = result.gameObject;
            var animator = imported.GetComponent<Animator>();
            Require(animator != null && animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid, "VRM 1 runtime import uses its own valid Humanoid");
            Debug.Log("MANGOS_VRM_RUNTIME_TEST PASSED: synthetic VRM 1.0 metadata, raw skeleton, valid Animator");
        }
        finally { UnityEngine.Object.DestroyImmediate(source); if (imported != null) UnityEngine.Object.DestroyImmediate(imported); }
    }
    private static GameObject MakeRig(float scale)
    {
        var root = new GameObject("SyntheticHumanoid");
        var bones = new Dictionary<string, Transform> { ["Root"] = root.transform };
        void Add(string name, string parent, Vector3 offset)
        {
            var node = new GameObject("mixamorig:" + name).transform;
            node.SetParent(bones[parent], false); node.localPosition = offset * scale; bones[name] = node;
        }
        Add("Hips", "Root", new Vector3(0, 1, 0));
        Add("Spine", "Hips", new Vector3(0, .2f, 0)); Add("Chest", "Spine", new Vector3(0, .2f, 0));
        Add("Neck", "Chest", new Vector3(0, .2f, 0)); Add("Head", "Neck", new Vector3(0, .2f, 0));
        foreach (var side in new[] { "Left", "Right" })
        {
            float sign = side == "Left" ? 1 : -1;
            Add(side + "Arm", "Chest", new Vector3(sign * .2f, .1f, 0));
            Add(side + "ForeArm", side + "Arm", new Vector3(sign * .3f, 0, 0));
            Add(side + "Hand", side + "ForeArm", new Vector3(sign * .25f, 0, 0));
            Add(side + "UpLeg", "Hips", new Vector3(sign * .1f, -.1f, 0));
            Add(side + "Leg", side + "UpLeg", new Vector3(0, -.4f, 0));
            Add(side + "Foot", side + "Leg", new Vector3(0, -.4f, .05f));
        }
        return root;
    }
}
