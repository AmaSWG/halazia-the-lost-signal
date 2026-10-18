
using System;
using UnityEngine;
using UnityEngine.AI;
using Halazia.AI.Commander;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

namespace Halazia.Sandbox
{
    /// <summary>
    /// Lightweight mock implementation of ISquadAgent for testing
    /// Commander task allocation in the sandbox scene.
    /// Uses IAgentMotor for movement.
    /// </summary>
    [RequireComponent(typeof(AgentMotor))]
    public class MockSquadAgent : MonoBehaviour, Halazia.AI.Commander.ISquadAgent
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
        private bool _deathReported;

        public string AgentId => _agentId;
        public AgentRole Role => _role;
        public Vector3 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsAlive =>
            _currentHealth > 0.001f && gameObject.activeInHierarchy;

        public bool IsEngagedWithPlayer =>
            _currentTask == SquadTaskType.Pursue;

        public SquadTaskType CurrentTask => _currentTask;

        // These events must match the Shared ISquadAgent interface.
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnDied;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnTaskArrived;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnTaskBlocked;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnTargetLost;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();
            _deathReported = false;
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
            var commander = FindAnyObjectByType<CommanderAgent>();
            commander?.UnregisterSquadMember(this);
        }

        private void OnDestroy()
        {
            var commander = FindAnyObjectByType<CommanderAgent>();
            commander?.UnregisterSquadMember(this);
        }

        private void Update()
        {
            if (_currentTask == SquadTaskType.Idle)
                return;

            if (_motor == null)
                return;

            if (_activeOrder.TargetPosition != Vector3.zero &&
                !_motor.IsMoving)
            {
                float distance = Vector3.Distance(
                    transform.position,
                    _activeOrder.TargetPosition
                );

                if (distance <= 1.8f)
                {
                    _currentTask = SquadTaskType.Idle;
                    OnTaskArrived?.Invoke(this);
                }
                else if (
                    _currentTask == SquadTaskType.Pursue ||
                    _currentTask == SquadTaskType.Flank ||
                    _currentTask == SquadTaskType.Investigate ||
                    _currentTask == SquadTaskType.CoverRoute)
                {
                    ExecutePath(_activeOrder.TargetPosition);
                }
            }
        }

        public void AssignOrder(SquadOrder order)
        {
            _activeOrder = order;
            _currentTask = order.TaskType;

            Debug.Log(
                $"[MockSquadAgent:{_agentId}] Received order: " +
                $"{order.TaskType} to {order.TargetPosition}"
            );

            ExecutePath(order.TargetPosition);
        }

        // Optional convenience method.
        // The shared interface also provides a default implementation.
        public void AcceptOrder(SquadOrder order)
        {
            AssignOrder(order);
        }

        private void ExecutePath(Vector3 targetPosition)
        {
            if (_motor == null)
                return;

            Vector3 start = transform.position;
            Vector3 target = targetPosition;

            if (NavMesh.SamplePosition(
                start,
                out NavMeshHit startHit,
                3.0f,
                NavMesh.AllAreas))
            {
                start = startHit.position;
            }

            if (NavMesh.SamplePosition(
                target,
                out NavMeshHit targetHit,
                5.0f,
                NavMesh.AllAreas))
            {
                target = targetHit.position;
            }

            NavMeshPath path = new NavMeshPath();

            if (NavMesh.CalculatePath(
                start,
                target,
                NavMesh.AllAreas,
                path))
            {
                if (path.corners != null &&
                    path.corners.Length > 0)
                {
                    _motor.SetPath(path.corners);

                    if (_currentTask == SquadTaskType.Pursue ||
                        _currentTask == SquadTaskType.Flank)
                    {
                        _motor.SetFaceTarget(target);
                    }
                }
                else
                {
                    OnTaskBlocked?.Invoke(this);
                }
            }
            else
            {
                OnTaskBlocked?.Invoke(this);
            }
        }

        public void CancelOrder()
        {
            _currentTask = SquadTaskType.Idle;
            _motor?.Stop();
        }

        public void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0f)
                return;

            _currentHealth = Mathf.Max(
                0f,
                _currentHealth - damage
            );

            if (_currentHealth <= 0.001f &&
                !_deathReported)
            {
                _deathReported = true;
                OnDied?.Invoke(this);
            }
        }

        private void OnPathBlockedHandler()
        {
            OnTaskBlocked?.Invoke(this);
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            Color roleColor = _role switch
            {
                AgentRole.Hunter => Color.red,
                AgentRole.Flanker => Color.yellow,
                AgentRole.Sentinel => Color.blue,
                _ => Color.white
            };

            Gizmos.color = roleColor;

            Gizmos.DrawWireSphere(
                transform.position + Vector3.up * 2f,
                0.25f
            );

            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 2.3f,
                $"{_role}\n[{_currentTask}]"
            );
#endif
        }
    }
}
