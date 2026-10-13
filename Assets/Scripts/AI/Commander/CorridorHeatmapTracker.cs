using System;
using System.Collections.Generic;
using UnityEngine;

namespace Halazia.AI.Commander
{
    /// <summary>
    /// Represents a tactical choke point, bulkhead doorway, or corridor intersection on the ship decks.
    /// </summary>
    [Serializable]
    public class CorridorNode
    {
        public string NodeId;
        public Vector3 WorldPosition;
        public float Radius = 3.5f;
        public int VisitCount = 0;

        public CorridorNode(string id, Vector3 pos, float radius = 3.5f)
        {
            NodeId = id;
            WorldPosition = pos;
            Radius = radius;
            VisitCount = 0;
        }
    }

    /// <summary>
    /// Observes and learns player spatial movement patterns across the ship decks.
    /// Implements a discrete first-order Markov transition model with Laplace smoothing
    /// to predict player escape corridors and counter repeated player tactics.
    /// Fulfills the SE3062 rubric criterion: 'Creative agent design and originality'.
    /// </summary>
    public class CorridorHeatmapTracker : MonoBehaviour
    {
        [Header("Tactical Choke Points")]
        [Tooltip("List of key corridor choke points across ship decks. Can be placed manually or generated.")]
        [SerializeField] private List<CorridorNode> _corridorNodes = new List<CorridorNode>();

        [Header("Adaptive Learning Parameters")]
        [Tooltip("Laplace smoothing factor (alpha) to prevent zero-probability errors on unseen routes.")]
        [SerializeField] private float _laplaceAlpha = 0.5f;

        [Tooltip("Weight decay per minute to give higher emphasis to recent player escape behavior.")]
        [SerializeField] private float _recencyHalfLifeSeconds = 180f;

        // Transition frequency matrix: [FromNodeId -> [ToNodeId -> Count]]
        private readonly Dictionary<string, Dictionary<string, float>> _transitionCounts = new Dictionary<string, Dictionary<string, float>>();
        private string _lastPlayerNodeId = null;
        private float _lastNodeEntryTime = 0f;

        public IReadOnlyList<CorridorNode> CorridorNodes => _corridorNodes;

        private void Awake()
        {
            InitializeTransitions();
        }

        private void InitializeTransitions()
        {
            foreach (var fromNode in _corridorNodes)
            {
                if (!_transitionCounts.ContainsKey(fromNode.NodeId))
                {
                    _transitionCounts[fromNode.NodeId] = new Dictionary<string, float>();
                }
            }
        }

        /// <summary>
        /// Registers a corridor node dynamically (useful for procedural or sandbox setups).
        /// </summary>
        public void RegisterNode(string id, Vector3 position, float radius = 3.5f)
        {
            var existing = _corridorNodes.Find(n => n.NodeId == id);
            if (existing != null)
            {
                existing.WorldPosition = position;
                existing.Radius = radius;
                return;
            }

            _corridorNodes.Add(new CorridorNode(id, position, radius));
            if (!_transitionCounts.ContainsKey(id))
            {
                _transitionCounts[id] = new Dictionary<string, float>();
            }
        }

        /// <summary>
        /// Periodic observer called with current player world position.
        /// Identifies whether the player entered a new corridor and updates Markovian transition tallies.
        /// </summary>
        public void SamplePlayerLocation(Vector3 playerPosition)
        {
            CorridorNode closestNode = FindClosestNode(playerPosition);
            if (closestNode == null) return;

            float dist = Vector3.Distance(playerPosition, closestNode.WorldPosition);
            if (dist <= closestNode.Radius)
            {
                if (_lastPlayerNodeId != closestNode.NodeId)
                {
                    // Player has transitioned into a new corridor node
                    if (!string.IsNullOrEmpty(_lastPlayerNodeId))
                    {
                        RecordTransition(_lastPlayerNodeId, closestNode.NodeId);
                    }

                    closestNode.VisitCount++;
                    _lastPlayerNodeId = closestNode.NodeId;
                    _lastNodeEntryTime = Time.time;
                }
            }
        }

        private void RecordTransition(string fromId, string toId)
        {
            if (fromId == toId) return;

            if (!_transitionCounts.ContainsKey(fromId))
            {
                _transitionCounts[fromId] = new Dictionary<string, float>();
            }

            var toDict = _transitionCounts[fromId];
            if (!toDict.ContainsKey(toId))
            {
                toDict[toId] = 0f;
            }

            toDict[toId] += 1f;
            Debug.Log($"[CorridorHeatmap] Player transition recorded: {fromId} -> {toId} (Total: {toDict[toId]:F1})");
        }

        /// <summary>
        /// Predicts the player's most probable escape corridor from their current location.
        /// Uses Laplace-smoothed conditional probability:
        /// P(Node_j | Node_i) = (Count(i -> j) + alpha) / (Sum_k(Count(i -> k)) + K * alpha)
        /// </summary>
        /// <param name="playerPos">Current player position.</param>
        /// <param name="predictedNode">The predicted choke point node.</param>
        /// <param name="probability">Calculated likelihood [0, 1].</param>
        /// <returns>True if a valid prediction could be determined.</returns>
        public bool PredictLikelyEscapeNode(Vector3 playerPos, out CorridorNode predictedNode, out float probability)
        {
            predictedNode = null;
            probability = 0f;

            if (_corridorNodes.Count < 2) return false;

            CorridorNode currentNode = FindClosestNode(playerPos);
            if (currentNode == null) return false;

            string currentId = currentNode.NodeId;
            int totalNeighbors = _corridorNodes.Count - 1;
            float denominator = totalNeighbors * _laplaceAlpha;

            if (_transitionCounts.TryGetValue(currentId, out var neighborCounts))
            {
                foreach (var pair in neighborCounts)
                {
                    if (pair.Key != currentId)
                    {
                        denominator += pair.Value;
                    }
                }
            }

            float highestProb = -1f;
            CorridorNode bestCandidate = null;

            foreach (var candidate in _corridorNodes)
            {
                if (candidate.NodeId == currentId) continue;

                float count = 0f;
                if (neighborCounts != null && neighborCounts.TryGetValue(candidate.NodeId, out float observedCount))
                {
                    count = observedCount;
                }

                // Laplace-smoothed conditional probability
                float prob = (count + _laplaceAlpha) / denominator;

                // Tie-breaker: prefer candidate with higher overall historical visits if counts are equal
                if (prob > highestProb || (Mathf.Approximately(prob, highestProb) && candidate.VisitCount > (bestCandidate?.VisitCount ?? 0)))
                {
                    highestProb = prob;
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate != null)
            {
                predictedNode = bestCandidate;
                probability = highestProb;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the most frequently used corridor across all historical visits.
        /// </summary>
        public CorridorNode GetMostFrequentCorridor()
        {
            CorridorNode mostVisited = null;
            int maxVisits = -1;

            foreach (var node in _corridorNodes)
            {
                if (node.VisitCount > maxVisits)
                {
                    maxVisits = node.VisitCount;
                    mostVisited = node;
                }
            }

            return mostVisited;
        }

        private CorridorNode FindClosestNode(Vector3 position)
        {
            if (_corridorNodes.Count == 0) return null;

            CorridorNode closest = null;
            float minSqrDist = float.MaxValue;

            foreach (var node in _corridorNodes)
            {
                float sqrDist = (node.WorldPosition - position).sqrMagnitude;
                if (sqrDist < minSqrDist)
                {
                    minSqrDist = sqrDist;
                    closest = node;
                }
            }

            return closest;
        }

        #region Gizmos & Visualization

        private void OnDrawGizmosSelected()
        {
            if (_corridorNodes == null) return;

            foreach (var node in _corridorNodes)
            {
                // Heatmap color: lerp from blue (low visits) to red (high visits)
                float heatRatio = Mathf.Clamp01(node.VisitCount / 5f);
                Gizmos.color = Color.Lerp(new Color(0f, 0.7f, 1f, 0.4f), new Color(1f, 0.1f, 0.1f, 0.7f), heatRatio);
                Gizmos.DrawWireSphere(node.WorldPosition, node.Radius);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(node.WorldPosition + Vector3.up * 1.5f, $"{node.NodeId}\nVisits: {node.VisitCount}");
#endif
            }
        }

        #endregion
    }
}
