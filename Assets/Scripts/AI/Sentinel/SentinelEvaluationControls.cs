using UnityEngine;
using Halazia.Shared.Squad;

namespace Halazia.AI.Sentinel
{
    /// <summary>Inspector context-menu controls and report counters for the isolated fixture.</summary>
    public sealed class SentinelEvaluationControls : MonoBehaviour
    {
        [SerializeField] private SentinelAgent _sentinel;
        [SerializeField] private Transform _orderPosition;
        [Tooltip("Order selection only. Changing it does nothing until Issue Selected Order is pressed in Play Mode. " +
                 "Patrol always starts at the first patrol waypoint and ignores Order Position.")]
        [SerializeField] private SquadTaskType _task = SquadTaskType.CoverRoute;
        [SerializeField] private string _corridorId = "TestExit";
        [Tooltip("Issue a Patrol order automatically on entering Play Mode (no Commander needed). " +
                 "Without any order the Sentinel still patrols its route from its post.")]
        [SerializeField] private bool _issuePatrolOnPlay = false;
        [Tooltip("Order-destination arrivals, not patrol waypoint visits.")]
        [SerializeField] private int _arrivals;
        [SerializeField] private int _blocks, _losses, _deaths;
        [Header("Live diagnostics (updated during Play Mode)")]
        [SerializeField] private SentinelAgent.SentinelState _liveState;
        [SerializeField] private bool _hasActiveOrder;
        [SerializeField] private SquadTaskType _activeTask;
        [SerializeField] private int _patrolVisits, _nextPatrolWaypoint;
        [SerializeField] private string _lastPatrolWaypoint;
        private void OnEnable()
        {
            if (_sentinel == null) _sentinel = GetComponent<SentinelAgent>();
            if (_sentinel == null) return;
            _sentinel.OnTaskArrived += Arrived;
            _sentinel.OnTaskBlocked += Blocked;
            _sentinel.OnTargetLost += Lost;
            _sentinel.OnDied += Died;
        }
        private void OnDisable()
        {
            if (_sentinel == null) return;
            _sentinel.OnTaskArrived -= Arrived;
            _sentinel.OnTaskBlocked -= Blocked;
            _sentinel.OnTargetLost -= Lost;
            _sentinel.OnDied -= Died;
        }
        private void Arrived(ISquadAgent _) { _arrivals++; Debug.Log("Sentinel report: arrived", this); }
        private void Blocked(ISquadAgent _) { _blocks++; Debug.Log("Sentinel report: blocked", this); }
        private void Start() { if (_issuePatrolOnPlay) StartPatrolRoute(); }
        // Losses count every end of sight contact, including brief sightings outside the defense radius.
        private void Lost(ISquadAgent _)
        {
            _losses++;
            Debug.Log($"Sentinel report: target lost (state {_sentinel.State}, task {_sentinel.CurrentTask}, " +
                      $"last seen {_sentinel.LastKnownPlayerPosition})", this);
        }
        private void Died(ISquadAgent _) { _deaths++; Debug.Log("Sentinel report: died", this); }
        private void Update()
        {
            if (_sentinel == null) return;
            _liveState = _sentinel.State;
            _hasActiveOrder = _sentinel.HasOrder;
            _activeTask = _sentinel.CurrentTask;
            _patrolVisits = _sentinel.PatrolVisits;
            _nextPatrolWaypoint = _sentinel.NextPatrolWaypointNumber;
            _lastPatrolWaypoint = _sentinel.LastPatrolWaypoint;
        }
        [ContextMenu("Issue Selected Order (Play Mode)")]
        public void IssueOrder()
        {
            if (!CanIssue()) return;
            // A Patrol order must begin the configured route, not travel to Order Position first.
            if (_task == SquadTaskType.Patrol) { StartPatrolRoute(); return; }
            if (_orderPosition == null && _task != SquadTaskType.Idle)
            {
                Debug.LogWarning("Assign Order Position, or use Start Patrol Route to start at the first patrol waypoint.", this);
                return;
            }
            var order = _task == SquadTaskType.Idle ? SquadOrder.Idle() :
                new SquadOrder(_task, _orderPosition.position, _corridorId);
            _sentinel.AssignOrder(order);
            Debug.Log($"Sentinel order issued: {order}", this);
        }
        [ContextMenu("Start Patrol Route (Play Mode)")]
        public void StartPatrolRoute()
        {
            if (!CanIssue()) return;
            if (!_sentinel.StartPatrolRoute())
            { Debug.LogWarning("Patrol needs at least one assigned, valid waypoint.", this); return; }
            _task = SquadTaskType.Patrol;
            Debug.Log("Patrol issued at the first valid route waypoint. Player detection can still interrupt patrol.", this);
        }
        private bool CanIssue()
        {
            if (Application.isPlaying && _sentinel != null && _sentinel.IsAlive && _sentinel.isActiveAndEnabled) return true;
            Debug.LogWarning("Enter Play Mode and assign a living, enabled Sentinel before issuing an evaluation order.", this);
            return false;
        }
        [ContextMenu("Cancel Order (Play Mode)")]
        public void Cancel() { if (Application.isPlaying && _sentinel != null) _sentinel.CancelOrder(); }
        [ContextMenu("Damage Sentinel 25 (Play Mode)")]
        public void Damage() { if (Application.isPlaying && _sentinel != null) _sentinel.TakeDamage(25f); }
    }
}
