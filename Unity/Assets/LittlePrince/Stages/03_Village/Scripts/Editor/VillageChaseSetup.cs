using System.Collections.Generic;
using System.Linq;
using LittlePrince.GuidedWalk;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LittlePrince.Stages.Village.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Stage 2 - Village > Update Scene: 2 Children + Wheat Field
    ///
    /// Updates the existing village layout in place (your models and decoration are kept):
    ///   - lowers the player's camera a little
    ///   - only two children: the boy waits at his wave stop, then runs ahead and the
    ///     visitor follows him; he hides behind a house and disappears, then the girl
    ///     appears and runs ahead to the wheat field, where she ducks into the wheat
    ///   - the other children's wave stops become plain waypoints
    ///   - the fox section becomes a wheat field (the fox hides in the wheat)
    /// Safe to run again: it rebuilds what it created.
    /// </summary>
    public static class VillageChaseSetup
    {
        public const float CameraHeight = 1.3f;
        const string WheatMaterialPath = "Assets/LittlePrince/Stages/03_Village/Materials/Wheat.mat";
        const string BoyName = "Child 1 - Boy";
        const string GirlName = "Child 2 - Girl";
        const string WheatName = "Wheat Field";
        const int BoyLeavesAt = 9;   // "09 Waypoint": he turns off behind the houses here
        const int GirlLeavesAt = 14; // "14 Waypoint": the edge of the wheat field

        [MenuItem("Little Prince/Stage 2 - Village/Update Scene: 2 Children + Wheat Field")]
        static void Menu()
        {
            var root = GameObject.Find("Village Layout");
            if (root == null)
            {
                EditorUtility.DisplayDialog("Village", "No \"Village Layout\" in the open scene. Open 03_Village first.", "OK");
                return;
            }
            if (Apply(root.transform))
            {
                EditorSceneManager.MarkSceneDirty(root.scene);
                Debug.Log("[Village] Updated: camera lowered, 2 children (boy -> girl), wheat field. Save the scene. " +
                          "Move \"Boy Hide Spot\" / \"Girl Hide Spot\" if you want them to hide somewhere else.");
            }
        }

        /// <summary>Applies the 2-children chase and the wheat field to a built village layout.</summary>
        public static bool Apply(Transform root)
        {
            var path = root.GetComponentInChildren<GuidedPath>(true);
            var kids = root.Find("Children (placeholders)");
            if (path == null || kids == null)
            {
                Debug.LogError("[Village] The layout has no path or no \"Children (placeholders)\".");
                return false;
            }
            Undo.SetCurrentGroupName("Village: 2 children + wheat field");
            path.Rebuild();

            LowerCamera();

            // ---- Children ------------------------------------------------------------------
            var boy = FindKid(kids, BoyName, "Child 1");
            var girl = FindKid(kids, GirlName, "Child 2");
            if (boy == null || girl == null)
            {
                Debug.LogError("[Village] Need \"Child 1\" and \"Child 2\" under \"Children (placeholders)\".");
                return false;
            }
            Undo.RecordObject(boy.gameObject, "Rename");
            boy.name = BoyName;
            Undo.RecordObject(girl.gameObject, "Rename");
            girl.name = GirlName;
            girl.gameObject.SetActive(true); // ChildRunner hides her itself on Play

            // Only the boy's wave stop stays; other children's stops become waypoints.
            PathStop boyStop = null;
            foreach (Transform w in path.transform.Cast<Transform>().ToList())
            {
                var stop = w.GetComponent<PathStop>();
                if (stop == null || !w.name.Contains("Child")) continue;
                if (w.name.Contains("Child 1"))
                {
                    boyStop = stop;
                    Undo.RecordObject(stop, "Boy stop");
                    stop.lookAt = boy;
                    continue;
                }
                var g = w.GetComponent<GestureStop>();
                if (g != null) Undo.DestroyObjectImmediate(g);
                Undo.DestroyObjectImmediate(stop);
                Undo.RecordObject(w.gameObject, "Rename");
                w.name = w.name.Substring(0, Mathf.Min(2, w.name.Length)) + " Waypoint";
            }
            if (boyStop == null) Debug.LogWarning("[Village] No \"Child 1\" stop found: the boy will wait forever.");

            var walker = Object.FindObjectOfType<GuidedPathWalker>();
            var boyLeave = Waypoint(path, BoyLeavesAt);
            var girlLeave = Waypoint(path, GirlLeavesAt);
            var markers = Marker(kids, "Hide Spots", null);

            var boyRun = Runner(boy);
            boyRun.walker = walker;
            boyRun.startAfterStop = boyStop;
            boyRun.hiddenUntilCalled = false;
            boyRun.leavePathAt = boyLeave;
            boyRun.hideSpot = Marker(markers, "Boy Hide Spot (behind a house)", OutOfSight(path, boyLeave, boyRun.leadDistance));
            boyRun.duckAtHideSpot = false;
            boyRun.followHint = "Follow him!";

            var girlRun = Runner(girl);
            girlRun.walker = walker;
            girlRun.startAfterStop = null;
            girlRun.hiddenUntilCalled = true;
            girlRun.leavePathAt = girlLeave;
            girlRun.hideSpot = Marker(markers, "Girl Hide Spot (in the wheat)", IntoTheField(path, girlLeave));
            girlRun.duckAtHideSpot = true;
            girlRun.followHint = "Follow her to the wheat field!";

            for (int i = boyRun.onHidden.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(boyRun.onHidden, i);
            UnityEventTools.AddVoidPersistentListener(boyRun.onHidden, girlRun.Appear);
            EditorUtility.SetDirty(boyRun);
            EditorUtility.SetDirty(girlRun);

            // ---- Wheat field -----------------------------------------------------------------
            var foxSection = root.Find("Fox Section");
            var game = root.GetComponentInChildren<FoxHideAndSeek>(true);
            BuildWheat(foxSection != null ? foxSection : root, path, girlLeave, game);
            return true;
        }

        static void LowerCamera()
        {
            foreach (var walker in Object.FindObjectsOfType<GuidedPathWalker>())
            {
                var cam = walker.pitchPivot != null && walker.pitchPivot != walker.transform
                    ? walker.pitchPivot
                    : walker.GetComponentInChildren<Camera>()?.transform;
                if (cam == null) continue;
                Undo.RecordObject(cam, "Lower camera");
                var p = cam.localPosition;
                cam.localPosition = new Vector3(p.x, CameraHeight, p.z);
            }
        }

        static void BuildWheat(Transform parent, GuidedPath path, Transform fieldStart, FoxHideAndSeek game)
        {
            var old = parent.Find(WheatName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // Everything from the field's edge to the end of the path, plus the fox's spots.
            var points = new List<Vector3>();
            bool inField = false;
            foreach (Transform w in path.transform)
            {
                if (w == fieldStart) inField = true;
                if (inField) points.Add(w.position);
            }
            var clearings = new List<WheatField.Clearing>();
            if (game != null)
            {
                foreach (var s in game.spots)
                    if (s != null && s.spot != null)
                    {
                        points.Add(s.spot.position);
                        clearings.Add(new WheatField.Clearing { center = s.spot, radius = 1.1f });
                    }
                if (game.finalSpot != null)
                {
                    points.Add(game.finalSpot.position);
                    clearings.Add(new WheatField.Clearing { center = game.finalSpot, radius = 2.4f });
                }
            }
            if (points.Count == 0) points.Add(fieldStart.position);
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            bounds.Expand(new Vector3(7f, 0f, 7f)); // 3.5 m around

            var go = new GameObject(WheatName);
            Undo.RegisterCreatedObjectUndo(go, "Wheat field");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(bounds.center.x, fieldStart.position.y, bounds.center.z);
            var field = go.AddComponent<WheatField>();
            field.material = AssetDatabase.LoadAssetAtPath<Material>(WheatMaterialPath);
            field.size = new Vector2(bounds.size.x, bounds.size.z);
            field.trail = path;
            field.clearings = clearings.ToArray();
            if (field.material == null) Debug.LogWarning($"[Village] {WheatMaterialPath} not found: assign a material to the Wheat Field.");
            field.Generate();
        }

        // ---- helpers ---------------------------------------------------------------------
        static Transform FindKid(Transform kids, string newName, string oldName)
        {
            var t = kids.Find(newName);
            if (t != null) return t;
            foreach (Transform c in kids) if (c.name == oldName) return c;
            return null;
        }

        static ChildRunner Runner(Transform kid)
        {
            var r = kid.GetComponent<ChildRunner>();
            if (r == null) r = Undo.AddComponent<ChildRunner>(kid.gameObject);
            else Undo.RecordObject(r, "Child runner");
            return r;
        }

        static Transform Waypoint(GuidedPath path, int number)
        {
            string prefix = number.ToString("00") + " ";
            foreach (Transform w in path.transform) if (w.name.StartsWith(prefix)) return w;
            int i = Mathf.Clamp(number - 1, 0, path.transform.childCount - 1);
            return path.transform.GetChild(i);
        }

        static Transform Marker(Transform parent, string name, Vector3? position)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                Undo.RegisterCreatedObjectUndo(t.gameObject, "Marker");
                t.SetParent(parent, false);
            }
            if (position.HasValue)
            {
                Undo.RecordObject(t, "Marker");
                t.position = position.Value;
            }
            return t;
        }

        /// <summary>
        /// A spot near <paramref name="leave"/> the child can run to in a straight line without
        /// going through anything, that the visitor (a few metres back on the path) can't see.
        /// </summary>
        static Vector3 OutOfSight(GuidedPath path, Transform leave, float lead)
        {
            Physics.SyncTransforms();
            Vector3 from = leave.position;
            float leaveD = path.ClosestDistance(from);
            Vector3 eye = path.PositionAt(Mathf.Max(0f, leaveD - lead)) + Vector3.up * CameraHeight;
            Vector3 chest = Vector3.up * 0.6f;

            Vector3? best = null, fallback = null;
            float bestDist = float.MaxValue, fallbackScore = float.MinValue;
            for (int ring = 1; ring <= 5; ring++)
            for (int k = 0; k < 24; k++)
            {
                float r = 1.5f + ring * 1.2f;
                float a = k * Mathf.PI * 2f / 24f;
                var c = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (Physics.CheckSphere(c + chest, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue; // inside a house
                if (Physics.Linecast(from + chest, c + chest, ~0, QueryTriggerInteraction.Ignore)) continue; // can't run there
                path.ClosestDistance(c, out float gap);
                if (gap < 1.5f) continue; // not on the path itself
                bool hidden = Physics.Linecast(eye, c + chest, ~0, QueryTriggerInteraction.Ignore);
                if (hidden && r < bestDist) { bestDist = r; best = c; }
                float score = Vector3.Distance(eye, c);
                if (score > fallbackScore) { fallbackScore = score; fallback = c; }
            }
            if (best == null)
                Debug.LogWarning($"[Village] No spot out of sight near {leave.name}: move \"Boy Hide Spot\" behind a house yourself.");
            return best ?? fallback ?? from + Vector3.left * 4f;
        }

        /// <summary>A few metres into the wheat, beside the trail.</summary>
        static Vector3 IntoTheField(GuidedPath path, Transform edge)
        {
            float d = path.ClosestDistance(edge.position);
            Vector3 fwd = path.DirectionAt(d, 3f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            foreach (float side in new[] { 1f, -1f })
            {
                var p = edge.position + fwd * 3f + right * side * 3.5f;
                path.ClosestDistance(p, out float gap);
                if (gap > 2.5f) return p;
            }
            return edge.position + fwd * 3f + right * 5f;
        }
    }
}
