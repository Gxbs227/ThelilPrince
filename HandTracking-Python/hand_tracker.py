"""
The Little Prince - MediaPipe -> Unity hand tracking bridge.

Reads the webcam, runs MediaPipe Hands, classifies a simple gesture per hand
and streams everything to Unity as one small JSON packet per frame over UDP.

Run:
    python hand_tracker.py                 # default: camera 0 -> 127.0.0.1:5052
    python hand_tracker.py --camera 1 --port 5052 --max-hands 2
    python hand_tracker.py --no-preview    # no OpenCV window

Packet format (must match HandTrackingReceiver.cs in Unity):
{
  "t": 1712345678.12,          # sender time (seconds)
  "frame": 123,
  "hands": [
    {
      "side": "Right",         # "Left" / "Right" as the user sees it (mirrored view)
      "score": 0.97,
      "gesture": "open",       # open | fist | pinch | point | none
      "pinch": 0.85,           # 0..1 how closed thumb+index are
      "lm": [x0, y0, z0, ... x20, y20, z20]   # 21 landmarks, normalised image coords
    }
  ]
}
x, y are 0..1 with the origin in the TOP-LEFT of the (mirrored) image.
An empty "hands" list is sent when no hand is visible, so Unity knows it was lost.

If you already have your own MediaPipe loop, you only need unity_bridge.py
(`UnityHandSender` + `build_hand_packet()`): call sender.send(hands) once per frame.
"""

import argparse
import os
import time
import urllib.request

import cv2
import mediapipe as mp

from unity_bridge import HAND_CONNECTIONS, UnityHandSender, build_hand_packet

TASK_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
    "hand_landmarker/float16/latest/hand_landmarker.task"
)


# --------------------------------------------------------------------------
# MediaPipe backends (legacy "solutions" API or the newer "tasks" API)
# --------------------------------------------------------------------------
class SolutionsBackend:
    def __init__(self, max_hands, min_det, min_track):
        self.hands = mp.solutions.hands.Hands(
            static_image_mode=False,
            max_num_hands=max_hands,
            model_complexity=1,
            min_detection_confidence=min_det,
            min_tracking_confidence=min_track,
        )

    def process(self, rgb, _timestamp_ms):
        res = self.hands.process(rgb)
        out = []
        if res.multi_hand_landmarks:
            for lms, handed in zip(res.multi_hand_landmarks, res.multi_handedness):
                c = handed.classification[0]
                out.append(([(p.x, p.y, p.z) for p in lms.landmark], c.label, c.score))
        return out

    def close(self):
        self.hands.close()


class TasksBackend:
    def __init__(self, max_hands, min_det, min_track, model_path):
        from mediapipe.tasks import python as mp_python
        from mediapipe.tasks.python import vision

        if not os.path.exists(model_path):
            print(f"Downloading hand model to {model_path} ...")
            urllib.request.urlretrieve(TASK_MODEL_URL, model_path)

        options = vision.HandLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=model_path),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=max_hands,
            min_hand_detection_confidence=min_det,
            min_tracking_confidence=min_track,
        )
        self.landmarker = vision.HandLandmarker.create_from_options(options)

    def process(self, rgb, timestamp_ms):
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        res = self.landmarker.detect_for_video(image, timestamp_ms)
        out = []
        for lms, handed in zip(res.hand_landmarks, res.handedness):
            c = handed[0]
            out.append(([(p.x, p.y, p.z) for p in lms], c.category_name, c.score))
        return out

    def close(self):
        self.landmarker.close()


def make_backend(args):
    use_solutions = args.backend == "solutions" or (
        args.backend == "auto" and hasattr(mp, "solutions") and hasattr(mp.solutions, "hands")
    )
    if use_solutions:
        print("Using MediaPipe 'solutions' Hands API")
        return SolutionsBackend(args.max_hands, args.min_detection, args.min_tracking)
    print("Using MediaPipe 'tasks' HandLandmarker API")
    return TasksBackend(args.max_hands, args.min_detection, args.min_tracking, args.model)


# --------------------------------------------------------------------------
def draw_preview(frame, hands):
    h, w = frame.shape[:2]
    for hand in hands:
        lm = hand["lm"]
        pts = [(int(lm[i * 3] * w), int(lm[i * 3 + 1] * h)) for i in range(21)]
        for a, b in HAND_CONNECTIONS:
            cv2.line(frame, pts[a], pts[b], (235, 220, 255), 2)
        for p in pts:
            cv2.circle(frame, p, 4, (120, 200, 255), -1)
        label = f'{hand["side"]}: {hand["gesture"]}  pinch {hand["pinch"]:.2f}'
        cv2.putText(frame, label, (pts[0][0] - 60, pts[0][1] + 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="Stream MediaPipe hands to Unity over UDP")
    ap.add_argument("--host", default="127.0.0.1", help="IP of the machine running Unity")
    ap.add_argument("--port", type=int, default=5052)
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--max-hands", type=int, default=2)
    ap.add_argument("--min-detection", type=float, default=0.6)
    ap.add_argument("--min-tracking", type=float, default=0.5)
    ap.add_argument("--no-mirror", action="store_true", help="don't mirror the webcam image")
    ap.add_argument("--no-preview", action="store_true", help="don't open the preview window")
    ap.add_argument("--backend", choices=["auto", "solutions", "tasks"], default="auto")
    ap.add_argument("--model", default=os.path.join(here, "hand_landmarker.task"),
                    help="model file for the tasks backend (downloaded if missing)")
    args = ap.parse_args()

    cap = cv2.VideoCapture(args.camera)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
    if not cap.isOpened():
        raise SystemExit(f"Could not open camera {args.camera}")

    backend = make_backend(args)
    sender = UnityHandSender(args.host, args.port)
    print(f"Streaming hands to udp://{args.host}:{args.port}  (press Q or Esc to quit)")

    start = time.time()
    last_ts = -1
    fps, fps_t, fps_n = 0.0, time.time(), 0
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                continue
            if not args.no_mirror:
                frame = cv2.flip(frame, 1)  # selfie view: moving right moves right in Unity

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            ts = int((time.time() - start) * 1000)
            ts = max(ts, last_ts + 1)  # tasks API needs strictly increasing timestamps
            last_ts = ts

            hands = []
            for landmarks, side, score in backend.process(rgb, ts):
                if args.no_mirror:
                    # MediaPipe assumes a mirrored image; swap so "Right" is the user's right hand
                    side = "Left" if side == "Right" else "Right"
                hands.append(build_hand_packet(landmarks, side, score))
            sender.send(hands)

            fps_n += 1
            if time.time() - fps_t >= 1.0:
                fps, fps_t, fps_n = fps_n / (time.time() - fps_t), time.time(), 0

            if not args.no_preview:
                draw_preview(frame, hands)
                cv2.putText(frame, f"{fps:.0f} fps -> {args.host}:{args.port}", (10, 25),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
                cv2.imshow("Little Prince - hand tracking", frame)
                if cv2.waitKey(1) & 0xFF in (ord("q"), 27):
                    break
    except KeyboardInterrupt:
        pass
    finally:
        sender.send([])  # tell Unity the hand is gone
        backend.close()
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
