# ThelilPrince
IXD 2 project: a soft low-poly walk-through based on *The Little Prince*, controlled with hand movements seen by a webcam.

> "Don't think too much, eventually you will find a way."

Three stages (garden → village → outer space), 5–8 minutes, looping back to the start after the final quote and animation. There is no goal to reach: the journey is the point.

---

## Project structure and teamwork

The Unity project is the `Unity/` folder. Stages are split into one scene each (`Main` plus `01_Desert` … `05_Final`), with one owner per scene. Folders exist for models, characters, textures, audio, UI and cinematics. **Read [`docs/PROJECT_STRUCTURE.md`](docs/PROJECT_STRUCTURE.md) before adding assets**: it has the folder map, who edits what, and the one-time Git and Unity setup.

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

1. Open the repo's `Unity/` folder as the Unity project (see [`docs/PROJECT_STRUCTURE.md`](docs/PROJECT_STRUCTURE.md)). Alternatively, copy `Unity/Assets/LittlePrince` **together with its `.meta` files** into your project's `Assets/`. The scripts were compile-checked against Unity 2021.3 and work with the Built-in pipeline, URP and HDRP, and with either the old or new Input System.
2. In the Unity menu choose **Little Prince → Create Hand Tracking Test Scene**. This creates `Assets/LittlePrince/Scenes/Sandbox/HandTrackingTest.unity`: a pastel garden with pinchable flowers and a rose.
3. Press **Play**, then start `unity_sender.py` (or `mock_sender.py`).
4. The top-left overlay (toggle with **F1**) should turn green (**● MediaPipe connected**) and show the pose, the active story gestures and a live hand skeleton.
5. Pinch a flower to make it bloom. The rose only blooms with the story gesture **pinch and hold still** (`PICK_AND_LOOK`) while you point at it.

With no Python running, the **mouse acts as the hand**: move to aim, hold left for pinch, hold right for open palm (walk), and hold both for fist. WASD or the arrow keys also move the camera.

**Python starts by itself.** The `PythonLauncher` component (already in the test scene and the story flow) starts `unity_sender.py` when you press Play and closes it when you press Stop. Python's messages and errors appear in Unity's Console with the prefix `[Python]`.
- **Finding the folder:** it looks for `HandTracking-Python` inside, next to, or one level around the Unity project. For a build, it looks next to the `.exe`.
- **If it isn't found:** use **Little Prince → Set Python Folder…** once. The choice is saved on that computer only, so each teammate can keep the folder wherever they like.
- **Which Python:** it uses the folder's `.venv`. Create it once with `python -m venv .venv` and `.venv\Scripts\python.exe -m pip install -r requirements.txt`.
- **Already running by hand:** if Python is already running, it doesn't start a second one.
- **Inspector options:** camera index, 1 or 2 hands, show or hide the webcam window, and whether to launch in builds (for the exhibition PC).

To add hand tracking to your own scenes, add these components:

- One `HandTrackingReceiver` in the scene (add `HandDebugVisualizer` next to it while testing, and `PythonLauncher` to start Python automatically: **Little Prince → Add Python Launcher to Scene**).
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

### Guided walk (the camera moves by itself)

To keep the experience free of pressure, the visitor doesn't have to steer. `GuidedPathWalker` (on the Player) moves the camera slowly along a path and pauses at points of interest. The hand only **looks around, aims and touches**:

| Hand | Guided walk |
|---|---|
| move hand left / right / up / down | turn your head a little (up to 35° / 18°) |
| ☝️ Point | aim the cursor at things |
| 🤏 Pinch | touch what the cursor is on; the view holds still |
| ✊ Fist | pause the walk; open the hand to continue |

- **The path:** an object with `GuidedPath`. Its **child objects are the waypoints**, in Hierarchy order, and the curve is drawn in the Scene view. Place the waypoints on the ground; tick **Loop** for a circuit.
- **Pauses:** add `PathStop` to a waypoint to pause there. The walk continues after:
  - **Seconds**: a few seconds (a nice view);
  - **Interaction**: touching the listed objects;
  - **StoryStep**: when the story reaches step #N (the number in the overlay);
  - **Resume**: a call from your own scripts.

  **Max Wait Seconds** always continues the walk eventually, and **Look At** turns the view toward something while waiting.
- **Settings:** speed, softness, how far the head turns, and ground snapping are all on the `GuidedPathWalker`. While it's enabled, `HandCameraController` is switched off; disable the walker to walk freely again.
- **Where it's already set up:**
  - The **test scene** has a slow loop around the garden that pauses at three flowers until you pinch one (or after 20 s).
  - **Add Story Flow (from flowchart)** creates a path for the desert, garden and village. The stops are tied to the story: roses → step 6, children → step 8, station and switchman → step 10, bush and fox → step 13. Each stage's first step starts its path (`Follow Path`). Steps with *Allow Walking* off hold the walk.

### Story flow (from the flowchart)

[`docs/Experience_Flow_Chart.pdf`](docs/Experience_Flow_Chart.pdf) is implemented by `StoryFlowManager` (`Unity/Assets/LittlePrince/Scripts/Story/`). In any scene choose **Little Prince → Add Story Flow (from flowchart)**. It adds all 18 steps below, plus these placeholders for your real environments:
- a **Stage - Intro / Garden / Village / Space / Final** object for each stage; put each environment under its object, and only the current stage is shown;
- **Player Start** points;
- **Area** triggers for the gate and the cliff.

| # | Stage | Step | Completes when |
|---|---|---|---|
| 1 | Intro | Black space | 4 s |
| 2 | Intro | Sky becomes full of stars | `CLASP_OPEN_UPWARD` |
| 3 | Intro | Follow the footprints in the desert | `POINT_ALTERNATE_LR` (another gesture shows *"Try again"*) |
| 4 | Intro | Get to the gate for stage 1 | player walks into *Area - Gate* |
| 5 | Garden | Interact with the roses that light up | `PICK_FORWARD` ×3 |
| 6 | Garden | Lead to the cliff for second stage | player walks into *Area - Cliff* |
| 7 | Village | Interaction with some children | `WAVE_RAPID` |
| 8 | Village | Navigate through the village to train station | `CLASP_SWING_RUN` |
| 9 | Village | Interaction with the switchman | `WAVE_CALM` or `PAT_SHOULDER` |
| 10 | Village | Walk to the bush and interact with the fox | `SWIPE` |
| 11 | Village | Take the piece of paper from the fox | `PICK_AND_LOOK` |
| 12 | Village | Fold the paper into origami birds | `FOLD_PAPER` |
| 13 | Village | Fly to the third stage | 6 s animation |
| 14 | Space | Going through the different planets | 10 s animation |
| 15 | Space | Touch the stars that light up | `POINT_UP` ×3 |
| 16 | Space | Follow the sound and interact with the rose | `CLASP_SWING_RUN` |
| 17 | Final | Third person view of the rose, character and sunset | video ends (or 15 s) |
| 18 | Final | Quote: *"Don't think too much, eventually you will find a way."* | 10 s, then **back to the start** |

Each step has the following settings in the Inspector:
- **Events** (UnityEvents) for your animations, sounds and UI:
  - `onEnter`: the step starts;
  - `onProgress`: one repetition done, e.g. one rose lit;
  - `onHint`: the gentle hint appears;
  - `onIncorrect`: a different gesture was made;
  - `onComplete`: the step is done.
- **Pacing:**
  - a gentle hint text appears after 12 s;
  - gesture and area steps **move on by themselves** after 45–60 s. Nobody gets stuck, because there's no winning or losing.
- **Allow Walking:** whether the camera can move during the step.
- **Teleport Player To:** where the player is placed when the step starts.
- **Must Point At:** the cursor has to be on a given object during the gesture.

For testing, press **N** to skip a step and **F2** to hide the step overlay. You can also right-click the component and choose **Skip current step**. From code, listen to `StepStarted`, `StageChanged`, `HintChanged` and `Looped`, or call `CompleteCurrentStep()` for `Manual` steps.

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
