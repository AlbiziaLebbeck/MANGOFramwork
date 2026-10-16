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
