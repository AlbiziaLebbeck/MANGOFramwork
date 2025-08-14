#if UNITY_WEBGL && !UNITY_EDITOR
    using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace MANGOsFramework.Experiment
{
    [CreateAssetMenu(fileName = "New AuthConfig", menuName = "MANGOsFramework/Create New AuthConfig")]
    public class AuthConfig : ScriptableObject
    {
        public string METAVERSE_NAME;
        public string HOST;
        public string AUTH_HOST;
        public string REDIRECT_URL;
        public string METAVERSE_CLIENT_ID;
        public string SECRET;
    }
}

