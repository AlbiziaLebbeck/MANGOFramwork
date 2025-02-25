using FishNet;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// This is local scene loader, load by clients, nothing to do with the server. Unless you want to have a networkObject.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    ConnectionStarter connectionStarter;

    public List<string> scenesToLoad = new List<string>();

    void Start()
    {
        connectionStarter = FindObjectOfType<ConnectionStarter>();

        if (InstanceFinder.NetworkManager.IsServerStarted)
        {
            if (connectionStarter != null)
            {
                if (connectionStarter.StartType == StartType.Server)
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        StartCoroutine(LoadScenesInOrder());
    }

    private IEnumerator LoadScenesInOrder()
    {
        PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(true);

        for (int i = 0; i < scenesToLoad.Count; i++)
        {
            string sceneName = scenesToLoad[i];

            if (!SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(true);
                PersistentCanvas.LoadingCanvas.SetInformationDisplay($"Loading {sceneName}...");

                AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

                while (!asyncLoad.isDone)
                {
                    yield return null;
                }

                if (i == scenesToLoad.Count - 1)
                {
                    PersistentCanvas.LoadingCanvas.SetInformationDisplay($"Set Active Scene for {sceneName}...");
                    SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
                }

                EventHandler.OnLoadSceneCompleted(sceneName);
            }
        }

        PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
    }
}
