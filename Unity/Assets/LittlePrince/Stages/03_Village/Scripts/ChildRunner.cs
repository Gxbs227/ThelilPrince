using LittlePrince.GuidedWalk;
using UnityEngine;
using UnityEngine.Events;

namespace LittlePrince.Stages.Village
{
    /// <summary>
    /// A child who runs ahead of the visitor along the guided path, so the visitor
    /// "follows" them, then runs off the path to a hiding place and disappears.
    ///
    ///   Boy:  waits next to his wave stop; when the walk leaves that stop (after the
    ///         wave) he starts running. He leaves the path at "Leave Path At", runs
    ///         behind a house ("Hide Spot") and vanishes once the visitor can't see him.
    ///   Girl: "Hidden Until Called" = on; the boy's On Hidden calls Appear(). She
    ///         runs to the wheat field and ducks into the wheat.
    ///
    /// The child never gets more than "Max Lead" metres ahead of the visitor: it waits
    /// (looking back) until the visitor catches up.
    /// Put it on the child's root object (pivot can be anywhere; its height above the
    /// ground is kept). If the child has an Animator with a float "Speed", it is set.
    /// </summary>
    public class ChildRunner : MonoBehaviour
    {
        public enum State { Waiting, Running, Hiding, Gone }

        [Header("When to start")]
        [Tooltip("Start running when the guided walk leaves this stop (e.g. the wave stop).")]
        public PathStop startAfterStop;
        [Tooltip("Invisible until something calls Appear() (e.g. the other child's On Hidden).")]
        public bool hiddenUntilCalled;

        [Header("Leading the visitor")]
        public GuidedPathWalker walker;
        [Tooltip("Path to run along. Empty = the walker's path.")]
        public GuidedPath path;
        [Tooltip("How far ahead of the visitor (along the path) the child likes to be.")]
        public float leadDistance = 4f;
        [Tooltip("Wait for the visitor when this far ahead.")]
        public float maxLead = 9f;
        public float runSpeed = 3f;

        [Header("Hiding")]
        [Tooltip("Waypoint of the path where the child leaves it to go and hide.")]
        public Transform leavePathAt;
        [Tooltip("Where the child hides (behind a house, in the wheat...). Empty = vanish at Leave Path At.")]
        public Transform hideSpot;
        [Tooltip("At the hide spot, crouch down into cover (wheat) instead of waiting until out of sight.")]
        public bool duckAtHideSpot;
        public float duckDepth = 1.4f;
        public float duckSeconds = 0.8f;
        [Tooltip("Vanish after this many seconds at the hide spot even if the visitor can still see the child.")]
        public float maxVisibleSeconds = 4f;

        [Header("Look")]
        public bool snapToGround = true;
        public LayerMask groundLayers = ~0;
        [Tooltip("Highest step the child walks up onto (kerbs, stairs). Anything taller (roofs, walls, fences) is not ground.")]
        public float maxStepUp = 0.4f;
        public float turnSpeed = 10f;
        [Tooltip("Little hop while running (placeholders without animation).")]
        public float bobHeight = 0.08f;
        public float bobFrequency = 9f;

        [Header("Hint")]
        public string followHint = "Follow him!";
        public float hintSeconds = 4f;
        public bool showHint = true;

        [Header("Events (animations, sounds)")]
        public UnityEvent onStartRunning = new UnityEvent();
        public UnityEvent onHidden = new UnityEvent();

        public State CurrentState { get; private set; } = State.Waiting;

        float _d, _leaveD = float.MaxValue, _feetOffset, _baseY, _hideTimer, _duck, _runStarted = -999f, _bobTime;
        bool _onPath, _appeared, _subscribed, _initialised;
        GuidedPathWalker _subscribedWalker;
        Animator _animator;
        bool _animatorHasSpeed;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        GuidedPath Path => path != null ? path : walker != null ? walker.path : null;

        void Awake()
        {
            Init();
            if (hiddenUntilCalled && !_appeared) gameObject.SetActive(false);
        }

        void Init()
        {
            if (_initialised) return;
            _initialised = true;
            // Pivot -> feet, from the model itself (works for the capsule placeholders and real models alike).
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                float feet = renderers[0].bounds.min.y;
                foreach (var r in renderers) feet = Mathf.Min(feet, r.bounds.min.y);
                _feetOffset = Mathf.Max(0f, transform.position.y - feet);
            }
            _baseY = transform.position.y;
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
                foreach (var p in _animator.parameters)
                    if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) _animatorHasSpeed = true;
        }

        void OnDisable() => Unsubscribe();

        /// <summary>Show the child and start running (hook the other child's On Hidden here).</summary>
        public void Appear()
        {
            if (CurrentState != State.Waiting) return;
            _appeared = true;
            gameObject.SetActive(true);
            StartRunning();
        }

        /// <summary>Start leading the visitor now.</summary>
        public void StartRunning()
        {
            if (CurrentState != State.Waiting) return;
            Init();
            CurrentState = State.Running;
            _runStarted = Time.time;
            _onPath = false;
            onStartRunning?.Invoke();
        }

        void Update()
        {
            if (walker == null) walker = FindObjectOfType<GuidedPathWalker>();
            Subscribe();
            float dt = Time.deltaTime;
            Vector3 before = transform.position;

            switch (CurrentState)
            {
                case State.Waiting:
                    if (walker != null) Face(walker.transform.position - transform.position, dt);
                    SetPosition(transform.position, moving: false);
                    break;
                case State.Running: Run(dt); break;
                case State.Hiding: Hide(dt); break;
            }

            if (_animatorHasSpeed)
            {
                Vector3 v = transform.position - before;
                v.y = 0f;
                _animator.SetFloat("Speed", dt > 0f ? v.magnitude / dt : 0f);
            }
        }

        void Run(float dt)
        {
            var p = Path;
            if (p == null || walker == null) return;
            float playerD = walker.path == p ? walker.Distance : p.ClosestDistance(walker.transform.position);

            if (!_onPath)
            {
                _onPath = true;
                _leaveD = leavePathAt != null ? p.ClosestDistance(leavePathAt.position) : p.Length;
                _d = Mathf.Clamp(Mathf.Max(p.ClosestDistance(transform.position), playerD + 1f), 0f, _leaveD);
            }

            float want = Mathf.Min(playerD + leadDistance, _leaveD);
            bool waitForVisitor = _d - playerD > maxLead;
            if (!waitForVisitor && _d < want) _d = Mathf.MoveTowards(_d, want, runSpeed * dt);

            Vector3 target = p.PositionAt(_d);
            bool arrived = MoveTowards(target, runSpeed * 1.3f, dt);
            if (arrived && waitForVisitor) Face(walker.transform.position - transform.position, dt);

            if (arrived && _d >= _leaveD - 0.01f)
            {
                CurrentState = State.Hiding;
                _hideTimer = 0f;
                _duck = 0f;
            }
        }

        void Hide(float dt)
        {
            if (hideSpot != null && !MoveTowards(hideSpot.position, runSpeed, dt)) return;

            _hideTimer += dt;
            if (duckAtHideSpot)
            {
                _duck = Mathf.MoveTowards(_duck, duckDepth, duckDepth / Mathf.Max(0.05f, duckSeconds) * dt);
                if (walker != null) Face(walker.transform.position - transform.position, dt);
                SetPosition(transform.position, moving: false);
                if (_duck >= duckDepth) Vanish();
                return;
            }
            if (!IsVisibleToCamera() || _hideTimer >= maxVisibleSeconds) Vanish();
        }

        void Vanish()
        {
            CurrentState = State.Gone;
            onHidden?.Invoke();
            gameObject.SetActive(false);
        }

        /// <summary>Moves toward a point on the ground; true once there.</summary>
        bool MoveTowards(Vector3 target, float speed, float dt)
        {
            Vector3 pos = transform.position;
            Vector3 flat = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
            float dist = flat.magnitude;
            float step = speed * dt;
            bool arrived = dist <= step + 0.02f;
            Vector3 next = arrived ? new Vector3(target.x, pos.y, target.z) : pos + flat / dist * step;
            if (!arrived) Face(flat, dt);
            SetPosition(next, moving: !arrived);
            return arrived;
        }

        /// <summary>Puts the child's feet on the ground at <paramref name="pos"/> (x, z); hop and duck are added on top.</summary>
        void SetPosition(Vector3 pos, bool moving)
        {
            // _baseY is the height without hop/duck, so they can never pile up frame after frame.
            if (snapToGround && TryGround(pos, _baseY - _feetOffset, out float g)) _baseY = g + _feetOffset;
            if (moving) _bobTime += Time.deltaTime;
            else _bobTime = 0f;
            pos.y = _baseY + Mathf.Abs(Mathf.Sin(_bobTime * bobFrequency * 0.5f)) * bobHeight - _duck;
            transform.position = pos;
        }

        void Face(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), dt * turnSpeed);
        }

        /// <summary>
        /// Ground under (x, z): the highest walkable surface no more than Max Step Up above the
        /// child's feet (so roofs, fences and walls overhead don't count); otherwise the lowest one.
        /// </summary>
        bool TryGround(Vector3 pos, float feetY, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(new Vector3(pos.x, feetY + 20f, pos.z), Vector3.down, Hits, 60f,
                                            groundLayers, QueryTriggerInteraction.Ignore);
            bool step = false, any = false;
            float lowest = 0f;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.transform.IsChildOf(transform)) continue;
                if (!any || h.point.y < lowest) lowest = h.point.y;
                any = true;
                if (h.normal.y > 0.6f && h.point.y <= feetY + maxStepUp && (!step || h.point.y > y))
                {
                    y = h.point.y;
                    step = true;
                }
            }
            if (!step && any) y = lowest;
            return any;
        }

        bool IsVisibleToCamera()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), b)) return false;

            Vector3 eye = cam.transform.position;
            foreach (var point in new[] { b.center, b.center + Vector3.up * b.extents.y * 0.8f })
            {
                if (!Physics.Linecast(eye, point, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
                if (hit.transform.IsChildOf(transform)) return true;
            }
            return false; // something (a house) is in the way
        }

        void Subscribe()
        {
            if (walker == _subscribedWalker && _subscribed) return;
            Unsubscribe();
            if (walker == null) return;
            walker.StopLeft += OnStopLeft;
            _subscribedWalker = walker;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (_subscribedWalker != null) _subscribedWalker.StopLeft -= OnStopLeft;
            _subscribedWalker = null;
            _subscribed = false;
        }

        void OnStopLeft(PathStop stop)
        {
            if (stop != null && stop == startAfterStop && !hiddenUntilCalled) StartRunning();
        }

        void OnGUI()
        {
            if (!showHint || CurrentState != State.Running || string.IsNullOrEmpty(followHint)) return;
            if (Time.time - _runStarted > hintSeconds) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            float w = Mathf.Min(720f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height - 110f, w, 60f), followHint, style);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.55f, 0.7f, 0.95f);
            if (leavePathAt != null) Gizmos.DrawWireSphere(leavePathAt.position + Vector3.up * 0.2f, 0.4f);
            if (leavePathAt != null && hideSpot != null) Gizmos.DrawLine(leavePathAt.position, hideSpot.position);
            if (hideSpot != null) Gizmos.DrawWireCube(hideSpot.position + Vector3.up * 0.5f, new Vector3(0.6f, 1f, 0.6f));
        }

        [ContextMenu("Test: start running now")]
        void TestRun()
        {
            if (!Application.isPlaying) { Debug.Log("Enter Play mode first."); return; }
            if (hiddenUntilCalled) Appear(); else StartRunning();
        }
    }
}
