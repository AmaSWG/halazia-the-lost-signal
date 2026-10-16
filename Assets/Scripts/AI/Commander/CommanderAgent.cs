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

        [Header("Self-Defense & Combat")]
        [Tooltip("Distance threshold at which Commander defends itself directly against the player.")]
        [SerializeField] private float _meleeAttackDistance = 2.8f;

        [Tooltip("Cooldown period between self-defense attacks (seconds).")]
        [SerializeField] private float _attackCooldown = 1.2f;

        private float _lastAttackTime = -10f;
        private bool _isInMeleeDefenseMode = false;

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
        private readonly Dictionary<ISquadAgent, Vector3> _lastDispatchedPositions = new Dictionary<ISquadAgent, Vector3>();

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

            // Continuous close-quarters self-defense check against player
            CheckCloseQuartersSelfDefense();

            if (_tickTimer >= _decisionTickInterval)
            {
                _tickTimer = 0f;
                RunTacticalDecisionCycle();
            }
        }

        private void CheckCloseQuartersSelfDefense()
        {
            if (_playerTransform == null) return;

            float distToPlayer = Vector3.Distance(transform.position, _playerTransform.position);
            if (distToPlayer <= _meleeAttackDistance)
            {
                _isInMeleeDefenseMode = true;
                _motor?.SetFaceTarget(_playerTransform);

                if (Time.time - _lastAttackTime >= _attackCooldown)
                {
                    _lastAttackTime = Time.time;
                    ExecuteSelfDefenseAttack();
                }
            }
            else
            {
                _isInMeleeDefenseMode = false;
            }
        }

        private void ExecuteSelfDefenseAttack()
        {
            Debug.LogWarning($"[CommanderAgent] ⚔️ Player in close quarters ({Vector3.Distance(transform.position, _playerTransform.position):F1}m)! Commander executes tactical defense strike!");

            var driver = GetComponent<AgentAnimatorDriver>();
            if (driver != null)
            {
                driver.TriggerAttack();
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

            // 2. Ensure squad members are discovered if registry was reset
            EnsureSquadRegistered();

            // 3. Generate required tactical orders based on game situation
            List<SquadOrder> orders = FormulateTacticalOrders();

            // 4. Filter only currently active and alive squad members (non-destructive)
            var activeSquad = new List<ISquadAgent>();
            foreach (var agent in _registeredSquad)
            {
                if (agent != null && agent.IsAlive)
                {
                    if (agent is MonoBehaviour mb && mb != null && mb.gameObject.activeInHierarchy)
                    {
                        activeSquad.Add(agent);
                    }
                }
            }

            if (activeSquad.Count > 0 && orders.Count > 0)
            {
                var assignments = _utilityAllocator.SolveOptimalAllocation(activeSquad, orders);

                foreach (var pair in assignments)
                {
                    ISquadAgent agent = pair.Key;
                    SquadOrder order = pair.Value;

                    bool taskChanged = agent.CurrentTask != order.TaskType;
                    bool targetMoved = !_lastDispatchedPositions.TryGetValue(agent, out Vector3 lastPos) 
                                       || Vector3.Distance(lastPos, order.TargetPosition) > 0.75f;

                    if (taskChanged || targetMoved)
                    {
                        _lastDispatchedPositions[agent] = order.TargetPosition;
                        agent.AssignOrder(order);
                        Debug.Log($"[CommanderAgent] Dispatched order {order} to agent {agent.AgentId} ({agent.Role}).");
                    }
                }
            }

            // 5. Update Commander's own tactical positioning via IAgentMotor
            UpdateCommanderSelfMovement();
        }

        private void EnsureSquadRegistered()
        {
            if (_registeredSquad.Count == 0)
            {
                var found = FindObjectsOfType<MonoBehaviour>();
                foreach (var mb in found)
                {
                    if (mb is ISquadAgent squadAgent && (MonoBehaviour)squadAgent != this)
                    {
                        RegisterSquadMember(squadAgent);
                    }
                }
            }
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
                // Order A: Direct frontal pursuit (Top priority, direct combat pressure)
                orders.Add(new SquadOrder(SquadTaskType.Pursue, _lastKnownPlayerPosition, priority: 1.0f));

                // Order B: Lateral flanking vector (Tactical angle)
                Vector3 flankOffset = Vector3.Cross(Vector3.up, (_lastKnownPlayerPosition - transform.position).normalized) * 7.0f;
                Vector3 flankPos = _lastKnownPlayerPosition + flankOffset;
                orders.Add(new SquadOrder(SquadTaskType.Flank, flankPos, priority: 0.80f));

                // Order C: Cover predicted escape corridor (Preemptive hold, subordinate to direct combat)
                if (_heatmapTracker != null && _heatmapTracker.PredictLikelyEscapeNode(_lastKnownPlayerPosition, out var predictedNode, out float prob))
                {
                    orders.Add(new SquadOrder(
                        SquadTaskType.CoverRoute,
                        predictedNode.WorldPosition,
                        predictedNode.NodeId,
                        priority: 0.65f + (prob * 0.12f)));
                }
                else
                {
                    // Fallback to covering the highest overall historical corridor
                    var frequentNode = _heatmapTracker?.GetMostFrequentCorridor();
                    if (frequentNode != null)
                    {
                        orders.Add(new SquadOrder(SquadTaskType.CoverRoute, frequentNode.WorldPosition, frequentNode.NodeId, 0.60f));
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
            if (_motor == null || _playerTransform == null) return;

            float distToPlayer = Vector3.Distance(transform.position, _playerTransform.position);

            // In close-quarters melee combat: stop running and plant feet to face enemy directly
            if (distToPlayer <= _meleeAttackDistance)
            {
                if (_motor.IsMoving)
                {
                    _motor.Stop();
                }
                _motor.SetFaceTarget(_playerTransform);
                return;
            }

            if (_isPlayerDetected)
            {
                // Reposition to maintain standoff oversight distance
                Vector3 awayDir = (transform.position - _lastKnownPlayerPosition);
                awayDir.y = 0;
                if (awayDir.sqrMagnitude < 0.01f) awayDir = -transform.forward;
                awayDir.Normalize();

                Vector3 bestPost = Vector3.zero;
                bool foundPost = false;

                // Test straight back, 45 deg angles, and lateral strafes to prevent getting stuck against deck walls/edges
                float[] angleOffsets = { 0f, 45f, -45f, 90f, -90f, 135f, -135f };
                foreach (float angle in angleOffsets)
                {
                    Vector3 candidateDir = Quaternion.Euler(0, angle, 0) * awayDir;
                    Vector3 candidatePost = _lastKnownPlayerPosition + candidateDir * _preferredCommandDistance;

                    if (NavMesh.SamplePosition(candidatePost, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                    {
                        bestPost = hit.position;
                        foundPost = true;
                        break;
                    }
                }

                if (foundPost)
                {
                    float distToPost = Vector3.Distance(transform.position, bestPost);
                    if (distToPost > 2.0f && !_motor.IsMoving)
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, bestPost, NavMesh.AllAreas, path))
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

            // Draw close-quarters melee defense zone
            if (_isInMeleeDefenseMode)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.0f, _meleeAttackDistance);
            }
            else
            {
                Gizmos.color = new Color(1f, 0.35f, 0f, 0.35f);
                Gizmos.DrawWireSphere(transform.position, _meleeAttackDistance);
            }

            // Draw line to observed player
            if (_playerTransform != null)
            {
                Gizmos.color = _isInMeleeDefenseMode ? Color.red : Color.yellow;
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
