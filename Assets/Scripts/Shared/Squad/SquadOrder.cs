using UnityEngine;

namespace Halazia.Shared.Squad
{
    /// <summary>
    /// Immutable data packet issued by the Commander to a squad agent via ISquadAgent.AssignOrder().
    /// Carries the task type, spatial destination, and optional corridor node reference needed
    /// for the agent to execute its assignment without querying the Commander again.
    /// </summary>
    public struct SquadOrder
    {
        /// <summary>
        /// The tactical task this order instructs the agent to perform.
        /// </summary>
        public SquadTaskType Task;

        /// <summary>
        /// World-space destination the agent should navigate toward.
        /// Semantics vary by task:
        ///   Pursue       → last known player position
        ///   Flank        → computed lateral intercept position
        ///   CoverRoute   → entrance of the predicted escape corridor
        ///   Investigate  → suspicious sound or last-seen location
        ///   HoldPosition → position to stand guard at
        ///   Patrol       → first waypoint of the patrol loop (agent manages the rest internally)
        ///   Idle         → ignored
        /// </summary>
        public Vector3 TargetPosition;

        /// <summary>
        /// Corridor node identifier used for CoverRoute assignments.
        /// Matches the NodeId on a CorridorNode MonoBehaviour placed in the scene.
        /// Empty string for all other task types.
        /// </summary>
        public string TargetNodeId;

        /// <summary>
        /// Normalized priority of this order in [0.0, 1.0].
        /// Derived from the Commander's utility score for this agent-task pairing.
        /// Agents may use this to resolve conflicts if they receive overlapping orders.
        /// </summary>
        public float Priority;

        /// <summary>
        /// Convenience factory for the most common order types.
        /// </summary>
        public static SquadOrder Pursue(Vector3 position, float priority = 1.0f) =>
            new SquadOrder { Task = SquadTaskType.Pursue, TargetPosition = position, TargetNodeId = "", Priority = priority };

        public static SquadOrder Flank(Vector3 position, float priority = 0.80f) =>
            new SquadOrder { Task = SquadTaskType.Flank, TargetPosition = position, TargetNodeId = "", Priority = priority };

        public static SquadOrder CoverRoute(Vector3 position, string nodeId, float priority = 0.70f) =>
            new SquadOrder { Task = SquadTaskType.CoverRoute, TargetPosition = position, TargetNodeId = nodeId, Priority = priority };

        public static SquadOrder Investigate(Vector3 position, float priority = 0.50f) =>
            new SquadOrder { Task = SquadTaskType.Investigate, TargetPosition = position, TargetNodeId = "", Priority = priority };

        public static SquadOrder HoldPosition(Vector3 position, float priority = 0.40f) =>
            new SquadOrder { Task = SquadTaskType.HoldPosition, TargetPosition = position, TargetNodeId = "", Priority = priority };

        public static SquadOrder Patrol(Vector3 firstWaypoint, float priority = 0.30f) =>
            new SquadOrder { Task = SquadTaskType.Patrol, TargetPosition = firstWaypoint, TargetNodeId = "", Priority = priority };

        public static SquadOrder Idle() =>
            new SquadOrder { Task = SquadTaskType.Idle, TargetPosition = Vector3.zero, TargetNodeId = "", Priority = 0f };

        public override string ToString() =>
            $"SquadOrder[{Task} → {TargetPosition} | node='{TargetNodeId}' | priority={Priority:F2}]";
    }
}
