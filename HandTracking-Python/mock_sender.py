"""
Fake hand data for testing the Unity side WITHOUT a webcam or MediaPipe.

A synthetic hand drifts slowly in a circle and cycles through the gestures
(open -> point -> pinch -> fist) every few seconds, using exactly the same
packet format as hand_tracker.py.

Run:
    python mock_sender.py            # -> 127.0.0.1:5052
    python mock_sender.py --port 5052 --host 192.168.0.20
"""

import argparse
import math
import time

from unity_bridge import UnityHandSender, build_hand_packet

# A rough open right hand in normalised image coords, centred on (0, 0), wrist at the bottom.
OPEN_HAND = [
    (0.00, 0.12), (-0.04, 0.09), (-0.07, 0.05), (-0.09, 0.02), (-0.11, -0.01),  # wrist + thumb
    (-0.03, 0.00), (-0.035, -0.05), (-0.04, -0.08), (-0.045, -0.11),              # index
    (0.00, -0.005), (0.00, -0.06), (0.00, -0.095), (0.00, -0.125),                # middle
    (0.03, 0.00), (0.035, -0.05), (0.04, -0.08), (0.045, -0.105),                 # ring
    (0.055, 0.015), (0.065, -0.025), (0.07, -0.05), (0.075, -0.07),               # pinky
]
FINGERS = {"index": (5, 8), "middle": (9, 12), "ring": (13, 16), "pinky": (17, 20)}


def pose(gesture):
    pts = [list(p) for p in OPEN_HAND]

    def curl(name):
        mcp, tip = FINGERS[name]
        for i in range(mcp + 1, tip + 1):  # fold the finger back onto its knuckle
            pts[i][0] = pts[mcp][0] * 0.9
            pts[i][1] = pts[mcp][1] + 0.02
    if gesture in ("fist", "point"):
        for name in ("middle", "ring", "pinky"):
            curl(name)
        if gesture == "fist":
            curl("index")
            pts[4] = [-0.02, 0.03]
    if gesture == "pinch":
        pts[4] = [pts[8][0] + 0.005, pts[8][1] + 0.005]
    return pts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5052)
    ap.add_argument("--fps", type=float, default=30)
    args = ap.parse_args()

    sender = UnityHandSender(args.host, args.port)
    cycle = ["open", "open", "point", "pinch", "open", "fist"]
    print(f"Sending mock hand to udp://{args.host}:{args.port}  (Ctrl+C to stop)")
    start = time.time()
    last = None
    try:
        while True:
            t = time.time() - start
            gesture = cycle[int(t / 2.5) % len(cycle)]
            cx = 0.5 + 0.25 * math.cos(t * 0.4)
            cy = 0.5 + 0.15 * math.sin(t * 0.8)
            lm = [(cx + x, cy + y, 0.0) for x, y in pose(gesture)]
            packet = build_hand_packet(lm, "Right", 0.99)
            if packet["gesture"] != last:
                print(f"  gesture: {packet['gesture']}")
                last = packet["gesture"]
            sender.send([packet])
            time.sleep(1.0 / args.fps)
    except KeyboardInterrupt:
        sender.send([])


if __name__ == "__main__":
    main()
