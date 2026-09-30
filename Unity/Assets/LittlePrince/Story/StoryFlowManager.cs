using System;
using System.Collections.Generic;
using LittlePrince.HandTracking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

namespace LittlePrince.Story
{
    public enum StoryStage { Intro, Garden, Village, Space, Final }

    public enum StepType
    {
        /// <summary>Wait for one of the accepted story gestures.</summary>
        Gesture,
        /// <summary>Wait until the player walks into an area (e.g. the gate, the cliff).</summary>
        ReachArea,
        /// <summary>Play for a fixed time (animation, quote, black space).</summary>
        Timed,
        /// <summary>Play a VideoPlayer to the end.</summary>
        Video,
        /// <summary>Wait until something calls StoryFlowManager.CompleteCurrentStep().</summary>
        Manual,
    }

    [Serializable]
    public class StoryStep
    {
        public string title = "New step";
        public StoryStage stage = StoryStage.Intro;
        public StepType type = StepType.Gesture;

        [Header("Gesture step")]
        [Tooltip("Any of these completes the step, e.g. Wave Calm OR Pat Shoulder for the switchman.")]
        public StoryGesture[] acceptedGestures = new StoryGesture[0];
        [Tooltip("How many times the gesture must happen (e.g. 3 roses to pick).")]
        public int repetitions = 1;
        [Tooltip("Optional: the cursor must be pointing at this object while doing the gesture.")]
        public HandInteractable mustPointAt;

        [Header("Reach area step")]
        [Tooltip("Collider marking the destination (it can be a trigger).")]
        public Collider area;

        [Header("Timed / video step")]
        public float duration = 5f;
        public VideoPlayer video;

        [Header("Pacing (no pressure: the story always moves on)")]
        [Tooltip("Show a gentle hint after this many seconds. 0 = never.")]
        public float hintAfterSeconds = 12f;
        [TextArea] public string hintText;
        [Tooltip("Shown for a few seconds when a different story gesture is made (the flowchart's 'Try again'). Empty = nothing.")]
        public string incorrectText = "";
        [Tooltip("Move on by itself after this many seconds, so nobody gets stuck. 0 = never.")]
        public float autoAdvanceAfterSeconds = 45f;
        [Tooltip("Can the visitor walk / look around during this step?")]
        public bool allowWalking = true;
        [Tooltip("Optional: move the player here when the step starts.")]
        public Transform teleportPlayerTo;

        [Header("Events")]
        public UnityEvent onEnter = new UnityEvent();
        [Tooltip("A repetition was done (e.g. one rose lit up) but more are needed.")]
        public UnityEvent onProgress = new UnityEvent();
        public UnityEvent onHint = new UnityEvent();
        [Tooltip("A different story gesture was made: the flowchart's gentle 'try again'.")]
        public UnityEvent onIncorrect = new UnityEvent();
        public UnityEvent onComplete = new UnityEvent();
    }

    [Serializable]
    public class StageRoot
    {
        public StoryStage stage;
        [Tooltip("Everything that belongs to this stage. Only the current stage's root is active.")]
        public GameObject root;
    }

    /// <summary>
    /// Runs the experience flowchart: a list of steps, each waiting for a gesture,
    /// a place, a timer or a video. After the last step it goes back to the start.
    ///
    /// Build it from the flowchart with: Little Prince > Add Story Flow (from flowchart).
    /// Dev keys: N = skip step, F2 = toggle the step overlay.
    /// </summary>
    public class StoryFlowManager : MonoBehaviour
    {
        public List<StoryStep> steps = new List<StoryStep>();
        public List<StageRoot> stageRoots = new List<StageRoot>();

        public HandCameraController player;
        public bool startOnPlay = true;
        public bool loop = true;

        [Header("Wrong gestures")]
        [Tooltip("Fire onIncorrect when a different story gesture is made during a gesture step.")]
        public bool reactToWrongGestures = true;
        public float incorrectCooldown = 3f;
        [Tooltip("Minimum seconds between two counted repetitions, so one long gesture doesn't count 3 times.")]
        public float repetitionCooldown = 1f;

        [Header("Dev")]
        public bool showOverlay = true;

        public event Action<StoryStep, int> StepStarted;
        public event Action<StoryStep, int> StepCompleted;
        public event Action<StoryStage> StageChanged;
        /// <summary>Hint text to show (empty string = hide). Hook your UI up to this.</summary>
        public event Action<string> HintChanged;
        public event Action Looped;

        public int CurrentIndex { get; private set; } = -1;
        public StoryStep Current => CurrentIndex >= 0 && CurrentIndex < steps.Count ? steps[CurrentIndex] : null;
        public StoryStage CurrentStage { get; private set; }
        public float StepTime { get; private set; }
        public int RepetitionsDone { get; private set; }
        public string CurrentHint { get; private set; } = "";

        HandTrackingReceiver _receiver;
        bool _hintShown, _videoDone;
        float _lastIncorrect = -999f, _lastRepetition = -999f, _restoreHintAt;
        GUIStyle _style;

        void Start()
        {
            if (player == null) player = FindObjectOfType<HandCameraController>();
            if (startOnPlay) Restart();
        }

        void OnDisable() => Subscribe(null);

        void Subscribe(HandTrackingReceiver r)
        {
            if (r == _receiver) return;
            if (_receiver != null) _receiver.StoryGestureStarted -= OnStoryGesture;
            _receiver = r;
            if (r != null) r.StoryGestureStarted += OnStoryGesture;
        }

        /// <summary>Go back to the first step ("Go back to the start" in the flowchart).</summary>
        public void Restart()
        {
            CurrentIndex = -1;
            GoTo(0);
        }

        public void GoTo(int index)
        {
            if (steps.Count == 0) return;
            if (index >= steps.Count)
            {
                if (!loop) { CurrentIndex = steps.Count; SetHint(""); return; }
                Looped?.Invoke();
                index = 0;
            }

            CurrentIndex = index;
            StepTime = 0f;
            RepetitionsDone = 0;
            _hintShown = false;
            _videoDone = false;
            _restoreHintAt = 0f;
            SetHint("");

            var step = steps[index];
            SetStage(step.stage);
            if (player != null) player.InputEnabled = step.allowWalking;
            if (step.teleportPlayerTo != null && player != null) Teleport(player.transform, step.teleportPlayerTo);
            if (step.type == StepType.Video && step.video != null)
            {
                step.video.loopPointReached -= OnVideoFinished;
                step.video.loopPointReached += OnVideoFinished;
                step.video.Play();
            }

            Debug.Log($"[Story] {index + 1}/{steps.Count} {step.stage}: {step.title}");
            step.onEnter?.Invoke();
            StepStarted?.Invoke(step, index);
        }

        /// <summary>Finish the current step (for Manual steps, UI buttons, or your own scripts).</summary>
        public void CompleteCurrentStep()
        {
            var step = Current;
            if (step == null) return;
            if (step.type == StepType.Video && step.video != null)
            {
                step.video.loopPointReached -= OnVideoFinished;
                step.video.Stop();
            }
            step.onComplete?.Invoke();
            StepCompleted?.Invoke(step, CurrentIndex);
            GoTo(CurrentIndex + 1);
        }

        void Update()
        {
            Subscribe(HandTrackingReceiver.Instance);
            if (DevInput.SkipPressed) CompleteCurrentStep();
            if (DevInput.ToggleStoryOverlayPressed) showOverlay = !showOverlay;

            var step = Current;
            if (step == null) return;
            StepTime += Time.deltaTime;

            switch (step.type)
            {
                case StepType.ReachArea:
                    if (step.area != null && player != null && step.area.bounds.Contains(player.transform.position))
                    { CompleteCurrentStep(); return; }
                    break;
                case StepType.Timed:
                    if (StepTime >= step.duration) { CompleteCurrentStep(); return; }
                    break;
                case StepType.Video:
                    if (_videoDone || (step.video == null && StepTime >= step.duration)) { CompleteCurrentStep(); return; }
                    break;
            }

            if (_restoreHintAt > 0f && Time.time >= _restoreHintAt)
            {
                _restoreHintAt = 0f;
                SetHint(_hintShown ? step.hintText : "");
            }
            if (!_hintShown && step.hintAfterSeconds > 0f && StepTime >= step.hintAfterSeconds)
            {
                _hintShown = true;
                SetHint(step.hintText);
                step.onHint?.Invoke();
            }
            if (step.autoAdvanceAfterSeconds > 0f && StepTime >= step.autoAdvanceAfterSeconds)
            {
                Debug.Log($"[Story] '{step.title}' moved on by itself after {step.autoAdvanceAfterSeconds:0}s");
                CompleteCurrentStep();
            }
        }

        void OnStoryGesture(TrackedHand hand, string label)
        {
            var step = Current;
            if (step == null || step.type != StepType.Gesture) return;

            bool accepted = false;
            foreach (var g in step.acceptedGestures)
                if (StoryGestureTrigger.Matches(g, label)) { accepted = true; break; }

            if (!accepted)
            {
                if (reactToWrongGestures && Time.time - _lastIncorrect > incorrectCooldown)
                {
                    _lastIncorrect = Time.time;
                    if (!string.IsNullOrEmpty(step.incorrectText))
                    {
                        SetHint(step.incorrectText);
                        _restoreHintAt = Time.time + 3f;
                    }
                    step.onIncorrect?.Invoke();
                }
                return;
            }
            if (step.mustPointAt != null && !step.mustPointAt.IsHovered) return;
            if (Time.time - _lastRepetition < repetitionCooldown) return;

            _lastRepetition = Time.time;
            RepetitionsDone++;
            if (RepetitionsDone >= Mathf.Max(1, step.repetitions)) CompleteCurrentStep();
            else step.onProgress?.Invoke();
        }

        void OnVideoFinished(VideoPlayer vp) => _videoDone = true;

        void SetStage(StoryStage stage)
        {
            bool changed = stage != CurrentStage || CurrentIndex == 0;
            CurrentStage = stage;
            foreach (var s in stageRoots)
                if (s.root != null) s.root.SetActive(s.stage == stage);
            if (changed) StageChanged?.Invoke(stage);
        }

        void SetHint(string text)
        {
            CurrentHint = text ?? "";
            HintChanged?.Invoke(CurrentHint);
        }

        static void Teleport(Transform who, Transform to)
        {
            var cc = who.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            who.SetPositionAndRotation(to.position, Quaternion.Euler(0f, to.eulerAngles.y, 0f));
            if (cc != null) cc.enabled = true;
        }

        [ContextMenu("Skip current step")]
        void SkipFromMenu() => CompleteCurrentStep();

        void OnGUI()
        {
            var step = Current;
            if (step == null) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, alignment = TextAnchor.MiddleCenter, wordWrap = true };

            if (!string.IsNullOrEmpty(CurrentHint))
            {
                GUI.color = new Color(0f, 0f, 0f, 0.35f);
                var hr = new Rect(Screen.width * 0.2f, Screen.height - 110, Screen.width * 0.6f, 44);
                GUI.DrawTexture(hr, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(hr, $"<i>{CurrentHint}</i>", _style);
            }

            if (!showOverlay) return;
            string waitingFor = step.type == StepType.Gesture
                ? string.Join(" / ", Array.ConvertAll(step.acceptedGestures, g => g.ToString())) +
                  (step.repetitions > 1 ? $"  ({RepetitionsDone}/{step.repetitions})" : "")
                : step.type.ToString();
            var r = new Rect(Screen.width - 430, 10, 420, 64);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, $"<b>{CurrentIndex + 1}/{steps.Count}  {step.stage}: {step.title}</b>\n" +
                         $"<size=13>waiting for: {waitingFor}   {StepTime:0}s   (N skip, F2 hide)</size>", _style);
        }
    }
}
