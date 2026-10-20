using UnityEngine;

namespace Mangos.Ttb.Commerce
{
    public sealed class TtbWorldCommerceSettings : ScriptableObject
    {
        public string worldScene = "BaseWorld";
        public GameObject zone;
        public Vector3 position = new Vector3(0, 0, 8);
    }
}
