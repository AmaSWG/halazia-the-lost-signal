using UnityEngine;
using UnityEngine.AI;

namespace Halazia.AI.Sentinel
{
    /// <summary>Existing NavMesh path planner (legacy class name retained). Requires a baked NavMesh.
    /// Does not attach a NavMeshAgent or move the transform.</summary>
    [DisallowMultipleComponent]
    public sealed class SentinelNavMeshPrototype : SentinelPathProvider
    {
        [Tooltip("Destination tolerance: orders farther than this from the NavMesh are unreachable.")]
        [SerializeField, Min(0.01f)] private float _sampleDistance = 0.1f;
        [Tooltip("Tolerance for the agent's own position. The CharacterController can rest up to about one " +
                 "agent radius off the eroded NavMesh edge (corners, pushes); a tight value strands it permanently.")]
        [SerializeField, Min(0.01f)] private float _startSampleDistance = 0.75f;
        [SerializeField] private int _areaMask = NavMesh.AllAreas;
        private NavMeshPath _path;

        // NavMeshPath owns native Unity state; construct it on the main thread.
        private void Awake() => _path = new NavMeshPath();

        public override bool TryGetPath(Vector3 start, Vector3 destination, out Vector3[] waypoints)
        {
            waypoints = null;
            if (_path == null) _path = new NavMeshPath();
            if (!NavMesh.SamplePosition(start, out var from, Mathf.Max(_startSampleDistance, _sampleDistance), _areaMask) ||
                !NavMesh.SamplePosition(destination, out var to, _sampleDistance, _areaMask) ||
                !NavMesh.CalculatePath(from.position, to.position, _areaMask, _path) ||
                _path.status != NavMeshPathStatus.PathComplete)
                return false;
            waypoints = _path.corners;
            return waypoints.Length > 0;
        }
    }
}
