using LittlePrince.HandTracking;
using LittlePrince.Story;
using UnityEngine;
using UnityEngine.Events;

namespace LittlePrince.GuidedWalk
{
    /// <summary>
    /// Put this on a waypoint of a <see cref="GuidedPath"/>: the guided walk pauses
    /// there (e.g. in front of the roses) and continues when the visitor has done
    /// something, or after a while anyway, so nobody gets stuck.
    /// </summary>
    public class PathStop : MonoBehaviour
    {
        public enum WaitFor
        {
            /// <summary>Just pause for a few seconds (a nice view).</summary>
            Seconds,
            /// <summary>Until the visitor pinches the listed objects.</summary>
            Interaction,
            /// <summary>Until the StoryFlowManager reaches a given step number.</summary>
            StoryStep,
            /// <summary>Until something calls GuidedPathWalker.Resume().</summary>
            Resume,
        }

        public WaitFor waitFor = WaitFor.Interaction;
        [Tooltip("Seconds mode: how long to pause.")]
        public float seconds = 4f;

        [Header("Interaction mode")]
        [Tooltip("Objects to touch here (empty = any HandInteractable).")]
        public HandInteractable[] interactables = new HandInteractable[0];
        public int interactionsNeeded = 1;

        [Header("Story step mode")]
        [Tooltip("Walk on when the story reaches this step number (the number in the story overlay, starting at 1). " +
                 "0 = when the step that was active on arrival is done.")]
        public int continueAtStep = 0;

        [Header("Always")]
        [Tooltip("Walk on after this many seconds no matter what. 0 = wait forever.")]
        public float maxWaitSeconds = 40f;
        [Tooltip("Turn the view toward this while waiting (e.g. the flower bed). Optional.")]
        public Transform lookAt;

        public UnityEvent onArrive = new UnityEvent();
        public UnityEvent onLeave = new UnityEvent();

        public float WaitedSeconds { get; private set; }
        public int InteractionsDone { get; private set; }

        StoryFlowManager _story;
        int _storyIndexAtArrival, _storyLoopsAtArrival;
        bool _waiting;

        internal void Arrive()
        {
            _waiting = true;
            WaitedSeconds = 0f;
            InteractionsDone = 0;
            HandInteractable.AnySelected += OnSelected;
            if (waitFor == WaitFor.StoryStep)
            {
                _story = FindObjectOfType<StoryFlowManager>();
                if (_story != null) { _storyIndexAtArrival = _story.CurrentIndex; _storyLoopsAtArrival = _story.LoopCount; }
            }
            onArrive?.Invoke();
        }

        internal void Leave()
        {
            _waiting = false;
            HandInteractable.AnySelected -= OnSelected;
            onLeave?.Invoke();
        }

        void OnDisable()
        {
            if (_waiting) HandInteractable.AnySelected -= OnSelected;
            _waiting = false;
        }

        /// <summary>Called every frame by the walker while waiting here.</summary>
        internal bool Tick(float dt)
        {
            WaitedSeconds += dt;
            if (maxWaitSeconds > 0f && WaitedSeconds >= maxWaitSeconds) return true;
            switch (waitFor)
            {
                case WaitFor.Seconds:
                    return WaitedSeconds >= seconds;
                case WaitFor.Interaction:
                    return InteractionsDone >= Mathf.Max(1, interactionsNeeded);
                case WaitFor.StoryStep:
                    if (_story == null) return WaitedSeconds >= seconds;
                    if (_story.LoopCount != _storyLoopsAtArrival) return true; // story restarted
                    return continueAtStep > 0
                        ? _story.CurrentIndex + 1 >= continueAtStep
                        : _story.CurrentIndex != _storyIndexAtArrival;
                default:
                    return false; // Resume: only GuidedPathWalker.Resume() continues
            }
        }

        void OnSelected(HandInteractable h)
        {
            if (!_waiting) return;
            if (interactables.Length == 0 || System.Array.IndexOf(interactables, h) >= 0) InteractionsDone++;
        }
    }
}
