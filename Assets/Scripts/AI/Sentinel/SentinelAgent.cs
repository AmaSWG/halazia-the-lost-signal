using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Halazia.AI.Commander;
using Halazia.Shared.Combat;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

namespace Halazia.AI.Sentinel
{
    /// <summary>Area security decisions. The path provider plans; AgentMotor alone moves.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentMotor))]
    public class SentinelAgent : MonoBehaviour, ISquadAgent, ISentinelDamageable
    {
        public enum SentinelState { Idle, Patrol, MoveToOrder, Guard, Investigate, Search, Chase, Attack, ReturnToPost, Dead }

        [Header("Agent Identity")]
        [SerializeField] private string _agentId = "Sentinel_01";
        [Header("Health")]
        [SerializeField, Min(0.01f)] private float _maxHealth = 100f;
        [Header("Patrol")]
        [SerializeField] private Transform[] _patrolWaypoints = Array.Empty<Transform>();
        [SerializeField] private bool _loopPatrol = true;
        [SerializeField, Min(0.01f)] private float _patrolSpeed = 2f;
        [SerializeField, Min(0f)] private float _patrolWait = 2f;
        [Header("Defense")]
        [Tooltip("Optional initial post. Otherwise uses the spawn position.")]
        [SerializeField] private Transform _guardPosition = null;
        [SerializeField, Min(0.1f)] private float _defenseRadius = 12f;
        [Header("Perception")]
        [Tooltip("Assign the player root; no scene searches are performed for targets.")]
        [SerializeField] private Transform _playerTarget = null;
        [SerializeField, Min(0.1f)] private float _detectionRange = 15f;
        [SerializeField, Range(1f, 360f)] private float _fieldOfView = 110f;
        [Tooltip("Include every solid obstacle layer. Self and target colliders are filtered out.")]
        [SerializeField] private LayerMask _sightObstacleMask = ~0;
        [SerializeField] private Vector3 _eyeOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField] private Vector3 _targetSightOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(0.05f)] private float _perceptionInterval = 0.1f;
        [SerializeField, Min(0f)] private float _targetLostTimeout = 1.5f;
        [Header("Investigation")]
        [SerializeField, Min(0f)] private float _searchDuration = 5f;
        [SerializeField, Min(0f)] private float _searchTurnSpeed = 70f;
        [Header("Combat")]
        [SerializeField, Min(0.1f)] private float _attackRange = 1.8f;
        [SerializeField, Min(0.05f)] private float _attackCooldown = 1.2f;
        [SerializeField, Min(0f)] private float _damage = 15f;
        [SerializeField, Range(1f, 180f)] private float _attackFacingAngle = 30f;
        [Header("Navigation")]
        [Tooltip("Required: Weighted A* adapter, or explicitly opt into SentinelNavMeshPrototype.")]
        [SerializeField] private SentinelPathProvider _pathProvider;
        [Tooltip("Match AgentMotor Max Speed: its API exposes a multiplier, not absolute speed.")]
        [SerializeField, Min(0.01f)] private float _motorBaseSpeed = 4.2f;
        [SerializeField, Min(0.01f)] private float _moveSpeed = 4.2f;
        [Tooltip("Must be >= AgentMotor stopping distance; also bounds endpoint projection error.")]
        [SerializeField, Min(0.05f)] private float _arrivalDistance = 0.6f;
        [SerializeField, Min(0.1f)] private float _repathInterval = 0.6f;
        [SerializeField, Min(0.1f)] private float _targetMoveThreshold = 0.8f;
        [SerializeField, Min(0.5f)] private float _stuckTimeout = 3f;
        [Header("Debug")]
        [SerializeField] private bool _debugLogs = false;
        [SerializeField] private bool _drawGizmos = true;

        private float _currentHealth;
        private SquadOrder _currentOrder;
        private bool _hasOrder, _arrivedReported, _blockedReported;
        private CommanderAgent _commander;
        private IAgentMotor _motor;
        private ICombatActions _combat;
        private ISentinelDamageable _targetHealth;
        private UnityEngine.Object _targetHealthObject;
        private Transform _cachedTarget;
        private Vector3 _homePost, _post, _lastKnownPlayerPosition, _destination, _progressPosition;
        private bool _tracking, _visible, _moving, _motorBlocked, _motorArrived, _patrolComplete;
        private int _patrolIndex, _orderVersion;
        private float _nextPerception, _lastSeen, _nextAttack, _nextPath, _progressTime, _waitUntil, _retryAfter;
        private float _nextCommanderLookup;
        private float _nextHealthLookup;
        private bool _warnedMissingHealth;
        private readonly List<MonoBehaviour> _targetComponents = new List<MonoBehaviour>();
        private readonly RaycastHit[] _sightHits = new RaycastHit[64];
        private SentinelState _state;

        public string AgentId => _agentId;
        public AgentRole Role => AgentRole.Sentinel;
        public Vector3 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _currentHealth > 0f;
        public bool IsEngagedWithPlayer => IsAlive && (_state == SentinelState.Chase || _state == SentinelState.Attack);
        public SquadTaskType CurrentTask => _hasOrder ? _currentOrder.TaskType : SquadTaskType.Idle;
        public SquadOrder CurrentOrder => _currentOrder;
        public bool HasOrder => _hasOrder;
        public SentinelState State => _state;
        public Vector3 LastKnownPlayerPosition => _lastKnownPlayerPosition;
        public event Action<ISquadAgent> OnTaskArrived;
        public event Action<ISquadAgent> OnTaskBlocked;
        public event Action<ISquadAgent> OnTargetLost;
        public event Action<ISquadAgent> OnDied;

        private void Awake()
        {
            OnValidate();
            _currentHealth = _maxHealth;
            _motor = GetComponent<IAgentMotor>();
            _combat = GetComponent<ICombatActions>();
            _homePost = _guardPosition != null ? _guardPosition.position : Position;
            _post = _homePost;
            if (_pathProvider == null) _pathProvider = GetComponent<SentinelPathProvider>();
            var navAgent = GetComponent<NavMeshAgent>();
            bool competingController = navAgent != null && navAgent.enabled;
            // Inspect contracts rather than depending on concrete sandbox/test AI types.
            foreach (var component in GetComponents<MonoBehaviour>())
                if (component != null && component != this && component.isActiveAndEnabled &&
                    (component is ISquadAgent || (component is IAgentMotor && !(component is AgentMotor))))
                    competingController = true;
            if (competingController)
            {
                Debug.LogError($"[SentinelAgent:{_agentId}] Remove competing navigation/AI components before enabling Sentinel.", this);
                enabled = false;
            }
            if (_pathProvider == null)
                Debug.LogError($"[SentinelAgent:{_agentId}] Missing path provider. Weighted A* is absent; explicitly add SentinelNavMeshPrototype for prototype navigation.", this);
        }

        private void OnEnable()
        {
            _motor.OnArrived += MotorArrived;
            _motor.OnPathBlocked += MotorBlocked;
            _nextPerception = 0f;
            if (!IsAlive) { StopNavigation(); SetState(SentinelState.Dead); return; }
            _commander = FindAnyObjectByType<CommanderAgent>();
            if (_commander != null) _commander.RegisterSquadMember(this);
            else Debug.LogWarning($"[SentinelAgent:{_agentId}] No CommanderAgent found in scene.", this);
            if (_hasOrder && _blockedReported) SetState(SentinelState.Idle);
            else if (_hasOrder && !_arrivedReported) BeginOrder();
            else SetState(SentinelState.ReturnToPost);
        }

        private void OnDisable()
        {
            if (_motor != null)
            {
                _motor.OnArrived -= MotorArrived;
                _motor.OnPathBlocked -= MotorBlocked;
                StopNavigation();
            }
            if (_commander != null) _commander.UnregisterSquadMember(this);
            _commander = null;
            _tracking = _visible = false;
        }

        private void Update()
        {
            if (!IsAlive) return;
            // Retry at a bounded frequency when a Commander spawns after this agent.
            if (_commander == null && Time.time >= _nextCommanderLookup)
            {
                _nextCommanderLookup = Time.time + 2f;
                _commander = FindAnyObjectByType<CommanderAgent>();
                if (_commander != null) _commander.RegisterSquadMember(this);
            }
            if (Time.time >= _nextPerception)
            {
                _nextPerception = Time.time + _perceptionInterval;
                ObservePlayer();
            }
            if (_tracking && ((!_visible && Time.time - _lastSeen >= _targetLostTimeout) || !TargetValid()))
            {
                _tracking = false;
                int version = _orderVersion;
                if (_state == SentinelState.Chase || _state == SentinelState.Attack) SetState(SentinelState.Investigate);
                // Commander can synchronously assign a new order during this callback.
                OnTargetLost?.Invoke(this);
                if (version != _orderVersion || !IsAlive || !isActiveAndEnabled) return;
            }

            bool canEngage = !_hasOrder || (!_blockedReported &&
                CurrentTask != SquadTaskType.Retreat && CurrentTask != SquadTaskType.Idle &&
                (_arrivedReported || CurrentTask == SquadTaskType.Pursue));
            if (canEngage && Time.time >= _retryAfter && _visible && TargetValid() && WithinDefense(_playerTarget.position) && WithinDefense(Position))
                SetState(Vector3.Distance(Position, _playerTarget.position) <= _attackRange ? SentinelState.Attack : SentinelState.Chase);
            else if (_state == SentinelState.Chase || _state == SentinelState.Attack)
                SetState(_visible ? SentinelState.ReturnToPost : SentinelState.Investigate);

            switch (_state)
            {
                case SentinelState.Patrol: TickPatrol(); break;
                case SentinelState.MoveToOrder:
                    if (MoveTo(_currentOrder.TargetPosition, _moveSpeed)) CompleteOrderArrival();
                    break;
                case SentinelState.Guard:
                    if (Vector3.Distance(Position, _post) > _arrivalDistance) SetState(SentinelState.ReturnToPost);
                    break;
                case SentinelState.Investigate:
                    if (MoveTo(ClampToDefense(_lastKnownPlayerPosition), _moveSpeed)) StartSearch();
                    break;
                case SentinelState.Search:
                    transform.Rotate(0f, _searchTurnSpeed * Time.deltaTime, 0f);
                    if (Time.time >= _waitUntil) SetState(SentinelState.ReturnToPost);
                    break;
                case SentinelState.Chase:
                    // No live-position pursuit through walls.
                    _motor.SetFaceTarget(_lastKnownPlayerPosition);
                    MoveTo(ClampToDefense(_lastKnownPlayerPosition), _moveSpeed);
                    break;
                case SentinelState.Attack: TickAttack(); break;
                case SentinelState.ReturnToPost:
                    if (MoveTo(_post, _moveSpeed)) ResumeAtPost();
                    break;
                case SentinelState.Idle:
                    if (!_hasOrder && Time.time >= _retryAfter) SetState(SentinelState.ReturnToPost);
                    break;
            }
        }

        public void AssignOrder(SquadOrder order)
        {
            if (!IsAlive) return;
            _orderVersion++;
            _currentOrder = order;
            _hasOrder = true;
            _arrivedReported = _blockedReported = false;
            _retryAfter = 0f;
            BeginOrder();
        }

        private void BeginOrder()
        {
            StopNavigation();
            _waitUntil = 0f;
            _patrolComplete = false;
            _post = CurrentTask == SquadTaskType.CoverRoute || CurrentTask == SquadTaskType.HoldPosition ||
                    CurrentTask == SquadTaskType.Flank || CurrentTask == SquadTaskType.Retreat || CurrentTask == SquadTaskType.Patrol
                ? _currentOrder.TargetPosition : _homePost;
            switch (CurrentTask)
            {
                case SquadTaskType.Idle: SetState(SentinelState.Idle); break;
                case SquadTaskType.Patrol:
                case SquadTaskType.CoverRoute:
                case SquadTaskType.HoldPosition:
                case SquadTaskType.Flank:
                case SquadTaskType.Retreat:
                case SquadTaskType.Pursue:
                case SquadTaskType.Investigate:
                    SetState(SentinelState.MoveToOrder); break;
                default: BlockNavigation(); break;
            }
        }

        public void CancelOrder()
        {
            _orderVersion++;
            _hasOrder = false;
            _currentOrder = default;
            _arrivedReported = _blockedReported = false;
            _post = _homePost;
            _patrolComplete = false;
            _waitUntil = _retryAfter = 0f;
            StopNavigation();
            if (IsAlive) SetState(SentinelState.ReturnToPost);
        }

        public void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            _currentHealth = Mathf.Clamp(_currentHealth - damage, 0f, _maxHealth);
            if (IsAlive) { _combat?.Hit(); return; }
            SetState(SentinelState.Dead);
            _tracking = _visible = false;
            OnDied?.Invoke(this);
            if (_commander != null) _commander.UnregisterSquadMember(this);
        }

        private void CompleteOrderArrival()
        {
            bool report = !_arrivedReported;
            _arrivedReported = true;
            if (CurrentTask == SquadTaskType.Investigate || CurrentTask == SquadTaskType.Pursue) StartSearch();
            else SetState(CurrentTask == SquadTaskType.Patrol ? SentinelState.Patrol : SentinelState.Guard);
            // Set state first; never overwrite an order assigned by an event subscriber.
            if (report) OnTaskArrived?.Invoke(this);
        }

        private void ResumeAtPost()
        {
            if (_hasOrder && !_arrivedReported && !_blockedReported) SetState(SentinelState.MoveToOrder);
            else if ((!_hasOrder || CurrentTask == SquadTaskType.Patrol) && !_patrolComplete && TryPatrolPoint(out _)) SetState(SentinelState.Patrol);
            else SetState(SentinelState.Guard);
        }

        private void TickPatrol()
        {
            if (Time.time < _waitUntil) return;
            if (!TryPatrolPoint(out Vector3 point))
            {
                _patrolComplete = true;
                SetState(SentinelState.ReturnToPost);
                return;
            }
            if (!MoveTo(point, _patrolSpeed)) return;
            _patrolIndex++;
            if (_patrolIndex >= _patrolWaypoints.Length)
            {
                if (_loopPatrol) _patrolIndex = 0;
                else { _patrolComplete = true; SetState(SentinelState.ReturnToPost); return; }
            }
            _waitUntil = Time.time + _patrolWait;
        }

        private bool TryPatrolPoint(out Vector3 point)
        {
            point = default;
            if (_patrolWaypoints == null || _patrolWaypoints.Length == 0) return false;
            for (int i = 0; i < _patrolWaypoints.Length; i++)
            {
                if (_patrolIndex >= _patrolWaypoints.Length)
                {
                    if (!_loopPatrol) { _patrolComplete = true; return false; }
                    _patrolIndex = 0;
                }
                var waypoint = _patrolWaypoints[_patrolIndex];
                if (waypoint != null) { point = waypoint.position; return true; }
                _patrolIndex++;
            }
            return false;
        }

        private void ObservePlayer()
        {
            if (_cachedTarget != _playerTarget)
            {
                // Contact and health belong to a particular target, never its replacement.
                _tracking = _visible = false;
                _cachedTarget = _playerTarget;
                _targetHealth = null;
                _targetHealthObject = null;
                _nextHealthLookup = 0f;
                _warnedMissingHealth = false;
                if (_state == SentinelState.Chase || _state == SentinelState.Attack ||
                    _state == SentinelState.Investigate || _state == SentinelState.Search)
                    SetState(SentinelState.ReturnToPost);
            }
            if (_playerTarget != null && _targetHealthObject == null && Time.time >= _nextHealthLookup)
            {
                _nextHealthLookup = Time.time + 2f;
                _targetHealth = null;
                _playerTarget.GetComponentsInParent(false, _targetComponents);
                foreach (var component in _targetComponents)
                    if (component is ISentinelDamageable health)
                    { _targetHealth = health; _targetHealthObject = component; break; }
                if (_targetHealth == null && !_warnedMissingHealth)
                {
                    _warnedMissingHealth = true;
                    Debug.LogWarning($"[SentinelAgent:{_agentId}] Target has no ISentinelDamageable. Add SentinelTargetHealth or a health adapter to receive damage.", this);
                }
            }
            _visible = TargetValid() && CanSeePlayer(true);
            if (!_visible) return;
            _tracking = true;
            _lastSeen = Time.time;
            _lastKnownPlayerPosition = _playerTarget.position;
        }

        private bool TargetValid() => _playerTarget != null && _playerTarget != transform &&
            !_playerTarget.IsChildOf(transform) && _playerTarget.gameObject.activeInHierarchy &&
            (_targetHealth == null || (_targetHealthObject != null && _targetHealth.IsAlive));

        private bool CanSeePlayer(bool checkFov)
        {
            Vector3 origin = Position + _eyeOffset;
            Vector3 delta = _playerTarget.position + _targetSightOffset - origin;
            float distance = delta.magnitude;
            if (distance > _detectionRange) return false;
            if (checkFov && Vector3.Angle(transform.forward, delta) > _fieldOfView * 0.5f) return false;
            if (distance < 0.001f) return true;
            int count = Physics.RaycastNonAlloc(origin, delta / distance, _sightHits, distance,
                _sightObstacleMask, QueryTriggerInteraction.Ignore);
            // Saturated buffers fail closed: no sight through unexamined obstacles.
            if (count == _sightHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = _sightHits[i].transform;
                if (hit == transform || hit.IsChildOf(transform) ||
                    hit == _playerTarget || hit.IsChildOf(_playerTarget)) continue;
                return false;
            }
            return true;
        }

        private void TickAttack()
        {
            if (!TargetValid() || !CanSeePlayer(false) || !WithinDefense(_playerTarget.position))
            { SetState(SentinelState.Investigate); return; }
            _motor.SetFaceTarget(_playerTarget);
            Vector3 direction = _playerTarget.position - Position;
            direction.y = 0f;
            if (Time.time < _nextAttack || Vector3.Angle(transform.forward, direction) > _attackFacingAngle) return;
            if (Vector3.Distance(Position, _playerTarget.position) > _attackRange) return;
            _nextAttack = Time.time + _attackCooldown;
            int version = _orderVersion;
            _combat?.Attack();
            if (!IsAlive || !isActiveAndEnabled || version != _orderVersion) return;
            if (_targetHealthObject != null && _targetHealth != null && _targetHealth.IsAlive)
                _targetHealth.TakeDamage(_damage);
        }

        // Verify arrival against the requested destination, never only the motor's event.
        private bool MoveTo(Vector3 destination, float speed)
        {
            if (!Finite(destination)) { BlockNavigation(); return false; }
            if (Vector3.Distance(Position, destination) <= _arrivalDistance)
            { StopNavigation(); return true; }
            if (_motorBlocked) { BlockNavigation(); return false; }
            if (_moving)
            {
                if (Vector3.Distance(Position, _progressPosition) >= 0.1f)
                { _progressPosition = Position; _progressTime = Time.time; }
                if (_motorArrived)
                {
                    // Reaching an old pursuit endpoint is progress, not a blocked path.
                    if (Vector3.Distance(_destination, destination) <= 0.01f)
                    { BlockNavigation(); return false; }
                    if (Time.time < _nextPath) return false;
                    _moving = false;
                }
                else
                {
                    if (Time.time - _progressTime >= _stuckTimeout)
                    { BlockNavigation(); return false; }
                    if (Vector3.Distance(_destination, destination) < _targetMoveThreshold || Time.time < _nextPath) return false;
                }
            }
            _motor.Stop();
            if (_pathProvider == null || !_pathProvider.isActiveAndEnabled ||
                !_pathProvider.TryGetPath(Position, destination, out var path) || path == null || path.Length == 0 ||
                Vector3.Distance(path[path.Length - 1], destination) > _arrivalDistance)
            { BlockNavigation(); return false; }
            for (int i = 0; i < path.Length; i++)
            {
                if (!Finite(path[i]) || ((_state == SentinelState.Chase || _state == SentinelState.Investigate) && !WithinDefense(path[i])))
                { BlockNavigation(); return false; }
            }
            _destination = destination;
            _nextPath = Time.time + _repathInterval;
            if (!_moving) { _progressTime = Time.time; _progressPosition = Position; }
            _moving = true;
            _motorArrived = false;
            _motor.SetMovementLocked(false);
            _motor.SetSpeedModifier(speed / _motorBaseSpeed);
            _motor.SetPath(path);
            return false;
        }

        private void MotorArrived() { if (_moving) _motorArrived = true; }
        private void MotorBlocked() { if (_moving) _motorBlocked = true; }
        private void BlockNavigation()
        {
            SetState(SentinelState.Idle);
            StopNavigation();
            _retryAfter = Time.time + _stuckTimeout;
            Debug.LogWarning($"[SentinelAgent:{_agentId}] Cannot reach destination for {CurrentTask}.", this);
            if (_hasOrder && !_blockedReported)
            { _blockedReported = true; OnTaskBlocked?.Invoke(this); }
        }
        private void StopNavigation()
        {
            _moving = _motorBlocked = _motorArrived = false;
            _motor.Stop();
            _motor.SetMovementLocked(!IsAlive);
            _motor.SetFaceTarget((Transform)null);
        }
        private void SetState(SentinelState state)
        {
            if (_state == state) return;
            StopNavigation();
            _state = state;
            if (state == SentinelState.Attack || state == SentinelState.Dead) _motor.SetMovementLocked(true);
            if (_debugLogs) Debug.Log($"[SentinelAgent:{_agentId}] {state}", this);
        }
        private void StartSearch()
        {
            SetState(SentinelState.Search);
            _waitUntil = Time.time + _searchDuration;
        }
        private bool WithinDefense(Vector3 point) => (point - _post).sqrMagnitude <= _defenseRadius * _defenseRadius;
        private Vector3 ClampToDefense(Vector3 point) => _post + Vector3.ClampMagnitude(point - _post, _defenseRadius);
        private static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);

        private void OnValidate()
        {
            _maxHealth = Mathf.Max(0.01f, _maxHealth);
            _patrolSpeed = Mathf.Max(0.01f, _patrolSpeed);
            _patrolWait = Mathf.Max(0f, _patrolWait);
            _defenseRadius = Mathf.Max(0.1f, _defenseRadius);
            _detectionRange = Mathf.Max(0.1f, _detectionRange);
            _fieldOfView = Mathf.Clamp(_fieldOfView, 1f, 360f);
            _perceptionInterval = Mathf.Max(0.05f, _perceptionInterval);
            _targetLostTimeout = Mathf.Max(0f, _targetLostTimeout);
            _searchDuration = Mathf.Max(0f, _searchDuration);
            _searchTurnSpeed = Mathf.Max(0f, _searchTurnSpeed);
            _attackRange = Mathf.Max(0.1f, _attackRange);
            _attackCooldown = Mathf.Max(0.05f, _attackCooldown);
            _damage = Mathf.Max(0f, _damage);
            _motorBaseSpeed = Mathf.Max(0.01f, _motorBaseSpeed);
            _moveSpeed = Mathf.Max(0.01f, _moveSpeed);
            _arrivalDistance = Mathf.Max(0.05f, _arrivalDistance);
            _repathInterval = Mathf.Max(0.1f, _repathInterval);
            _targetMoveThreshold = Mathf.Max(0.1f, _targetMoveThreshold);
            _stuckTimeout = Mathf.Max(0.5f, _stuckTimeout);
        }
        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos) return;
            Vector3 post = Application.isPlaying ? _post : _guardPosition != null ? _guardPosition.position : Position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(post, _defenseRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Position + _eyeOffset, _detectionRange);
            Gizmos.DrawRay(Position + _eyeOffset, Quaternion.AngleAxis(-_fieldOfView * 0.5f, Vector3.up) * transform.forward * _detectionRange);
            Gizmos.DrawRay(Position + _eyeOffset, Quaternion.AngleAxis(_fieldOfView * 0.5f, Vector3.up) * transform.forward * _detectionRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(Position, _attackRange);
        }
    }
}
