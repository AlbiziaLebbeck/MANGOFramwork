using GameKit.Dependencies.Utilities.Types;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Launcher : MonoBehaviour
{
    [SerializeField, Scene] private string targetScene;

    public void Launch()
    {
        SceneManager.LoadScene(targetScene);
    }
}
