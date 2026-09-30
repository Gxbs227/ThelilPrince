using LittlePrince.HandTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LittlePrince.Story.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Add Story Flow (from flowchart)
    /// Adds a StoryFlowManager to the open scene, filled in with every step of
    /// docs/Experience_Flow_Chart.pdf, plus placeholder stage roots, start
    /// points and destination areas to replace with the real environments.
    /// </summary>
    public static class StoryFlowBuilder
    {
        const float StageSpacing = 200f; // stages are laid out 200 m apart on X

        [MenuItem("Little Prince/Add Story Flow (from flowchart)")]
        public static void Build()
        {
            var existing = Object.FindObjectOfType<StoryFlowManager>();
            if (existing != null &&
                !EditorUtility.DisplayDialog("Story Flow", "This scene already has a StoryFlowManager. Add another one?", "Add", "Cancel"))
                return;

            var root = new GameObject("Story Flow");
            Undo.RegisterCreatedObjectUndo(root, "Add Story Flow");
            var flow = root.AddComponent<StoryFlowManager>();
            flow.player = Object.FindObjectOfType<HandCameraController>();

            if (Object.FindObjectOfType<HandTrackingReceiver>() == null)
            {
                root.AddComponent<HandTrackingReceiver>();
                root.AddComponent<HandDebugVisualizer>();
            }

            // ---- Stage roots, start points and destination areas ------------------------
            var stages = new[] { StoryStage.Intro, StoryStage.Garden, StoryStage.Village, StoryStage.Space, StoryStage.Final };
            var stageRoots = new Transform[stages.Length];
            var starts = new Transform[stages.Length];
            for (int i = 0; i < stages.Length; i++)
            {
                var s = new GameObject($"Stage - {stages[i]}").transform;
                s.SetParent(root.transform, false);
                s.localPosition = new Vector3(i * StageSpacing, 0f, 0f);
                stageRoots[i] = s;
                flow.stageRoots.Add(new StageRoot { stage = stages[i], root = s.gameObject });

                starts[i] = new GameObject($"Player Start - {stages[i]}").transform;
                starts[i].SetParent(s, false);
                starts[i].localPosition = new Vector3(0f, 0.05f, -8f);
            }

            Collider gate = Area(stageRoots[0], "Area - Gate to the garden", new Vector3(0f, 1.5f, 20f));
            Collider cliff = Area(stageRoots[1], "Area - Cliff", new Vector3(0f, 1.5f, 25f));

            // ---- The flowchart -------------------------------------------------------
            // Intro
            Add(flow, StoryStage.Intro, "Black space", StepType.Timed, duration: 4f, walk: false, start: starts[0]);
            Add(flow, StoryStage.Intro, "Sky becomes full of stars", StepType.Gesture, walk: false,
                gestures: new[] { StoryGesture.ClaspOpenUpward },
                hint: "Open and close your hand as you raise it toward the sky…");
            var footprints = Add(flow, StoryStage.Intro, "Follow the footprints in the desert", StepType.Gesture, walk: false,
                gestures: new[] { StoryGesture.PointAlternateLR },
                hint: "Point left, then right, to follow the footprints.");
            footprints.incorrectText = "Try again: point left and right.";
            Add(flow, StoryStage.Intro, "Get to the gate for stage 1", StepType.ReachArea, area: gate,
                hint: "Open your hand to walk toward the gate.", autoAdvance: 60f);

            // Garden
            Add(flow, StoryStage.Garden, "Interact with the roses that light up", StepType.Gesture, start: starts[1],
                gestures: new[] { StoryGesture.PickForward }, repetitions: 3,
                hint: "Reach forward and pick the glowing roses.");
            Add(flow, StoryStage.Garden, "Lead to the cliff for second stage", StepType.ReachArea, area: cliff,
                hint: "Follow the path to the cliff.", autoAdvance: 60f);

            // Village
            Add(flow, StoryStage.Village, "Interaction with some children", StepType.Gesture, start: starts[2],
                gestures: new[] { StoryGesture.WaveRapid },
                hint: "Wave hello to the children!");
            Add(flow, StoryStage.Village, "Navigate through the village to train station", StepType.Gesture,
                gestures: new[] { StoryGesture.ClaspSwingRun },
                hint: "Close your hand and swing it, as if running.");
            Add(flow, StoryStage.Village, "Interaction with the switchman", StepType.Gesture,
                gestures: new[] { StoryGesture.WaveCalm, StoryGesture.PatShoulder },
                hint: "Wave calmly, or give the switchman a pat on the shoulder.");
            Add(flow, StoryStage.Village, "Walk to the bush and interact with the fox", StepType.Gesture,
                gestures: new[] { StoryGesture.Swipe },
                hint: "Swipe the bush aside.");
            Add(flow, StoryStage.Village, "Take the piece of paper from the fox", StepType.Gesture,
                gestures: new[] { StoryGesture.PickAndLook },
                hint: "Pinch the paper and hold it still to look at it.");
            Add(flow, StoryStage.Village, "Fold the paper into origami birds", StepType.Gesture,
                gestures: new[] { StoryGesture.FoldPaper },
                hint: "Pinch and release a few times to fold the paper.");
            Add(flow, StoryStage.Village, "Fly to the third stage", StepType.Timed, duration: 6f, walk: false);

            // Space
            Add(flow, StoryStage.Space, "Animation of going through the different planets", StepType.Timed,
                duration: 10f, walk: false, start: starts[3]);
            Add(flow, StoryStage.Space, "Touch the stars that light up", StepType.Gesture, walk: false,
                gestures: new[] { StoryGesture.PointUp }, repetitions: 3,
                hint: "Point up toward the glowing stars.");
            Add(flow, StoryStage.Space, "Follow the sound and interact with the rose", StepType.Gesture,
                gestures: new[] { StoryGesture.ClaspSwingRun },
                hint: "Follow the sound… swing your closed hand to reach the rose.");

            // Final
            Add(flow, StoryStage.Final, "Third person view of the rose, character and sunset", StepType.Video,
                duration: 15f, walk: false, start: starts[4]);
            var quote = Add(flow, StoryStage.Final, "Quote", StepType.Timed, duration: 10f, walk: false,
                hint: "Don't think too much, eventually you will find a way.");
            quote.hintAfterSeconds = 0.5f; // the quote is shown through the hint line
            // …then StoryFlowManager.loop sends everyone back to the start.

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Story] Added {flow.steps.Count} steps from the flowchart. " +
                      "Put each environment under its 'Stage - …' object, move the Player Start points and Areas, " +
                      "and assign the VideoPlayer on the final step. Press N in Play mode to skip a step.");
        }

        static StoryStep Add(StoryFlowManager flow, StoryStage stage, string title, StepType type,
                             StoryGesture[] gestures = null, int repetitions = 1, Collider area = null,
                             float duration = 5f, bool walk = true, Transform start = null,
                             string hint = "", float autoAdvance = 45f)
        {
            var step = new StoryStep
            {
                title = title,
                stage = stage,
                type = type,
                acceptedGestures = gestures ?? new StoryGesture[0],
                repetitions = repetitions,
                area = area,
                duration = duration,
                allowWalking = walk,
                teleportPlayerTo = start,
                hintText = hint,
                hintAfterSeconds = string.IsNullOrEmpty(hint) ? 0f : 12f,
                // Timed and video steps end on their own, so they don't need a fallback.
                autoAdvanceAfterSeconds = type == StepType.Timed || type == StepType.Video ? 0f : autoAdvance,
            };
            flow.steps.Add(step);
            return step;
        }

        static Collider Area(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(6f, 3f, 4f);
            return box;
        }
    }
}
