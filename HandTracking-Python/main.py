"""
main.py

The script you actually run to test a gesture against your webcam.

Usage:
    python main.py

It will:
  1. list the available gesture detectors from gestures.py
  2. ask you which one to test (type its number)
  3. open your webcam and show a live feed with:
       - the hand skeleton drawn on top
       - debug numbers (finger states, motion stats)
       - a big green "DETECTED!" banner whenever your gesture fires

Controls while running:
  q  = quit
  r  = reset the detector's internal state (counters/history)
"""
import sys
import time
import cv2

from hand_tracker import HandTracker, draw_landmarks, open_webcam
from gesture_utils import FingerState, MotionTracker
from gestures import ALL_GESTURES


def choose_gesture():
    print("\nAvailable gestures to test:")
    for key, (name, _cls) in ALL_GESTURES.items():
        print(f"  [{key}] {name}")
    choice = input("\nType the number of the gesture you want to test, then press Enter: ").strip()
    if choice not in ALL_GESTURES:
        print("Unknown choice, defaulting to '1'.")
        choice = "1"
    return ALL_GESTURES[choice]


class HandSlot:
    """
    Holds one independent detector + motion tracker + trigger counter, so each
    physical hand (slot 0, slot 1) has its own state and doesn't interfere with
    the other hand's gesture progress.
    """

    def __init__(self, detector_cls):
        self.detector_cls = detector_cls
        self.detector = detector_cls()
        self.motion = MotionTracker(history_len=20)
        self.last_active = False
        self.trigger_count = 0
        self.handedness = "?"

    def reset(self):
        self.detector = self.detector_cls()
        self.motion.clear()
        self.trigger_count = 0
        self.last_active = False


def main():
    name, detector_cls = choose_gesture()
    two_hands_input = input("Track one hand or two? Type 1 or 2 (default 1): ").strip()
    max_hands = 2 if two_hands_input == "2" else 1

    # One independent slot per possible hand (slot 0 = first hand MediaPipe reports
    # that frame, slot 1 = second). MediaPipe doesn't guarantee the same hand always
    # lands in the same slot frame-to-frame, but for testing a gesture on each of
    # your hands this is good enough -- watch the on-screen "Left"/"Right" label.
    slots = [HandSlot(detector_cls) for _ in range(max_hands)]

    print(f"\nTracking {max_hands} hand(s). Starting webcam. "
          "Press 'q' to quit, 'r' to reset all detectors.\n")
    tracker = HandTracker(max_hands=max_hands)
    cap = open_webcam()

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                print("Failed to read from webcam.")
                break

            frame = cv2.flip(frame, 1)  # mirror, feels more natural
            hands = tracker.process(frame)

            if not hands:
                for slot in slots:
                    slot.motion.clear()
                    slot.last_active = False
                cv2.putText(frame, "No hand detected", (10, 60),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2, cv2.LINE_AA)

            y = 60
            for slot_index, slot in enumerate(slots):
                if slot_index >= len(hands):
                    # This slot currently has no hand in view -- clear its motion
                    # history so a wave/swipe doesn't "resume" once the hand comes back.
                    slot.motion.clear()
                    slot.last_active = False
                    continue

                hand_data = hands[slot_index]
                landmarks = hand_data["landmarks"]
                slot.handedness = hand_data["handedness"]

                draw_landmarks(frame, landmarks)

                fs = FingerState(landmarks)
                slot.motion.update(landmarks)

                result = slot.detector.update(fs, slot.motion)
                is_active = result["active"]

                if is_active and not slot.last_active:
                    slot.trigger_count += 1
                    print(f"[{time.strftime('%H:%M:%S')}] hand#{slot_index} "
                          f"({slot.handedness}) {result['label']} DETECTED "
                          f"(#{slot.trigger_count}) -- {result['detail']}")
                slot.last_active = is_active

                status_text = (f"Hand {slot_index} ({slot.handedness}): {result['label']} "
                               f"{'DETECTED' if is_active else 'watching...'} "
                               f"[triggers={slot.trigger_count}]")
                status_color = (0, 200, 0) if is_active else (0, 165, 255)
                cv2.putText(frame, status_text, (10, y),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, status_color, 2, cv2.LINE_AA)
                y += 30

                debug_line = (
                    f"  fingers(t,i,m,r,p)="
                    f"{int(fs.thumb)}{int(fs.index)}{int(fs.middle)}{int(fs.ring)}{int(fs.pinky)} "
                    f"pinch={fs.pinch_distance():.2f} | {result['detail']}"
                )
                cv2.putText(frame, debug_line, (10, y),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1, cv2.LINE_AA)
                y += 25

            cv2.putText(frame, f"Testing: {name}  (q=quit, r=reset)", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2, cv2.LINE_AA)

            cv2.imshow("Gesture test - MediaPipe", frame)
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q'):
                break
            elif key == ord('r'):
                for slot in slots:
                    slot.reset()
                print("--- all detectors reset ---")

    finally:
        cap.release()
        cv2.destroyAllWindows()
        tracker.close()


if __name__ == "__main__":
    sys.exit(main() or 0)
