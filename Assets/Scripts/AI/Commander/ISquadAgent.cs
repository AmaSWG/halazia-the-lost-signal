using System;
using UnityEngine;

namespace Halazia.AI.Commander
{
    public enum AgentRole
    {
        Commander,
        Sentinel,   // Defensive tank, guards choke points and bulkheads
        Hunter,     // Aggressive pursuit, tracks and applies direct pressure
        Flanker     // Stealth / alternative routes, cuts off escape corridors
    }

    public enum SquadTaskType
    {
        Idle,
        Investigate,    // Scout last known acoustic or visual disturbance
        Pursue,         // Direct engagement / pressure on player
        Flank,          // Approach player from lateral route or rear
        CoverRoute,     // Hold choke point or predicted escape corridor
        Retreat         // Fall back to regroup with Commander
    }

    /// <summary>
    /// Tactical directive issued by the Commander to a squad member.
    /// </summary>
    [Serializable]
    public struct SquadOrder
    {
        public SquadTaskType TaskType;
        public Vector3 TargetPosition;
        public string TargetCorridorId;
        public float Priority;
        public float IssueTimestamp;

        public SquadOrder(SquadTaskType taskType, Vector3 targetPosition, string corridorId = "", float priority = 1.0f)
        {
            TaskType = taskType;
            TargetPosition = targetPosition;
            TargetCorridorId = corridorId;
            Priority = priority;
            IssueTimestamp = Time.time;
        }

        public override string ToString()
        {
            return $"[{TaskType}] at {TargetPosition} (Corridor: '{TargetCorridorId}', Pri: {Priority:F1})";
        }
    }

    /// <summary>
    /// Decoupled interface implemented by Sentinel, Hunter, Flanker, and Commander.
    /// Allows the Commander to evaluate state and issue orders without tight coupling.
    /// </summary>
    public interface ISquadAgent
    {
        string AgentId { get; }
        AgentRole Role { get; }
        Vector3 Position { get; }
        float CurrentHealth { get; }
        float MaxHealth { get; }
        float HealthPercentage => (MaxHealth > 0.001f) ? (CurrentHealth / MaxHealth) : 1f;
        bool IsAlive { get; }
        bool IsEngagedWithPlayer { get; }
        SquadTaskType CurrentTask { get; }

        void AssignOrder(SquadOrder order);
        void CancelOrder();
    }
}
