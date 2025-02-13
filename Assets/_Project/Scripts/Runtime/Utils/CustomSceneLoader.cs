using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;

public class CustomSceneLoader : MonoBehaviour
{
    private static string pendingUnloadScene;

    private static List<string> loadedScene = new List<string>();

    private static string sceneToUnload;

    public static void LoadScene(string sceneNameToLoad, bool unloadPrevious = true)
    {
        if (unloadPrevious)
        {
            pendingUnloadScene = SceneManager.GetActiveScene().name;
        }


        SceneManager.sceneLoaded += ActivateAndUnload;
        SceneManager.LoadScene(sceneNameToLoad, LoadSceneMode.Additive);
    }

    private static void ActivateAndUnload(Scene scene, LoadSceneMode mode)
    {
        if(pendingUnloadScene != null)
        {
            SceneManager.sceneLoaded -= ActivateAndUnload;
            SceneManager.UnloadSceneAsync(pendingUnloadScene);
            
            if (loadedScene.Contains(pendingUnloadScene))
            {
                loadedScene.Remove(pendingUnloadScene);
            }

            pendingUnloadScene = null;
        }
        loadedScene.Add(scene.name);

        SceneManager.SetActiveScene(scene);
    }

    public static void UnloadScene(string sceneNameToUnload)
    {
        if (loadedScene.Contains(sceneNameToUnload))
        {
            sceneToUnload = sceneNameToUnload;
            SceneManager.sceneUnloaded += SceneManager_sceneUnloaded;
        }

        SceneManager.UnloadSceneAsync(sceneNameToUnload);
        
    }

    private static void SceneManager_sceneUnloaded(Scene arg0)
    {
        if(arg0.name == sceneToUnload)
        {
            loadedScene.Remove(arg0.name);
        }
    }
}
