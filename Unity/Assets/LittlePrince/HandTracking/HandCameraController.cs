using UnityEngine;

namespace LittlePrince.HandTracking
{
    /// <summary>
    /// Calm, hand-driven walk-through camera.
    ///
    ///   Open palm            -> walk forward gently
    ///   Hand left / right    -> turn (outside a centre dead zone)
    ///   Hand up / down       -> look up / down a little
    ///   Fist                 -> stop and rest
    ///   Point                -> stand still, turn slowly (aiming)
    ///   Pinch                -> stand still, view frozen (grabbing)
    ///   No hand              -> glide to a stop
    ///   Story gesture active -> glide to a stop (see holdStillDuringStoryGestures)
    ///
    /// Put this on the "Player" object. The camera can be a child (assign it to
    /// <see cref="pitchPivot"/>) or on the same object. A CharacterController is
    /// used for collisions if present. WASD / arrows also work for testing.
    /// </summary>
    public class HandCameraController : MonoBehaviour
    {
        [Tooltip("Usually the child Camera. Pitch (look up/down) is applied here. If empty, this transform is used.")]
        public Transform pitchPivot;

        [Header("Walking")]
        public float walkSpeed = 1.6f;
        [Tooltip("How quickly we speed up / slow down. Low = floaty and dreamy.")]
        public float acceleration = 1.5f;
        [Tooltip("Keep walking while the hand is open. Untick to require 'point' for walking instead.")]
        public bool walkWithOpenPalm = true;

        [Header("Looking")]
        [Tooltip("Degrees per second when the hand is at the edge of the view.")]
        public float turnSpeed = 55f;
        public float maxPitch = 20f;
        [Range(0f, 0.4f), Tooltip("Radius around the screen centre where the hand does nothing.")]
        public float deadZone = 0.12f;
        public float lookSmoothing = 4f;

        [Header("Story gestures")]
        [Tooltip("Stand still and stop turning while a story gesture (wave, swipe, run...) is happening, so e.g. an open-palm wave doesn't also walk and turn the camera.")]
        public bool holdStillDuringStoryGestures = true;

        [Header("Limits")]
        [Tooltip("0 = unlimited. Keeps the visitor inside the stage.")]
        public float maxDistanceFromStart = 0f;
        public bool useGravity = true;

        [Header("Testing")]
        public bool keyboardFallback = true;

        public float CurrentSpeed => _speed;
        /// <summary>Set to false from a StageManager during cut-scenes / transitions.</summary>
        public bool InputEnabled { get; set; } = true;

        CharacterController _cc;
        Vector3 _start;
        float _speed, _yawVel, _pitch, _targetPitch, _verticalVel;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _start = transform.position;
            if (pitchPivot == null) pitchPivot = transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float targetSpeed = 0f, targetYawVel = 0f;
            _targetPitch = Mathf.Lerp(_targetPitch, 0f, dt * 0.5f); // drift back to level

            var receiver = HandTrackingReceiver.Instance;
            var hand = receiver != null ? receiver.PrimaryHand : null;

            bool storyGesture = holdStillDuringStoryGestures && hand != null && hand.StoryGestures.Count > 0;
            if (InputEnabled && hand != null && !storyGesture)
            {
                Vector2 offset = hand.Palm - new Vector2(0.5f, 0.5f); // -0.5..0.5
                float x = ApplyDeadZone(offset.x);
                float y = ApplyDeadZone(offset.y);

                bool walking = walkWithOpenPalm ? hand.Gesture == HandGesture.Open
                                                : hand.Gesture == HandGesture.Point;
                bool resting = hand.Gesture == HandGesture.Fist;

                // Pinch = hold the view still so the user can grab things.
                // Point = turn gently so the user can aim at things.
                float lookScale = resting || hand.Gesture == HandGesture.Pinch ? 0f
                                : hand.Gesture == HandGesture.Point ? 0.35f
                                : 1f;
                if (lookScale > 0f)
                {
                    targetYawVel = x * 2f * turnSpeed * lookScale;
                    _targetPitch = -y * 2f * maxPitch;
                }
                if (walking) targetSpeed = walkSpeed;
            }

            if (InputEnabled && keyboardFallback)
            {
                Vector2 k = DevInput.Move;
                if (k != Vector2.zero)
                {
                    targetSpeed = k.y * walkSpeed;
                    targetYawVel = k.x * turnSpeed;
                }
            }

            // Smooth everything so it feels like floating, not driving.
            _speed = Mathf.MoveTowards(_speed, targetSpeed, acceleration * dt);
            _yawVel = Mathf.Lerp(_yawVel, targetYawVel, dt * lookSmoothing);
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(_targetPitch, -maxPitch, maxPitch), dt * lookSmoothing);

            transform.Rotate(0f, _yawVel * dt, 0f, Space.World);
            if (pitchPivot == transform)
            {
                var e = transform.eulerAngles;
                transform.rotation = Quaternion.Euler(_pitch, e.y, 0f);
            }
            else
            {
                pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 move = forward * _speed;

            if (_cc != null)
            {
                if (useGravity)
                {
                    _verticalVel = _cc.isGrounded ? -1f : _verticalVel + Physics.gravity.y * dt;
                    move.y = _verticalVel;
                }
                _cc.Move(move * dt);
            }
            else
            {
                transform.position += move * dt;
            }

            if (maxDistanceFromStart > 0f)
            {
                Vector3 fromStart = transform.position - _start;
                fromStart.y = 0f;
                if (fromStart.magnitude > maxDistanceFromStart)
                {
                    Vector3 clamped = _start + fromStart.normalized * maxDistanceFromStart;
                    clamped.y = transform.position.y;
                    if (_cc != null) _cc.enabled = false;
                    transform.position = clamped;
                    if (_cc != null) _cc.enabled = true;
                }
            }
        }

        float ApplyDeadZone(float v)
        {
            float a = Mathf.Abs(v);
            if (a < deadZone) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((a - deadZone) / (0.5f - deadZone)) * 0.5f;
        }
    }
}
