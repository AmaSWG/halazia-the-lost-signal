using System;
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
    /// </summary>
    [RequireComponent(typeof(AgentMotor))]
    public class MockSquadAgent : MonoBehaviour, ISquadAgent
    {
        [Header("Agent Identity")]
        [SerializeField] private string _agentId = "Agent_01";
        [SerializeField] private AgentRole _role = AgentRole.Hunter;
        [SerializeField] private float _maxHealth = 100f;

        [Header("State")]
        [SerializeField] private SquadTaskType _currentTask = SquadTaskType.Idle;

        private float _currentHealth;
        private IAgentMotor _motor;
        private SquadOrder _activeOrder;

        public string AgentId => _agentId;
        public AgentRole Role => _role;
        public Vector3 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _currentHealth > 0.001f && gameObject.activeInHierarchy;
        public bool IsEngagedWithPlayer => _currentTask == SquadTaskType.Pursue;
        public SquadTaskType CurrentTask => _currentTask;

        // Fix 3: Reporting events — fired to push state back to the Commander
        public event Action<ISquadAgent> OnDied;
        public event Action<ISquadAgent> OnTaskArrived;
        public event Action<ISquadAgent> OnTaskBlocked;
        public event Action<ISquadAgent> OnTargetLost;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();
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
        }

        private void OnDestroy()
        {
            // Guard: only fire if not already fired by OnDisable
            if (!IsAlive) return;
            OnDied?.Invoke(this);
            var commander = FindAnyObjectByType<CommanderAgent>();
            commander?.UnregisterSquadMember(this);
        }

        private void Update()
        {
            if (_currentTask == SquadTaskType.Idle) return;
            if (_motor == null) return;

            if (_activeOrder.TargetPosition != Vector3.zero && !_motor.IsMoving)
            {
                float dist = Vector3.Distance(transform.position, _activeOrder.TargetPosition);
                if (dist <= 1.8f)
                {
                    // Arrived at destination
                    OnTaskArrived?.Invoke(this);
                    _currentTask = SquadTaskType.Idle;
                }
                else if (_currentTask == SquadTaskType.Pursue || _currentTask == SquadTaskType.Flank ||
                         _currentTask == SquadTaskType.Investigate || _currentTask == SquadTaskType.CoverRoute)
                {
                    // Stopped but not arrived: retry or report blocked
                    ExecutePath(_activeOrder.TargetPosition);
                }
            }
        }

        public void AssignOrder(SquadOrder order)
        {
            _activeOrder = order;
            _currentTask = order.TaskType;

            Debug.Log($"[MockSquadAgent:{_agentId}] Received order: {order.TaskType} to {order.TargetPosition}");

            ExecutePath(order.TargetPosition);
        }

        private void ExecutePath(Vector3 targetPosition)
        {
            if (_motor == null) return;

            Vector3 start = transform.position;
            Vector3 target = targetPosition;

            if (NavMesh.SamplePosition(start, out NavMeshHit startHit, 3.0f, NavMesh.AllAreas))
            {
                start = startHit.position;
            }

            if (NavMesh.SamplePosition(target, out NavMeshHit targetHit, 5.0f, NavMesh.AllAreas))
            {
                target = targetHit.position;
            }

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
        }

        public void CancelOrder()
        {
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

        /// <summary>Fires OnPathBlocked so the Commander reassigns this agent.</summary>
        private void OnPathBlockedHandler()
        {
            OnTaskBlocked?.Invoke(this);
        }

        private void OnDrawGizmos()
        {
            // Draw current role and task over the agent
#if UNITY_EDITOR
            Color roleColor = _role switch
            {
                AgentRole.Hunter => Color.red,
                AgentRole.Flanker => Color.yellow,
                AgentRole.Sentinel => Color.blue,
                _ => Color.white
            };

            Gizmos.color = roleColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.25f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.3f, $"{_role}\n[{_currentTask}]");
#endif
        }
    }
}
