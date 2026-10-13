using System;
using System.Collections.Generic;
using UnityEngine;

namespace Halazia.Shared.Movement
{
    /// <summary>
    /// Kinematic path-follower utilizing CharacterController.
    /// Converts discrete AI waypoint paths into physically believable, smooth 3D movement.
    /// Incorporates acceleration, braking curves, angular rotation clamping, corner deceleration,
    /// dynamic obstacle blockage detection, and deck gravity.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class AgentMotor : MonoBehaviour, IAgentMotor
    {
        [Header("Locomotion Configuration")]
        [Tooltip("Maximum cruising speed on straight paths (m/s).")]
        [SerializeField] private float _maxSpeed = 4.2f;

        [Tooltip("Rate of linear acceleration from rest (m/s^2).")]
        [SerializeField] private float _acceleration = 7.5f;

        [Tooltip("Rate of linear deceleration when coming to a halt (m/s^2).")]
        [SerializeField] private float _deceleration = 9.0f;

        [Tooltip("Distance from final target at which braking commences (meters).")]
        [SerializeField] private float _brakingDistance = 2.5f;

        [Tooltip("Acceptance radius around final waypoint before declaring arrival (meters).")]
        [SerializeField] private float _stoppingDistance = 0.35f;

        [Header("Rotational Dynamics")]
        [Tooltip("Maximum angular turn rate (degrees/second) to prevent unnatural instant snapping.")]
        [SerializeField] private float _angularSpeed = 380f;

        [Tooltip("Minimum approach speed when negotiating sharp corners (m/s).")]
        [SerializeField] private float _minCornerSpeed = 1.2f;

        [Tooltip("Distance threshold to next intermediate waypoint before advancing (meters).")]
        [SerializeField] private float _waypointTolerance = 0.5f;

        [Header("Gravity & Grounding")]
        [Tooltip("Downward gravitational acceleration for ship decks (m/s^2).")]
        [SerializeField] private float _gravity = 18.0f;

        [Tooltip("Slight downward velocity applied when grounded to maintain contact with deck slopes/stairs.")]
        [SerializeField] private float _groundStickVelocity = -2.0f;

        [Header("Obstacle & Blockage Detection")]
        [Tooltip("Threshold displacement (meters) below which the agent is considered physically obstructed.")]
        [SerializeField] private float _stuckDisplacementThreshold = 0.08f;

        [Tooltip("Time interval (seconds) for sampling displacement progress.")]
        [SerializeField] private float _stuckSampleInterval = 0.35f;

        [Tooltip("Continuous duration (seconds) spent stuck before firing OnPathBlocked.")]
        [SerializeField] private float _stuckTimeout = 0.8f;

        [Header("Debug Visualization")]
        [SerializeField] private bool _drawDebugGizmos = true;
        [SerializeField] private Color _gizmoPathColor = new Color(0f, 0.8f, 1f, 0.9f);

        // Events
        public event Action OnArrived;
        public event Action OnPathBlocked;

        // Components & References
        private CharacterController _characterController;

        // Path State
        private readonly List<Vector3> _pathWaypoints = new List<Vector3>();
        private int _currentWaypointIndex = 0;
        private bool _isMoving = false;
        private Vector3 _targetDestination;

        // Facing Overrides
        private Transform _faceTargetTransform;
        private Vector3? _faceTargetPosition;

        // Motion Variables
        private float _currentForwardSpeed = 0f;
        private float _verticalVelocity = 0f;
        private Vector3 _lastWorldPosition;
        private Vector3 _actualVelocity;
        private Vector3 _currentMoveDirection = Vector3.forward;

        // Stuck Detection Tracking
        private float _stuckSampleTimer = 0f;
        private float _stuckDuration = 0f;
        private Vector3 _positionAtLastSample;

        // Public Interface Properties
        public bool IsMoving => _isMoving;
        public Vector3 Velocity => _actualVelocity;
        public float CurrentSpeed => _currentForwardSpeed;
        public Vector3 TargetDestination => _targetDestination;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _lastWorldPosition = transform.position;
            _positionAtLastSample = transform.position;
            _currentMoveDirection = transform.forward;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            // 1. Process active path traversal
            if (_isMoving && _pathWaypoints.Count > 0)
            {
                ProcessPathFollowing(deltaTime);
                CheckForBlockage(deltaTime);
            }
            else
            {
                // Decelerate naturally to 0 if stopped mid-movement
                if (_currentForwardSpeed > 0f)
                {
                    _currentForwardSpeed = Mathf.Max(0f, _currentForwardSpeed - _deceleration * deltaTime);
                }
            }

            // 2. Handle rotation (align with face target if specified, otherwise align with movement direction)
            ProcessRotation(deltaTime);

            // 3. Handle vertical physics (gravity & deck adherence)
            ProcessGravity(deltaTime);

            // 4. Translate via CharacterController along calculated movement direction
            // IMPORTANT: Uses _currentMoveDirection so strafing towards a face target moves along the path,
            // and agents turning into a new waypoint don't drift sideways.
            Vector3 horizontalMove = _currentMoveDirection * (_currentForwardSpeed * deltaTime);
            Vector3 verticalMove = Vector3.up * (_verticalVelocity * deltaTime);
            _characterController.Move(horizontalMove + verticalMove);

            // 5. Compute measured actual velocity (crucial for accurate animator sync without foot sliding)
            _actualVelocity = (transform.position - _lastWorldPosition) / deltaTime;
            _lastWorldPosition = transform.position;
        }

        /// <summary>
        /// Supplies an array of world-space waypoints for the motor to follow.
        /// </summary>
        public void SetPath(Vector3[] waypoints)
        {
            _pathWaypoints.Clear();
            _stuckDuration = 0f;
            _stuckSampleTimer = 0f;
            _positionAtLastSample = transform.position;

            if (waypoints == null || waypoints.Length == 0)
            {
                Stop();
                return;
            }

            for (int i = 0; i < waypoints.Length; i++)
            {
                _pathWaypoints.Add(waypoints[i]);
            }

            _targetDestination = _pathWaypoints[_pathWaypoints.Count - 1];
            _currentWaypointIndex = 0;
            _isMoving = true;

            // Skip immediate redundant waypoint (e.g., current agent position)
            Vector3 flatSelf = Flatten(transform.position);
            while (_currentWaypointIndex < _pathWaypoints.Count - 1 &&
                   Vector3.Distance(flatSelf, Flatten(_pathWaypoints[_currentWaypointIndex])) < _waypointTolerance)
            {
                _currentWaypointIndex++;
            }

            // Prime initial movement direction
            Vector3 initialDir = Flatten(_pathWaypoints[_currentWaypointIndex] - transform.position);
            if (initialDir.sqrMagnitude > 0.001f)
            {
                _currentMoveDirection = initialDir.normalized;
            }
        }

        /// <summary>
        /// Commands the agent to look toward a dynamic transform (e.g. player during combat) while moving.
        /// </summary>
        public void SetFaceTarget(Transform target)
        {
            _faceTargetTransform = target;
            _faceTargetPosition = null;
        }

        /// <summary>
        /// Commands the agent to look toward a specific world position while moving.
        /// </summary>
        public void SetFaceTarget(Vector3? worldPosition)
        {
            _faceTargetPosition = worldPosition;
            _faceTargetTransform = null;
        }

        /// <summary>
        /// Immediately ceases movement, clears current path, and brings the agent to a controlled stop.
        /// </summary>
        public void Stop()
        {
            _isMoving = false;
            _pathWaypoints.Clear();
            _currentWaypointIndex = 0;
            _stuckDuration = 0f;
        }

        #region Locomotion & Steering Logic

        private void ProcessPathFollowing(float deltaTime)
        {
            if (_currentWaypointIndex >= _pathWaypoints.Count)
            {
                CompleteArrival();
                return;
            }

            Vector3 currentTarget = _pathWaypoints[_currentWaypointIndex];
            Vector3 flatSelf = Flatten(transform.position);
            Vector3 flatTarget = Flatten(currentTarget);
            float distToWaypoint = Vector3.Distance(flatSelf, flatTarget);

            bool isLastWaypoint = (_currentWaypointIndex == _pathWaypoints.Count - 1);

            // 1. Waypoint transition check (tolerance distance + waypoint plane pass-through to prevent circling)
            if (isLastWaypoint)
            {
                if (distToWaypoint <= _stoppingDistance)
                {
                    CompleteArrival();
                    return;
                }
            }
            else
            {
                bool passedWaypoint = false;
                if (_currentWaypointIndex > 0)
                {
                    Vector3 prevPoint = Flatten(_pathWaypoints[_currentWaypointIndex - 1]);
                    Vector3 segmentDir = (flatTarget - prevPoint).normalized;
                    // If projection past the waypoint is positive, agent has crossed the waypoint threshold
                    if (Vector3.Dot(flatSelf - flatTarget, segmentDir) > 0f)
                    {
                        passedWaypoint = true;
                    }
                }

                if (distToWaypoint <= _waypointTolerance || passedWaypoint)
                {
                    _currentWaypointIndex++;
                    if (_currentWaypointIndex >= _pathWaypoints.Count)
                    {
                        CompleteArrival();
                        return;
                    }
                    flatTarget = Flatten(_pathWaypoints[_currentWaypointIndex]);
                }
            }

            // 2. Update horizontal move direction towards current target
            Vector3 dirToTarget = Flatten(flatTarget - flatSelf);
            if (dirToTarget.sqrMagnitude > 0.001f)
            {
                _currentMoveDirection = dirToTarget.normalized;
            }

            // 3. Calculate total remaining distance across remaining path segments
            float remainingDistance = CalculateRemainingDistance(flatSelf);

            // 4. Determine target speed based on braking curve and corner sharpness
            float targetSpeed = CalculateTargetSpeed(remainingDistance, isLastWaypoint);

            // 5. Apply smooth linear acceleration / deceleration
            if (_currentForwardSpeed < targetSpeed)
            {
                _currentForwardSpeed = Mathf.MoveTowards(_currentForwardSpeed, targetSpeed, _acceleration * deltaTime);
            }
            else
            {
                _currentForwardSpeed = Mathf.MoveTowards(_currentForwardSpeed, targetSpeed, _deceleration * deltaTime);
            }
        }

        /// <summary>
        /// Computes desired speed taking into account:
        /// 1. Constant deceleration braking curve: v = v_max * sqrt(d / d_brake) when nearing final destination.
        /// 2. Corner deceleration factor: v_corner = v_max * ((1 + cos(theta)) / 2) for sharp turns.
        /// </summary>
        private float CalculateTargetSpeed(float remainingDistance, bool isLastWaypoint)
        {
            float speed = _maxSpeed;

            // 1. Braking curve for final destination approach
            if (remainingDistance < _brakingDistance)
            {
                float brakeFactor = Mathf.Clamp01(remainingDistance / _brakingDistance);
                speed = Mathf.Min(speed, _maxSpeed * Mathf.Sqrt(brakeFactor));
            }

            // 2. Corner slowdown for intermediate waypoints (prevents sliding out of ship corridor bulkheads)
            if (!isLastWaypoint && _currentWaypointIndex < _pathWaypoints.Count - 1)
            {
                Vector3 currentDir = (Flatten(_pathWaypoints[_currentWaypointIndex]) - Flatten(transform.position)).normalized;
                Vector3 nextDir = (Flatten(_pathWaypoints[_currentWaypointIndex + 1]) - Flatten(_pathWaypoints[_currentWaypointIndex])).normalized;

                if (currentDir.sqrMagnitude > 0.001f && nextDir.sqrMagnitude > 0.001f)
                {
                    float dot = Vector3.Dot(currentDir, nextDir);
                    // Map dot [-1, 1] to corner factor [0, 1]: (1 + cos(theta)) / 2
                    float cornerFactor = Mathf.Clamp01((1.0f + dot) * 0.5f);
                    float cornerSpeed = Mathf.Lerp(_minCornerSpeed, _maxSpeed, cornerFactor);
                    speed = Mathf.Min(speed, cornerSpeed);
                }
            }

            return speed;
        }

        private float CalculateRemainingDistance(Vector3 flatSelf)
        {
            if (_pathWaypoints.Count == 0 || _currentWaypointIndex >= _pathWaypoints.Count)
                return 0f;

            float dist = Vector3.Distance(flatSelf, Flatten(_pathWaypoints[_currentWaypointIndex]));
            for (int i = _currentWaypointIndex; i < _pathWaypoints.Count - 1; i++)
            {
                dist += Vector3.Distance(Flatten(_pathWaypoints[i]), Flatten(_pathWaypoints[i + 1]));
            }
            return dist;
        }

        private void ProcessRotation(float deltaTime)
        {
            Vector3 desiredLookDir = Vector3.zero;

            // Priority 1: Explicit target transform (e.g. strafing while facing player)
            if (_faceTargetTransform != null)
            {
                desiredLookDir = Flatten(_faceTargetTransform.position - transform.position);
            }
            // Priority 2: Explicit look target point
            else if (_faceTargetPosition.HasValue)
            {
                desiredLookDir = Flatten(_faceTargetPosition.Value - transform.position);
            }
            // Priority 3: Path movement direction
            else if (_isMoving)
            {
                desiredLookDir = _currentMoveDirection;
            }

            if (desiredLookDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(desiredLookDir.normalized, Vector3.up);
                // Rotate smoothly capped at angularSpeed to avoid snapping
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, _angularSpeed * deltaTime);
            }
        }

        private void ProcessGravity(float deltaTime)
        {
            if (_characterController.isGrounded)
            {
                // Slight downward velocity keeps controller pinned to uneven deck geometries and stairs
                _verticalVelocity = _groundStickVelocity;
            }
            else
            {
                _verticalVelocity -= _gravity * deltaTime;
            }
        }

        private void CheckForBlockage(float deltaTime)
        {
            _stuckSampleTimer += deltaTime;
            if (_stuckSampleTimer >= _stuckSampleInterval)
            {
                float displacement = Vector3.Distance(Flatten(transform.position), Flatten(_positionAtLastSample));
                _positionAtLastSample = transform.position;
                _stuckSampleTimer = 0f;

                // If agent is commanding speed but displacement over sample window is negligible
                if (_currentForwardSpeed > 0.5f && displacement < _stuckDisplacementThreshold)
                {
                    _stuckDuration += _stuckSampleInterval;
                    if (_stuckDuration >= _stuckTimeout)
                    {
                        Debug.LogWarning($"[AgentMotor] Obstacle detected! Agent '{gameObject.name}' blocked at {transform.position}.");
                        Stop();
                        OnPathBlocked?.Invoke();
                    }
                }
                else
                {
                    _stuckDuration = Mathf.Max(0f, _stuckDuration - _stuckSampleInterval);
                }
            }
        }

        private void CompleteArrival()
        {
            _isMoving = false;
            _currentForwardSpeed = 0f;
            _pathWaypoints.Clear();
            _currentWaypointIndex = 0;
            OnArrived?.Invoke();
        }

        private static Vector3 Flatten(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        #endregion

        #region Editor Debugging & Gizmos

        private void OnDrawGizmosSelected()
        {
            if (!_drawDebugGizmos) return;

            // Draw active path
            if (_pathWaypoints != null && _pathWaypoints.Count > 0)
            {
                Gizmos.color = _gizmoPathColor;
                Vector3 prev = transform.position;
                for (int i = _currentWaypointIndex; i < _pathWaypoints.Count; i++)
                {
                    Vector3 wp = _pathWaypoints[i];
                    Gizmos.DrawLine(prev, wp);
                    Gizmos.DrawSphere(wp, 0.15f);
                    prev = wp;
                }

                // Highlight final destination
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(_targetDestination, _stoppingDistance);
            }

            // Draw face target if active
            if (_faceTargetTransform != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position + Vector3.up, _faceTargetTransform.position + Vector3.up);
            }
            else if (_faceTargetPosition.HasValue)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position + Vector3.up, _faceTargetPosition.Value + Vector3.up);
            }
        }

        #endregion
    }
}
