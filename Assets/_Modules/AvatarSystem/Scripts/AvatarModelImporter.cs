using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using Newtonsoft.Json.Linq;
using UniVRM10;
using UnityEngine;
using UnityEngine.Networking;

public static class AvatarModelImporter
{
    public static async Task<GameObject> Load(string url, CancellationToken token)
    {
        using var request = UnityWebRequest.Get(url);
        request.timeout = 45;
        var pending = request.SendWebRequest();
        while (!pending.isDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
        if (request.result != UnityWebRequest.Result.Success) throw new InvalidOperationException("Avatar download failed: " + request.error);
        token.ThrowIfCancellationRequested();
        var bytes = request.downloadHandler.data;
        bool glb = bytes.Length >= 20 && BitConverter.ToUInt32(bytes, 0) == 0x46546c67;
        bool vrm = new Uri(url).AbsolutePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase);
        if (glb)
        {
            int length = BitConverter.ToInt32(bytes, 12);
            if (length < 0 || length > bytes.Length - 20) throw new InvalidOperationException("Invalid GLB JSON chunk.");
            var metadata = JObject.Parse(Encoding.UTF8.GetString(bytes, 20, length));
            vrm |= metadata["extensions"]?["VRMC_vrm"] != null || metadata["extensions"]?["VRM"] != null;
        }
        if (vrm)
        {
            var instance = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: true, controlRigGenerationOption: ControlRigGenerationOption.None, showMeshes: true, ct: token);
            if (instance == null) throw new InvalidOperationException("VRM importer returned no model.");
            if (token.IsCancellationRequested) { UnityEngine.Object.Destroy(instance.gameObject); token.ThrowIfCancellationRequested(); }
            return instance.gameObject;
        }
        var gltf = new GltfImport();
        GameObject root = null;
        try
        {
            bool loaded = glb ? await gltf.LoadGltfBinary(bytes, new Uri(url), cancellationToken: token) : await gltf.Load(url, cancellationToken: token);
            if (!loaded) throw new InvalidOperationException("glTF import failed.");
            token.ThrowIfCancellationRequested();
            root = new GameObject("AvatarModel");
            if (!await gltf.InstantiateMainSceneAsync(root.transform, token)) throw new InvalidOperationException("glTF scene could not be instantiated.");
            token.ThrowIfCancellationRequested();
            root.AddComponent<AvatarModelLifetime>().Importer = gltf;
            gltf = null;
            return root;
        }
        catch { if (root != null) UnityEngine.Object.Destroy(root); throw; }
        finally { gltf?.Dispose(); }
    }
}
