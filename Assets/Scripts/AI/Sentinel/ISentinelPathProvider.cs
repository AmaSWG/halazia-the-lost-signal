using UnityEngine;

namespace Halazia.AI.Sentinel
{
    /// <summary>Plans world-space paths without moving the agent.</summary>
    public interface ISentinelPathProvider
    {
        /// <summary>Returns a complete walkable path to destination, or false if unreachable.</summary>
        bool TryGetPath(Vector3 start, Vector3 destination, out Vector3[] waypoints);
    }
}
