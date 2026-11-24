using System;
using UnityEngine;

namespace Halazia.Shared.Squad
{

    // ─────────────────────────────────────────────────────────────────────────────
    // Squad Agent Contract
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Contract implemented by every squad member: Sentinel, Hunter, Flanker, and Commander.
    /// Allows the Commander to evaluate state and issue orders without any coupling to
    /// concrete agent classes. Teammates MUST implement every member below.
    ///
    /// REPORTING: agents fire events to push state changes back to the Commander.
    /// The Commander must subscribe to these events immediately after RegisterSquadMember().
    /// </summary>
    public interface ISquadAgent
    {
        // ── Identity ──────────────────────────────────────────────────────────────
        /// <summary>Unique identifier for log messages and debug output.</summary>
        string AgentId { get; }

        /// <summary>Archetype of this agent (Sentinel / Hunter / Flanker / Commander).</summary>
        AgentRole Role { get; }

        // ── State ─────────────────────────────────────────────────────────────────
        /// <summary>World-space position this frame.</summary>
        Vector3 Position { get; }

        /// <summary>Raw health points.</summary>
        float CurrentHealth { get; }

        /// <summary>Maximum health points (must be > 0).</summary>
        float MaxHealth { get; }

        /// <summary>Health as a fraction in [0, 1]. Defaults via interface implementation.</summary>
        float HealthPercentage => MaxHealth > 0.001f ? CurrentHealth / MaxHealth : 1f;

        /// <summary>Normalized health in [0, 1] alias for HealthPercentage.</summary>
        float Health01 => HealthPercentage;

        /// <summary>False when the agent has been eliminated. Commander skips dead agents.</summary>
        bool IsAlive { get; }

        /// <summary>True while the agent is actively targeting or attacking the player.</summary>
        bool IsEngagedWithPlayer { get; }

        /// <summary>The task type the agent is currently executing.</summary>
        SquadTaskType CurrentTask { get; }

        // ── Commands (Commander → Agent) ──────────────────────────────────────────
        /// <summary>
        /// Assigns a new tactical order. The agent transitions its internal state machine
        /// to match the order's TaskType and navigates to TargetPosition.
        /// </summary>
        void AssignOrder(SquadOrder order);

        /// <summary>Alias for AssignOrder matching team specification.</summary>
        void AcceptOrder(SquadOrder order) => AssignOrder(order);

        /// <summary>Cancels the current order and returns the agent to Idle.</summary>
        void CancelOrder();

        // ── Reports (Agent → Commander) ───────────────────────────────────────────
        /// <summary>
        /// Fired when the agent arrives at the order's TargetPosition.
        /// Arg: the agent that arrived.
        /// </summary>
        event Action<ISquadAgent> OnTaskArrived;

        /// <summary>
        /// Fired when the agent's path is blocked and it cannot reach the target.
        /// Commander should assign an alternative order.
        /// Arg: the blocked agent.
        /// </summary>
        event Action<ISquadAgent> OnTaskBlocked;

        /// <summary>
        /// Fired when the agent loses sight or sound contact with the player.
        /// Arg: the agent that lost contact.
        /// </summary>
        event Action<ISquadAgent> OnTargetLost;

        /// <summary>
        /// Fired once when the agent's health drops to zero.
        /// Commander must unregister the agent after receiving this event.
        /// Arg: the dying agent.
        /// </summary>
        event Action<ISquadAgent> OnDied;
    }
}
