using System.Collections.Generic;
using System.Reflection;
using Halazia.AI.Commander;
using Halazia.Shared.Squad;
using UnityEngine;

namespace Halazia.AI.Sentinel
{
    /// <summary>Test-scene controls for Commander ↔ Sentinel communication. Uses only the existing
    /// ISquadAgent events and Commander dispatch; never changes either agent's decision logic.</summary>
    public sealed class SentinelCommanderTestControls : MonoBehaviour
    {
        [SerializeField] private CommanderAgent _commander;
        [SerializeField] private SentinelAgent _sentinel;
        [Tooltip("Destination for the CoverRoute test (a corridor marker).")]
        [SerializeField] private Transform _coverRoutePoint;
        [Tooltip("Destination for the HoldPosition test.")]
        [SerializeField] private Transform _holdPoint;
        [Tooltip("Off-NavMesh point used for the blocked-path test (inside the obstacle wall).")]
        [SerializeField] private Vector3 _unreachablePoint = new Vector3(0.5f, 0f, 0.5f);
        [Tooltip("How long a Commander direct order suppresses its automatic reallocation for this Sentinel.")]
        [SerializeField, Min(1f)] private float _directOrderHoldSeconds = 60f;

        [Header("Live diagnostics (Play Mode)")]
        [SerializeField] private bool _registeredWithCommander;
        [SerializeField] private int _commanderSquadSize;
        [SerializeField] private SquadTaskType _sentinelTask;
        [SerializeField] private SentinelAgent.SentinelState _sentinelState;
        [SerializeField] private int _arrivedReports, _blockedReports, _targetLostReports, _diedReports;
        [SerializeField] private string _lastEvent = "None";

        private static readonly FieldInfo RosterField =
            typeof(CommanderAgent).GetField("_registeredSquad", BindingFlags.NonPublic | BindingFlags.Instance);

        public bool IsRegistered => _registeredWithCommander;
        public int ArrivedReports => _arrivedReports;
        public int BlockedReports => _blockedReports;
        public int TargetLostReports => _targetLostReports;

        private void OnEnable()
        {
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

        private void Arrived(ISquadAgent a) => Record(ref _arrivedReports, $"arrived ({a.CurrentTask})");
        private void Blocked(ISquadAgent a) => Record(ref _blockedReports, $"blocked ({a.CurrentTask})");
        private void Lost(ISquadAgent a) => Record(ref _targetLostReports, $"target lost ({a.CurrentTask})");
        private void Died(ISquadAgent a) => Record(ref _diedReports, "died");
        private void Record(ref int counter, string evt)
        {
            counter++;
            _lastEvent = $"{Time.time:F1}s {evt}";
            Debug.Log($"[CommanderSentinelTest] Sentinel reported {evt}; Commander registered: {_registeredWithCommander}", this);
        }

        private void Update()
        {
            if (_sentinel == null) return;
            var roster = _commander != null ? RosterField?.GetValue(_commander) as List<ISquadAgent> : null;
            _commanderSquadSize = roster?.Count ?? 0;
            _registeredWithCommander = roster != null && roster.Contains(_sentinel);
            _sentinelTask = _sentinel.CurrentTask;
            _sentinelState = _sentinel.State;
        }

        [ContextMenu("Commander: CoverRoute (Play Mode)")]
        public void CommanderCoverRoute()
        {
            if (_coverRoutePoint == null) { Debug.LogWarning("Assign Cover Route Point.", this); return; }
            Dispatch(SquadOrder.CoverRoute(_coverRoutePoint.position, _coverRoutePoint.name));
        }

        [ContextMenu("Commander: HoldPosition (Play Mode)")]
        public void CommanderHoldPosition()
        {
            if (_holdPoint == null) { Debug.LogWarning("Assign Hold Point.", this); return; }
            Dispatch(SquadOrder.HoldPosition(_holdPoint.position));
        }

        [ContextMenu("Commander: Blocked Path Test (Play Mode)")]
        public void CommanderBlockedPath() => Dispatch(SquadOrder.HoldPosition(_unreachablePoint));

        [ContextMenu("Commander: Release Sentinel To Automatic Allocation (Play Mode)")]
        public void ReleaseToCommander() { if (Ready()) _commander.ReleaseDirectOrder(_sentinel); }

        [ContextMenu("Sentinel: Cancel Order (Play Mode)")]
        public void CancelSentinelOrder()
        {
            if (!Ready()) return;
            _commander.ReleaseDirectOrder(_sentinel);
            _sentinel.CancelOrder();
            Debug.Log("[CommanderSentinelTest] Sentinel order cancelled; returning to its post.", this);
        }

        [ContextMenu("Target: Toggle Active (target-lost test) (Play Mode)")]
        public void ToggleTarget()
        {
            if (!Application.isPlaying) return;
            var target = GameObject.Find("TestPlayerTarget") ?? FindInactive("TestPlayerTarget");
            if (target == null) return;
            target.SetActive(!target.activeSelf);
            Debug.Log($"[CommanderSentinelTest] TestPlayerTarget active: {target.activeSelf}", this);
        }

        private void Dispatch(SquadOrder order)
        {
            if (!Ready()) return;
            if (!_commander.IssueDirectOrder(_sentinel, order, _directOrderHoldSeconds))
                Debug.LogWarning("[CommanderSentinelTest] Commander refused the order (Sentinel not registered or not alive).", this);
        }

        private bool Ready()
        {
            if (Application.isPlaying && _commander != null && _sentinel != null) return true;
            Debug.LogWarning("Enter Play Mode and assign Commander and Sentinel.", this);
            return false;
        }

        private static GameObject FindInactive(string name)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }
    }
}
