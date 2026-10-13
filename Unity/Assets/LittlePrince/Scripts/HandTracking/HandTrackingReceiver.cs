using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace LittlePrince.HandTracking
{
    /// <summary>
    /// Listens for hand packets from HandTracking-Python/unity_sender.py (UDP, JSON)
    /// and exposes smoothed hands + events to the rest of the experience.
    ///
    /// Put ONE of these in the scene. Other scripts use HandTrackingReceiver.Instance.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HandTrackingReceiver : MonoBehaviour
    {
        public static HandTrackingReceiver Instance { get; private set; }

        [Header("Network")]
        [Tooltip("Must match --port in unity_sender.py")]
        public int port = 5052;
        [Tooltip("Hand is considered lost if no packet mentions it for this long (seconds).")]
        public float lostTimeout = 0.4f;

        [Header("Data")]
        [Tooltip("unity_sender.py already mirrors the webcam. Only tick this if you run it with --no-mirror.")]
        public bool mirrorX = false;
        [Range(0f, 0.95f), Tooltip("0 = raw/jittery, 0.9 = very smooth but laggy")]
        public float smoothing = 0.6f;
        [Tooltip("Which hand drives the camera/cursor when both are visible.")]
        public string preferredSide = "Right";

        [Header("Testing without a camera")]
        [Tooltip("When no packets arrive, the mouse acts as the hand: move = point/aim, hold left = pinch, hold right = open palm (walk), both = fist.")]
        public bool simulateWithMouse = true;

        [Header("Swipe detection")]
        public float swipeSpeed = 1.6f;   // viewport widths per second
        public float swipeCooldown = 0.6f;

        // ---- Events -------------------------------------------------------------
        public event Action<TrackedHand> HandFound;
        public event Action<TrackedHand> HandLost;
        public event Action<TrackedHand, HandGesture> GestureChanged;   // new gesture
        public event Action<TrackedHand> PinchStarted;
        public event Action<TrackedHand> PinchEnded;
        public event Action<TrackedHand, Vector2> Swiped;                // direction: left/right/up/down
        /// <summary>A story gesture from gestures.py started, e.g. "WAVE_CALM", "SWIPE", "TOUCH".</summary>
        public event Action<TrackedHand, string> StoryGestureStarted;
        public event Action<TrackedHand, string> StoryGestureEnded;

        // ---- State --------------------------------------------------------------
        public IReadOnlyList<TrackedHand> Hands => _hands;
        /// <summary>The hand that should drive interaction (null if none).</summary>
        public TrackedHand PrimaryHand { get; private set; }
        public bool HasHand => PrimaryHand != null;
        /// <summary>True while real packets are arriving from Python.</summary>
        public bool IsConnected => Time.unscaledTime - _lastPacketTime < 1f;
        public bool IsSimulating { get; private set; }
        public float PacketsPerSecond { get; private set; }
        public string LastError { get; private set; }

        readonly List<TrackedHand> _hands = new List<TrackedHand>();
        readonly Dictionary<string, TrackedHand> _bySide = new Dictionary<string, TrackedHand>();
        readonly Dictionary<TrackedHand, bool> _wasPinching = new Dictionary<TrackedHand, bool>();
        readonly HashSet<string> _seenThisPacket = new HashSet<string>();
        readonly List<string> _started = new List<string>(), _ended = new List<string>();
        float _lastSwipeTime;
        TrackedHand _simHand;

        // Background thread -> main thread hand-off
        Thread _thread;
        UdpClient _client;
        volatile bool _running;
        readonly object _lock = new object();
        string _latestJson;
        int _packetCounter;
        float _lastPacketTime = -10f, _ppsTimer;
        bool _warnedNoData;
        int _ppsCount;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[HandTracking] More than one HandTrackingReceiver in the scene; disabling the extra one.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnEnable() => StartListening();
        void OnDisable() => StopListening();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void StartListening()
        {
            if (_running) return;
            try
            {
                _client = new UdpClient(port);
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "HandTrackingUDP" };
                _thread.Start();
                Debug.Log($"[HandTracking] Listening for MediaPipe hands on UDP port {port}");
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[HandTracking] Could not open UDP port {port}: {e.Message}. Is another app (or another Unity instance) using it?");
            }
        }

        void StopListening()
        {
            _running = false;
            try { _client?.Close(); } catch { /* ignored */ }
            _client = null;
            if (_thread != null && _thread.IsAlive) _thread.Join(200);
            _thread = null;
        }

        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _client.Receive(ref any);
                    string json = Encoding.UTF8.GetString(data);
                    lock (_lock)
                    {
                        _latestJson = json; // only the newest frame matters
                        _packetCounter++;
                    }
                }
                catch (SocketException) { /* socket closed on shutdown */ }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { LastError = e.Message; }
            }
        }

        void Update()
        {
            string json;
            int count;
            lock (_lock)
            {
                json = _latestJson;
                _latestJson = null;
                count = _packetCounter;
                _packetCounter = 0;
            }

            _ppsCount += count;
            _ppsTimer += Time.unscaledDeltaTime;
            if (_ppsTimer >= 1f) { PacketsPerSecond = _ppsCount / _ppsTimer; _ppsCount = 0; _ppsTimer = 0f; }

            if (json != null)
            {
                _lastPacketTime = Time.unscaledTime;
                if (_simHand != null) RemoveHand(_simHand); // real data takes over from the mouse
                ProcessPacket(json);
            }

            if (!_warnedNoData && _lastPacketTime < 0f && Time.timeSinceLevelLoad > 4f)
            {
                _warnedNoData = true;
                bool hasLauncher = GetComponent<PythonLauncher>() != null || FindObjectOfType<PythonLauncher>() != null;
                Debug.LogWarning(hasLauncher
                    ? "[HandTracking] No hand data yet. Check the [Python] messages above for what went wrong."
                    : "[HandTracking] No hand data yet, and nothing starts Python in this scene. " +
                      "Use the menu  Little Prince > Add Python Launcher to Scene  (starts it on Play), " +
                      "or run  python unity_sender.py  in the HandTracking-Python folder.");
            }

            IsSimulating = simulateWithMouse && !IsConnected;
            if (IsSimulating) Simulate();

            // Drop hands we haven't heard about for a while.
            for (int i = _hands.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - _hands[i].LastSeenTime > lostTimeout)
                    RemoveHand(_hands[i]);

            ChoosePrimary();
            DetectPinchEdgesAndSwipes();
        }

        void ProcessPacket(string json)
        {
            HandFramePacket frame;
            try { frame = JsonUtility.FromJson<HandFramePacket>(json); }
            catch (Exception e) { LastError = "Bad packet: " + e.Message; return; }
            if (frame?.hands == null) return;

            _seenThisPacket.Clear();
            foreach (var p in frame.hands)
            {
                if (p == null || string.IsNullOrEmpty(p.side)) continue;
                // MediaPipe sometimes labels both hands the same; keep them apart.
                if (!_seenThisPacket.Add(p.side))
                {
                    p.side = p.side == "Right" ? "Left" : "Right";
                    if (!_seenThisPacket.Add(p.side)) continue;
                }
                bool isNew = !_bySide.TryGetValue(p.side, out var hand);
                if (isNew)
                {
                    hand = new TrackedHand();
                    _bySide[p.side] = hand;
                    _hands.Add(hand);
                }
                float dt = isNew ? 0f : Mathf.Max(0.001f, Time.unscaledTime - hand.LastSeenTime);
                var before = hand.Gesture;
                hand.Apply(p, mirrorX, smoothing, dt);
                if (isNew) HandFound?.Invoke(hand);
                if (hand.Gesture != before) GestureChanged?.Invoke(hand, hand.Gesture);

                hand.SetStoryGestures(p.events, _started, _ended);
                foreach (var label in _ended) StoryGestureEnded?.Invoke(hand, label);
                foreach (var label in _started) StoryGestureStarted?.Invoke(hand, label);
            }
        }

        /// <summary>
        /// Fires a story gesture on the primary hand as if gestures.py had detected it.
        /// For testing stage logic without a camera (see StoryGestureTrigger's context menu).
        /// </summary>
        public void SimulateStoryGesture(string label)
        {
            var hand = PrimaryHand;
            if (hand == null) { Debug.LogWarning("[HandTracking] No hand to simulate a story gesture on."); return; }
            StoryGestureStarted?.Invoke(hand, label);
            StoryGestureEnded?.Invoke(hand, label);
        }

        void Simulate()
        {
            bool isNew = _simHand == null;
            if (isNew)
            {
                _simHand = new TrackedHand();
                _hands.Add(_simHand);
                _bySide["Right"] = _simHand;
            }
            bool l = DevInput.LeftMouse, r = DevInput.RightMouse;
            var gesture = l && r ? HandGesture.Fist
                        : l ? HandGesture.Pinch
                        : r ? HandGesture.Open
                        : HandGesture.Point;
            var before = _simHand.Gesture;
            _simHand.ApplySimulated(DevInput.MouseViewport, gesture, Time.unscaledDeltaTime);
            if (isNew) HandFound?.Invoke(_simHand);
            if (_simHand.Gesture != before) GestureChanged?.Invoke(_simHand, _simHand.Gesture);
        }

        void RemoveHand(TrackedHand hand)
        {
            _hands.Remove(hand);
            if (_bySide.TryGetValue(hand.Side, out var h) && h == hand) _bySide.Remove(hand.Side);
            if (_wasPinching.TryGetValue(hand, out bool pinching) && pinching) PinchEnded?.Invoke(hand);
            hand.SetStoryGestures(null, _started, _ended);
            foreach (var label in _ended) StoryGestureEnded?.Invoke(hand, label);
            _wasPinching.Remove(hand);
            if (hand == _simHand) _simHand = null;
            HandLost?.Invoke(hand);
        }

        void ChoosePrimary()
        {
            if (_hands.Count == 0) { PrimaryHand = null; return; }
            if (PrimaryHand != null && _hands.Contains(PrimaryHand) && PrimaryHand.Side == preferredSide) return;
            PrimaryHand = _bySide.TryGetValue(preferredSide, out var preferred) ? preferred
                        : (PrimaryHand != null && _hands.Contains(PrimaryHand) ? PrimaryHand : _hands[0]);
        }

        void DetectPinchEdgesAndSwipes()
        {
            foreach (var hand in _hands)
            {
                _wasPinching.TryGetValue(hand, out bool was);
                if (hand.IsPinching && !was) PinchStarted?.Invoke(hand);
                else if (!hand.IsPinching && was) PinchEnded?.Invoke(hand);
                _wasPinching[hand] = hand.IsPinching;
            }

            var primary = PrimaryHand;
            if (primary == null || Time.unscaledTime - _lastSwipeTime < swipeCooldown) return;
            Vector2 v = primary.Velocity;
            if (v.magnitude < swipeSpeed) return;
            Vector2 dir = Mathf.Abs(v.x) > Mathf.Abs(v.y)
                ? new Vector2(Mathf.Sign(v.x), 0f)
                : new Vector2(0f, Mathf.Sign(v.y));
            _lastSwipeTime = Time.unscaledTime;
            Swiped?.Invoke(primary, dir);
        }
    }
}
