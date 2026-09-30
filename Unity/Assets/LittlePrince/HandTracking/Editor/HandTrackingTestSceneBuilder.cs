using System.IO;
using LittlePrince.HandTracking.Demo;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittlePrince.HandTracking.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Create Hand Tracking Test Scene
    /// Builds a small pastel "garden" with a hand-driven walk-through camera,
    /// the hand cursor, the debug overlay and a few flowers you can pinch.
    /// </summary>
    public static class HandTrackingTestSceneBuilder
    {
        const string Folder = "Assets/LittlePrince/Scenes";
        const string ScenePath = Folder + "/HandTrackingTest.unity";

        [MenuItem("Little Prince/Create Hand Tracking Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Hand Tracking Test Scene",
                    $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            Directory.CreateDirectory(Folder + "/TestMaterials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- Atmosphere: soft, pastel, a little foggy ---------------------------
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.85f, 0.88f, 1f);
            RenderSettings.ambientEquatorColor = new Color(1f, 0.87f, 0.8f);
            RenderSettings.ambientGroundColor = new Color(0.55f, 0.6f, 0.5f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(1f, 0.9f, 0.85f);
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 45f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            // ---- Ground ------------------------------------------------------------------
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Garden Ground";
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
            ground.GetComponent<Renderer>().sharedMaterial = Mat("Ground", new Color(0.62f, 0.78f, 0.58f));

            // ---- Player + camera -----------------------------------------------------
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.05f, -8f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.6f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.8f, 0f);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            var walker = player.AddComponent<HandCameraController>();
            walker.pitchPivot = camGo.transform;
            walker.maxDistanceFromStart = 20f;

            // ---- Hand tracking -------------------------------------------------------
            var tracking = new GameObject("Hand Tracking");
            tracking.AddComponent<HandTrackingReceiver>();
            tracking.AddComponent<HandDebugVisualizer>();
            tracking.AddComponent<PythonLauncher>();
            var cursor = tracking.AddComponent<HandCursor>();
            cursor.targetCamera = cam;

            // ---- Pinchable flowers ---------------------------------------------------
            var stemMat = Mat("Stem", new Color(0.4f, 0.62f, 0.42f));
            var headMat = Mat("FlowerHead", new Color(0.55f, 0.75f, 0.55f));
            Color[] blooms =
            {
                new Color(1f, 0.55f, 0.6f), new Color(1f, 0.8f, 0.45f), new Color(0.7f, 0.6f, 1f),
                new Color(1f, 0.65f, 0.4f), new Color(0.55f, 0.8f, 1f), new Color(1f, 0.5f, 0.75f),
            };
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                float radius = 3f + (i % 3) * 1.6f;
                var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                CreateFlower($"Flower {i + 1}", pos, stemMat, headMat, blooms[i % blooms.Length]);
            }

            // A "rose under its glass dome" in the middle, as a larger target.
            var rose = CreateFlower("The Rose", Vector3.zero, stemMat, headMat, new Color(0.95f, 0.25f, 0.35f));
            rose.transform.localScale = Vector3.one * 1.4f;

            // Story gesture example: pinch and hold still while pointing at the rose
            // (PICK_AND_LOOK from gestures.py) makes it bloom. Normal pinch-to-select is
            // turned off on the rose so only the story gesture opens it.
            var roseFlower = rose.GetComponent<DemoFlower>();
            roseFlower.toggleOnSelect = false;
            var story = rose.AddComponent<StoryGestureTrigger>();
            story.gesture = StoryGesture.PickAndLook;
            story.requireHover = true;
            story.hoverTarget = rose.GetComponent<HandInteractable>();
            UnityEventTools.AddPersistentListener(story.onDetected, roseFlower.Toggle);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Selection.activeGameObject = tracking;
            Debug.Log($"[HandTracking] Test scene created at {ScenePath}. Press Play, Python starts by itself (or use the mouse).");
        }

        static GameObject CreateFlower(string name, Vector3 pos, Material stemMat, Material headMat, Color bloom)
        {
            var root = new GameObject(name);
            root.transform.position = pos;

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            Object.DestroyImmediate(stem.GetComponent<Collider>());
            stem.transform.SetParent(root.transform, false);
            stem.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            stem.transform.localScale = new Vector3(0.06f, 0.5f, 0.06f);
            stem.GetComponent<Renderer>().sharedMaterial = stemMat;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            head.transform.localScale = new Vector3(0.3f, 0.22f, 0.3f);
            head.GetComponent<Renderer>().sharedMaterial = headMat;

            // One generous collider on the root so the flower is easy to point at.
            var col = root.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.radius = 0.4f;
            col.isTrigger = true;

            var interactable = root.AddComponent<HandInteractable>();
            interactable.interactionId = name;
            var flower = root.AddComponent<DemoFlower>();
            flower.head = head.GetComponent<Renderer>();
            flower.bloomColor = bloom;
            return root;
        }

        static Material Mat(string name, Color color)
        {
            string path = $"{Folder}/TestMaterials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("HDRP/Lit");
            }
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f); // soft, matte
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
