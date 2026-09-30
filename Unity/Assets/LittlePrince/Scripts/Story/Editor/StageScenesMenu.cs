using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LittlePrince.Story.EditorTools
{
    /// <summary>
    /// Menu: Little Prince > Create Stage Scenes
    /// Creates the team's scene layout (only the scenes that don't exist yet) and
    /// puts them in File > Build Settings in the right order:
    ///
    ///   Scenes/Main.unity                  shared: hand tracking, player, story flow, UI
    ///   Scenes/Stages/01_Desert.unity …    one scene per stage, one owner each
    /// </summary>
    public static class StageScenesMenu
    {
        const string ScenesFolder = "Assets/LittlePrince/Scenes";
        const string MainScene = ScenesFolder + "/Main.unity";

        public static readonly string[] StageScenes =
        {
            ScenesFolder + "/Stages/01_Desert.unity",
            ScenesFolder + "/Stages/02_Garden.unity",
            ScenesFolder + "/Stages/03_Village.unity",
            ScenesFolder + "/Stages/04_Space.unity",
            ScenesFolder + "/Stages/05_Final.unity",
        };

        [MenuItem("Little Prince/Create Stage Scenes")]
        static void CreateStageScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string current = SceneManager.GetActiveScene().path;
            var created = new List<string>();

            if (!File.Exists(MainScene))
            {
                var main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("--- Main: hand tracking, player, story flow, UI (no environment art here) ---");
                EditorSceneManager.SaveScene(main, MainScene);
                created.Add(MainScene);
            }

            foreach (var path in StageScenes)
            {
                if (File.Exists(path)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                // Stage scenes bring their own light/sky; the player camera lives in Main.
                foreach (var cam in Object.FindObjectsOfType<Camera>()) Object.DestroyImmediate(cam.gameObject);
                new GameObject($"Stage - {Path.GetFileNameWithoutExtension(path)}");
                EditorSceneManager.SaveScene(scene, path);
                created.Add(path);
            }

            // Build Settings: Main first, then the stages in order, then anything else already there.
            var ordered = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MainScene, true) };
            ordered.AddRange(StageScenes.Select(p => new EditorBuildSettingsScene(p, true)));
            foreach (var s in EditorBuildSettings.scenes)
                if (ordered.All(o => o.path != s.path)) ordered.Add(s);
            EditorBuildSettings.scenes = ordered.ToArray();

            if (!string.IsNullOrEmpty(current) && File.Exists(current)) EditorSceneManager.OpenScene(current);
            AssetDatabase.Refresh();
            Debug.Log(created.Count == 0
                ? "[Story] All stage scenes already exist. Build Settings updated."
                : "[Story] Created: " + string.Join(", ", created.Select(Path.GetFileName)) + ". Build Settings updated.");
        }
    }
}
