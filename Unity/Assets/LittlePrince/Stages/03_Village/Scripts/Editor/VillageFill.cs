using System.Collections.Generic;
using System.Linq;
using LittlePrince.GuidedWalk;
using LittlePrince.HandTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittlePrince.Stages.Village.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Stage 2 - Village > Fill Village (houses + trees)
    ///   Fills the first part (the village, up to the wheat field) with placeholder
    ///   houses along the path and trees from your own tree prefabs around them.
    ///   Everything goes under "Village Layout/Village Fill" so running it again
    ///   (or a different Seed) replaces only that; your own objects are kept and avoided.
    ///
    /// Menu: Little Prince > Stage 2 - Village > Bush: Swipe or Wave
    ///   The bush at the fox accepts a swipe OR a wave (the swipe alone was too hard).
    /// </summary>
    public static class VillageFill
    {
        const string FillName = "Village Fill";
        const string PrefabFolder = "Assets/LittlePrince/Stages/03_Village/Prefabs";
        const string MatFolder = "Assets/LittlePrince/Stages/03_Village/Materials/Placeholders";

        [MenuItem("Little Prince/Stage 2 - Village/Fill Village (houses + trees)")]
        static void Fill()
        {
            var root = GameObject.Find("Village Layout");
            var path = root != null ? root.GetComponentInChildren<GuidedPath>(true) : null;
            if (path == null)
            {
                EditorUtility.DisplayDialog("Village", "No \"Village Layout\" with a path in the open scene.", "OK");
                return;
            }
            Undo.SetCurrentGroupName("Fill village");
            var old = root.transform.Find(FillName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Physics.SyncTransforms();
            path.Rebuild();

            var fill = new GameObject(FillName).transform;
            Undo.RegisterCreatedObjectUndo(fill.gameObject, "Fill village");
            fill.SetParent(root.transform, false);
            var housesRoot = new GameObject("Houses").transform;
            housesRoot.SetParent(fill, false);
            var treesRoot = new GameObject("Trees").transform;
            treesRoot.SetParent(fill, false);

            // The village = the path up to the wheat field.
            var wheat = root.GetComponentInChildren<WheatField>(true);
            float villageEnd = path.Length;
            if (wheat != null)
            {
                float fieldMinX = wheat.transform.position.x - wheat.size.x * 0.5f;
                for (float d = 0f; d < path.Length; d += 0.5f)
                    if (path.PositionAt(d).x >= fieldMinX - 1f) { villageEnd = d; break; }
            }

            var rng = new System.Random(7);
            var avoid = new List<(Vector3 pos, float radius)>(); // children, stops, hide spots, existing trees
            foreach (var r in Object.FindObjectsByType<ChildRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                avoid.Add((r.transform.position, 2f));
                if (r.hideSpot != null) avoid.Add((r.hideSpot.position, 1.5f));
            }
            var existingTrees = ExistingTrees(root.transform, fill);
            foreach (var t in existingTrees) avoid.Add((t.position, 2.5f));

            // ---- Houses: a row on each side of the path, a second row behind ----------------
            var walls = new[]
            {
                Mat("House Wall Cream", new Color(0.96f, 0.9f, 0.8f)), Mat("House Wall White", new Color(0.97f, 0.96f, 0.93f)),
                Mat("House Wall Peach", new Color(0.97f, 0.82f, 0.7f)), Mat("House Wall Blue", new Color(0.78f, 0.86f, 0.93f)),
            };
            var roofs = new[]
            {
                Mat("Roof Terracotta", new Color(0.78f, 0.4f, 0.3f)), Mat("Roof Red", new Color(0.7f, 0.3f, 0.28f)),
                Mat("Roof Blue", new Color(0.35f, 0.47f, 0.65f)), Mat("Roof Brown", new Color(0.5f, 0.36f, 0.28f)),
            };
            var doorMat = Mat("Door", new Color(0.45f, 0.3f, 0.22f));
            var windowMat = Mat("Window", new Color(0.55f, 0.7f, 0.85f));

            int houses = 0;
            foreach (var (setback, spacing, chance) in new[] { (3.2f, 6.5f, 0.85f), (10f, 7.5f, 0.6f) })
            {
                for (float d = 4f; d < villageEnd - 2f; d += spacing)
                {
                    Vector3 at = path.PositionAt(d);
                    Vector3 fwd = path.DirectionAt(d, 1f);
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (rng.NextDouble() > chance) continue;
                        var size = new Vector3(Range(rng, 3f, 4.6f), Range(rng, 2.6f, 3.4f), Range(rng, 2.8f, 3.8f));
                        Vector3 center = at + right * side * (setback + size.z * 0.5f) + fwd * Range(rng, -1.2f, 1.2f);
                        center.y = Ground(center);
                        // front faces the path
                        Vector3 toPath = path.PositionAt(path.ClosestDistance(center)) - center;
                        toPath.y = 0f;
                        var rot = Quaternion.LookRotation(toPath.sqrMagnitude > 0.01f ? toPath.normalized : -right * side);
                        if (!HouseFits(path, center, size, rot, avoid)) continue;
                        House(housesRoot, ++houses, center, size, rot, walls[rng.Next(walls.Length)],
                              roofs[rng.Next(roofs.Length)], doorMat, windowMat);
                        Physics.SyncTransforms();
                    }
                }
            }

            // ---- Trees: around and behind the houses, denser toward the village edge ----------
            var prefabs = TreePrefabs(existingTrees);
            float targetHeight = TypicalHeight(existingTrees, 6f);
            var bounds = new Bounds(path.PositionAt(0f), Vector3.zero);
            for (float d = 0f; d <= villageEnd; d += 1f) bounds.Encapsulate(path.PositionAt(d));
            bounds.Expand(new Vector3(30f, 0f, 30f));
            var placed = new List<Vector3>(existingTrees.Select(t => t.position));
            int trees = 0;
            for (int attempt = 0; attempt < 4000 && trees < 90 && prefabs.Count > 0; attempt++)
            {
                var p = new Vector3(Range(rng, bounds.min.x, bounds.max.x), 0f, Range(rng, bounds.min.z, bounds.max.z));
                float along = path.ClosestDistance(p, out float gap);
                if (along > villageEnd + 2f) continue;                  // not into the wheat
                if (gap < 4.5f) continue;                               // keep the path open
                if (gap < 9f && rng.NextDouble() < 0.6) continue;       // fewer close to the path
                if (placed.Any(q => (q - p).sqrMagnitude < 3.8f * 3.8f)) continue;
                if (avoid.Any(a => (a.pos - p).sqrMagnitude < a.radius * a.radius)) continue;
                p.y = Ground(p);
                if (Physics.CheckSphere(p + Vector3.up * 2f, 1.4f, ~0, QueryTriggerInteraction.Ignore)) continue; // a house

                var prefab = prefabs[rng.Next(prefabs.Count)];
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
                Undo.RegisterCreatedObjectUndo(tree, "Fill village");
                tree.transform.SetParent(treesRoot, true);
                tree.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f));
                float h = Height(tree);
                if (h > 0.01f) tree.transform.localScale *= targetHeight / h * Range(rng, 0.8f, 1.2f);
                placed.Add(p);
                trees++;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = fill.gameObject;
            Debug.Log($"[Village] Filled the village: {houses} placeholder houses, {trees} trees (height ~{targetHeight:0.0} m, " +
                      $"from {prefabs.Count} tree prefabs). Delete or move any of them; run it again to start over.");
        }

        [MenuItem("Little Prince/Stage 2 - Village/Bush: Swipe or Wave")]
        static void BushGesture()
        {
            int changed = 0;
            foreach (var g in Object.FindObjectsByType<GestureStop>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var step in g.steps)
                {
                    if (step.gestures == null || !step.gestures.Contains(StoryGesture.Swipe)) continue;
                    Undo.RecordObject(g, "Bush gesture");
                    step.gestures = new[] { StoryGesture.Swipe, StoryGesture.WaveAny };
                    step.hint = "Push the bush aside: sweep or wave your hand.";
                    EditorUtility.SetDirty(g);
                    changed++;
                }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(changed > 0
                ? $"[Village] {changed} swipe step(s) now accept a swipe or a wave. Save the scene."
                : "[Village] No swipe step found in this scene.");
        }

        // ------------------------------------------------------------------------------
        static bool HouseFits(GuidedPath path, Vector3 center, Vector3 size, Quaternion rot, List<(Vector3 pos, float radius)> avoid)
        {
            // corners and edge midpoints must stay off the path
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Vector3 p = center + rot * new Vector3(x * size.x * 0.5f, 0f, z * size.z * 0.5f);
                path.ClosestDistance(p, out float gap);
                if (gap < 2f) return false;
            }
            float r = Mathf.Max(size.x, size.z) * 0.5f;
            if (avoid.Any(a => (a.pos - center).sqrMagnitude < (a.radius + r) * (a.radius + r))) return false;
            // no other house / collider in the way (with a little street between houses)
            return !Physics.CheckBox(center + Vector3.up * (size.y * 0.5f + 0.2f), size * 0.5f + new Vector3(1f, 0f, 1f), rot,
                                     ~0, QueryTriggerInteraction.Ignore);
        }

        static void House(Transform parent, int index, Vector3 basePos, Vector3 size, Quaternion rot,
                          Material wall, Material roof, Material door, Material window)
        {
            var house = new GameObject($"House {index:00}").transform;
            house.SetParent(parent, false);
            house.SetPositionAndRotation(basePos, rot);

            var body = Box("Walls", house, new Vector3(0f, size.y * 0.5f, 0f), size, wall);
            body.AddComponent<BoxCollider>(); // so the boy can hide behind it

            // Gabled roof: two slanted slabs meeting at the ridge (along x).
            float rise = size.z * 0.38f;
            float slope = Mathf.Sqrt(rise * rise + size.z * size.z * 0.25f);
            float angle = Mathf.Atan2(rise, size.z * 0.5f) * Mathf.Rad2Deg;
            foreach (float s in new[] { -1f, 1f })
            {
                var slab = Box("Roof", house, new Vector3(0f, size.y + rise * 0.5f, s * size.z * 0.25f),
                               new Vector3(size.x + 0.4f, 0.15f, slope + 0.35f), roof);
                slab.transform.localRotation = Quaternion.Euler(s * angle, 0f, 0f); // each slab slopes down to its eave
            }
            // Gable ends: a wedge under the roof on each side.
            foreach (float s in new[] { -1f, 1f })
            {
                Box("Gable", house, new Vector3(s * (size.x * 0.5f - 0.05f), size.y + rise * 0.35f, 0f),
                                new Vector3(0.1f, rise * 0.7f, size.z * 0.5f), wall);
            }

            // Door and two windows on the front (+z faces the path).
            float front = size.z * 0.5f + 0.02f;
            Box("Door", house, new Vector3(0f, 0.95f, front), new Vector3(0.9f, 1.9f, 0.05f), door);
            Box("Window", house, new Vector3(-size.x * 0.3f, size.y * 0.6f, front), new Vector3(0.7f, 0.7f, 0.05f), window);
            Box("Window", house, new Vector3(size.x * 0.3f, size.y * 0.6f, front), new Vector3(0.7f, 0.7f, 0.05f), window);
        }

        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Tree prefab instances already in the layout (yours), outside the fill.</summary>
        static List<Transform> ExistingTrees(Transform root, Transform fill)
        {
            var list = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.IsChildOf(fill) || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (src != null && src.name.Contains("Tree")) list.Add(t);
            }
            return list;
        }

        /// <summary>The tree prefabs you already use; if none, every Tree prefab in the stage folder.</summary>
        static List<GameObject> TreePrefabs(List<Transform> existing)
        {
            var set = new List<GameObject>();
            foreach (var t in existing)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (src != null && !set.Contains(src)) set.Add(src);
            }
            if (set.Count > 0) return set;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.name.Contains("Tree")) set.Add(go);
            }
            return set;
        }

        static float TypicalHeight(List<Transform> trees, float fallback)
        {
            var heights = trees.Select(t => Height(t.gameObject)).Where(h => h > 0.5f).OrderBy(h => h).ToList();
            return heights.Count > 0 ? heights[heights.Count / 2] : fallback;
        }

        static float Height(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.size.y;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        static float Ground(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, 50f, p.z), Vector3.down, Hits, 100f, ~0, QueryTriggerInteraction.Ignore);
            float y = 0f;
            bool found = false;
            for (int i = 0; i < n; i++)
                if (!found || Hits[i].point.y < y) { y = Hits[i].point.y; found = true; }
            return found ? y : 0f;
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static Material Mat(string name, Color color)
        {
            string path = $"{MatFolder}/M_{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            System.IO.Directory.CreateDirectory(MatFolder);
            var shader = GraphicsSettings.currentRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") : null;
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = "M_" + name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
