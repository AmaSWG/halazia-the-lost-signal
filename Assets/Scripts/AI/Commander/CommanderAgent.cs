using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Halazia.Shared.Movement;

namespace Halazia.AI.Commander
{
    /// <summary>
    /// The Commander AI Agent (SE3062 Intelligent Systems individual component).
    /// Deliberative tactical coordinator that observes the 4-deck ship combat state,
    /// samples player escape routes into an adaptive Markov heatmap, and allocates conflict-free
    /// tasks to Sentinel, Hunter, and Flanker using a multi-criteria utility engine.
    /// Moves itself strictly via IAgentMotor to preserve architecture decoupling.
    /// </summary>
    [RequireComponent(typeof(AgentMotor))]
    [DisallowMultipleComponent]
    public class CommanderAgent : MonoBehaviour, ISquadAgent
    {
        [Header("Tactical Perception")]
        [Tooltip("Direct reference or transform of the player to observe.")]
        [SerializeField] private Transform _playerTransform;

        [Tooltip("Perception update frequency (seconds) to prevent frame drops across all 4 agents.")]
        [SerializeField] private float _decisionTickInterval = 0.5f;

        [Tooltip("Distance at which Commander prefers to oversee combat without engaging in close-quarters melee.")]
        [SerializeField] private float _preferredCommandDistance = 12.0f;

        [Header("Squad Registry")]
        [Tooltip("Manual list of squad agents, or automatically registered at runtime.")]
        [SerializeField] private List<MonoBehaviour> _initialSquadMembers = new List<MonoBehaviour>();

        [Header("Sub-Systems")]
        [SerializeField] private CorridorHeatmapTracker _heatmapTracker;

        // ISquadAgent implementation for the Commander itself
        public string AgentId => "Commander_01";
        public AgentRole Role => AgentRole.Commander;
        public Vector3 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _currentHealth > 0.001f;
        public bool IsEngagedWithPlayer => _isPlayerDetected;
        public SquadTaskType CurrentTask => _currentSelfTask;

        [Header("Commander Health & State")]
        [SerializeField] private float _maxHealth = 150f;
        private float _currentHealth = 150f;
        private SquadTaskType _currentSelfTask = SquadTaskType.Idle;

        // Squad tracking
        private readonly List<ISquadAgent> _registeredSquad = new List<ISquadAgent>();
        private SquadUtilityAllocator _utilityAllocator;
        private IAgentMotor _motor;

        // Combat perception state
        private bool _isPlayerDetected = false;
        private Vector3 _lastKnownPlayerPosition;
        private float _tickTimer = 0f;

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();
            _utilityAllocator = new SquadUtilityAllocator();

            if (_heatmapTracker == null)
            {
                _heatmapTracker = GetComponent<CorridorHeatmapTracker>() ?? FindAnyObjectByType<CorridorHeatmapTracker>();
            }

            // Register initial squad components
            foreach (var mono in _initialSquadMembers)
            {
                if (mono is ISquadAgent squadAgent)
                {
                    RegisterSquadMember(squadAgent);
                }
            }
        }

        private void Start()
        {
            if (_playerTransform == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                {
                    _playerTransform = playerObj.transform;
                }
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            _tickTimer += deltaTime;

            if (_tickTimer >= _decisionTickInterval)
            {
                _tickTimer = 0f;
                RunTacticalDecisionCycle();
            }
        }

        #region Squad Registry Management

        public void RegisterSquadMember(ISquadAgent agent)
        {
            if (agent != null && !_registeredSquad.Contains(agent) && agent != (ISquadAgent)this)
            {
                _registeredSquad.Add(agent);
                Debug.Log($"[CommanderAgent] Registered squad agent '{agent.AgentId}' (Role: {agent.Role}). Total squad size: {_registeredSquad.Count}");
            }
        }

        public void UnregisterSquadMember(ISquadAgent agent)
        {
            if (_registeredSquad.Contains(agent))
            {
                _registeredSquad.Remove(agent);
                Debug.Log($"[CommanderAgent] Unregistered squad agent '{agent.AgentId}'.");
            }
        }

        #endregion

        #region Tactical Deliberation & Allocation Cycle

        private void RunTacticalDecisionCycle()
        {
            // 1. Observe player state
            UpdatePlayerObservation();

            // 2. Generate required tactical orders based on game situation
            List<SquadOrder> orders = FormulateTacticalOrders();

            // 3. Solve optimal, conflict-free allocation
            if (_registeredSquad.Count > 0 && orders.Count > 0)
            {
                var assignments = _utilityAllocator.SolveOptimalAllocation(_registeredSquad, orders);

                foreach (var pair in assignments)
                {
                    ISquadAgent agent = pair.Key;
                    SquadOrder order = pair.Value;

                    // Issue order only if it represents a new directive
                    if (agent.CurrentTask != order.TaskType)
                    {
                        agent.AssignOrder(order);
                        Debug.Log($"[CommanderAgent] Dispatched order {order} to agent {agent.AgentId} ({agent.Role}).");
                    }
                }
            }

            // 4. Update Commander's own tactical positioning via IAgentMotor
            UpdateCommanderSelfMovement();
        }

        private void UpdatePlayerObservation()
        {
            if (_playerTransform != null)
            {
                _lastKnownPlayerPosition = _playerTransform.position;
                _isPlayerDetected = true;

                // Feed player location into the Markovian corridor heatmap
                if (_heatmapTracker != null)
                {
                    _heatmapTracker.SamplePlayerLocation(_lastKnownPlayerPosition);
                }
            }
        }

        /// <summary>
        /// Generates the menu of active tactical orders needed right now.
        /// Distinct tasks are generated to guarantee diversity:
        /// 1. Pursue (Frontal assault)
        /// 2. Flank (Side angle approach)
        /// 3. CoverRoute (Preempt predicted escape choke point)
        /// </summary>
        private List<SquadOrder> FormulateTacticalOrders()
        {
            var orders = new List<SquadOrder>();

            if (_isPlayerDetected)
            {
                // Order A: Direct frontal pursuit
                orders.Add(new SquadOrder(SquadTaskType.Pursue, _lastKnownPlayerPosition, priority: 1.0f));

                // Order B: Lateral flanking vector (offset from player facing)
                Vector3 flankOffset = Vector3.Cross(Vector3.up, (_lastKnownPlayerPosition - transform.position).normalized) * 7.0f;
                Vector3 flankPos = _lastKnownPlayerPosition + flankOffset;
                orders.Add(new SquadOrder(SquadTaskType.Flank, flankPos, priority: 0.9f));

                // Order C: Cover predicted escape corridor (adaptive intelligence)
                if (_heatmapTracker != null && _heatmapTracker.PredictLikelyEscapeNode(_lastKnownPlayerPosition, out var predictedNode, out float prob))
                {
                    orders.Add(new SquadOrder(
                        SquadTaskType.CoverRoute,
                        predictedNode.WorldPosition,
                        predictedNode.NodeId,
                        priority: 0.85f + (prob * 0.3f)));
                }
                else
                {
                    // Fallback to covering the highest overall historical corridor
                    var frequentNode = _heatmapTracker?.GetMostFrequentCorridor();
                    if (frequentNode != null)
                    {
                        orders.Add(new SquadOrder(SquadTaskType.CoverRoute, frequentNode.WorldPosition, frequentNode.NodeId, 0.75f));
                    }
                }
            }
            else
            {
                // In search mode: investigate last known disturbance
                orders.Add(new SquadOrder(SquadTaskType.Investigate, _lastKnownPlayerPosition, priority: 0.8f));
            }

            return orders;
        }

        /// <summary>
        /// Calculates tactical positioning for the Commander itself.
        /// Preserves command oversight distance, using IAgentMotor for smooth navigation.
        /// </summary>
        private void UpdateCommanderSelfMovement()
        {
            if (_motor == null) return;

            if (_isPlayerDetected)
            {
                // Maintain standoff command distance
                Vector3 awayFromPlayer = (transform.position - _lastKnownPlayerPosition).normalized;
                Vector3 desiredCommandPost = _lastKnownPlayerPosition + awayFromPlayer * _preferredCommandDistance;

                if (NavMesh.SamplePosition(desiredCommandPost, out NavMeshHit hit, 4.0f, NavMesh.AllAreas))
                {
                    float distToPost = Vector3.Distance(transform.position, hit.position);
                    if (distToPost > 2.5f && !_motor.IsMoving)
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path))
                        {
                            _motor.SetPath(path.corners);
                            _motor.SetFaceTarget(_playerTransform);
                        }
                    }
                }
            }
        }

        #endregion

        #region ISquadAgent Interface Methods

        public void AssignOrder(SquadOrder order)
        {
            _currentSelfTask = order.TaskType;
        }

        public void CancelOrder()
        {
            _currentSelfTask = SquadTaskType.Idle;
        }

        #endregion

        #region Editor Visualization

        private void OnDrawGizmos()
        {
            // Draw command radius around Commander
            Gizmos.color = new Color(1f, 0.84f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _preferredCommandDistance);

            // Draw line to observed player
            if (_playerTransform != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, _playerTransform.position + Vector3.up * 1.5f);
            }

            // Draw lines to active squad members
            Gizmos.color = Color.cyan;
            foreach (var agent in _registeredSquad)
            {
                if (agent != null && agent.IsAlive)
                {
                    Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, agent.Position + Vector3.up * 1.5f);
                }
            }
        }

        #endregion
    }
}
