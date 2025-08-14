using GameKit.Dependencies.Utilities.Types;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MANGOsFramework.Deprecated
{
    public class Launcher : MonoBehaviour
    {
        [SerializeField, Scene] private string targetScene;

        public void Launch()
        {
            PersistentCanvas.LoadingCanvas?.ToggleLoadingScreen(true);

            CustomSceneLoader.LoadScene(targetScene);
        }
    }
}

