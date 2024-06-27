using GameKit.Dependencies.Utilities.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Launcher : MonoBehaviour
{
    [SerializeField, Scene] private string targetScene;

    public void Launch()
    {
        PersistentCanvas.LoadingCanvas?.ToggleLoadingScreen(true);

        CustomSceneLoader.LoadScene(targetScene);
    }

}
