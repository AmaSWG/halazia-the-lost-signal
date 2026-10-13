using UnityEngine;
using UnityEngine.AI;
using Halazia.Shared.Movement;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Halazia.Sandbox
{
    /// <summary>
    /// Test harness for verifying AgentMotor kinematics, turning, corner deceleration,
    /// braking curves, and obstacle blockage in an isolated sandbox environment.
    /// Not intended for production combat scenes.
    /// </summary>
    public class MovementSandboxDriver : MonoBehaviour
    {
        [Header("Target Motor")]
        [Tooltip("Reference to the motor. If unassigned, attempts to find on this GameObject.")]
        [SerializeField] private AgentMotor _motor;

        [Header("Controls & Testing")]
        [Tooltip("Layer mask for ground raycasting when clicking to pick a destination.")]
        [SerializeField] private LayerMask _groundLayer = ~0;

        [Tooltip("Max radius when picking a random wander point on NavMesh.")]
        [SerializeField] private float _randomWanderRadius = 12f;

        [Header("Debug Visualization")]
        [SerializeField] private bool _showTargetMarker = true;

        private Vector3 _lastCommandedDestination;
        private bool _hasTarget = false;

        private void Awake()
        {
            if (_motor == null)
            {
                _motor = GetComponent<AgentMotor>();
            }
        }

        private void OnEnable()
        {
            if (_motor != null)
            {
                _motor.OnArrived += HandleArrived;
                _motor.OnPathBlocked += HandlePathBlocked;
            }
        }

        private void OnDisable()
        {
            if (_motor != null)
            {
                _motor.OnArrived -= HandleArrived;
                _motor.OnPathBlocked -= HandlePathBlocked;
            }
        }

        private void Update()
        {
            if (_motor == null) return;

            // 1. Mouse Click Detection (Left click = move to clicked position)
            if (CheckLeftClick(out Vector2 screenPosition))
            {
                ProcessClickToMove(screenPosition);
            }

            // 2. Key Press Detection (Space or R = pick random point on NavMesh)
            if (CheckRandomTrigger())
            {
                PickRandomNavMeshDestination();
            }
        }

        private void ProcessClickToMove(Vector2 screenPosition)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[MovementSandboxDriver] No Main Camera found in scene for raycasting!");
                return;
            }

            Ray ray = cam.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, _groundLayer))
            {
                // Sample closest position on NavMesh
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 3.0f, NavMesh.AllAreas))
                {
                    NavigateTo(navHit.position);
                }
                else
                {
                    Debug.LogWarning($"[MovementSandboxDriver] Clicked point {hit.point} is not near a valid NavMesh area.");
                }
            }
        }

        private void PickRandomNavMeshDestination()
        {
            Vector3 randomDirection = Random.insideUnitSphere * _randomWanderRadius;
            randomDirection += transform.position;

            if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, _randomWanderRadius, NavMesh.AllAreas))
            {
                Debug.Log($"[MovementSandboxDriver] Triggered random destination at {hit.position}.");
                NavigateTo(hit.position);
            }
        }

        private void NavigateTo(Vector3 destination)
        {
            NavMeshPath path = new NavMeshPath();
            bool pathCalculated = NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, path);

            if (pathCalculated && (path.status == NavMeshPathStatus.PathComplete || path.status == NavMeshPathStatus.PathPartial))
            {
                _lastCommandedDestination = destination;
                _hasTarget = true;

                Debug.Log($"[MovementSandboxDriver] Calculated path with {path.corners.Length} corners. Status: {path.status}. Passing to IAgentMotor.");
                _motor.SetPath(path.corners);
            }
            else
            {
                Debug.LogWarning($"[MovementSandboxDriver] Failed to calculate valid NavMesh path to {destination}. Status: {path.status}.");
            }
        }

        private void HandleArrived()
        {
            Debug.Log($"[MovementSandboxDriver] <color=green>SUCCESS: Agent arrived at destination {_lastCommandedDestination}!</color>");
            _hasTarget = false;
        }

        private void HandlePathBlocked()
        {
            Debug.LogWarning($"[MovementSandboxDriver] <color=red>ALERT: Agent reported OnPathBlocked at {transform.position}!</color>");
            _hasTarget = false;
        }

        #region Cross-Input-System Helpers

        private bool CheckLeftClick(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif
            return false;
        }

        private bool CheckRandomTrigger()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame))
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.R))
            {
                return true;
            }
#endif
            return false;
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            if (!_showTargetMarker || !_hasTarget) return;

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_lastCommandedDestination, 0.4f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, _lastCommandedDestination + Vector3.up * 0.5f);
        }

        #endregion
    }
}
