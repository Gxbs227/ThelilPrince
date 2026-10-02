using UnityEngine;

namespace LittlePrince.HandTracking.Demo
{
    /// <summary>
    /// Test-scene flower: a closed bud that blooms (grows + changes colour)
    /// when the visitor pinches it (or when a StoryGestureTrigger calls Toggle).
    /// Shows how to react to <see cref="HandInteractable"/> from code.
    /// </summary>
    [RequireComponent(typeof(HandInteractable))]
    public class DemoFlower : MonoBehaviour
    {
        public Renderer head;
        public Color budColor = new Color(0.55f, 0.75f, 0.55f);
        public Color bloomColor = new Color(1f, 0.55f, 0.6f);
        public float bloomScale = 1.8f;
        public float speed = 3f;
        [Tooltip("Bloom when pinched. Turn off to open it only via a StoryGestureTrigger.")]
        public bool toggleOnSelect = true;

        bool _bloomed;
        float _t;
        Vector3 _headBaseScale;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            if (toggleOnSelect) GetComponent<HandInteractable>().onSelect.AddListener(Toggle);
            if (head != null) _headBaseScale = head.transform.localScale;
            _mpb = new MaterialPropertyBlock();
        }

        public void Toggle()
        {
            _bloomed = !_bloomed;
            Debug.Log($"[HandTracking demo] {name} {(_bloomed ? "bloomed" : "closed")}");
        }

        void Update()
        {
            if (head == null) return;
            _t = Mathf.MoveTowards(_t, _bloomed ? 1f : 0f, Time.deltaTime * speed);
            float e = Mathf.SmoothStep(0f, 1f, _t);
            head.transform.localScale = _headBaseScale * Mathf.Lerp(1f, bloomScale, e);
            head.transform.localRotation = Quaternion.Euler(0f, e * 180f, 0f);

            Color c = Color.Lerp(budColor, bloomColor, e);
            head.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c); // URP / HDRP
            _mpb.SetColor("_Color", c);     // Built-in
            head.SetPropertyBlock(_mpb);
        }
    }
}
