"""
unity_bridge.py

Turns one hand's landmarks (+ the story gestures from gestures.py) into the
small JSON packet Unity's HandTrackingReceiver listens for, and sends it over UDP.

Packet format (must match HandData.cs in Unity):
{
  "t": 1712345678.12, "frame": 123,
  "hands": [
    {
      "side": "Right",                  # MediaPipe handedness label
      "score": 1.0,
      "gesture": "open",                # basic pose: open | fist | pinch | point | none
      "pinch": 0.85,                    # 0..1 how closed thumb+index are
      "events": ["WAVE_CALM"],          # story gestures from gestures.py active right now
      "lm": [x0, y0, z0, ... x20, y20, z20]   # 21 landmarks, normalised, origin top-left
    }
  ]
}
An empty "hands" list means no hand is visible.
"""
import json
import socket
import time

from gesture_utils import FingerState, MotionTracker
from gestures import ALL_GESTURES

PINCH_THRESHOLD = 0.35  # same value gestures.py uses


def basic_gesture(fs: FingerState):
    """Simple always-on pose used by Unity for walking / pointing / grabbing."""
    pinch_dist = fs.pinch_distance()
    # 0.25 (touching) -> 1.0, 0.8 (wide apart) -> 0.0
    pinch = max(0.0, min(1.0, (0.8 - pinch_dist) / (0.8 - 0.25)))

    if fs.is_fist:  # checked before pinch: in a fist the thumb also rests on the index
        gesture = "fist"
    elif pinch_dist < PINCH_THRESHOLD:
        gesture = "pinch"
    elif fs.is_open_palm:
        gesture = "open"
    elif fs.is_pointing:
        gesture = "point"
    else:
        gesture = "none"
    return gesture, pinch


def build_hand_packet(landmarks, side, score=1.0, events=(), finger_state=None):
    """landmarks: 21 (x, y, z) tuples in normalised image coordinates."""
    fs = finger_state or FingerState(landmarks)
    gesture, pinch = basic_gesture(fs)
    flat = []
    for x, y, z in landmarks:
        flat.extend((round(x, 4), round(y, 4), round(z, 4)))
    return {
        "side": side,
        "score": round(float(score), 3),
        "gesture": gesture,
        "pinch": round(pinch, 3),
        "events": list(events),
        "lm": flat,
    }


class UnityHandSender:
    def __init__(self, host="127.0.0.1", port=5052):
        self.addr = (host, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.frame = 0

    def send(self, hands):
        self.frame += 1
        msg = {"t": round(time.time(), 3), "frame": self.frame, "hands": hands}
        self.sock.sendto(json.dumps(msg, separators=(",", ":")).encode("utf-8"), self.addr)


# One-frame gestures (e.g. TOUCH) are held "active" at least this long, so Unity
# never misses them even if it drops a packet or runs slower than the webcam.
MIN_EVENT_SECONDS = 0.3
# Forget a hand's detector state if it has been out of view this long.
FORGET_AFTER_SECONDS = 1.0


class HandState:
    """Motion history + one instance of every detector, for one hand (Left or Right)."""

    def __init__(self):
        self.motion = MotionTracker(history_len=20)
        self.detectors = [cls() for _name, cls in ALL_GESTURES.values()]
        self.active_until = {}  # label -> time it stays active until
        self.printed = set()    # labels already printed to the console
        self.last_seen = time.time()

    def update(self, landmarks):
        now = time.time()
        self.last_seen = now
        fs = FingerState(landmarks)
        self.motion.update(landmarks, now)
        for detector in self.detectors:
            result = detector.update(fs, self.motion)
            if result["active"]:
                self.active_until[result["label"]] = now + MIN_EVENT_SECONDS
        self.active_until = {k: t for k, t in self.active_until.items() if t >= now}
        return fs, sorted(self.active_until)
