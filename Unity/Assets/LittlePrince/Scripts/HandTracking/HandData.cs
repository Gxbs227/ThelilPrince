using System;
using System.Collections.Generic;
using UnityEngine;

namespace LittlePrince.HandTracking
{
    public enum HandGesture { None, Open, Fist, Pinch, Point }

    // ---- Raw UDP packet (must match unity_bridge.py) -------------------------
    [Serializable]
    public class HandPacket
    {
        public string side;     // "Left" / "Right"
        public float score;
        public string gesture;  // open | fist | pinch | point | none
        public float pinch;     // 0..1
        public string[] events; // story gestures from gestures.py active right now, e.g. "WAVE_CALM"
        public float[] lm;      // 21 * (x, y, z), image coords, origin top-left
    }

    [Serializable]
    public class HandFramePacket
    {
        public double t;
        public int frame;
        public HandPacket[] hands;
    }

    /// <summary>MediaPipe hand landmark indices.</summary>
    public static class HandLandmark
    {
        public const int Wrist = 0, ThumbTip = 4, IndexMcp = 5, IndexTip = 8,
                         MiddleMcp = 9, MiddleTip = 12, RingMcp = 13, RingTip = 16,
                         PinkyMcp = 17, PinkyTip = 20;
    }

    // ---- Smoothed, Unity-friendly hand ------------------------------------------
    /// <summary>
    /// One tracked hand. Positions are in Unity viewport space:
    /// (0,0) = bottom-left of the screen, (1,1) = top-right.
    /// </summary>
    public class TrackedHand
    {
        public const int LandmarkCount = 21;

        public string Side = "Right";
        public float Score;
        public HandGesture Gesture = HandGesture.None;
        public HandGesture PreviousGesture = HandGesture.None;
        /// <summary>0..1, how closed thumb and index are.</summary>
        public float PinchStrength;
        /// <summary>Pinch with hysteresis, so it doesn't flicker at the threshold.</summary>
        public bool IsPinching;
        /// <summary>x/y viewport, z = MediaPipe relative depth (smaller = closer to camera).</summary>
        public readonly Vector3[] Landmarks = new Vector3[LandmarkCount];
        /// <summary>Story gestures (labels from gestures.py) active right now, e.g. "SWIPE", "WAVE_CALM".</summary>
        public readonly HashSet<string> StoryGestures = new HashSet<string>();

        /// <summary>Centre of the palm, smoothed. Best for steering the camera.</summary>
        public Vector2 Palm;
        /// <summary>Index finger tip, smoothed. Best for pointing / cursor.</summary>
        public Vector2 IndexTip;
        /// <summary>Palm speed in viewport units per second.</summary>
        public Vector2 Velocity;
        /// <summary>Apparent hand size (wrist to middle knuckle). Bigger = closer to the camera.</summary>
        public float Size;

        public float LastSeenTime;
        bool _initialised;

        public bool IsRight => Side == "Right";

        public void Apply(HandPacket p, bool mirrorX, float smoothing, float dt)
        {
            Side = p.side;
            Score = p.score;
            PinchStrength = p.pinch;

            var g = ParseGesture(p.gesture);
            if (g != Gesture) { PreviousGesture = Gesture; Gesture = g; }

            if (p.lm != null && p.lm.Length >= LandmarkCount * 3)
            {
                for (int i = 0; i < LandmarkCount; i++)
                {
                    float x = p.lm[i * 3];
                    float y = 1f - p.lm[i * 3 + 1]; // image y is top-down, viewport is bottom-up
                    if (mirrorX) x = 1f - x;
                    Landmarks[i] = new Vector3(x, y, p.lm[i * 3 + 2]);
                }
            }

            Vector2 palm = (Vector2)(Landmarks[HandLandmark.Wrist] + Landmarks[HandLandmark.IndexMcp]
                                     + Landmarks[HandLandmark.MiddleMcp] + Landmarks[HandLandmark.RingMcp]
                                     + Landmarks[HandLandmark.PinkyMcp]) / 5f;
            Vector2 tip = Landmarks[HandLandmark.IndexTip];
            Size = Vector2.Distance(Landmarks[HandLandmark.Wrist], Landmarks[HandLandmark.MiddleMcp]);

            if (!_initialised)
            {
                Palm = palm; IndexTip = tip; Velocity = Vector2.zero;
                _initialised = true;
            }
            else
            {
                // Exponential smoothing that is frame-rate independent.
                float k = 1f - Mathf.Pow(smoothing, dt * 60f);
                Vector2 newPalm = Vector2.Lerp(Palm, palm, k);
                if (dt > 0f) Velocity = Vector2.Lerp(Velocity, (newPalm - Palm) / dt, 0.5f);
                Palm = newPalm;
                IndexTip = Vector2.Lerp(IndexTip, tip, k);
            }

            // Hysteresis: start pinching at 0.8, release below 0.55.
            IsPinching = IsPinching ? PinchStrength > 0.55f : (PinchStrength > 0.8f || Gesture == HandGesture.Pinch);
            LastSeenTime = Time.unscaledTime;
        }

        /// <summary>Replaces the active story gestures and reports which ones started / ended.</summary>
        public void SetStoryGestures(string[] labels, List<string> started, List<string> ended)
        {
            ended.Clear();
            started.Clear();
            foreach (var label in StoryGestures)
                if (labels == null || Array.IndexOf(labels, label) < 0) ended.Add(label);
            foreach (var label in ended) StoryGestures.Remove(label);
            if (labels == null) return;
            foreach (var label in labels)
                if (!string.IsNullOrEmpty(label) && StoryGestures.Add(label)) started.Add(label);
        }

        /// <summary>Used by the mouse simulator in the receiver.</summary>
        public void ApplySimulated(Vector2 viewportPos, HandGesture gesture, float dt)
        {
            if (gesture != Gesture) { PreviousGesture = Gesture; Gesture = gesture; }
            Side = "Right";
            Score = 1f;
            PinchStrength = gesture == HandGesture.Pinch ? 1f : 0f;
            IsPinching = gesture == HandGesture.Pinch;
            if (dt > 0f && _initialised) Velocity = Vector2.Lerp(Velocity, (viewportPos - Palm) / dt, 0.5f);
            Palm = IndexTip = viewportPos;
            Size = 0.15f;
            for (int i = 0; i < LandmarkCount; i++) Landmarks[i] = viewportPos;
            _initialised = true;
            LastSeenTime = Time.unscaledTime;
        }

        public static HandGesture ParseGesture(string s)
        {
            switch (s)
            {
                case "open": return HandGesture.Open;
                case "fist": return HandGesture.Fist;
                case "pinch": return HandGesture.Pinch;
                case "point": return HandGesture.Point;
                default: return HandGesture.None;
            }
        }
    }
}
