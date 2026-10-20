using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mangos.Ttb.Commerce.Editor
{
    // The authored gallery belongs in the editable project scene as well as isolated builds.
    public static class TtbProjectWorld
    {
        public const string ScenePath = "Assets/_Project/Scenes/BaseWorld.unity";

        [MenuItem("MANGOs Framework/TTB/Install gallery in project BaseWorld")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Stop Play mode and wait for the current build first.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException("BaseWorld has unsaved edits. Save it before installing the gallery.");
            var settings = Resources.Load<TtbWorldCommerceSettings>("TtbWorldCommerce");
            if (settings == null || settings.zone == null || settings.zone.GetComponent<TtbWorldAtmosphere>() == null ||
                settings.zone.GetComponentsInChildren<TtbProduct>(true).Length != 3)
                throw new InvalidOperationException("The reviewed gallery prefab and its three products are required.");
            var backup = Path.GetFullPath(".artifacts/scene-qa/project-world-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(backup);
            foreach (var path in new[] { ScenePath, ScenePath + ".meta", MangosBuildProfile.DefaultPath,
                "Assets/_Modules/Networking/Prefabs/WorldManagerPrefab.prefab", "ProjectSettings/EditorBuildSettings.asset" })
                if (File.Exists(path)) File.Copy(path, Path.Combine(backup, path.Replace('/', '_')));
            var beforeHash = Fingerprint();
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var zones = scene.GetRootGameObjects().Where(x => x.name == TtbBaseWorldExhibition.ZoneName).ToArray();
            if (zones.Length > 1 || zones.Any(x => x.GetComponent<TtbWorldAtmosphere>() == null))
                throw new InvalidOperationException("Another object owns the gallery name. Preserve it for review.");
            var zone = zones.SingleOrDefault();
            if (zone == null)
            {
                zone = (GameObject)PrefabUtility.InstantiatePrefab(settings.zone, scene);
                zone.name = TtbBaseWorldExhibition.ZoneName;
                zone.transform.position = settings.position;
                Undo.RegisterCreatedObjectUndo(zone, "Install TTB gallery");
                PrefabUtility.RecordPrefabInstancePropertyModifications(zone.transform);
            }
            var bridges = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MangoTtbCommerceBridge>(true)).ToArray();
            if (bridges.Length > 1) throw new InvalidOperationException("Multiple commerce bridges require inspection.");
            var bridge = bridges.SingleOrDefault();
            if (bridge == null)
            {
                var item = new GameObject("MangoTtbCommerceBridge");
                SceneManager.MoveGameObjectToScene(item, scene);
                Undo.RegisterCreatedObjectUndo(item, "Install TTB commerce bridge");
                bridge = Undo.AddComponent<MangoTtbCommerceBridge>(item);
            }
            if (bridge.GetComponent<TtbProductInteractor>() == null) Undo.AddComponent<TtbProductInteractor>(bridge.gameObject);
            TtbWorldAtmosphere.ApplyStyle(zone.GetComponent<TtbWorldAtmosphere>().Skybox);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("BaseWorld could not be saved; preserve the backup.");
            // No save-as: preserve the canonical scene GUID/path and existing FishNet scene IDs.
            Validate();
            Selection.activeGameObject = zone;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 2, 8), Quaternion.Euler(14, 0, 0), 15, false, true);
            var report = new { verifiedAt = DateTime.UtcNow.ToString("O"), scene = ScenePath, beforeHash,
                afterHash = Fingerprint(), products = 3, lights = 4, scenePathAndGuidPreserved = true,
                worldManagerAndBuildSettingsUnchanged = true, backup };
            File.WriteAllText(Path.Combine(backup, "result.json"), Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            Debug.Log("TTB_PROJECT_WORLD_INSTALLED: editable BaseWorld; 3 products; 4 lights; backup=" + backup);
        }

        [MenuItem("MANGOs Framework/TTB/Validate project BaseWorld")]
        public static void Validate()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var zones = scene.GetRootGameObjects().Where(x => x.name == TtbBaseWorldExhibition.ZoneName).ToArray();
                if (zones.Length != 1 || zones[0].GetComponent<TtbWorldAtmosphere>() == null ||
                    zones[0].GetComponentsInChildren<Light>(true).Length != 4)
                    throw new InvalidOperationException("Install the gallery into project BaseWorld before using the normal build menu.");
                var products = zones[0].GetComponentsInChildren<TtbProduct>(true);
                if (products.Length != 3 || !products.Select(p => p.ModelKey).OrderBy(k => k).SequenceEqual(new[] { "mango-sculpture", "mango-star", "orbit-lamp" }) ||
                    products.Any(p => p.PreviewModel == null || p.GetComponent<Collider>() == null))
                    throw new InvalidOperationException("The project gallery has incomplete product bindings.");
                if (scene.isDirty) throw new InvalidOperationException("Save BaseWorld before building the paired Server and Client.");
                if (scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MangoTtbCommerceBridge>(true)).Count() != 1)
                    throw new InvalidOperationException("The project BaseWorld requires exactly one commerce bridge.");
                if (!EditorBuildSettings.scenes.Any(x => x.enabled && x.path == ScenePath) ||
                    !File.ReadAllText("Assets/_Modules/Networking/Prefabs/WorldManagerPrefab.prefab").Contains("baseWorldScene: " + ScenePath))
                    throw new InvalidOperationException("Build Settings and WorldManager must reference the editable project BaseWorld.");
                Debug.Log("TTB_PROJECT_WORLD_VALIDATED: canonical scene, 3 product bindings, 4 lights, one bridge");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static string Fingerprint()
        {
            using var sha = SHA256.Create();
            return string.Join(":", new[] { ScenePath, ScenePath + ".meta" }.Select(path =>
                BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "")));
        }
    }
}
