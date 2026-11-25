using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Halazia.AI.Commander;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

namespace Halazia.Sandbox
{
    /// <summary>Sandbox squad agent using AgentMotor and an Investigate search dwell.</summary>
    [RequireComponent(typeof(AgentMotor))]
    public class MockSquadAgent : MonoBehaviour, Halazia.Shared.Squad.ISquadAgent
    {
        [Header("Agent Identity")]
        [SerializeField] private string _agentId = "Agent_01";
        [SerializeField] private AgentRole _role = AgentRole.Sentinel;
        [SerializeField] private float _maxHealth = 100f;
        [Header("State")]
        [SerializeField] private SquadTaskType _currentTask = SquadTaskType.Idle;
        [SerializeField, Min(0f)] private float _searchDwellSeconds = 5f;

        private float _currentHealth;
        private IAgentMotor _motor;
        private bool _deathReported, _arrivedReported;
        private Coroutine _searchRoutine;
        private int _orderVersion;
        private CommanderAgent _commander;

        public string AgentId => _agentId;
        public AgentRole Role => _role;
        public Vector3 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _currentHealth > 0.001f && gameObject.activeInHierarchy;
        public bool IsEngagedWithPlayer => _currentTask == SquadTaskType.Pursue;
        public SquadTaskType CurrentTask => _currentTask;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnDied;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnTaskArrived;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnTaskBlocked;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnTargetLost;
        public event Action<Halazia.Shared.Squad.ISquadAgent> OnInvestigateComplete;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();
        }

        private void OnEnable()
        {
            if (_motor != null)
            {
                _motor.OnArrived += HandleMotorArrived;
                _motor.OnPathBlocked += HandleMotorBlocked;
            }
            RegisterWithCommander();
        }

        private void Start() => RegisterWithCommander();

        private void RegisterWithCommander()
        {
            if (_commander == null) _commander = FindAnyObjectByType<CommanderAgent>();
            if (_commander != null) _commander.RegisterSquadMember(this);
        }

        private void OnDisable()
        {
            if (_motor != null)
            {
                _motor.OnArrived -= HandleMotorArrived;
                _motor.OnPathBlocked -= HandleMotorBlocked;
            }
            CancelOrder();
            if (_commander != null) _commander.UnregisterSquadMember(this);
        }

        private void StopSearch()
        {
            if (_searchRoutine == null) return;
            StopCoroutine(_searchRoutine);
            _searchRoutine = null;
        }

        public void AssignOrder(SquadOrder order)
        {
            StopSearch();
            _orderVersion++;
            _currentTask = order.TaskType;
            _arrivedReported = false;
            Debug.Log($"[MockSquadAgent:{_agentId}] Received order: {order.TaskType} to {order.TargetPosition}");
            if (order.TaskType == SquadTaskType.Idle || order.TaskType == SquadTaskType.Patrol)
            {
                // Mock agents have no waypoint patrol route.
                _motor?.Stop();
                return;
            }
            ExecutePath(order.TargetPosition);
        }

        public void AcceptOrder(SquadOrder order) => AssignOrder(order);

        private void ExecutePath(Vector3 targetPosition)
        {
            if (_motor == null) return;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, 3f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(targetPosition, out NavMeshHit targetHit, 5f, NavMesh.AllAreas))
            {
                HandleMotorBlocked();
                return;
            }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, targetHit.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length == 0)
            {
                HandleMotorBlocked();
                return;
            }
            if (_currentTask == SquadTaskType.Pursue || _currentTask == SquadTaskType.Flank)
                _motor.SetFaceTarget(targetHit.position);
            _motor.SetPath(path.corners);
        }

        private void HandleMotorArrived()
        {
            if (!IsAlive || _arrivedReported || _currentTask == SquadTaskType.Idle ||
                _currentTask == SquadTaskType.Patrol) return;
            _arrivedReported = true;
            int version = _orderVersion;
            Debug.Log($"[MockSquadAgent:{_agentId}] Arrived at {_currentTask} destination.");
            OnTaskArrived?.Invoke(this);
            if (version != _orderVersion || !isActiveAndEnabled || !IsAlive) return;
            if (_currentTask == SquadTaskType.Investigate)
                _searchRoutine = StartCoroutine(SearchDwellRoutine(version));
            else
                _currentTask = SquadTaskType.Idle;
        }

        private IEnumerator SearchDwellRoutine(int version)
        {
            yield return new WaitForSeconds(_searchDwellSeconds);
            _searchRoutine = null;
            if (!IsAlive || version != _orderVersion || _currentTask != SquadTaskType.Investigate)
                yield break;
            // Commander requires the task to remain Investigate during this callback.
            OnInvestigateComplete?.Invoke(this);
            if (version == _orderVersion && _currentTask == SquadTaskType.Investigate)
                _currentTask = SquadTaskType.Idle;
        }

        private void HandleMotorBlocked()
        {
            if (!IsAlive) return;
            StopSearch();
            Debug.LogWarning($"[MockSquadAgent:{_agentId}] Path blocked on task {_currentTask}.");
            OnTaskBlocked?.Invoke(this);
        }

        public void CancelOrder()
        {
            StopSearch();
            _orderVersion++;
            _currentTask = SquadTaskType.Idle;
            _arrivedReported = false;
            _motor?.Stop();
        }

        public void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0f) return;
            _currentHealth = Mathf.Max(0f, _currentHealth - damage);
            if (_currentHealth <= 0.001f && !_deathReported)
            {
                _deathReported = true;
                CancelOrder();
                OnDied?.Invoke(this);
            }
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            Gizmos.color = _role switch
            {
                AgentRole.Hunter => Color.red,
                AgentRole.Flanker => Color.yellow,
                AgentRole.Sentinel => Color.blue,
                _ => Color.white
            };
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.25f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.3f, $"{_role}\n[{_currentTask}]");
#endif
        }
    }
}