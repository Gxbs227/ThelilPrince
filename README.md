# ThelilPrince
IXD 2 project: a soft low-poly walk-through based on *The Little Prince*, controlled with hand movements seen by a webcam.

> "Don't think too much, eventually you will find a way."

Three stages (garden → village → outer space), 5–8 minutes, looping back to the start after the final quote and animation. There is no goal to reach: the journey is the point.

---

## Hand tracking: MediaPipe → Unity

```
 Webcam ──► hand_tracker.py (MediaPipe HandLandmarker)
             └► gestures.py: all 11 story gesture detectors, per hand
                 └► unity_sender.py ──UDP 5052, JSON──► HandTrackingReceiver (Unity)
                                                          ├─ StoryGestureTrigger   (WAVE, SWIPE, TOUCH… → UnityEvent)
                                                          ├─ HandCameraController  (walk / look)
                                                          ├─ HandCursor            (point + pinch at objects)
                                                          ├─ HandInteractable      (flowers, rose, fox…)
                                                          └─ HandDebugVisualizer   (F1 status overlay)
```

### 1. Python side (`HandTracking-Python/`)

| File | What it is |
|---|---|
| `hand_tracker.py`, `gesture_utils.py`, `gestures.py` | our MediaPipe tracker and the story gesture detectors (tune thresholds at `TUNE ME`) |
| `main.py` | tests **one** gesture at a time on the webcam, with no Unity involved |
| `download_model.py` | downloads `hand_landmarker.task` (runs automatically from `unity_sender.py` if missing) |
| `unity_sender.py` | **runs all detectors at once and streams to Unity** |
| `unity_bridge.py` | builds and sends the UDP packets (used by the two senders) |
| `mock_sender.py` | fake hand for testing Unity without a webcam; still uses the real detectors |

```bash
cd HandTracking-Python
python -m venv .venv
# Windows: .venv\Scripts\activate      macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt

python unity_sender.py            # webcam -> Unity on this computer, port 5052
```

The preview window shows the skeleton, the basic pose, and every story gesture that is currently active (in green). They are also printed in the console. Press **q** to quit and **r** to reset the detectors.

| Option | What it does |
|---|---|
| `--camera 1` | use a specific webcam (by default it tries 0, 1, 2) |
| `--hands 1` | track only one hand |
| `--host 192.168.x.x` | Unity runs on a **different** computer |
| `--port 5052` | UDP port (must match the Receiver in Unity) |
| `--no-preview` | no OpenCV window (for the exhibition) |

Each hand is sent with:
- **Landmarks:** the 21 points.
- **Basic pose:** `open` / `fist` / `pinch` / `point`, taken from `FingerState` and used for walking and the cursor.
- **Story gestures:** the labels from `gestures.py` that are active right now, e.g. `WAVE_CALM`, `SWIPE`, `TOUCH`. A one-frame gesture like `TOUCH` is held for 0.3 s so Unity can't miss it.

**No webcam?** Run `python mock_sender.py`. It acts out a loop of drift → wave → point left/right → pinch-and-hold → fist → swipe, runs it through the real detectors, and prints which story gestures fire.

**New detector?** Add it to `ALL_GESTURES` in `gestures.py`. `unity_sender.py` picks it up automatically, and in Unity its label shows in the F1 overlay and arrives through `StoryGestureStarted`. Add it to the `StoryGesture` enum in `StoryGestureTrigger.cs` to make it selectable in the Inspector.

### 2. Unity side

1. Copy the folder `Unity/Assets/LittlePrince` into the `Assets/` folder of your Unity project. The scripts were compile-checked against Unity 2021.3 and work with the Built-in pipeline, URP and HDRP, and with either the old or new Input System.
2. In the Unity menu choose **Little Prince → Create Hand Tracking Test Scene**. This creates `Assets/LittlePrince/Scenes/HandTrackingTest.unity`: a pastel garden with pinchable flowers and a rose.
3. Press **Play**, then start `unity_sender.py` (or `mock_sender.py`).
4. The top-left overlay (toggle with **F1**) should turn green (**● MediaPipe connected**) and show the pose, the active story gestures and a live hand skeleton.
5. Pinch a flower to make it bloom. The rose only blooms with the story gesture **pinch and hold still** (`PICK_AND_LOOK`) while you point at it.

With no Python running, the **mouse acts as the hand**: move to aim, hold left for pinch, hold right for open palm (walk), and hold both for fist. WASD or the arrow keys also move the camera.

To add hand tracking to your own scenes, add these components:

- One `HandTrackingReceiver` in the scene (add `HandDebugVisualizer` next to it while testing).
- `HandCameraController` on the player object. Set its child camera as **Pitch Pivot**. Add a `CharacterController` for collisions.
- `HandCursor` anywhere, with **Target Camera** set.
- `HandInteractable` plus a Collider on anything touchable. Wire your animations and sounds into `onHoverEnter`, `onSelect` and the other events in the Inspector.
- `StoryGestureTrigger` for each story moment. Pick the **Gesture** (e.g. Swipe on the bush and fox, Wave Any on the children, Touch on the stars) and hook the reaction into **On Detected**. Use **Require Hover** if the user must point at the object, and **Max Triggers** = 1 for a one-time moment. Enable the component only while that part of the story is active. To test without a camera, right-click the component → **Test: fire now** in Play mode.

### Gestures

Basic poses (always on, drive movement):

| Pose | In the experience |
|---|---|
| ✋ Open palm | walk forward gently; move the hand left or right to turn, up or down to look |
| ☝️ Point | stand still and aim the cursor (turns slowly) |
| 🤏 Pinch | select or touch what the cursor is on; the view holds still |
| ✊ Fist | stop and rest |

While a story gesture is active the camera holds still, so waving doesn't also walk and turn (`Hold Still During Story Gestures` on the controller).

Story gestures (from `gestures.py`, selectable in `StoryGestureTrigger`):

| Stage | Gesture | Label |
|---|---|---|
| 1 | Clasp and open, moving up (stars light up) | `CLASP_OPEN_UPWARD` |
| 1 | Point alternating left/right (footprints) | `POINT_ALTERNATE_LR` |
| 1–2 | Pick forward (roses, paper) | `PICK_FORWARD` |
| 2 | Wave rapid / calm (children, switchman) | `WAVE_RAPID` / `WAVE_CALM` |
| 2 | Pat on shoulder (switchman) | `PAT_SHOULDER` |
| 2–3 | Clasp and swing / running (village, rose) | `CLASP_SWING_RUN` |
| 2 | Swipe (bush, fox) | `SWIPE` |
| 2 | Pick and look (paper from the fox) | `PICK_AND_LOOK` |
| 2 | Fold paper (origami) | `FOLD_PAPER` |
| 3 | Point up (stars) | `POINT_UP` |
| 3 | Touch (stars, rose) | `TOUCH` |

Speeds, dead zone and smoothing can be changed in the Inspector. The defaults are deliberately slow and floaty to match the relaxed, no-pressure tone.

### Scripting hooks for the stages

```csharp
var hands = HandTrackingReceiver.Instance;
hands.HandFound      += h => { /* first interaction: e.g. start defining the garden background */ };
hands.GestureChanged += (h, g) => { if (g == HandGesture.Open) { /* ... */ } };
hands.StoryGestureStarted += (h, label) => { if (label == "SWIPE") { /* the fox appears */ } };
hands.StoryGestureEnded   += (h, label) => { /* e.g. stop the running animation */ };
HandInteractable.AnySelected += obj => { /* count interactions to move to the next stage */ };
// Freeze walking during a transition or the final video:
player.GetComponent<HandCameraController>().InputEnabled = false;
```

### Troubleshooting

- **The overlay stays red or yellow while Python is running:** check that both sides use the same port. Only one app can listen on a port, so close any second Unity instance. If Unity runs on another computer, pass `--host <that computer's IP>` and allow UDP 5052 through its firewall.
- **Could not open UDP port:** another program is already using 5052. Change the port on both sides.
- **A story gesture fires too easily or never:** tune the `TUNE ME` numbers in `gestures.py` and try them with `main.py` first.
- **Left and right feel reversed:** `unity_sender.py` mirrors the webcam like a selfie by default. If you run it with `--no-mirror`, tick **Mirror X** on the Receiver.
- **Jittery cursor:** raise **Smoothing** on the Receiver (0.6 → 0.75) and keep the hand well lit.
