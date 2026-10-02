using System.IO;
using LittlePrince.GuidedWalk;
using LittlePrince.HandTracking;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LittlePrince.Stages.Village.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Stage 2 - Village > Build Village Layout
    ///
    /// Lays out the stage 2 sketch (village with 4 children, then the fox's
    /// hide and seek through the trees, then meeting the fox) with placeholder
    /// shapes, the guided path and all its stops. Move the waypoints, kids and
    /// hiding spots onto your real models afterwards; the logic stays.
    /// </summary>
    public static class VillageLayoutBuilder
    {
        const string ScenePath = "Assets/LittlePrince/Scenes/Stages/03_Village.unity";
        const string MatFolder = "Assets/LittlePrince/Stages/03_Village/Materials/Placeholders";
        const string RootName = "Village Layout";
        const string RigName = "TEST RIG (delete once the Main scene drives this stage)";

        // The sketch is ~1400 x 500 px; 1 px = 5 cm, the path starts at the origin
        // and walks toward +X (sketch right), sketch "up" is +Z.
        const float Scale = 0.05f;
        static Vector3 P(float px, float py) => new Vector3((px - 90f) * Scale, 0f, -(py - 440f) * Scale);

        [MenuItem("Little Prince/Stage 2 - Village/Build Village Layout")]
        static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                int choice = EditorUtility.DisplayDialogComplex("Build Village Layout",
                    $"The open scene is not {Path.GetFileName(ScenePath)}.", "Open 03_Village", "Cancel", "Build here anyway");
                if (choice == 1) return;
                if (choice == 0)
                {
                    if (!File.Exists(ScenePath))
                    {
                        EditorUtility.DisplayDialog("Build Village Layout",
                            "03_Village.unity does not exist yet. Run Little Prince > Create Stage Scenes first.", "OK");
                        return;
                    }
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }
            }

            var old = GameObject.Find(RootName);
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("Build Village Layout",
                        $"\"{RootName}\" already exists in this scene. Replace it? (Your own objects outside it are kept.)",
                        "Replace", "Cancel"))
                    return;
                Undo.DestroyObjectImmediate(old);
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Village Layout");

            Prim(PrimitiveType.Plane, "Placeholder Ground", root.transform, new Vector3(31.5f, 0f, -0.5f),
                              new Vector3(7.5f, 1f, 3f), Mat("Ground", new Color(0.86f, 0.8f, 0.66f)), keepCollider: true);

            // ---- Village: houses and the four children ------------------------------------
            var houses = Child(root.transform, "Houses (placeholders)");
            var houseMat = Mat("House", new Color(0.96f, 0.9f, 0.82f));
            float[][] rects =
            {
                new float[] { 300, 470, 355, 525 }, new float[] { 370, 455, 455, 485 }, new float[] { 385, 500, 445, 525 },
                new float[] { 295, 622, 380, 662 }, new float[] { 425, 598, 445, 612 }, new float[] { 488, 624, 570, 668 },
            };
            for (int i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                var size = new Vector3((r[2] - r[0]) * Scale, 3f, (r[3] - r[1]) * Scale);
                var pos = P((r[0] + r[2]) / 2f, (r[1] + r[3]) / 2f) + Vector3.up * 1.5f;
                Prim(PrimitiveType.Cube, $"House {i + 1}", houses, pos, size, houseMat, keepCollider: true);
            }

            var kidsRoot = Child(root.transform, "Children (placeholders)");
            var kidMat = Mat("Child", new Color(0.55f, 0.7f, 0.95f));
            var kid1 = Kid("Child 1", kidsRoot, P(275, 525), kidMat);
            var kid2 = Kid("Child 2", kidsRoot, P(560, 465), kidMat);
            var kid3 = Kid("Child 3", kidsRoot, P(495, 595), kidMat);
            var kid4 = Kid("Child 4", kidsRoot, P(600, 615), kidMat);

            // ---- Fox section: trees, hiding spots, the fox ------------------------------
            var forest = Child(root.transform, "Fox Section");
            var leafMat = Mat("Tree", new Color(0.55f, 0.72f, 0.5f));
            var trunkMat = Mat("Trunk", new Color(0.55f, 0.42f, 0.32f));
            var bushMat = Mat("Bush", new Color(0.42f, 0.6f, 0.42f));
            foreach (var t in new[] { P(800, 395), P(860, 490), P(1160, 340), P(1050, 545), P(760, 600) })
                Tree(forest, "Tree", t, leafMat, trunkMat);

            var fox = Fox(forest, Mat("Fox", new Color(0.95f, 0.55f, 0.25f)));

            // ---- The path (red line of the sketch) ---------------------------------------
            var path = Child(root.transform, "Path - Village and fox").gameObject.AddComponent<GuidedPath>();
            var pt = path.transform;
            int n = 0;
            Wp(pt, ++n, 90, 440); Wp(pt, ++n, 100, 560); Wp(pt, ++n, 180, 585);
            KidStop(pt, ++n, "Child 1", P(275, 582), kid1);
            Wp(pt, ++n, 380, 578); Wp(pt, ++n, 410, 628); Wp(pt, ++n, 455, 635);
            KidStop(pt, ++n, "Child 3", P(467, 590), kid3);
            Wp(pt, ++n, 472, 505);
            KidStop(pt, ++n, "Child 2", P(530, 492), kid2);
            Wp(pt, ++n, 540, 560);
            KidStop(pt, ++n, "Child 4", P(600, 567), kid4);
            Wp(pt, ++n, 680, 552);
            // into the trees: the S-curve of hide and seek
            Wp(pt, ++n, 790, 480); Wp(pt, ++n, 890, 385); Wp(pt, ++n, 950, 290);
            var h1 = FoxStop(pt, ++n, 1, P(1020, 262));
            Wp(pt, ++n, 1075, 310); Wp(pt, ++n, 1065, 420);
            var h2 = FoxStop(pt, ++n, 2, P(990, 505));
            Wp(pt, ++n, 965, 580); Wp(pt, ++n, 1020, 608);
            var h3 = FoxStop(pt, ++n, 3, P(1110, 600));
            Wp(pt, ++n, 1165, 515); Wp(pt, ++n, 1210, 452);
            var meet = Stop(pt, ++n, "Stop - Meet the fox", P(1255, 445), 60f);

            // ---- Hide and seek -----------------------------------------------------------
            var game = Child(forest, "Hide and Seek").gameObject.AddComponent<FoxHideAndSeek>();
            game.fox = fox;
            game.spots = new[]
            {
                Spot(game.transform, 1, h1, P(985, 345), leafMat, trunkMat, bushMat),
                Spot(game.transform, 2, h2, P(1080, 470), leafMat, trunkMat, bushMat),
                Spot(game.transform, 3, h3, P(1195, 560), leafMat, trunkMat, bushMat),
            };
            var final = new GameObject("Final Spot - fox behind the bush").transform;
            final.SetParent(game.transform, false);
            final.position = P(1335, 445);
            final.rotation = Quaternion.LookRotation(meet.transform.position - final.position);
            game.finalSpot = final;

            // ---- Meeting the fox: swipe the bush, take the paper, fold the birds --------
            var encounter = Child(forest, "Fox Encounter");
            var bush = Prim(PrimitiveType.Sphere, "Bush (swipe it away)", encounter, P(1300, 445) + Vector3.up * 0.5f,
                            new Vector3(1.6f, 1.1f, 1.8f), bushMat);
            var paper = Prim(PrimitiveType.Cube, "Paper (placeholder)", encounter,
                             final.position + (meet.transform.position - final.position).normalized * 1.2f + Vector3.up * 1.2f,
                             new Vector3(0.3f, 0.01f, 0.21f), Mat("Paper", new Color(0.98f, 0.97f, 0.93f)));
            paper.transform.rotation = Quaternion.Euler(-60f, -90f, 0f); // tilted toward the visitor
            paper.SetActive(false);

            var birds = Child(encounter, "Origami Birds (fly off)");
            birds.position = final.position + Vector3.up * 1.4f;
            birds.rotation = Quaternion.LookRotation(Vector3.right); // the sketch's arrow: on to the next stage
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cube, $"Bird {i + 1}", birds, birds.position + new Vector3(i * 0.5f - 0.5f, i % 2 * 0.3f, i * 0.35f),
                     new Vector3(0.35f, 0.02f, 0.18f), paper.GetComponent<Renderer>().sharedMaterial);
            birds.gameObject.AddComponent<OrigamiBirdsFly>();
            birds.gameObject.SetActive(false);

            var meetGestures = meet.gameObject.AddComponent<GestureStop>();
            meet.lookAt = final;
            meetGestures.steps = new[]
            {
                new GestureStop.Step { hint = "Swipe the bush aside.", gestures = new[] { StoryGesture.Swipe } },
                new GestureStop.Step { hint = "Take the paper from the fox: pinch it and hold still.", gestures = new[] { StoryGesture.PickAndLook } },
                new GestureStop.Step { hint = "Fold the paper: pinch and release a few times.", gestures = new[] { StoryGesture.FoldPaper } },
            };
            UnityEventTools.AddBoolPersistentListener(meetGestures.steps[0].onDone, bush.SetActive, false);
            UnityEventTools.AddBoolPersistentListener(meetGestures.steps[1].onDone, paper.SetActive, true);
            UnityEventTools.AddBoolPersistentListener(meetGestures.steps[2].onDone, paper.SetActive, false);
            UnityEventTools.AddBoolPersistentListener(meetGestures.steps[2].onDone, birds.gameObject.SetActive, true);
            meetGestures.walkOnDelay = 4f; // watch the birds fly
            EditorUtility.SetDirty(meetGestures);

            path.Rebuild();

            // ---- Test rig: only when nothing else provides hand tracking + a player -----
            if (Object.FindObjectOfType<HandTrackingReceiver>() == null) TestRig(root.scene, path);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = path.gameObject;
            Debug.Log("[Village] Layout built. Save the scene, press Play, and move the waypoints / children / hiding spots " +
                      "onto your real models. Stops: 4 children (wave), 3 fox hiding spots (point at the fox), meeting the fox.");
        }

        // ------------------------------------------------------------------------------
        static void Wp(Transform path, int index, float px, float py)
        {
            var w = new GameObject($"{index:00} Waypoint").transform;
            w.SetParent(path, false);
            w.position = P(px, py);
        }

        static PathStop Stop(Transform path, int index, string name, Vector3 pos, float maxWait)
        {
            var stop = new GameObject($"{index:00} {name}").AddComponent<PathStop>();
            stop.transform.SetParent(path, false);
            stop.transform.position = pos;
            stop.waitFor = PathStop.WaitFor.Resume;
            stop.maxWaitSeconds = maxWait;
            return stop;
        }

        static PathStop KidStop(Transform path, int index, string kidName, Vector3 pos, Transform kid)
        {
            var stop = Stop(path, index, $"Stop - {kidName} (wave)", pos, 25f);
            stop.lookAt = kid;
            var g = stop.gameObject.AddComponent<GestureStop>();
            g.steps = new[] { new GestureStop.Step { hint = "Wave hello to the child!", gestures = new[] { StoryGesture.WaveAny } } };
            return stop;
        }

        static PathStop FoxStop(Transform path, int index, int spot, Vector3 pos) =>
            Stop(path, index, $"Stop - Hide and seek {spot}", pos, 30f);

        static FoxHideAndSeek.HidingSpot Spot(Transform parent, int index, PathStop stop, Vector3 pos,
                                              Material leaf, Material trunk, Material bush)
        {
            var spot = new GameObject($"Hiding Spot {index}").transform;
            spot.SetParent(parent, false);
            spot.position = pos;
            Vector3 toVisitor = stop.transform.position - pos;
            toVisitor.y = 0f;
            spot.rotation = Quaternion.LookRotation(toVisitor);
            stop.lookAt = spot;

            // The fox peeks out from beside this tree; it waits behind the bush at its foot.
            Tree(spot, "Tree (hiding place)", spot.TransformPoint(new Vector3(0.9f, 0f, -0.5f)), leaf, trunk);
            Prim(PrimitiveType.Sphere, "Bush", spot, spot.TransformPoint(new Vector3(0.9f, 0.45f, -0.6f)),
                 new Vector3(1.5f, 0.9f, 1.3f), bush);
            return new FoxHideAndSeek.HidingSpot { stop = stop, spot = spot, hiddenOffset = new Vector3(0.9f, 0f, -1.5f) };
        }

        static Transform Kid(string name, Transform parent, Vector3 pos, Material mat)
        {
            var kid = Prim(PrimitiveType.Capsule, name, parent, pos + Vector3.up * 0.6f, new Vector3(0.45f, 0.6f, 0.45f), mat);
            return kid.transform;
        }

        static HandInteractable Fox(Transform parent, Material mat)
        {
            var fox = new GameObject("Fox (placeholder)");
            fox.transform.SetParent(parent, false);
            Prim(PrimitiveType.Cube, "Body", fox.transform, Vector3.up * 0.3f, new Vector3(0.3f, 0.3f, 0.65f), mat, local: true);
            Prim(PrimitiveType.Cube, "Head", fox.transform, new Vector3(0f, 0.5f, 0.38f), new Vector3(0.28f, 0.25f, 0.25f), mat, local: true);
            Prim(PrimitiveType.Cube, "Ear L", fox.transform, new Vector3(-0.08f, 0.68f, 0.38f), new Vector3(0.07f, 0.12f, 0.04f), mat, local: true);
            Prim(PrimitiveType.Cube, "Ear R", fox.transform, new Vector3(0.08f, 0.68f, 0.38f), new Vector3(0.07f, 0.12f, 0.04f), mat, local: true);
            var tail = Prim(PrimitiveType.Cube, "Tail", fox.transform, new Vector3(0f, 0.35f, -0.48f), new Vector3(0.14f, 0.14f, 0.4f), mat, local: true);
            tail.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);

            // Generous, easy to point at.
            var col = fox.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.4f, 0f);
            col.size = new Vector3(0.9f, 0.9f, 1.2f);
            var interactable = fox.AddComponent<HandInteractable>();
            interactable.interactionId = "fox";
            return interactable;
        }

        static void Tree(Transform parent, string name, Vector3 pos, Material leaf, Material trunk)
        {
            var tree = new GameObject(name).transform;
            tree.SetParent(parent, false);
            tree.position = pos;
            Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0f, 1f, 0f), new Vector3(0.35f, 1f, 0.35f), trunk, local: true, keepCollider: true);
            Prim(PrimitiveType.Sphere, "Leaves", tree, new Vector3(0f, 2.6f, 0f), new Vector3(2.2f, 2f, 2.2f), leaf, local: true);
        }

        static void TestRig(Scene scene, GuidedPath path)
        {
            var rig = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(rig, "Village test rig");

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(rig.transform, false);
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            if (Object.FindObjectsOfType<Light>().Length > 1) sun.gameObject.SetActive(false); // the scene has its own light

            var player = new GameObject("Player");
            player.transform.SetParent(rig.transform, false);
            player.transform.position = path.transform.GetChild(0).position;
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            foreach (var other in Object.FindObjectsOfType<Camera>())
                if (other != cam && other.gameObject.scene == scene) other.gameObject.SetActive(false);

            var walker = player.AddComponent<GuidedPathWalker>();
            walker.path = path;
            walker.startOnPlay = true;
            walker.pitchPivot = camGo.transform;
            player.AddComponent<RunGestureBoost>().walker = walker;

            var tracking = new GameObject("Hand Tracking");
            tracking.transform.SetParent(rig.transform, false);
            tracking.AddComponent<HandTrackingReceiver>();
            tracking.AddComponent<HandDebugVisualizer>();
            tracking.AddComponent<PythonLauncher>();
            tracking.AddComponent<HandCursor>().targetCamera = cam;
        }

        // ---- small helpers -----------------------------------------------------------
        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
                               bool local = false, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            if (local) go.transform.localPosition = pos; else go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Material Mat(string name, Color color)
        {
            string path = $"{MatFolder}/M_{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(MatFolder);
            Shader shader = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("HDRP/Lit");
            }
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = "M_" + name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
