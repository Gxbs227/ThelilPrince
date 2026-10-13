using LittlePrince.GuidedWalk;
using LittlePrince.HandTracking;
using UnityEngine;

namespace LittlePrince.Stages.Village
{
    /// <summary>
    /// "Navigate through the village": while the visitor swings a closed hand as if
    /// running (CLASP_SWING_RUN), the guided walk goes faster. Put it on the Player.
    /// </summary>
    public class RunGestureBoost : MonoBehaviour
    {
        public GuidedPathWalker walker;
        [Tooltip("Walk speed is multiplied by this while running.")]
        public float speedMultiplier = 2f;
        [Tooltip("Keep running this long after the gesture stops (it comes in bursts).")]
        public float holdSeconds = 0.8f;

        public bool IsRunning => Time.time < _runUntil;

        HandTrackingReceiver _receiver;
        float _baseSpeed = -1f, _runUntil = -1f;

        void OnDisable()
        {
            if (_receiver != null) _receiver.StoryGestureStarted -= OnGesture;
            _receiver = null;
            if (walker != null && _baseSpeed > 0f) walker.speed = _baseSpeed;
            _baseSpeed = -1f;
        }

        void Update()
        {
            if (walker == null) walker = GetComponent<GuidedPathWalker>();
            if (walker == null) return;
            var r = HandTrackingReceiver.Instance;
            if (r != null && r != _receiver)
            {
                if (_receiver != null) _receiver.StoryGestureStarted -= OnGesture;
                _receiver = r;
                r.StoryGestureStarted += OnGesture;
            }

            var hand = r != null ? r.PrimaryHand : null;
            if (hand != null && hand.StoryGestures.Contains("CLASP_SWING_RUN")) _runUntil = Time.time + holdSeconds;

            if (_baseSpeed < 0f) _baseSpeed = walker.speed;
            walker.speed = IsRunning ? _baseSpeed * speedMultiplier : _baseSpeed;
        }

        void OnGesture(TrackedHand hand, string label)
        {
            if (label == "CLASP_SWING_RUN") _runUntil = Time.time + holdSeconds;
        }
    }
}
