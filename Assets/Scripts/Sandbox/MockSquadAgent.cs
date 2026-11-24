using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Halazia.AI.Commander;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

namespace Halazia.Sandbox
{
    /// <summary>
    /// Lightweight mock implementation of ISquadAgent for testing Commander task allocation
    /// in the sandbox scene before teammate scripts (Hunter, Flanker, Sentinel) are finished.
    /// Demonstrates full decoupled integration via IAgentMotor.
    ///
    /// Fix: now implements OnInvestigateComplete and simulates the full Investigate → Search → Patrol
    /// lifecycle so the Commander's investigation order management can be exercised in the sandbox.
    /// Set _searchDwellSeconds in the Inspector (default 5 s) to match the Sentinel's intended dwell.
    /// </summary>
    [RequireComponent(typeof(AgentMotor))]
    public class MockSquadAgent : MonoBehaviour, ISquadAgent
    {
        [Header("Agent Identity")]
        [SerializeField] private string _agentId = "Agent_01";
        [SerializeField] private AgentRole _role = AgentRole.Sentinel;
        [SerializeField] private float _maxHealth = 100f;

        [Header("State")]
        [SerializeField] private SquadTaskType _currentTask = SquadTaskType.Idle;

        [Header("Investigate / Search Simulation")]
        [Tooltip("Seconds the agent dwells at an Investigate destination before firing OnInvestigateComplete. Matches the real Sentinel Search timer.")]
        [SerializeField] private float _searchDwellSeconds = 5f;

        private float _currentHealth;
        private IAgentMotor _motor;
        private SquadOrder _activeOrder;
        private Coroutine _searchRoutine;

        public string AgentId => _agentId;
        public AgentRole Role => _role;
        public Vector3 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _currentHealth > 0.001f && gameObject.activeInHierarchy;
        public bool IsEngagedWithPlayer => _currentTask == SquadTaskType.Pursue;
        public SquadTaskType CurrentTask => _currentTask;

        // Reporting events — fired to push state back to the Commander
        public event Action<ISquadAgent> OnDied;
        public event Action<ISquadAgent> OnTaskArrived;
        public event Action<ISquadAgent> OnTaskBlocked;
        public event Action<ISquadAgent> OnTargetLost;

        /// <summary>
        /// Fix: fired after the Search dwell timer completes with no player contact.
        /// The Commander subscribes to this to close the Investigate order and restore Patrol.
        /// </summary>
        public event Action<ISquadAgent> OnInvestigateComplete;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();

            if (_motor != null)
            {
                _motor.OnArrived     += HandleMotorArrived;
                _motor.OnPathBlocked += HandleMotorBlocked;
            }
        }

        private void OnDestroy()
        {
            if (_motor != null)
            {
                _motor.OnArrived     -= HandleMotorArrived;
                _motor.OnPathBlocked -= HandleMotorBlocked;
            }
        }

        private void OnEnable()
        {
            var commander = FindAnyObjectByType<CommanderAgent>();
            if (commander != null)
            {
                commander.RegisterSquadMember(this);
            }
        }

        private void OnDisable()
        {
            // Fire OnDied before unregistering so Commander receives the event first
            if (IsAlive) OnDied?.Invoke(this);
            var commander = FindAnyObjectByType<CommanderAgent>();
            commander?.UnregisterSquadMember(this);

            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
            }
        }

        public void AssignOrder(SquadOrder order)
        {
            // Cancel any in-progress Search dwell if a new order interrupts
            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
            }

            _activeOrder = order;
            _currentTask = order.TaskType;

            Debug.Log($"[MockSquadAgent:{_agentId}] Received order: {order.TaskType} to {order.TargetPosition}");

            if (order.TaskType == SquadTaskType.Patrol || order.TargetPosition == Vector3.zero)
            {
                // Patrol with no specific destination: stay in place for sandbox purposes
                _motor?.Stop();
                Debug.Log($"[MockSquadAgent:{_agentId}] Resuming Patrol (no forced destination).");
                return;
            }

            ExecutePath(order.TargetPosition);
        }

        private void ExecutePath(Vector3 targetPosition)
        {
            if (_motor == null) return;

            Vector3 start = transform.position;
            Vector3 target = targetPosition;

            if (NavMesh.SamplePosition(start, out NavMeshHit startHit, 3.0f, NavMesh.AllAreas))
                start = startHit.position;

            if (NavMesh.SamplePosition(target, out NavMeshHit targetHit, 5.0f, NavMesh.AllAreas))
                target = targetHit.position;

            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(start, target, NavMesh.AllAreas, path))
            {
                if (path.corners != null && path.corners.Length > 0)
                {
                    _motor.SetPath(path.corners);
                    if (_currentTask == SquadTaskType.Pursue || _currentTask == SquadTaskType.Flank)
                    {
                        _motor.SetFaceTarget(target);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[MockSquadAgent:{_agentId}] NavMesh path failed to {target}. Reporting blocked.");
                OnTaskBlocked?.Invoke(this);
            }
        }

        /// <summary>
        /// Called by AgentMotor when the agent reaches the final waypoint.
        /// For Investigate orders: fires OnTaskArrived then starts the Search dwell coroutine.
        /// For all other orders: fires OnTaskArrived and returns to Idle.
        /// </summary>
        private void HandleMotorArrived()
        {
            OnTaskArrived?.Invoke(this);

            if (_currentTask == SquadTaskType.Investigate)
            {
                // Navigation complete — begin Search dwell. Do NOT change _currentTask yet;
                // it stays Investigate so the Commander's deduplication guard does not re-dispatch.
                Debug.Log($"[MockSquadAgent:{_agentId}] Arrived at investigate position. Starting {_searchDwellSeconds}s Search dwell.");
                _searchRoutine = StartCoroutine(SearchDwellRoutine());
            }
            else
            {
                Debug.Log($"[MockSquadAgent:{_agentId}] Arrived at {_currentTask} destination.");
                _currentTask = SquadTaskType.Idle;
            }
        }

        /// <summary>
        /// Simulates the Sentinel's 5-second Search behaviour. Waits _searchDwellSeconds then
        /// fires OnInvestigateComplete, signalling the Commander that the full cycle is done.
        /// </summary>
        private IEnumerator SearchDwellRoutine()
        {
            yield return new WaitForSeconds(_searchDwellSeconds);

            Debug.Log($"[MockSquadAgent:{_agentId}] Search dwell complete. No player found. Firing OnInvestigateComplete.");
            _currentTask = SquadTaskType.Idle;
            _searchRoutine = null;
            OnInvestigateComplete?.Invoke(this);
        }

        private void HandleMotorBlocked()
        {
            Debug.LogWarning($"[MockSquadAgent:{_agentId}] Path blocked on task {_currentTask}.");
            OnTaskBlocked?.Invoke(this);
        }

        public void CancelOrder()
        {
            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
            }
            _currentTask = SquadTaskType.Idle;
            _motor?.Stop();
        }

        /// <summary>
        /// Call this from your agent's health system when damage is taken.
        /// </summary>
        public void TakeDamage(float damage)
        {
            if (!IsAlive) return;
            _currentHealth = Mathf.Max(0f, _currentHealth - damage);
            if (_currentHealth <= 0.001f)
            {
                OnDied?.Invoke(this);
            }
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            Color roleColor = _role switch
            {
                AgentRole.Hunter   => Color.red,
                AgentRole.Flanker  => Color.yellow,
                AgentRole.Sentinel => Color.blue,
                _                  => Color.white
            };

            Gizmos.color = roleColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.25f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.3f, $"{_role}\n[{_currentTask}]");
#endif
        }
    }
}
