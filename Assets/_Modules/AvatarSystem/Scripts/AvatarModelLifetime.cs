using System;
using GLTFast;
using UnityEngine;

public sealed class AvatarModelLifetime : MonoBehaviour
{
    [NonSerialized] public GltfImport Importer;
    [NonSerialized] public Avatar GeneratedAvatar;
    private void OnDestroy()
    {
        Importer?.Dispose();
        Importer = null;
        if (GeneratedAvatar != null)
        {
            if (Application.isPlaying) Destroy(GeneratedAvatar);
            else DestroyImmediate(GeneratedAvatar);
        }
    }
}
