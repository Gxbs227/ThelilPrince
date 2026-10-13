using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LittlePrince.Lighting.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Lighting > Afternoon: Match Lighting To Skybox
    ///
    /// Makes the open scene's objects take the colour of its skybox (HDRI):
    ///   - looks at the sky, finds the sun in it and turns the Directional Light to
    ///     match (low afternoon angle), tinted with the sun's colour
    ///   - ambient light and reflections come from the skybox (Generate Lighting)
    ///   - fog in the horizon's colour, so far things melt into the sky
    ///   - a global post-processing Volume with a warm afternoon grade
    /// Run it again after changing the skybox. The light, Volume and cameras can be
    /// undone (Ctrl+Z); fog / ambient settings are in the Lighting window > Environment.
    /// </summary>
    public static class AfternoonLighting
    {
        const string ProfilePath = "Assets/LittlePrince/Settings/PostProcessing/Afternoon.asset";
        const string VolumeName = "Afternoon Volume";
        const int FaceSize = 64;
        static readonly Color Warm = new Color(1f, 0.74f, 0.5f);

        [MenuItem("Little Prince/Lighting/Afternoon: Match Lighting To Skybox")]
        static void Run()
        {
            if (RenderSettings.skybox == null)
            {
                EditorUtility.DisplayDialog("Afternoon lighting", "The scene has no skybox (Lighting window > Environment).", "OK");
                return;
            }

            var sky = SampleSky();
            var scene = EditorSceneManager.GetActiveScene();
            Undo.SetCurrentGroupName("Afternoon lighting");

            // ---- Sun ------------------------------------------------------------------------
            var sun = FindSun();
            if (sun != null)
            {
                Undo.RecordObject(sun, "Afternoon lighting");
                Undo.RecordObject(sun.transform, "Afternoon lighting");
                Vector3 toSun = sky.sunDirection;
                float elevation = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
                float clamped = Mathf.Clamp(elevation, 12f, 32f); // afternoon: low, long shadows, but not night
                Vector3 flat = new Vector3(toSun.x, 0f, toSun.z);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                flat.Normalize();
                float rad = clamped * Mathf.Deg2Rad;
                toSun = flat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
                sun.transform.rotation = Quaternion.LookRotation(-toSun);

                Color c = Color.Lerp(Normalised(sky.sunColor), Warm, 0.35f);
                sun.color = Normalised(c);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
                RenderSettings.sun = sun;
                Debug.Log($"[Lighting] Sun found in the sky at {elevation:0}° (set to {clamped:0}°), colour {sun.color}.");
            }
            else Debug.LogWarning("[Lighting] No active Directional Light in the scene.");

            // ---- Ambient, reflections, fog ---------------------------------------------------
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = Normalised(sky.horizonColor, 0.9f).gamma;

            // ---- Post-processing -------------------------------------------------------------
            SetupVolume(scene);
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.gameObject.scene != scene) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                Undo.RecordObject(data, "Afternoon lighting");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
            }

            EditorSceneManager.MarkSceneDirty(scene);

            // ---- Ambient from the HDRI needs generated lighting ------------------------------
            if (EditorUtility.DisplayDialog("Afternoon lighting",
                    "Sun, fog and post-processing are set.\n\nGenerate lighting now? That's what makes the ambient light " +
                    "and reflections come from the HDRI (otherwise Unity uses a default grey sky). " +
                    "It can take a while if there are many Static objects.",
                    "Generate", "Later"))
                Lightmapping.BakeAsync();
        }

        // ------------------------------------------------------------------------------
        struct SkySample
        {
            public Vector3 sunDirection;
            public Color sunColor, horizonColor, skyColor;
        }

        /// <summary>Renders the skybox in 6 directions and reads it back (linear, HDR when possible).</summary>
        static SkySample SampleSky()
        {
            var go = new GameObject("Sky Sampler") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 0; // only the sky
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            cam.allowHDR = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;
            var rt = new RenderTexture(FaceSize, FaceSize, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var tex = new Texture2D(FaceSize, FaceSize, TextureFormat.RGBAHalf, false, true);

            var texels = new List<(Vector3 dir, Color col, float lum)>();
            var faces = new[]
            {
                (Vector3.forward, Vector3.up), (Vector3.back, Vector3.up), (Vector3.left, Vector3.up),
                (Vector3.right, Vector3.up), (Vector3.up, Vector3.back), (Vector3.down, Vector3.forward),
            };
            var prev = RenderTexture.active;
            try
            {
                foreach (var (fwd, up) in faces)
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(fwd, up));
                    cam.targetTexture = rt;
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                    else cam.Render();

                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, FaceSize, FaceSize), 0, 0);
                    tex.Apply();
                    var px = tex.GetPixels();
                    for (int y = 0; y < FaceSize; y++)
                    for (int x = 0; x < FaceSize; x++)
                    {
                        Color c = px[y * FaceSize + x];
                        Vector3 dir = cam.ViewportPointToRay(new Vector3((x + 0.5f) / FaceSize, (y + 0.5f) / FaceSize, 0f)).direction;
                        texels.Add((dir, c, 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b));
                    }
                }
            }
            finally
            {
                RenderTexture.active = prev;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(go);
            }

            // Sun: the brightest 0.2% of the sky, weighted by brightness.
            var sorted = new List<float>(texels.Count);
            foreach (var t in texels) sorted.Add(t.lum);
            sorted.Sort();
            float threshold = sorted[Mathf.Clamp((int)(sorted.Count * 0.998f), 0, sorted.Count - 1)];

            Vector3 sunSum = Vector3.zero;
            Color sunCol = Color.black, horizon = Color.black, skyCol = Color.black;
            int sunN = 0, horizonN = 0, skyN = 0;
            foreach (var t in texels)
            {
                if (t.lum >= threshold) { sunSum += t.dir * t.lum; sunCol += t.col; sunN++; }
                if (Mathf.Abs(t.dir.y) < 0.12f) { horizon += t.col; horizonN++; }
                if (t.dir.y > 0.3f) { skyCol += t.col; skyN++; }
            }
            return new SkySample
            {
                sunDirection = sunSum.sqrMagnitude > 0f ? sunSum.normalized : new Vector3(0.5f, 0.3f, 0.8f).normalized,
                sunColor = sunN > 0 ? sunCol / sunN : Warm,
                horizonColor = horizonN > 0 ? horizon / horizonN : Color.gray,
                skyColor = skyN > 0 ? skyCol / skyN : Color.gray,
            };
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled) return RenderSettings.sun;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled) return l;
            return null;
        }

        /// <summary>Same hue, brightest channel = <paramref name="max"/>.</summary>
        static Color Normalised(Color c, float max = 1f)
        {
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m <= 1e-5f) return Color.white;
            return new Color(c.r / m * max, c.g / m * max, c.b / m * max, 1f);
        }

        static void SetupVolume(UnityEngine.SceneManagement.Scene scene)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) // created once; your own tweaks to it are kept afterwards
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);

                var tone = profile.Add<Tonemapping>(true);
                tone.mode.Override(TonemappingMode.ACES);

                var color = profile.Add<ColorAdjustments>(true);
                color.postExposure.Override(0.35f);
                color.contrast.Override(8f);
                color.saturation.Override(10f);
                color.colorFilter.Override(new Color(1f, 0.94f, 0.86f));

                var wb = profile.Add<WhiteBalance>(true);
                wb.temperature.Override(12f);

                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(0.5f);
                bloom.threshold.Override(1f);
                bloom.scatter.Override(0.7f);

                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.Override(0.2f);

                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            Volume volume = null;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (v.gameObject.scene == scene && v.name == VolumeName) volume = v;
            if (volume == null)
            {
                var go = new GameObject(VolumeName);
                Undo.RegisterCreatedObjectUndo(go, "Afternoon lighting");
                SceneManagerMove(go, scene);
                volume = go.AddComponent<Volume>();
            }
            else Undo.RecordObject(volume, "Afternoon lighting");
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }
    }
}
