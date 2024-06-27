using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PersistentCanvas : Singleton<PersistentCanvas>
{
    [SerializeField] private LoadingCanvas loadingCanvas;
    public static LoadingCanvas LoadingCanvas { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        LoadingCanvas = loadingCanvas;
    }
}
