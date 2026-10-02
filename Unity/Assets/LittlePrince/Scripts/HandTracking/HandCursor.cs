using UnityEngine;

namespace LittlePrince.HandTracking
{
    /// <summary>
    /// Soft circular cursor that follows the index finger, raycasts into the
    /// world and talks to <see cref="HandInteractable"/>s:
    /// hover = point at it, select = pinch (or hold still for dwellTime).
    ///
    /// Draws itself with OnGUI so it works with no Canvas setup. Assign
    /// <see cref="cursorGraphic"/> (a UI element on a Screen Space - Overlay canvas) to use your own art instead.
    /// </summary>
    public class HandCursor : MonoBehaviour
    {
        public Camera targetCamera;
        public float maxDistance = 25f;
        public LayerMask interactableLayers = ~0;

        [Tooltip("Seconds of hovering that also count as a select. 0 = pinch only.")]
        public float dwellTime = 0f;

        [Header("Look")]
        public RectTransform cursorGraphic;
        public float size = 42f;
        public Color idleColor = new Color(1f, 1f, 1f, 0.55f);
        public Color hoverColor = new Color(1f, 0.85f, 0.55f, 0.9f);
        public Color pinchColor = new Color(1f, 0.65f, 0.45f, 1f);

        public HandInteractable Hovered { get; private set; }
        public Vector2 ViewportPosition { get; private set; }

        HandInteractable _selected;
        float _hoverTimer;
        bool _dwellFired;
        Texture2D _ring, _dot;

        HandTrackingReceiver _subscribedTo;

        void Start()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            _ring = MakeCircle(128, 0.78f);
            _dot = MakeCircle(64, 0f);
        }

        void OnDisable()
        {
            Subscribe(null);
            SetHovered(null);
        }

        void Subscribe(HandTrackingReceiver r)
        {
            if (r == _subscribedTo) return;
            if (_subscribedTo != null) { _subscribedTo.PinchStarted -= OnPinchStarted; _subscribedTo.PinchEnded -= OnPinchEnded; }
            _subscribedTo = r;
            if (r != null) { r.PinchStarted += OnPinchStarted; r.PinchEnded += OnPinchEnded; }
        }

        void Update()
        {
            Subscribe(HandTrackingReceiver.Instance);
            var hand = _subscribedTo != null ? _subscribedTo.PrimaryHand : null;
            if (hand == null || targetCamera == null)
            {
                SetHovered(null);
                if (cursorGraphic != null) cursorGraphic.gameObject.SetActive(false);
                return;
            }

            ViewportPosition = hand.IndexTip;
            Ray ray = targetCamera.ViewportPointToRay(new Vector3(ViewportPosition.x, ViewportPosition.y, 0f));
            HandInteractable hit = null;
            if (Physics.Raycast(ray, out var info, maxDistance, interactableLayers, QueryTriggerInteraction.Collide))
                hit = info.collider.GetComponentInParent<HandInteractable>();
            if (hit != null && !hit.isActiveAndEnabled) hit = null;
            SetHovered(hit);

            if (dwellTime > 0f && Hovered != null && _selected == null && !_dwellFired)
            {
                _hoverTimer += Time.deltaTime;
                if (_hoverTimer >= dwellTime)
                {
                    _dwellFired = true;
                    Hovered.Select();
                    Hovered.Release();
                }
            }

            if (cursorGraphic != null)
            {
                cursorGraphic.gameObject.SetActive(true);
                cursorGraphic.position = new Vector2(ViewportPosition.x * Screen.width, ViewportPosition.y * Screen.height);
                cursorGraphic.localScale = Vector3.one * (hand.IsPinching ? 0.75f : Hovered != null ? 1.2f : 1f);
            }
        }

        void SetHovered(HandInteractable target)
        {
            if (target == Hovered) return;
            if (Hovered != null) Hovered.HoverExit();
            Hovered = target;
            _hoverTimer = 0f;
            _dwellFired = false;
            if (Hovered != null) Hovered.HoverEnter();
        }

        void OnPinchStarted(TrackedHand hand)
        {
            if (hand != _subscribedTo.PrimaryHand || Hovered == null) return;
            _selected = Hovered;
            _selected.Select();
        }

        void OnPinchEnded(TrackedHand hand)
        {
            if (_selected == null) return;
            _selected.Release();
            _selected = null;
        }

        void OnGUI()
        {
            if (cursorGraphic != null || _ring == null) return;
            var receiver = HandTrackingReceiver.Instance;
            var hand = receiver != null ? receiver.PrimaryHand : null;
            if (hand == null) return;

            Vector2 p = new Vector2(ViewportPosition.x * Screen.width, (1f - ViewportPosition.y) * Screen.height);
            float s = size * (Hovered != null ? 1.25f : 1f) * Mathf.Lerp(1f, 0.7f, hand.PinchStrength);
            GUI.color = hand.IsPinching ? pinchColor : Hovered != null ? hoverColor : idleColor;
            GUI.DrawTexture(new Rect(p.x - s / 2, p.y - s / 2, s, s), _ring);

            // Inner dot grows with dwell progress / pinch strength.
            float fill = dwellTime > 0f && Hovered != null ? Mathf.Clamp01(_hoverTimer / dwellTime) : hand.PinchStrength;
            float d = Mathf.Lerp(size * 0.18f, s * 0.8f, fill);
            GUI.DrawTexture(new Rect(p.x - d / 2, p.y - d / 2, d, d), _dot);
            GUI.color = Color.white;
        }

        static Texture2D MakeCircle(int res, float innerRadius)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (res - 1) / 2f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float outer = Mathf.Clamp01((1f - r) * res * 0.25f);
                float inner = innerRadius <= 0f ? 1f : Mathf.Clamp01((r - innerRadius) * res * 0.25f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, outer * inner));
            }
            tex.Apply();
            return tex;
        }
    }
}
