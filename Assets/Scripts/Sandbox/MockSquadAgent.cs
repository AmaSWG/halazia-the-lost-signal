using UnityEngine;
using UnityEngine.AI;
using Halazia.AI.Commander;
using Halazia.Shared.Movement;

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
        public bool IsAlive => _currentHealth > 0.001f;
        public bool IsEngagedWithPlayer => _currentTask == SquadTaskType.Pursue;
        public SquadTaskType CurrentTask => _currentTask;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();

            // Auto-register with Commander if present in scene
            var commander = FindAnyObjectByType<CommanderAgent>();
            if (commander != null)
            {
                commander.RegisterSquadMember(this);
            }
        }

        private void OnDestroy()
        {
            var commander = FindAnyObjectByType<CommanderAgent>();
            if (commander != null)
            {
                commander.UnregisterSquadMember(this);
            }
        }

        public void AssignOrder(SquadOrder order)
        {
            _activeOrder = order;
            _currentTask = order.TaskType;

            Debug.Log($"[MockSquadAgent:{_agentId}] Received order: {order.TaskType} to {order.TargetPosition}");

            // Calculate path and execute through IAgentMotor
            if (_motor != null)
            {
                NavMeshPath path = new NavMeshPath();
                if (NavMesh.CalculatePath(transform.position, order.TargetPosition, NavMesh.AllAreas, path))
                {
                    _motor.SetPath(path.corners);
                }
            }
        }

        public void CancelOrder()
        {
            _currentTask = SquadTaskType.Idle;
            if (_motor != null)
            {
                _motor.Stop();
            }
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
