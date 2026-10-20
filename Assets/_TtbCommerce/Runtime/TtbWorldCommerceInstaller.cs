using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mangos.Ttb.Commerce
{
    // Scene-owned objects unload together. The existing Bootstrap/Launcher, network player and camera remain in control.
    public static class TtbWorldCommerceInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; }
        private static void Loaded(Scene scene, LoadSceneMode mode)
        {
            var settings = Resources.Load<TtbWorldCommerceSettings>("TtbWorldCommerce");
            if (settings == null || settings.zone == null || scene.name != settings.worldScene) return;
            var hasZone = false;
            foreach (var root in scene.GetRootGameObjects()) if (root.name == "TTB commerce zone") { hasZone = true; break; }
            if (!hasZone)
            {
                var zone = Object.Instantiate(settings.zone); zone.name = "TTB commerce zone"; zone.transform.position = settings.position;
                SceneManager.MoveGameObjectToScene(zone, scene);
            }
#if !UNITY_SERVER
            // An authored exhibition must still receive a bridge; avoid duplicate bridges in the prepared scene.
            foreach (var root in scene.GetRootGameObjects()) if (root.GetComponent<MangoTtbCommerceBridge>() != null) return;
            var rootBridge = new GameObject("MangoTtbCommerceBridge"); SceneManager.MoveGameObjectToScene(rootBridge, scene);
            rootBridge.AddComponent<MangoTtbCommerceBridge>(); rootBridge.AddComponent<TtbProductInteractor>();
#endif
        }
    }
}
