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
    /// braking curves, obstacle blockage, and combat animation triggers (Attack, Hit, Die)
    /// in an isolated sandbox environment.
    /// </summary>
    public class MovementSandboxDriver : MonoBehaviour
    {
        [Header("Target Components")]
        [Tooltip("Reference to the motor. If unassigned, attempts to find on this GameObject.")]
        [SerializeField] private AgentMotor _motor;

        [Tooltip("Reference to the animator driver. If unassigned, attempts to find on this GameObject.")]
        [SerializeField] private AgentAnimatorDriver _animatorDriver;

        [Header("Controls & Testing")]
        [Tooltip("Layer mask for ground raycasting when clicking to pick a destination.")]
        [SerializeField] private LayerMask _groundLayer = ~0;

        [Tooltip("Max radius when picking a random wander point on NavMesh.")]
        [SerializeField] private float _randomWanderRadius = 12f;

        [Header("Combat Simulation")]
        [Tooltip("Optional transform to look at while moving (tests strafing facing).")]
        [SerializeField] private Transform _combatFaceTarget;

        [Header("Debug Visualization")]
        [SerializeField] private bool _showTargetMarker = true;

        private Vector3 _lastCommandedDestination;
        private bool _hasTarget = false;
        private bool _isStrafing = false;

        private void Awake()
        {
            if (_motor == null)
            {
                _motor = GetComponent<AgentMotor>();
            }

            if (_animatorDriver == null)
            {
                _animatorDriver = GetComponent<AgentAnimatorDriver>() ?? GetComponentInChildren<AgentAnimatorDriver>();
            }
        }

        private void OnEnable()
        {
            if (_motor != null)
            {
                _motor.OnArrived += HandleArrived;
                _motor.OnPathBlocked += HandlePathBlocked;
            }

            if (_animatorDriver != null)
            {
                _animatorDriver.OnAttackHitWindow += HandleAttackHitWindow;
                _animatorDriver.OnAttackFinished += HandleAttackFinished;
            }
        }

        private void OnDisable()
        {
            if (_motor != null)
            {
                _motor.OnArrived -= HandleArrived;
                _motor.OnPathBlocked -= HandlePathBlocked;
            }

            if (_animatorDriver != null)
            {
                _animatorDriver.OnAttackHitWindow -= HandleAttackHitWindow;
                _animatorDriver.OnAttackFinished -= HandleAttackFinished;
            }
        }

        private void Update()
        {
            if (_motor == null) return;

            // 1. Left Click: Move to clicked NavMesh position
            if (CheckLeftClick(out Vector2 screenPosition))
            {
                ProcessClickToMove(screenPosition);
            }

            // 2. Space / R: Wander to random NavMesh point
            if (CheckWanderTrigger())
            {
                PickRandomNavMeshDestination();
            }

            // 3. Right Click / F: Trigger Attack animation & movement lock
            if (CheckAttackTrigger())
            {
                TriggerTestAttack();
            }

            // 4. H Key: Trigger Hit reaction animation
            if (CheckHitTrigger())
            {
                TriggerTestHit();
            }

            // 5. T Key: Toggle strafing face target (tests moving while looking at target)
            if (CheckToggleFaceTarget())
            {
                ToggleFaceTarget();
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

        [ContextMenu("1. Wander to Random NavMesh Point (Space / R)")]
        public void PickRandomNavMeshDestination()
        {
            Vector3 randomDirection = Random.insideUnitSphere * _randomWanderRadius;
            randomDirection.y = 0;
            randomDirection += transform.position;

            if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, _randomWanderRadius, NavMesh.AllAreas))
            {
                Debug.Log($"[MovementSandboxDriver] Triggered random destination at {hit.position}.");
                NavigateTo(hit.position);
            }
        }

        [ContextMenu("2. Walk Into Barricade (Test Blockage Detector)")]
        public void WalkIntoBarricadeMenu()
        {
            var barricade = GameObject.Find("Corridor_Barricade");
            if (barricade != null)
            {
                // Commanded point directly through / behind the barricade
                Vector3 target = barricade.transform.position + Vector3.forward * 2.5f;
                Debug.Log($"[MovementSandboxDriver] Commanding agent to walk through barricade at {target} to test blockage detection...");
                NavigateTo(target);
            }
        }

        [ContextMenu("3. Trigger Test Attack (F / Right-Click)")]
        public void TriggerTestAttack()
        {
            if (_animatorDriver != null)
            {
                Debug.Log("[MovementSandboxDriver] <color=orange>ACTION: Triggering Attack animation. Motor translation temporarily locked.</color>");
                _animatorDriver.TriggerAttack(0);
            }
        }

        [ContextMenu("4. Trigger Test Hit Reaction (H)")]
        public void TriggerTestHit()
        {
            if (_animatorDriver != null)
            {
                Debug.Log("[MovementSandboxDriver] <color=yellow>ACTION: Triggering Hit reaction animation.</color>");
                _animatorDriver.TriggerHit();
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

                Debug.Log($"[MovementSandboxDriver] Path calculated ({path.corners.Length} corners, status: {path.status}). Passing to IAgentMotor.");
                _motor.SetPath(path.corners);
            }
            else
            {
                Debug.LogWarning($"[MovementSandboxDriver] Failed to calculate NavMesh path to {destination}. Status: {path.status}.");
            }
        }

        private void ToggleFaceTarget()
        {
            if (_combatFaceTarget == null)
            {
                Debug.LogWarning("[MovementSandboxDriver] Assign 'Combat Face Target' in inspector to test strafing orientation!");
                return;
            }

            _isStrafing = !_isStrafing;
            if (_isStrafing)
            {
                Debug.Log($"[MovementSandboxDriver] <color=cyan>STRAFING ENABLED: Motor will maintain gaze at {_combatFaceTarget.name} while following path.</color>");
                _motor.SetFaceTarget(_combatFaceTarget);
            }
            else
            {
                Debug.Log("[MovementSandboxDriver] <color=cyan>STRAFING DISABLED: Motor restored to natural movement facing.</color>");
                _motor.SetFaceTarget((Transform)null);
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

        private void HandleAttackHitWindow()
        {
            Debug.Log("[MovementSandboxDriver] <color=red>HIT EVENT: Sword impact / weapon discharge frame reached!</color>");
        }

        private void HandleAttackFinished()
        {
            Debug.Log("[MovementSandboxDriver] <color=lime>ATTACK FINISHED: Motor translation restored.</color>");
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

        private bool CheckAttackTrigger()
        {
#if ENABLE_INPUT_SYSTEM
            if ((Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) ||
                (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.F))
            {
                return true;
            }
#endif
            return false;
        }

        private bool CheckWanderTrigger()
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

        private bool CheckHitTrigger()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.H))
            {
                return true;
            }
#endif
            return false;
        }

        private bool CheckToggleFaceTarget()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.T))
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
