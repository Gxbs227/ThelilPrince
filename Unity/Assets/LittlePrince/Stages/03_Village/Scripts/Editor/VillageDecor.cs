using System.Collections.Generic;
using System.Linq;
using LittlePrince.GuidedWalk;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LittlePrince.Stages.Village.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Stage 2 - Village > Decorate Village (Suntail)
    ///
    /// Spreads the Suntail Village nature and props over the village (up to the wheat field):
    ///   - grass lining both sides of the village path (its centre stays clear so it still
    ///     reads as a path), and looser patches across the village
    ///   - flowers along the path and in front of houses, bushes against walls, stones
    ///   - a few props (barrels, boxes, sacks, logs, carts) beside each house
    /// Nothing goes through houses, fences or anything else with a collider, onto the
    /// children, their hiding spots or the stops. Everything lands under
    /// "Village Layout/Village Decor": run it again to start over (other objects are kept).
    /// </summary>
    public static class VillageDecor
    {
        const string DecorName = "Village Decor";
        const string Suntail = "Assets/Suntail Village/Prefabs";

        static readonly string[] GrassNames = { "Nature/Grass_1", "Nature/Grass_2" };
        static readonly string[] FlowerNames = { "Nature/Flowers_Purple", "Nature/Flowers_Yellow" };
        static readonly string[] BushNames = { "Nature/Bush_1", "Nature/Bush_2" };
        static readonly string[] StoneNames = { "Nature/Stone_1", "Nature/Stone_2", "Nature/Stone_3", "Nature/Stone_4" };
        static readonly string[] PropNames =
        {
            "Environment/Barrel", "Environment/Box_1", "Environment/Box_2", "Environment/Bag_1", "Environment/Bag_2",
            "Environment/Bag_3", "Environment/Log_2", "Environment/Log_3", "Environment/Cart_1", "Environment/Washtub",
            "Props/Bucket", "Props/Pumpkin",
        };

        [MenuItem("Little Prince/Stage 2 - Village/Decorate Village (Suntail)")]
        static void Decorate()
        {
            var root = GameObject.Find("Village Layout");
            var path = root != null ? root.GetComponentInChildren<GuidedPath>(true) : null;
            if (path == null)
            {
                EditorUtility.DisplayDialog("Village", "No \"Village Layout\" with a path in the open scene.", "OK");
                return;
            }
            var grass = Load(GrassNames);
            if (grass.Count == 0)
            {
                EditorUtility.DisplayDialog("Village", $"Suntail Village prefabs not found in {Suntail}.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Decorate village");
            var old = root.transform.Find(DecorName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Physics.SyncTransforms();
            path.Rebuild();

            var decor = new GameObject(DecorName).transform;
            Undo.RegisterCreatedObjectUndo(decor.gameObject, "Decorate village");
            decor.SetParent(root.transform, false);
            Transform Group(string n) { var t = new GameObject(n).transform; t.SetParent(decor, false); return t; }
            var grassRoot = Group("Grass");
            var flowerRoot = Group("Flowers");
            var bushRoot = Group("Bushes and Stones");
            var propRoot = Group("Props");

            float villageEnd = VillageEnd(root, path);
            var keepClear = KeepClear(root, path);
            var rng = new System.Random(11);
            int nGrass = 0, nFlowers = 0, nBushes = 0, nProps = 0;

            // ---- Grass lining the path ---------------------------------------------------------
            float step = Mathf.Max(0.35f, Footprint(grass) * 0.55f);
            for (float d = 0f; d < villageEnd; d += step)
            {
                Vector3 at = path.PositionAt(d);
                Vector3 right = Vector3.Cross(Vector3.up, path.DirectionAt(d, 1f));
                foreach (float side in new[] { -1f, 1f })
                    for (int k = 0; k < 2; k++)
                    {
                        // denser right at the edge of the path, thinning out further away
                        float t = (float)rng.NextDouble();
                        float off = Mathf.Lerp(0.75f, 3.2f, t * t);
                        var p = at + right * side * off + Vector3.Cross(right, Vector3.up) * Range(rng, -step, step);
                        if (Place(grass, grassRoot, p, rng, 0.8f, 1.3f, path, villageEnd, keepClear, 0.6f, collides: false)) nGrass++;
                    }
            }

            // ---- Looser grass, flowers, bushes and stones across the village ------------------
            var flowers = Load(FlowerNames);
            var bushes = Load(BushNames);
            var stones = Load(StoneNames);
            var area = VillageBounds(path, villageEnd, 25f);
            for (int i = 0; i < 2500; i++)
            {
                var p = new Vector3(Range(rng, area.min.x, area.max.x), 0f, Range(rng, area.min.z, area.max.z));
                path.ClosestDistance(p, out float gap);
                if (gap < 3f) continue; // the path edge is done above
                double r = rng.NextDouble();
                if (r < 0.62) { if (Place(grass, grassRoot, p, rng, 0.8f, 1.4f, path, villageEnd, keepClear, 1.2f, false)) nGrass++; }
                else if (r < 0.75) { if (Place(flowers, flowerRoot, p, rng, 0.8f, 1.2f, path, villageEnd, keepClear, 1.2f, false)) nFlowers++; }
                else if (r < 0.88) { if (Place(bushes, bushRoot, p, rng, 0.8f, 1.2f, path, villageEnd, keepClear, 2.2f, false)) nBushes++; }
                else { if (Place(stones, bushRoot, p, rng, 0.6f, 1.2f, path, villageEnd, keepClear, 2.2f, false)) nBushes++; }
            }

            // a few flowers right on the path edge
            for (float d = 2f; d < villageEnd; d += Range(rng, 3f, 6f))
            {
                Vector3 at = path.PositionAt(d);
                Vector3 right = Vector3.Cross(Vector3.up, path.DirectionAt(d, 1f));
                var p = at + right * (rng.NextDouble() < 0.5 ? -1f : 1f) * Range(rng, 1f, 1.8f);
                if (Place(flowers, flowerRoot, p, rng, 0.8f, 1.1f, path, villageEnd, keepClear, 0.8f, false)) nFlowers++;
            }

            // ---- Around each house: bushes against the walls, props at the sides -------------
            var props = Load(PropNames);
            foreach (var house in Houses(root.transform, decor))
            {
                var b = BoundsOf(house.gameObject);
                if (b.size.x < 1f || b.size.z < 1f) continue;
                var spots = AroundBox(b, 0.9f, 1.6f).OrderBy(_ => rng.Next()).ToList();
                int wantProps = rng.Next(2, 5), wantBushes = rng.Next(2, 4);
                foreach (var p in spots)
                {
                    if (wantProps > 0 && Place(props, propRoot, p, rng, 1f, 1f, path, villageEnd, keepClear, 2f, true)) { wantProps--; nProps++; }
                    else if (wantBushes > 0 && Place(bushes, bushRoot, p, rng, 0.8f, 1.2f, path, villageEnd, keepClear, 1.6f, false)) { wantBushes--; nBushes++; }
                    else if (rng.NextDouble() < 0.5 && Place(flowers, flowerRoot, p, rng, 0.8f, 1.1f, path, villageEnd, keepClear, 1.2f, false)) nFlowers++;
                    if (wantProps <= 0 && wantBushes <= 0) break;
                }
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = decor.gameObject;
            Debug.Log($"[Village] Decorated: {nGrass} grass, {nFlowers} flowers, {nBushes} bushes/stones, {nProps} props " +
                      $"(under \"{DecorName}\"). Delete what you don't like; run it again to start over.");
        }

        // ------------------------------------------------------------------------------
        /// <summary>Instantiates one of <paramref name="prefabs"/> at p if the spot is free. True if placed.</summary>
        static bool Place(List<GameObject> prefabs, Transform parent, Vector3 p, System.Random rng, float minScale, float maxScale,
                          GuidedPath path, float villageEnd, List<(Vector3 pos, float r)> keepClear, float minPathGap, bool collides)
        {
            if (prefabs.Count == 0) return false;
            float along = path.ClosestDistance(p, out float gap);
            if (gap < minPathGap || along > villageEnd) return false;
            foreach (var c in keepClear)
                if ((new Vector2(c.pos.x - p.x, c.pos.z - p.z)).sqrMagnitude < c.r * c.r) return false;
            p.y = Ground(p);
            float radius = collides ? 0.6f : 0.25f;
            if (Physics.CheckSphere(p + Vector3.up * (radius + 0.2f), radius, ~0, QueryTriggerInteraction.Ignore)) return false;

            var prefab = prefabs[rng.Next(prefabs.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(go, "Decorate village");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f));
            go.transform.localScale = prefab.transform.localScale * Range(rng, minScale, maxScale);
            if (collides) Physics.SyncTransforms(); // so the next prop doesn't land on this one
            return true;
        }

        static List<GameObject> Load(IEnumerable<string> names) =>
            names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{Suntail}/{n}.prefab")).Where(g => g != null).ToList();

        /// <summary>Path distance where the wheat field starts (the village ends there).</summary>
        static float VillageEnd(GameObject root, GuidedPath path)
        {
            var wheat = root.GetComponentInChildren<WheatField>(true);
            if (wheat == null) return path.Length;
            float fieldMinX = wheat.transform.position.x - wheat.size.x * Mathf.Abs(wheat.transform.lossyScale.x) * 0.5f;
            for (float d = 0f; d < path.Length; d += 0.5f)
                if (path.PositionAt(d).x >= fieldMinX - 1f) return d;
            return path.Length;
        }

        static Bounds VillageBounds(GuidedPath path, float villageEnd, float margin)
        {
            var b = new Bounds(path.PositionAt(0f), Vector3.zero);
            for (float d = 0f; d <= villageEnd; d += 1f) b.Encapsulate(path.PositionAt(d));
            b.Expand(new Vector3(margin * 2f, 0f, margin * 2f));
            return b;
        }

        /// <summary>Children, their hiding spots and the path stops stay free.</summary>
        static List<(Vector3, float)> KeepClear(GameObject root, GuidedPath path)
        {
            var list = new List<(Vector3, float)>();
            foreach (var r in Object.FindObjectsByType<ChildRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                list.Add((r.transform.position, 1.2f));
                if (r.hideSpot != null) list.Add((r.hideSpot.position, 1.2f));
            }
            foreach (var s in path.GetComponentsInChildren<PathStop>(true)) list.Add((s.transform.position, 1.5f));
            return list;
        }

        /// <summary>The house objects you placed (anything under "Houses (placeholders)").</summary>
        static IEnumerable<Transform> Houses(Transform root, Transform decor)
        {
            var houses = root.Find("Houses (placeholders)");
            if (houses == null) yield break;
            foreach (Transform h in houses) if (!h.IsChildOf(decor)) yield return h;
        }

        /// <summary>Points around a box on the ground, between near and far metres outside it.</summary>
        static IEnumerable<Vector3> AroundBox(Bounds b, float near, float far)
        {
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                // distance from the centre to the box edge along dir
                float tx = Mathf.Abs(dir.x) > 1e-3f ? b.extents.x / Mathf.Abs(dir.x) : float.MaxValue;
                float tz = Mathf.Abs(dir.z) > 1e-3f ? b.extents.z / Mathf.Abs(dir.z) : float.MaxValue;
                float edge = Mathf.Min(tx, tz);
                foreach (float o in new[] { near, far })
                    yield return new Vector3(b.center.x, b.min.y, b.center.z) + dir * (edge + o);
            }
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Typical width of a prefab (so grass spacing fits the clump size).</summary>
        static float Footprint(List<GameObject> prefabs)
        {
            float sum = 0f;
            int n = 0;
            foreach (var p in prefabs)
            {
                var rs = p.GetComponentsInChildren<Renderer>();
                foreach (var r in rs)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var s = Vector3.Scale(mf.sharedMesh.bounds.size, r.transform.lossyScale);
                    sum += (s.x + s.z) * 0.5f;
                    n++;
                    break;
                }
            }
            return n > 0 ? sum / n : 0.8f;
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
    }
}
