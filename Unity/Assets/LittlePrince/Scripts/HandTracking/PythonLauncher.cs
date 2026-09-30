using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LittlePrince.HandTracking
{
    /// <summary>
    /// Starts HandTracking-Python/unity_sender.py when Play starts and closes it on Stop,
    /// so nobody has to open a terminal.
    ///
    /// Finding the Python folder on each computer, in this order:
    ///   1. The folder chosen with  Little Prince > Set Python Folder…  (saved per computer)
    ///   2. A "HandTracking-Python" folder inside, next to, or one level around the Unity project
    ///      (for a build: next to the .exe).
    /// It uses the folder's .venv if there is one, otherwise "python" from the PATH.
    /// If Python is already running by hand, nothing extra is started.
    /// </summary>
    public class PythonLauncher : MonoBehaviour
    {
        public const string FolderPrefKey = "LittlePrince.PythonFolder";
        const string ScriptName = "unity_sender.py";
        const string FolderName = "HandTracking-Python";

        [Tooltip("Start Python automatically when Play starts.")]
        public bool launchOnPlay = true;
        [Tooltip("Also start it in a built game (for the exhibition PC).")]
        public bool launchInBuilds = true;
        [Tooltip("Show the OpenCV window with the webcam and skeleton.")]
        public bool showCameraPreview = true;
        [Tooltip("Webcam index. -1 = let the script try cameras 0, 1, 2.")]
        public int cameraIndex = -1;
        [Tooltip("1 or 2 hands.")]
        [Range(1, 2)] public int hands = 2;
        [Tooltip("Extra command line arguments for unity_sender.py.")]
        public string extraArguments = "";

        public string Status { get; private set; } = "not started";
        public bool IsRunning => _process != null && !_process.HasExited;

        Process _process;
        readonly StringBuilder _errors = new StringBuilder();

        IEnumerator Start()
        {
            if (!launchOnPlay || (!Application.isEditor && !launchInBuilds))
            {
                Status = "disabled";
                Debug.Log("[Python] PythonLauncher is disabled (Launch On Play is off), not starting Python.");
                yield break;
            }

            // If Python is already running (started by hand), don't start a second one.
            yield return new WaitForSecondsRealtime(0.6f);
            var receiver = HandTrackingReceiver.Instance;
            if (receiver != null && receiver.IsConnected)
            {
                Status = "already running (started by hand)";
                Debug.Log("[Python] Hand data is already arriving, so not starting another unity_sender.py.");
                yield break;
            }

            Launch(receiver != null ? receiver.port : 5052);
        }

        void Launch(int port)
        {
            string folder = FindPythonFolder();
            if (folder == null)
            {
                Status = "HandTracking-Python folder not found";
                Debug.LogError("[Python] Could not find the HandTracking-Python folder (with unity_sender.py). " +
                               "Use the menu  Little Prince > Set Python Folder…  to choose it on this computer.");
                return;
            }

            string assets = Path.GetFullPath(Application.dataPath).TrimEnd('\\', '/');
            if (Application.isEditor && Path.GetFullPath(folder).StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning("[Python] The HandTracking-Python folder is inside Assets/. Unity will try to import " +
                                 "the thousands of files in its .venv, which makes the Editor slow and can cause errors. " +
                                 "Move it next to Assets/ (into the project folder) and recreate the .venv there.");

            string python = FindPythonExecutable(folder);
            var args = new StringBuilder($"-u \"{ScriptName}\" --port {port} --hands {hands}");
            if (cameraIndex >= 0) args.Append($" --camera {cameraIndex}");
            if (!showCameraPreview) args.Append(" --no-preview");
            if (!string.IsNullOrWhiteSpace(extraArguments)) args.Append(' ').Append(extraArguments.Trim());

            var info = new ProcessStartInfo(python, args.ToString())
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            try
            {
                _errors.Clear();
                _process = new Process { StartInfo = info, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log("[Python] " + e.Data); };
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    lock (_errors) _errors.AppendLine(e.Data);
                    // Show Python's errors/warnings right away, not only if it crashes
                    // (MediaPipe's own start-up chatter is skipped).
                    if (!IsMediaPipeNoise(e.Data)) Debug.LogWarning("[Python] " + e.Data);
                };
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                Status = "running";
                Debug.Log($"[Python] Started: {python} {args}\n  in {folder}");
                StartCoroutine(WatchForCrash());
                StartCoroutine(WarnIfNoData());
            }
            catch (Exception e)
            {
                Status = "could not start Python";
                _process = null;
                Debug.LogError($"[Python] Could not start '{python}': {e.Message}\n" +
                               "Is Python installed? Create the environment once in HandTracking-Python:\n" +
                               "  python -m venv .venv\n  .venv\\Scripts\\python.exe -m pip install -r requirements.txt");
            }
        }

        IEnumerator WatchForCrash()
        {
            while (_process != null && !_process.HasExited) yield return new WaitForSecondsRealtime(0.5f);
            if (_process == null) yield break; // stopped by us
            yield return new WaitForSecondsRealtime(0.2f); // let the last error lines arrive
            string err;
            lock (_errors) err = _errors.ToString().Trim();
            Status = $"Python stopped (exit code {_process.ExitCode})";
            if (_process.ExitCode != 0)
                Debug.LogError($"[Python] unity_sender.py stopped with an error (exit code {_process.ExitCode}):\n{Tail(err, 25)}");
            else
                Debug.Log("[Python] unity_sender.py closed.");
            _process.Dispose();
            _process = null;
        }

        IEnumerator WarnIfNoData()
        {
            yield return new WaitForSecondsRealtime(25f);
            var receiver = HandTrackingReceiver.Instance;
            if (IsRunning && receiver != null && !receiver.IsConnected)
                Debug.LogWarning("[Python] Python has been running for 25 s but no hand data has arrived. " +
                                 "Look for the 'Little Prince -> Unity' camera window (it can open BEHIND Unity), " +
                                 "and check that no other app (Zoom, Teams, Camera) is using the webcam. " +
                                 "To see everything Python prints, stop Play and run it by hand in the HandTracking-Python folder:  " +
                                 ".venv\\Scripts\\python.exe unity_sender.py");
        }

        static bool IsMediaPipeNoise(string line) =>
            line.StartsWith("W0000") || line.StartsWith("I0000") || line.StartsWith("INFO:") ||
            line.Contains("absl::InitializeLog") || line.Contains("inference_feedback_manager") ||
            line.Contains("landmark_projection_calculator") || line.StartsWith("WARNING: All log messages before");

        void OnDisable() => StopPython();
        void OnApplicationQuit() => StopPython();

        public void StopPython()
        {
            var p = _process;
            _process = null;
            if (p == null) return;
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(1000);
                    Debug.Log("[Python] Stopped unity_sender.py");
                }
            }
            catch (Exception e) { Debug.LogWarning("[Python] Could not stop Python: " + e.Message); }
            finally { p.Dispose(); }
            Status = "stopped";
        }

        // ---- Finding things ----------------------------------------------------------

        public static string FindPythonFolder()
        {
            string saved = PlayerPrefs.GetString(FolderPrefKey, "");
            if (IsPythonFolder(saved)) return Path.GetFullPath(saved);

            // Project root in the Editor (…/Assets/..), or the folder with the .exe in a build.
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string parent = Path.GetDirectoryName(root);
            string grandParent = parent != null ? Path.GetDirectoryName(parent) : null;

            foreach (var dir in new[] { root, parent, grandParent })
            {
                if (dir == null) continue;
                string direct = Path.Combine(dir, FolderName);
                if (IsPythonFolder(direct)) return direct;
            }

            // Next to the project inside a sibling folder, e.g. …/ThelilPrince-main/HandTracking-Python
            foreach (var dir in new[] { parent, grandParent })
            {
                if (dir == null || !Directory.Exists(dir)) continue;
                try
                {
                    foreach (var sibling in Directory.GetDirectories(dir))
                    {
                        string candidate = Path.Combine(sibling, FolderName);
                        if (IsPythonFolder(candidate)) return candidate;
                    }
                }
                catch (Exception) { /* no permission to list, ignore */ }
            }
            return null;
        }

        public static bool IsPythonFolder(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, ScriptName));

        static string FindPythonExecutable(string folder)
        {
            string[] venvs =
            {
                Path.Combine(folder, ".venv", "Scripts", "python.exe"), // Windows
                Path.Combine(folder, ".venv", "bin", "python3"),        // macOS / Linux
                Path.Combine(folder, ".venv", "bin", "python"),
                Path.Combine(folder, "venv", "Scripts", "python.exe"),
                Path.Combine(folder, "venv", "bin", "python3"),
            };
            foreach (var p in venvs)
                if (File.Exists(p)) return p;

            Debug.LogWarning("[Python] No .venv found in " + folder + ", using the system Python.");
            return Application.platform == RuntimePlatform.WindowsEditor ||
                   Application.platform == RuntimePlatform.WindowsPlayer ? "python" : "python3";
        }

        static string Tail(string text, int lines)
        {
            var all = text.Split('\n');
            return all.Length <= lines ? text : string.Join("\n", all, all.Length - lines, lines);
        }
    }
}
