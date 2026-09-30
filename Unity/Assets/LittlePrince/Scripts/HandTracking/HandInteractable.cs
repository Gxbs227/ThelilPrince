using System;
using UnityEngine;
using UnityEngine.Events;

namespace LittlePrince.HandTracking
{
    /// <summary>
    /// Anything the visitor can touch with their hand: flowers, the rose, the fox,
    /// lamps, planets... Needs a Collider. Hook your animations / sounds into the
    /// UnityEvents in the Inspector.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HandInteractable : MonoBehaviour
    {
        [Tooltip("Optional label, handy for debugging and for stage logic.")]
        public string interactionId;

        [Header("Feedback")]
        public bool scaleOnHover = true;
        public float hoverScale = 1.12f;
        public float selectScale = 0.9f;
        public float feedbackSpeed = 8f;

        [Header("Events")]
        public UnityEvent onHoverEnter = new UnityEvent();
        public UnityEvent onHoverExit = new UnityEvent();
        /// <summary>Pinch started (or dwell finished) while pointing at this object.</summary>
        public UnityEvent onSelect = new UnityEvent();
        public UnityEvent onRelease = new UnityEvent();

        /// <summary>Fired for every interactable when it is selected. Useful for stage progress.</summary>
        public static event Action<HandInteractable> AnySelected;

        public bool IsHovered { get; private set; }
        public bool IsSelected { get; private set; }
        public int TimesSelected { get; private set; }

        Vector3 _baseScale;

        void Awake() => _baseScale = transform.localScale;

        void Update()
        {
            if (!scaleOnHover) return;
            float target = IsSelected ? selectScale : IsHovered ? hoverScale : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, _baseScale * target,
                                                Time.deltaTime * feedbackSpeed);
        }

        public void HoverEnter() { IsHovered = true; onHoverEnter?.Invoke(); }
        public void HoverExit() { IsHovered = false; onHoverExit?.Invoke(); }

        public void Select()
        {
            IsSelected = true;
            TimesSelected++;
            onSelect?.Invoke();
            AnySelected?.Invoke(this);
        }

        public void Release()
        {
            if (!IsSelected) return;
            IsSelected = false;
            onRelease?.Invoke();
        }
    }
}
