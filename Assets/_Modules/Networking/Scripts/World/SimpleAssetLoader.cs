using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleAssetLoader : Singleton<SimpleAssetLoader>
{
    public event Action OnAssetLoaded;

    public List<GameObject> AssetPrefab;

    public bool IsInstantiated;

    private void Start()
    {
        IsInstantiated = false;
        LoadAsset();
    }

    public void LoadAsset()
    {
        if (IsInstantiated) return;
        StartCoroutine(LoadAssetCoroutine());
    }

    IEnumerator LoadAssetCoroutine()
    {
        for(int i = 0; i < AssetPrefab.Count; i++)
        {
            Instantiate(AssetPrefab[i]);
            yield return null;
        }

        IsInstantiated = true;

        if(OnAssetLoaded != null) OnAssetLoaded();

        yield return null;
    }
}
