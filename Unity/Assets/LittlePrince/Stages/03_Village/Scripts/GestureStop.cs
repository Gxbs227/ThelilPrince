using System;
using LittlePrince.GuidedWalk;
using LittlePrince.HandTracking;
using UnityEngine;
using UnityEngine.Events;

namespace LittlePrince.Stages.Village
{
    /// <summary>
    /// Put next to a <see cref="PathStop"/> (mode "Resume"): when the guided walk
    /// stops here, the visitor does one or more story gestures in order (e.g. wave
    /// to a child, or swipe the bush, take the paper, fold it), then the walk goes on.
    ///
    /// Hook animations / sounds into each step's On Done.
    /// Right-click the component header > "Test: do the current gesture" to try it without a camera.
    /// </summary>
    [RequireComponent(typeof(PathStop))]
    public class GestureStop : MonoBehaviour
    {
        [Serializable]
        public class Step
        {
            [Tooltip("Shown at the bottom of the screen while waiting.")]
            public string hint = "Wave hello!";
            [Tooltip("Any of these gestures counts.")]
            public StoryGesture[] gestures = { StoryGesture.WaveAny };
            public int repetitions = 1;
            public UnityEvent onDone = new UnityEvent();
        }

        public Step[] steps = { new Step() };
        [Tooltip("Seconds between the last gesture and walking on (time for the reaction).")]
        public float walkOnDelay = 1.5f;
        [Tooltip("If the stop times out (PathStop > Max Wait Seconds), still run the remaining On Done events " +
                 "so the story can go on (paper appears, birds fly...).")]
        public bool completeIfSkipped = true;
        public bool showHint = true;

        public UnityEvent onAllDone = new UnityEvent();

        public int CurrentStep { get; private set; }
        public bool IsActive => _walker != null && _walker.CurrentStop == _stop;

        PathStop _stop;
        GuidedPathWalker _walker;
        HandTrackingReceiver _receiver;
        bool _wasActive;
        int _count;
        float _lastCount = -999f, _resumeAt = -1f;
        const float Cooldown = 0.8f;

        void Awake() => _stop = GetComponent<PathStop>();

        void OnDisable()
        {
            if (_receiver != null) _receiver.StoryGestureStarted -= OnGesture;
            _receiver = null;
        }

        void Update()
        {
            if (_walker == null) _walker = FindObjectOfType<GuidedPathWalker>();
            var r = HandTrackingReceiver.Instance;
            if (r != null && r != _receiver)
            {
                if (_receiver != null) _receiver.StoryGestureStarted -= OnGesture;
                _receiver = r;
                r.StoryGestureStarted += OnGesture;
            }

            bool active = IsActive;
            if (active && !_wasActive) { CurrentStep = 0; _count = 0; _resumeAt = -1f; }
            if (!active && _wasActive && completeIfSkipped && CurrentStep < steps.Length)
            {
                while (CurrentStep < steps.Length) steps[CurrentStep++].onDone?.Invoke();
                onAllDone?.Invoke();
            }
            _wasActive = active;

            if (active && _resumeAt > 0f && Time.time >= _resumeAt)
            {
                _resumeAt = -1f;
                _walker.Resume();
            }
        }

        void OnGesture(TrackedHand hand, string label)
        {
            if (!IsActive || CurrentStep >= steps.Length) return;
            var step = steps[CurrentStep];
            if (Array.FindIndex(step.gestures, g => StoryGestureTrigger.Matches(g, label)) < 0) return;
            if (Time.time - _lastCount < Cooldown) return;
            _lastCount = Time.time;

            if (++_count < Mathf.Max(1, step.repetitions)) return;
            _count = 0;
            CurrentStep++;
            step.onDone?.Invoke();
            if (CurrentStep >= steps.Length)
            {
                onAllDone?.Invoke();
                _resumeAt = Time.time + walkOnDelay;
            }
        }

        void OnGUI()
        {
            if (!showHint || !IsActive || CurrentStep >= steps.Length) return;
            string text = steps[CurrentStep].hint;
            if (string.IsNullOrEmpty(text)) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            float w = Mathf.Min(720f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height - 110f, w, 60f), text, style);
        }

        [ContextMenu("Test: do the current gesture")]
        void TestGesture()
        {
            if (!Application.isPlaying) { Debug.Log("Enter Play mode first."); return; }
            if (!IsActive) { Debug.Log("[Village] The walk is not at this stop yet."); return; }
            if (CurrentStep < steps.Length && steps[CurrentStep].gestures.Length > 0 && HandTrackingReceiver.Instance != null)
                HandTrackingReceiver.Instance.SimulateStoryGesture(StoryGestureTrigger.LabelFor(steps[CurrentStep].gestures[0]));
        }
    }
}
