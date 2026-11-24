using System;
using UnityEngine;

namespace Halazia.Shared.Squad
{
    /// <summary>
    /// Tactical directive issued by the Commander to a squad member via ISquadAgent.AssignOrder().
    /// Carries the task type, spatial destination, and optional corridor node reference needed
    /// for the agent to execute its assignment without querying the Commander again.
    /// Immutable value type — create a new order instead of mutating an existing one.
    /// </summary>
    [Serializable]
    public struct SquadOrder
    {
        /// <summary>The tactical task this order instructs the agent to perform.</summary>
        public SquadTaskType TaskType;

        /// <summary>World-space destination the agent should navigate toward.</summary>
        public Vector3 TargetPosition;

        /// <summary>Corridor node identifier used for CoverRoute predictions.</summary>
        public string TargetCorridorId;

        /// <summary>Normalized priority of this order in [0.0, 1.0].</summary>
        public float Priority;

        /// <summary>Time.time timestamp when this order was issued.</summary>
        public float IssueTimestamp;

        // Compatibility aliases for team conventions
        public SquadTaskType Task
        {
            get => TaskType;
            set => TaskType = value;
        }

        public string TargetNodeId
        {
            get => TargetCorridorId;
            set => TargetCorridorId = value;
        }

        public SquadOrder(SquadTaskType taskType, Vector3 targetPosition, string corridorId = "", float priority = 1.0f)
        {
            TaskType = taskType;
            TargetPosition = targetPosition;
            TargetCorridorId = corridorId;
            Priority = priority;
            IssueTimestamp = Time.time;
        }

        // Convenience factories
        public static SquadOrder Pursue(Vector3 position, float priority = 1.0f) =>
            new SquadOrder(SquadTaskType.Pursue, position, "", priority);

        public static SquadOrder Flank(Vector3 position, float priority = 0.80f) =>
            new SquadOrder(SquadTaskType.Flank, position, "", priority);

        public static SquadOrder CoverRoute(Vector3 position, string nodeId, float priority = 0.70f) =>
            new SquadOrder(SquadTaskType.CoverRoute, position, nodeId, priority);

        public static SquadOrder Investigate(Vector3 position, float priority = 0.50f) =>
            new SquadOrder(SquadTaskType.Investigate, position, "", priority);

        public static SquadOrder HoldPosition(Vector3 position, float priority = 0.40f) =>
            new SquadOrder(SquadTaskType.HoldPosition, position, "", priority);

        public static SquadOrder Patrol(Vector3 firstWaypoint, float priority = 0.30f) =>
            new SquadOrder(SquadTaskType.Patrol, firstWaypoint, "", priority);

        public static SquadOrder Retreat(Vector3 fallbackPosition, float priority = 0.90f) =>
            new SquadOrder(SquadTaskType.Retreat, fallbackPosition, "", priority);

        public static SquadOrder Idle() =>
            new SquadOrder(SquadTaskType.Idle, Vector3.zero, "", 0f);

        public override string ToString() =>
            $"[{TaskType}] at {TargetPosition} (Corridor: '{TargetCorridorId}', Pri: {Priority:F2})";
    }
}
