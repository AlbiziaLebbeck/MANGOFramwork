using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mangos.Ttb.Commerce.Editor
{
    public static class TtbGalleryBuild
    {
        private const string Generated = "Assets/_TtbCommerce/Generated";
        private const string ScenePath = Generated + "/TtbGallery.unity";
        [MenuItem("MANGOs Framework/TTB/Create isolated gallery scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory(Generated); AssetDatabase.Refresh();
            // Batch builds run in the isolated checkout. Interactive use preserves the user's open scenes.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var ceramic = Material("Terracotta", new Color(.72f, .27f, .1f), .05f, .25f);
            var ivory = Material("Warm inner light", new Color(1, .84f, .57f), 0, .3f);
            ivory.EnableKeyword("_EMISSION"); ivory.SetColor("_EmissionColor", new Color(.8f, .55f, .25f)); EditorUtility.SetDirty(ivory);
            var oak = Material("Oak", new Color(.63f, .43f, .24f), 0, .22f);
            var gold = Material("Gold", new Color(.88f, .61f, .17f), .5f, .5f);
            var stone = Material("Pale stone", new Color(.78f, .77f, .72f), 0, .2f);
            var floor = Material("Gallery floor", new Color(.78f, .79f, .76f), 0, .28f);
            var wall = Material("Gallery plaster", new Color(.88f, .88f, .84f), 0, .15f);
            var torus = MeshAsset("Orbit torus", TtbDemoMeshes.Torus()); var sculpture = MeshAsset("Wood sculpture", TtbDemoMeshes.Sculpture()); var star = MeshAsset("Faceted star", TtbDemoMeshes.Star());
            Primitive("Gallery floor", PrimitiveType.Cube, new Vector3(0, -.12f, 1), new Vector3(22, .2f, 22), floor);
            Primitive("Gallery back wall", PrimitiveType.Cube, new Vector3(0, 3, 6), new Vector3(20, 6, .25f), wall);
            Primitive("Gallery left wing", PrimitiveType.Cube, new Vector3(-8, 3, 0), new Vector3(.25f, 6, 14), wall);
            Primitive("Gallery right wing", PrimitiveType.Cube, new Vector3(8, 3, 0), new Vector3(.25f, 6, 14), wall);
            var lamp = Product("Orbit Lamp", "orbit-lamp", new Vector3(-3, 1.15f, 3));
            Part("Lamp ring", lamp.transform.GetChild(0), torus, new Vector3(0, .96f, 0), Quaternion.Euler(0, 15, 0), ceramic, ivory);
            var lampBase = Primitive("Lamp oval base", PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.1f, .08f, .72f), ceramic); lampBase.transform.SetParent(lamp.transform.GetChild(0), false); lampBase.transform.localPosition = new Vector3(0, .08f, 0); RemoveCollider(lampBase);
            var wood = Product("Mango Sculpture", "mango-sculpture", new Vector3(0, 1.15f, 3));
            Part("Carved mango", wood.transform.GetChild(0), sculpture, new Vector3(0, .17f, 0), Quaternion.Euler(0, -20, 0), oak);
            var woodBase = Primitive("Sculpture stone base", PrimitiveType.Cube, Vector3.zero, new Vector3(.9f, .17f, .65f), stone); woodBase.transform.SetParent(wood.transform.GetChild(0), false); woodBase.transform.localPosition = new Vector3(0, .085f, 0); RemoveCollider(woodBase);
            var collectible = Product("Mango Star", "mango-star", new Vector3(3, 1.15f, 3));
            Part("Star collectible", collectible.transform.GetChild(0), star, new Vector3(0, .65f, 0), Quaternion.Euler(0, -12, 0), gold);
            foreach (var product in new[] { lamp, wood, collectible })
            {
                Primitive(product.name + " plinth", PrimitiveType.Cube, new Vector3(product.transform.position.x, .56f, 3), new Vector3(1.9f, 1.1f, 1.7f), stone);
                var renderers = product.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var collider = product.AddComponent<BoxCollider>(); collider.center = bounds.center - product.transform.position; collider.size = bounds.size + Vector3.one * .05f;
            }
            var sign = new GameObject("Gallery wordmark"); sign.transform.position = new Vector3(0, 4, 5.8f); sign.transform.localScale = Vector3.one * .08f;
            var text = sign.AddComponent<TextMesh>(); text.text = "MANGOs"; text.fontSize = 80; text.characterSize = .7f; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = new Color(.08f, .18f, .14f);
            var lightObject = new GameObject("Gallery daylight"); lightObject.transform.rotation = Quaternion.Euler(48, -30, 0); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.cullingMask = ~(1 << 31); light.shadows = LightShadows.Soft; RenderSettings.sun = light;
            var fillObject = new GameObject("Gallery window fill"); fillObject.transform.position = new Vector3(-2, 4, -3); var fill = fillObject.AddComponent<Light>(); fill.type = LightType.Point; fill.intensity = 4; fill.range = 18;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.4f, .42f, .43f); RenderSettings.fog = false;
            var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera"; var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.89f, .9f, .88f); camera.fieldOfView = 42; camera.farClipPlane = 100; cameraObject.AddComponent<AudioListener>(); cameraObject.AddComponent<TtbGalleryCamera>();
            var bridge = new GameObject("MangoTtbCommerceBridge"); bridge.AddComponent<MangoTtbCommerceBridge>(); bridge.AddComponent<TtbProductInteractor>();
            TtbWorldIntegration.Prepare(scene);
            // Preview the same authored exhibition used in BaseWorld, retaining this gallery's camera/HTML bridge.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Camera>() != null || root.GetComponent<MangoTtbCommerceBridge>() != null || root == lightObject) continue;
                UnityEngine.Object.DestroyImmediate(root);
            }
            var settings = Resources.Load<TtbWorldCommerceSettings>("TtbWorldCommerce");
            var exhibition = (GameObject)PrefabUtility.InstantiatePrefab(settings.zone, scene);
            exhibition.name = TtbBaseWorldExhibition.ZoneName; exhibition.transform.position = Vector3.zero;
            // The inspection camera sits close to the front lintel; its large exterior lettering is for world visitors.
            exhibition.transform.Find("Architecture/Entrance lettering").gameObject.SetActive(false);
            TtbWorldAtmosphere.ApplyStyle(exhibition.GetComponent<TtbWorldAtmosphere>().Skybox);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            Debug.Log("TTB_GALLERY_CREATED: BaseWorld exhibition preview, native models, HTML bridge; original scene list unchanged");
        }
        public static void BuildLocal()
        {
            CreateScene(); var originalTemplate = PlayerSettings.WebGL.template; var originalCompression = PlayerSettings.WebGL.compressionFormat;
            try
            {
                PlayerSettings.WebGL.template = "PROJECT:MANGOsTTB"; PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                var output = Path.GetFullPath("Builds/TTB/LocalGallery"); Directory.CreateDirectory(output);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output, target = BuildTarget.WebGL, options = BuildOptions.None });
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0) throw new InvalidOperationException("TTB WebGL build failed; inspect the full Editor log.");
                Debug.Log("TTB_WEBGL_BUILD_PASSED: " + output);
            }
            finally { PlayerSettings.WebGL.template = originalTemplate; PlayerSettings.WebGL.compressionFormat = originalCompression; }
        }
        private static GameObject Product(string name, string key, Vector3 position)
        {
            var root = new GameObject(name); root.transform.position = position; var geometry = new GameObject("Product geometry"); geometry.transform.SetParent(root.transform, false); root.AddComponent<TtbProduct>().Configure(key, geometry.transform); return root;
        }
        private static void Part(string name, Transform parent, Mesh mesh, Vector3 position, Quaternion rotation, params Material[] materials)
        {
            var part = new GameObject(name); part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localRotation = rotation;
            part.AddComponent<MeshFilter>().sharedMesh = mesh; part.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }
        private static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(type); item.name = name; item.transform.position = position; item.transform.localScale = scale; item.GetComponent<Renderer>().sharedMaterial = material; return item;
        }
        private static void RemoveCollider(GameObject item) { var collider = item.GetComponent<Collider>(); if (collider != null) UnityEngine.Object.DestroyImmediate(collider); }
        private static Material Material(string name, Color color, float metallic, float smoothness)
        {
            var path = Generated + "/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.color = color; material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smoothness); EditorUtility.SetDirty(material); return material;
        }
        private static Mesh MeshAsset(string name, Mesh generated)
        {
            if (generated.vertices.Any(v => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)))
                throw new InvalidOperationException("Generated mesh contains invalid coordinates: " + name);
            var path = Generated + "/" + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(generated, existing); UnityEngine.Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(generated, path); return generated;
        }
    }
}
