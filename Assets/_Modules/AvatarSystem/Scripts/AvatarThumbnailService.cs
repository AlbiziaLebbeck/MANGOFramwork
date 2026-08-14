using GLTFast;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MANGOsFramework.Experiment
{
    public sealed class AvatarThumbnailService : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private const int TextureSize = 200;
        private const float FramePadding = 1.12f;
        private const float RequestTimeoutSeconds = 45f;

        private sealed class ThumbnailRequest
        {
            public string Url;
            public readonly List<Action<Texture2D>> Callbacks = new();
        }

        private readonly Queue<ThumbnailRequest> queue = new();
        private readonly Dictionary<string, ThumbnailRequest> pending = new();
        private readonly Dictionary<string, Texture2D> cache = new();

        private CancellationTokenSource cancellation;
        private bool isProcessing;

        public bool IsBusy => isProcessing || queue.Count > 0;

        private void Awake()
        {
            cancellation = new CancellationTokenSource();
        }

        public void RequestThumbnail(string avatarUrl, Action<Texture2D> callback)
        {
            string resolvedUrl = MangosApiClient.ResolveAssetUrl(
                avatarUrl,
                AuthConfig.DefaultApiOrigin);

            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                callback?.Invoke(null);
                return;
            }

            if (cache.TryGetValue(resolvedUrl, out Texture2D cachedTexture))
            {
                callback?.Invoke(cachedTexture);
                return;
            }

            if (pending.TryGetValue(resolvedUrl, out ThumbnailRequest existingRequest))
            {
                if (callback != null)
                {
                    existingRequest.Callbacks.Add(callback);
                }

                return;
            }

            var request = new ThumbnailRequest { Url = resolvedUrl };
            if (callback != null)
            {
                request.Callbacks.Add(callback);
            }

            pending.Add(resolvedUrl, request);
            queue.Enqueue(request);

            if (!isProcessing)
            {
                ProcessQueueAsync();
            }
        }

        public IEnumerator WaitForIdle()
        {
            while (IsBusy)
            {
                yield return null;
            }
        }

        private async void ProcessQueueAsync()
        {
            isProcessing = true;

            while (queue.Count > 0 && !cancellation.IsCancellationRequested)
            {
                ThumbnailRequest request = queue.Dequeue();
                Texture2D texture = null;

                try
                {
                    texture = await CreateThumbnailWithTimeoutAsync(request.Url);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Avatar thumbnail generation failed for {request.Url}: {exception.Message}");
                }

                pending.Remove(request.Url);
                if (texture != null)
                {
                    cache[request.Url] = texture;
                }

                for (int i = 0; i < request.Callbacks.Count; i++)
                {
                    try
                    {
                        request.Callbacks[i]?.Invoke(texture);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Avatar thumbnail callback failed: {exception.Message}");
                    }
                }

                await Task.Yield();
            }

            isProcessing = false;
        }

        private async Task<Texture2D> CreateThumbnailWithTimeoutAsync(string url)
        {
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellation.Token);
            Task<Texture2D> thumbnailTask = CreateThumbnailAsync(
                url,
                requestCancellation.Token);
            float startedAt = Time.realtimeSinceStartup;

            while (!thumbnailTask.IsCompleted &&
                   Time.realtimeSinceStartup - startedAt < RequestTimeoutSeconds)
            {
                await Task.Yield();
            }

            if (!thumbnailTask.IsCompleted)
            {
                requestCancellation.Cancel();
                try
                {
                    await thumbnailTask;
                }
                catch (OperationCanceledException)
                {
                }

                throw new TimeoutException($"Avatar thumbnail request timed out for {url}.");
            }

            return await thumbnailTask;
        }

        private static async Task<Texture2D> CreateThumbnailAsync(
            string url,
            CancellationToken cancellationToken)
        {
            var gltf = new GltfImport();
            GameObject previewRoot = null;

            try
            {
                bool loaded = await gltf.Load(url, cancellationToken: cancellationToken);
                if (!loaded || cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                previewRoot = new GameObject("AvatarThumbnailPreview");
                previewRoot.hideFlags = HideFlags.HideAndDontSave;
                previewRoot.transform.position = new Vector3(10000f, -10000f, 10000f);

                bool instantiated = await gltf.InstantiateMainSceneAsync(
                    previewRoot.transform,
                    cancellationToken);
                if (!instantiated || cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                SetLayerRecursively(previewRoot.transform, PreviewLayer);
                DisableAnimation(previewRoot);
                await Task.Yield();
                await Task.Yield();

                if (!TryGetRendererBounds(previewRoot, out Bounds bounds))
                {
                    return null;
                }

                return Capture(previewRoot, bounds);
            }
            finally
            {
                if (previewRoot != null)
                {
                    Destroy(previewRoot);
                }

                gltf.Dispose();
            }
        }

        private static Texture2D Capture(GameObject previewRoot, Bounds bounds)
        {
            var cameraObject = new GameObject("AvatarThumbnailCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(
                0.1f,
                Mathf.Max(bounds.extents.y, bounds.extents.x) * FramePadding);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(10f, bounds.size.z + 6f);
            camera.transform.position = bounds.center + Vector3.forward * (bounds.extents.z + 3f);
            camera.transform.LookAt(bounds.center, Vector3.up);

            var lightObject = new GameObject("AvatarThumbnailLight");
            lightObject.hideFlags = HideFlags.HideAndDontSave;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.cullingMask = 1 << PreviewLayer;
            light.transform.rotation = Quaternion.Euler(35f, 145f, 0f);

            var target = new RenderTexture(
                TextureSize,
                TextureSize,
                24,
                RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            camera.targetTexture = target;

            RenderTexture previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
                {
                    name = $"{previewRoot.name}Texture"
                };
                texture.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
                texture.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                Destroy(target);
                Destroy(cameraObject);
                Destroy(lightObject);
            }

            return texture;
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer.bounds.size.sqrMagnitude <= 0f)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private static void DisableAnimation(GameObject root)
        {
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                animators[i].enabled = false;
            }

            Animation[] animations = root.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length; i++)
            {
                animations[i].enabled = false;
            }
        }

        private static void SetLayerRecursively(Transform current, int layer)
        {
            current.gameObject.layer = layer;
            for (int i = 0; i < current.childCount; i++)
            {
                SetLayerRecursively(current.GetChild(i), layer);
            }
        }

        private void OnDestroy()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();

            foreach (Texture2D texture in cache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            cache.Clear();
            pending.Clear();
            queue.Clear();
        }
    }
}
