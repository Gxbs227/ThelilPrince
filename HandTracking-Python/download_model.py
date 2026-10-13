"""
Downloads the MediaPipe HandLandmarker model file (hand_landmarker.task).
Run this ONCE before running main.py.

Usage:
    python download_model.py
"""
import os
import urllib.request

MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
    "hand_landmarker/float16/1/hand_landmarker.task"
)
OUT_PATH = os.path.join(os.path.dirname(__file__), "hand_landmarker.task")


def main():
    if os.path.exists(OUT_PATH):
        print(f"Model already exists at {OUT_PATH}, skipping download.")
        return

    print(f"Downloading model from:\n  {MODEL_URL}")
    print(f"Saving to:\n  {OUT_PATH}")
    urllib.request.urlretrieve(MODEL_URL, OUT_PATH)
    print("Done.")


if __name__ == "__main__":
    main()
