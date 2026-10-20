using System;
using System.Linq;
using UnityEngine;

namespace Mangos.Ttb.Commerce
{
    [CreateAssetMenu(menuName = "MANGOs/TTB/Game model registry")]
    public sealed class TtbGameModelRegistry : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string modelKey; public GameObject prefab; public Vector3 offset = new Vector3(.6f, 1.5f, 0); public float scale = .3f; }
        public Entry[] entries = Array.Empty<Entry>();
        private static TtbGameModelRegistry Registry => Resources.Load<TtbGameModelRegistry>("TtbGameModels");
        public static bool Contains(string key) => !string.IsNullOrEmpty(key) && Registry != null && Registry.entries.Count(x => x.modelKey == key && x.prefab != null) == 1;
        public static GameObject Equip(string key, Transform parent)
        {
            if (!Contains(key)) return null;
            var entry = Registry.entries.Single(x => x.modelKey == key);
            var model = Instantiate(entry.prefab, parent, false); model.name = "TTB authorized item";
            model.transform.localPosition = entry.offset; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one * entry.scale;
            return model;
        }
    }
}
