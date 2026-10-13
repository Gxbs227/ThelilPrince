"""
gesture_utils.py

Low-level building blocks shared by every gesture detector in gestures.py:

  - FingerState: which fingers are currently extended
  - pinch_distance: how close thumb-tip and index-tip are (for "picking"/"pinch")
  - MotionTracker: keeps a short history of the hand's position so we can
    measure speed, direction, and oscillation (for waves, swipes, pats, etc.)

You generally don't need to edit this file either -- gestures.py is where the
actual gesture rules live.
"""
import math
from collections import deque

# Landmark index shortcuts (see hand_tracker.py for the full list)
WRIST = 0
THUMB_TIP, THUMB_IP = 4, 3
INDEX_TIP, INDEX_PIP, INDEX_MCP = 8, 6, 5
MIDDLE_TIP, MIDDLE_PIP, MIDDLE_MCP = 12, 10, 9
RING_TIP, RING_PIP = 16, 14
PINKY_TIP, PINKY_PIP, PINKY_MCP = 20, 18, 17


def _dist(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def hand_size(hand):
    """A rough scale reference (wrist to middle-finger MCP), used to normalize thresholds
    so detection works whether the hand is close to or far from the camera."""
    return max(_dist(hand[WRIST], hand[MIDDLE_MCP]), 1e-6)


class FingerState:
    """Which fingers are extended, computed from a single frame's landmarks."""

    def __init__(self, hand):
        self.hand = hand
        scale = hand_size(hand)

        # Non-thumb fingers: extended if the tip is further from the wrist
        # than the pip joint is (works regardless of hand rotation).
        self.index = _dist(hand[WRIST], hand[INDEX_TIP]) > _dist(hand[WRIST], hand[INDEX_PIP])
        self.middle = _dist(hand[WRIST], hand[MIDDLE_TIP]) > _dist(hand[WRIST], hand[MIDDLE_PIP])
        self.ring = _dist(hand[WRIST], hand[RING_TIP]) > _dist(hand[WRIST], hand[RING_PIP])
        self.pinky = _dist(hand[WRIST], hand[PINKY_TIP]) > _dist(hand[WRIST], hand[PINKY_PIP])

        # Thumb: compare distance from the pinky-MCP (works for left/right hand
        # and for palm facing toward or away from the camera reasonably well).
        self.thumb = _dist(hand[PINKY_MCP], hand[THUMB_TIP]) > _dist(hand[PINKY_MCP], hand[THUMB_IP]) * 1.1

        self.count = sum([self.thumb, self.index, self.middle, self.ring, self.pinky])
        self.is_fist = self.count == 0
        self.is_open_palm = self.count >= 4
        # "Pointing" = only the index finger extended
        self.is_pointing = self.index and not self.middle and not self.ring and not self.pinky

    def pinch_distance(self):
        """Normalized thumb-tip to index-tip distance (0 = touching, ~1 = fully spread)."""
        return _dist(self.hand[THUMB_TIP], self.hand[INDEX_TIP]) / hand_size(self.hand)


class MotionTracker:
    """
    Tracks the (x, y) position of a reference point (default: wrist) over the
    last `history_len` frames, and derives simple motion features from it:
    velocity, net direction, and oscillation (for waves/swipes/pats).

    Call `update(hand)` once per frame, then read the properties below.
    """

    def __init__(self, history_len=20, ref_point=WRIST):
        self.history_len = history_len
        self.ref_point = ref_point
        self.positions = deque(maxlen=history_len)   # (x, y, z, t)
        self.timestamps = deque(maxlen=history_len)

    def update(self, hand, t=None):
        import time
        if t is None:
            t = time.time()
        x, y, z = hand[self.ref_point]
        self.positions.append((x, y, z))
        self.timestamps.append(t)

    def clear(self):
        self.positions.clear()
        self.timestamps.clear()

    @property
    def ready(self):
        return len(self.positions) >= max(4, self.history_len // 3)

    def velocity(self):
        """Average per-second (dx, dy) velocity over the tracked window (normalized coords/sec)."""
        if len(self.positions) < 2:
            return 0.0, 0.0
        (x0, y0, _), t0 = self.positions[0], self.timestamps[0]
        (x1, y1, _), t1 = self.positions[-1], self.timestamps[-1]
        dt = max(t1 - t0, 1e-6)
        return (x1 - x0) / dt, (y1 - y0) / dt

    def amplitude_x(self):
        """How far the hand has swept left-right within the tracked window."""
        if not self.positions:
            return 0.0
        xs = [p[0] for p in self.positions]
        return max(xs) - min(xs)

    def amplitude_y(self):
        if not self.positions:
            return 0.0
        ys = [p[1] for p in self.positions]
        return max(ys) - min(ys)

    def zero_crossings_x(self, threshold=0.01):
        """
        Counts direction reversals in horizontal movement -- a fast back-and-forth
        wave has many crossings in a short window; a single swipe has ~0-1.
        """
        xs = [p[0] for p in self.positions]
        if len(xs) < 3:
            return 0
        diffs = [xs[i + 1] - xs[i] for i in range(len(xs) - 1)]
        signs = [1 if d > threshold else (-1 if d < -threshold else 0) for d in diffs]
        signs = [s for s in signs if s != 0]
        crossings = sum(1 for i in range(len(signs) - 1) if signs[i] != signs[i + 1])
        return crossings

    def duration(self):
        if len(self.timestamps) < 2:
            return 0.0
        return self.timestamps[-1] - self.timestamps[0]
