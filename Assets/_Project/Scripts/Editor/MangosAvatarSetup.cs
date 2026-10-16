using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

[InitializeOnLoad]
public static class MangosAvatarSetup
{
    static MangosAvatarSetup() => EditorApplication.delayCall += Configure;
    public static void Configure()
    {
        // UniGLTF handles Editor .glb/.gltf imports. glTFast remains the runtime
        // loader for ordinary GLB; two default ScriptedImporters cannot coexist.
        foreach (var target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Server, NamedBuildTarget.WebGL })
        {
            var symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (symbols.Contains("GLTFAST_FORCE_DEFAULT_IMPORTER_OFF")) continue;
            symbols.Add("GLTFAST_FORCE_DEFAULT_IMPORTER_OFF");
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
        }
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets.Length > 0)
        {
            var settings = new SerializedObject(assets[0]);
            var shaders = settings.FindProperty("m_AlwaysIncludedShaders");
            if (shaders != null)
                foreach (var name in new[] { "VRM10/MToon10", "VRM10/Universal Render Pipeline/MToon10" })
                {
                    var shader = Shader.Find(name);
                    if (shader == null) continue;
                    bool found = false;
                    for (int i = 0; i < shaders.arraySize; i++) found |= shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                    if (!found) { int slot = shaders.arraySize; shaders.InsertArrayElementAtIndex(slot); shaders.GetArrayElementAtIndex(slot).objectReferenceValue = shader; }
                }
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        AssetDatabase.SaveAssets();
    }
}
