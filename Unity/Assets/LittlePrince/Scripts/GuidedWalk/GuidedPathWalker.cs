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
    ///   Hand left / right / up / down -> look around a little (like turning your head)
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

        float _distance, _speed, _bodyYaw, _lookYaw, _lookPitch;
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
            float targetLookYaw = _lookYaw, targetLookPitch = _lookPitch;
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
            }
            _lookYaw = Mathf.Lerp(_lookYaw, targetLookYaw, dt * lookSmoothing);
            _lookPitch = Mathf.Lerp(_lookPitch, targetLookPitch, dt * lookSmoothing);

            Place(snapRotation: false);
        }

        void Arrive(int index)
        {
            _nextStop = index;
            CurrentStop = _stops[index].stop;
            CurrentStop.Arrive();
            StopReached?.Invoke(CurrentStop);
        }

        void Place(bool snapRotation)
        {
            Vector3 pos = path.PositionAt(_distance);
            if (snapToGround && Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 20f,
                                                groundLayers, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
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

        float ApplyDeadZone(float v)
        {
            float a = Mathf.Abs(v);
            if (a < deadZone) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((a - deadZone) / (0.5f - deadZone)) * 0.5f;
        }
    }
}
