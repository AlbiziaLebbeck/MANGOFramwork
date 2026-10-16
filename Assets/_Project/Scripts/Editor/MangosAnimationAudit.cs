using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MangosAnimationAudit
{
    [MenuItem("MANGOs Framework/Avatar/Export Animation Audit")]
    public static void Export()
    {
        var lines = new List<string> { "Asset,Clip,Humanoid,Legacy,TransformPositionCurves,TransformScaleCurves,Events" };
        foreach (var path in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
            {
                var curves = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).ToArray();
                lines.Add(string.Join(",", new[] { Quote(path), Quote(clip.name), clip.isHumanMotion.ToString(), clip.legacy.ToString(),
                    curves.Count(c => c.propertyName.Contains("Position")).ToString(), curves.Count(c => c.propertyName.Contains("Scale")).ToString(),
                    Quote(string.Join(";", AnimationUtility.GetAnimationEvents(clip).Select(e => e.functionName))) }));
            }
        File.WriteAllLines("Library/MangosAnimationAudit.csv", lines);
        Debug.Log($"MANGOS_ANIMATION_AUDIT clips={lines.Count - 1} path=Library/MangosAnimationAudit.csv");
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
