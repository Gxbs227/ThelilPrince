using UnityEngine;

namespace LittlePrince.HandTracking
{
    /// <summary>
    /// On-screen panel for checking the MediaPipe -> Unity connection:
    /// connection status, packets per second, gesture, pinch, and a live 2D
    /// skeleton of every hand. Press F1 to toggle.
    /// </summary>
    public class HandDebugVisualizer : MonoBehaviour
    {
        public bool show = true;
        [Tooltip("Size of the hand preview box in pixels.")]
        public Vector2 previewSize = new Vector2(320, 180);

        static readonly int[] Bones =
        {
            0,1, 1,2, 2,3, 3,4,  0,5, 5,6, 6,7, 7,8,  5,9, 9,10, 10,11, 11,12,
            9,13, 13,14, 14,15, 15,16,  13,17, 0,17, 17,18, 18,19, 19,20,
        };

        Texture2D _white;
        GUIStyle _label;

        void Update()
        {
            if (DevInput.ToggleDebugPressed) show = !show;
        }

        void OnGUI()
        {
            if (!show) return;
            if (_white == null) { _white = Texture2D.whiteTexture; }
            if (_label == null) _label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

            var r = HandTrackingReceiver.Instance;
            float pad = 10f;
            var box = new Rect(pad, pad, previewSize.x, 118 + previewSize.y);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, _white);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(box.x + 8, box.y + 6, box.width - 16, 112));
            if (r == null)
            {
                GUILayout.Label("<color=#ff8080>No HandTrackingReceiver in scene</color>", _label);
            }
            else
            {
                string status = r.IsConnected
                    ? $"<color=#9dffa0>● MediaPipe connected</color>  {r.PacketsPerSecond:0} pkt/s"
                    : r.IsSimulating
                        ? "<color=#ffd27f>● Mouse simulation</color> (start unity_sender.py)"
                        : $"<color=#ff8080>● Waiting for data on UDP {r.port}</color>";
                GUILayout.Label(status, _label);

                var h = r.PrimaryHand;
                GUILayout.Label(h == null
                    ? "Hand: none"
                    : $"Hand: {h.Side}   gesture: <b>{h.Gesture}</b>   pinch: {h.PinchStrength:0.00}{(h.IsPinching ? "  PINCHING" : "")}", _label);
                GUILayout.Label(h == null ? "" : h.StoryGestures.Count > 0
                    ? $"Story: <color=#9dffa0><b>{string.Join("  ", h.StoryGestures)}</b></color>"
                    : $"Palm: ({h.Palm.x:0.00}, {h.Palm.y:0.00})   size: {h.Size:0.00}", _label);
                GUILayout.Label($"<size=11>Hands: {r.Hands.Count}   F1 hide   {(string.IsNullOrEmpty(r.LastError) ? "" : "err: " + r.LastError)}</size>", _label);
            }
            GUILayout.EndArea();

            if (r == null) return;

            // Hand preview (aspect of the camera image is roughly 16:9)
            var preview = new Rect(box.x + 8, box.y + 118, previewSize.x - 16, previewSize.y - 8);
            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(preview, _white);

            foreach (var hand in r.Hands)
            {
                Color c = hand == r.PrimaryHand ? new Color(1f, 0.85f, 0.55f) : new Color(0.6f, 0.8f, 1f);
                GUI.color = c;
                for (int i = 0; i < Bones.Length; i += 2)
                    DrawLine(ToPreview(preview, hand.Landmarks[Bones[i]]), ToPreview(preview, hand.Landmarks[Bones[i + 1]]), 2f);
                foreach (var lm in hand.Landmarks)
                {
                    Vector2 p = ToPreview(preview, lm);
                    GUI.DrawTexture(new Rect(p.x - 3, p.y - 3, 6, 6), _white);
                }
            }
            GUI.color = Color.white;
        }

        static Vector2 ToPreview(Rect area, Vector3 viewport) =>
            new Vector2(area.x + viewport.x * area.width, area.y + (1f - viewport.y) * area.height);

        void DrawLine(Vector2 a, Vector2 b, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), _white);
            GUI.matrix = m;
        }
    }
}
