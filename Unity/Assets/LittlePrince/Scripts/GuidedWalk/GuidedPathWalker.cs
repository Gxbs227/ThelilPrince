using System;
using System.Collections.Generic;
using LittlePrince.HandTracking;
using UnityEngine;
using UnityEngine.Events;

namespace LittlePrince.GuidedWalk
{
    /// <summary>
    /// Guided walk: moves the Player gently along a <see cref="GuidedPath"/>, pausing
    /// at <see cref="PathStop"/>s. The hand never has to steer:
    ///
    ///   Hand left / right / up / down -> look around a little (like turning your head);
    ///                                    keep it near the edge and the view keeps turning
    ///                                    (Exploring), so you can look up at roofs, down the
    ///                                    streets, behind you... It drifts back when centred.
    ///   Point                         -> aim the cursor (HandCursor) at things
    ///   Pinch                         -> touch; the view holds still while pinching
    ///   Fist                          -> pause the walk (rest), open the hand to continue
    ///
    /// Put it on the Player (next to or instead of HandCameraController; the
    /// controller is switched off while this is active).
    /// </summary>
    public class GuidedPathWalker : MonoBehaviour
    {
        public GuidedPath path;
        public bool startOnPlay = true;
        [Tooltip("The child camera (look up/down is applied here).")]
        public Transform pitchPivot;

        [Header("Walking")]
        public float speed = 1.1f;
        [Tooltip("How gently it starts and stops. Lower = softer.")]
        public float acceleration = 0.5f;
        [Tooltip("Start slowing down this many metres before a stop.")]
        public float slowDownDistance = 2.5f;
        [Tooltip("Stick to the ground below the path (terrain, hills).")]
        public bool snapToGround = true;
        public LayerMask groundLayers = ~0;

        [Header("Looking around with the hand")]
        [Tooltip("How far the head turns left/right when the hand is at the edge (degrees).")]
        public float lookAroundYaw = 35f;
        public float lookAroundPitch = 18f;
        [Range(0f, 0.4f)] public float deadZone = 0.1f;
        public float lookSmoothing = 3f;
        [Tooltip("How quickly the body turns to follow the path's curves.")]
        public float turnSmoothing = 2f;

        [Header("Exploring (hand held near the edge keeps turning the view)")]
        public bool edgePan = true;
        [Tooltip("How far from the centre the hand must be to keep turning (0.5 = the edge of the image).")]
        [Range(0.15f, 0.48f)] public float panZone = 0.32f;
        [Tooltip("Turning speed with the hand right at the edge (degrees per second).")]
        public float panSpeed = 50f;
        [Tooltip("Furthest the view can turn left/right from the walking direction (degrees).")]
        public float maxYaw = 120f;
        public float maxPitchUp = 45f;
        public float maxPitchDown = 40f;
        [Tooltip("Seconds with the hand centred (or no hand) before the view drifts back to the path.")]
        public float recenterDelay = 2.5f;
        public float recenterSpeed = 30f;

        [Header("Hand")]
        [Tooltip("Make a fist to pause the walk; open the hand to continue.")]
        public bool fistPauses = true;

        [Header("Events")]
        public UnityEvent onPathFinished = new UnityEvent();

        public event Action<PathStop> StopReached;
        public event Action<PathStop> StopLeft;

        /// <summary>Set false to hold the walk (e.g. during a story step where the visitor should stay).</summary>
        public bool AllowMoving { get; set; } = true;
        public PathStop CurrentStop { get; private set; }
        public bool IsFinished { get; private set; }
        public float Distance => _distance;
        public float CurrentSpeed => _speed;

        float _distance, _speed, _bodyYaw, _lookYaw, _lookPitch, _panYaw, _panPitch, _centredFor;
        bool _recenterNow;
        readonly List<(float distance, PathStop stop)> _stops = new List<(float, PathStop)>();
        int _nextStop;
        bool _started;
        HandCameraController _manualController;
        CharacterController _cc;

        void OnEnable()
        {
            if (pitchPivot == null)
            {
                var cam = GetComponentInChildren<Camera>();
                pitchPivot = cam != null ? cam.transform : transform;
            }
            _manualController = GetComponent<HandCameraController>();
            if (_manualController != null) _manualController.enabled = false;
            _cc = GetComponent<CharacterController>();
            if (_cc != null) _cc.enabled = false; // we place the player ourselves
        }

        void OnDisable()
        {
            if (_manualController != null) _manualController.enabled = true;
            if (_cc != null) _cc.enabled = true;
        }

        void Start()
        {
            if (startOnPlay && path != null && !_started) StartPath(path);
        }

        /// <summary>Start walking a path from its first waypoint.</summary>
        public void StartPath(GuidedPath newPath)
        {
            if (CurrentStop != null) { var s = CurrentStop; CurrentStop = null; s.Leave(); }
            _started = true;
            path = newPath;
            _distance = 0f;
            _speed = 0f;
            _nextStop = 0;
            IsFinished = false;
            _stops.Clear();
            if (path == null) return;

            path.Rebuild();
            for (int i = 0; i < path.Waypoints.Count; i++)
            {
                var stop = path.Waypoints[i].GetComponent<PathStop>();
                if (stop != null && stop.isActiveAndEnabled) _stops.Add((path.DistanceOfWaypoint(i), stop));
            }
            _stops.Sort((a, b) => a.distance.CompareTo(b.distance));

            // A stop on the very first waypoint is handled right away.
            Place(snapRotation: true);
            if (_stops.Count > 0 && _stops[0].distance <= 0.01f) Arrive(0);
        }

        /// <summary>Stop following any path (the player stays where it is).</summary>
        public void StopPath()
        {
            if (CurrentStop != null) { var s = CurrentStop; CurrentStop = null; s.Leave(); }
            path = null;
            _stops.Clear();
        }

        /// <summary>Continue from the current stop (for PathStop mode "Resume", UI, or your scripts).</summary>
        public void Resume()
        {
            if (CurrentStop == null) return;
            var s = CurrentStop;
            CurrentStop = null;
            _nextStop++;
            s.Leave();
            StopLeft?.Invoke(s);
        }

        void Update()
        {
            if (path == null) return;
            float dt = Time.deltaTime;
            var receiver = HandTrackingReceiver.Instance;
            var hand = receiver != null ? receiver.PrimaryHand : null;

            // ---- Stops -------------------------------------------------------------------
            if (CurrentStop != null && CurrentStop.Tick(dt)) Resume();

            bool resting = fistPauses && hand != null && hand.Gesture == HandGesture.Fist;
            bool hold = !AllowMoving || CurrentStop != null || resting || IsFinished;

            float target = hold ? 0f : speed;
            if (!hold && _nextStop < _stops.Count && slowDownDistance > 0f)
            {
                float toStop = _stops[_nextStop].distance - _distance;
                if (toStop >= 0f) target *= Mathf.Clamp(toStop / slowDownDistance, 0.2f, 1f);
            }
            _speed = Mathf.MoveTowards(_speed, target, acceleration * dt);
            _distance += _speed * dt;

            if (_nextStop < _stops.Count && _distance >= _stops[_nextStop].distance && CurrentStop == null)
            {
                _distance = _stops[_nextStop].distance;
                _speed = 0f;
                Arrive(_nextStop);
            }
            else if (_distance >= path.Length)
            {
                if (path.loop)
                {
                    _distance -= path.Length;
                    _nextStop = 0;
                }
                else if (!IsFinished)
                {
                    _distance = path.Length;
                    IsFinished = true;
                    onPathFinished?.Invoke();
                }
            }

            // ---- Looking around with the hand -------------------------------------------
            float targetLookYaw = _lookYaw - _panYaw, targetLookPitch = _lookPitch - _panPitch;
            bool centred = true;
            if (hand == null)
            {
                targetLookYaw = 0f;
                targetLookPitch = 0f;
            }
            else if (!hand.IsPinching) // pinching holds the view still for touching
            {
                Vector2 offset = hand.Palm - new Vector2(0.5f, 0.5f);
                targetLookYaw = ApplyDeadZone(offset.x) * 2f * lookAroundYaw;
                targetLookPitch = -ApplyDeadZone(offset.y) * 2f * lookAroundPitch;
                centred = Mathf.Abs(offset.x) < deadZone && Mathf.Abs(offset.y) < deadZone;

                if (edgePan)
                {
                    float px = EdgeAmount(offset.x), py = EdgeAmount(offset.y);
                    if (px != 0f || py != 0f) _recenterNow = false;
                    _panYaw += px * panSpeed * dt;
                    _panPitch -= py * panSpeed * dt; // hand up = look up (negative pitch)
                }
            }
            else centred = false;

            // Drift back toward the path after a while (right away when arriving at a stop).
            _centredFor = centred ? _centredFor + dt : 0f;
            if (_recenterNow || _centredFor > recenterDelay)
            {
                float step = recenterSpeed * (_recenterNow ? 3f : 1f) * dt;
                _panYaw = Mathf.MoveTowards(_panYaw, 0f, step);
                _panPitch = Mathf.MoveTowards(_panPitch, 0f, step);
                if (_panYaw == 0f && _panPitch == 0f) _recenterNow = false;
            }
            _panYaw = Mathf.Clamp(_panYaw, -Mathf.Max(0f, maxYaw - lookAroundYaw), Mathf.Max(0f, maxYaw - lookAroundYaw));
            _panPitch = Mathf.Clamp(_panPitch, -Mathf.Max(0f, maxPitchUp - lookAroundPitch), Mathf.Max(0f, maxPitchDown - lookAroundPitch));

            _lookYaw = Mathf.Lerp(_lookYaw, targetLookYaw + _panYaw, dt * lookSmoothing);
            _lookPitch = Mathf.Lerp(_lookPitch, targetLookPitch + _panPitch, dt * lookSmoothing);

            Place(snapRotation: false);
        }

        void Arrive(int index)
        {
            _nextStop = index;
            CurrentStop = _stops[index].stop;
            _recenterNow = true; // face what the stop is about
            CurrentStop.Arrive();
            StopReached?.Invoke(CurrentStop);
        }

        void Place(bool snapRotation)
        {
            Vector3 pos = path.PositionAt(_distance);
            if (snapToGround && TryGround(pos, snapRotation ? pos.y : transform.position.y, out float groundY)) pos.y = groundY;
            transform.position = pos;

            // Body faces along the path, or toward the stop's lookAt target while waiting.
            Vector3 dir = path.DirectionAt(_distance, 1.5f);
            if (CurrentStop != null && CurrentStop.lookAt != null)
            {
                Vector3 to = CurrentStop.lookAt.position - pos;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) dir = to.normalized;
            }
            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _bodyYaw = snapRotation ? targetYaw : Mathf.LerpAngle(_bodyYaw, targetYaw, Time.deltaTime * turnSmoothing);

            if (pitchPivot == transform)
                transform.rotation = Quaternion.Euler(_lookPitch, _bodyYaw + _lookYaw, 0f);
            else
            {
                transform.rotation = Quaternion.Euler(0f, _bodyYaw + _lookYaw, 0f);
                pitchPivot.localRotation = Quaternion.Euler(_lookPitch, 0f, 0f);
            }
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>
        /// Ground under the path: the highest walkable surface no more than half a metre above the
        /// feet (steps, bridges), so roofs, eaves, fences or a well beside the path are not climbed onto.
        /// </summary>
        bool TryGround(Vector3 pos, float feetY, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(new Vector3(pos.x, feetY + 20f, pos.z), Vector3.down, Hits, 60f,
                                            groundLayers, QueryTriggerInteraction.Ignore);
            bool step = false, any = false;
            float lowest = 0f;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.transform.IsChildOf(transform)) continue;
                if (!any || h.point.y < lowest) lowest = h.point.y;
                any = true;
                if (h.normal.y > 0.6f && h.point.y <= feetY + 0.5f && (!step || h.point.y > y))
                {
                    y = h.point.y;
                    step = true;
                }
            }
            if (!step && any) y = lowest;
            return any;
        }

        /// <summary>-1..1: how far past the pan zone the hand is (0 inside it).</summary>
        float EdgeAmount(float v)
        {
            float a = Mathf.Abs(v);
            if (a <= panZone) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((a - panZone) / Mathf.Max(0.01f, 0.5f - panZone));
        }

        float ApplyDeadZone(float v)
        {
            float a = Mathf.Abs(v);
            if (a < deadZone) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((a - deadZone) / (0.5f - deadZone)) * 0.5f;
        }
    }
}
