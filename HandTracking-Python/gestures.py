"""
gestures.py

One class per movement from the "right column" of your table. Each detector:
  - is fed the current frame's FingerState + MotionTracker (see gesture_utils.py)
  - keeps a small amount of internal state (counters, previous frame data)
  - exposes `.update(fs, motion)` which returns a dict:
        {"active": bool, "label": str, "detail": str}

These are intentionally simple, rule-based (no training needed) so you can
run them immediately and then tune the thresholds once you see how your own
hand/webcam behaves. Search for "TUNE ME" for the numbers you'll most likely
want to adjust.

Every detector is deliberately independent and self-contained, so you can
copy just the one you need into your own script.
"""
import time
from gesture_utils import FingerState, MotionTracker


class ClaspOpenDetector:
    """Stage 1, gesture 1: repeated fist <-> open-hand, moving upward -> 'stars light up'."""

    def __init__(self, required_reps=3, window_seconds=4.0):
        self.required_reps = required_reps
        self.window_seconds = window_seconds
        self.was_fist = False
        self.rep_times = []

    def update(self, fs: FingerState, motion: MotionTracker):
        now = time.time()
        if self.was_fist and fs.is_open_palm:
            self.rep_times.append(now)
        self.was_fist = fs.is_fist

        # drop reps older than the window
        self.rep_times = [t for t in self.rep_times if now - t <= self.window_seconds]

        vy = motion.velocity()[1] if motion.ready else 0.0
        moving_up = vy < -0.15  # TUNE ME: normalized units/sec (y decreases upward in image coords)

        active = len(self.rep_times) >= self.required_reps and moving_up
        return {
            "active": active,
            "label": "CLASP_OPEN_UPWARD",
            "detail": f"reps={len(self.rep_times)}/{self.required_reps} moving_up={moving_up}",
        }


class PointAlternateDetector:
    """Stage 1, gesture 2: index-finger pointing, alternating left/right (follow footprints)."""

    def __init__(self, min_switches=2, window_seconds=3.0):
        self.min_switches = min_switches
        self.window_seconds = window_seconds
        self.last_side = None
        self.switch_times = []

    def update(self, fs: FingerState, motion: MotionTracker):
        now = time.time()
        if fs.is_pointing and motion.ready:
            x = motion.positions[-1][0]
            side = "left" if x < 0.5 else "right"  # TUNE ME: 0.5 = center of frame
            if self.last_side is not None and side != self.last_side:
                self.switch_times.append(now)
            self.last_side = side

        self.switch_times = [t for t in self.switch_times if now - t <= self.window_seconds]

        active = fs.is_pointing and len(self.switch_times) >= self.min_switches
        return {
            "active": active,
            "label": "POINT_ALTERNATE_LR",
            "detail": f"pointing={fs.is_pointing} switches={len(self.switch_times)}",
        }


class PointUpDetector:
    """Stage 3, gesture 1: point upward toward a lit star."""

    def update(self, fs: FingerState, motion: MotionTracker):
        vy = motion.velocity()[1] if motion.ready else 0.0
        moving_up = vy < -0.1  # TUNE ME
        active = fs.is_pointing and moving_up
        return {
            "active": active,
            "label": "POINT_UP",
            "detail": f"pointing={fs.is_pointing} vy={vy:.2f}",
        }


class PickingForwardDetector:
    """Stage 1, gesture 3 / Stage 2, gesture 5 (part 1): pinch (thumb+index) reaching forward."""

    def __init__(self, pinch_threshold=0.35, forward_z_delta=-0.02):
        self.pinch_threshold = pinch_threshold      # TUNE ME
        self.forward_z_delta = forward_z_delta      # TUNE ME: z gets more negative moving toward camera
        self.z_history = []

    def update(self, fs: FingerState, motion: MotionTracker):
        pinch = fs.pinch_distance() < self.pinch_threshold
        z = fs.hand[0][2]  # wrist z
        self.z_history.append(z)
        self.z_history = self.z_history[-10:]
        moving_forward = len(self.z_history) >= 2 and (self.z_history[-1] - self.z_history[0]) < self.forward_z_delta
        active = pinch and moving_forward
        return {
            "active": active,
            "label": "PICK_FORWARD",
            "detail": f"pinch_dist={fs.pinch_distance():.2f} pinch={pinch} forward={moving_forward}",
        }


class PickingLookDetector:
    """Stage 2, gesture 6: pinch, then hold still (as if examining the paper)."""

    def __init__(self, pinch_threshold=0.35, hold_seconds=0.8):
        self.pinch_threshold = pinch_threshold
        self.hold_seconds = hold_seconds
        self.pinch_start = None

    def update(self, fs: FingerState, motion: MotionTracker):
        pinch = fs.pinch_distance() < self.pinch_threshold
        now = time.time()
        if pinch:
            if self.pinch_start is None:
                self.pinch_start = now
        else:
            self.pinch_start = None

        held = self.pinch_start is not None and (now - self.pinch_start) >= self.hold_seconds
        still = motion.amplitude_x() < 0.05 and motion.amplitude_y() < 0.05 if motion.ready else False
        active = held and still
        return {
            "active": active,
            "label": "PICK_AND_LOOK",
            "detail": f"pinch={pinch} held_for={0 if not self.pinch_start else now - self.pinch_start:.1f}s still={still}",
        }


class WaveDetector:
    """
    Stage 2, gesture 1 (rapid) and gesture 3 (calm): open hand oscillating side to side.
    Distinguishes rapid vs calm by oscillation frequency (zero-crossings per second).
    """

    def __init__(self, rapid_crossings_per_sec=1.5):
        self.rapid_threshold = rapid_crossings_per_sec  # TUNE ME

        # TUNE ME
    def update(self, fs: FingerState, motion: MotionTracker):
        if not motion.ready or not fs.is_open_palm:
            return {"active": False, "label": "WAVE", "detail": "not open palm / not enough motion history"}

        crossings = motion.zero_crossings_x()
        duration = max(motion.duration(), 1e-6)
        rate = crossings / duration
        amplitude = motion.amplitude_x()

        is_waving = crossings >= 2 and amplitude > 0.05  # TUNE ME
        speed = "rapid" if rate >= self.rapid_threshold else "calm"

        return {
            "active": is_waving,
            "label": f"WAVE_{speed.upper()}",
            "detail": f"crossings={crossings} rate={rate:.2f}/s amplitude={amplitude:.2f}",
        }


class PatDetector:
    """Stage 2, gesture 3 (alternative): a short pat -- small vertical bounce, low horizontal travel."""

    def __init__(self, min_bounces=1):
        self.min_bounces = min_bounces

    def update(self, fs: FingerState, motion: MotionTracker):
        if not motion.ready:
            return {"active": False, "label": "PAT", "detail": "warming up"}
        vy_amp = motion.amplitude_y()
        x_amp = motion.amplitude_x()
        active = fs.is_open_palm and vy_amp > 0.03 and x_amp < 0.05  # TUNE ME
        return {
            "active": active,
            "label": "PAT_SHOULDER",
            "detail": f"y_amp={vy_amp:.2f} x_amp={x_amp:.2f}",
        }


class ClaspSwingDetector:
    """Stage 2, gesture 2 / Stage 3, gesture 2 (part 1): closed hand (fist/palm) swinging, like running arms."""

    def __init__(self, min_crossings=2):
        self.min_crossings = min_crossings

    def update(self, fs: FingerState, motion: MotionTracker):
        if not motion.ready:
            return {"active": False, "label": "CLASP_SWING", "detail": "warming up"}
        crossings = motion.zero_crossings_x()
        amplitude = motion.amplitude_x()
        # "just detect the palm part" -> we don't require a specific finger state here,
        # just that the palm/hand is present and swinging back and forth.
        active = crossings >= self.min_crossings and amplitude > 0.08  # TUNE ME
        return {
            "active": active,
            "label": "CLASP_SWING_RUN",
            "detail": f"crossings={crossings} amplitude={amplitude:.2f}",
        }


class SwipeDetector:
    """Stage 2, gesture 4: a single fast lateral swipe (checking the bush)."""

    def __init__(self, min_speed=0.8):
        self.min_speed = min_speed  # TUNE ME: normalized units/sec

    def update(self, fs: FingerState, motion: MotionTracker):
        if not motion.ready:
            return {"active": False, "label": "SWIPE", "detail": "warming up"}
        vx, _ = motion.velocity()
        crossings = motion.zero_crossings_x()
        # Fast + mostly one direction (few crossings) distinguishes a swipe from a wave.
        active = abs(vx) > self.min_speed and crossings <= 1
        return {
            "active": active,
            "label": "SWIPE",
            "detail": f"vx={vx:.2f} crossings={crossings}",
        }


class FoldPaperDetector:
    """
    Stage 2, gesture 7: folding paper -- simplified as a repeated pinch/release cycle
    with the hand roughly stationary (as opposed to PICK_FORWARD's reaching motion).
    This is the most "sequence-like" gesture; treat this as a starting point to refine
    once you decide exactly how many fold steps you want to recognize.
    """

    def __init__(self, required_cycles=3, window_seconds=6.0):
        self.required_cycles = required_cycles
        self.window_seconds = window_seconds
        self.was_pinching = False
        self.cycle_times = []

    def update(self, fs: FingerState, motion: MotionTracker):
        now = time.time()
        pinching = fs.pinch_distance() < 0.35  # TUNE ME
        if self.was_pinching and not pinching:
            self.cycle_times.append(now)  # counts a pinch->release as one "fold step"
        self.was_pinching = pinching

        self.cycle_times = [t for t in self.cycle_times if now - t <= self.window_seconds]
        still = motion.amplitude_x() < 0.1 and motion.amplitude_y() < 0.1 if motion.ready else True

        active = len(self.cycle_times) >= self.required_cycles and still
        return {
            "active": active,
            "label": "FOLD_PAPER",
            "detail": f"cycles={len(self.cycle_times)}/{self.required_cycles} still={still}",
        }


class TouchDetector:
    """Stage 3: quick forward 'touch' jab toward a target (e.g. touching a lit star / the rose)."""

    def __init__(self, forward_z_delta=-0.04, cooldown=0.6):
        self.forward_z_delta = forward_z_delta  # TUNE ME
        self.z_history = []
        self.last_trigger = 0.0
        self.cooldown = cooldown

    def update(self, fs: FingerState, motion: MotionTracker):
        z = fs.hand[0][2]
        self.z_history.append((time.time(), z))
        self.z_history = self.z_history[-8:]
        now = time.time()

        active = False
        if len(self.z_history) >= 2:
            dz = self.z_history[-1][1] - self.z_history[0][1]
            if dz < self.forward_z_delta and (now - self.last_trigger) > self.cooldown:
                active = True
                self.last_trigger = now

        return {
            "active": active,
            "label": "TOUCH",
            "detail": f"dz={(self.z_history[-1][1]-self.z_history[0][1]) if len(self.z_history)>=2 else 0:.3f}",
        }


# Registry used by main.py -- add new detectors here once you write them.
ALL_GESTURES = {
    "1": ("Clasp & open, moving up (Stage1: stars)", ClaspOpenDetector),
    "2": ("Point alternating L/R (Stage1: footprints)", PointAlternateDetector),
    "3": ("Pick forward (Stage1: roses / Stage2: paper)", PickingForwardDetector),
    "4": ("Wave rapid/calm (Stage2: children / switchman)", WaveDetector),
    "5": ("Pat on shoulder (Stage2: switchman alt.)", PatDetector),
    "6": ("Clasp & swing / running (Stage2: village / Stage3: rose)", ClaspSwingDetector),
    "7": ("Swipe (Stage2: bush/fox)", SwipeDetector),
    "8": ("Pick & look (Stage2: paper from fox)", PickingLookDetector),
    "9": ("Fold paper, repeated (Stage2: origami)", FoldPaperDetector),
    "10": ("Point upward (Stage3: stars)", PointUpDetector),
    "11": ("Touch (Stage3: stars/rose)", TouchDetector),
}
