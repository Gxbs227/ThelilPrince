"""
unity_sender.py

Like main.py, but instead of testing ONE gesture it runs ALL the detectors
from gestures.py at the same time (per hand) and streams the hand + every
active story gesture to Unity over UDP.

Usage:
    python unity_sender.py                    # -> Unity on this computer, port 5052
    python unity_sender.py --hands 1          # track only one hand
    python unity_sender.py --host 192.168.0.20 --port 5052 --camera 1
    python unity_sender.py --no-preview       # no OpenCV window (exhibition)

Controls in the preview window:
  q / Esc = quit     r = reset all detectors
"""
import argparse
import os
import time

import cv2

import download_model
from hand_tracker import MODEL_PATH, HandTracker, draw_landmarks, open_webcam
from unity_bridge import FORGET_AFTER_SECONDS, HandState, UnityHandSender, build_hand_packet

def main():
    ap = argparse.ArgumentParser(description="Stream hands + story gestures to Unity")
    ap.add_argument("--host", default="127.0.0.1", help="IP of the computer running Unity")
    ap.add_argument("--port", type=int, default=5052)
    ap.add_argument("--camera", type=int, default=None, help="camera index (default: try 0, 1, 2)")
    ap.add_argument("--hands", type=int, default=2, choices=[1, 2])
    ap.add_argument("--no-mirror", action="store_true", help="don't mirror the webcam image")
    ap.add_argument("--no-preview", action="store_true", help="don't open the preview window")
    args = ap.parse_args()

    if not os.path.exists(MODEL_PATH):
        download_model.main()

    tracker = HandTracker(max_hands=args.hands)
    cap = open_webcam(args.camera)
    sender = UnityHandSender(args.host, args.port)
    states = {}  # "Left"/"Right" -> HandState
    print(f"Streaming to udp://{args.host}:{args.port}  (q = quit, r = reset)")

    fps, fps_t, fps_n = 0.0, time.time(), 0
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                print("Failed to read from webcam.")
                break
            if not args.no_mirror:
                frame = cv2.flip(frame, 1)  # selfie view, same as main.py

            packets, drawn, seen = [], [], set()
            for hand in tracker.process(frame):
                side = hand["handedness"] if hand["handedness"] in ("Left", "Right") else "Right"
                if side in seen:  # MediaPipe sometimes labels both hands the same
                    side = "Left" if side == "Right" else "Right"
                if side in seen:
                    continue
                seen.add(side)

                state = states.setdefault(side, HandState())
                fs, events = state.update(hand["landmarks"])
                packet = build_hand_packet(hand["landmarks"], side, 1.0, events, fs)
                packets.append(packet)
                drawn.append((packet, hand["landmarks"]))

                for label in events:
                    if label not in state.printed:
                        print(f"[{time.strftime('%H:%M:%S')}] {side}: {label}")
                state.printed = set(events)

            # Hands out of view: clear motion (like main.py), forget them after a while.
            now = time.time()
            for side in list(states):
                if side not in seen:
                    states[side].motion.clear()
                    states[side].active_until.clear()
                    if now - states[side].last_seen > FORGET_AFTER_SECONDS:
                        del states[side]

            sender.send(packets)

            fps_n += 1
            if now - fps_t >= 1.0:
                fps, fps_t, fps_n = fps_n / (now - fps_t), now, 0

            if not args.no_preview:
                y = 30
                cv2.putText(frame, f"{fps:.0f} fps -> Unity {args.host}:{args.port}  (q=quit, r=reset)",
                            (10, y), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 0), 2, cv2.LINE_AA)
                for p, landmarks in drawn:
                    draw_landmarks(frame, landmarks)
                    y += 28
                    text = f"{p['side']}: {p['gesture']}  {' '.join(p['events'])}"
                    color = (0, 200, 0) if p["events"] else (0, 165, 255)
                    cv2.putText(frame, text, (10, y), cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2, cv2.LINE_AA)
                cv2.imshow("Little Prince -> Unity", frame)
                key = cv2.waitKey(1) & 0xFF
                if key in (ord("q"), 27):
                    break
                if key == ord("r"):
                    states.clear()
                    print("--- all detectors reset ---")
    except KeyboardInterrupt:
        pass
    finally:
        sender.send([])  # tell Unity the hands are gone
        cap.release()
        cv2.destroyAllWindows()
        tracker.close()


if __name__ == "__main__":
    main()
