using System;
using System.Collections.Generic;
using LittlePrince.GuidedWalk;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittlePrince.Stages.Village
{
    /// <summary>
    /// A wheat field, generated when the scene loads (nothing heavy is saved in the
    /// scene). Tufts of wheat (three crossed cards each) fill the box Size around this
    /// object, sitting on the ground below, and leave a trail along the guided path
    /// plus clearings around the listed spots (where the fox peeks out, the meeting).
    ///
    /// Move / resize it in the Scene view (yellow box); it regenerates by itself.
    /// Scaling the object enlarges the area only: the wheat keeps its real height.
    /// Anything with a collider (houses, rocks) gets no wheat growing through it.
    /// Material: Wheat (Grass shader graph, so it sways in the wind).
    /// </summary>
    [ExecuteAlways]
    public class WheatField : MonoBehaviour
    {
        [Serializable]
        public class Clearing
        {
            public Transform center;
            public float radius = 1.2f;
        }

        public Material material;
        [Tooltip("Field size in metres (x, z), centred on this object.")]
        public Vector2 size = new Vector2(30f, 24f);
        [Tooltip("Tufts per square metre.")]
        [Range(0.5f, 8f)] public float density = 3f;
        public Vector2 heightRange = new Vector2(0.95f, 1.3f);
        public float tuftWidth = 0.7f;
        public int seed = 12;

        [Header("Leave room for")]
        [Tooltip("Keep a trail free along this path.")]
        public GuidedPath trail;
        public float trailHalfWidth = 0.9f;
        [Tooltip("Shorter wheat near the trail edge, so it doesn't brush the camera.")]
        public float trailSoftEdge = 0.8f;
        public Clearing[] clearings = new Clearing[0];
        [Tooltip("Soft, ragged edge of the field (metres).")]
        public float edgeFade = 2f;

        [Header("Ground")]
        public bool snapToGround = true;
        public LayerMask groundLayers = ~0;
        [Tooltip("No wheat where something with a collider stands (houses, walls, rocks...).")]
        public bool avoidColliders = true;
        [Tooltip("Gap kept around those colliders (metres).")]
        public float colliderMargin = 0.35f;

        const int TuftsPerChunk = 2500; // 3 cards x 4 verts -> 30k verts per mesh
        const string GeneratedName = "Wheat (generated)";
        readonly List<GameObject> _chunks = new List<GameObject>();
        readonly List<Mesh> _meshes = new List<Mesh>();
        bool _dirty;

        void OnEnable()
        {
            Generate();
            _dirty = true; // once more on the first frame, when every ground collider is surely there
        }
        void OnDisable() => Clear();

        void OnValidate() => _dirty = true;

        void Update()
        {
            bool moved = !Application.isPlaying && transform.hasChanged;
            if (!_dirty && !moved) return;
            _dirty = false;
            Generate();
        }

        [ContextMenu("Regenerate")]
        public void Generate()
        {
            Clear();
            if (material == null) return;
            if (trail != null) trail.Rebuild();
            Physics.SyncTransforms();

            var rng = new System.Random(seed);
            Vector3 scale = transform.lossyScale;
            float area = Mathf.Abs(size.x * scale.x * size.y * scale.z); // real square metres
            int count = Mathf.Clamp(Mathf.RoundToInt(area * density), 0, 200000);

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int tuftsInChunk = 0;

            for (int i = 0; i < count; i++)
            {
                float lx = ((float)rng.NextDouble() - 0.5f) * size.x;
                float lz = ((float)rng.NextDouble() - 0.5f) * size.y;
                float r1 = (float)rng.NextDouble(), r2 = (float)rng.NextDouble(), r3 = (float)rng.NextDouble();

                // Ragged edges
                float edge = Mathf.Min(size.x * 0.5f - Mathf.Abs(lx), size.y * 0.5f - Mathf.Abs(lz));
                if (edgeFade > 0f && edge < edgeFade && r1 > edge / edgeFade) continue;

                Vector3 world = transform.TransformPoint(new Vector3(lx, 0f, lz));
                float heightScale = 1f;
                if (!Allowed(world, ref heightScale)) continue;

                if (snapToGround && TryGround(world, out float gy)) world.y = gy;
                if (avoidColliders && Occupied(world)) continue;

                float h = Mathf.Lerp(heightRange.x, heightRange.y, r2) * heightScale;
                AddTuft(verts, normals, uvs, tris, world, h, tuftWidth * Mathf.Lerp(0.8f, 1.2f, r3), r1 * 360f,
                        transform.worldToLocalMatrix);

                if (++tuftsInChunk >= TuftsPerChunk)
                {
                    Flush(verts, normals, uvs, tris);
                    tuftsInChunk = 0;
                }
            }
            Flush(verts, normals, uvs, tris);
            transform.hasChanged = false;
        }

        bool Allowed(Vector3 world, ref float heightScale)
        {
            foreach (var c in clearings)
            {
                if (c == null || c.center == null) continue;
                Vector3 d = world - c.center.position;
                d.y = 0f;
                if (d.sqrMagnitude < c.radius * c.radius) return false;
            }
            if (trail != null && trail.Length > 0f)
            {
                trail.ClosestDistance(world, out float gap);
                if (gap < trailHalfWidth) return false;
                if (trailSoftEdge > 0f && gap < trailHalfWidth + trailSoftEdge)
                    heightScale = Mathf.Lerp(0.55f, 1f, (gap - trailHalfWidth) / trailSoftEdge);
            }
            return true;
        }

        /// <summary>Something with a collider stands here (checked from just above the ground up to the wheat's height).</summary>
        bool Occupied(Vector3 world)
        {
            float r = Mathf.Max(0.05f, colliderMargin);
            Vector3 bottom = world + Vector3.up * (r + 0.15f);
            Vector3 top = world + Vector3.up * Mathf.Max(r + 0.2f, heightRange.y);
            return Physics.CheckCapsule(bottom, top, r, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Builds the tuft in world units (so the object's scale doesn't stretch it), stored in local space.</summary>
        static void AddTuft(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 basePos,
                            float height, float width, float yaw, Matrix4x4 toLocal)
        {
            int start = v.Count;
            for (int k = 0; k < 3; k++)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw + k * 60f, 0f);
                Vector3 right = rot * Vector3.right * (width * 0.5f);
                Vector3 up = Vector3.up * height;
                Vector3 normal = rot * Vector3.forward;
                // Lean a touch so the field doesn't look combed.
                Vector3 lean = rot * new Vector3(0f, 0f, 0.08f * height);
                int i0 = v.Count;
                v.Add(basePos - right);
                v.Add(basePos + right);
                v.Add(basePos + right + up + lean);
                v.Add(basePos - right + up + lean);
                // Normals point mostly up: soft, even lighting on both faces (like grass).
                Vector3 nn = (Vector3.up * 0.8f + normal * 0.2f).normalized;
                n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f));
                uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
                t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
            }
            for (int i = start; i < v.Count; i++)
            {
                v[i] = toLocal.MultiplyPoint3x4(v[i]);
                n[i] = toLocal.MultiplyVector(n[i]).normalized;
            }
        }

        void Flush(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            if (v.Count == 0) return;
            var mesh = new Mesh { name = "Wheat", hideFlags = HideFlags.HideAndDontSave };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            _meshes.Add(mesh);

            var go = new GameObject(GeneratedName) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off; // thousands of cards: shadows cost a lot for little
            mr.receiveShadows = true;
            _chunks.Add(go);

            v.Clear(); n.Clear(); uv.Clear(); t.Clear();
        }

        void Clear()
        {
            foreach (var go in _chunks) if (go != null) DestroyImmediate(go);
            foreach (var m in _meshes) if (m != null) DestroyImmediate(m);
            _chunks.Clear();
            _meshes.Clear();
            // Leftovers from a domain reload
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name == GeneratedName && (c.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(c.gameObject);
            }
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>Lowest hit straight below (bushes and props don't count as ground).</summary>
        bool TryGround(Vector3 pos, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(pos + Vector3.up * 10f, Vector3.down, Hits, 40f, groundLayers,
                                            QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (!found || Hits[i].point.y < y) y = Hits[i].point.y;
                found = true;
            }
            return found;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.95f, 0.78f, 0.35f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.5f, new Vector3(size.x, 1f, size.y));
            Gizmos.matrix = Matrix4x4.identity;
            foreach (var c in clearings)
                if (c != null && c.center != null) Gizmos.DrawWireSphere(c.center.position, c.radius);
        }
    }
}
