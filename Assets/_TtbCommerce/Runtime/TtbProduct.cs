using System.Text.RegularExpressions;
using UnityEngine;

namespace Mangos.Ttb.Commerce
{
    // Presentation binding only. Price, stock, identity and payment authority stay on the commerce backend.
    [DisallowMultipleComponent]
    public sealed class TtbProduct : MonoBehaviour
    {
        [SerializeField] private string modelKey;
        [SerializeField] private string productId;
        [SerializeField] private Transform previewModel;
        public string ModelKey => modelKey;
        public string ProductId => productId;
        public Transform PreviewModel => previewModel != null ? previewModel : transform;
        public bool HasBinding => !string.IsNullOrEmpty(productId) || !string.IsNullOrEmpty(modelKey) && Regex.IsMatch(modelKey, "^[A-Za-z0-9_-]{1,100}$");
        public void Configure(string key, Transform model, string id = "") { modelKey = key; previewModel = model; productId = id; }
    }
}
