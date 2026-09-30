"""
Dependency-free helpers shared by hand_tracker.py and mock_sender.py:
gesture classification + the UDP packet sender Unity listens to.

If you already have your own MediaPipe loop, just copy this file and do:

    sender = UnityHandSender("127.0.0.1", 5052)
    hands = [build_hand_packet([(p.x, p.y, p.z) for p in lms], "Right", score), ...]
    sender.send(hands)          # once per frame, send [] when no hand is visible
"""

import json
import math
import socket
import time

# Landmark indices (same for every MediaPipe hand model)
WRIST = 0
THUMB_TIP = 4
INDEX_MCP, INDEX_PIP, INDEX_TIP = 5, 6, 8
MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP = 9, 10, 12
RING_PIP, RING_TIP = 14, 16
PINKY_PIP, PINKY_TIP = 18, 20

HAND_CONNECTIONS = [
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (5, 9), (9, 10), (10, 11), (11, 12),
    (9, 13), (13, 14), (14, 15), (15, 16),
    (13, 17), (0, 17), (17, 18), (18, 19), (19, 20),
]


# --------------------------------------------------------------------------
# Gesture classification
# --------------------------------------------------------------------------
def _dist(a, b):
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def classify_gesture(lm):
    """lm: list of 21 (x, y, z) tuples. Returns (gesture, pinch_strength)."""
    wrist = lm[WRIST]
    hand_size = max(_dist(wrist, lm[MIDDLE_MCP]), 1e-6)

    # A finger is "extended" when its tip is clearly further from the wrist
    # than its middle joint. Distance-based, so it works at any hand rotation.
    def extended(tip, pip):
        return _dist(wrist, lm[tip]) > _dist(wrist, lm[pip]) * 1.15

    fingers = [
        extended(INDEX_TIP, INDEX_PIP),
        extended(MIDDLE_TIP, MIDDLE_PIP),
        extended(RING_TIP, RING_PIP),
        extended(PINKY_TIP, PINKY_PIP),
    ]

    pinch_dist = _dist(lm[THUMB_TIP], lm[INDEX_TIP]) / hand_size
    # 0.25 (touching) -> 1.0, 0.8 (wide apart) -> 0.0
    pinch = max(0.0, min(1.0, (0.8 - pinch_dist) / (0.8 - 0.25)))

    # In a fist the thumb also rests on the index, so check "fist" before "pinch":
    # all fingers curled AND the index tip tucked in near the palm.
    index_to_palm = _dist(lm[INDEX_TIP], lm[MIDDLE_MCP]) / hand_size

    if not any(fingers) and index_to_palm < 0.6:
        gesture = "fist"
    elif pinch_dist < 0.35:
        gesture = "pinch"
    elif all(fingers):
        gesture = "open"
    elif fingers[0] and not any(fingers[1:]):
        gesture = "point"
    else:
        gesture = "none"
    return gesture, pinch


def build_hand_packet(landmarks, side, score):
    """landmarks: 21 (x, y, z) tuples in normalised image coordinates."""
    gesture, pinch = classify_gesture(landmarks)
    flat = []
    for x, y, z in landmarks:
        flat.extend((round(x, 4), round(y, 4), round(z, 4)))
    return {
        "side": side,
        "score": round(float(score), 3),
        "gesture": gesture,
        "pinch": round(pinch, 3),
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
