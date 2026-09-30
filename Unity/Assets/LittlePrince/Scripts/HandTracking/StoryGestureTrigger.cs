using UnityEngine;
using UnityEngine.Events;

namespace LittlePrince.HandTracking
{
    /// <summary>The story gestures detected by HandTracking-Python/gestures.py.</summary>
    public enum StoryGesture
    {
        ClaspOpenUpward,   // Stage 1: stars light up
        PointAlternateLR,  // Stage 1: follow the footprints
        PickForward,       // Stage 1: roses / Stage 2: paper
        WaveAny,           // Stage 2: children / switchman (rapid or calm)
        WaveRapid,
        WaveCalm,
        PatShoulder,       // Stage 2: switchman (alternative)
        ClaspSwingRun,     // Stage 2: village / Stage 3: rose
        Swipe,             // Stage 2: bush / fox
        PickAndLook,       // Stage 2: paper from the fox
        FoldPaper,         // Stage 2: origami
        PointUp,           // Stage 3: stars
        Touch,             // Stage 3: stars / rose
    }

    /// <summary>
    /// Runs its UnityEvent when a story gesture from gestures.py is detected.
    /// Add one per moment in the story, e.g. on the fox: Gesture = Swipe, and
    /// hook the fox's animation into On Detected. Disable the component (or its
    /// GameObject) when that moment is not active yet.
    ///
    /// Right-click the component header > "Test: fire now" to try it without a camera.
    /// </summary>
    public class StoryGestureTrigger : MonoBehaviour
    {
        public StoryGesture gesture = StoryGesture.Swipe;
        public enum HandFilter { Any, Left, Right }
        public HandFilter hand = HandFilter.Any;

        [Tooltip("Minimum seconds between two triggers.")]
        public float cooldown = 1.5f;
        [Tooltip("0 = unlimited. E.g. 1 for a moment that should only happen once.")]
        public int maxTriggers = 0;

        [Header("Only while the cursor points at this object")]
        public bool requireHover = false;
        [Tooltip("Defaults to the HandInteractable on this GameObject.")]
        public HandInteractable hoverTarget;

        public UnityEvent onDetected = new UnityEvent();
        [Tooltip("Runs when the gesture stops (useful for continuous ones like waving or running).")]
        public UnityEvent onEnded = new UnityEvent();

        public int TimesTriggered { get; private set; }
        public bool IsActive { get; private set; }

        HandTrackingReceiver _receiver;
        float _lastTrigger = -999f;

        /// <summary>The label(s) gestures.py sends for this gesture.</summary>
        public static bool Matches(StoryGesture g, string label)
        {
            switch (g)
            {
                case StoryGesture.ClaspOpenUpward: return label == "CLASP_OPEN_UPWARD";
                case StoryGesture.PointAlternateLR: return label == "POINT_ALTERNATE_LR";
                case StoryGesture.PickForward: return label == "PICK_FORWARD";
                case StoryGesture.WaveAny: return label.StartsWith("WAVE_");
                case StoryGesture.WaveRapid: return label == "WAVE_RAPID";
                case StoryGesture.WaveCalm: return label == "WAVE_CALM";
                case StoryGesture.PatShoulder: return label == "PAT_SHOULDER";
                case StoryGesture.ClaspSwingRun: return label == "CLASP_SWING_RUN";
                case StoryGesture.Swipe: return label == "SWIPE";
                case StoryGesture.PickAndLook: return label == "PICK_AND_LOOK";
                case StoryGesture.FoldPaper: return label == "FOLD_PAPER";
                case StoryGesture.PointUp: return label == "POINT_UP";
                case StoryGesture.Touch: return label == "TOUCH";
                default: return false;
            }
        }

        public static string LabelFor(StoryGesture g)
        {
            switch (g)
            {
                case StoryGesture.ClaspOpenUpward: return "CLASP_OPEN_UPWARD";
                case StoryGesture.PointAlternateLR: return "POINT_ALTERNATE_LR";
                case StoryGesture.PickForward: return "PICK_FORWARD";
                case StoryGesture.WaveAny:
                case StoryGesture.WaveCalm: return "WAVE_CALM";
                case StoryGesture.WaveRapid: return "WAVE_RAPID";
                case StoryGesture.PatShoulder: return "PAT_SHOULDER";
                case StoryGesture.ClaspSwingRun: return "CLASP_SWING_RUN";
                case StoryGesture.Swipe: return "SWIPE";
                case StoryGesture.PickAndLook: return "PICK_AND_LOOK";
                case StoryGesture.FoldPaper: return "FOLD_PAPER";
                case StoryGesture.PointUp: return "POINT_UP";
                case StoryGesture.Touch: return "TOUCH";
                default: return "";
            }
        }

        void Awake()
        {
            if (hoverTarget == null) hoverTarget = GetComponent<HandInteractable>();
        }

        void OnEnable() => Subscribe();
        void Start() => Subscribe(); // in case the receiver woke up after us

        void OnDisable()
        {
            if (_receiver != null)
            {
                _receiver.StoryGestureStarted -= OnStarted;
                _receiver.StoryGestureEnded -= OnEnded;
            }
            _receiver = null;
            IsActive = false;
        }

        void Subscribe()
        {
            var r = HandTrackingReceiver.Instance;
            if (r == null || r == _receiver) return;
            _receiver = r;
            r.StoryGestureStarted += OnStarted;
            r.StoryGestureEnded += OnEnded;
        }

        bool Accepts(TrackedHand h, string label)
        {
            if (!Matches(gesture, label)) return false;
            if (hand == HandFilter.Left && h.Side != "Left") return false;
            if (hand == HandFilter.Right && h.Side != "Right") return false;
            return true;
        }

        void OnStarted(TrackedHand h, string label)
        {
            if (!Accepts(h, label)) return;
            if (requireHover && (hoverTarget == null || !hoverTarget.IsHovered)) return;
            if (maxTriggers > 0 && TimesTriggered >= maxTriggers) return;
            if (Time.time - _lastTrigger < cooldown) return;

            _lastTrigger = Time.time;
            TimesTriggered++;
            IsActive = true;
            onDetected?.Invoke();
        }

        void OnEnded(TrackedHand h, string label)
        {
            if (!IsActive || !Accepts(h, label)) return;
            IsActive = false;
            onEnded?.Invoke();
        }

        [ContextMenu("Test: fire now")]
        void TestFire()
        {
            if (!Application.isPlaying) { Debug.Log("Enter Play mode first."); return; }
            if (HandTrackingReceiver.Instance != null)
                HandTrackingReceiver.Instance.SimulateStoryGesture(LabelFor(gesture));
        }
    }
}
