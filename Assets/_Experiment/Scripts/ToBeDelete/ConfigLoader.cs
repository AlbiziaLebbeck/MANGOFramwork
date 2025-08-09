//using System.Runtime.InteropServices;
//using UnityEngine;

//namespace MANGOsFramework.Experiment
//{
//    public class ConfigLoader : MonoBehaviour
//    {
//        [DllImport("__Internal")]
//        private static extern void LoadConfigJSON(string callbackGameObject, string callbackMethod);

//        public static AuthConfig Current;

//        public static bool UseMock;
//        public bool _useMock;

//        private void Start()
//        {
//#if UNITY_WEBGL && !UNITY_EDITOR
//            LoadConfigJSON(gameObject.name, "OnConfigLoaded");
//#else
//            UseMock = _useMock;
//            LoadFromResources();
//#endif
//        }

//        public void OnConfigLoaded(string json)
//        {
//            Current = JsonUtility.FromJson<AuthConfig>(json);
//            Debug.Log("WebGL Config Loaded: " + Current.HOST);
//        }

//        private void LoadFromResources()
//        {
//            TextAsset config = Resources.Load<TextAsset>("Experiment/Config/config_dev");
//            Current = JsonUtility.FromJson<AuthConfig>(config.text);
//            Debug.Log("Editor Config Loaded: " + Current.HOST);
//        }
//    }

//    [System.Serializable]
//    public class AuthConfig
//    {
//        public string HOST;
//        public string AUTH_HOST;
//        public string METAVERSE_CLIENT_ID;
//        public string SECRET;
//        public string environment;
//    }
//}

