# ThelilPrince
IXD 2 project: a soft low-poly walk-through based on *The Little Prince*, controlled with hand movements seen by a webcam.

> "Don't think too much, eventually you will find a way."

Three stages (garden → village → outer space), 5–8 minutes, looping back to the start after the final quote and animation. There is no goal to reach: the journey is the point.

---

## Hand tracking: MediaPipe → Unity

```
 Webcam ──► hand_tracker.py (MediaPipe) ──UDP 5052, JSON──► HandTrackingReceiver (Unity)
                                                              ├─ HandCameraController  (walk / look)
                                                              ├─ HandCursor            (point + pinch at objects)
                                                              ├─ HandInteractable      (flowers, rose, fox…)
                                                              └─ HandDebugVisualizer   (F1 status overlay)
```

### 1. Python side

```bash
cd HandTracking-Python
python -m venv .venv
# Windows: .venv\Scripts\activate      macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt

python hand_tracker.py            # webcam 0 -> Unity on this computer, port 5052
```

A preview window shows the skeleton and the detected gesture. Press **Q** or **Esc** to quit.

Useful options:

| Option | What it does |
|---|---|
| `--camera 1` | use another webcam |
| `--host 192.168.x.x` | Unity runs on a **different** computer |
| `--port 5052` | UDP port (must match the Receiver in Unity) |
| `--max-hands 1` | track only one hand |
| `--no-preview` | no OpenCV window (for the exhibition) |

Newer MediaPipe releases (1.x) no longer include the old `mp.solutions.hands` API. The script detects this and switches to the new Tasks `HandLandmarker`, downloading `hand_landmarker.task` (about 8 MB) the first time.

**No webcam?** Run `python mock_sender.py`. It sends a fake hand that moves around and cycles through the gestures, so you can test the connection without OpenCV or MediaPipe installed.

**Already have your own MediaPipe script?** Keep it. Copy `unity_bridge.py` next to it and add:

```python
from unity_bridge import UnityHandSender, build_hand_packet
sender = UnityHandSender("127.0.0.1", 5052)
# every frame, for each detected hand:
hands = [build_hand_packet([(p.x, p.y, p.z) for p in hand_landmarks], "Right", score)]
sender.send(hands)          # send [] when no hand is visible
```

### 2. Unity side

1. Copy the folder `Unity/Assets/LittlePrince` into the `Assets/` folder of your Unity project. The scripts were compile-checked against Unity 2021.3 and work with the Built-in pipeline, URP and HDRP, and with either the old or new Input System.
2. In the Unity menu choose **Little Prince → Create Hand Tracking Test Scene**. This creates `Assets/LittlePrince/Scenes/HandTrackingTest.unity`: a pastel garden with pinchable flowers and a rose.
3. Press **Play**, then start `hand_tracker.py` (or `mock_sender.py`).
4. The top-left overlay (toggle with **F1**) should turn green (**● MediaPipe connected**) and show the gesture and a live hand skeleton.

With no Python running, the **mouse acts as the hand**: move to aim, hold left for pinch, hold right for open palm (walk), and hold both for fist. WASD or the arrow keys also move the camera.

To add hand tracking to your own scenes, add these components:

- One `HandTrackingReceiver` in the scene (add `HandDebugVisualizer` next to it while testing).
- `HandCameraController` on the player object. Set its child camera as **Pitch Pivot**. Add a `CharacterController` for collisions.
- `HandCursor` anywhere, with **Target Camera** set.
- `HandInteractable` plus a Collider on anything touchable. Wire your animations and sounds into `onHoverEnter`, `onSelect` and the other events in the Inspector.

### Gestures

| Gesture | In the experience |
|---|---|
| ✋ Open palm | walk forward gently; move the hand left or right to turn, up or down to look |
| ☝️ Point | stand still and aim the cursor (turns slowly) |
| 🤏 Pinch | select or touch what the cursor is on; the view holds still |
| ✊ Fist | stop and rest |
| (swipe) | `HandTrackingReceiver.Swiped` event, e.g. for turning a story page |

Speeds, dead zone and smoothing can be changed in the Inspector. The defaults are deliberately slow and floaty to match the relaxed, no-pressure tone.

### Scripting hooks for the stages

```csharp
var hands = HandTrackingReceiver.Instance;
hands.HandFound      += h => { /* first interaction: e.g. start defining the garden background */ };
hands.GestureChanged += (h, g) => { if (g == HandGesture.Open) { /* ... */ } };
hands.Swiped         += (h, dir) => { /* dir = left/right/up/down */ };
HandInteractable.AnySelected += obj => { /* count interactions to move to the next stage */ };
// Freeze walking during a transition or the final video:
player.GetComponent<HandCameraController>().InputEnabled = false;
```

### Troubleshooting

- **The overlay stays red or yellow while Python is running:** check that both sides use the same port. Only one app can listen on a port, so close any second Unity instance. If Unity runs on another computer, pass `--host <that computer's IP>` and allow UDP 5052 through its firewall.
- **Could not open UDP port:** another program is already using 5052. Change the port on both sides.
- **Left and right feel reversed:** `hand_tracker.py` mirrors the webcam like a selfie by default. If you run it with `--no-mirror`, tick **Mirror X** on the Receiver.
- **Jittery cursor:** raise **Smoothing** on the Receiver (0.6 → 0.75) and keep the hand well lit.
