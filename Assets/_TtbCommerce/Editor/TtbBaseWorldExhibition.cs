using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Mangos.Ttb.Commerce.Editor
{
    public static class TtbBaseWorldExhibition
    {
        public const string ZoneName = "TTB commerce zone";
        public const string PreparedScenePath = "Assets/_TtbCommerce/Generated/World/BaseWorld.unity";
        private const string Generated = "Assets/_TtbCommerce/Generated/World";

        public static GameObject CreateZone(Scene scene, TtbProduct[] products)
        {
            Directory.CreateDirectory(Generated + "/Materials"); Directory.CreateDirectory(Generated + "/Meshes");
            AssetDatabase.Refresh();
            if (TMP_Settings.defaultFontAsset == null) throw new InvalidOperationException("The project TMP font is required for world signage.");
            var root = new GameObject(ZoneName); SceneManager.MoveGameObjectToScene(root, scene);
            var architecture = Child("Architecture", root.transform);
            var displays = Child("Collection / product bindings", root.transform);
            var furnishings = Child("Furniture and planting", root.transform);
            var lighting = Child("Gallery lighting / four additional lights", root.transform);
            var stone = Material("Limestone", new Color(.78f, .76f, .68f), 0, .24f);
            var plaster = Material("Ivory plaster", new Color(.88f, .86f, .79f), 0, .16f);
            var green = Material("Forest green", new Color(.075f, .17f, .135f), .05f, .3f);
            var brass = Material("Brushed brass", new Color(.62f, .43f, .19f), .6f, .42f);
            var oak = Material("Smoked oak", new Color(.32f, .20f, .12f), 0, .25f);
            var leaf = Material("Mango foliage", new Color(.16f, .29f, .18f), 0, .18f);
            leaf.SetFloat("_Cull", 0); EditorUtility.SetDirty(leaf);
            var grout = Material("Stone joints", new Color(.70f, .68f, .60f), 0, .14f);
            var glow = Material("Warm light strips", new Color(1, .85f, .58f), 0, .2f);
            glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", new Color(1.3f, .9f, .46f)); EditorUtility.SetDirty(glow);

            // The top is above the original BaseWorld ground, avoiding the old checkerboard/z-fighting.
            Box("Continuous stone platform", architecture, new Vector3(0, -.06f, 0), new Vector3(20, .2f, 20), stone, true);
            Box("Back plaster wall", architecture, new Vector3(0, 2.32f, 6.35f), new Vector3(16.4f, 4.56f, .3f), plaster, true);
            foreach (var side in new[] { -1, 1 })
            {
                Box("Side gallery wall", architecture, new Vector3(side * 8.05f, 2.32f, .25f), new Vector3(.25f, 4.56f, 12.5f), plaster, true);
                Box("Stone border", architecture, new Vector3(side * 7.78f, .052f, .15f), new Vector3(.10f, .012f, 12.4f), green);
                Box("Brass floor inlay", architecture, new Vector3(side * 7.60f, .055f, .15f), new Vector3(.018f, .014f, 12.4f), brass);
                Box("Entrance pier", architecture, new Vector3(side * 4.55f, 2.32f, -5.85f), new Vector3(.32f, 4.56f, .36f), green, true);
                Box("Roof edge", architecture, new Vector3(side * 7.95f, 4.58f, .15f), new Vector3(.18f, .18f, 12.7f), oak);
                for (var index = 0; index < 9; index++)
                    Box("Wall fluting", architecture, new Vector3(side * 7.87f, 2.30f, -4.8f + index * 1.15f), new Vector3(.08f, 4.4f, .07f), stone);
                Bench(furnishings, new Vector3(side * 5.45f, .04f, -1.65f), oak, green, brass);
                Plant(furnishings, new Vector3(side * 6.55f, .04f, 4.25f), plaster, leaf);
                Plant(furnishings, new Vector3(side * 6.55f, .04f, -4.65f), plaster, leaf);
            }
            Box("Entrance lintel", architecture, new Vector3(0, 4.58f, -5.85f), new Vector3(9.42f, .22f, .36f), green);
            Box("Entry threshold", architecture, new Vector3(0, .055f, -5.72f), new Vector3(8.8f, .014f, .055f), brass);
            Box("Back roof edge", architecture, new Vector3(0, 4.58f, 6.35f), new Vector3(16.2f, .18f, .24f), oak);
            for (var index = 0; index < 7; index++)
            {
                var z = -4.9f + index * 1.65f;
                Box("Open skylight crossbeam", architecture, new Vector3(0, 4.60f, z), new Vector3(15.9f, .12f, .10f), oak);
                if (index % 2 == 0) Box("Warm ceiling ribbon", architecture, new Vector3(0, 4.525f, z), new Vector3(7.2f, .028f, .045f), glow);
            }
            for (var index = -3; index <= 3; index++)
                Box("Fine stone joint", architecture, new Vector3(index * 2.0f, .046f, .15f), new Vector3(.013f, .006f, 12.3f), grout);
            Box("Arrival floor inset", architecture, new Vector3(0, .049f, -3.6f), new Vector3(3.8f, .012f, 3.25f), green);
            Box("Arrival inset brass edge", architecture, new Vector3(0, .058f, -5.23f), new Vector3(3.8f, .012f, .025f), brass);

            var keys = new[] { "orbit-lamp", "mango-sculpture", "mango-star" };
            var captions = new[] { "ORBIT / 01", "MANGO / 02", "STAR / 03" };
            for (var index = 0; index < keys.Length; index++)
            {
                var x = (index - 1) * 3f;
                var niche = Child("Arched collection niche " + (index + 1), architecture);
                Arch("Niche stone surround", niche, new Vector3(x, .12f, 6.15f), 2.65f, 3.72f, stone);
                Arch("Fine brass reveal", niche, new Vector3(x, .16f, 6.115f), 2.47f, 3.57f, brass);
                Arch("Green arched recess", niche, new Vector3(x, .20f, 6.08f), 2.40f, 3.48f, green);
                for (var flute = -4; flute <= 4; flute++)
                    Box("Niche vertical relief", niche, new Vector3(x + flute * .21f, 1.65f, 6.055f), new Vector3(.022f, 2.80f, .035f), oak);
                Cylinder("Brass plinth foot", displays, new Vector3(x, .09f, 3), 2.04f, .10f, brass, true);
                Cylinder("Ivory display plinth", displays, new Vector3(x, .64f, 3), 1.94f, 1.02f, plaster, true);
                Cylinder("Stone display top", displays, new Vector3(x, 1.13f, 3), 2.02f, .04f, stone);
                var source = products.Single(product => product.ModelKey == keys[index]);
                var copy = UnityEngine.Object.Instantiate(source.gameObject, displays, false); copy.name = source.name;
                copy.transform.localPosition = new Vector3(x, 1.15f, 3); copy.transform.localRotation = Quaternion.identity;
                if (copy.GetComponent<TtbProduct>()?.PreviewModel == null || copy.GetComponent<Collider>() == null)
                    throw new InvalidOperationException("An exhibition model lost its native preview or product collider.");
                Box("Product label plate", displays, new Vector3(x, .76f, 2.02f), new Vector3(1.28f, .28f, .025f), green);
                Label(captions[index], displays, new Vector3(x, .81f, 1.998f), 1.35f, 1.18f, new Color(.94f, .90f, .78f));
                Label(index == 2 ? "DIGITAL COLLECTIBLE" : "PHYSICAL OBJECT", displays, new Vector3(x, .68f, 1.995f), .70f, 1.18f, new Color(.75f, .77f, .64f));
                Spotlight(lighting, "Collection spotlight " + (index + 1), new Vector3(x, 4.35f, 1.8f), new Vector3(x, 1.6f, 3), 6.5f, 40);
            }
            Label("MANGOs", architecture, new Vector3(0, 4.12f, 6.05f), 7.0f, 6.5f, new Color(.08f, .18f, .14f));
            Label("OBJECTS  /  COLLECTIBLES  /  EXPERIENCES", architecture, new Vector3(0, 3.83f, 6.04f), 1.30f, 7.8f, new Color(.30f, .34f, .27f));
            var entranceLettering = Child("Entrance lettering", architecture);
            Label("THE MANGO GALLERY", entranceLettering, new Vector3(0, 4.14f, -6.05f), 3.1f, 7.8f, new Color(.92f, .88f, .73f));
            Label("EXPLORE THE COLLECTION", entranceLettering, new Vector3(0, 3.78f, -6.06f), 1.15f, 7.8f, new Color(.78f, .79f, .64f));
            var fill = Child("Warm room fill", lighting).gameObject.AddComponent<Light>();
            fill.type = LightType.Point; fill.transform.localPosition = new Vector3(0, 3.7f, -1.8f);
            fill.color = new Color(1, .88f, .70f); fill.intensity = 2.4f; fill.range = 13; fill.shadows = LightShadows.None; fill.cullingMask = ~(1 << 31);
            var sky = Skybox(); root.AddComponent<TtbWorldAtmosphere>().Configure(sky);
            return root;
        }

        public static string PrepareBuildScene(string originalPath)
        {
            if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Replace('\\', '/').Contains("/Builds/TTB/BuildWorkspace/"))
                throw new InvalidOperationException("Prepare the full-world scene only in the isolated build workspace.");
            var settings = Resources.Load<TtbWorldCommerceSettings>("TtbWorldCommerce");
            if (settings == null || settings.zone == null || settings.zone.GetComponent<TtbWorldAtmosphere>() == null)
                throw new InvalidOperationException("Generate the current gallery/product assets before preparing BaseWorld.");
            var scene = EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            var collision = scene.GetRootGameObjects().FirstOrDefault(root => root.name == ZoneName);
            if (collision != null && collision.GetComponent<TtbWorldAtmosphere>() == null)
                throw new InvalidOperationException("An unrelated root owns the exhibition name; preserve it for review.");
            if (collision != null) UnityEngine.Object.DestroyImmediate(collision);
            var zone = (GameObject)PrefabUtility.InstantiatePrefab(settings.zone, scene); zone.name = ZoneName; zone.transform.position = settings.position;
            if (!scene.GetRootGameObjects().Any(root => root.GetComponent<MangoTtbCommerceBridge>() != null))
            {
                var bridge = new GameObject("MangoTtbCommerceBridge"); SceneManager.MoveGameObjectToScene(bridge, scene);
                bridge.AddComponent<MangoTtbCommerceBridge>(); bridge.AddComponent<TtbProductInteractor>();
            }
            SceneManager.SetActiveScene(scene); TtbWorldAtmosphere.ApplyStyle(zone.GetComponent<TtbWorldAtmosphere>().Skybox);
            Directory.CreateDirectory(Path.GetDirectoryName(PreparedScenePath));
            if (!EditorSceneManager.SaveScene(scene, PreparedScenePath)) throw new InvalidOperationException("Prepared BaseWorld could not be saved.");
            // Save-as changes FishNet's scene-path hash. Generate IDs before BuildPipeline validates them.
            FishNet.Editing.RebuildSceneIdMenu.RebuildSceneIds();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Prepared BaseWorld network IDs could not be saved.");
            AssetDatabase.SaveAssets();
            if (zone.GetComponentsInChildren<TtbProduct>(true).Length != 3 || zone.GetComponentsInChildren<Light>(true).Length != 4)
                throw new InvalidOperationException("BaseWorld exhibition bindings/light budget changed unexpectedly.");
            Debug.Log("TTB_BASEWORLD_PREPARED: arched gallery;3bound products;4additional lights; scene=" + PreparedScenePath);
            return PreparedScenePath;
        }

        public static string ReuseBuildScene(string fingerprint)
        {
            if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Replace('\\', '/').Contains("/Builds/TTB/BuildWorkspace/"))
                throw new InvalidOperationException("Reuse the prepared scene only in the isolated build workspace.");
            var actual = FileHash(PreparedScenePath) + ":" + FileHash(PreparedScenePath + ".meta");
            if (!string.Equals(actual, fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Prepared BaseWorld differs from the scene used for the paired server build.");
            var scene = EditorSceneManager.OpenScene(PreparedScenePath, OpenSceneMode.Single);
            var zone = scene.GetRootGameObjects().Single(root => root.name == ZoneName);
            if (zone.GetComponent<TtbWorldAtmosphere>() == null || zone.GetComponentsInChildren<TtbProduct>(true).Length != 3 ||
                zone.GetComponentsInChildren<Light>(true).Length != 4)
                throw new InvalidOperationException("The paired BaseWorld exhibition is incomplete.");
            Debug.Log("TTB_BASEWORLD_REUSED: paired server scene and metadata hashes match; network IDs preserved");
            return PreparedScenePath;
        }

        private static string FileHash(string path)
        {
            using var input = File.OpenRead(path); using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
        }

        private static Transform Child(string name, Transform parent)
        {
            var item = new GameObject(name); item.transform.SetParent(parent, false); return item.transform;
        }
        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision = false)
            => Primitive(name, PrimitiveType.Cube, parent, position, scale, material, collision);
        private static void Cylinder(string name, Transform parent, Vector3 position, float diameter, float height, Material material, bool collision = false)
            => Primitive(name, PrimitiveType.Cylinder, parent, position, new Vector3(diameter, height * .5f, diameter), material, collision);
        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision)
        {
            var item = GameObject.CreatePrimitive(type); item.name = name; item.transform.SetParent(parent, false);
            item.transform.localPosition = position; item.transform.localScale = scale; item.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(item, StaticEditorFlags.BatchingStatic); return item;
        }
        private static Material Material(string name, Color color, float metallic, float smoothness)
        {
            var path = Generated + "/Materials/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("The authored exhibition requires the project's URP Lit shader.");
                material = new Material(shader); AssetDatabase.CreateAsset(material, path);
            }
            material.color = color; material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
        private static Material Skybox()
        {
            var path = Generated + "/Materials/Gallery daylight sky.mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Skybox/Procedural");
                if (shader == null) throw new InvalidOperationException("The scene daylight sky shader is unavailable.");
                material = new Material(shader); AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_SkyTint", new Color(.65f, .74f, .71f)); material.SetColor("_GroundColor", new Color(.52f, .55f, .47f));
            material.SetFloat("_AtmosphereThickness", 1.1f); material.SetFloat("_Exposure", .9f); EditorUtility.SetDirty(material); return material;
        }
        private static void Arch(string name, Transform parent, Vector3 position, float width, float height, Material material)
        {
            var path = Generated + "/Meshes/Arch " + width.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                var outline = new List<Vector3> { new Vector3(-width / 2, 0, 0), new Vector3(width / 2, 0, 0) };
                for (var step = 0; step <= 24; step++)
                {
                    var angle = step * Mathf.PI / 24;
                    outline.Add(new Vector3(Mathf.Cos(angle) * width * .5f, height - width * .5f + Mathf.Sin(angle) * width * .5f, 0));
                }
                var vertices = new[] { new Vector3(0, height * .45f, 0) }.Concat(outline).ToArray();
                var triangles = new List<int>();
                for (var index = 0; index < outline.Count; index++) { triangles.Add(0); triangles.Add((index + 1) % outline.Count + 1); triangles.Add(index + 1); }
                mesh = new Mesh { name = name, vertices = vertices, triangles = triangles.ToArray() }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
            }
            var item = Child(name, parent); item.localPosition = position;
            item.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; item.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(item.gameObject, StaticEditorFlags.BatchingStatic);
        }
        private static void Label(string value, Transform parent, Vector3 position, float size, float width, Color color)
        {
            var item = Child("World lettering / " + value, parent); item.localPosition = position;
            var text = item.gameObject.AddComponent<TextMeshPro>(); text.font = TMP_Settings.defaultFontAsset; text.text = value;
            text.fontSize = size; text.alignment = TextAlignmentOptions.Center; text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(width, 1); text.raycastTarget = false;
        }
        private static void Spotlight(Transform parent, string name, Vector3 position, Vector3 target, float intensity, float angle)
        {
            var item = Child(name, parent); item.localPosition = position; item.localRotation = Quaternion.LookRotation(target - position);
            var light = item.gameObject.AddComponent<Light>(); light.type = LightType.Spot; light.color = new Color(1, .89f, .72f);
            light.intensity = intensity; light.range = 8; light.spotAngle = angle; light.innerSpotAngle = angle * .65f;
            light.shadows = LightShadows.None; light.cullingMask = ~(1 << 31);
        }
        private static void Bench(Transform parent, Vector3 position, Material oak, Material green, Material brass)
        {
            var root = Child("Gallery bench", parent); root.localPosition = position;
            Box("Oak bench", root, new Vector3(0, .43f, 0), new Vector3(2.1f, .14f, .76f), oak, true);
            Box("Green seat", root, new Vector3(0, .54f, 0), new Vector3(1.96f, .09f, .66f), green);
            foreach (var x in new[] { -.76f, .76f }) Box("Brass bench support", root, new Vector3(x, .20f, 0), new Vector3(.08f, .4f, .58f), brass);
        }
        private static void Plant(Transform parent, Vector3 position, Material pot, Material leaf)
        {
            var root = Child("Mango planting", parent); root.localPosition = position;
            Cylinder("Ceramic planter", root, new Vector3(0, .34f, 0), .78f, .68f, pot, true);
            var path = Generated + "/Meshes/Mango leaf.asset"; var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (var index = 0; index <= 8; index++)
                {
                    var t = index / 8f; var width = Mathf.Sin(t * Mathf.PI) * .22f;
                    var center = new Vector3(.65f * t * t, 1.5f * t, 0);
                    vertices.Add(center + new Vector3(0, 0, -width)); vertices.Add(center + new Vector3(0, .04f * Mathf.Sin(t * Mathf.PI), width));
                    if (index == 0) continue;
                    var a = (index - 1) * 2; triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
                mesh = new Mesh { name = "Mango leaf", vertices = vertices.ToArray(), triangles = triangles.ToArray() }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
            }
            for (var index = 0; index < 11; index++)
            {
                var item = Child("Sculpted mango leaf", root); item.localPosition = new Vector3(0, .58f, 0);
                item.localRotation = Quaternion.Euler(0, index * 137.5f, 10 + index % 3 * 9);
                item.localScale = Vector3.one * (.72f + index % 4 * .12f);
                item.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; item.gameObject.AddComponent<MeshRenderer>().sharedMaterial = leaf;
                GameObjectUtility.SetStaticEditorFlags(item.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
