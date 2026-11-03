using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cabin.Core
{
    /// <summary>
    /// Drives the atmosphere pass (<c>Cabin/Atmosphere</c>, a full-screen renderer feature set up by WorldLook):
    /// the sun's direction and where it is on screen, the cloud layer's scroll, and the same clouds as a cookie on
    /// the sun so their shadows drift over the ground. The cloud lattice is laid along the sun's own axes so one
    /// tiling texture serves both the sky and the cookie, whose tile is the lattice foreshortened by the sun's
    /// elevation. With the pass off, Unity's own fog comes back. On the Look object.
    /// </summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>Each part on its own (the settings): shafts, haze, clouds, cloud shadows, the sun's glow.</summary>
        public static bool Shafts = true, HazeOn = true, CloudsOn = true, CloudShadows = true, Glow = true;

        [SerializeField] Material material;
        [SerializeField] Light sun;
        [SerializeField] ScriptableRendererFeature feature;
        [SerializeField] float cloudScale = 3000f;
        [SerializeField] Vector2 wind = new Vector2(1f, 0.35f);
        [SerializeField] float windSpeed = 5f;
        [SerializeField] float cloudCoverage = 0.48f, cloudSoftness = 0.14f, shadowStrength = 0.5f;
        [SerializeField] Color fallbackFog = new Color(0.71f, 0.78f, 0.86f);
        [SerializeField] float fallbackFogDensity = 0.00045f;

        Texture2D clouds, cookie;
        UniversalAdditionalLightData sunData;
        Vector2 axisR, axisU, offset;
        float cosElevation;
        bool wasEnabled = true;
        static readonly int SunDirId = Shader.PropertyToID("_SunDir"), SunScreenId = Shader.PropertyToID("_SunScreen"),
            AxisRId = Shader.PropertyToID("_CloudAxisR"), AxisUId = Shader.PropertyToID("_CloudAxisU"), OffsetId = Shader.PropertyToID("_CloudOffset"),
            CloudTexId = Shader.PropertyToID("_CloudTex"), SunColorId = Shader.PropertyToID("_SunColor"), TogglesId = Shader.PropertyToID("_Toggles"),
            ScaleId = Shader.PropertyToID("_CloudScale"), CoverageId = Shader.PropertyToID("_CloudCoverage"), SoftnessId = Shader.PropertyToID("_CloudSoftness");

        public void Set(Material m, Light s, ScriptableRendererFeature f)
        {
            material = m;
            sun = s;
            feature = f;
        }

        void Start()
        {
            if (material == null || sun == null) return;
            clouds = AtmosphereNoise.CloudTexture(256, 4, 1234);
            cookie = AtmosphereNoise.CookieTexture(clouds, cloudCoverage, cloudSoftness, shadowStrength);
            material.SetTexture(CloudTexId, clouds);
            material.SetFloat(ScaleId, cloudScale);
            material.SetFloat(CoverageId, cloudCoverage);
            material.SetFloat(SoftnessId, cloudSoftness);
            Axes(sun.transform, out axisR, out axisU, out cosElevation);
            material.SetVector(AxisRId, new Vector4(axisR.x, axisR.y, 0f, 0f));
            material.SetVector(AxisUId, new Vector4(axisU.x, axisU.y, 0f, 0f));
            sunData = sun.GetComponent<UniversalAdditionalLightData>();
            if (sunData == null) sunData = sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            sun.cookie = cookie;
            sunData.lightCookieSize = new Vector2(cloudScale, cloudScale * Mathf.Max(0.05f, cosElevation));
            Apply(true);
        }

        /// <summary>The cloud lattice's axes in world xz: the sun's right, and its up projected to the ground (unit), plus that projection's length (the sine of the sun's elevation: how much a tile is foreshortened in the cookie).</summary>
        public static void Axes(Transform sunTransform, out Vector2 right, out Vector2 upGround, out float upGroundLength)
        {
            Vector3 r = sunTransform.right, u = sunTransform.up;
            right = new Vector2(r.x, r.z).normalized;
            var ug = new Vector2(u.x, u.z);
            upGroundLength = ug.magnitude;
            upGround = upGroundLength > 1e-4f ? ug / upGroundLength : new Vector2(-right.y, right.x);
        }

        /// <summary>How visible the sun is for the shafts: 1 on screen, fading to 0 half a screen outside, 0 behind the camera.</summary>
        public static float SunVisibility(Vector3 viewport)
        {
            if (viewport.z <= 0f) return 0f;
            float outside = Mathf.Max(0f, -viewport.x, viewport.x - 1f, -viewport.y, viewport.y - 1f);
            return Mathf.Clamp01(1f - outside / 0.5f);
        }

        void Apply(bool on)
        {
            if (feature != null) feature.SetActive(on);
            RenderSettings.fog = !on;
            if (!on)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fallbackFog;
                RenderSettings.fogDensity = fallbackFogDensity;
            }
            if (sun != null) sun.cookie = on ? cookie : null;
            wasEnabled = on;
        }

        void LateUpdate()
        {
            if (material == null || sun == null) return;
            if (Enabled != wasEnabled) Apply(Enabled);
            if (!Enabled) return;
            // The parts: with the haze off, Unity's flat fog stands in for it (the "before" look) while the rest still runs.
            material.SetVector(TogglesId, new Vector4(Shafts ? 1f : 0f, HazeOn ? 1f : 0f, CloudsOn ? 1f : 0f, Glow ? 1f : 0f));
            RenderSettings.fog = !HazeOn;
            sun.cookie = CloudShadows && CloudsOn ? cookie : null;
            Vector3 toSun = -sun.transform.forward;
            material.SetVector(SunDirId, toSun);
            material.SetColor(SunColorId, sun.color * Mathf.Clamp(sun.intensity * 0.5f, 0.6f, 1.4f));
            var cam = Camera.main;
            if (cam != null)
            {
                var vp = cam.WorldToViewportPoint(cam.transform.position + toSun * 1000f);
                material.SetVector(SunScreenId, new Vector4(vp.x, vp.y, SunVisibility(vp), 0f));
            }
            // The wind, expressed along the lattice axes so the sky and the cookie scroll together.
            Vector2 w = wind.normalized * windSpeed * Time.deltaTime;
            offset += new Vector2(Vector2.Dot(w, axisR), Vector2.Dot(w, axisU));
            material.SetVector(OffsetId, new Vector4(offset.x, offset.y, 0f, 0f));
            if (sunData != null) sunData.lightCookieOffset = -offset / cloudScale;
        }

        void OnDestroy()
        {
            if (sun != null) sun.cookie = null;
            if (clouds != null) Destroy(clouds);
            if (cookie != null) Destroy(cookie);
        }
    }
}
