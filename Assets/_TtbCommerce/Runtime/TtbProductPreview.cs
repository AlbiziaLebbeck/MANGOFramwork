using System.Collections.Generic;
using UnityEngine;

namespace Mangos.Ttb.Commerce
{
    public sealed class TtbProductPreview : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private Camera previewCamera;
        private GameObject stage, model;
        private readonly List<Mesh> bakedMeshes = new List<Mesh>();
        private readonly Dictionary<Camera, bool> previousPreviewVisibility = new Dictionary<Camera, bool>();
        private Light keyLight, fillLight;
        private Vector3 center;
        private float radius, yaw, pitch, initialYaw, distance, fitDistance, minimumZoom = .5f, maximumZoom = 3;

        public bool Show(Transform source, float scale, float rotation, float minZoom, float maxZoom)
        {
            Hide(); EnsureStage(); stage.SetActive(true); minimumZoom = minZoom; maximumZoom = Mathf.Max(minZoom + .1f, maxZoom); initialYaw = rotation;
            model = new GameObject("Preview geometry"); model.layer = PreviewLayer; model.transform.SetParent(stage.transform, false);
            // Copy render geometry only: never instantiate product colliders, network components or purchase scripts into the preview.
            foreach (var renderer in source.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned) { mesh = new Mesh(); skinned.BakeMesh(mesh); bakedMeshes.Add(mesh); }
                else { var filter = renderer.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
                if (mesh == null) continue;
                var part = new GameObject(renderer.name); part.layer = PreviewLayer; part.transform.SetParent(model.transform, false);
                part.transform.localPosition = source.InverseTransformPoint(renderer.transform.position);
                part.transform.localRotation = Quaternion.Inverse(source.rotation) * renderer.transform.rotation;
                var relativeScale = renderer.transform.lossyScale; var sourceScale = source.lossyScale;
                part.transform.localScale = new Vector3(relativeScale.x / Nonzero(sourceScale.x), relativeScale.y / Nonzero(sourceScale.y), relativeScale.z / Nonzero(sourceScale.z));
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            if (model.transform.childCount == 0) { Hide(); return false; }
            model.transform.localScale = Vector3.Scale(source.lossyScale, Vector3.one * scale);
            var renderers = model.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            model.transform.position += stage.transform.position - bounds.center;
            radius = Mathf.Max(.01f, bounds.extents.magnitude); center = stage.transform.position;
            keyLight.transform.position = center + new Vector3(-3, 3, -4) * radius; keyLight.range = radius * 12; keyLight.intensity = radius * radius * 24;
            fillLight.transform.position = center + new Vector3(3, 1, -2) * radius; fillLight.range = radius * 12; fillLight.intensity = radius * radius * 10;
            foreach (var camera in Camera.allCameras)
                if (camera != previewCamera) { previousPreviewVisibility[camera] = (camera.cullingMask & (1 << PreviewLayer)) != 0; camera.cullingMask &= ~(1 << PreviewLayer); }
            previewCamera.enabled = true; ResetView(); return true;
        }
        public void SetViewport(Rect rectangle)
        {
            if (previewCamera == null) return;
            var zoomRatio = fitDistance > 0 ? distance / fitDistance : 1;
            // Preserve the full rectangle while it scrolls; the framebuffer clips pixels beyond its bounds.
            rectangle.x = Mathf.Clamp(rectangle.x, -2, 2); rectangle.y = Mathf.Clamp(rectangle.y, -2, 2);
            rectangle.width = Mathf.Clamp(rectangle.width, .01f, 2); rectangle.height = Mathf.Clamp(rectangle.height, .01f, 2);
            previewCamera.rect = rectangle; FrameDistance(); distance = Mathf.Clamp(fitDistance * zoomRatio, fitDistance * minimumZoom, fitDistance * maximumZoom); PositionCamera();
        }
        public void Orbit(float horizontal, float vertical) { yaw += horizontal * .35f; pitch = Mathf.Clamp(pitch + vertical * .3f, -65, 65); PositionCamera(); }
        public void Zoom(float delta) { distance = Mathf.Clamp(distance * Mathf.Exp(delta), fitDistance * minimumZoom, fitDistance * maximumZoom); PositionCamera(); }
        public void ResetView() { yaw = initialYaw; pitch = 5; FrameDistance(); distance = fitDistance; PositionCamera(); }
        public void Hide()
        {
            if (previewCamera != null) previewCamera.enabled = false;
            if (model != null) { model.SetActive(false); Destroy(model); } model = null;
            foreach (var mesh in bakedMeshes) Destroy(mesh); bakedMeshes.Clear();
            foreach (var item in previousPreviewVisibility) if (item.Key != null)
                item.Key.cullingMask = item.Value ? item.Key.cullingMask | (1 << PreviewLayer) : item.Key.cullingMask & ~(1 << PreviewLayer);
            previousPreviewVisibility.Clear();
            if (stage != null) stage.SetActive(false);
        }
        private void EnsureStage()
        {
            if (stage != null) return;
            stage = new GameObject("TTB private preview stage"); stage.transform.SetParent(transform, false); stage.transform.position = new Vector3(0, -500, 0);
            var cameraObject = new GameObject("TTB preview camera"); cameraObject.transform.SetParent(stage.transform, false);
            previewCamera = cameraObject.AddComponent<Camera>(); previewCamera.depth = 100; previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor; previewCamera.backgroundColor = new Color(.93f, .93f, .92f); previewCamera.fieldOfView = 33;
            previewCamera.nearClipPlane = .01f; previewCamera.farClipPlane = 1000; previewCamera.allowHDR = false; previewCamera.eventMask = 0; previewCamera.enabled = false;
            // Finite local lights at the remote stage cannot replace URP's world directional main light.
            var lightObject = new GameObject("TTB preview key light"); lightObject.layer = PreviewLayer; lightObject.transform.SetParent(stage.transform, false);
            keyLight = lightObject.AddComponent<Light>(); keyLight.type = LightType.Point; keyLight.cullingMask = 1 << PreviewLayer;
            var fillObject = new GameObject("TTB preview fill light"); fillObject.layer = PreviewLayer; fillObject.transform.SetParent(stage.transform, false);
            fillLight = fillObject.AddComponent<Light>(); fillLight.type = LightType.Point; fillLight.cullingMask = 1 << PreviewLayer;
        }
        private void FrameDistance()
        {
            if (previewCamera == null) return;
            var vertical = previewCamera.fieldOfView * Mathf.Deg2Rad * .5f;
            var horizontal = Mathf.Atan(Mathf.Tan(vertical) * Mathf.Max(.1f, previewCamera.aspect));
            fitDistance = radius * 1.12f / Mathf.Sin(Mathf.Min(vertical, horizontal));
            distance = Mathf.Clamp(distance <= 0 ? fitDistance : distance, fitDistance * minimumZoom, fitDistance * maximumZoom);
        }
        private void PositionCamera()
        {
            if (previewCamera == null || model == null) return;
            var orientation = Quaternion.Euler(pitch, yaw, 0);
            previewCamera.transform.position = center + orientation * new Vector3(0, 0, -distance);
            previewCamera.transform.LookAt(center);
            previewCamera.farClipPlane = Mathf.Max(10, distance + radius * 5);
        }
        private static float Nonzero(float value) => Mathf.Abs(value) > .000001f ? value : 1;
        private void OnDestroy() { Hide(); if (stage != null) Destroy(stage); }
    }
}
