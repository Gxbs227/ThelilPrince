"""
Fake hand for testing the Unity side WITHOUT a webcam or MediaPipe.

A synthetic hand acts out a short loop of movements (drift, wave, point left/right,
pinch-and-hold, fist, swipe) and runs through the REAL detectors from gestures.py,
so Unity receives exactly what unity_sender.py would send, story gestures included.

Run:
    python mock_sender.py            # -> 127.0.0.1:5052
    python mock_sender.py --host 192.168.0.20 --port 5052
"""

import argparse
import math
import time

from unity_bridge import HandState, UnityHandSender, build_hand_packet

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
            pts[3] = [-0.03, 0.04]
            pts[4] = [-0.02, 0.03]
    if gesture == "pinch":
        pts[4] = [pts[8][0] + 0.005, pts[8][1] + 0.005]
    return pts


# (name, seconds, pose, function t -> (x, y) centre of the hand)
SCRIPT = [
    ("drift",          4.0, "open",  lambda t: (0.5 + 0.2 * math.cos(t * 0.8), 0.5 + 0.1 * math.sin(t * 1.6))),
    ("wave",           3.0, "open",  lambda t: (0.5 + 0.12 * math.sin(t * 9.0), 0.45)),
    ("point L/R",      3.0, "point", lambda t: (0.35 if int(t / 0.6) % 2 == 0 else 0.65, 0.5)),
    ("pinch and hold", 3.0, "pinch", lambda t: (0.5, 0.5)),
    ("fist",           2.0, "fist",  lambda t: (0.5, 0.55)),
    ("swipe",          1.5, "open",  lambda t: (0.15 + min(t, 0.4) / 0.4 * 0.7, 0.5)),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5052)
    ap.add_argument("--fps", type=float, default=30)
    args = ap.parse_args()

    sender = UnityHandSender(args.host, args.port)
    state = HandState()
    print(f"Sending mock hand to udp://{args.host}:{args.port}  (Ctrl+C to stop)")
    total = sum(s[1] for s in SCRIPT)
    start = time.time()
    last_step, last_events = None, []
    try:
        while True:
            t = (time.time() - start) % total
            for name, seconds, gesture, path in SCRIPT:
                if t < seconds:
                    break
                t -= seconds
            if name != last_step:
                print(f"- {name}")
                last_step = name

            cx, cy = path(t)
            lm = [(cx + x, cy + y, 0.0) for x, y in pose(gesture)]
            fs, events = state.update(lm)
            packet = build_hand_packet(lm, "Right", 1.0, events, fs)
            new = [e for e in events if e not in last_events]
            if new:
                print(f"    story gesture: {', '.join(new)}   (basic pose: {packet['gesture']})")
            last_events = events
            sender.send([packet])
            time.sleep(1.0 / args.fps)
    except KeyboardInterrupt:
        sender.send([])


if __name__ == "__main__":
    main()
