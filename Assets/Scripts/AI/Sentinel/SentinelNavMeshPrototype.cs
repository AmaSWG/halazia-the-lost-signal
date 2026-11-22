using UnityEngine;
using UnityEngine.AI;

namespace Halazia.AI.Sentinel
{
    /// <summary>Existing NavMesh path planner (legacy class name retained). Requires a baked NavMesh.
    /// Does not attach a NavMeshAgent or move the transform.</summary>
    [DisallowMultipleComponent]
    public sealed class SentinelNavMeshPrototype : SentinelPathProvider
    {
        [SerializeField, Min(0.01f)] private float _sampleDistance = 0.1f;
        [SerializeField] private int _areaMask = NavMesh.AllAreas;
        private NavMeshPath _path;

        // NavMeshPath owns native Unity state; construct it on the main thread.
        private void Awake() => _path = new NavMeshPath();

        public override bool TryGetPath(Vector3 start, Vector3 destination, out Vector3[] waypoints)
        {
            waypoints = null;
            if (_path == null) _path = new NavMeshPath();
            if (!NavMesh.SamplePosition(start, out var from, _sampleDistance, _areaMask) ||
                !NavMesh.SamplePosition(destination, out var to, _sampleDistance, _areaMask) ||
                !NavMesh.CalculatePath(from.position, to.position, _areaMask, _path) ||
                _path.status != NavMeshPathStatus.PathComplete)
                return false;
            waypoints = _path.corners;
            return waypoints.Length > 0;
        }
    }
}
