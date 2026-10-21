using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace LittlePrince.Rendering.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Rendering > Convert Synty Materials to URP
    ///   (or right-click a folder / materials > Little Prince > Convert Materials to URP)
    ///
    /// Synty's POLYGON packs ship Built-in pipeline shaders, which render pink in URP.
    /// This switches every material in Assets/Synty to a URP shader, keeping textures/colours:
    ///   Synty Triplanar (rocks)        -> SyntyStudios URP/Triplanar   (same properties)
    ///   Synty Vegetation (plants)      -> SyntyStudios URP/Vegetation  (same properties, wind)
    ///   Standard / legacy / Basic LOD  -> URP Lit
    ///   Particles                      -> URP Particles/Unlit (alpha, additive or multiply)
    ///   Water, sulphur pools, lava     -> URP Lit (transparent / emissive), simplified
    /// Materials already on a URP shader are left alone, so running it again is safe.
    /// </summary>
    public static class SyntyToURP
    {
        const string SyntyFolder = "Assets/Synty";
        const string UrpLit = "Universal Render Pipeline/Lit";
        const string UrpParticles = "Universal Render Pipeline/Particles/Unlit";

        [MenuItem("Little Prince/Rendering/Convert Synty Materials to URP")]
        static void ConvertSynty()
        {
            if (!AssetDatabase.IsValidFolder(SyntyFolder))
            {
                EditorUtility.DisplayDialog("Convert to URP", $"{SyntyFolder} not found.", "OK");
                return;
            }
            Convert(AssetDatabase.FindAssets("t:Material", new[] { SyntyFolder }).Select(AssetDatabase.GUIDToAssetPath));
        }

        [MenuItem("Assets/Little Prince/Convert Materials to URP")]
        static void ConvertSelection()
        {
            var paths = new HashSet<string>();
            foreach (var o in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(o);
                if (AssetDatabase.IsValidFolder(p))
                    foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { p })) paths.Add(AssetDatabase.GUIDToAssetPath(g));
                else if (o is Material) paths.Add(p);
            }
            Convert(paths);
        }

        static void Convert(IEnumerable<string> paths)
        {
            var counts = new Dictionary<string, int>();
            var skipped = new List<string>();
            var list = paths.ToList();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < list.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Convert to URP", list[i], i / (float)list.Count);
                    var m = AssetDatabase.LoadAssetAtPath<Material>(list[i]);
                    if (m == null || m.shader == null) continue;
                    string result = ConvertOne(m);
                    if (result == null) { if (!IsUrp(m.shader.name)) skipped.Add($"{m.name} ({m.shader.name})"); continue; }
                    counts[result] = counts.TryGetValue(result, out int c) ? c + 1 : 1;
                    EditorUtility.SetDirty(m);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            int total = counts.Values.Sum();
            Debug.Log($"[URP] Converted {total} materials: " + string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key}")) +
                      (skipped.Count > 0 ? $". Left as they were ({skipped.Count}): " + string.Join(", ", skipped) : "."));
        }

        static bool IsUrp(string shader) =>
            shader.StartsWith("Universal Render Pipeline") || shader.StartsWith("SyntyStudios URP") ||
            shader.StartsWith("Shader Graphs") || shader.StartsWith("LittlePrince") || shader.StartsWith("Skybox/") ||
            shader.StartsWith("SyntyStudios/Skybox"); // the Synty skyboxes are unlit and work as they are

        /// <summary>Converts one material; returns what it became, or null if left alone.</summary>
        static string ConvertOne(Material m)
        {
            string s = m.shader.name;
            if (IsUrp(s)) return null;

            if (s.StartsWith("SyntyStudios/") && s.Contains("Triplanar"))
            {
                m.shader = Find("SyntyStudios URP/Triplanar");
                m.SetFloat("_Glossiness", 0.1f); // the original was matte; Standard's leftover 0.5 would look wet
                return "rocks (URP Triplanar)";
            }
            if (s.StartsWith("SyntyStudios/Vegitation"))
            {
                m.shader = Find("SyntyStudios URP/Vegetation");
                return "plants (URP Vegetation)";
            }
            if (s == "SyntyStudios/Basic_LOD_Shader")
            {
                var tex = Tex(m, "_Albedo") ?? Tex(m, "_MainTex");
                var col = Col(m, "_AlbedoColour", Color.white);
                float smooth = Flt(m, "_Smoothness", 0.2f);
                ToLit(m, tex, col, null, smooth, 0f);
                return "LOD (URP Lit)";
            }
            if (s == "SyntyStudios/TexturePanner") // lava
            {
                var tex = Tex(m, "_Albedo");
                var col = Col(m, "_AlbedoTint", Color.white);
                var normal = Tex(m, "_Normal");
                var emission = Tex(m, "_Emission");
                ToLit(m, tex, col, normal, Flt(m, "_Smoothness", 0.2f), 0f);
                if (emission != null)
                {
                    m.SetTexture("_EmissionMap", emission);
                    m.SetColor("_EmissionColor", Color.white * 1.5f);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                return "lava (URP Lit, emissive)";
            }
            if (s.Contains("SulphurPools"))
            {
                var col = Color.Lerp(Col(m, "_ShallowColor", new Color(0.8f, 0.7f, 0.1f)), Col(m, "_DeepColor", new Color(0.3f, 0.45f, 0.1f)), 0.5f);
                col.a = Flt(m, "_Opacity", 0.9f);
                ToLit(m, null, col, null, Flt(m, "_Smoothness", 0.5f), 0f, transparent: col.a < 0.99f);
                return "pools (URP Lit)";
            }
            if (s.StartsWith("SyntyStudios/Water"))
            {
                var col = Col(m, "_DeepColour", new Color(0.07f, 0.43f, 0.4f));
                col.a = Mathf.Min(0.8f, Flt(m, "_Opacity", 0.8f));
                ToLit(m, null, col, Tex(m, "_RipplesNormal"), 0.9f, 0f, transparent: true);
                return "water (URP Lit, transparent)";
            }
            if (s.StartsWith("SyntyStudios/Cloud") || s.StartsWith("SyntyStudios/Crystal") ||
                s.StartsWith("SyntyStudios/Glacier") || s.StartsWith("SyntyStudios/Aurora"))
            {
                var col = Col(m, "_Colour", Col(m, "_Color", Color.white));
                col.a = 1f;
                ToLit(m, Tex(m, "_MainTex"), col, null, 0.3f, 0f);
                return "other Synty (URP Lit, simplified)";
            }
            if (s.Contains("Particles"))
            {
                ToParticles(m, s);
                return "particles (URP Particles)";
            }
            if (s.StartsWith("Standard") || s.StartsWith("Legacy Shaders/") || s.StartsWith("Mobile/") || s.StartsWith("Nature/"))
            {
                FromStandard(m);
                return "Standard (URP Lit)";
            }
            return null;
        }

        // ---- Built-in Standard -> URP Lit ----------------------------------------------------
        static void FromStandard(Material m)
        {
            var tex = Tex(m, "_MainTex");
            var col = Col(m, "_Color", Color.white);
            var normal = Tex(m, "_BumpMap");
            var metalMap = Tex(m, "_MetallicGlossMap");
            var occlusion = Tex(m, "_OcclusionMap");
            var emissionMap = Tex(m, "_EmissionMap");
            var emission = Col(m, "_EmissionColor", Color.black);
            float smooth = Flt(m, "_Glossiness", 0.5f), metal = Flt(m, "_Metallic", 0f);
            float mode = Flt(m, "_Mode", 0f), cutoff = Flt(m, "_Cutoff", 0.5f);
            bool wasEmissive = m.IsKeywordEnabled("_EMISSION") && emission.maxColorComponent > 0.001f;

            ToLit(m, tex, col, normal, smooth, metal, transparent: mode >= 2f, alphaClip: mode == 1f, cutoff: cutoff);
            if (metalMap != null) m.SetTexture("_MetallicGlossMap", metalMap);
            if (occlusion != null) m.SetTexture("_OcclusionMap", occlusion);
            if (wasEmissive)
            {
                m.SetColor("_EmissionColor", emission);
                if (emissionMap != null) m.SetTexture("_EmissionMap", emissionMap);
                m.EnableKeyword("_EMISSION");
            }
            Keywords(m);
        }

        static void ToLit(Material m, Texture tex, Color col, Texture normal, float smooth, float metal,
                          bool transparent = false, bool alphaClip = false, float cutoff = 0.5f)
        {
            m.shader = Find(UrpLit);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", Mathf.Clamp01(smooth));
            m.SetFloat("_Metallic", Mathf.Clamp01(metal));
            if (normal != null) m.SetTexture("_BumpMap", normal);
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            m.SetFloat("_Cutoff", cutoff);
            Keywords(m);
        }

        static void ToParticles(Material m, string shaderName)
        {
            var tex = Tex(m, "_MainTex");
            var col = Col(m, "_TintColor", Col(m, "_Color", Color.white));
            if (m.HasProperty("_TintColor")) col *= 2f; // legacy particles doubled the tint
            float mode = Flt(m, "_Mode", 2f);
            string n = (shaderName + " " + m.name).ToLowerInvariant();
            float blend = n.Contains("additive") || mode == 4f ? 2f   // additive
                        : n.Contains("multiply") || mode == 6f ? 3f   // multiply
                        : 0f;                                           // alpha

            m.shader = Find(UrpParticles);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", blend);
            Keywords(m);
        }

        static void Keywords(Material m)
        {
            if (m.shader.name == UrpLit) BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            else BaseShaderGUI.SetMaterialKeywords(m);
        }

        // ---- helpers ----------------------------------------------------------------------
        static Shader Find(string name)
        {
            var s = Shader.Find(name);
            if (s == null) throw new System.Exception($"Shader \"{name}\" not found (is URP installed and the LittlePrince/Shared/Shaders folder imported?).");
            return s;
        }

        static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        static Color Col(Material m, string p, Color fallback) => m.HasProperty(p) ? m.GetColor(p) : fallback;
        static float Flt(Material m, string p, float fallback) => m.HasProperty(p) ? m.GetFloat(p) : fallback;
    }
}
