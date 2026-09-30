using UnityEditor;
using UnityEngine;

namespace LittlePrince.HandTracking.EditorTools
{
    /// <summary>
    /// Menu items to tell Unity where HandTracking-Python is on THIS computer.
    /// The choice is saved per computer (PlayerPrefs), not in the scene, so every
    /// teammate can keep the folder wherever they like.
    /// </summary>
    public static class PythonFolderMenu
    {
        [MenuItem("Little Prince/Set Python Folder…")]
        static void SetFolder()
        {
            string current = PythonLauncher.FindPythonFolder() ?? Application.dataPath;
            string folder = EditorUtility.OpenFolderPanel("Choose the HandTracking-Python folder", current, "");
            if (string.IsNullOrEmpty(folder)) return;

            if (!PythonLauncher.IsPythonFolder(folder))
            {
                EditorUtility.DisplayDialog("Python folder",
                    "That folder doesn't contain unity_sender.py.\nChoose the HandTracking-Python folder.", "OK");
                return;
            }
            PlayerPrefs.SetString(PythonLauncher.FolderPrefKey, folder);
            PlayerPrefs.Save();
            Debug.Log("[Python] Folder set for this computer: " + folder);
        }

        [MenuItem("Little Prince/Show Python Folder")]
        static void ShowFolder()
        {
            string folder = PythonLauncher.FindPythonFolder();
            if (folder == null)
            {
                if (EditorUtility.DisplayDialog("Python folder",
                        "HandTracking-Python was not found automatically on this computer.", "Choose it…", "Cancel"))
                    SetFolder();
                return;
            }
            Debug.Log("[Python] Using: " + folder);
            EditorUtility.RevealInFinder(System.IO.Path.Combine(folder, "unity_sender.py"));
        }

        [MenuItem("Little Prince/Add Python Launcher to Scene")]
        static void AddLauncher()
        {
            if (Object.FindObjectOfType<PythonLauncher>() != null)
            {
                Debug.Log("[Python] This scene already has a PythonLauncher.");
                return;
            }
            var receiver = Object.FindObjectOfType<HandTrackingReceiver>();
            GameObject target = receiver != null ? receiver.gameObject : new GameObject("Hand Tracking");
            if (receiver == null)
            {
                Undo.RegisterCreatedObjectUndo(target, "Add Python Launcher");
                target.AddComponent<HandTrackingReceiver>();
                target.AddComponent<HandDebugVisualizer>();
            }
            Undo.AddComponent<PythonLauncher>(target);
            Selection.activeGameObject = target;
        }
    }
}
