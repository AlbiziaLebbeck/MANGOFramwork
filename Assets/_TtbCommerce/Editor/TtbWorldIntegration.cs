using System;
using System.IO;
using System.Linq;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mangos.Ttb.Commerce.Editor
{
    public static class TtbWorldIntegration
    {
        public static void Prepare(Scene gallery)
        {
            Directory.CreateDirectory("Assets/_TtbCommerce/Resources"); AssetDatabase.Refresh();
            var products = gallery.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<TtbProduct>()).ToArray();
            var star = products.Single(x => x.ModelKey == "mango-star");
            var geometry = UnityEngine.Object.Instantiate(star.PreviewModel.gameObject);
            var itemPath = "Assets/_TtbCommerce/Generated/MangoStarItem.prefab";
            var itemPrefab = PrefabUtility.SaveAsPrefabAsset(geometry, itemPath); UnityEngine.Object.DestroyImmediate(geometry);
            if (itemPrefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || itemPrefab.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("A game item must contain only render geometry.");
            var models = Asset<TtbGameModelRegistry>("Assets/_TtbCommerce/Resources/TtbGameModels.asset");
            models.entries = new[] { new TtbGameModelRegistry.Entry { modelKey = "mango-star", prefab = itemPrefab } }; EditorUtility.SetDirty(models);
            var zone = TtbBaseWorldExhibition.CreateZone(gallery, products);
            var zonePrefab = PrefabUtility.SaveAsPrefabAsset(zone, "Assets/_TtbCommerce/Generated/TtbCommerceZone.prefab"); UnityEngine.Object.DestroyImmediate(zone);
            var settings = Asset<TtbWorldCommerceSettings>("Assets/_TtbCommerce/Resources/TtbWorldCommerce.asset");
            settings.zone = zonePrefab; settings.position = new Vector3(0, 0, 8); EditorUtility.SetDirty(settings);
            foreach (var path in new[] { "Assets/_Modules/Networking/Prefabs/NetworkPlayer.prefab", "Assets/_Modules/Networking/Prefabs/NetworkPlayer_Flyable.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<NetworkObject>() == null) throw new InvalidOperationException("The player is missing its root NetworkObject.");
                    if (root.GetComponent<TtbGameRelay>() == null) root.AddComponent<TtbGameRelay>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("TTB_WORLD_INTEGRATION_PREPARED: BaseWorld scene-owned zone; both existing player types have the same trusted relay");
        }
        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
    }
}
