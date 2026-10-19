using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Halazia.Shared.Combat;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

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

        [Header("Perception & Line of Sight")]
        [Tooltip("Maximum sight range for Commander's direct vision.")]
        [SerializeField] private float _visionRange = 22.0f;

        [Tooltip("Field of view angle (degrees) for Commander's forward vision cone.")]
        [SerializeField] private float _visionFovAngle = 120.0f;

        [Tooltip("Physics layer mask for line-of-sight obstruction (walls, barricades, bulkheads).")]
        [SerializeField] private LayerMask _obstacleMask = ~0;

        [Tooltip("Duration in seconds that combat state persists after last sighting.")]
        [SerializeField] private float _combatWindowSeconds = 8.0f;

        [Tooltip("If true, Commander strictly requires direct Line of Sight or a squad report to locate the player. If false, Commander tracks player position directly (useful for rapid sandbox testing).")]
        [SerializeField] private bool _requireLineOfSightOrSquadReport = true;

        [Header("Self-Defense & Combat")]
        [Tooltip("Distance at which Commander executes a defensive melee strike (highest priority, ≤ close-quarters range).")]
        [SerializeField] private float _meleeAttackDistance = 2.5f;

        [Tooltip("Distance below which Commander halts movement and enters close-quarters defense. Melee fires when cooldown allows.")]
        [SerializeField] private float _closeQuartersDistance = 2.8f;

        [Tooltip("Distance below which Commander begins active tactical retreat away from the player.")]
        [SerializeField] private float _retreatTriggerDistance = 8.0f;

        [Tooltip("Cooldown period between self-defense melee strikes (seconds).")]
        [SerializeField] private float _attackCooldown = 1.2f;

        private float _lastAttackTime = -10f;

        /// <summary>
        /// Four mutually exclusive behaviour modes evaluated in strict priority order every frame.
        /// MeleeDefense (1) → CloseQuartersDefense (2) → Retreat (3) → TacticalOversight (4).
        /// </summary>
        private enum CommanderBehaviourMode { TacticalOversight, Retreat, CloseQuartersDefense, MeleeDefense }
        private CommanderBehaviourMode _behaviourMode = CommanderBehaviourMode.TacticalOversight;
        private CommanderBehaviourMode _previousBehaviourMode = CommanderBehaviourMode.TacticalOversight;

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
        private ICombatActions _combatActions;   // Fix 1: resolved at Awake, never AgentAnimatorDriver directly

        // Combat perception state
        private bool _isPlayerDetected = false;
        private Vector3 _lastKnownPlayerPosition;
        private float _timeSincePlayerDetected = 999f;
        private float _tickTimer = 0f;
        private readonly Dictionary<ISquadAgent, Vector3> _lastDispatchedPositions = new Dictionary<ISquadAgent, Vector3>();

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();

            // Fix 1: Resolve combat actions via interface — decoupled from AgentAnimatorDriver
            _combatActions = GetComponent<ICombatActions>();
            if (_combatActions == null)
                Debug.LogWarning("[CommanderAgent] No ICombatActions found on this GameObject. Add a CombatActionsAdapter component.");

            _utilityAllocator = new SquadUtilityAllocator();

            if (_heatmapTracker == null)
            {
                _heatmapTracker = GetComponent<CorridorHeatmapTracker>() ?? FindAnyObjectByType<CorridorHeatmapTracker>();
            }

            // Fix 3: Register initial squad and subscribe to their reporting events
            foreach (var mono in _initialSquadMembers)
            {
                if (mono is ISquadAgent squadAgent)
                    RegisterSquadMember(squadAgent);
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

            // Combat window countdown: exit combat mode if no sighting within _combatWindowSeconds (8s)
            if (_isPlayerDetected)
            {
                _timeSincePlayerDetected += deltaTime;
                if (_timeSincePlayerDetected >= _combatWindowSeconds)
                {
                    _isPlayerDetected = false;
                    Debug.Log($"[CommanderAgent] Combat window expired ({_combatWindowSeconds:F1}s without sighting). Squad transitioning to patrol/search.");
                }
            }

            // Evaluate mutually exclusive behaviour mode each frame and execute per-frame actions
            EvaluateAndExecuteBehaviourMode();

            if (_tickTimer >= _decisionTickInterval)
            {
                _tickTimer = 0f;
                RunTacticalDecisionCycle();
            }
        }

        /// <summary>
        /// Evaluates which mutually exclusive behaviour mode applies this frame (Priority 1 → 4)
        /// and executes per-frame actions for combat modes (halt, face, attack).
        /// Movement for Retreat and TacticalOversight is handled in the 0.5 s decision tick.
        /// </summary>
        private void EvaluateAndExecuteBehaviourMode()
        {
            if (_playerTransform == null) return;

            float distToPlayer = Vector3.Distance(transform.position, _playerTransform.position);
            bool meleeAvailable = (Time.time - _lastAttackTime) >= _attackCooldown;

            // Determine new mode (strictly prioritised)
            CommanderBehaviourMode newMode;

            if (distToPlayer <= _meleeAttackDistance && meleeAvailable)
                newMode = CommanderBehaviourMode.MeleeDefense;
            else if (distToPlayer <= _closeQuartersDistance)
                newMode = CommanderBehaviourMode.CloseQuartersDefense;
            else if (_isPlayerDetected && distToPlayer <= _retreatTriggerDistance)
                newMode = CommanderBehaviourMode.Retreat;
            else
                newMode = CommanderBehaviourMode.TacticalOversight;

            // Fix 2: Handle mode transitions — enables/disables strafe mode exactly once per transition
            if (newMode != _previousBehaviourMode)
            {
                HandleBehaviourModeTransition(_previousBehaviourMode, newMode);
                _previousBehaviourMode = newMode;
            }
            _behaviourMode = newMode;

            // Execute per-frame actions for combat modes
            switch (_behaviourMode)
            {
                case CommanderBehaviourMode.MeleeDefense:
                    if (_motor != null && _motor.IsMoving) _motor.Stop();
                    _motor?.SetFaceTarget(_playerTransform);
                    _lastAttackTime = Time.time;
                    ExecuteSelfDefenseAttack();
                    break;

                case CommanderBehaviourMode.CloseQuartersDefense:
                    if (_motor != null && _motor.IsMoving) _motor.Stop();
                    _motor?.SetFaceTarget(_playerTransform);
                    break;

                case CommanderBehaviourMode.Retreat:
                    // Face the player; movement path issued in UpdateCommanderSelfMovement on the 0.5 s tick
                    _motor?.SetFaceTarget(_playerTransform);
                    break;

                case CommanderBehaviourMode.TacticalOversight:
                    // No per-frame facing override; UpdateCommanderSelfMovement handles positioning
                    break;
            }
        }

        /// <summary>
        /// Called exactly once when behaviour mode changes.
        /// Enables strafe on entering Retreat; clears strafe on leaving it so the agent
        /// never gets stuck in a strafing state.
        /// </summary>
        private void HandleBehaviourModeTransition(CommanderBehaviourMode from, CommanderBehaviourMode to)
        {
            // Always clear strafe when leaving Retreat
            if (from == CommanderBehaviourMode.Retreat)
            {
                _motor?.SetStrafe(false);
            }

            // Enable strafe when entering Retreat (face player, move backward toward escape point)
            if (to == CommanderBehaviourMode.Retreat)
            {
                _motor?.SetStrafe(true, _playerTransform);
            }

            // Ensure movement stops cleanly when entering close-combat modes
            if (to == CommanderBehaviourMode.MeleeDefense || to == CommanderBehaviourMode.CloseQuartersDefense)
            {
                _motor?.SetStrafe(false);   // Strafe always cleared on any combat entry
            }
        }

        private void ExecuteSelfDefenseAttack()
        {
            Debug.Log($"[CommanderAgent] ⚔️ Player in close quarters ({Vector3.Distance(transform.position, _playerTransform.position):F1}m)! Commander executes tactical defense strike!");

            // Fix 1: Uses ICombatActions — no reference to AgentAnimatorDriver
            _combatActions?.Attack();
        }

        #region Squad Registry Management

        /// <summary>
        /// Registers a squad member and subscribes to all its reporting events.
        /// Safe to call at runtime (e.g. when a new agent spawns mid-mission).
        /// </summary>
        public void RegisterSquadMember(ISquadAgent agent)
        {
            if (agent == null || agent == (ISquadAgent)this || _registeredSquad.Contains(agent)) return;

            _registeredSquad.Add(agent);

            // Fix 3: Subscribe to reporting events so Commander reacts rather than polling
            agent.OnDied        += OnAgentDied;
            agent.OnTaskBlocked += OnAgentTaskBlocked;
            agent.OnTaskArrived += OnAgentTaskArrived;
            agent.OnTargetLost  += OnAgentTargetLost;

            Debug.Log($"[CommanderAgent] Registered '{agent.AgentId}' ({agent.Role}). Squad size: {_registeredSquad.Count}");
        }

        /// <summary>
        /// Unregisters a squad member and unsubscribes from all events.
        /// Call when an agent is destroyed so the Commander holds no dead references.
        /// </summary>
        public void UnregisterSquadMember(ISquadAgent agent)
        {
            if (agent == null || !_registeredSquad.Contains(agent)) return;

            // Unsubscribe before removing to prevent dangling event handlers
            agent.OnDied        -= OnAgentDied;
            agent.OnTaskBlocked -= OnAgentTaskBlocked;
            agent.OnTaskArrived -= OnAgentTaskArrived;
            agent.OnTargetLost  -= OnAgentTargetLost;

            _registeredSquad.Remove(agent);
            _lastDispatchedPositions.Remove(agent);
            Debug.Log($"[CommanderAgent] Unregistered '{agent.AgentId}'. Squad size: {_registeredSquad.Count}");
        }

        // ── Event reactions ──────────────────────────────────────────────────────

        private void OnAgentDied(ISquadAgent agent)
        {
            Debug.Log($"[CommanderAgent] Agent '{agent.AgentId}' died. Removing from roster and forcing reallocation.");
            UnregisterSquadMember(agent);
            // Force a reallocation on the next tick so remaining agents cover the gap
            _tickTimer = _decisionTickInterval;
        }

        private void OnAgentTaskBlocked(ISquadAgent agent)
        {
            Debug.Log($"[CommanderAgent] Agent '{agent.AgentId}' path blocked on task {agent.CurrentTask}. Forcing reallocation.");
            _tickTimer = _decisionTickInterval;
        }

        private void OnAgentTaskArrived(ISquadAgent agent)
        {
            Debug.Log($"[CommanderAgent] Agent '{agent.AgentId}' arrived at task destination ({agent.CurrentTask}).");
            // Optionally reassign a follow-up task next tick
        }

        private void OnAgentTargetLost(ISquadAgent agent)
        {
            Debug.Log($"[CommanderAgent] Agent '{agent.AgentId}' lost sight of target. Switching to Investigate.");
            // Issue an Investigate order at the last known player position
            if (_isPlayerDetected)
            {
                agent.AssignOrder(new SquadOrder(SquadTaskType.Investigate, _lastKnownPlayerPosition));
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

        /// <summary>
        /// Called by squad members (Hunter, Flanker, Sentinel) or perception sensors when the player is detected.
        /// Preserves architectural decoupling: Commander updates last known coordinates without wallhacking.
        /// </summary>
        public void ReportPlayerSighted(Vector3 playerPosition)
        {
            _lastKnownPlayerPosition = playerPosition;
            _timeSincePlayerDetected = 0f;
            _isPlayerDetected = true;

            // Feed player location into the Markovian corridor heatmap
            if (_heatmapTracker != null)
            {
                _heatmapTracker.NotifyCombatDetection();
                _heatmapTracker.SamplePlayerLocation(_lastKnownPlayerPosition);
            }
        }

        private void UpdatePlayerObservation()
        {
            if (_playerTransform == null) return;

            // 1. Direct vision check: Does the Commander have unobstructed line-of-sight to the player?
            if (HasDirectLineOfSightToPlayer())
            {
                ReportPlayerSighted(_playerTransform.position);
                return;
            }

            // 2. Squad intelligence check: Is any registered squad member currently engaged with the player?
            foreach (var agent in _registeredSquad)
            {
                if (agent != null && agent.IsAlive && agent.IsEngagedWithPlayer)
                {
                    ReportPlayerSighted(_playerTransform.position);
                    return;
                }
            }

            // 3. Fallback if strict line-of-sight is disabled (e.g. testing in sandbox without agents)
            if (!_requireLineOfSightOrSquadReport)
            {
                ReportPlayerSighted(_playerTransform.position);
            }
        }

        /// <summary>
        /// Checks whether the Commander has an unobstructed line of sight to the player
        /// within vision range and forward field of view cone.
        /// </summary>
        public bool HasDirectLineOfSightToPlayer()
        {
            if (_playerTransform == null) return false;

            Vector3 eyePos = transform.position + Vector3.up * 1.5f;
            Vector3 targetPos = _playerTransform.position + Vector3.up * 1.2f;
            Vector3 toTarget = targetPos - eyePos;
            float dist = toTarget.magnitude;

            if (dist > _visionRange) return false;

            // Outside close quarters, check forward vision angle
            if (dist > _closeQuartersDistance)
            {
                Vector3 toTargetFlat = new Vector3(toTarget.x, 0f, toTarget.z);
                if (toTargetFlat.sqrMagnitude > 0.001f)
                {
                    float angle = Vector3.Angle(transform.forward, toTargetFlat.normalized);
                    if (angle > _visionFovAngle * 0.5f) return false;
                }
            }

            // Raycast for obstacles (bulkheads, walls, barricades)
            if (Physics.Raycast(eyePos, toTarget.normalized, out RaycastHit hit, dist, _obstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.CompareTag("Player") && hit.transform != _playerTransform)
                {
                    return false; // Vision obstructed
                }
            }

            return true;
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

                // Order B: NavMesh-validated lateral flanking position — check both left and right sides
                Vector3 playerDir = (_lastKnownPlayerPosition - transform.position);
                playerDir.y = 0f;
                playerDir = playerDir.sqrMagnitude > 0.01f ? playerDir.normalized : transform.forward;

                Vector3 leftCandidate  = _lastKnownPlayerPosition + Vector3.Cross(Vector3.up, playerDir).normalized  *  7.0f;
                Vector3 rightCandidate = _lastKnownPlayerPosition - Vector3.Cross(Vector3.up, playerDir).normalized *  7.0f;

                bool leftOk  = NavMesh.SamplePosition(leftCandidate,  out NavMeshHit leftHit,  5.0f, NavMesh.AllAreas);
                bool rightOk = NavMesh.SamplePosition(rightCandidate, out NavMeshHit rightHit, 5.0f, NavMesh.AllAreas);

                Vector3 flankPos  = Vector3.zero;
                bool    flankValid = false;

                if (leftOk && rightOk)
                {
                    // Pick the side farther from the Commander to maximise flanking angle and avoid overlap
                    flankPos  = (Vector3.Distance(transform.position, leftHit.position) >=
                                 Vector3.Distance(transform.position, rightHit.position))
                                 ? leftHit.position : rightHit.position;
                    flankValid = true;
                }
                else if (leftOk)  { flankPos = leftHit.position;  flankValid = true; }
                else if (rightOk) { flankPos = rightHit.position; flankValid = true; }

                if (flankValid)
                {
                    orders.Add(new SquadOrder(SquadTaskType.Flank, flankPos, priority: 0.80f));
                }
                else
                {
                    // No valid lateral position reachable — issue a secondary pursuit instead of getting stuck
                    orders.Add(new SquadOrder(SquadTaskType.Pursue, _lastKnownPlayerPosition, priority: 0.70f));
                }

                // Order C: Cover predicted escape corridor (Preemptive hold, subordinate to direct combat)
                if (_heatmapTracker != null && _heatmapTracker.PredictLikelyEscapeNode(_lastKnownPlayerPosition, out var predictedNode, out float prob))
                {
                    orders.Add(new SquadOrder(
                        SquadTaskType.CoverRoute,
                        predictedNode.WorldPosition,
                        predictedNode.NodeId,
                        priority: Mathf.Clamp(0.65f + (prob * 0.12f), 0.65f, 0.77f)));
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
        /// Manages the Commander's own movement based on the current behaviour mode.
        /// MeleeDefense and CloseQuartersDefense halt movement directly in EvaluateAndExecuteBehaviourMode;
        /// Retreat and TacticalOversight request new NavMesh paths here on the 0.5 s tick.
        /// </summary>
        private void UpdateCommanderSelfMovement()
        {
            if (_motor == null || _playerTransform == null) return;

            // Combat modes already stopped movement per-frame — nothing more to do here
            if (_behaviourMode == CommanderBehaviourMode.MeleeDefense ||
                _behaviourMode == CommanderBehaviourMode.CloseQuartersDefense)
            {
                return;
            }

            if (_behaviourMode == CommanderBehaviourMode.Retreat)
            {
                ExecuteRetreatMovement();
                return;
            }

            // TacticalOversight: Reposition to maintain standoff command distance (~12 m)
            if (_isPlayerDetected)
            {
                Vector3 awayDir = (transform.position - _lastKnownPlayerPosition);
                awayDir.y = 0;
                if (awayDir.sqrMagnitude < 0.01f) awayDir = -transform.forward;
                awayDir.Normalize();

                Vector3 bestPost = Vector3.zero;
                bool foundPost = false;

                // Test straight back, 45 deg angles, and lateral strafes to prevent getting stuck against deck walls
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

        /// <summary>
        /// Actively moves the Commander away from the player when in Retreat mode.
        /// Tests angular escape directions to avoid oscillating into walls.
        /// Does not reissue a path while already actively retreating.
        /// </summary>
        private void ExecuteRetreatMovement()
        {
            if (_motor.IsMoving) return; // Already retreating — do not interrupt mid-path

            Vector3 awayDir = (transform.position - _lastKnownPlayerPosition);
            awayDir.y = 0f;
            if (awayDir.sqrMagnitude < 0.01f) awayDir = -transform.forward;
            awayDir.Normalize();

            // Retreat to just beyond the trigger threshold to clear the Retreat zone
            float retreatDistance = _retreatTriggerDistance * 0.65f;
            float[] angleOffsets = { 0f, 45f, -45f, 90f, -90f, 135f, -135f };

            foreach (float angle in angleOffsets)
            {
                Vector3 candidateDir  = Quaternion.Euler(0, angle, 0) * awayDir;
                Vector3 candidatePost = transform.position + candidateDir * retreatDistance;

                if (NavMesh.SamplePosition(candidatePost, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path))
                    {
                        _motor.SetPath(path.corners);
                        _motor.SetFaceTarget(_playerTransform); // Strafe-backwards: face player while retreating
                        return;
                    }
                }
            }
        }

        #endregion

        #region ISquadAgent Interface Methods

        public event Action<ISquadAgent> OnTaskArrived;
        public event Action<ISquadAgent> OnTaskBlocked;
        public event Action<ISquadAgent> OnTargetLost;
        public event Action<ISquadAgent> OnDied;

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
            // Draw tactical command radius
            Gizmos.color = new Color(1f, 0.84f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _preferredCommandDistance);

            // Draw retreat trigger radius
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, _retreatTriggerDistance);

            // Draw close-quarters and melee zones with mode-appropriate colours
            bool inCombat = _behaviourMode == CommanderBehaviourMode.MeleeDefense ||
                            _behaviourMode == CommanderBehaviourMode.CloseQuartersDefense;

            Gizmos.color = inCombat ? new Color(1f, 0.3f, 0f, 0.6f) : new Color(1f, 0.35f, 0f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, _closeQuartersDistance);

            if (_behaviourMode == CommanderBehaviourMode.MeleeDefense)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.9f);
                Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, _meleeAttackDistance);
            }

            // Draw line to player with colour indicating current mode
            if (_playerTransform != null)
            {
                Gizmos.color = _behaviourMode switch
                {
                    CommanderBehaviourMode.MeleeDefense         => Color.red,
                    CommanderBehaviourMode.CloseQuartersDefense => new Color(1f, 0.5f, 0f),
                    CommanderBehaviourMode.Retreat              => Color.magenta,
                    _                                           => Color.yellow
                };
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
