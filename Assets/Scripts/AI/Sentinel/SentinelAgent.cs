using System;
using UnityEngine;
using Halazia.AI.Commander;
using Halazia.Shared.Squad;

namespace Halazia.AI.Sentinel
{
    /// <summary>
    /// The Sentinel squad agent (area security: patrol, choke points, escape-route denial).
    /// FOUNDATION ONLY: registers with the Commander and receives/stores orders.
    /// Movement, patrol, and combat behaviour are not implemented yet.
    /// </summary>
    [DisallowMultipleComponent]
    public class SentinelAgent : MonoBehaviour, ISquadAgent
    {
        [Header("Agent Identity")]
        [SerializeField] private string _agentId = "Sentinel_01";

        [Header("Health")]
        [SerializeField] private float _maxHealth = 100f;

        private float _currentHealth;
        private SquadOrder _currentOrder;
        private bool _hasOrder;
        private CommanderAgent _commander;

        // ── ISquadAgent: Identity ────────────────────────────────────────────────
        public string AgentId => _agentId;
        public AgentRole Role => AgentRole.Sentinel;

        // ── ISquadAgent: State ───────────────────────────────────────────────────
        public Vector3 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _currentHealth > 0.001f;
        public bool IsEngagedWithPlayer => false; // No combat yet.
        public SquadTaskType CurrentTask => _hasOrder ? _currentOrder.TaskType : SquadTaskType.Idle;

        /// <summary>The most recent order received from the Commander (valid only if HasOrder is true).</summary>
        public SquadOrder CurrentOrder => _currentOrder;
        public bool HasOrder => _hasOrder;

        // ── ISquadAgent: Reports (Agent → Commander) ─────────────────────────────
        // Declared to satisfy the contract; they will be fired once movement/combat exist.
#pragma warning disable 0067
        public event Action<ISquadAgent> OnTaskArrived;
        public event Action<ISquadAgent> OnTaskBlocked;
        public event Action<ISquadAgent> OnTargetLost;
        public event Action<ISquadAgent> OnDied;
#pragma warning restore 0067

        private void Awake()
        {
            _currentHealth = _maxHealth;
        }

        private void OnEnable()
        {
            // Join the Commander's squad so it can send us orders.
            _commander = FindAnyObjectByType<CommanderAgent>();
            if (_commander != null)
                _commander.RegisterSquadMember(this);
            else
                Debug.LogWarning($"[SentinelAgent:{_agentId}] No CommanderAgent found in scene. Orders will not be received.");
        }

        private void OnDisable()
        {
            if (_commander != null)
                _commander.UnregisterSquadMember(this);
        }

        private void Start()
        {
            Debug.Log($"[SentinelAgent:{_agentId}] Sentinel AI initialized");
        }

        // ── ISquadAgent: Commands (Commander → Agent) ────────────────────────────
        public void AssignOrder(SquadOrder order)
        {
            if (!IsAlive)
            {
                Debug.LogWarning($"[SentinelAgent:{_agentId}] Ignored order {order.TaskType}: agent is dead.");
                return;
            }

            _currentOrder = order;
            _hasOrder = true;

            Debug.Log($"[SentinelAgent:{_agentId}] Received new order: {order.TaskType} -> {order}");
        }

        public void CancelOrder()
        {
            _hasOrder = false;
            _currentOrder = default;

            Debug.Log($"[SentinelAgent:{_agentId}] Order cancelled. Now Idle.");
        }
    }
}
