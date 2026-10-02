using UnityEngine;

namespace LittlePrince.Stages.Village
{
    /// <summary>
    /// The paper birds fly off toward the next stage as soon as this object is
    /// switched on. Each child is one bird and flaps a little. Placeholder motion:
    /// replace with an Animator / Timeline when the real birds are ready.
    /// </summary>
    public class OrigamiBirdsFly : MonoBehaviour
    {
        public float speed = 2.5f;
        public float rise = 0.8f;
        public float flapAngle = 25f;
        public float flapSpeed = 9f;
        [Tooltip("Switch the birds off after this many seconds (0 = never).")]
        public float lifetime = 12f;

        Vector3 _start;
        float _t;

        void Awake() => _start = transform.position;

        void OnEnable()
        {
            transform.position = _start;
            _t = 0f;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float ease = Mathf.Clamp01(_t / 1.5f); // take off gently
            transform.position += (transform.forward * speed + Vector3.up * rise) * ease * Time.deltaTime;

            int i = 0;
            foreach (Transform bird in transform)
            {
                float flap = Mathf.Sin(_t * flapSpeed + i * 1.7f) * flapAngle;
                bird.localRotation = Quaternion.Euler(0f, 0f, flap);
                bird.localPosition += Vector3.up * (Mathf.Sin(_t * 2f + i) * 0.1f * Time.deltaTime);
                i++;
            }

            if (lifetime > 0f && _t >= lifetime) gameObject.SetActive(false);
        }
    }
}
