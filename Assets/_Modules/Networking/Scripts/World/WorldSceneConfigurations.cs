using GameKit.Dependencies.Utilities.Types;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(fileName = "New World", menuName = "Worlds/New Worlds")]
public class WorldSceneConfigurations : ScriptableObject
{
    public string WorldName;

    [SerializeField]
    private Object[] _scenes = new Object[0];
    [SerializeField, HideInInspector]
    private string[] _sceneNames = new string[0];

    [SerializeField] 

    private void OnValidate()
    {
        List<string> additives = new List<string>();
        if (_scenes != null)
        {
            foreach (Object item in _scenes)
            {
                if (item != null)
                    additives.Add(item.name);
            }
        }
        _sceneNames = additives.ToArray();
    }

    /// <summary>
    /// Gets scenes to load when a player starts a room.
    /// </summary>
    public virtual string[] GetGameScenes()
    {
        return _sceneNames;
    }
}
