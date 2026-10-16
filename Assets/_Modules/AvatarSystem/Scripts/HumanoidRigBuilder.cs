using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public static class HumanoidRigBuilder
{
    public static Avatar Build(GameObject model, AvatarRigProfile profile = null)
    {
        var nodes = model.GetComponentsInChildren<Transform>(true);
        var mappings = new List<Dictionary<string, Transform>>();
        if (profile != null)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var bone in profile.bones)
            {
                var node = model.transform.Find(bone.nodePath);
                if (node != null) map[HumanTrait.BoneName[(int)bone.humanBone]] = node;
            }
            mappings.Add(map);
        }
        else
        {
            // Old Avatar assets contribute bone names only. Their rest pose,
            // proportions and Avatar object are never assigned to the new model.
            foreach (var reference in Resources.LoadAll<Avatar>("AvatarLoader"))
            {
                if (reference == null || !reference.isHuman || !reference.isValid) continue;
                var map = new Dictionary<string, Transform>();
                foreach (var human in reference.humanDescription.human)
                {
                    var matches = nodes.Where(n => n.name == human.boneName).ToArray();
                    if (matches.Length == 1) map[human.humanName] = matches[0];
                }
                mappings.Add(map);
            }
            var automatic = new Dictionary<string, Transform>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                var aliases = Aliases((HumanBodyBones)i);
                var matches = nodes.Where(n => aliases.Contains(Normalize(n.name))).ToArray();
                if (matches.Length == 1) automatic[HumanTrait.BoneName[i]] = matches[0];
            }
            mappings.Add(automatic);
        }
        var required = Enumerable.Range(0, HumanTrait.BoneCount).Where(HumanTrait.RequiredBone).Select(i => HumanTrait.BoneName[i]).ToArray();
        var selected = mappings.OrderByDescending(m => required.Count(m.ContainsKey)).ThenByDescending(m => m.Count).FirstOrDefault();
        var missing = required.Where(n => selected == null || !selected.ContainsKey(n)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("Humanoid mapping is incomplete: " + string.Join(", ", missing) + ". Provide an Avatar Rig Profile or a valid VRM.");
        if (selected.Values.Distinct().Count() != selected.Count) throw new InvalidOperationException("One bone was mapped to multiple humanoid roles.");
        foreach (var node in selected.Values)
            if (nodes.Count(n => n.name == node.name) != 1) throw new InvalidOperationException("Ambiguous bone name: " + node.name);
        CalibrateTPose(model.transform, selected);
        var description = new HumanDescription {
            human = selected.Select(pair => new HumanBone { humanName = pair.Key, boneName = pair.Value.name, limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
            skeleton = nodes.Select(n => new SkeletonBone { name = n.name, position = n.localPosition, rotation = n.localRotation, scale = n.localScale }).ToArray(),
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0, legStretch = 0, feetSpacing = 0, hasTranslationDoF = false
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(model, description);
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            if (avatar != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(avatar);
                else UnityEngine.Object.DestroyImmediate(avatar);
            }
            throw new InvalidOperationException("The model's hierarchy/rest pose could not form a valid Humanoid Avatar. Check T-pose, parent axes and skinning.");
        }
        avatar.name = model.name + "_OwnHumanoid";
        return avatar;
    }
    private static void CalibrateTPose(Transform root, Dictionary<string, Transform> bones)
    {
        Transform Bone(HumanBodyBones role) => bones.TryGetValue(HumanTrait.BoneName[(int)role], out var bone) ? bone : null;
        var hips = Bone(HumanBodyBones.Hips);
        var head = Bone(HumanBodyBones.Head);
        var leftArm = Bone(HumanBodyBones.LeftUpperArm);
        var rightArm = Bone(HumanBodyBones.RightUpperArm);
        var up = (head.position - hips.position).normalized;
        var right = Vector3.ProjectOnPlane(rightArm.position - leftArm.position, up).normalized;
        if (up.sqrMagnitude < .5f || right.sqrMagnitude < .5f) throw new InvalidOperationException("Cannot determine the humanoid's up/right axes.");
        var correction = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
        // Rotate mesh and skeleton together. Skin bind matrices remain unchanged.
        foreach (Transform child in root)
        {
            child.localPosition = correction * child.localPosition;
            child.localRotation = correction * child.localRotation;
        }
        void Align(HumanBodyBones parent, HumanBodyBones child, Vector3 direction)
        {
            var from = Bone(parent); var to = Bone(child);
            if (from == null || to == null) return;
            var vector = to.position - from.position;
            if (vector.sqrMagnitude < 1e-8f) throw new InvalidOperationException("Zero-length humanoid segment: " + parent);
            from.rotation = Quaternion.FromToRotation(vector.normalized, direction) * from.rotation;
        }
        // AvatarBuilder expects a T-pose; many GLBs are exported in an A/relaxed pose.
        // Rotate joints into calibration pose, never alter bone lengths or skin weights.
        Align(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, Vector3.left);
        Align(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, Vector3.left);
        Align(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, Vector3.right);
        Align(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, Vector3.right);
        Align(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, Vector3.down);
        Align(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, Vector3.down);
        Align(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, Vector3.down);
        Align(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, Vector3.down);
    }
    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant().Replace("mixamorig:", "").Replace("mixamorig", ""), "[^a-z0-9]", "");
    private static HashSet<string> Aliases(HumanBodyBones bone)
    {
        var role = bone.ToString();
        var values = new HashSet<string> { Normalize(role), Normalize(HumanTrait.BoneName[(int)bone]) };
        if (bone == HumanBodyBones.Hips) values.UnionWith(new[] { "pelvis", "phhips", "jbipchips" });
        if (bone == HumanBodyBones.Chest) values.UnionWith(new[] { "spine1", "spine01" });
        if (bone == HumanBodyBones.UpperChest) values.UnionWith(new[] { "spine2", "spine02" });
        foreach (var side in new[] { "Left", "Right" })
        {
            if (!role.StartsWith(side, StringComparison.Ordinal)) continue;
            var suffix = side == "Left" ? "l" : "r";
            var part = role.Substring(side.Length);
            values.Add(Normalize(part + suffix));
            values.Add(Normalize(suffix + part));
            string alternate = part == "UpperArm" ? "Arm" : part == "LowerArm" ? "ForeArm" : part == "UpperLeg" ? "UpLeg" : part == "LowerLeg" ? "Leg" : part == "Toes" ? "ToeBase" : part;
            values.Add(Normalize(side + alternate));
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                foreach (var segment in new[] { "Proximal", "Intermediate", "Distal" })
                    if (part == finger + segment) values.Add(Normalize(side + "Hand" + (finger == "Little" ? "Pinky" : finger) + (segment == "Proximal" ? "1" : segment == "Intermediate" ? "2" : "3")));
        }
        return values;
    }
}
