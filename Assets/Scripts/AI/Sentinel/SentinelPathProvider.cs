using UnityEngine;

namespace Halazia.AI.Sentinel
{
    /// <summary>Path planning only; AgentMotor remains the sole movement controller.
    /// Implement this adapter for the project's Weighted A* graph when available.</summary>
    public abstract class SentinelPathProvider : MonoBehaviour, ISentinelPathProvider
    {
        // Return a complete, walkable path ending at the requested destination, or false.
        public abstract bool TryGetPath(Vector3 start, Vector3 destination, out Vector3[] waypoints);
    }
}
