using System.Collections.Generic;
using UnityEngine;

namespace Halazia.AI.Commander
{
    /// <summary>
    /// Scene marker placed at corridor junctions, choke points, doorways, and stairwells across ship decks.
    /// Level designers place these in the scene to define escape paths and tactical choke points.
    /// CorridorHeatmapTracker auto-discovers all CorridorNodeMarkers in the scene at Awake().
    /// </summary>
    [DisallowMultipleComponent]
    [SelectionBase]
    public class CorridorNodeMarker : MonoBehaviour
    {
        [Tooltip("Unique identifier for this corridor junction (e.g., 'Deck1_CargoStairs', 'GunDeck_Door'). Auto-populated from GameObject name if blank.")]
        public string NodeId;

        [Tooltip("Player detection radius (meters) around this junction.")]
        [Range(1.0f, 10.0f)]
        public float Radius = 3.5f;

        [Tooltip("Directly reachable adjacent corridor junctions for route graph topology and visualization.")]
        public List<CorridorNodeMarker> ConnectedNodes = new List<CorridorNodeMarker>();

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(NodeId))
                NodeId = gameObject.name;
        }

        #region Editor Gizmos

        private void OnDrawGizmos()
        {
            // Center marker
            Gizmos.color = new Color(0f, 0.85f, 1f, 0.9f);
            Gizmos.DrawSphere(transform.position, 0.35f);

            // Detection volume
            Gizmos.color = new Color(0f, 0.85f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, Radius);

#if UNITY_EDITOR
            // Label above node
            UnityEditor.Handles.color = Color.white;
            string displayId = string.IsNullOrWhiteSpace(NodeId) ? gameObject.name : NodeId;
            UnityEditor.Handles.Label(transform.position + Vector3.up * (Radius * 0.5f + 0.5f), $"[Corridor: {displayId}]");
#endif

            // Connection lines to adjacent junctions
            if (ConnectedNodes != null)
            {
                Gizmos.color = new Color(0f, 0.85f, 1f, 0.6f);
                foreach (var neighbor in ConnectedNodes)
                {
                    if (neighbor != null)
                    {
                        Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, neighbor.transform.position + Vector3.up * 0.2f);
                    }
                }
            }
        }

        #endregion
    }
}
