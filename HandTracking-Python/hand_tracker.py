"""
hand_tracker.py

Thin wrapper around MediaPipe's HandLandmarker (Tasks API) that:
  - opens the webcam
  - runs hand landmark detection on each frame
  - returns landmarks in a simple, easy-to-use format

You normally won't need to edit this file. Import `HandTracker` and use it
inside main.py or your own scripts.
"""
import os
import time
import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision as mp_vision

MODEL_PATH = os.path.join(os.path.dirname(__file__), "hand_landmarker.task")

# The 21 hand landmark indices MediaPipe returns, for reference:
#  0 WRIST
#  1 THUMB_CMC   2 THUMB_MCP   3 THUMB_IP    4 THUMB_TIP
#  5 INDEX_MCP   6 INDEX_PIP   7 INDEX_DIP   8 INDEX_TIP
#  9 MIDDLE_MCP 10 MIDDLE_PIP 11 MIDDLE_DIP 12 MIDDLE_TIP
# 13 RING_MCP   14 RING_PIP   15 RING_DIP   16 RING_TIP
# 17 PINKY_MCP  18 PINKY_PIP  19 PINKY_DIP  20 PINKY_TIP


class HandTracker:
    def __init__(self, max_hands=1, min_detection_confidence=0.5, min_tracking_confidence=0.5):
        if not os.path.exists(MODEL_PATH):
            raise FileNotFoundError(
                f"Model file not found at {MODEL_PATH}.\n"
                "Run `python download_model.py` first."
            )

        base_options = mp_python.BaseOptions(model_asset_path=MODEL_PATH)
        options = mp_vision.HandLandmarkerOptions(
            base_options=base_options,
            running_mode=mp_vision.RunningMode.VIDEO,
            num_hands=max_hands,
            min_hand_detection_confidence=min_detection_confidence,
            min_hand_presence_confidence=min_tracking_confidence,
            min_tracking_confidence=min_tracking_confidence,
        )
        self.landmarker = mp_vision.HandLandmarker.create_from_options(options)
        self._start_time = time.time()

    def process(self, frame_bgr):
        """
        Runs hand detection on a single BGR frame (as returned by cv2.VideoCapture).

        Returns a list of hands, one entry per detected hand (up to `max_hands`).
        Each entry is a dict:
            {
                "landmarks": [(x, y, z), ...21 points...],  # normalized 0-1, z = rough depth
                "handedness": "Left" | "Right" | "Unknown",
            }
        Returns an empty list if no hand is detected.

        Note: MediaPipe's "Left"/"Right" label is from the camera's point of view
        (i.e. mirrored relative to how the label refers to *your* left/right hand,
        since the webcam sees you like a mirror). main.py flips the frame for
        display, which also flips which label corresponds to which of your hands --
        don't rely on the label being 100% intuitive, just consistent frame-to-frame.
        """
        frame_rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=frame_rgb)
        timestamp_ms = int((time.time() - self._start_time) * 1000)

        result = self.landmarker.detect_for_video(mp_image, timestamp_ms)

        hands = []
        if result.hand_landmarks:
            for i, hand_landmarks in enumerate(result.hand_landmarks):
                landmarks = [(lm.x, lm.y, lm.z) for lm in hand_landmarks]
                label = "Unknown"
                if result.handedness and i < len(result.handedness) and result.handedness[i]:
                    label = result.handedness[i][0].category_name
                hands.append({"landmarks": landmarks, "handedness": label})
        return hands

    def close(self):
        self.landmarker.close()


def draw_landmarks(frame_bgr, hand, color=(0, 255, 0)):
    """Draws the 21 landmarks + simple bone connections for one hand onto the frame (in place)."""
    h, w, _ = frame_bgr.shape
    pts = [(int(x * w), int(y * h)) for (x, y, z) in hand]

    connections = [
        (0, 1), (1, 2), (2, 3), (3, 4),          # thumb
        (0, 5), (5, 6), (6, 7), (7, 8),          # index
        (5, 9), (9, 10), (10, 11), (11, 12),     # middle
        (9, 13), (13, 14), (14, 15), (15, 16),   # ring
        (13, 17), (17, 18), (18, 19), (19, 20),  # pinky
        (0, 17),
    ]
    for a, b in connections:
        cv2.line(frame_bgr, pts[a], pts[b], color, 2)
    for p in pts:
        cv2.circle(frame_bgr, p, 4, (0, 0, 255), -1)


def open_webcam(camera_index=None, width=960, height=540):
    """
    Opens a webcam and makes sure it can really deliver frames.

    On Windows the default camera backend (MSMF) sometimes "opens" fine but then
    fails to give any picture, so we try DirectShow first, then the default.
    If camera_index is None we also try cameras 0, 1 and 2.
    """
    import sys
    indexes = [camera_index] if camera_index is not None else [0, 1, 2]
    backends = [cv2.CAP_DSHOW, cv2.CAP_ANY] if sys.platform.startswith("win") else [cv2.CAP_ANY]

    for idx in indexes:
        for backend in backends:
            cap = cv2.VideoCapture(idx, backend)
            if not cap.isOpened():
                cap.release()
                continue
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)

            # Cameras often need a few frames to "warm up" -- try several reads.
            for _ in range(30):
                ok, _frame = cap.read()
                if ok:
                    print(f"Webcam opened (camera {idx}).")
                    return cap
                time.sleep(0.05)
            cap.release()

    raise RuntimeError(
        "Could not get any picture from the webcam.\n"
        "  1. Close Google Meet / Zoom / Teams / Camera app / any browser tab using the camera.\n"
        "  2. Check Windows Settings > Privacy & security > Camera > allow desktop apps.\n"
        "  3. Run again."
    )
