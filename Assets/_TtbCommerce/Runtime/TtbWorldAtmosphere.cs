using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Mangos.Ttb.Commerce
{
    [DisallowMultipleComponent]
    public sealed class TtbWorldAtmosphere : MonoBehaviour
    {
        [SerializeField] private Material skybox;
        public Material Skybox => skybox;
        public void Configure(Material material) => skybox = material;

#if !UNITY_SERVER
        private bool applied, previousFog;
        private int activeSceneHandle;
        private AmbientMode previousAmbientMode;
        private FogMode previousFogMode;
        private Color previousSky, previousEquator, previousGround, previousFogColor;
        private float previousFogDensity;
        private Material previousSkybox;
        private Light previousSun;
        private Color previousSunColor;
        private float previousSunIntensity, previousSunShadowStrength;
        private int previousSunMask;

        private void Start()
        {
            if (gameObject.scene.name != "BaseWorld") return;
            activeSceneHandle = SceneManager.GetActiveScene().handle;
            previousAmbientMode = RenderSettings.ambientMode;
            previousSky = RenderSettings.ambientSkyColor; previousEquator = RenderSettings.ambientEquatorColor;
            previousGround = RenderSettings.ambientGroundColor; previousSkybox = RenderSettings.skybox;
            previousFog = RenderSettings.fog; previousFogMode = RenderSettings.fogMode;
            previousFogColor = RenderSettings.fogColor; previousFogDensity = RenderSettings.fogDensity;
            previousSun = RenderSettings.sun;
            if (previousSun != null)
            {
                previousSunColor = previousSun.color; previousSunIntensity = previousSun.intensity;
                previousSunShadowStrength = previousSun.shadowStrength;
                previousSunMask = previousSun.cullingMask;
            }
            ApplyStyle(skybox); applied = true;
        }

        private void OnDestroy()
        {
            // Do not overwrite the profile of another world which has already become active.
            if (!applied || SceneManager.GetActiveScene().handle != activeSceneHandle) return;
            RenderSettings.ambientMode = previousAmbientMode; RenderSettings.ambientSkyColor = previousSky;
            RenderSettings.ambientEquatorColor = previousEquator; RenderSettings.ambientGroundColor = previousGround;
            RenderSettings.skybox = previousSkybox; RenderSettings.fog = previousFog; RenderSettings.fogMode = previousFogMode;
            RenderSettings.fogColor = previousFogColor; RenderSettings.fogDensity = previousFogDensity;
            if (previousSun != null && RenderSettings.sun == previousSun)
            {
                previousSun.color = previousSunColor; previousSun.intensity = previousSunIntensity;
                previousSun.shadowStrength = previousSunShadowStrength;
                previousSun.cullingMask = previousSunMask;
            }
        }
#endif

        public static void ApplyStyle(Material material)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.76f, .79f, .73f);
            RenderSettings.ambientEquatorColor = new Color(.62f, .59f, .51f);
            RenderSettings.ambientGroundColor = new Color(.25f, .29f, .25f);
            if (material != null) RenderSettings.skybox = material;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.83f, .85f, .79f); RenderSettings.fogDensity = .012f;
            if (RenderSettings.sun != null)
            {
                RenderSettings.sun.color = new Color(1, .94f, .80f); RenderSettings.sun.intensity = 1.1f;
                RenderSettings.sun.shadowStrength = .28f;
                RenderSettings.sun.cullingMask &= ~(1 << 31);
            }
        }
    }
}
