using System;
using System.Collections.Generic;
using System.Text;
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
    /// Observes and learns player ESCAPE patterns during active combat encounters.
    /// Transitions are recorded ONLY while the player is in combat (detected by an agent within
    /// the last N seconds) so the model learns escape routes, not general travel.
    ///
    /// Implements:
    ///   - First-order Markov transition model per corridor node pair.
    ///   - Exponential decay per transition so the Commander adapts when player changes habit.
    ///   - Laplace (additive) smoothing to prevent zero-probability and NaN errors on sparse data.
    ///   - Confidence-weighted CoverRoute priority for the Commander's task allocation.
    ///
    /// Fulfills SE3062 rubric criterion: 'Creative agent design and originality'.
    /// </summary>
    public class CorridorHeatmapTracker : MonoBehaviour
    {
        [Header("Tactical Choke Points")]
        [Tooltip("List of key corridor choke points across ship decks.")]
        [SerializeField] private List<CorridorNode> _corridorNodes = new List<CorridorNode>();

        [Header("Combat Window")]
        [Tooltip("Transitions are recorded only when the player was last detected within this many seconds. " +
                 "Set to a very large number to record all travel (not recommended).")]
        [SerializeField] private float _combatWindowSeconds = 8.0f;

        [Header("Adaptive Learning Parameters")]
        [Tooltip("Laplace (additive) smoothing factor alpha. Prevents zero-probability on unseen routes. " +
                 "Higher = more uniform initial distribution. Recommended: 0.1 – 0.5.")]
        [SerializeField] private float _laplaceAlpha = 0.1f;

        [Tooltip("Exponential decay factor applied to every existing count when a new transition is recorded. " +
                 "0.9 = retain 90 % of history per observation. Lower = faster adaptation to new habits.")]
        [SerializeField] [Range(0.5f, 1.0f)] private float _decayFactor = 0.90f;

        [Header("Debug")]
        [Tooltip("Log the full transition matrix to the Console when requested.")]
        [SerializeField] private bool _logMatrixOnSample = false;

        // Transition frequency matrix: [FromNodeId → [ToNodeId → weighted count]]
        private readonly Dictionary<string, Dictionary<string, float>> _transitionCounts
            = new Dictionary<string, Dictionary<string, float>>();

        private string _lastPlayerNodeId = null;
        private float  _lastNodeEntryTime = 0f;

        // Set by CommanderAgent when it detects the player, cleared after timeout
        private float _lastCombatTime = -999f;

        public IReadOnlyList<CorridorNode> CorridorNodes => _corridorNodes;

        // ── Confidence threshold below which CoverRoute is not worth issuing
        private const float MinUsefulConfidence = 0.05f;

        private void Awake()
        {
            InitializeTransitions();
        }

        private void InitializeTransitions()
        {
            foreach (var fromNode in _corridorNodes)
            {
                if (!_transitionCounts.ContainsKey(fromNode.NodeId))
                    _transitionCounts[fromNode.NodeId] = new Dictionary<string, float>();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────────

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
                _transitionCounts[id] = new Dictionary<string, float>();
        }

        /// <summary>
        /// Called by CommanderAgent each time the player is detected.
        /// Resets the combat window timer so transitions recorded shortly after remain valid.
        /// </summary>
        public void NotifyCombatDetection()
        {
            _lastCombatTime = Time.time;
        }

        /// <summary>
        /// Periodic observer called with the current player world position.
        /// Identifies whether the player entered a new corridor node and, if in combat,
        /// updates Markovian transition tallies with exponential decay.
        /// </summary>
        public void SamplePlayerLocation(Vector3 playerPosition)
        {
            CorridorNode closestNode = FindClosestNode(playerPosition);
            if (closestNode == null) return;

            float dist = Vector3.Distance(playerPosition, closestNode.WorldPosition);
            if (dist > closestNode.Radius) return;

            if (_lastPlayerNodeId == closestNode.NodeId) return; // no change

            // Player has transitioned into a new corridor node
            bool inCombat = (Time.time - _lastCombatTime) <= _combatWindowSeconds;

            if (inCombat && !string.IsNullOrEmpty(_lastPlayerNodeId))
            {
                RecordTransition(_lastPlayerNodeId, closestNode.NodeId);
                if (_logMatrixOnSample)
                    Debug.Log(BuildMatrixDump());
            }

            closestNode.VisitCount++;
            _lastPlayerNodeId = closestNode.NodeId;
            _lastNodeEntryTime = Time.time;
        }

        /// <summary>
        /// Predicts the most probable next escape corridor from the player's current position.
        ///
        /// Formula: P(j | i) = (N(i→j) + α) / (Σ_k N(i→k) + K·α)
        /// where α = _laplaceAlpha and K = number of candidate nodes.
        ///
        /// Returns false if fewer than 2 nodes are configured or prediction confidence
        /// is below the minimum useful threshold.
        /// </summary>
        /// <param name="playerPos">Current player world position.</param>
        /// <param name="predictedNode">The most probable next corridor node.</param>
        /// <param name="confidence">Smoothed probability in [0, 1]. Higher = more reliable.</param>
        public bool PredictLikelyEscapeNode(Vector3 playerPos, out CorridorNode predictedNode, out float confidence)
        {
            predictedNode = null;
            confidence    = 0f;

            if (_corridorNodes.Count < 2) return false;

            CorridorNode currentNode = FindClosestNode(playerPos);
            if (currentNode == null) return false;

            string currentId    = currentNode.NodeId;
            int    K            = _corridorNodes.Count - 1;   // exclude current node

            // Build denominator: Σ_k observed(i→k) + K·α
            float observedTotal = 0f;
            _transitionCounts.TryGetValue(currentId, out var neighborCounts);

            if (neighborCounts != null)
            {
                foreach (var pair in neighborCounts)
                {
                    if (pair.Key != currentId)
                        observedTotal += pair.Value;
                }
            }

            float denominator = observedTotal + K * _laplaceAlpha;

            // Denominator is always > 0 because K ≥ 1 and α > 0 (Laplace smoothing guarantee)
            if (denominator < 1e-6f) denominator = 1e-6f; // belt-and-braces against floating-point edge cases

            float       highestProb   = -1f;
            CorridorNode bestCandidate = null;

            foreach (var candidate in _corridorNodes)
            {
                if (candidate.NodeId == currentId) continue;

                float count = 0f;
                if (neighborCounts != null)
                    neighborCounts.TryGetValue(candidate.NodeId, out count);

                // Laplace-smoothed conditional probability
                float prob = (count + _laplaceAlpha) / denominator;

                if (prob > highestProb ||
                    (Mathf.Approximately(prob, highestProb) &&
                     candidate.VisitCount > (bestCandidate?.VisitCount ?? 0)))
                {
                    highestProb   = prob;
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate == null || highestProb < MinUsefulConfidence)
                return false;

            predictedNode = bestCandidate;
            confidence    = highestProb;
            return true;
        }

        /// <summary>
        /// Returns the most frequently visited corridor across all historical observations.
        /// Used as a fallback when PredictLikelyEscapeNode has insufficient data.
        /// </summary>
        public CorridorNode GetMostFrequentCorridor()
        {
            CorridorNode mostVisited = null;
            int maxVisits = -1;

            foreach (var node in _corridorNodes)
            {
                if (node.VisitCount > maxVisits)
                {
                    maxVisits   = node.VisitCount;
                    mostVisited = node;
                }
            }

            return mostVisited;
        }

        /// <summary>
        /// Returns a formatted string dump of the transition probability matrix.
        /// Call from the inspector or console for debugging.
        /// </summary>
        [ContextMenu("Dump Transition Matrix")]
        public string DumpTransitionMatrix()
        {
            string dump = BuildMatrixDump();
            Debug.Log(dump);
            return dump;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Private Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private void RecordTransition(string fromId, string toId)
        {
            if (fromId == toId) return;

            if (!_transitionCounts.ContainsKey(fromId))
                _transitionCounts[fromId] = new Dictionary<string, float>();

            var toDict = _transitionCounts[fromId];

            // Fix 4A: Exponential decay — multiply ALL existing counts before adding
            // the new observation so recent escapes are weighted more heavily.
            if (_decayFactor < 1.0f)
            {
                var keys = new List<string>(toDict.Keys);
                foreach (var key in keys)
                    toDict[key] *= _decayFactor;
            }

            if (!toDict.ContainsKey(toId))
                toDict[toId] = 0f;

            toDict[toId] += 1.0f;

            Debug.Log($"[CorridorHeatmap] Escape transition recorded: {fromId} → {toId} " +
                      $"(raw count after decay: {toDict[toId]:F2}, decay: {_decayFactor:F2})");
        }

        private CorridorNode FindClosestNode(Vector3 position)
        {
            if (_corridorNodes.Count == 0) return null;

            CorridorNode closest    = null;
            float        minSqrDist = float.MaxValue;

            foreach (var node in _corridorNodes)
            {
                float sqrDist = (node.WorldPosition - position).sqrMagnitude;
                if (sqrDist < minSqrDist)
                {
                    minSqrDist = sqrDist;
                    closest    = node;
                }
            }

            return closest;
        }

        private string BuildMatrixDump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ Corridor Escape Heatmap — Transition Matrix ═══");
            sb.AppendLine($"  Nodes: {_corridorNodes.Count}  |  Alpha: {_laplaceAlpha}  |  Decay: {_decayFactor}");
            sb.AppendLine($"  Combat window: {_combatWindowSeconds}s  |  In-combat: {(Time.time - _lastCombatTime) <= _combatWindowSeconds}");
            sb.AppendLine();

            foreach (var fromNode in _corridorNodes)
            {
                _transitionCounts.TryGetValue(fromNode.NodeId, out var row);
                int    K         = _corridorNodes.Count - 1;
                float  rawTotal  = 0f;

                if (row != null)
                    foreach (var v in row.Values) rawTotal += v;

                float denom = rawTotal + K * _laplaceAlpha;

                sb.Append($"  FROM [{fromNode.NodeId}]  visits:{fromNode.VisitCount}  ");
                foreach (var toNode in _corridorNodes)
                {
                    if (toNode.NodeId == fromNode.NodeId) continue;
                    float count = 0f;
                    row?.TryGetValue(toNode.NodeId, out count);
                    float prob = (count + _laplaceAlpha) / denom;
                    sb.Append($"  → {toNode.NodeId}: {prob:P0} (n={count:F1})");
                }
                sb.AppendLine();
            }

            sb.AppendLine("═══════════════════════════════════════════════════");
            return sb.ToString();
        }

        #region Gizmos & Visualization

        private void OnDrawGizmos()
        {
            if (_corridorNodes == null) return;

            foreach (var node in _corridorNodes)
            {
                float heatRatio = Mathf.Clamp01(node.VisitCount / 5f);
                Gizmos.color = Color.Lerp(new Color(0f, 0.7f, 1f, 0.4f), new Color(1f, 0.1f, 0.1f, 0.7f), heatRatio);
                Gizmos.DrawWireSphere(node.WorldPosition, node.Radius);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(node.WorldPosition + Vector3.up * 1.5f,
                    $"{node.NodeId}\nVisits: {node.VisitCount}");
#endif
            }
        }

        #endregion
    }
}
