using System;
using LittlePrince.GuidedWalk;
using LittlePrince.HandTracking;
using UnityEngine;
using UnityEngine.Events;

namespace LittlePrince.Stages.Village
{
    /// <summary>
    /// Hide and seek with the fox. The fox waits hidden behind a tree at each
    /// hiding spot. When the guided walk stops at that spot's <see cref="PathStop"/>
    /// (mode "Resume"), the fox peeks out; the visitor looks around with the hand
    /// and points at it (or pinches it) to find it. The fox then runs to the next
    /// hiding spot and the walk goes on. After the last spot it runs to the final spot.
    ///
    /// Hiding spot transform = where the fox stands while peeking, facing the visitor.
    /// Hidden Offset = where it waits, in the spot's local space (behind the tree).
    /// </summary>
    public class FoxHideAndSeek : MonoBehaviour
    {
        [Serializable]
        public class HidingSpot
        {
            public PathStop stop;
            public Transform spot;
            [Tooltip("Where the fox hides, relative to the spot (x = right, z = toward the visitor).")]
            public Vector3 hiddenOffset = new Vector3(0.9f, 0f, -1.5f);

            public Vector3 HiddenPosition => spot.TransformPoint(hiddenOffset);
        }

        [Tooltip("The fox: needs a HandInteractable (and so a Collider) to be pointed at.")]
        public HandInteractable fox;
        public HidingSpot[] spots = new HidingSpot[0];
        [Tooltip("Where the fox goes after the last hiding spot (the bush where you meet it).")]
        public Transform finalSpot;

        [Header("Finding")]
        [Tooltip("Seconds the cursor has to stay on the fox to find it (a pinch on it finds it right away).")]
        public float findSeconds = 0.8f;
        public float walkOnDelay = 1.2f;
        public string hint = "Look around... where is the fox hiding? Point at it.";
        public bool showHint = true;

        [Header("Movement")]
        public float peekSpeed = 1.5f;
        public float runSpeed = 4.5f;
        public float turnSpeed = 8f;

        [Header("Events (animations, sounds)")]
        public UnityEvent onPeek = new UnityEvent();
        public UnityEvent onFound = new UnityEvent();
        public UnityEvent onRunAway = new UnityEvent();
        public UnityEvent onReachedFinalSpot = new UnityEvent();

        public enum Phase { Hidden, Peeking, Found, Running, AtFinalSpot }
        public Phase CurrentPhase { get; private set; }
        public int CurrentSpot { get; private set; }

        GuidedPathWalker _walker;
        float _hoverTime, _resumeAt = -1f;
        Vector3 _runTarget;

        void OnEnable() => HandInteractable.AnySelected += OnSelected;
        void OnDisable() => HandInteractable.AnySelected -= OnSelected;

        void Start()
        {
            if (fox == null || spots.Length == 0 || spots[0].spot == null) { enabled = false; return; }
            CurrentSpot = 0;
            CurrentPhase = Phase.Hidden;
            fox.transform.SetPositionAndRotation(spots[0].HiddenPosition, spots[0].spot.rotation);
        }

        void Update()
        {
            if (_walker == null) _walker = FindObjectOfType<GuidedPathWalker>();
            float dt = Time.deltaTime;
            var current = CurrentSpot < spots.Length ? spots[CurrentSpot] : null;
            bool atStop = current != null && _walker != null && _walker.CurrentStop == current.stop;

            switch (CurrentPhase)
            {
                case Phase.Hidden:
                    MoveFox(current.HiddenPosition, peekSpeed, current.spot.forward, dt);
                    if (atStop) { CurrentPhase = Phase.Peeking; _hoverTime = 0f; onPeek?.Invoke(); }
                    break;

                case Phase.Peeking:
                    MoveFox(current.spot.position, peekSpeed, current.spot.forward, dt);
                    _hoverTime = fox.IsHovered ? _hoverTime + dt : 0f;
                    if (_hoverTime >= findSeconds) Found();
                    else if (!atStop) RunToNext(); // the stop timed out
                    break;

                case Phase.Found:
                    MoveFox(current.spot.position, peekSpeed, current.spot.forward, dt);
                    if (_resumeAt > 0f && Time.time >= _resumeAt)
                    {
                        _resumeAt = -1f;
                        if (atStop) _walker.Resume();
                    }
                    if (!atStop && _resumeAt < 0f) RunToNext();
                    break;

                case Phase.Running:
                    Vector3 dir = _runTarget - fox.transform.position;
                    dir.y = 0f;
                    MoveFox(_runTarget, runSpeed, dir, dt);
                    if ((fox.transform.position - _runTarget).sqrMagnitude < 0.0025f)
                    {
                        if (CurrentSpot < spots.Length) CurrentPhase = Phase.Hidden;
                        else { CurrentPhase = Phase.AtFinalSpot; onReachedFinalSpot?.Invoke(); }
                    }
                    break;

                case Phase.AtFinalSpot:
                    if (finalSpot != null) MoveFox(finalSpot.position, peekSpeed, finalSpot.forward, dt);
                    break;
            }
        }

        void OnSelected(HandInteractable h)
        {
            if (h == fox && CurrentPhase == Phase.Peeking) Found();
        }

        void Found()
        {
            CurrentPhase = Phase.Found;
            _resumeAt = Time.time + walkOnDelay;
            onFound?.Invoke();
        }

        void RunToNext()
        {
            CurrentSpot++;
            if (CurrentSpot < spots.Length) _runTarget = spots[CurrentSpot].HiddenPosition;
            else if (finalSpot != null) _runTarget = finalSpot.position;
            else { CurrentPhase = Phase.AtFinalSpot; return; }
            CurrentPhase = Phase.Running;
            onRunAway?.Invoke();
        }

        void MoveFox(Vector3 target, float speed, Vector3 facing, float dt)
        {
            var t = fox.transform;
            t.position = Vector3.MoveTowards(t.position, target, speed * dt);
            facing.y = 0f;
            if (facing.sqrMagnitude > 1e-4f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(facing), dt * turnSpeed);
        }

        void OnGUI()
        {
            if (!showHint || CurrentPhase != Phase.Peeking || string.IsNullOrEmpty(hint)) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            float w = Mathf.Min(720f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height - 110f, w, 60f), hint, style);
        }

        void OnDrawGizmos()
        {
            foreach (var s in spots)
            {
                if (s == null || s.spot == null) continue;
                Gizmos.color = new Color(1f, 0.55f, 0.2f);
                Gizmos.DrawWireSphere(s.spot.position + Vector3.up * 0.3f, 0.3f);
                Gizmos.DrawLine(s.spot.position, s.HiddenPosition);
                Gizmos.DrawWireCube(s.HiddenPosition + Vector3.up * 0.3f, Vector3.one * 0.4f);
                if (s.stop != null) { Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.4f); Gizmos.DrawLine(s.stop.transform.position, s.spot.position); }
            }
            if (finalSpot != null) { Gizmos.color = new Color(1f, 0.4f, 0.1f); Gizmos.DrawWireSphere(finalSpot.position + Vector3.up * 0.3f, 0.4f); }
        }

        [ContextMenu("Test: find the fox now")]
        void TestFind()
        {
            if (!Application.isPlaying) { Debug.Log("Enter Play mode first."); return; }
            if (CurrentPhase == Phase.Peeking) Found();
            else Debug.Log($"[Village] The fox is not peeking right now ({CurrentPhase}).");
        }
    }
}
