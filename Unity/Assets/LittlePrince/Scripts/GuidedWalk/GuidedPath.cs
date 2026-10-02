using System.Collections.Generic;
using UnityEngine;

namespace LittlePrince.GuidedWalk
{
    /// <summary>
    /// A smooth path for the guided walk. Its CHILD objects are the waypoints, in
    /// Hierarchy order: move them around in the Scene view and the curve follows.
    /// Put waypoints on the ground; the camera height comes from the Player's camera.
    /// Add a <see cref="PathStop"/> to a waypoint to make the walk pause there.
    /// </summary>
    [ExecuteAlways]
    public class GuidedPath : MonoBehaviour
    {
        [Tooltip("Go back to the first waypoint after the last one.")]
        public bool loop = false;
        [Range(4, 32)] public int samplesPerSegment = 16;
        public Color gizmoColor = new Color(1f, 0.8f, 0.4f);

        public float Length { get; private set; }

        readonly List<Vector3> _samples = new List<Vector3>();
        readonly List<float> _distances = new List<float>();
        readonly List<float> _waypointDistances = new List<float>();
        readonly List<Transform> _waypoints = new List<Transform>();

        public IReadOnlyList<Transform> Waypoints => _waypoints;

        /// <summary>Recompute the curve from the child waypoints.</summary>
        public void Rebuild()
        {
            _waypoints.Clear();
            foreach (Transform child in transform) _waypoints.Add(child);
            _samples.Clear();
            _distances.Clear();
            _waypointDistances.Clear();
            Length = 0f;
            int n = _waypoints.Count;
            if (n == 0) return;
            if (n == 1)
            {
                _samples.Add(_waypoints[0].position);
                _distances.Add(0f);
                _waypointDistances.Add(0f);
                return;
            }

            int segments = loop ? n : n - 1;
            for (int s = 0; s < segments; s++)
            {
                Vector3 p0 = Point(s - 1), p1 = Point(s), p2 = Point(s + 1), p3 = Point(s + 2);
                for (int k = 0; k < samplesPerSegment; k++)
                {
                    if (k == 0) _waypointDistances.Add(Length);
                    AddSample(CatmullRom(p0, p1, p2, p3, k / (float)samplesPerSegment));
                }
            }
            AddSample(loop ? Point(0) : Point(n - 1));
            if (!loop) _waypointDistances.Add(Length);
        }

        void AddSample(Vector3 p)
        {
            if (_samples.Count > 0) Length += Vector3.Distance(_samples[_samples.Count - 1], p);
            _samples.Add(p);
            _distances.Add(Length);
        }

        Vector3 Point(int i)
        {
            int n = _waypoints.Count;
            if (loop) i = ((i % n) + n) % n;
            else i = Mathf.Clamp(i, 0, n - 1);
            return _waypoints[i].position;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>Distance along the path at which waypoint (child) <paramref name="index"/> sits.</summary>
        public float DistanceOfWaypoint(int index) =>
            index >= 0 && index < _waypointDistances.Count ? _waypointDistances[index] : 0f;

        public float Wrap(float d) => loop && Length > 0f ? Mathf.Repeat(d, Length) : Mathf.Clamp(d, 0f, Length);

        public Vector3 PositionAt(float distance)
        {
            if (_samples.Count == 0) return transform.position;
            if (_samples.Count == 1) return _samples[0];
            distance = Wrap(distance);
            int i = _distances.BinarySearch(distance);
            if (i >= 0) return _samples[i];
            i = ~i; // first sample with a larger distance
            if (i <= 0) return _samples[0];
            if (i >= _samples.Count) return _samples[_samples.Count - 1];
            float d0 = _distances[i - 1], d1 = _distances[i];
            return Vector3.Lerp(_samples[i - 1], _samples[i], d1 > d0 ? (distance - d0) / (d1 - d0) : 0f);
        }

        /// <summary>Flat walking direction at a distance along the path.</summary>
        public Vector3 DirectionAt(float distance, float lookAhead = 1f)
        {
            Vector3 a = PositionAt(distance);
            Vector3 b = PositionAt(loop ? distance + lookAhead : Mathf.Min(distance + lookAhead, Length));
            if ((b - a).sqrMagnitude < 1e-6f) b = a + (a - PositionAt(distance - lookAhead));
            Vector3 d = b - a;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) Rebuild();
            Gizmos.color = gizmoColor;
            for (int i = 1; i < _samples.Count; i++) Gizmos.DrawLine(_samples[i - 1], _samples[i]);
            foreach (var w in _waypoints)
            {
                bool stop = w.GetComponent<PathStop>() != null;
                Gizmos.color = stop ? new Color(1f, 0.45f, 0.45f) : gizmoColor;
                Gizmos.DrawSphere(w.position, stop ? 0.35f : 0.18f);
            }
        }
    }
}
